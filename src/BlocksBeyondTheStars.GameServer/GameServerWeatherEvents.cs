// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What the weather actually DOES to the world (#900). Before this, weather cost a few °C and put fires
/// out; now it can hurt you, help you and leave a mark:
/// <list type="bullet">
/// <item>Corrosive and falling weather (acid rain, ember fall, meteor shower) drains the suit and then
/// health — but only out in the open, so a roof is a real answer.</item>
/// <item>An ion storm is the inversion: dangerous to your instruments, but it CHARGES an exposed suit,
/// so bad weather becomes something you might go out into on purpose.</item>
/// <item>Rain waters the ground (planted flora regrows faster), a spore bloom fattens the harvest,
/// blowing dust and ionised air shorten scanner range, violent weather makes creatures hunker down.</item>
/// <item>Snow settles as real blocks on a hard budget, tracked in its own deposit table so the melt pass
/// can never remove snow a player placed themselves.</item>
/// </list>
/// Gated by the same "Environmental hazards" world option as the temperature hazard
/// (<see cref="Shared.Configuration.GameRules.TemperatureHazardsEnabled"/>) — no new rule, so a
/// save-baked <c>RulesOverride</c> cannot silently drop it.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Suit drain per second at full intensity, before the hazard tier scales it. Deliberately
    /// gentler than the temperature hazard: weather passes, and you can walk out of it.</summary>
    private const float WeatherSuitDrainPerSecond = 1.2f;

    /// <summary>Health per second once the suit is empty and you stay out in corrosive weather.</summary>
    private const float WeatherDamagePerSecond = 1.4f;

    /// <summary>An exposed suit gains this much per second in a full-strength ion storm — the reason to
    /// walk INTO the storm instead of away from it.</summary>
    private const float IonChargePerSecond = 2.6f;

    /// <summary>Seconds between snow accumulation/melt passes.</summary>
    private const double DepositInterval = 2.0;

    /// <summary>Cells the snow pass may place or melt in one go — the whole budget of the feature.</summary>
    private const int DepositsPerPass = 8;

    /// <summary>Hard ceiling on weather-placed cells per world, so a long blizzard cannot grow the save
    /// without bound. Oldest deposits melt first once it's reached.</summary>
    private const int MaxDepositsPerWorld = 2000;

    /// <summary>How far from a player snow may settle. Beyond it nobody would see it land anyway.</summary>
    private const int DepositRadius = 22;

    /// <summary>Warm seconds a deposited cell survives before it melts away.</summary>
    private const double DepositMeltSeconds = 45.0;

    private ushort _weatherSnowId;
    private bool _weatherIdsResolved;

    /// <summary>Per-tick weather gameplay: player effects first (cheap, per player), then the throttled
    /// snow pass. Called from <c>TickWeather</c>, so it already runs per resident world.</summary>
    private void TickWeatherEffects(double dt)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            ApplyWeatherToPlayer(session, dt);
        }

        _worlds.Active.SinceWeatherDeposit += dt;
        if (_worlds.Active.SinceWeatherDeposit >= DepositInterval)
        {
            _worlds.Active.SinceWeatherDeposit = 0;
            TickWeatherDeposits();
        }
    }

    /// <summary>Corrosive/falling weather hurts, an ion storm charges — both only under open sky, and
    /// never to someone sheltered, aboard a ship or on a station.</summary>
    private void ApplyWeatherToPlayer(PlayerSession session, double dt)
    {
        var p = session.State;
        if (p.InEva || p.AboardShip || p.GodMode || p.Health <= 0f || InStation(p.PlayerId))
        {
            return;
        }

        var (state, intensity) = BiomeWeatherAt(p.Position);
        if (intensity <= 0.01f)
        {
            return;
        }

        bool sheltered = RoofedAt(p.Position);
        if (state == "ion_storm")
        {
            // The one weather you WANT to be caught in: an exposed suit soaks up the charge.
            if (!sheltered && p.SuitEnergy < 100f)
            {
                p.SuitEnergy = Math.Min(100f, p.SuitEnergy + (float)(dt * IonChargePerSecond * intensity));
            }

            return;
        }

        if (!Rules.TemperatureHazardsEnabledFor(p.ModeOverride) || sheltered)
        {
            return;
        }

        // Acid eats through a suit, embers scorch it, meteorite grit shreds it. All three are survivable
        // and readable: the suit buffer goes first, health only once it's gone.
        float bite = state switch
        {
            "acid_rain" => 1.0f,
            "ember_fall" => 0.75f,
            "meteor_shower" => 0.6f,
            _ => 0f,
        };

        if (bite <= 0f)
        {
            return;
        }

        float scale = bite * intensity * Rules.HazardSeverityFactor;
        if (p.SuitEnergy > 0f)
        {
            p.SuitEnergy = Math.Max(0f, p.SuitEnergy - (float)(dt * WeatherSuitDrainPerSecond * scale));
        }
        else
        {
            p.Health = Math.Max(0f, p.Health - (float)(dt * WeatherDamagePerSecond * scale));
        }
    }

    // ---------------- Queries other systems ask ----------------

    /// <summary>How fast a harvested plant grows back here right now: rain waters the ground, a dry
    /// season and a scorching sky slow it. 1 = the plain rate (#900).</summary>
    private double WeatherRegrowFactor(Vector3i pos)
    {
        if (_planetWeatherMode != "dynamic")
        {
            return 1.0;
        }

        var at = new Vector3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        var (state, intensity) = BiomeWeatherAt(at);
        string precip = PrecipitationFor(state, CurrentTemperature(state, _dayFraction, at));
        double factor = precip switch
        {
            "rain" => 1.0 + 0.9 * intensity,     // a downpour nearly halves the wait
            "drizzle" => 1.0 + 0.5 * intensity,
            "sleet" or "snow" => 1.0,
            "acid" => 0.6,                        // corrosive rain is no help at all
            "ash" or "sandstorm" or "dust" => 0.75,
            _ => state == "heatwave" ? 0.65 : 1.0,
        };

        // The season leans on it too, so a dry spell is felt even between showers.
        return factor * (0.85 + 0.3 * _sim.Wetness(_systemTimeDays));
    }

    /// <summary>Test seam: how far a scan pulse reaches under the current sky (1 = unimpeded).</summary>
    public double WeatherScanFactorForTest() => WeatherScanFactor();

    /// <summary>Test seam: how fast harvested flora grows back at a cell right now (1 = the plain rate).</summary>
    public double WeatherRegrowFactorForTest(Vector3i pos) => WeatherRegrowFactor(pos);

    /// <summary>Scanner/beacon range multiplier for the current weather — blown grit and ionised air cut
    /// a pulse short (#900).</summary>
    private double WeatherScanFactor()
        => _weatherState switch
        {
            "ion_storm" => 0.45,
            "gale" => 0.7,
            "fog" => 0.65,
            "blizzard" => 0.6,
            "storm" => 0.8,
            _ => PrecipitationFor(_weatherState, 20f) == "sandstorm" ? 0.55 : 1.0,
        };

    /// <summary>Extra flora yield during a spore bloom — the harvest reason to head out into it.</summary>
    public int WeatherHarvestBonus()
        => _weatherState == "spore_bloom" && _weatherIntensity > 0.35f ? 1 : 0;

    /// <summary>How lively the fauna is right now: in violent weather animals hunker down (#900).</summary>
    public double WeatherCreatureActivity()
        => WeatherCatalog.Find(_weatherState)?.Family switch
        {
            WeatherFamily.Violent => 1.0 - 0.55 * _weatherIntensity,
            WeatherFamily.Exotic => 1.0 - 0.35 * _weatherIntensity,
            WeatherFamily.Obscuring => 1.0 - 0.2 * _weatherIntensity,
            _ => 1.0,
        };

    // ---------------- Snow that settles (hard budget) ----------------

    /// <summary>Lays down and melts weather snow. Everything about this pass is bounded: it only runs
    /// every <see cref="DepositInterval"/> s, only near a player, places at most
    /// <see cref="DepositsPerPass"/> cells, and never exceeds <see cref="MaxDepositsPerWorld"/> per world
    /// — a blizzard must not be able to grow the save or flood the network.</summary>
    private void TickWeatherDeposits()
    {
        if (!_weatherIdsResolved)
        {
            _weatherIdsResolved = true;
            _weatherSnowId = _content.GetBlock("snow")?.NumericId.Value ?? 0;
        }

        if (_weatherSnowId == 0 || _content.GetPlanet(_worlds.Active.PlanetType)?.Void == true)
        {
            return;
        }

        var deposits = _worlds.Active.WeatherDeposits;
        if (ShouldAccumulateSnow())
        {
            AccumulateSnow(deposits);
        }

        MeltSnow(deposits);
    }

    /// <summary>Snow only settles while snow is genuinely falling on this world.</summary>
    private bool ShouldAccumulateSnow()
    {
        if (_planetWeatherMode != "dynamic" || _weatherIntensity < 0.25f)
        {
            return false;
        }

        string precip = PrecipitationFor(_weatherState, CurrentTemperature(_weatherState, _dayFraction));
        return precip is "snow" && CurrentTemperature(_weatherState, _dayFraction) <= 1f;
    }

    private void AccumulateSnow(Dictionary<Vector3i, double> deposits)
    {
        if (deposits.Count >= MaxDepositsPerWorld)
        {
            return;
        }

        int placed = 0;
        foreach (var session in JoinedInActiveWorld())
        {
            if (placed >= DepositsPerPass)
            {
                break;
            }

            var p = session.State;
            if (p.InEva || p.AboardShip)
            {
                continue;
            }

            for (int attempt = 0; attempt < DepositsPerPass * 2 && placed < DepositsPerPass; attempt++)
            {
                int x = (int)Math.Floor(p.Position.X) + _weatherDepositRng.Next(-DepositRadius, DepositRadius + 1);
                int z = (int)Math.Floor(p.Position.Z) + _weatherDepositRng.Next(-DepositRadius, DepositRadius + 1);
                if (!TryFindSnowSurface(x, (int)Math.Round(p.Position.Y), z, out var cell))
                {
                    continue;
                }

                if (deposits.ContainsKey(cell))
                {
                    continue;
                }

                _world.SetBlock(cell, new BlockId(_weatherSnowId));
                // Weather-placed blocks are still block edits, so clients MUST be told or they'd only
                // see the snow after a chunk reload.
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = _weatherSnowId });
                deposits[cell] = DepositMeltSeconds;
                _repo.SaveWeatherDeposit(_world.LocationId, cell, _weatherSnowId, DepositMeltSeconds);
                placed++;
            }
        }
    }

    /// <summary>Finds the air cell resting on the first solid block below the player's level in this
    /// column, if it sees the sky. Reads only already-loaded chunks — the snow pass never generates.</summary>
    private bool TryFindSnowSurface(int x, int fromY, int z, out Vector3i cell)
    {
        cell = default;
        for (int y = fromY + 8; y > fromY - 12; y--)
        {
            var here = _world.GetBlockIfLoaded(new Vector3i(x, y, z));
            if (here.Value == 0)
            {
                continue;
            }

            // Don't stack snow on snow, on ice, or on anything already fluid.
            if (here.Value == _weatherSnowId)
            {
                return false;
            }

            var above = new Vector3i(x, y + 1, z);
            if (_world.GetBlockIfLoaded(above).Value != 0 || !SkyExposed(above))
            {
                return false;
            }

            cell = above;
            return true;
        }

        return false;
    }

    /// <summary>Melts weather snow once the air turns warm (or the world stopped snowing long enough),
    /// bounded to the same per-pass budget. Only cells in the deposit table are ever removed, so a
    /// player's own snow blocks are untouchable here.</summary>
    private void MeltSnow(Dictionary<Vector3i, double> deposits)
    {
        if (deposits.Count == 0)
        {
            return;
        }

        bool warm = CurrentTemperature(_weatherState, _dayFraction) > 2f;
        bool overCap = deposits.Count > MaxDepositsPerWorld;
        if (!warm && !overCap)
        {
            return; // still freezing and within budget — the snow stays
        }

        // Warmth ages every deposit; over the cap the oldest go regardless of temperature.
        var doomed = new List<Vector3i>(DepositsPerPass);
        foreach (var key in new List<Vector3i>(deposits.Keys))
        {
            if (warm)
            {
                deposits[key] -= DepositInterval;
            }

            if (doomed.Count < DepositsPerPass && (overCap || deposits[key] <= 0))
            {
                doomed.Add(key);
            }
        }

        foreach (var cell in doomed)
        {
            deposits.Remove(cell);
            _repo.DeleteWeatherDeposit(_world.LocationId, cell);
            // Only ever clear a cell that is still the snow WE put there — a player may have mined it,
            // built over it, or placed their own snow next to it.
            if (_world.GetBlockIfLoaded(cell).Value == _weatherSnowId)
            {
                _world.SetBlock(cell, new BlockId(0));
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = 0 });
            }
        }
    }

    // ---------------- Forecast (the weather scanner gadget) ----------------

    /// <summary>Reads the sky for a player: what it's doing here now, the next few episodes with their ETA,
    /// and how far off the nearest front is. The peek forks the model's RNG stream, so asking never nudges
    /// the world's actual weather (#900).</summary>
    private void SendWeatherForecast(PlayerSession session) => Send(session, BuildWeatherForecast(session));

    private WeatherForecast BuildWeatherForecast(PlayerSession session)
    {
        var (state, _) = BiomeWeatherAt(session.State.Position);
        var forecast = new WeatherForecast
        {
            Current = state,
            CurrentEndsInSeconds = (float)Math.Max(0, _sim.Duration - _sim.Elapsed),
            FrontDistance = (float)_sim.NearestFront(session.State.Position.X, _world.Circumference).Distance,
            SeasonWetness = (float)_sim.Wetness(_systemTimeDays),
        };

        foreach (var (next, startsIn, duration) in _sim.Forecast(WeatherForecastEpisodes, WeatherCtx()))
        {
            forecast.Upcoming.Add(new WeatherForecastEntry
            {
                State = next,
                StartsInSeconds = (float)startsIn,
                DurationSeconds = (float)duration,
            });
        }

        return forecast;
    }

    /// <summary>Test seam: the forecast the weather scanner would send this player right now.</summary>
    public WeatherForecast? WeatherForecastForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? BuildWeatherForecast(s) : null;

    /// <summary>Restores this world's persisted weather snow so a restart doesn't strand cells that can
    /// never melt (mirrors <c>LoadFloraRegrow</c>).</summary>
    private void LoadWeatherDeposits()
    {
        foreach (var d in _repo.ListWeatherDeposits(_world.LocationId))
        {
            _worlds.Active.WeatherDeposits[d.WorldPosition] = d.Timer;
        }
    }

    private readonly Random _weatherDepositRng = new(0x5D0F);
}

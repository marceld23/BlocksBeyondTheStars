// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Temperature survival hazard (#666–#668): outside the suit's comfort band, extreme heat, cold and
/// vacuum first drain <see cref="Shared.State.PlayerState.SuitEnergy"/> (climate control; carried
/// thermal insulation slows it), and once the suit is empty slowly damage health — always gentler
/// than lava/suffocation, always escapable. Gated by <see cref="Shared.Configuration.GameRules
/// .TemperatureHazardsEnabled"/> (off in Creative and at hazard tier Off — the "Environmental
/// hazards" world option is the switch). The expensive part (block probe + shelter scan) runs at
/// ~1 Hz per player and is cached on the session; the drain itself applies every tick.
/// </summary>
public sealed partial class GameServer
{
    // Comfort band + grace: severity is the °C beyond [ComfortLowC-Grace .. ComfortHighC+Grace], so
    // mild worlds (savanna 32 °C) stay free and a desert (44 °C base) only stresses the suit at midday.
    private const float ComfortLowC = -5f;
    private const float ComfortHighC = 40f;
    private const float ComfortGraceC = 5f;

    /// <summary>Severity ceiling — bounds the worst case (deep vacuum shadow, lava world noon) so the
    /// suit buffer never collapses below ~4½ minutes with no gear at Normal.</summary>
    private const float SeverityCapC = 60f;

    /// <summary>Suit energy per second per °C of severity (Normal tier, no insulation). Tuning anchor
    /// (user, 2026-08-02): an ice world (severity ≈ 28) empties a full 100 bar in ≈ 10 min naked and
    /// ≈ 30 min with the tier-2 liner (0.65 insulation).</summary>
    private const float TemperatureEnergyDrainPerDegree = 0.006f;

    /// <summary>Health per second per °C of severity once the suit is empty…</summary>
    private const float TemperatureDamagePerDegree = 0.05f;

    /// <summary>…capped at starvation level (3/s) — well below suffocation (5/s) and lava (15/s), so
    /// exposure is always the slow, readable killer (kid-friendly precedent: hunger #456).</summary>
    private const float MaxTemperatureDamagePerSecond = 3f;

    /// <summary>A roof overhead halves the severity (#667) — player-built surface shelters matter.</summary>
    private const float ShelterSeverityFactor = 0.5f;

    /// <summary>Blocks scanned upward for the shelter check (mirrors the client's open-sky scan).</summary>
    private const int ShelterScanHeight = 50;

    /// <summary>Half-extent of the local heat/cold source probe box (#667). 7³ = 343 loaded-only block
    /// reads per player per second — bounded and chunk-load-free.</summary>
    private const int SourceProbeRadius = 3;

    private const double TemperatureScanInterval = 1.0;

    private ushort _tempIceId, _tempSnowId;
    private bool _tempIdsResolved;

    /// <summary>Sun-dependent vacuum reading (#668): ≈ +120 °C in full sunlight down to ≈ −150 °C in
    /// shadow, following the world's day curve — the HUD shows it during EVA instead of "—", and the
    /// hazard treats it like any other temperature (the severity cap bounds both extremes).</summary>
    public static float VacuumTemperature(double timeOfDay)
        => (float)System.Math.Round(-15.0 + 135.0 * System.Math.Cos((timeOfDay - 0.5) * 2.0 * System.Math.PI));

    /// <summary>°C beyond the comfort band (0 = comfortable) for an effective temperature — the pure
    /// band math, exposed for tests.</summary>
    public static float TemperatureSeverityFor(float temperatureC)
    {
        float excess = System.Math.Max(ComfortLowC - temperatureC, temperatureC - ComfortHighC) - ComfortGraceC;
        return System.Math.Clamp(excess, 0f, SeverityCapC);
    }

    /// <summary>Per-tick half of the hazard: applies the cached severity as suit-energy drain, or as
    /// slow health damage once the suit is empty. Runs inside the TickEnvironment player loop (after
    /// the lava/fire burns, before hunger); GodMode and dead-choosing players never reach it.</summary>
    private void TickTemperature(PlayerSession session, double dt)
    {
        var p = session.State;
        if (!Rules.TemperatureHazardsEnabled)
        {
            p.SuitClimateActive = false;
            return;
        }

        // Life support = climate control for free: aboard the ship, inside a landed ship's cabin, or
        // boarded on a station. (Void worlds — ship interior / station decks — read 22 °C anyway.)
        bool insideShip = !p.InEva && ShipInteriorContains(p.Position);
        if (!p.InEva && (p.AboardShip || insideShip || InStation(p.PlayerId)))
        {
            p.SuitClimateActive = false;
            session.TemperatureSeverity = 0f;
            session.TemperatureScanIn = 0; // rescan immediately after stepping back out
            return;
        }

        session.TemperatureScanIn -= dt;
        if (session.TemperatureScanIn <= 0)
        {
            session.TemperatureScanIn = TemperatureScanInterval;
            session.TemperatureSeverity = ComputeTemperatureSeverity(session);
        }

        float severity = session.TemperatureSeverity;
        if (severity <= 0f)
        {
            p.SuitClimateActive = false;
            return;
        }

        p.SuitClimateActive = true;
        float scale = Rules.HazardSeverityFactor * (1f - ThermalInsulation(p));
        if (p.SuitEnergy > 0f)
        {
            p.SuitEnergy = System.Math.Max(0f, p.SuitEnergy - (float)(dt * severity * TemperatureEnergyDrainPerDegree * scale));
        }
        else
        {
            float dps = System.Math.Min(MaxTemperatureDamagePerSecond, severity * TemperatureDamagePerDegree);
            p.Health = System.Math.Max(0f, p.Health - (float)(dt * dps * scale));
        }
    }

    /// <summary>The ~1 Hz half: effective temperature at the player (vacuum / positional climate with
    /// the underground blend / local ice-lava-fire sources) → severity, with the shelter bonus.</summary>
    private float ComputeTemperatureSeverity(PlayerSession session)
    {
        var p = session.State;

        // Vacuum exposure (EVA spacewalk, or on foot above the atmosphere line): sun-dependent hull
        // temperature; no block probe (an EVA position is space-instance coordinates) and no roof.
        if (p.InEva || p.AboveAtmosphere)
        {
            session.EffectiveTemperatureC = VacuumTemperature(_dayFraction);
            return TemperatureSeverityFor(session.EffectiveTemperatureC);
        }

        var (weather, _) = BiomeWeatherAt(p.Position);
        float t = CurrentTemperature(weather, _dayFraction, p.Position);
        t = ApplyLocalSources(p.Position, t);
        session.EffectiveTemperatureC = t;

        float severity = TemperatureSeverityFor(t);
        if (severity > 0f && RoofedAt(p.Position))
        {
            severity *= ShelterSeverityFactor;
        }

        return severity;
    }

    /// <summary>Local heat/cold sources override the ambient reading (#667): lava keeps its surroundings
    /// dangerous (the deep lava table stays hot), open fire is a gentle campfire warmth capped inside
    /// the comfort band, and ice/snow-walled spaces (an ice world's caves) hold the cold. Reads only
    /// already-loaded chunks — never generates.</summary>
    private float ApplyLocalSources(Shared.Geometry.Vector3f pos, float t)
    {
        if (!_tempIdsResolved)
        {
            _tempIdsResolved = true;
            _tempIceId = _content.GetBlock("ice")?.NumericId.Value ?? 0;
            _tempSnowId = _content.GetBlock("snow")?.NumericId.Value ?? 0;
        }

        var center = new Vector3i(
            (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y + 1f), (int)System.Math.Floor(pos.Z));
        int lavaDist = int.MaxValue, fireDist = int.MaxValue, icy = 0;
        for (int dx = -SourceProbeRadius; dx <= SourceProbeRadius; dx++)
        {
            for (int dy = -SourceProbeRadius; dy <= SourceProbeRadius; dy++)
            {
                for (int dz = -SourceProbeRadius; dz <= SourceProbeRadius; dz++)
                {
                    ushort id = _world.GetBlockIfLoaded(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz)).Value;
                    if (id == 0)
                    {
                        continue;
                    }

                    int dist = System.Math.Max(System.Math.Abs(dx), System.Math.Max(System.Math.Abs(dy), System.Math.Abs(dz)));
                    if (id == _lavaId)
                    {
                        lavaDist = System.Math.Min(lavaDist, System.Math.Max(1, dist));
                    }
                    else if (_fireId != 0 && id == _fireId)
                    {
                        fireDist = System.Math.Min(fireDist, System.Math.Max(1, dist));
                    }
                    else if (id == _tempIceId || id == _tempSnowId)
                    {
                        icy++;
                    }
                }
            }
        }

        if (lavaDist != int.MaxValue)
        {
            t = System.Math.Max(t, 65f - 6f * lavaDist); // adjacent ≈ 59 °C — near-lava is never cosy
        }
        else if (fireDist != int.MaxValue)
        {
            t = System.Math.Max(t, 30f - 4f * (fireDist - 1)); // campfire warmth, capped inside the band
        }
        else if (icy >= 10)
        {
            t = System.Math.Min(t, -15f); // ice-walled space: milder than an ice surface, never comfortable
        }

        return t;
    }

    /// <summary>Server-side open-sky check (#667): a solid block within <see cref="ShelterScanHeight"/>
    /// above the head means the player is under cover. Loaded-chunk reads only — the column above a
    /// present player is resident in practice, and "unknown" correctly reads as open sky.</summary>
    private bool RoofedAt(Shared.Geometry.Vector3f pos)
    {
        int px = (int)System.Math.Floor(pos.X), pz = (int)System.Math.Floor(pos.Z);
        int start = (int)System.Math.Floor(pos.Y) + 2;
        for (int y = start; y <= start + ShelterScanHeight; y++)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(px, y, pz)).IsAir)
            {
                return true;
            }
        }

        return false;
    }
}

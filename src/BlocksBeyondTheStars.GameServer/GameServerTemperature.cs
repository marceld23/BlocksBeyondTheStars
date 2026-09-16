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

    private ushort _tempIceId, _tempSnowId, _tempCampfireId;
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
        if (!Rules.TemperatureHazardsEnabledFor(p.ModeOverride))
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
            p.Exposure = 0f; // 2026-09: the ship and the station reset the exposure meter
            session.ExposureActive = false;
            session.ExposureFullSeconds = 0;
            session.ExposureWarned = 0;
            return;
        }

        if (ExposurePlanet() is { } exposurePlanet && !p.InEva && !p.AboveAtmosphere)
        {
            TickExposure(session, exposurePlanet, dt);
            return;
        }

        session.ExposureActive = false;

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
        // A station boarder floating past the gravity box (#1485) is AboveAtmosphere too, but the station's
        // void world is not a sun-exposed orbit — hull work from outside the deck stays heat-free (#1568).
        if (p.InEva || (p.AboveAtmosphere && !InStation(p.PlayerId)))
        {
            session.EffectiveTemperatureC = VacuumTemperature(_dayFraction);
            return TemperatureSeverityFor(session.EffectiveTemperatureC);
        }

        var (weather, _) = BiomeWeatherAt(p.Position);
        float t = CurrentTemperature(weather, _dayFraction, p.Position);
        if (InSpsLab(p.Position))
        {
            t = SpsLabInsideC; // 2026-09: the abandoned SPS modules are colder than the snow outside them feels
        }

        t = ApplyLocalSources(p.Position, t);
        if (InCityShelter(p.Position))
        {
            t = CityComfortC; // #1793: under a roof inside the walls the G.D.S. city is cool, whatever the desert does
        }

        session.EffectiveTemperatureC = t;

        float severity = TemperatureSeverityFor(t);
        if (severity > 0f && RoofedAt(p.Position))
        {
            severity *= ShelterSeverityFactor;
        }

        return severity;
    }

    // --- Exposure meter (2026-09, Titas: "outside, the cold kills you after 40 minutes, the heat after 30") ---

    /// <summary>A heated place drains a full meter in this many seconds.</summary>
    private const double ExposureRecoverSeconds = 60.0;

    /// <summary>Seconds of the rising damage ramp at a full meter: 0.5 HP/s, +0.1 per second, capped like the classic drain.</summary>
    private const float ExposureDamageStart = 0.5f, ExposureDamageRise = 0.1f;

    /// <summary>The active world's type when it times cold and heat instead of draining the suit (generation 8), else null.</summary>
    private Shared.Definitions.PlanetType? ExposurePlanet()
    {
        var planet = _world.Planet;
        return planet is { Void: false } && planet.ExposureMinutesCold > 0
            && _generator.TerrainGeneration >= Shared.World.WorldDescription.ExtremePlanetsGeneration
            ? planet
            : null;
    }

    /// <summary>How much longer the carried thermal liner makes the timer last: I ×1.25, II ×1.5, III ×2.</summary>
    private float ExposureGearFactor(Shared.State.PlayerState p)
    {
        float insulation = ThermalInsulation(p);
        return insulation >= 0.84f ? 2f : insulation >= 0.64f ? 1.5f : insulation >= 0.39f ? 1.25f : 1f;
    }

    /// <summary>How much longer the environment tier makes the timer last: Light ×1.5, Hard ×0.75.</summary>
    private float ExposureDifficultyFactor()
    {
        float severity = Rules.HazardSeverityFactor;
        return severity < 1f ? 1.5f : severity > 1f ? 0.75f : 1f;
    }

    /// <summary>The seconds a bare meter takes to fill here (before the roof halving) — exposed for tests.</summary>
    public double ExposureSecondsForTest(string playerId, bool hot)
    {
        var planet = ExposurePlanet();
        var session = FindSessionByPlayerId(playerId);
        if (planet is null || session is null)
        {
            return 0;
        }

        return ExposureSeconds(session.State, planet, hot);
    }

    private double ExposureSeconds(Shared.State.PlayerState p, Shared.Definitions.PlanetType planet, bool hot)
    {
        double minutes = hot && planet.ExposureMinutesHot > 0 ? planet.ExposureMinutesHot : planet.ExposureMinutesCold;
        return minutes * 60.0 * ExposureGearFactor(p) * ExposureDifficultyFactor();
    }

    /// <summary>The meter on a timed-exposure type: fills outside (half speed under a roof), drains in warmth, hurts at full
    /// with a rising damage, and VEGA warns at 50, 75 and 90 %.</summary>
    private void TickExposure(PlayerSession session, Shared.Definitions.PlanetType planet, double dt)
    {
        var p = session.State;
        p.SuitClimateActive = false; // the suit battery is not the buffer here — the meter is
        session.ExposureActive = true;
        session.TemperatureScanIn -= dt;
        if (session.TemperatureScanIn <= 0)
        {
            session.TemperatureScanIn = TemperatureScanInterval;
            session.TemperatureSeverity = ComputeTemperatureSeverity(session); // keeps the HUD/VEGA reading current
            var cell = new Vector3i((int)System.Math.Floor(p.Position.X), (int)System.Math.Floor(p.Position.Y), (int)System.Math.Floor(p.Position.Z));
            session.ExposureHot = _generator.IsHotZoneAt(planet, cell.X, cell.Z) && session.EffectiveTemperatureC > ComfortHighC;
            session.ExposureRoofed = RoofedAt(p.Position);
            session.ExposureSheltered = InAnyBaseZone(cell) || InSealedBaseRoom(cell) || session.TemperatureSeverity <= 0f;
            if (InSpsLab(p.Position))
            {
                ShipAiHintOnce(session, "sps_lab"); // the first step into an old SPS module
            }
        }

        if (session.ExposureSheltered)
        {
            p.Exposure = System.Math.Max(0f, p.Exposure - (float)(dt / ExposureRecoverSeconds));
        }
        else
        {
            double seconds = System.Math.Max(1.0, ExposureSeconds(p, planet, session.ExposureHot));
            double rate = (session.ExposureRoofed ? 0.5 : 1.0) / seconds;
            p.Exposure = System.Math.Min(1f, p.Exposure + (float)(dt * rate));
        }

        if (p.Exposure >= 1f)
        {
            session.ExposureFullSeconds += dt;
            float dps = System.Math.Min(MaxTemperatureDamagePerSecond, ExposureDamageStart + ExposureDamageRise * (float)session.ExposureFullSeconds);
            p.Health = System.Math.Max(0f, p.Health - (float)(dt * dps));
            session.HazardDeathReason = session.ExposureHot ? "@srv.death.burned" : "@srv.death.froze";
        }
        else
        {
            session.ExposureFullSeconds = 0;
        }

        int level = p.Exposure >= 0.9f ? 3 : p.Exposure >= 0.75f ? 2 : p.Exposure >= 0.5f ? 1 : 0;
        if (level > session.ExposureWarned)
        {
            session.ExposureWarned = level;
            string kind = session.ExposureHot ? "hot" : "cold";
            SendVegaLine(session, $"vega.sys.exposure_{kind}_{(level == 1 ? 50 : level == 2 ? 75 : 90)}", 3);
        }
        else if (p.Exposure < 0.4f)
        {
            session.ExposureWarned = 0;
        }
    }

    /// <summary>Test/util: expose the local-source override (mirrors <see cref="NearHealTankForTest"/>).</summary>
    public float ApplyLocalSourcesForTest(Shared.Geometry.Vector3f pos, float ambientC) => ApplyLocalSources(pos, ambientC);

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
            _tempCampfireId = _content.GetBlock("campfire")?.NumericId.Value ?? 0;
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
                    else if ((_fireId != 0 && id == _fireId) || (_tempCampfireId != 0 && id == _tempCampfireId))
                    {
                        // The placed campfire block (#807) warms exactly like open fire — a controlled
                        // flame with the same gentle, comfort-band-capped radius.
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

    /// <summary>The temperature inside the city world's buildings (#1793) — the one cool place on the planet.</summary>
    private const float CityComfortC = 22f;

    /// <summary>True inside the city world's walls AND under a roof (#1793): the city is climate-controlled in
    /// its rooms, not on its streets — a garden or the landing plaza is as hot as the desert outside.</summary>
    private bool InCityShelter(Shared.Geometry.Vector3f pos)
    {
        if (_worlds.Active.CityFootprint is not { } city)
        {
            return false;
        }

        int px = (int)System.Math.Floor(pos.X), pz = (int)System.Math.Floor(pos.Z);
        return px >= city.MinX && px <= city.MaxX && pz >= city.MinZ && pz <= city.MaxZ && RoofedAt(pos);
    }

    /// <summary>Test seam (#1793).</summary>
    public bool InCityShelterForTest(Shared.Geometry.Vector3f pos) => InCityShelter(pos);

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

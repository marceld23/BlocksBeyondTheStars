// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Toxic worlds (#2024, terrain generation 13): the server half of the rolled world traits. Corrosive air (#2026) slowly
/// eats health outdoors — the ship, a station, a base zone and a sealed room keep it out, the suit liners' corrosion
/// resistance slows it — and VEGA reports each toxic world's hazards once, on landing (#2032). Like every environmental
/// hazard it is off in Creative mode and at hazard tier Off, and it scales with the tier.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The traits the active world rolled — THE function (<see cref="WorldTraits.For"/>) with the roster seed, as
    /// worldgen and the rosters call it; resolved once per world.</summary>
    private WorldTraits ActiveTraits
        => _worlds.Active.Traits ??= WorldTraits.For(_world.Planet,
            WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId), _meta.Description.TerrainGeneration);

    /// <summary>Test seam (#2024): the traits of the active world.</summary>
    public WorldTraits ActiveTraitsForTest => ActiveTraits;

    /// <summary>Corrosive air (#2026): outdoors on a world that rolled it, health drops by the type's
    /// <see cref="Shared.Definitions.PlanetType.AirDamagePerSecond"/> × the hazard tier × (1 − the suit's corrosion
    /// resistance). <paramref name="sheltered"/> = life support (ship, station, base zone, sealed room) or under water.</summary>
    private void TickCorrosiveAir(PlayerSession session, double dt, bool sheltered)
    {
        var p = session.State;
        double dps = _world.Planet?.AirDamagePerSecond ?? 0.0;
        if (dps <= 0.0 || sheltered || p.InEva || p.AboardShip || p.AboveAtmosphere
            || !Rules.TemperatureHazardsEnabledFor(p.ModeOverride) || !ActiveTraits.CorrosiveAir)
        {
            return;
        }

        float loss = (float)(dt * dps) * Rules.HazardSeverityFactor * (1f - CorrosionResistance(p));
        p.Health = System.Math.Max(0f, p.Health - loss);
        session.HazardDeathReason = "@srv.death.corrosive_air";
    }

    /// <summary>#2032: a toxic world's hazards are rolled per WORLD, so VEGA reports this world's scan once per world
    /// (a player who lands on a second toxic world hears that world's own line). Only types whose air CAN corrode.</summary>
    private void ShipAiToxicScan(PlayerSession session)
    {
        if (_world.Planet is not { AirDamagePerSecond: > 0 }
            || _meta.Description.TerrainGeneration < Shared.World.WorldDescription.ToxicWorldsGeneration)
        {
            return;
        }

        var traits = ActiveTraits;
        string scan = traits.CorrosiveAir ? (traits.ToxicWater ? "both" : "air") : traits.ToxicWater ? "water" : "calm";
        ShipAiHintOnce(session, ToxicScanHintPrefix + _world.LocationId + ":" + scan);
    }

    /// <summary>The once-flag prefix of the per-world toxic scan (mapped to the line <c>vega.hint.toxic_scan_&lt;scan&gt;</c>).</summary>
    private const string ToxicScanHintPrefix = "toxic:";

    /// <summary>Test seam (#2032): the landing scan for one player, as a landing runs it.</summary>
    public void ShipAiToxicScanForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            ShipAiToxicScan(session);
        }
    }
}

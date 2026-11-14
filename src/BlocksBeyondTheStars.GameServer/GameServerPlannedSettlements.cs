// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The grown settlements of terrain generation 41 (#3602), the stamper's half:
/// <list type="bullet">
/// <item><b>Site first, layout second</b> (#3589): a fresh settlement of a planned kit (grid or grown) seats the kit's extent
/// with the guaranteed search, reads the ground of the site it found and the side the nearest fresh water lies on, has the
/// planner lay it out into that ground (<see cref="SettlementPlanner"/>), pins the plan in its record and reserves the ground
/// lot by lot before it stamps.</item>
/// <item><b>Replay</b> (#3584): a record with a plan bakes it again — no search, no terrain read, no growth.</item>
/// </list>
/// The instance stream draws what every settlement draws (the ruin verdict, then the island wish), so the loot of a ruin
/// reads the same numbers on every load.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Everything one planned instance of the settlement roll carries into its stamp.</summary>
    private readonly record struct PlannedInstance(int Index, long InstSeed, System.Random Stream, StructurePlacementRecord? Record,
        StructureKit? Kit, string Tier, string RolledTier, double Hospitability, string Surface);

    /// <summary>The planned settlement of one instance (fresh or pinned), or null when it has no spot (recorded) or its plan
    /// cannot be read (warned; the stamped blocks stay).</summary>
    private PlacedSettlement? StampPlannedInstance(PlannedInstance pi, List<(int Cx, int Cz, int Hw, int Hl)> reserved,
        HashSet<string> usedNames, IReadOnlyList<StructureTemplate> kitPool)
    {
        var planet = _world.Planet;
        bool rolledRuined = pi.Stream.NextDouble() < RuinChance(pi.Hospitability) || planet.RuinedSettlementsOnly;
        bool ruined = pi.Record?.Ruined ?? rolledRuined;
        bool islandRoll = pi.Stream.NextDouble() < 0.5; // drawn on every world, as for every settlement
        bool wantIsland = planet.FloatingIslands && (islandRoll || planet.IsGasWorld);
        var placed = pi.Record is { Placed: true } rec
            ? ReplayPlanned(pi, rec, ruined, kitPool, usedNames)
            : FreshPlanned(pi, ruined, wantIsland, reserved, usedNames, kitPool);
        if (placed is not null)
        {
            PinSettlementTraits(FindPlacementRecord("settlement", pi.Index), pi.RolledTier, ruined);
        }

        return placed;
    }

    private PlacedSettlement? ReplayPlanned(PlannedInstance pi, StructurePlacementRecord rec, bool ruined,
        IReadOnlyList<StructureTemplate> kitPool, HashSet<string> usedNames)
    {
        if (!SettlementPlan.TryParse(rec.Plan, out var plan))
        {
            _log.Warn($"The plan of settlement {pi.Index} on '{_world.LocationId}' cannot be read — its blocks stay, nothing else is restored.");
            return null;
        }

        string tier = rec.Tier is { Length: > 0 } t ? t : pi.Tier;
        var structure = SettlementPlanBaker.Bake(plan, tier, ruined, pi.InstSeed, pi.Surface, _content, kitPool, _log.Warn);
        usedNames.Add(rec.Name);
        return NewPlanned(structure, new Vector3i(rec.X, rec.GroundY, rec.Z), rec.GroundY, tier, ruined, rec.OnIsland, rec.Name, pi.Stream, rec.Seat);
    }

    private PlacedSettlement? FreshPlanned(PlannedInstance pi, bool ruined, bool wantIsland, List<(int Cx, int Cz, int Hw, int Hl)> reserved,
        HashSet<string> usedNames, IReadOnlyList<StructureTemplate> kitPool)
    {
        var kit = pi.Kit!;
        int extent = SettlementPlanner.ExtentFor(kit, pi.Tier);
        var probe = SettlementStructure.Empty(extent, 24, extent, pi.Tier);
        string kind = ruined ? StructureTypeRules.Ruin : StructureTypeRules.Settlement;
        if (!TryPlaceStructureGuaranteed(probe, RngFor(pi.InstSeed, "search"), reserved, wantIsland,
                ruined ? SeatPolicyOf(StructureTypeRules.Ruin, SeatPolicy.Ruin) : SeatPolicyOf(StructureTypeRules.Settlement, SeatPolicy.Inhabited),
                avoidPlayerEdits: false, out var origin, out int groundY, out bool onIsland, out string seat, siteKind: kind))
        {
            RecordPlacementSkip("settlement", pi.Index);
            return null;
        }

        var plan = SettlementPlanner.Plan(new SettlementPlanRequest
        {
            Tier = pi.Tier,
            Seed = pi.InstSeed,
            Kit = kit,
            Pool = kitPool,
            Ground = FollowsGround(seat, onIsland) ? (x, z) => _generator.SurfaceHeight(_world.Planet, origin.X + x, origin.Z + z) : null,
            WaterSide = WaterSideOf(origin.X, origin.Z, extent),
        });
        groundY = plan.GroundBase ?? groundY;
        origin = new Vector3i(origin.X, groundY, origin.Z);
        var structure = SettlementPlanBaker.Bake(plan, pi.Tier, ruined, pi.InstSeed, pi.Surface, _content, kitPool, _log.Warn);
        string name = UniqueName(SettlementDisplayName(pi.Tier, ruined, RngFor(pi.InstSeed, "name")), usedNames);
        RecordPlacement("settlement", pi.Index, origin, groundY, onIsland, seat, name, FreshProceduralTemplate(kit),
            modules: 2, kit: kit.Key, hashRevision: StructurePlacementRecord.MixedHashRevision);
        FindPlacementRecord("settlement", pi.Index)!.Plan = plan.Serialize();
        ReservePlanGround(kind, plan, origin, groundY, seat);
        return NewPlanned(structure, origin, groundY, pi.Tier, ruined, onIsland, name, pi.Stream, seat);
    }

    /// <summary>Whether a seat lets a planned settlement follow the ground: on land, not on stilts, lava or an island.</summary>
    private static bool FollowsGround(string seat, bool onIsland) => !onIsland && seat is "flat" or "slope" or "shelf";

    private static PlacedSettlement NewPlanned(SettlementStructure structure, Vector3i origin, int groundY, string tier, bool ruined,
        bool onIsland, string name, System.Random stream, string seat)
        => new()
        {
            Structure = structure,
            Origin = origin,
            GroundY = groundY,
            Tier = tier,
            Ruined = ruined,
            OnIsland = onIsland,
            Name = name,
            Rng = stream,
            Seat = seat,
        };

    /// <summary>The box side the nearest fresh water lies beyond (#3587's bank village): the box edge whose middle is closest
    /// to fresh water, when that water is within a box's length; −1 on a world without rivers or with none near.</summary>
    private int WaterSideOf(int ox, int oz, int extent)
    {
        var planet = _world.Planet;
        int best = -1, bestD = extent;
        int half = extent / 2;
        var probes = new (int X, int Z)[] { (ox + half, oz - 8), (ox + half, oz + extent + 8), (ox - 8, oz + half), (ox + extent + 8, oz + half) };
        for (int side = 0; side < 4; side++)
        {
            int d = _generator.FreshWaterDistance(planet, probes[side].X, probes[side].Z);
            if (d != RiverGeography.None && d < bestD)
            {
                bestD = d;
                best = side;
            }
        }

        return best;
    }
}

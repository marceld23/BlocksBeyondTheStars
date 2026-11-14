// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The grown city's wall and its suburbs (#3599).</summary>
internal static partial class CityGrowth
{
    /// <summary>
    /// The wall along the outline of the inside (<see cref="Inside"/>): purple with its red band, open where a gate road
    /// crosses it, a tower on every outer corner with a light on top, guardians beside every gate inside and out.
    /// </summary>
    internal static void Wall(CityLayoutSpec spec, List<Gate> gates, System.Action<int, int, int, ushort, int> setTinted, ushort wall,
        ushort lamp, List<SettlementMarker> markers)
    {
        int w = spec.Footprint;
        var inside = Inside(spec);
        bool In(int x, int z) => x >= 0 && z >= 0 && x < w && z < w && inside[(x * w) + z];
        bool OnGate(int x, int z)
        {
            foreach (var g in gates)
            {
                var (x0, z0, x1, z1) = GateRoad(spec, g);
                if (x >= x0 && x <= x1 && z >= z0 && z <= z1)
                {
                    return true;
                }
            }

            return false;
        }

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < w; z++)
            {
                if (!In(x, z) || (In(x - 1, z) && In(x + 1, z) && In(x, z - 1) && In(x, z + 1)) || OnGate(x, z))
                {
                    continue;
                }

                for (int y = 1; y <= CityGenerator.WallHeight; y++)
                {
                    setTinted(x, y, z, wall, y == 3 ? CityGenerator.Red : CityGenerator.Purple);
                }

                bool corner = (!In(x - 1, z) || !In(x + 1, z)) && (!In(x, z - 1) || !In(x, z + 1));
                if (corner)
                {
                    Tower(x, z, setTinted, wall, lamp);
                }
            }
        }

        foreach (var g in gates)
        {
            GateGuards(spec, g, In, markers);
        }
    }

    /// <summary>A wall tower: a 3 × 3 column two rows above the wall, a light on top.</summary>
    private static void Tower(int x, int z, System.Action<int, int, int, ushort, int> setTinted, ushort wall, ushort lamp)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                for (int y = 1; y <= CityGenerator.WallHeight + 2; y++)
                    setTinted(x + dx, y, z + dz, wall, y == 3 || y == CityGenerator.WallHeight + 2 ? CityGenerator.Red : CityGenerator.Purple);

        setTinted(x, CityGenerator.WallHeight + 3, z, lamp, 0);
    }

    /// <summary>Two guardians beside a gate's road just inside the wall and two just outside it.</summary>
    private static void GateGuards(CityLayoutSpec spec, Gate g, System.Func<int, int, bool> inside, List<SettlementMarker> markers)
    {
        var (x0, z0, x1, z1) = GateRoad(spec, g);
        bool alongX = g.Side is 2 or 3;
        int step = g.Side is 0 or 2 ? 1 : -1; // from the footprint's edge inward
        int start = g.Side switch { 0 => z0, 1 => z1, 2 => x0, _ => x1 };
        int end = g.Side switch { 0 => z1, 1 => z0, 2 => x1, _ => x0 };
        int mid = alongX ? (z0 + z1) / 2 : (x0 + x1) / 2;
        for (int a = start; a != end; a += step)
        {
            if (!(alongX ? inside(a, mid) : inside(mid, a)))
            {
                continue;
            }

            foreach (int at in new[] { a - (2 * step), a + (2 * step) })
            {
                foreach (int side in new[] { (alongX ? z0 : x0) - 2, (alongX ? z1 : x1) + 2 })
                {
                    markers.Add(new SettlementMarker("guard_post", alongX ? new Vector3i(at, 1, side) : new Vector3i(side, 1, at)));
                }
            }

            return;
        }
    }

    /// <summary>
    /// The suburbs (#3599): outside every gate whose ground is clear of the city a street settlement of the suburb kit, planned
    /// on flat ground with its straight main road on the gate road, baked and merged into the city (its plaza and road-exit
    /// markers left out — the city has its own). <paramref name="plans"/> pins them like the districts' composition: a
    /// non-empty list replays (one entry per gate, "" = none), an empty one records. Returns the buildings it added.
    /// </summary>
    internal static int Suburbs(CityLayoutSpec spec, List<Gate> gates, long seed, GameContent content, StructureKit? cityKit,
        IReadOnlyList<StructureTemplate>? pool, IList<string>? plans, System.Action<string>? warn, RoomFurnisher.CellSink setCell,
        List<SettlementMarker> markers)
    {
        if (pool is null || content.KitByKey(spec.SuburbKit) is not { } kit)
        {
            return 0;
        }

        bool replay = plans is { Count: > 0 };
        var inside = Inside(spec);
        int buildings = 0;
        for (int g = 0; g < gates.Count; g++)
        {
            var box = SuburbBox(spec, gates[g], inside);
            SettlementPlan? plan = null;
            if (replay)
            {
                plan = g < plans!.Count && SettlementPlan.TryParse(plans[g], out var pinned) ? pinned : null;
            }
            else if (box is { } b)
            {
                plan = SettlementPlanner.Plan(new SettlementPlanRequest
                {
                    Tier = kit.Tier, Seed = SuburbSeed(seed, g), Kit = kit, Pool = pool, Extent = b.Size, Form = "street",
                    Axis = gates[g].Side is 2 or 3 ? "x" : "z", Straight = true,
                });
            }

            plans?.Add(replay ? plans[g] : plan?.Serialize() ?? string.Empty);
            if (plan is not null && box is { } at)
            {
                buildings += Merge(SettlementPlanBaker.Bake(plan, kit.Tier, false, SuburbSeed(seed, g), "sand", content, pool, warn), at.X, at.Z,
                    setCell, markers);
            }
        }

        if (replay)
        {
            while (plans!.Count > gates.Count)
            {
                plans.RemoveAt(plans.Count - 1); // a replay reads the pins; it never grows the list
            }
        }

        return buildings;
    }

    /// <summary>The square a gate's suburb plans in: outside the wall, its middle on the gate road, as deep as the suburb's
    /// margin; null when the city's inside reaches into it.</summary>
    private static (int X, int Z, int Size)? SuburbBox(CityLayoutSpec spec, Gate gate, bool[] inside)
    {
        int w = spec.Footprint, e = CityLayoutSpec.SuburbDepth;
        int mid = gate.Across + (gate.Width / 2) - 1 - (e / 2); // the planner's main road runs at e / 2 (two wide)
        var (x, z) = gate.Side switch
        {
            0 => (mid, 0),
            1 => (mid, w - e),
            2 => (0, mid),
            _ => (w - e, mid),
        };
        for (int dx = 0; dx < e; dx++)
            for (int dz = 0; dz < e; dz++)
            {
                int cx = x + dx, cz = z + dz;
                if (cx < 0 || cz < 0 || cx >= w || cz >= w || inside[(cx * w) + cz])
                {
                    return null;
                }
            }

        return (x, z, e);
    }

    /// <summary>A suburb's seed: the city's seed and the gate, rolled on until its people are human (the G.D.S. suburb kit
    /// holds human modules only).</summary>
    private static long SuburbSeed(long seed, int gate)
    {
        for (int k = 0; ; k++)
        {
            long s = unchecked(seed ^ (long)StableHashing.Mixed($"city:suburb:{gate}:{k}"));
            if (!SettlementPlanner.IsAlien(s))
            {
                return s;
            }
        }
    }

    private static int Merge(SettlementStructure s, int ox, int oz, RoomFurnisher.CellSink setCell, List<SettlementMarker> markers)
    {
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b != 0)
                    {
                        var (tint, glow) = s.GetModifier(x, y, z);
                        setCell(ox + x, y, oz + z, b, s.GetShape(x, y, z), tint, glow);
                    }
                }

        foreach (var m in s.Markers)
        {
            if (m.Type != SettlementPlanBaker.PlazaMarker && m.Type != SettlementPlanBaker.RoadExitMarker)
            {
                markers.Add(new SettlementMarker(m.Type, new Vector3i(ox + m.LocalPos.X, m.LocalPos.Y, oz + m.LocalPos.Z), m.DoorAxis, m.Data));
            }
        }

        return s.BuildingCount;
    }
}

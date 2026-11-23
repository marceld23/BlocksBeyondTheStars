// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity line's builder (#3984; the main file is GameServerIntercityRail.cs): it takes planned lines — any number of
/// stops each, any number of lines — pins them (<c>rail_line</c>/line index, <c>rail_station</c>/
/// <see cref="IntercityStationRecordIndex"/>), replays them from their records, makes them the active world's lines
/// (stations, pylon tops, protected pylon columns, reservations) and stamps each line's voxels once (its own feature key).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Makes the lines the active world's, then stamps every line whose voxels are not in the world yet (each in
    /// its own transaction, marked after it), and logs each.</summary>
    private void BuildIntercityLines(IReadOnlyList<IntercityPlan> lines, PlanetType planet, Stopwatch sw)
    {
        ActivateIntercity(lines);
        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        foreach (var line in lines)
        {
            int carved = 0, trees = 0;
            string feature = IntercityFeatureOf(line.LineIndex);
            if (!FeatureStamped(feature))
            {
                _repo.RunInTransaction(() => (carved, trees) = StampIntercityVoxels(line, surface));
                MarkFeatureStamped(feature); // after the transaction: a crash mid-stamp re-stamps, never half-marks
            }

            string which = line.LineIndex == 0 ? string.Empty : " " + line.LineIndex.ToString(CultureInfo.InvariantCulture); // line 0: the line as it always logged
            _log.Info($"Intercity line{which} on '{_world.LocationId}': " +
                      $"{string.Join(" ↔ ", line.Stops.Select(s => s.SettlementName))}, " +
                      $"{line.Pylons.Count} pylons, route {line.RouteLength:F0} blocks, {carved} corridor cells + {trees} tree cells cleared " +
                      $"in {sw.ElapsedMilliseconds} ms.");
        }
    }

    // =====================================================================================================
    // Pinning and replaying
    // =====================================================================================================

    /// <summary>Pins a planned line: the line record (the first stop's origin, the stops' names, the pylon tops in line
    /// order) and one record per stop (origin, floor, seat, name, heading).</summary>
    private void PinIntercityPlan(IntercityPlan plan)
    {
        var first = plan.Stops[0];
        RecordPlacement(IntercityLineKind, plan.LineIndex, first.Origin, first.Origin.Y, onIsland: false, "line",
            string.Join(" | ", plan.Stops.Select(s => s.SettlementName)),
            composition: plan.Pylons.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture) + "," + p.Z.ToString(CultureInfo.InvariantCulture)).ToList());
        for (int i = 0; i < plan.Stops.Count; i++)
        {
            var st = plan.Stops[i];
            RecordPlacement(IntercityStationKind, IntercityStationRecordIndex(plan.LineIndex, i), st.Origin, st.Origin.Y, onIsland: false, st.Seat,
                st.SettlementName, template: "heading=" + st.Heading.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Every placed line record of the body after line 0 (lines a given route pinned), replayed in index order.</summary>
    private List<IntercityPlan> ReplayFurtherIntercityLines()
    {
        var lines = new List<IntercityPlan>();
        foreach (var rec in _meta.Placements
                     .Where(r => r.LocationId == _world.LocationId && r.Kind == IntercityLineKind && r.Index > 0 && r.Placed)
                     .OrderBy(r => r.Index))
        {
            if (ReplayIntercityLine(rec) is { } plan)
            {
                lines.Add(plan);
            }
            else
            {
                _log.Warn($"The intercity line record {rec.Index.ToString(CultureInfo.InvariantCulture)} of '{_world.LocationId}' could not be replayed — the line stays off this load.");
            }
        }

        return lines;
    }

    /// <summary>A line from its records: its stops (the station records of its index, read in order until one is missing —
    /// at least two, every one placed with a heading) and its pylon tops (at least two per stop). Null when any part is broken.</summary>
    private IntercityPlan? ReplayIntercityLine(StructurePlacementRecord line)
    {
        var plan = new IntercityPlan { LineIndex = line.Index };
        for (int i = 0; i < IntercityStopsPerLine; i++)
        {
            var rec = FindPlacementRecord(IntercityStationKind, IntercityStationRecordIndex(line.Index, i));
            if (rec is null && i >= 2)
            {
                break; // the line's last stop was the one before
            }

            if (rec is not { Placed: true } || !rec.Template.StartsWith("heading=", StringComparison.Ordinal)
                || !int.TryParse(rec.Template.Substring("heading=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int heading)
                || heading is < 0 or > 3)
            {
                return null;
            }

            plan.Stops.Add(BuildStation(rec.Name, heading, new Vector3i(rec.X, rec.GroundY, rec.Z), rec.Seat));
        }

        foreach (string s in line.Composition ?? new List<string>())
        {
            if (!TryCell(s, out var cell))
            {
                return null;
            }

            plan.Pylons.Add(cell);
        }

        if (plan.Pylons.Count < 2 * plan.Stops.Count)
        {
            return null;
        }

        for (int j = 1; j < plan.Pylons.Count; j++)
        {
            float dx = (float)WorldConstants.WrapDeltaX((double)(plan.Pylons[j].X - plan.Pylons[j - 1].X), _world.Circumference);
            float dz = plan.Pylons[j].Z - plan.Pylons[j - 1].Z;
            plan.RouteLength += (float)Math.Sqrt(dx * dx + dz * dz);
        }

        return plan;
    }

    // =====================================================================================================
    // The active world's lines
    // =====================================================================================================

    /// <summary>Makes the plans the active world's lines: the stations, the pylon tops, the protected pylon columns and the
    /// footprints later stampers keep clear of (every line's stations first, then every line's spans outside a hall).</summary>
    private void ActivateIntercity(IReadOnlyList<IntercityPlan> lines)
    {
        var ic = Intercity;
        ic.Clear();
        foreach (var plan in lines)
        {
            var state = new IntercityLineState { Index = plan.LineIndex };
            state.Stations.AddRange(plan.Stops);
            state.Pylons.AddRange(plan.Pylons);
            ic.Lines.Add(state);
            ic.Stations.AddRange(plan.Stops);
            foreach (var p in plan.Pylons)
            {
                ic.PylonTops.Add(p);
            }

            ProtectIntercityPylons(plan);
        }

        foreach (var st in ic.Stations)
        {
            ic.Reservations.Add(st.Rect);
        }

        int circ = _world.Circumference;
        foreach (var plan in lines)
        {
            for (int j = 0; j < plan.Pylons.Count - 1; j++)
            {
                if (plan.IsStationSpan(j))
                {
                    continue;
                }

                var p = plan.Pylons[j];
                var q = plan.Pylons[j + 1];
                int dx = WorldConstants.WrapDeltaX(q.X - p.X, circ), dz = q.Z - p.Z;
                ic.Reservations.Add((p.X + dx / 2, p.Z + dz / 2, Math.Abs(dx) / 2 + 3, Math.Abs(dz) / 2 + 3));
            }
        }

        foreach (var r in ic.Reservations)
        {
            RegisterFootprint(StructureTypeRules.Intercity, r, owner: StructureTypeRules.Intercity); // #3514: one structure, many boxes
        }
    }

    /// <summary>Every cell of every free pylon column of a line (its foot up to its top) is protected.</summary>
    private void ProtectIntercityPylons(IntercityPlan plan)
    {
        var planet = _world.Planet;
        int circ = _world.Circumference;
        for (int j = 0; j < plan.Pylons.Count; j++)
        {
            if (plan.IsStationPylon(j))
            {
                continue;
            }

            var top = plan.Pylons[j];
            int foot = Math.Min(top.Y, PylonFoot(planet, top.X, top.Z));
            for (int yy = foot; yy <= top.Y; yy++)
            {
                Intercity.ProtectedCells.Add(WorldConstants.CanonicalBlock(new Vector3i(top.X, yy, top.Z), circ));
            }
        }
    }

    // =====================================================================================================
    // Voxels (once)
    // =====================================================================================================

    /// <summary>Stamps a line's stations, its pylon columns and its corridor. Returns the corridor cells and tree cells
    /// cleared. Must run inside a repo transaction.</summary>
    private (int Carved, int Trees) StampIntercityVoxels(IntercityPlan plan, string surface)
    {
        var planet = _world.Planet;
        foreach (var st in plan.Stops)
        {
            StampSettlementBlocks(new PlacedSettlement
            {
                Structure = st.Structure,
                Origin = st.Origin,
                GroundY = st.Origin.Y,
                Tier = RailStationGenerator.Tier,
                Ruined = false,
                OnIsland = false,
                Name = st.SettlementName,
                Rng = new Random(1),
                Seat = st.Seat,
            }, surface, crownRing: false);
        }

        var pylonId = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId ?? BlockId.Air;
        if (pylonId.IsAir)
        {
            return (0, 0);
        }

        for (int j = 0; j < plan.Pylons.Count; j++)
        {
            if (plan.IsStationPylon(j))
            {
                continue; // a station's own pylons are part of its hall
            }

            var top = plan.Pylons[j];
            int foot = Math.Min(top.Y, PylonFoot(planet, top.X, top.Z));
            for (int yy = foot; yy <= top.Y; yy++)
            {
                _world.SetBlock(new Vector3i(top.X, yy, top.Z), pylonId);
            }
        }

        return StampIntercityCorridor(plan);
    }

    /// <summary>Clears a line's corridor (every span, the halls' own included): anything in it goes but fluids, the pylons,
    /// the stops, the stations' own layout and the towns' blocks; a tree that reaches into it goes as a whole, within the
    /// line's tree budget.</summary>
    private (int Carved, int Trees) StampIntercityCorridor(IntercityPlan plan)
    {
        int carved = 0, trees = 0, treeCap = IntercityVegetationCapFor(_generator.TerrainGeneration, plan.RouteLength);
        var veg = SettlementVegetationIds;
        var seen = new HashSet<Vector3i>();
        for (int j = 0; j < plan.Pylons.Count - 1; j++)
        {
            foreach (var cell in IntercityCorridor(plan.Pylons[j], plan.Pylons[j + 1]))
            {
                var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
                if (!seen.Add(c) || !WithinBuildHeight(c.Y) || IsStationLayoutCell(c) || Intercity.PylonTops.Contains(c))
                {
                    continue; // the station's own blocks stand clear of the corridor by design; anything else in it goes
                }

                var id = _world.GetBlock(c);
                if (id.IsAir)
                {
                    continue;
                }

                var def = _content.BlockById(id);
                if (def is null || def.Liquid || IsFluid(id.Value)
                    || def.Key is RailRules.PylonBlockKey or RailRules.StopBlockKey || InAnySettlementBox(c))
                {
                    continue; // fluids stay (the line runs over them); the towns' blocks are never touched
                }

                if (veg.Contains(id.Value) && trees < treeCap)
                {
                    trees += ClearTreeAt(c, veg, treeCap - trees);
                    continue;
                }

                _world.SetBlock(c, BlockId.Air);
                carved++;
            }
        }

        return (carved, trees);
    }

    /// <summary>The corridor of one span: first exactly the cells <c>LinkClear</c> checks (so the generated link passes the
    /// player's own clearance rule), then the wagon's whole box (3 wide, 3.2 high over its floor) swept along the straight
    /// run between the two pylon tops half a block at a time, reaching to the box's edges.</summary>
    private IEnumerable<Vector3i> IntercityCorridor(Vector3i a, Vector3i b)
    {
        foreach (var cell in LinkClearCells(a, b))
        {
            yield return cell;
        }

        var ta = PylonTop(a);
        var tb = PylonTop(b);
        float dx = (float)WorldConstants.WrapDeltaX((double)(tb.X - ta.X), _world.Circumference);
        float dy = tb.Y - ta.Y, dz = tb.Z - ta.Z;
        float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = Math.Max(1, (int)Math.Ceiling(len * 2f));
        float hl = (float)Math.Sqrt(dx * dx + dz * dz);
        float rx = hl < 1e-3f ? 1f : dz / hl, rz = hl < 1e-3f ? 0f : -dx / hl;
        float[] sides = { -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f };
        float[] ups = { 0f, 1f, 2f, 3f, RailRules.WagonHeight + 0.1f };
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float cx = ta.X + dx * t, cy = ta.Y + dy * t + RailRules.HoverHeight, cz = ta.Z + dz * t;
            foreach (float side in sides)
            {
                foreach (float up in ups)
                {
                    yield return new Vector3i((int)Math.Floor(cx + rx * side), (int)Math.Floor(cy + up), (int)Math.Floor(cz + rz * side));
                }
            }
        }
    }

    /// <summary>Takes a whole tree out of the world from one of its cells: every connected trunk, crown and fruit cell within
    /// a tree's reach, so the corridor leaves neither a stump nor a floating crown. Never inside a town. Returns the cells cleared.</summary>
    private int ClearTreeAt(Vector3i seed, HashSet<ushort> veg, int budget)
    {
        const int Reach = 6, Rise = 26;
        const int PerTree = 600;
        int cleared = 0;
        var stack = new Stack<Vector3i>();
        var seen = new HashSet<Vector3i> { seed };
        stack.Push(seed);
        while (stack.Count > 0 && cleared < budget && cleared < PerTree)
        {
            var c = stack.Pop();
            if (!WithinBuildHeight(c.Y) || InAnySettlementBox(c) || IsStationLayoutCell(c))
            {
                continue;
            }

            var id = _world.GetBlock(c);
            if (!veg.Contains(id.Value))
            {
                continue;
            }

            _world.SetBlock(c, BlockId.Air);
            cleared++;
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = WorldConstants.CanonicalBlock(c + face, _world.Circumference);
                if (Math.Abs(WorldConstants.WrapDeltaX(n.X - seed.X, _world.Circumference)) <= Reach && Math.Abs(n.Z - seed.Z) <= Reach
                    && Math.Abs(n.Y - seed.Y) <= Rise && seen.Add(n))
                {
                    stack.Push(n);
                }
            }
        }

        return cleared;
    }

    /// <summary>Whether a cell lies inside any settlement's box, ruins included — the line's stamp never touches a town.</summary>
    private bool InAnySettlementBox(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - s.Min.X, circ);
            if (lx >= 0 && lx <= s.Max.X - s.Min.X && pos.Z >= s.Min.Z && pos.Z <= s.Max.Z && pos.Y >= s.Min.Y - 48 && pos.Y <= s.Max.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a station's own layout puts a block at this cell (its floor, posts, roof, lamps, benches, pylons,
    /// the stop) — as opposed to whatever else stands inside its hall.</summary>
    private bool IsStationLayoutCell(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var st in Intercity.Stations)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - st.Origin.X, circ);
            int ly = pos.Y - st.Origin.Y;
            int lz = pos.Z - st.Origin.Z;
            if (st.Structure.InBounds(lx, ly, lz) && st.Structure.Get(lx, ly, lz) != 0)
            {
                return true;
            }
        }

        return false;
    }
}

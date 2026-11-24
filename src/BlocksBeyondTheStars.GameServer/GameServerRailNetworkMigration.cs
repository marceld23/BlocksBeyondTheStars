// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Listed rail lines pinned by an older planner (#4016; the network's main file is GameServerRailNetwork.cs). A listed line
/// is planned once and replayed from its records ever after — so a world that reached Earth before the routed planner
/// (#4014) would keep the lines that ran through its cities. A line record carries the planner it was made by
/// (<see cref="ListedRailPlanPin"/> in its template) and the names of its stops; a record of another planner, of other stops
/// or of a line its list no longer has is <b>retired</b> on the load that finds it, before any line plans:
/// <list type="bullet">
/// <item><b>Its voxels come back</b>, cell by cell, where the old line's sections were stamped: the edit rows its halls wrote,
/// its pylon blocks, and the air its corridor and the trees it took out left — never a row a player wrote since (the row's
/// owner), never a town's columns, never another structure's box. The terrain, the city and the trees the generator and the
/// overlays make come back with the row gone.</item>
/// <item><b>Its records go</b> (the line's, every stop's) and its sections' stamped marks, so the new plan pins afresh and
/// stamps its own sections as players come near.</item>
/// <item><b>Its graph goes</b> when the rail state loads (<see cref="PurgeRetiredRailLines"/>): its public train, its links,
/// its nodes and its stops — the new line's graph and train are then made as on a first load.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The template of a listed line's record from the routed planner (#4014) on; a record without it was made by an
    /// older planner and is re-planned.</summary>
    internal const string ListedRailPlanPin = "listed:routed";

    /// <summary>Retires every listed line record of the body that is outdated: made by an older planner, for other stops than
    /// its place names now, or of a line the list no longer has.</summary>
    private void RetireOutdatedListedRailLines(PlaceListDefinition list, List<PlaceDefinition> places)
    {
        var outdated = _meta.Placements
            .Where(r => r.LocationId == _world.LocationId && r.Kind == IntercityLineKind && r.Index >= ListedRailLineIndexBase
                        && ListedRailRecordOutdated(list, places, r))
            .ToList();
        foreach (var rec in outdated)
        {
            RetireListedRailLine(rec);
        }
    }

    /// <summary>Whether a listed line record must be re-planned.</summary>
    private bool ListedRailRecordOutdated(PlaceListDefinition list, List<PlaceDefinition> places, StructurePlacementRecord rec)
    {
        int i = rec.Index - ListedRailLineIndexBase;
        if (i >= places.Count || rec.Template != ListedRailPlanPin)
        {
            return true;
        }

        var names = places[i].Stops.Select(s => ListedRailCityOf(list, s.City)?.Name).ToList();
        return names.TrueForAll(n => n is not null) && rec.Name != string.Join(" | ", names);
    }

    /// <summary>Retires one listed line: takes back what its stamped sections wrote, drops its records and its sections'
    /// marks, and leaves its plan for the rail state to purge its graph.</summary>
    private void RetireListedRailLine(StructurePlacementRecord rec)
    {
        var old = rec.Placed ? ReplayIntercityLine(rec) : null;
        int restored = 0;
        if (old is not null)
        {
            old.Listed = true;
            var built = IntercitySections(old).Where(s => s.Built).ToList();
            Intercity.Retired.Add(old); // before the take-back: its halls count as its own cells
            _repo.RunInTransaction(() => restored = TakeBackRailStamp(old, built));
        }

        string loc = _world.LocationId;
        int line = rec.Index;
        _meta.Placements.RemoveAll(r => r.LocationId == loc
                                        && ((r.Kind == IntercityLineKind && r.Index == line)
                                            || (r.Kind == IntercityStationKind && r.Index / IntercityStopsPerLine == line)));
        string sections = loc + "|" + IntercityFeatureOf(line) + ":";
        _meta.StampedFeatures.RemoveAll(f => f.StartsWith(sections, StringComparison.Ordinal));
        _placementRecordsDirty = true;
        SavePlacementRecords();
        _log.Info($"Rail line {line.ToString(CultureInfo.InvariantCulture)} on '{loc}' ({rec.Name}) was pinned by an older planner or for other stops: " +
                  $"retired for a new plan, {restored} stamped cells taken back.");
    }

    /// <summary>Takes back what the stamp of a line's <paramref name="sections"/> wrote (see the class summary). Returns the
    /// cells restored. Must run inside a repo transaction.</summary>
    private int TakeBackRailStamp(IntercityPlan old, List<IntercitySection> sections)
    {
        ushort pylon = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId.Value ?? 0;
        var boxes = new List<(Vector3i Min, Vector3i Max, Func<ushort, bool> Wrote)>();
        foreach (var s in sections)
        {
            foreach (var st in old.Stops.Where(st => StampsWithStretch(old, st, s.First, s.Last)))
            {
                boxes.Add((new Vector3i(st.Origin.X - 1, st.Origin.Y - IntercityStationDepth, st.Origin.Z - 1),
                    new Vector3i(st.Origin.X + st.W, st.Origin.Y + Math.Max(st.Structure.Height, SettlementVegetationRise) + 1, st.Origin.Z + st.L),
                    _ => true)); // a hall's stamp: whatever it wrote
            }

            for (int j = s.First; j <= s.Last; j++)
            {
                if (!old.IsStationPylon(j))
                {
                    var top = old.Pylons[j];
                    int foot = Math.Min(top.Y, Math.Min(PylonFoot(_world.Planet, top.X, top.Z), IntercityPlanFoot(old, top.X, top.Z)));
                    boxes.Add((new Vector3i(top.X, foot, top.Z), top, b => b == pylon));
                }
            }

            for (int j = s.First; j < s.Last; j++)
            {
                if (!old.IsStationSpan(j))
                {
                    boxes.Add(CorridorBand(old.Pylons[j], old.Pylons[j + 1]));
                }
            }
        }

        return RestoreStampedCells(boxes);
    }

    /// <summary>The box round a span that its corridor and the trees it took out lie in: the span's columns, a wagon's half width
    /// and a tree's reach to each side, from a tree's rise under the wagon's floor to a tree's rise over its roof — air only.</summary>
    private (Vector3i Min, Vector3i Max, Func<ushort, bool> Wrote) CorridorBand(Vector3i a, Vector3i b)
    {
        int dx = WorldConstants.WrapDeltaX(b.X - a.X, _world.Circumference);
        int reach = IntercityTreeReach + 3;
        int x0 = Math.Min(a.X, a.X + dx) - reach, x1 = Math.Max(a.X, a.X + dx) + reach;
        int z0 = Math.Min(a.Z, b.Z) - reach, z1 = Math.Max(a.Z, b.Z) + reach;
        int y0 = Math.Min(a.Y, b.Y) - IntercityTreeRise, y1 = Math.Max(a.Y, b.Y) + (int)Math.Ceiling(RailRules.WagonHeight) + 3 + IntercityTreeRise;
        return (new Vector3i(x0, y0, z0), new Vector3i(x1, y1, z1), block => block == 0);
    }

    /// <summary>Deletes every edit row inside the boxes that holds what its box's stamp wrote, carries no player's name and lies
    /// in no town's columns and no other structure's box; the loaded chunks of the boxes are dropped so they load again
    /// without the rows. Returns the rows deleted.</summary>
    private int RestoreStampedCells(List<(Vector3i Min, Vector3i Max, Func<ushort, bool> Wrote)> boxes)
    {
        var edits = new Dictionary<ChunkCoord, IReadOnlyList<BlockEdit>>();
        var done = new HashSet<Vector3i>();
        int restored = 0;
        foreach (var (min, max, wrote) in boxes)
        {
            foreach (var chunk in BoxChunks(min.X, max.X, min.Y, max.Y, min.Z, max.Z))
            {
                if (!edits.TryGetValue(chunk, out var rows))
                {
                    edits[chunk] = rows = _repo.LoadChunkEdits(_world.LocationId, chunk);
                }

                foreach (var edit in rows)
                {
                    var c = edit.WorldPosition;
                    if (InBoxWrapped(c, min, max) && wrote(edit.Block) && !done.Contains(c) && RestorableRailCell(c))
                    {
                        _repo.DeleteBlockEdits(_world.LocationId, c, c);
                        done.Add(c);
                        restored++;
                    }
                }
            }

            _world.ForgetChunksIn(min, max);
        }

        return restored;
    }

    /// <summary>Whether an old line's cell may come back: no player wrote it, and it lies in no town's columns and in no box of
    /// another structure (the lines' own reservations aside).</summary>
    private bool RestorableRailCell(Vector3i c)
    {
        if (InAnySettlementColumns(c) && !InIntercityHallOf(c))
        {
            return false;
        }

        foreach (var f in Footprints.Entries)
        {
            if (f.Kind != StructureTypeRules.Intercity && f.Kind != StructureTypeRules.Settlement
                && WorldConstants.WrapDeltaX(c.X - f.MinX, _world.Circumference) is int lx && lx >= 0 && lx <= f.MaxX - f.MinX
                && c.Z >= f.MinZ && c.Z <= f.MaxZ)
            {
                return false;
            }
        }

        return !_repo.HasPlayerBlockEdits(_world.LocationId, c, c);
    }

    /// <summary>Whether a cell lies in the hall box of a line being retired (a hall stands on a city's square, in its columns).</summary>
    private bool InIntercityHallOf(Vector3i c)
    {
        foreach (var plan in Intercity.Retired)
        {
            foreach (var st in plan.Stops)
            {
                int lx = WorldConstants.WrapDeltaX(c.X - st.Origin.X, _world.Circumference), lz = c.Z - st.Origin.Z;
                if (lx >= -1 && lx <= st.W && lz >= -1 && lz <= st.L && c.Y >= st.Origin.Y - IntercityStationDepth)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a cell lies in an inclusive box (X wrapped).</summary>
    private bool InBoxWrapped(Vector3i c, Vector3i min, Vector3i max)
    {
        int lx = WorldConstants.WrapDeltaX(c.X - min.X, _world.Circumference);
        return lx >= 0 && lx <= max.X - min.X && c.Y >= min.Y && c.Y <= max.Y && c.Z >= min.Z && c.Z <= max.Z;
    }

    /// <summary>When the rail state loads (#4016): the retired lines' public trains, links, nodes and stops go — but the nodes
    /// and stops a line of this load stands on. Returns whether anything went.</summary>
    private bool PurgeRetiredRailLines()
    {
        var retired = Intercity.Retired;
        if (retired.Count == 0)
        {
            return false;
        }

        RebuildRailLines(); // the lines as saved, so the trains find the ones they ran on
        var oldCells = new HashSet<Vector3i>(retired.SelectMany(p => p.Pylons));
        foreach (var t in Rails.Trains.ToList())
        {
            if (RailRules.IsPublic(t.OwnerId) && Rails.Lines.TryGetValue(t.LineId, out var line) && line.Pylons.Any(oldCells.Contains))
            {
                ReturnTrain(t, "@srv.rail.line_gone");
            }
        }

        foreach (var plan in retired)
        {
            UnlinkRetiredLine(plan);
        }

        retired.Clear();
        return true;
    }

    /// <summary>Takes one retired line out of the rail graph: its links, then every node of it no line of this load stands on
    /// and that keeps no other link, and its stops no station of this load has.</summary>
    private void UnlinkRetiredLine(IntercityPlan plan)
    {
        var nodes = Rails.Nodes;
        for (int j = 0; j + 1 < plan.Pylons.Count; j++)
        {
            if (nodes.TryGetValue(plan.Pylons[j], out var a) && nodes.TryGetValue(plan.Pylons[j + 1], out var b))
            {
                a.Links.Remove(b.Cell);
                b.Links.Remove(a.Cell);
            }
        }

        foreach (var p in plan.Pylons.Where(p => !Intercity.PylonTops.Contains(p)))
        {
            if (nodes.TryGetValue(p, out var node) && node.Links.Count == 0)
            {
                nodes.Remove(p);
            }
        }

        foreach (var st in plan.Stops)
        {
            if (!Intercity.Stations.Exists(s => s.Stop == st.Stop) && CrystalNet.Cells.TryGetValue(st.Stop, out var cell)
                && cell.Kind == CrystalDeviceKind.RailStop)
            {
                UnregisterCrystalCell(cell, relight: false);
            }
        }
    }
}

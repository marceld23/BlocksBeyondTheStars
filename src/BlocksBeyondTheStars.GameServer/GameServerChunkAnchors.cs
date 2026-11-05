// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sparse chunk anchoring (#3211). Only players kept chunks resident: the far-chunk sweep evicts every chunk farther than
/// the widest view distance + 4 from all players on a body, and every system that reads blocks only where chunks are
/// loaded — the Crystal Net's lamps, adjacency and machines, base walls and air, fluids, drills — failed open or stalled
/// at a base a few hundred blocks away from where its player stood on the same planet.
/// <para>So while at least one player is on a body, the server keeps a sparse set of that body's chunks resident wherever
/// they are: (1) every chunk that holds a Crystal Net cell, plus the shaft column of every lift — grouped by network and
/// taken smallest group first, so the cap drops whole networks, never half of one; (2) the chunks with player edits
/// within the largest reach a base can have — read once per base from the block-edit store and kept current by the edits
/// players make (<c>GameServerChunkAnchorsBases.cs</c>), never the whole box — nearest each core first, merged across the
/// bases. The host cap is <see cref="ServerConfig.MaxAnchoredChunks"/> per world (0 = off); a cut is logged once per change.</para>
/// <para>The set is recomputed at the sweep's pace (every 10 s, in <see cref="SweepFarChunks"/>) and on the first tick a
/// world has a player again; a chunk it names that is not resident is loaded a few per tick (<see cref="TickChunkAnchors"/>),
/// never as a synchronous mass load. A body nobody is on loads and ticks nothing: its machines keep the capped catch-up on
/// return (<c>GameServerCrystalCatchUp.cs</c>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Per-world anchor state (lives on <see cref="LoadedWorld"/>).</summary>
    internal sealed class ChunkAnchorState
    {
        /// <summary>The anchored chunks (canonical coords): the sweep keeps them resident.</summary>
        public HashSet<ChunkCoord> Anchored { get; } = new();

        /// <summary>Anchored chunks that were not resident at the last refresh, in priority order.</summary>
        public Queue<ChunkCoord> Pending { get; } = new();

        /// <summary>Whether the set was computed since this world (re)gained a player.</summary>
        public bool Computed { get; set; }

        /// <summary>Distinct chunks the last refresh wanted before the cap.</summary>
        public int Wanted { get; set; }

        /// <summary>How many chunks the cap cut at the last logged refresh (0 = none) — the log line repeats only on change.</summary>
        public int LastLoggedCut { get; set; }
    }

    /// <summary>Highest lift shaft (in chunks above its motor) the anchors cover.</summary>
    private const int AnchorLiftShaftMaxChunks = 16;

    /// <summary>Anchored chunks loaded per tick at most: one inline, or one per generation worker plus this thread.</summary>
    private const int AnchorLoadsPerTickCeiling = 3;

    private readonly List<ChunkCoord> _anchorLoadBatch = new();

    /// <summary>Test seam: chunks the anchor loader made resident so far.</summary>
    public int AnchorChunkLoadsForTest { get; private set; }

    /// <summary>Test seam: the anchored chunks of the active world.</summary>
    public int AnchoredChunkCountForTest => _worlds.Active.ChunkAnchors.Anchored.Count;

    /// <summary>Test seam: distinct chunks the active world's last refresh wanted before the cap.</summary>
    public int AnchorChunksWantedForTest => _worlds.Active.ChunkAnchors.Wanted;

    /// <summary>Test seam: whether a chunk of the active world is anchored.</summary>
    public bool IsChunkAnchoredForTest(ChunkCoord chunk)
        => _worlds.Active.ChunkAnchors.Anchored.Contains(WorldConstants.CanonicalChunk(chunk, _world.Circumference));

    /// <summary>Test seam: recomputes the active world's anchors now (as the sweep does).</summary>
    internal void RefreshChunkAnchorsForTest() => RefreshChunkAnchors();

    /// <summary>Per world, every tick (under <c>Guard</c>): computes the set on a world's first tick with a player and loads
    /// a few pending chunks. A world without a joined player (the headless fallback tick) drops its set and loads nothing.</summary>
    private void TickChunkAnchors()
    {
        var state = _worlds.Active.ChunkAnchors;
        if (_config.MaxAnchoredChunks <= 0 || !JoinedInActiveWorld().Any())
        {
            state.Anchored.Clear();
            state.Pending.Clear();
            state.Computed = false;
            return;
        }

        if (!state.Computed)
        {
            RefreshChunkAnchors();
        }

        LoadPendingAnchors(state);
    }

    /// <summary>Makes up to a few pending anchored chunks resident: in parallel on the generation pool when it runs, else
    /// one inline. Skips what got loaded meanwhile (a player walked there) or is no longer anchored.</summary>
    private void LoadPendingAnchors(ChunkAnchorState state)
    {
        if (state.Pending.Count == 0)
        {
            return;
        }

        var pool = ChunkGenPool();
        int budget = pool is null ? 1 : System.Math.Min(pool.Workers + 1, AnchorLoadsPerTickCeiling);
        var batch = _anchorLoadBatch;
        batch.Clear();
        while (state.Pending.Count > 0 && batch.Count < budget)
        {
            var chunk = state.Pending.Dequeue();
            if (state.Anchored.Contains(chunk) && !_world.IsChunkLoaded(chunk))
            {
                batch.Add(chunk);
            }
        }

        if (batch.Count == 0)
        {
            return;
        }

        pool?.EnsureGenerated(_world, batch);
        foreach (var chunk in batch)
        {
            _world.GetOrLoadChunk(chunk); // a no-op lookup for what the pool adopted
        }

        AnchorChunkLoadsForTest += batch.Count;
    }

    /// <summary>Recomputes the active world's anchored set (see the class summary): Crystal Net groups first, smallest
    /// first and whole, then the bases' built chunks nearest each core first, up to the host cap. Queues what is not
    /// resident for <see cref="LoadPendingAnchors"/>.</summary>
    private void RefreshChunkAnchors()
    {
        var state = _worlds.Active.ChunkAnchors;
        state.Computed = true;
        state.Anchored.Clear();
        state.Pending.Clear();
        int cap = _config.MaxAnchoredChunks;
        if (cap <= 0)
        {
            state.Wanted = 0;
            return;
        }

        var wanted = new HashSet<ChunkCoord>();
        foreach (var group in CrystalAnchorGroups())
        {
            wanted.UnionWith(group);
            if (CountNew(state.Anchored, group) <= cap - state.Anchored.Count)
            {
                foreach (var chunk in group)
                {
                    AddAnchor(state, chunk);
                }
            }
        }

        foreach (var (chunk, _) in BaseAnchorChunks())
        {
            wanted.Add(chunk);
            if (state.Anchored.Count < cap)
            {
                AddAnchor(state, chunk);
            }
        }

        state.Wanted = wanted.Count;
        LogAnchorCut(state, cap);
    }

    private void AddAnchor(ChunkAnchorState state, ChunkCoord chunk)
    {
        if (state.Anchored.Add(chunk) && !_world.IsChunkLoaded(chunk))
        {
            state.Pending.Enqueue(chunk);
        }
    }

    private static int CountNew(HashSet<ChunkCoord> anchored, List<ChunkCoord> group)
    {
        int fresh = 0;
        foreach (var chunk in group)
        {
            if (!anchored.Contains(chunk))
            {
                fresh++;
            }
        }

        return fresh;
    }

    /// <summary>One log line when the cap cuts the set — again only when the cut changes, so a sweep every 10 s stays quiet.</summary>
    private void LogAnchorCut(ChunkAnchorState state, int cap)
    {
        int cut = state.Wanted - state.Anchored.Count;
        if (cut <= 0)
        {
            state.LastLoggedCut = 0;
            return;
        }

        if (cut != state.LastLoggedCut)
        {
            state.LastLoggedCut = cut;
            _log.Info($"Chunk anchors on {_world.LocationId}: {state.Wanted} chunks hold Crystal Nets or base builds, the host cap " +
                      $"keeps {state.Anchored.Count} resident (MaxAnchoredChunks {cap}; whole nets smallest first, then base builds nearest each core).");
        }
    }

    /// <summary>The Crystal Net's chunks of the active world in groups, smallest first: one group per network (all the
    /// chunks its cells sit in), one per chunk holding cells without a network (gates, inert cells over a cap), one per
    /// lift shaft. Ties keep the order the groups were found in, so the result is deterministic.</summary>
    private List<List<ChunkCoord>> CrystalAnchorGroups()
    {
        var net = _worlds.Active.CrystalNet;
        var groups = new List<List<ChunkCoord>>();
        if (net.Cells.Count == 0)
        {
            return groups;
        }

        int circ = _world.Circumference;
        var byNet = new Dictionary<int, int>();
        var looseByChunk = new Dictionary<ChunkCoord, int>();
        var placed = new HashSet<(int Group, ChunkCoord Chunk)>();
        foreach (var cell in net.Cells.Values)
        {
            var chunk = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(cell.Cell), circ);
            bool inNet = cell.NetId != 0;
            int g;
            bool known = inNet ? byNet.TryGetValue(cell.NetId, out g) : looseByChunk.TryGetValue(chunk, out g);
            if (!known)
            {
                g = groups.Count;
                groups.Add(new List<ChunkCoord>());
                if (inNet)
                {
                    byNet[cell.NetId] = g;
                }
                else
                {
                    looseByChunk[chunk] = g;
                }
            }

            if (placed.Add((g, chunk)))
            {
                groups[g].Add(chunk);
            }
        }

        foreach (var lift in net.Lifts.Values)
        {
            groups.Add(LiftShaftChunks(lift, circ));
        }

        return SmallestFirst(groups);
    }

    /// <summary>Orders groups by size, ties by the order they were found (a stable sort).</summary>
    private static List<List<ChunkCoord>> SmallestFirst(List<List<ChunkCoord>> groups)
    {
        var order = new List<(int Count, int Index)>(groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            order.Add((groups[i].Count, i));
        }

        order.Sort();
        var sorted = new List<List<ChunkCoord>>(groups.Count);
        foreach (var (_, index) in order)
        {
            sorted.Add(groups[index]);
        }

        return sorted;
    }

    /// <summary>The chunk column of a lift from its motor up to its highest floor (capped).</summary>
    private static List<ChunkCoord> LiftShaftChunks(ServerLift lift, int circ)
    {
        int top = System.Math.Max((int)System.Math.Ceiling(lift.PlatformY), (int)System.Math.Ceiling(lift.TargetY));
        foreach (int level in lift.Levels)
        {
            top = System.Math.Max(top, level);
        }

        var motor = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(lift.Motor), circ);
        int hiCy = System.Math.Min(WorldConstants.WorldToChunk(top), motor.Y + AnchorLiftShaftMaxChunks - 1);
        var shaft = new List<ChunkCoord>();
        for (int cy = motor.Y; cy <= System.Math.Max(hiCy, motor.Y); cy++)
        {
            shaft.Add(new ChunkCoord(motor.X, cy, motor.Z));
        }

        return shaft;
    }

    /// <summary>Chebyshev distance between two canonical chunks, the short way round both seams.</summary>
    private static int ChunkChebyshev(ChunkCoord a, ChunkCoord b, int circ)
    {
        int dx = System.Math.Abs(WorldConstants.WrapChunkDelta(a.X - b.X, WorldConstants.ChunksAroundOf(circ)));
        int dz = System.Math.Abs(WorldConstants.WrapChunkDelta(a.Z - b.Z, WorldConstants.LatitudePeriodFor(circ) / WorldConstants.ChunkSize));
        return System.Math.Max(System.Math.Max(dx, System.Math.Abs(a.Y - b.Y)), dz);
    }
}

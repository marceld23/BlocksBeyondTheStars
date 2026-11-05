// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The base half of the chunk anchors (#3211): which chunks hold player builds around each base.
/// <para>Asking the block-edit store is a range scan over every edit of the body in the box's x strip, done under the
/// repository lock on the tick thread — far too much for every 10 s sweep of every base. So each base's edited chunks
/// are read ONCE (per <see cref="BaseAnchorRequerySeconds"/>) within the largest reach a base can have
/// (<see cref="WalledReachCapFor"/>; its enclosure box never grows past it, so a build anywhere a base can reach is
/// counted), and kept current from then on by the player edits on its resident world (<see cref="NoteBaseAnchorEdit"/>,
/// on <see cref="ServerWorld.PlayerBlockSet"/>). An edit row is never removed by a player — digging a cell writes an
/// owned air row — so adding is all the live path needs; the slow re-read heals what only a tool or a cleanup deletes.</para>
/// <para>The store query is never truncated (its limit is the box's own chunk count), so the nearest-first order of the
/// cap is decided here, on the wrapped distance to the core, not by the store's index order.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One base's player-edited chunks (canonical) within the largest reach a base can have.</summary>
    private sealed class BaseAnchorCache
    {
        public HashSet<ChunkCoord> Chunks { get; } = new();
        public double QueriedAt;
    }

    /// <summary>Seconds a base's edited-chunk set is trusted before it is read from the store again (a safety net — the live
    /// edits keep it current).</summary>
    private const double BaseAnchorRequerySeconds = 600.0;

    private readonly Dictionary<int, BaseAnchorCache> _baseAnchorCaches = new();

    /// <summary>Test seam: block-edit store queries the base anchors made so far.</summary>
    public int BaseAnchorQueriesForTest { get; private set; }

    /// <summary>The chunks with player edits around each base on the active world (plus each core's own chunk), with their
    /// Chebyshev chunk distance to that base's core, nearest first across all bases (ties by coordinate).</summary>
    private List<(ChunkCoord Chunk, int Dist)> BaseAnchorChunks()
    {
        var list = new List<(ChunkCoord Chunk, int Dist)>();
        string body = _world.LocationId;
        int circ = _world.Circumference;
        PruneBaseAnchorCaches();
        foreach (var b in _bases)
        {
            if (b.Planet != body)
            {
                continue;
            }

            var core = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(b.Cell), circ);
            list.Add((core, 0));
            foreach (var chunk in BaseAnchorCacheFor(b).Chunks)
            {
                list.Add((chunk, ChunkChebyshev(chunk, core, circ)));
            }
        }

        list.Sort((a, c) => a.Dist != c.Dist ? a.Dist.CompareTo(c.Dist)
            : a.Chunk.X != c.Chunk.X ? a.Chunk.X.CompareTo(c.Chunk.X)
            : a.Chunk.Y != c.Chunk.Y ? a.Chunk.Y.CompareTo(c.Chunk.Y)
            : a.Chunk.Z.CompareTo(c.Chunk.Z));
        return list;
    }

    /// <summary>A base's edited-chunk set: cached, or read from the store (one query per canonical piece of the box).</summary>
    private BaseAnchorCache BaseAnchorCacheFor(ServerBase b)
    {
        if (_baseAnchorCaches.TryGetValue(b.Id, out var cached) && _uptime - cached.QueriedAt < BaseAnchorRequerySeconds)
        {
            return cached;
        }

        var cache = new BaseAnchorCache { QueriedAt = _uptime };
        int circ = _world.Circumference;
        int latPeriod = WorldConstants.LatitudePeriodFor(circ);
        int reach = WalledReachCapFor(_world);
        foreach (var (xLo, xHi) in CanonicalRanges(b.Cell.X - reach, b.Cell.X + reach, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(b.Cell.Z - reach, b.Cell.Z + reach, -latPeriod / 2, latPeriod))
            {
                var min = new Vector3i(xLo, b.Cell.Y - reach, zLo);
                var max = new Vector3i(xHi, b.Cell.Y + reach, zHi);
                BaseAnchorQueriesForTest++;
                foreach (var chunk in _repo.ListPlayerEditedChunks(b.Planet, min, max, ChunksInBox(min, max)))
                {
                    cache.Chunks.Add(WorldConstants.CanonicalChunk(chunk, circ));
                }
            }
        }

        _baseAnchorCaches[b.Id] = cache;
        return cache;
    }

    /// <summary>How many chunks an inclusive box can touch — the store query's limit, so it never cuts.</summary>
    private static int ChunksInBox(Vector3i min, Vector3i max)
    {
        long n = (long)(WorldConstants.WorldToChunk(max.X) - WorldConstants.WorldToChunk(min.X) + 1)
                 * (WorldConstants.WorldToChunk(max.Y) - WorldConstants.WorldToChunk(min.Y) + 1)
                 * (WorldConstants.WorldToChunk(max.Z) - WorldConstants.WorldToChunk(min.Z) + 1);
        return (int)System.Math.Min(n, int.MaxValue);
    }

    /// <summary>A player set a block on a resident world: every base on it whose set is cached and whose largest reach holds
    /// the cell now counts the cell's chunk. No store access.</summary>
    private void NoteBaseAnchorEdit(ServerWorld world, Vector3i cell)
    {
        if (_baseAnchorCaches.Count == 0)
        {
            return;
        }

        int circ = world.Circumference;
        int reach = WalledReachCapFor(world);
        var canonical = WorldConstants.CanonicalBlock(cell, circ);
        foreach (var b in _bases)
        {
            if (b.Planet != world.LocationId || !_baseAnchorCaches.TryGetValue(b.Id, out var cache))
            {
                continue;
            }

            if (System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - b.Cell.X, circ)) <= reach
                && System.Math.Abs(canonical.Y - b.Cell.Y) <= reach
                && System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - b.Cell.Z, circ)) <= reach)
            {
                cache.Chunks.Add(WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(canonical), circ));
            }
        }
    }

    /// <summary>Drops the sets of bases that no longer exist (ids are never reused).</summary>
    private void PruneBaseAnchorCaches()
    {
        if (_baseAnchorCaches.Count <= _bases.Count)
        {
            return;
        }

        var live = new HashSet<int>();
        foreach (var b in _bases)
        {
            live.Add(b.Id);
        }

        var gone = new List<int>();
        foreach (int id in _baseAnchorCaches.Keys)
        {
            if (!live.Contains(id))
            {
                gone.Add(id);
            }
        }

        foreach (int id in gone)
        {
            _baseAnchorCaches.Remove(id);
        }
    }
}

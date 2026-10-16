// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #2676: where the flight view puts a derelict's broken-look extras — worked out once per hull mesh from the cells the
/// client already holds (no protocol change), so the per-frame part only animates:
/// <list type="bullet">
/// <item><see cref="BeaconCell"/> — the flickering beacon: the dead beacon block (a beta wreck's, floating beside its
/// snapped mast), else the highest cell over the hull's middle (an old derelict's dome or antenna);</item>
/// <item><see cref="SparkCells"/> — ragged edges where sparks fly now and then: cells with at least
/// <see cref="RaggedOpenFaces"/> open faces (a torn wing's edge, the mast stump, a loose block);</item>
/// <item><see cref="DebrisHomes"/> — three to six free cells a little way off those edges, where small pieces tumble.</item>
/// </list>
/// Deterministic from the structure id, so every client shows the same picture.
/// </summary>
public sealed class DerelictDecorPlan
{
    /// <summary>A cell with this many open faces (of six) counts as a ragged edge.</summary>
    public const int RaggedOpenFaces = 4;

    /// <summary>At most this many spark points are kept.</summary>
    public const int MaxSparkCells = 8;

    /// <summary>How far (in blocks, along X and Z) from the box's centre column a cell still counts as "over the middle".</summary>
    private const double MiddleReach = 4.5;

    private DerelictDecorPlan(Vector3i? beacon, List<Vector3i> sparks, List<Vector3i> debris)
    {
        BeaconCell = beacon;
        SparkCells = sparks;
        DebrisHomes = debris;
    }

    public Vector3i? BeaconCell { get; }

    public IReadOnlyList<Vector3i> SparkCells { get; }

    public IReadOnlyList<Vector3i> DebrisHomes { get; }

    /// <summary>Plans the extras of the hull <paramref name="cells"/>. <paramref name="isBeacon"/> picks the beacon
    /// block (the dead lamp); null takes the highest cell over the middle.</summary>
    public static DerelictDecorPlan Build(IReadOnlyDictionary<Vector3i, BlockId> cells, string structureId, Func<BlockId, bool>? isBeacon = null)
    {
        var sparks = new List<Vector3i>();
        var debris = new List<Vector3i>();
        if (cells.Count == 0)
        {
            return new DerelictDecorPlan(null, sparks, debris);
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var c in cells.Keys)
        {
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
        }

        bool Solid(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) && !b.IsAir;

        // The cells in a stable order — the dictionary's own order depends on how the design arrived.
        var ordered = new List<Vector3i>(cells.Count);
        foreach (var kv in cells)
        {
            if (!kv.Value.IsAir)
            {
                ordered.Add(kv.Key);
            }
        }

        ordered.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));

        // The beacon: the highest beacon block in the upper half, else the highest cell over the box's middle (within
        // MiddleReach of its centre column — a torn-off wing moves the box's centre a few blocks off the hub's).
        double cx = (minX + maxX + 1) * 0.5, cz = (minZ + maxZ + 1) * 0.5, midY = (minY + maxY) * 0.5;
        Vector3i? beacon = null, top = null;
        foreach (var c in ordered)
        {
            if (c.Y < midY)
            {
                continue;
            }

            if (isBeacon != null && isBeacon(cells[c]) && (beacon is null || c.Y > beacon.Value.Y))
            {
                beacon = c;
            }

            if (Math.Abs(c.X + 0.5 - cx) <= MiddleReach && Math.Abs(c.Z + 0.5 - cz) <= MiddleReach && (top is null || c.Y > top.Value.Y))
            {
                top = c;
            }
        }

        // The ragged edges.
        var ragged = new List<Vector3i>();
        foreach (var c in ordered)
        {
            int open = 0;
            open += Solid(c.X + 1, c.Y, c.Z) ? 0 : 1;
            open += Solid(c.X - 1, c.Y, c.Z) ? 0 : 1;
            open += Solid(c.X, c.Y + 1, c.Z) ? 0 : 1;
            open += Solid(c.X, c.Y - 1, c.Z) ? 0 : 1;
            open += Solid(c.X, c.Y, c.Z + 1) ? 0 : 1;
            open += Solid(c.X, c.Y, c.Z - 1) ? 0 : 1;
            if (open >= RaggedOpenFaces)
            {
                ragged.Add(c);
            }
        }

        var rng = new Random(unchecked((int)WorldGenerator.StableHash("derelict-decor:" + structureId)));
        var pool = new List<Vector3i>(ragged);
        while (sparks.Count < MaxSparkCells && pool.Count > 0)
        {
            int i = rng.Next(pool.Count);
            sparks.Add(pool[i]);
            pool.RemoveAt(i);
        }

        // The tumbling pieces: two or three blocks off a ragged edge, in free space (no hull cell next to it).
        int want = 3 + rng.Next(4);
        for (int tries = 0; tries < 60 && debris.Count < want && ragged.Count > 0; tries++)
        {
            var from = ragged[rng.Next(ragged.Count)];
            int reach = 2 + rng.Next(2);
            var home = new Vector3i(from.X + (rng.Next(3) - 1) * reach, from.Y + (rng.Next(3) - 1) * reach, from.Z + (rng.Next(3) - 1) * reach);
            if (home == from || debris.Contains(home) || !Clear(home))
            {
                continue;
            }

            debris.Add(home);
        }

        return new DerelictDecorPlan(beacon ?? top, sparks, debris);

        bool Clear(Vector3i c)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (Solid(c.X + dx, c.Y + dy, c.Z + dz))
                        {
                            return false;
                        }
                    }

            return true;
        }
    }
}

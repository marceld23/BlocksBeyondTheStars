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
/// Deterministic from the structure id, so every client shows the same picture. #3615: a giant wreck's shell holds over a
/// hundred thousand cells, so the plan walks them once with an occupancy bit set over the hull's box (no dictionary lookups
/// for the neighbours) and sorts only the ragged edges — the same picture as before, in a fraction of the time.
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

        var solid = Occupancy.Of(cells);

        // The beacon: the highest beacon block in the upper half, else the highest cell over the box's middle (within
        // MiddleReach of its centre column — a torn-off wing moves the box's centre a few blocks off the hub's). Ties go to the
        // lowest x, then the lowest z — the cell an x/y/z-sorted walk meets first, so the dictionary's order never matters.
        double cx = (solid.MinX + solid.MaxX + 1) * 0.5, cz = (solid.MinZ + solid.MaxZ + 1) * 0.5, midY = (solid.MinY + solid.MaxY) * 0.5;
        Vector3i? beacon = null, top = null;
        var ragged = new List<Vector3i>();
        foreach (var kv in cells)
        {
            if (kv.Value.IsAir)
            {
                continue;
            }

            var c = kv.Key;
            if (c.Y >= midY)
            {
                if (isBeacon != null && isBeacon(kv.Value) && Higher(c, beacon))
                {
                    beacon = c;
                }

                if (Math.Abs(c.X + 0.5 - cx) <= MiddleReach && Math.Abs(c.Z + 0.5 - cz) <= MiddleReach && Higher(c, top))
                {
                    top = c;
                }
            }

            if (solid.OpenFaces(c) >= RaggedOpenFaces)
            {
                ragged.Add(c); // the ragged edges, sorted below (x, then y, then z) for a stable pick
            }
        }

        solid.SortXyz(ragged);
        PickSparksAndDebris(ragged, solid, new Random(unchecked((int)WorldGenerator.StableHash("derelict-decor:" + structureId))), sparks, debris);
        return new DerelictDecorPlan(beacon ?? top, sparks, debris);
    }

    /// <summary>Whether <paramref name="c"/> beats <paramref name="best"/>: higher, or as high and first in x/z order.</summary>
    private static bool Higher(Vector3i c, Vector3i? best)
        => best is not { } b || c.Y > b.Y || (c.Y == b.Y && (c.X < b.X || (c.X == b.X && c.Z < b.Z)));

    /// <summary>Up to <see cref="MaxSparkCells"/> spark points among the ragged edges and three to six tumbling pieces two or
    /// three blocks off them, in free space (no hull cell next to it).</summary>
    private static void PickSparksAndDebris(List<Vector3i> ragged, Occupancy solid, Random rng, List<Vector3i> sparks, List<Vector3i> debris)
    {
        var pool = new List<Vector3i>(ragged);
        while (sparks.Count < MaxSparkCells && pool.Count > 0)
        {
            int i = rng.Next(pool.Count);
            sparks.Add(pool[i]);
            pool.RemoveAt(i);
        }

        int want = 3 + rng.Next(4);
        for (int tries = 0; tries < 60 && debris.Count < want && ragged.Count > 0; tries++)
        {
            var from = ragged[rng.Next(ragged.Count)];
            int reach = 2 + rng.Next(2);
            var home = new Vector3i(from.X + (rng.Next(3) - 1) * reach, from.Y + (rng.Next(3) - 1) * reach, from.Z + (rng.Next(3) - 1) * reach);
            if (home == from || debris.Contains(home) || !solid.ClearAround(home))
            {
                continue;
            }

            debris.Add(home);
        }
    }

    /// <summary>Which cells of the hull are solid (non-air), as one bit per cell of its box — the neighbour tests of the plan
    /// without a dictionary lookup each. A box too large for a bit set (never a generated hull: those stay within 256 a side)
    /// reads the dictionary instead.</summary>
    private sealed class Occupancy
    {
        private const long MaxBits = 1L << 26; // 64 Mi cells = 8 MB — four times a 256³ giant wreck's box

        private readonly IReadOnlyDictionary<Vector3i, BlockId> _cells;
        private readonly ulong[]? _bits;
        private readonly int _w, _h, _l;

        private Occupancy(IReadOnlyDictionary<Vector3i, BlockId> cells, Vector3i min, Vector3i max)
        {
            _cells = cells;
            MinX = min.X; MinY = min.Y; MinZ = min.Z;
            MaxX = max.X; MaxY = max.Y; MaxZ = max.Z;
            _w = max.X - min.X + 1;
            _h = max.Y - min.Y + 1;
            _l = max.Z - min.Z + 1;
            long volume = (long)_w * _h * _l;
            if (volume <= MaxBits)
            {
                _bits = new ulong[(volume + 63) / 64];
            }
        }

        public int MinX { get; }
        public int MinY { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxY { get; }
        public int MaxZ { get; }

        public static Occupancy Of(IReadOnlyDictionary<Vector3i, BlockId> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var c in cells.Keys)
            {
                minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
                maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
            }

            var o = new Occupancy(cells, new Vector3i(minX, minY, minZ), new Vector3i(maxX, maxY, maxZ));
            if (o._bits != null)
            {
                foreach (var kv in cells)
                {
                    if (!kv.Value.IsAir)
                    {
                        long i = o.Index(kv.Key.X, kv.Key.Y, kv.Key.Z);
                        o._bits[i >> 6] |= 1UL << (int)(i & 63);
                    }
                }
            }

            return o;
        }

        private long Index(int x, int y, int z) => (((long)(x - MinX) * _h) + (y - MinY)) * _l + (z - MinZ);

        public bool Solid(int x, int y, int z)
        {
            if (_bits == null)
            {
                return _cells.TryGetValue(new Vector3i(x, y, z), out var b) && !b.IsAir;
            }

            if (x < MinX || y < MinY || z < MinZ || x > MaxX || y > MaxY || z > MaxZ)
            {
                return false;
            }

            long i = Index(x, y, z);
            return (_bits[i >> 6] & (1UL << (int)(i & 63))) != 0;
        }

        public int OpenFaces(Vector3i c)
            => (Solid(c.X + 1, c.Y, c.Z) ? 0 : 1) + (Solid(c.X - 1, c.Y, c.Z) ? 0 : 1)
               + (Solid(c.X, c.Y + 1, c.Z) ? 0 : 1) + (Solid(c.X, c.Y - 1, c.Z) ? 0 : 1)
               + (Solid(c.X, c.Y, c.Z + 1) ? 0 : 1) + (Solid(c.X, c.Y, c.Z - 1) ? 0 : 1);

        /// <summary>Whether no solid cell touches <paramref name="c"/> (its 26 neighbours and itself).</summary>
        public bool ClearAround(Vector3i c)
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

        /// <summary>Sorts cells of the box by x, then y, then z: packed into one number each (21 bits per axis from the box's
        /// corner) and sorted as plain numbers — a box wider than that on an axis sorts with a comparison instead.</summary>
        public void SortXyz(List<Vector3i> cells)
        {
            const int Span = 1 << 21;
            if (_w > Span || _h > Span || _l > Span)
            {
                cells.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
                return;
            }

            var packed = new long[cells.Count];
            for (int i = 0; i < packed.Length; i++)
            {
                var c = cells[i];
                packed[i] = ((long)(c.X - MinX) << 42) | ((long)(c.Y - MinY) << 21) | (long)(c.Z - MinZ);
            }

            Array.Sort(packed);
            for (int i = 0; i < packed.Length; i++)
            {
                long p = packed[i];
                cells[i] = new Vector3i((int)(p >> 42) + MinX, (int)((p >> 21) & (Span - 1)) + MinY, (int)(p & (Span - 1)) + MinZ);
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 40 (#3557, #3558): the river valleys of one world. <see cref="RiverField.Build"/> keeps the EDGE
/// columns of every surface reach — a river column with a 4-neighbour that is not one — as samples (position, water
/// surface, class), bucketed by 16-block cell, plus a mask of the cells any sample can reach. <see cref="Lower"/> eases a
/// column's ground down toward the nearest water: within a class's flat floor the ground may sink to the water surface,
/// beyond it the allowed height rises with the distance, and over the outer 30 % of the reach the cut fades out, so the
/// valley meets the land without a step. The result only ever lowers.
/// <para>Integer inputs, IEEE-exact <c>Math.Sqrt</c> and plain arithmetic: server and client compute the same height.
/// Underground (karst) reaches record nothing — a valley never cuts toward a river in a tunnel.</para>
/// </summary>
public sealed class RiverValleys
{
    /// <summary>The sample bucket size (blocks).</summary>
    public const int CellSize = 16;

    /// <summary>A brook (one or two wide), a river that has gathered flow, a trunk river, a lake.</summary>
    public const byte Brook = 0, Gathered = 1, Trunk = 2, Lake = 3;

    // Per class at strength 100: the reach (blocks from the water's edge), the flat floor beside the water, the slope beyond
    // it (rise per run) and the deepest cut (a river through a massif widens its gorge, it does not carve the mountain away).
    private static readonly int[] Reach100 = { 10, 22, 36, 18 };
    private static readonly int[] Floor100 = { 1, 3, 6, 3 };
    private static readonly int[] RiseNum = { 1, 1, 1, 1 };
    private static readonly int[] RiseDen = { 1, 2, 3, 2 };
    private static readonly int[] MaxCut100 = { 10, 20, 28, 12 };

    /// <summary>The share of a valley's reach over which the cut fades out to nothing.</summary>
    private const double FadeShare = 0.3;

    private readonly int _circumference;
    private readonly int _period;
    private readonly int _gridW;
    private readonly int _gridH;
    private readonly int[] _start;   // CSR: the samples of cell c are [_start[c], _start[c + 1])
    private readonly int[] _x;       // wrapped world X
    private readonly int[] _z;       // wrapped world Z (latitude domain)
    private readonly int[] _water;   // the water surface the sample's column carries
    private readonly byte[] _class;
    private readonly bool[] _mask;   // cells within reach of any sample
    private readonly int _cellReach; // how many cells around a column can hold a sample in reach
    private readonly double[] _reach;
    private readonly double[] _floor;
    private readonly double[] _maxCut;
    private readonly double _depth;  // how much of the cut is taken (strength, capped at 1)

    private RiverValleys(int circumference, int period, int gridW, int gridH, int[] start, int[] x, int[] z, int[] water,
        byte[] cls, bool[] mask, int cellReach, double[] reach, double[] floor, double[] maxCut, double depth, int strengthPercent)
    {
        _circumference = circumference;
        _period = period;
        _gridW = gridW;
        _gridH = gridH;
        _start = start;
        _x = x;
        _z = z;
        _water = water;
        _class = cls;
        _mask = mask;
        _cellReach = cellReach;
        _reach = reach;
        _floor = floor;
        _maxCut = maxCut;
        _depth = depth;
        StrengthPercent = strengthPercent;
    }

    /// <summary>How many edge samples the valleys keep (inspection / tests).</summary>
    public int SampleCount => _x.Length;

    /// <summary>The strength the valleys were built with (100 = the class table as written; lava rivers 50).</summary>
    public int StrengthPercent { get; }

    /// <summary>The widest reach of any class at this strength (blocks from the water's edge).</summary>
    public double MaxReach => System.Math.Max(System.Math.Max(_reach[0], _reach[1]), System.Math.Max(_reach[2], _reach[3]));

    /// <summary>Builds the valleys from a field's stamped columns. <paramref name="classes"/> maps a column to its reach's
    /// class (absent = <see cref="Brook"/>). Null when <paramref name="strengthPercent"/> is 0 or no surface reach exists.</summary>
    internal static RiverValleys? Build(Dictionary<(int X, int Z), RiverField.RiverColumn> cols, Dictionary<(int X, int Z), byte> classes,
        int circumference, int strengthPercent)
    {
        if (strengthPercent <= 0 || cols.Count == 0)
        {
            return null;
        }

        int period = WorldConstants.LatitudePeriodFor(circumference);
        int gridW = System.Math.Max(1, (circumference + CellSize - 1) / CellSize);
        int gridH = System.Math.Max(1, (period + CellSize - 1) / CellSize);

        bool Surface((int, int) key) => cols.TryGetValue(key, out var c) && !c.Underground;

        // The edge columns, sorted so the buckets (and so every later sum) never depend on dictionary order.
        var edges = new List<(int Cell, int X, int Z, int Water, byte Class)>();
        foreach (var kv in cols)
        {
            if (kv.Value.Underground)
            {
                continue;
            }

            var (x, z) = kv.Key;
            bool edge = !Surface((WorldConstants.WrapX(x + 1, circumference), z))
                        || !Surface((WorldConstants.WrapX(x - 1, circumference), z))
                        || !Surface((x, WorldConstants.WrapZ(z + 1, circumference)))
                        || !Surface((x, WorldConstants.WrapZ(z - 1, circumference)));
            if (!edge)
            {
                continue;
            }

            byte cls = classes.TryGetValue(kv.Key, out var c) ? c : Brook;
            edges.Add((CellOf(x, z, period, gridW, gridH), x, z, kv.Value.WaterSurfaceY, cls));
        }

        if (edges.Count == 0)
        {
            return null;
        }

        edges.Sort((a, b) => a.Cell != b.Cell ? a.Cell.CompareTo(b.Cell)
            : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        int n = gridW * gridH;
        var start = new int[n + 1];
        foreach (var e in edges)
        {
            start[e.Cell + 1]++;
        }

        for (int c = 0; c < n; c++)
        {
            start[c + 1] += start[c];
        }

        var xs = new int[edges.Count];
        var zs = new int[edges.Count];
        var water = new int[edges.Count];
        var cls2 = new byte[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            (_, xs[i], zs[i], water[i], cls2[i]) = edges[i];
        }

        double s = strengthPercent / 100.0;
        var reach = new double[4];
        var floor = new double[4];
        var maxCut = new double[4];
        for (int k = 0; k < 4; k++)
        {
            reach[k] = System.Math.Max(2.0, Reach100[k] * s);
            floor[k] = Floor100[k] * s;
            maxCut[k] = MaxCut100[k] * s;
        }

        double maxReach = System.Math.Max(System.Math.Max(reach[0], reach[1]), System.Math.Max(reach[2], reach[3]));
        int cellReach = (int)System.Math.Ceiling(maxReach / CellSize);

        // The mask: every cell within cellReach of a cell that holds a sample.
        var mask = new bool[n];
        for (int c = 0; c < n; c++)
        {
            if (start[c + 1] == start[c])
            {
                continue;
            }

            int gx = c % gridW, gz = c / gridW;
            for (int dz = -cellReach; dz <= cellReach; dz++)
            {
                int mz = ((gz + dz) % gridH + gridH) % gridH;
                for (int dx = -cellReach; dx <= cellReach; dx++)
                {
                    int mx = ((gx + dx) % gridW + gridW) % gridW;
                    mask[mz * gridW + mx] = true;
                }
            }
        }

        return new RiverValleys(circumference, period, gridW, gridH, start, xs, zs, water, cls2, mask, cellReach, reach, floor,
            maxCut, System.Math.Min(1.0, s), strengthPercent);
    }

    private static int CellOf(int x, int z, int period, int gridW, int gridH)
    {
        int gx = x / CellSize;
        if (gx >= gridW) gx = gridW - 1;
        int zc = ((z + period / 2) % period + period) % period;
        int gz = zc / CellSize;
        if (gz >= gridH) gz = gridH - 1;
        return gz * gridW + gx;
    }

    /// <summary>True when a valley may reach this column at all (one array read — the answer for most of a world).</summary>
    public bool Near(int worldX, int worldZ)
        => _mask[CellOf(WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference), _period, _gridW, _gridH)];

    /// <summary>
    /// The ground of a column after the valleys: <paramref name="natural"/> eased down toward the nearest water, never
    /// raised. The lowest answer over every sample in reach wins, so where two valleys meet the deeper one shapes the land —
    /// and since each sample's answer is continuous in the distance, so is the minimum. The caller keeps the banks sealed
    /// and the land above the sea (<see cref="WorldGenerator"/>'s valley stage).
    /// </summary>
    public int Lower(int worldX, int worldZ, int natural)
    {
        int x = WorldConstants.WrapX(worldX, _circumference);
        int z = WorldConstants.WrapZ(worldZ, _circumference);
        int cell = CellOf(x, z, _period, _gridW, _gridH);
        if (!_mask[cell])
        {
            return natural;
        }

        int gx = cell % _gridW, gz = cell / _gridW;
        double best = natural;
        for (int dz = -_cellReach; dz <= _cellReach; dz++)
        {
            int cz = ((gz + dz) % _gridH + _gridH) % _gridH;
            for (int dx = -_cellReach; dx <= _cellReach; dx++)
            {
                int c = cz * _gridW + ((gx + dx) % _gridW + _gridW) % _gridW;
                for (int i = _start[c]; i < _start[c + 1]; i++)
                {
                    double lowered = LoweredBy(i, x, z, natural);
                    if (lowered < best)
                    {
                        best = lowered;
                    }
                }
            }
        }

        // Ceiling: the ground never sinks below what the nearest sample allows, whatever the rounding.
        return best >= natural ? natural : (int)System.Math.Ceiling(best - 1e-9);
    }

    /// <summary>One sample's answer for a column: the natural height where the column lies beyond the sample's reach or
    /// already below what it allows, otherwise cut toward the allowed height (all of the way inside the reach's inner 70 %,
    /// fading out over the rest).</summary>
    private double LoweredBy(int i, int x, int z, int natural)
    {
        int ddx = WorldConstants.WrapDeltaX(_x[i] - x, _circumference);
        int ddz = WorldConstants.WrapDeltaZ(_z[i] - z, _circumference);
        int k = _class[i];
        double reach = _reach[k];
        long d2 = (long)ddx * ddx + (long)ddz * ddz;
        if (d2 > reach * reach)
        {
            return natural;
        }

        double d = System.Math.Sqrt(d2);
        double rise = d <= _floor[k] ? 0.0 : (d - _floor[k]) * RiseNum[k] / RiseDen[k];
        double cap = _water[i] + rise;
        if (natural <= cap)
        {
            return natural;
        }

        double fadeFrom = reach * (1.0 - FadeShare);
        double w = d <= fadeFrom ? 1.0 : (reach - d) / (reach - fadeFrom);
        return natural - System.Math.Min(_maxCut[k], (natural - cap) * w * _depth);
    }
}

/// <summary>
/// Terrain generation 40 (#3561): how far every coarse cell of a world lies from fresh water and from the coast, on the
/// river network's own 16-block grid — two multi-source breadth-first passes over the torus at the field build, so the
/// placement search asks without sampling anything. Distances count cells (4-neighbour steps); a query answers in blocks.
/// </summary>
public sealed class RiverGeography
{
    /// <summary>The answer for "none on this world".</summary>
    public const int None = int.MaxValue;

    private const ushort Unreached = ushort.MaxValue;

    private readonly ushort[] _fresh;
    private readonly ushort[] _coast;
    private readonly int _cell;
    private readonly int _gridW;
    private readonly int _gridH;
    private readonly int _circumference;
    private readonly int _period;

    private RiverGeography(ushort[] fresh, ushort[] coast, int cell, int gridW, int gridH, int circumference, int period)
    {
        _fresh = fresh;
        _coast = coast;
        _cell = cell;
        _gridW = gridW;
        _gridH = gridH;
        _circumference = circumference;
        _period = period;
    }

    /// <summary>Builds both layers from the network's grid and the field's stamped surface columns (the fresh water a player
    /// sees — a capped basin the rasteriser left dry is no source).</summary>
    internal static RiverGeography Build(RiverNetwork net, Dictionary<(int X, int Z), RiverField.RiverColumn> cols, int circumference)
    {
        int cell = net.CellSize, gridW = net.GridW, gridH = net.GridH, period = net.LatitudePeriod;
        int n = gridW * gridH;
        var freshSources = new List<int>();
        var seen = new bool[n];
        foreach (var kv in cols)
        {
            if (kv.Value.Underground)
            {
                continue;
            }

            int c = CellOf(kv.Key.X, kv.Key.Z, cell, gridW, gridH, period);
            if (!seen[c])
            {
                seen[c] = true;
                freshSources.Add(c);
            }
        }

        freshSources.Sort();
        var coastSources = new List<int>();
        for (int c = 0; c < n; c++)
        {
            if (net.IsSea[c])
            {
                coastSources.Add(c);
            }
        }

        return new RiverGeography(Spread(freshSources, gridW, gridH), Spread(coastSources, gridW, gridH), cell, gridW, gridH,
            circumference, period);
    }

    private static int CellOf(int x, int z, int cell, int gridW, int gridH, int period)
    {
        int gx = WorldConstants.WrapX(x, gridW * cell) / cell;
        if (gx >= gridW) gx = gridW - 1;
        int zc = ((z + period / 2) % period + period) % period;
        int gz = zc / cell;
        if (gz >= gridH) gz = gridH - 1;
        return gz * gridW + gx;
    }

    /// <summary>A multi-source breadth-first pass over the wrapped grid: each cell's 4-neighbour step count to the nearest
    /// source, <see cref="Unreached"/> without any source.</summary>
    private static ushort[] Spread(List<int> sources, int gridW, int gridH)
    {
        int n = gridW * gridH;
        var dist = new ushort[n];
        for (int i = 0; i < n; i++)
        {
            dist[i] = Unreached;
        }

        var queue = new Queue<int>(sources.Count);
        foreach (int s in sources)
        {
            dist[s] = 0;
            queue.Enqueue(s);
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int gx = c % gridW, gz = c / gridW;
            ushort next = (ushort)System.Math.Min(Unreached - 1, dist[c] + 1);
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = gx, nz = gz;
                switch (dir)
                {
                    case 0: nx = gx + 1 == gridW ? 0 : gx + 1; break;
                    case 1: nx = gx == 0 ? gridW - 1 : gx - 1; break;
                    case 2: nz = gz + 1 == gridH ? 0 : gz + 1; break;
                    default: nz = gz == 0 ? gridH - 1 : gz - 1; break;
                }

                int ni = nz * gridW + nx;
                if (dist[ni] == Unreached)
                {
                    dist[ni] = next;
                    queue.Enqueue(ni);
                }
            }
        }

        return dist;
    }

    /// <summary>Blocks from a column to the nearest fresh water (river or lake), at cell resolution; <see cref="None"/> when
    /// the world has none.</summary>
    public int FreshWaterBlocks(int worldX, int worldZ) => Blocks(_fresh, worldX, worldZ);

    /// <summary>Blocks from a column to the nearest sea cell, at cell resolution; <see cref="None"/> without a sea.</summary>
    public int CoastBlocks(int worldX, int worldZ) => Blocks(_coast, worldX, worldZ);

    private int Blocks(ushort[] layer, int worldX, int worldZ)
    {
        ushort d = layer[CellOf(WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference), _cell, _gridW,
            _gridH, _period)];
        return d == Unreached ? None : d * _cell;
    }

    /// <summary>The grid the layers live on (the layer export).</summary>
    public (int Width, int Height, int Cell) Grid => (_gridW, _gridH, _cell);
}

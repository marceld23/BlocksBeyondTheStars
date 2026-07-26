// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A deterministic, gefälle-aware river network traced over a coarse heightfield of the whole (toroidal)
/// world (see <c>docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md</c>). Wired into
/// <see cref="WorldGenerator.Generate"/> via <c>RiverFieldFor</c> since Phase 1 — its only world coupling
/// stays an injected height sampler + sea level, so it remains standalone-testable, and the core runs in
/// INTEGER math (terrain heights are ints) so there is no floating-point drift server vs. client.
/// <para>
/// Algorithm: a Barnes "Priority-Flood" depression fill from the ocean cells yields, for every cell,
/// (a) a filled water level (cells filled above their terrain are lakes — the user-approved
/// fill-and-spill mechanism) and (b) a drainage direction toward the sea. Deterministically scattered
/// upland sources then accumulate flow down that drainage tree; cells above a flow threshold are
/// channels, and a channel step whose terrain drops more than <c>WaterfallMinDrop</c> is a waterfall.
/// </para>
/// </summary>
public sealed class RiverNetwork
{
    /// <summary>Coarse grid cell size in blocks (routing resolution; refined to block resolution later).</summary>
    public int CellSize { get; }

    /// <summary>Grid width (east–west cells, wraps) and height (north–south cells, wraps on the torus).</summary>
    public int GridW { get; }
    public int GridH { get; }

    public int SeaLevel { get; }

    /// <summary>Sampled terrain height per coarse cell (index = gz*GridW + gx).</summary>
    public int[] Height { get; }

    /// <summary>Priority-flood water level per cell (≥ <see cref="Height"/>); strictly greater ⇒ a lake cell.</summary>
    public int[] FilledLevel { get; }

    /// <summary>Downstream neighbour cell index per cell, or −1 for an ocean outlet (drains off-map into sea).</summary>
    public int[] FlowDir { get; }

    /// <summary>Accumulated number of upstream sources draining through each cell (river "size").</summary>
    public int[] FlowAccum { get; }

    /// <summary>True where terrain is at/below sea level (an ocean sink).</summary>
    public bool[] IsSea { get; }

    /// <summary>Cells carrying a real channel (<see cref="FlowAccum"/> ≥ the channel threshold).</summary>
    public IReadOnlyList<int> ChannelCells { get; }

    /// <summary>A waterfall step: a channel cell whose terrain drops &gt; <c>WaterfallMinDrop</c> into its downstream cell.</summary>
    public readonly struct Waterfall
    {
        public readonly int Cell;
        public readonly int DownstreamCell;
        public readonly int DropBlocks;
        public Waterfall(int cell, int downstream, int drop) { Cell = cell; DownstreamCell = downstream; DropBlocks = drop; }
    }

    public IReadOnlyList<Waterfall> Waterfalls { get; }

    /// <summary>Count of cells filled above their terrain by the depression fill — self-formed lakes.</summary>
    public int LakeCellCount { get; }

    /// <summary>Number of source cells seeded.</summary>
    public int SourceCount { get; }

    private RiverNetwork(int cellSize, int gridW, int gridH, int latitudePeriod, int seaLevel,
        int[] height, int[] filled, int[] flowDir, int[] flowAccum, bool[] isSea,
        IReadOnlyList<int> channels, IReadOnlyList<Waterfall> waterfalls, int lakeCells, int sources)
    {
        CellSize = cellSize; GridW = gridW; GridH = gridH; LatitudePeriod = latitudePeriod; SeaLevel = seaLevel;
        Height = height; FilledLevel = filled; FlowDir = flowDir; FlowAccum = flowAccum; IsSea = isSea;
        ChannelCells = channels; Waterfalls = waterfalls; LakeCellCount = lakeCells; SourceCount = sources;
    }

    /// <summary>Index helper (no wrap).</summary>
    public int Index(int gx, int gz) => gz * GridW + gx;

    /// <summary>The latitude (Z) wrap period this network was built for — needed to map cells back to world Z.</summary>
    public int LatitudePeriod { get; }

    /// <summary>World-space centre of a coarse cell (the same mapping <see cref="Build"/> sampled at).</summary>
    public void CellWorld(int cell, out int worldX, out int worldZ)
    {
        int gx = cell % GridW, gz = cell / GridW;
        worldX = gx * CellSize + CellSize / 2;
        worldZ = gz * CellSize - LatitudePeriod / 2 + CellSize / 2;
    }

    /// <summary>Walks the drainage tree from a cell to its outlet; true if it ends in the sea (−1 outlet
    /// or an ocean cell). The Phase-0 connectivity invariant: every channel cell must satisfy this.</summary>
    public bool ReachesSea(int cell)
    {
        int guard = GridW * GridH + 1;
        int c = cell;
        while (guard-- > 0)
        {
            if (IsSea[c]) return true;
            int d = FlowDir[c];
            if (d < 0) return true; // ocean outlet
            c = d;
        }

        return false; // cycle / unreachable (should never happen on a connected torus with an ocean)
    }

    /// <summary>Builds the network. <paramref name="height"/> maps (worldX, worldZ) → terrain surface Y
    /// (e.g. <see cref="WorldGenerator.SurfaceHeight"/>). Pure + deterministic for fixed inputs.</summary>
    public static RiverNetwork Build(
        long seed,
        int circumference,
        int latitudePeriod,
        int seaLevel,
        System.Func<int, int, int> height,
        int cellSize = 8,
        int sourceCount = 64,
        double upperHeightFraction = 0.45,
        int channelThreshold = 1,
        int waterfallMinDrop = 4)
    {
        if (cellSize < 1) cellSize = 1;
        int gridW = System.Math.Max(1, circumference / cellSize);
        int gridH = System.Math.Max(1, latitudePeriod / cellSize);
        int n = gridW * gridH;
        int half = latitudePeriod / 2;

        var h = new int[n];
        var isSea = new bool[n];
        for (int gz = 0; gz < gridH; gz++)
        {
            int worldZ = gz * cellSize - half + cellSize / 2; // centre of the cell in latitude domain
            for (int gx = 0; gx < gridW; gx++)
            {
                int worldX = gx * cellSize + cellSize / 2;
                int v = height(worldX, worldZ);
                int idx = gz * gridW + gx;
                h[idx] = v;
                isSea[idx] = v <= seaLevel;
            }
        }

        var filled = new int[n];
        var flowDir = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { filled[i] = 0; flowDir[i] = -1; }

        var heap = new MinHeap(n);

        // Seed the flood with the ocean. If a world has no cell at/below sea level (rare on a wet world
        // but possible on a small sample), fall back to the single lowest cell as the outlet so the whole
        // map still drains somewhere — no river can then dead-end on dry ground.
        bool anySea = false;
        for (int i = 0; i < n; i++)
        {
            if (isSea[i]) { closed[i] = true; filled[i] = h[i]; flowDir[i] = -1; heap.Push(h[i], i); anySea = true; }
        }

        if (!anySea)
        {
            int lo = 0;
            for (int i = 1; i < n; i++) if (h[i] < h[lo]) lo = i;
            closed[lo] = true; filled[lo] = h[lo]; flowDir[lo] = -1; heap.Push(h[lo], lo);
        }

        while (heap.Count > 0)
        {
            int c = heap.Pop();
            int cgx = c % gridW, cgz = c / gridW;
            for (int dir = 0; dir < 4; dir++)
            {
                int ngx = cgx, ngz = cgz;
                switch (dir)
                {
                    case 0: ngx = cgx + 1 == gridW ? 0 : cgx + 1; break;       // east (wrap)
                    case 1: ngx = cgx == 0 ? gridW - 1 : cgx - 1; break;       // west (wrap)
                    case 2: ngz = cgz + 1 == gridH ? 0 : cgz + 1; break;       // south (wrap)
                    default: ngz = cgz == 0 ? gridH - 1 : cgz - 1; break;      // north (wrap)
                }

                int ni = ngz * gridW + ngx;
                if (closed[ni]) continue;
                closed[ni] = true;
                filled[ni] = h[ni] > filled[c] ? h[ni] : filled[c]; // spill level along the lowest path to the sea
                flowDir[ni] = c;                                     // drains toward the cell we came from
                heap.Push(filled[ni], ni);
            }
        }

        int lakeCells = 0;
        for (int i = 0; i < n; i++) if (filled[i] > h[i]) lakeCells++;

        // Deterministic upland sources: among the higher, non-sea cells, take the `sourceCount` with the
        // lowest hash — a stable, float-free scatter. The height bar is a percentile of the integer heights.
        int bar = HeightPercentile(h, isSea, upperHeightFraction);
        var eligible = new List<int>();
        for (int i = 0; i < n; i++) if (!isSea[i] && h[i] >= bar) eligible.Add(i);
        eligible.Sort((a, b) =>
        {
            ulong ha = Hash(seed, a), hb = Hash(seed, b);
            return ha < hb ? -1 : ha > hb ? 1 : a.CompareTo(b);
        });
        int sources = System.Math.Min(sourceCount, eligible.Count);

        // Flow accumulation: walk each source down the drainage tree to the sea, tallying every cell it crosses.
        var flowAccum = new int[n];
        for (int s = 0; s < sources; s++)
        {
            int c = eligible[s];
            int guard = n + 1;
            while (guard-- > 0)
            {
                flowAccum[c]++;
                if (isSea[c]) break;
                int d = flowDir[c];
                if (d < 0) break;
                c = d;
            }
        }

        var channels = new List<int>();
        var waterfalls = new List<Waterfall>();
        for (int i = 0; i < n; i++)
        {
            if (isSea[i] || flowAccum[i] < channelThreshold) continue;
            channels.Add(i);
            int d = flowDir[i];
            if (d >= 0)
            {
                int drop = h[i] - h[d];
                if (drop > waterfallMinDrop) waterfalls.Add(new Waterfall(i, d, drop));
            }
        }

        return new RiverNetwork(cellSize, gridW, gridH, latitudePeriod, seaLevel, h, filled, flowDir, flowAccum, isSea,
            channels, waterfalls, lakeCells, sources);
    }

    /// <summary>The integer height at the given fraction up the sorted non-sea heights (0 = lowest land,
    /// 1 = highest). Used as the "upland source" bar. Pure integer + sort ⇒ deterministic.</summary>
    private static int HeightPercentile(int[] h, bool[] isSea, double fraction)
    {
        var land = new List<int>();
        for (int i = 0; i < h.Length; i++) if (!isSea[i]) land.Add(h[i]);
        if (land.Count == 0) return int.MaxValue;
        land.Sort();
        int k = (int)(fraction * (land.Count - 1));
        if (k < 0) k = 0; if (k >= land.Count) k = land.Count - 1;
        return land[k];
    }

    /// <summary>SplitMix64-style integer hash of (seed, cell) — deterministic scatter, no floats.</summary>
    private static ulong Hash(long seed, int cell)
    {
        ulong x = (ulong)seed + 0x9E3779B97F4A7C15UL * (ulong)(uint)cell + 0x632BE59BD9B4E019UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    /// <summary>A tiny binary min-heap over (level, cell) with a deterministic tie-break on the cell index
    /// (so the flood order — and thus the whole network — never depends on insertion timing).</summary>
    private sealed class MinHeap
    {
        private int[] _level;
        private int[] _cell;
        private int _count;

        public MinHeap(int capacity)
        {
            capacity = capacity < 16 ? 16 : capacity;
            _level = new int[capacity];
            _cell = new int[capacity];
        }

        public int Count => _count;

        public void Push(int level, int cell)
        {
            if (_count == _level.Length)
            {
                System.Array.Resize(ref _level, _count * 2);
                System.Array.Resize(ref _cell, _count * 2);
            }

            int i = _count++;
            _level[i] = level; _cell[i] = cell;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (Less(i, p)) { Swap(i, p); i = p; } else break;
            }
        }

        public int Pop()
        {
            int top = _cell[0];
            _count--;
            if (_count > 0)
            {
                _level[0] = _level[_count]; _cell[0] = _cell[_count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, m = i;
                    if (l < _count && Less(l, m)) m = l;
                    if (r < _count && Less(r, m)) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }

            return top;
        }

        private bool Less(int a, int b)
            => _level[a] < _level[b] || (_level[a] == _level[b] && _cell[a] < _cell[b]);

        private void Swap(int a, int b)
        {
            (_level[a], _level[b]) = (_level[b], _level[a]);
            (_cell[a], _cell[b]) = (_cell[b], _cell[a]);
        }
    }
}

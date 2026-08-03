// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Block-resolution river placement, rasterized once per world from the coarse <see cref="RiverNetwork"/>.
/// Where the network says "a channel of this size flows through here," this stamps the actual block columns
/// the river covers, each carrying a water-surface height, a carved bed, a flow axis, and — at a steep step —
/// a waterfall drop. <see cref="WorldGenerator.Generate"/> and the shared surface-water queries do an O(1)
/// lookup here instead of the old noise band, so a river follows the terrain down to a guaranteed sink.
/// <para>
/// Design (Phase 1, see <c>docs/developer/RIVER_ROUTING_AND_WATERFALLS_PLAN.md</c>):
/// the water surface FOLLOWS the terrain on a flowing reach (a thin sheet, so no tall "floating water wall"
/// on a slope), rises to the filled level inside a shallow capped basin (a pool/lake), and where the terrain
/// drops more than <c>WaterfallMinDrop</c> over one step the column is tagged with that drop so Generate can
/// pour a vertical waterfall column into the lower reach. Deep flood basins (the over-flooding the Phase-0
/// spike found) are capped: anything deeper than <c>MaxLakeDepth</c> is treated as a thin reach, not a lake.
/// </para>
/// Deterministic inputs + integer state (the interpolation uses <c>Math.Round(double)</c>, whose IEEE-754
/// result is fully specified) ⇒ identical on server and client.
/// </summary>
public sealed class RiverField
{
    public readonly struct RiverColumn
    {
        /// <summary>Topmost water cell Y (inclusive).</summary>
        public readonly int WaterSurfaceY;
        /// <summary>Carved channel bed Y (last solid cell below the water).</summary>
        public readonly int BedY;
        /// <summary>0 = none; &gt;0 = a vertical waterfall column of this many blocks pours into this column.</summary>
        public readonly int WaterfallDrop;
        /// <summary>0 = flow runs along X, 1 = along Z (feeds the surface-water flow classification).</summary>
        public readonly byte FlowAxis;

        public RiverColumn(int surface, int bed, int waterfallDrop, byte flowAxis)
        {
            WaterSurfaceY = surface; BedY = bed; WaterfallDrop = waterfallDrop; FlowAxis = flowAxis;
        }
    }

    private readonly Dictionary<(int X, int Z), RiverColumn> _cols;
    private readonly Dictionary<(int X, int Z), int> _lakeShore;
    private readonly int _circumference;

    public int ColumnCount => _cols.Count;
    public int WaterfallColumnCount { get; }

    /// <summary>Dry columns ringing a LARGE lake's pooled water (inspection / tests).</summary>
    public int LakeShoreColumnCount => _lakeShore.Count;

    /// <summary>The fluid this field fills its channels with — water on watery worlds, lava on volcanic ones.
    /// Generate reads it so one routing path serves both (L2). Air on an empty field.</summary>
    public BlockId FillFluid { get; }

    /// <summary>All stamped columns (inspection / tests).</summary>
    public IReadOnlyCollection<RiverColumn> Columns => _cols.Values;

    private RiverField(Dictionary<(int, int), RiverColumn> cols, Dictionary<(int, int), int> lakeShore,
        int circumference, int waterfalls, BlockId fillFluid)
    {
        _cols = cols; _lakeShore = lakeShore; _circumference = circumference;
        WaterfallColumnCount = waterfalls; FillFluid = fillFluid;
    }

    /// <summary>An empty field (dry / no-river worlds) — every lookup misses.</summary>
    public static RiverField Empty(int circumference)
        => new(new Dictionary<(int, int), RiverColumn>(), new Dictionary<(int, int), int>(), circumference, 0, default);

    /// <summary>O(1) lookup: is (worldX, worldZ) a river column, and with what surface/bed/waterfall? Wraps X.</summary>
    public bool TryGet(int worldX, int worldZ, out RiverColumn col)
        => _cols.TryGetValue((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)), out col);

    /// <summary>O(1) lookup: is (worldX, worldZ) a dry column on the shore ring of a LARGE lake — a pooled
    /// reach whose lake gathered at least the build's minimum of visible water columns? Returns the lake's
    /// flat water level so the caller can band-test a beach against it (#679). Wraps X/Z like TryGet.</summary>
    public bool TryGetLakeShore(int worldX, int worldZ, out int waterLevel)
        => _lakeShore.TryGetValue((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)), out waterLevel);

    public static RiverField Build(
        RiverNetwork net,
        System.Func<int, int, int> height,
        int circumference,
        BlockId fillFluid = default,
        int channelFlowThreshold = 1,
        int maxWidth = 7,
        int fullWidthAccum = 8,
        int waterfallMinDrop = 4,
        int maxLakeDepth = 6,
        int estuaryWiden = 3,
        int lakeShoreWidth = 3,
        int minLakeShoreColumns = 64)
    {
        var cols = new Dictionary<(int, int), RiverColumn>();
        // Pooled (flat-lake) columns and the coarse cell that set their level — the lake-shore pass below
        // rings these with dry shore markers (#679). Keyed like `cols` so the two lookups agree.
        var pooledCols = new Dictionary<(int X, int Z), int>();
        int period = net.LatitudePeriod;
        int cell = net.CellSize;
        int gridW = net.GridW, gridH = net.GridH;
        int waterfalls = 0;

        // Width is RELATIVE to this world's largest flow (#474): the old absolute divisor made width
        // depend on the source count, leaving sparse worlds with 1-block threads everywhere (and every
        // lava channel at width 1). fullWidthAccum is the floor of "full width" so a world whose flows
        // never merge doesn't promote every brook to a trunk river (lava passes 1 deliberately).
        int maxAccum = fullWidthAccum;
        foreach (int cc in net.ChannelCells)
        {
            if (net.FlowAccum[cc] > maxAccum)
            {
                maxAccum = net.FlowAccum[cc];
            }
        }

        // Cells the coarse network flagged as a real cascade step (#475): the rasterizer used to re-derive
        // drops at block resolution against the same 16-block threshold, which no FBM terrain ever met —
        // the network's own result was computed and thrown away, so natural waterfalls never fired.
        var fallCells = new HashSet<int>();
        foreach (var wf in net.Waterfalls)
        {
            fallCells.Add(wf.Cell);
            fallCells.Add(wf.DownstreamCell);
        }

        // Coarse cell containing a world column (wrapped).
        int CellOf(int wx, int wz)
        {
            int cgx = WorldConstants.WrapX(wx, circumference) / cell;
            if (cgx >= gridW) cgx = gridW - 1;
            int zc = ((wz + period / 2) % period + period) % period;
            int cgz = zc / cell;
            if (cgz >= gridH) cgz = gridH - 1;
            return cgz * gridW + cgx;
        }

        void Stamp(int wx, int wz, int surface, int bed, int waterfallDrop, byte axis)
        {
            var key = (WorldConstants.WrapX(wx, circumference), WorldConstants.WrapZ(wz, circumference));
            if (cols.TryGetValue(key, out var existing))
            {
                // Where two channel strokes overlap, keep the lower (more-downstream) water surface so the
                // confluence never lifts water above a reach that already ran lower through here.
                if (existing.WaterSurfaceY <= surface)
                {
                    if (waterfallDrop > 0 && existing.WaterfallDrop == 0)
                    {
                        cols[key] = new RiverColumn(existing.WaterSurfaceY, existing.BedY, waterfallDrop, existing.FlowAxis);
                        waterfalls++;
                    }

                    return;
                }

                if (existing.WaterfallDrop > 0) waterfalls--; // the replaced column was a waterfall
            }

            if (waterfallDrop > 0) waterfalls++;
            cols[key] = new RiverColumn(surface, bed, waterfallDrop, axis);
        }

        foreach (int c in net.ChannelCells)
        {
            if (net.FlowAccum[c] < channelFlowThreshold) continue;
            int d = net.FlowDir[c];
            if (d < 0) continue; // ocean outlet — the sea takes over here

            net.CellWorld(c, out int cx, out int cz);
            net.CellWorld(d, out int dx, out int dz);
            int ddx = WorldConstants.WrapDeltaX(dx - cx, circumference);
            int ddz = WorldConstants.WrapDeltaZ(dz - cz, circumference);
            int steps = System.Math.Max(System.Math.Abs(ddx), System.Math.Abs(ddz));
            if (steps == 0) steps = 1;

            byte axis = (byte)(System.Math.Abs(ddx) >= System.Math.Abs(ddz) ? 0 : 1);
            // Width grows with the upstream flow RELATIVE to the world's biggest trunk (#474): a headwater
            // brook is 1 wide, a gathered trunk approaches maxWidth. At the sea mouth an estuary flares.
            double rel = System.Math.Min(1.0, net.FlowAccum[c] / (double)maxAccum);
            int width = 1 + (int)System.Math.Floor((maxWidth - 1) * System.Math.Sqrt(rel));
            if (net.IsSea[d]) width = System.Math.Min(width + estuaryWiden, maxWidth + estuaryWiden);
            int half = width / 2;

            int prevTerrain = height(cx, cz);
            for (int s = 0; s <= steps; s++)
            {
                int wx = cx + (int)System.Math.Round((double)ddx * s / steps);
                int wz = cz + (int)System.Math.Round((double)ddz * s / steps);
                int terrain = height(wx, wz);

                int cellIdx = CellOf(wx, wz);
                int poolDepth = net.FilledLevel[cellIdx] - net.Height[cellIdx];
                bool pooled = poolDepth > 0 && poolDepth <= maxLakeDepth;

                int surface, bed;
                if (pooled)
                {
                    surface = net.FilledLevel[cellIdx]; // flat pool surface
                    bed = net.Height[cellIdx] - 1;
                }
                else
                {
                    surface = terrain;                  // thin sheet following the ground (no floating wall)
                    // Depth decoupled from a width-3 gate (#474): brooks are 1 deep, anything that has
                    // gathered flow runs 2, trunks 3 — deep enough that a river is swimmable, not wadable.
                    bed = terrain - (width >= 4 ? 3 : width >= 2 ? 2 : 1);
                }

                // Waterfall (#475): inside a network-flagged cascade cell a 3-block sheer step fires; far
                // from one it still takes the old 5-block cliff, so gentle slopes never sprout water pillars.
                int drop = prevTerrain - terrain;
                int minDrop = fallCells.Contains(cellIdx) ? 3 : waterfallMinDrop + 1;
                int waterfallDrop = drop >= minDrop ? drop : 0;
                prevTerrain = terrain;

                // Centerline + perpendicular band (flat cross-section at the centerline's surface).
                for (int o = -half; o <= half; o++)
                {
                    int sx = axis == 0 ? wx : wx + o;
                    int sz = axis == 0 ? wz + o : wz;
                    Stamp(sx, sz, surface, bed, o == 0 ? waterfallDrop : 0, axis);
                    if (pooled)
                    {
                        pooledCols[(WorldConstants.WrapX(sx, circumference), WorldConstants.WrapZ(sz, circumference))] = cellIdx;
                    }
                }
            }
        }

        var lakeShore = BuildLakeShores(net, height, circumference, cols, pooledCols, lakeShoreWidth, minLakeShoreColumns);
        return new RiverField(cols, lakeShore, circumference, waterfalls, fillFluid);
    }

    /// <summary>
    /// Lake shores (#679): labels each pooled reach's lake — connected coarse cells sharing one filled
    /// level (one basin fills to one spill level, so equality + adjacency IS the basin) — and, for lakes
    /// whose visible pooled water gathered at least <paramref name="minLakeShoreColumns"/> columns, rings
    /// the water with dry shore markers wherever the terrain sits just above the pool.
    /// <see cref="WorldGenerator"/> turns those into beach columns; small pools and plain flowing reaches
    /// get none. Only the lake's EDGE columns pay the terrain lookups, so the pass costs ~perimeter.
    /// </summary>
    private static Dictionary<(int, int), int> BuildLakeShores(
        RiverNetwork net,
        System.Func<int, int, int> height,
        int circumference,
        Dictionary<(int, int), RiverColumn> cols,
        Dictionary<(int X, int Z), int> pooledCols,
        int lakeShoreWidth,
        int minLakeShoreColumns)
    {
        var lakeShore = new Dictionary<(int, int), int>();
        if (lakeShoreWidth <= 0 || pooledCols.Count == 0)
        {
            return lakeShore;
        }

        int gridW = net.GridW, gridH = net.GridH;
        var ndx = new[] { 1, -1, 0, 0 };
        var ndz = new[] { 0, 0, 1, -1 };

        // Flood-label the lake component containing `start` (memoized), returning its root cell.
        var root = new Dictionary<int, int>();
        int RootOf(int start)
        {
            if (root.TryGetValue(start, out int known))
            {
                return known;
            }

            int level = net.FilledLevel[start];
            var comp = new List<int>();
            var queue = new Queue<int>();
            var seen = new HashSet<int> { start };
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                comp.Add(c);
                int gx = c % gridW, gz = c / gridW;
                for (int n = 0; n < 4; n++)
                {
                    int nx = (gx + ndx[n] + gridW) % gridW;
                    int nz = (gz + ndz[n] + gridH) % gridH;
                    int nc = nz * gridW + nx;
                    if (!seen.Contains(nc) && net.FilledLevel[nc] > net.Height[nc] && net.FilledLevel[nc] == level)
                    {
                        seen.Add(nc);
                        queue.Enqueue(nc);
                    }
                }
            }

            foreach (int c in comp)
            {
                root[c] = start;
            }

            return start;
        }

        // Visible size per lake = how many pooled water columns the strokes actually stamped for it —
        // the basin's cell count would overstate lakes the channels barely touch.
        var visibleColumns = new Dictionary<int, int>();
        foreach (var kv in pooledCols)
        {
            int r = RootOf(kv.Value);
            visibleColumns[r] = visibleColumns.TryGetValue(r, out int n) ? n + 1 : 1;
        }

        foreach (var kv in pooledCols)
        {
            if (visibleColumns[RootOf(kv.Value)] < minLakeShoreColumns)
            {
                continue; // small pool — no beach ring
            }

            var (px, pz) = kv.Key;
            bool edge = !cols.ContainsKey((WorldConstants.WrapX(px + 1, circumference), pz))
                || !cols.ContainsKey((WorldConstants.WrapX(px - 1, circumference), pz))
                || !cols.ContainsKey((px, WorldConstants.WrapZ(pz + 1, circumference)))
                || !cols.ContainsKey((px, WorldConstants.WrapZ(pz - 1, circumference)));
            if (!edge)
            {
                continue; // interior water — only the lake's rim rings shore markers
            }

            int lakeLevel = net.FilledLevel[kv.Value];
            for (int dx = -lakeShoreWidth; dx <= lakeShoreWidth; dx++)
                for (int dz = -lakeShoreWidth; dz <= lakeShoreWidth; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    var target = (WorldConstants.WrapX(px + dx, circumference), WorldConstants.WrapZ(pz + dz, circumference));
                    if (cols.ContainsKey(target))
                    {
                        continue; // water column, not shore
                    }

                    if (lakeShore.TryGetValue(target, out int prev) && prev <= lakeLevel)
                    {
                        continue; // already marked against an equal/lower pool — keep the lower waterline
                    }

                    int terrain = height(px + dx, pz + dz);
                    if (terrain >= lakeLevel && terrain <= lakeLevel + 3)
                    {
                        lakeShore[target] = lakeLevel;
                    }
                }
        }

        return lakeShore;
    }
}

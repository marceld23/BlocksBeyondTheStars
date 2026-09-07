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
        /// <summary>Carved channel bed Y (last solid cell below the water). Equal to <see cref="WaterSurfaceY"/>
        /// on an underground BANK column: the walkable ledge beside the water carries no water of its own.</summary>
        public readonly int BedY;
        /// <summary>0 = none; &gt;0 = a vertical waterfall column of this many blocks pours into this column.</summary>
        public readonly int WaterfallDrop;
        /// <summary>0 = flow runs along X, 1 = along Z (feeds the surface-water flow classification).</summary>
        public readonly byte FlowAxis;
        /// <summary>Terrain generation 3: the reach runs UNDERGROUND here. The terrain surface is untouched —
        /// instead a passage is carved from the bed up to <see cref="RoofY"/> with the water on its floor.
        /// False on every classic column, so generation 0–2 worlds never see one.</summary>
        public readonly bool Underground;
        /// <summary>The passage roof of an underground reach — the last carved (air) cell. Unused when
        /// <see cref="Underground"/> is false. Like the water surface it is the CENTERLINE's value for the
        /// whole cross-section, so on a slope it can sit above a band column's own ground; the column phase
        /// keeps the carve under the surface unless <see cref="Mouth"/> says otherwise.</summary>
        public readonly int RoofY;
        /// <summary>The column where the roof closes over the reach (or last opens): its shaft runs all the way
        /// through the surface — the swallow hole the river vanishes into, and the spring it comes back out of.</summary>
        public readonly bool Mouth;

        public RiverColumn(int surface, int bed, int waterfallDrop, byte flowAxis)
            : this(surface, bed, waterfallDrop, flowAxis, false, 0, false)
        {
        }

        public RiverColumn(int surface, int bed, int waterfallDrop, byte flowAxis, bool underground, int roofY, bool mouth)
        {
            WaterSurfaceY = surface; BedY = bed; WaterfallDrop = waterfallDrop; FlowAxis = flowAxis;
            Underground = underground; RoofY = roofY; Mouth = mouth;
        }

        /// <summary>True on an underground bank column: air from the waterline up to the roof, no water.</summary>
        public bool IsBank => Underground && BedY == WaterSurfaceY;
    }

    /// <summary>The least rock a passage roof must carry before a reach counts as underground (generation 3) —
    /// below it the ramp is still an open channel, so a diving river cuts in gradually.</summary>
    private const int SunkMinCover = 3;

    private readonly Dictionary<(int X, int Z), RiverColumn> _cols;
    private readonly Dictionary<(int X, int Z), int> _lakeShore;
    private readonly Dictionary<(int X, int Z), int> _pooled;
    private readonly HashSet<(int X, int Z)> _floodplain;
    private readonly int _circumference;

    public int ColumnCount => _cols.Count;
    public int WaterfallColumnCount { get; }

    /// <summary>Dry columns ringing a LARGE lake's pooled water (inspection / tests).</summary>
    public int LakeShoreColumnCount => _lakeShore.Count;

    /// <summary>Dry columns flagged as a trunk river's floodplain (terrain generation 3; always 0 on a classic field).</summary>
    public int FloodplainColumnCount => _floodplain.Count;

    /// <summary>Columns the delta fans and oxbow pools added on top of the classic strokes (tests).</summary>
    public int MorphologyColumnCount { get; }

    /// <summary>Whether this field was built with any river-morphology parameter on (tests).</summary>
    public bool Morphology { get; private set; }

    /// <summary>Strokes whose downstream cell is the sea, and strokes on a trunk reach (inspection / tests).</summary>
    public int OutletStrokeCount { get; private set; }
    public int TrunkStrokeCount { get; private set; }

    /// <summary>O(1): a pooled (flat-lake) water column, with the coarse cell whose filled level set it. Wraps X/Z.</summary>
    public bool TryGetPooled(int worldX, int worldZ, out int lakeCell)
        => _pooled.TryGetValue((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)), out lakeCell);

    /// <summary>O(1): a dry column on a trunk reach's floodplain (terrain generation 3) — the column phase paints
    /// it mud and floods a share of them one deep. Never true on a classic field. Wraps X/Z.</summary>
    public bool IsFloodplain(int worldX, int worldZ)
        => _floodplain.Contains((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)));

    /// <summary>The fluid this field fills its channels with — water on watery worlds, lava on volcanic ones.
    /// Generate reads it so one routing path serves both (L2). Air on an empty field.</summary>
    public BlockId FillFluid { get; }

    /// <summary>All stamped columns (inspection / tests).</summary>
    public IReadOnlyCollection<RiverColumn> Columns => _cols.Values;

    /// <summary>All stamped columns keyed by wrapped world (X, Z) (inspection / tests) — lets a test walk the
    /// river columns directly instead of scanning the whole world for them (#1067).</summary>
    public IReadOnlyDictionary<(int X, int Z), RiverColumn> ColumnsByPosition => _cols;

    private RiverField(Dictionary<(int, int), RiverColumn> cols, Dictionary<(int, int), int> lakeShore,
        Dictionary<(int, int), int> pooled, HashSet<(int, int)> floodplain, int morphologyColumns,
        int circumference, int waterfalls, BlockId fillFluid)
    {
        _cols = cols; _lakeShore = lakeShore; _pooled = pooled; _floodplain = floodplain; _circumference = circumference;
        MorphologyColumnCount = morphologyColumns; WaterfallColumnCount = waterfalls; FillFluid = fillFluid;
    }

    /// <summary>An empty field (dry / no-river worlds) — every lookup misses.</summary>
    public static RiverField Empty(int circumference)
        => new(new Dictionary<(int, int), RiverColumn>(), new Dictionary<(int, int), int>(),
            new Dictionary<(int, int), int>(), new HashSet<(int, int)>(), 0, circumference, 0, default);

    /// <summary>O(1) lookup: is (worldX, worldZ) a river column, and with what surface/bed/waterfall? Wraps X.</summary>
    public bool TryGet(int worldX, int worldZ, out RiverColumn col)
        => _cols.TryGetValue((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)), out col);

    /// <summary>O(1) lookup: is (worldX, worldZ) a dry column on the shore ring of a LARGE lake — a pooled
    /// reach whose lake gathered at least the build's minimum of visible water columns? Returns the lake's
    /// flat water level so the caller can band-test a beach against it (#679). Wraps X/Z like TryGet.</summary>
    public bool TryGetLakeShore(int worldX, int worldZ, out int waterLevel)
        => _lakeShore.TryGetValue((WorldConstants.WrapX(worldX, _circumference), WorldConstants.WrapZ(worldZ, _circumference)), out waterLevel);

    /// <summary>Rasterises the coarse network into block columns.
    /// <paramref name="sunkRegion"/> (terrain generation 3, null = the classic surface-only rasterisation) is a
    /// coarse-cell predicate: where BOTH ends of a stroke lie inside it the reach runs underground, where one
    /// end does the cover ramps over the stroke, and the column where the roof first closes (or last opens) is
    /// left as an open shaft — the swallow hole and the spring. <paramref name="sunkCover"/> is the rock kept
    /// above the passage roof, <paramref name="sunkHeadroom"/> the air between the water and that roof.
    /// <para>River morphology (terrain generation 3, every parameter's default is the classic no-op):
    /// <paramref name="sinuosity"/> bends a low-gradient stroke into one S per coarse cell (amplitude
    /// sinuosity × width × 3, capped at what stays inside the drainage cell row) and leaves an oxbow pool at
    /// a quarter of the apexes; <paramref name="distributaries"/> fans that many extra half-width strokes out
    /// of a trunk's sea outlet (a delta); <paramref name="floodplainWidth"/> flags the dry columns that many
    /// blocks either side of a trunk reach and within one block of its water as floodplain.</para></summary>
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
        int minLakeShoreColumns = 64,
        System.Func<int, int, bool>? sunkRegion = null,
        int sunkCover = 12,
        int sunkHeadroom = 3,
        double sinuosity = 0.0,
        int distributaries = 0,
        int floodplainWidth = 0)
    {
        var cols = new Dictionary<(int, int), RiverColumn>();
        var floodplain = new HashSet<(int, int)>();
        int morphologyColumns = 0, outletStrokes = 0, trunkStrokes = 0;
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

        void Stamp(int wx, int wz, int surface, int bed, int waterfallDrop, byte axis,
            bool underground = false, int roofY = 0, bool mouth = false)
        {
            var key = (WorldConstants.WrapX(wx, circumference), WorldConstants.WrapZ(wz, circumference));
            if (cols.TryGetValue(key, out var existing))
            {
                // Generation 3, before the classic rules (both tests are always false on a classic world, so
                // nothing below this point changes for generation 0–2).
                // 1) A SURFACE reach always beats an underground one, whatever the levels: the visible river
                //    must never be replaced by the tunnel of a channel that happens to pass under it.
                if (existing.Underground != underground)
                {
                    if (!existing.Underground)
                    {
                        return;
                    }

                    if (existing.WaterfallDrop > 0) waterfalls--;
                    if (waterfallDrop > 0) waterfalls++;
                    cols[key] = new RiverColumn(surface, bed, waterfallDrop, axis, underground, roofY, mouth);
                    return;
                }

                // 2) A BANK column (walkable ledge, no water) never displaces one that carries water.
                bool existingBank = existing.BedY == existing.WaterSurfaceY;
                bool newBank = bed == surface;
                if (existingBank != newBank)
                {
                    if (newBank)
                    {
                        return;
                    }

                    if (existing.WaterfallDrop > 0) waterfalls--;
                    if (waterfallDrop > 0) waterfalls++;
                    cols[key] = new RiverColumn(surface, bed, waterfallDrop, axis, underground, roofY, mouth);
                    return;
                }

                // Where two channel strokes overlap, keep the lower (more-downstream) water surface so the
                // confluence never lifts water above a reach that already ran lower through here.
                if (existing.WaterSurfaceY <= surface)
                {
                    if (waterfallDrop > 0 && existing.WaterfallDrop == 0)
                    {
                        cols[key] = new RiverColumn(existing.WaterSurfaceY, existing.BedY, waterfallDrop,
                            existing.FlowAxis, existing.Underground, existing.RoofY, existing.Mouth);
                        waterfalls++;
                    }

                    return;
                }

                if (existing.WaterfallDrop > 0) waterfalls--; // the replaced column was a waterfall
            }

            if (waterfallDrop > 0) waterfalls++;
            cols[key] = new RiverColumn(surface, bed, waterfallDrop, axis, underground, roofY, mouth);
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

            // Underground reaches (terrain generation 3): a stroke whose BOTH ends lie in the soluble-rock
            // region runs under a full rock roof; a stroke with one end inside ramps the cover over its
            // length, so the river dives in (or comes back out) instead of stepping into a wall. Sea cells
            // never sink — the estuary has to reach the water. `sunkRegion` is null on every classic world,
            // and then RampCover is 0 everywhere and nothing below changes.
            bool sunkC = sunkRegion != null && !net.IsSea[c] && sunkRegion(cx, cz);
            bool sunkD = sunkRegion != null && !net.IsSea[d] && sunkRegion(dx, dz);
            int RampCover(int step)
            {
                if (!sunkC && !sunkD)
                {
                    return 0;
                }

                double t = step / (double)steps;
                double f = sunkC && sunkD ? 1.0 : sunkD ? t : 1.0 - t;
                int cv = (int)System.Math.Round(sunkCover * f);
                return cv >= SunkMinCover ? cv : 0;
            }

            // Meanders (generation 3): a low-gradient surface stroke bends into one S between its cell centres —
            // the offset is perpendicular to the stroke, zero at both ends (so consecutive strokes stay joined)
            // and at the middle, and never larger than what keeps the band inside the drainage cell row. The
            // terrain is sampled at the OFFSET column, so the water still follows the ground. Zero on every
            // classic build (sinuosity 0), and then nothing below this comment changes.
            int meanderAmp = 0;
            int meanderSign = 1;
            bool oxbow = false;
            if (sinuosity > 0.0 && !sunkC && !sunkD && !net.IsSea[d] && steps >= 8
                && System.Math.Abs(height(cx, cz) - height(dx, dz)) <= 1)
            {
                int cap = cell / 2 - half - 1;
                double want = sinuosity * width * 3.0;
                meanderAmp = (int)System.Math.Round(want < cap ? want : cap);
                ulong mh = Noise.Hash(0x5EA0AD, c, 0, d);
                meanderSign = (mh & 1UL) != 0 ? 1 : -1;
                oxbow = meanderAmp >= 3 && ((mh >> 4) & 3UL) == 0;
            }

            int MeanderOffset(int step)
            {
                if (meanderAmp == 0)
                {
                    return 0;
                }

                double u = step / (double)steps;
                double wave = 16.0 * u * (1.0 - u) * (0.5 - u) / 0.7698; // one S, |wave| ≤ 1 (peaks at u ≈ 0.21 / 0.79), zero at 0, ½, 1
                return (int)System.Math.Round(meanderAmp * wave) * meanderSign;
            }

            int prevTerrain = height(cx, cz);
            // Every surface reach gets a floodplain; a reach that has gathered two brooks or more (FlowAccum counts
            // the SOURCES upstream — on a default world most rivers never merge at all) gets the full width, a
            // lone brook half of it. An absolute bar, not one relative to the world's largest river, which would
            // leave every other river without.
            bool gathered = net.FlowAccum[c] >= 2;
            bool trunk = floodplainWidth > 0 && !sunkC && !sunkD;
            int plainWidth = gathered ? floodplainWidth : System.Math.Max(1, floodplainWidth / 2);
            if (net.IsSea[d]) outletStrokes++;
            if (gathered) trunkStrokes++;
            for (int s = 0; s <= steps; s++)
            {
                int off = MeanderOffset(s);
                int wx = cx + (int)System.Math.Round((double)ddx * s / steps) + (axis == 1 ? off : 0);
                int wz = cz + (int)System.Math.Round((double)ddz * s / steps) + (axis == 0 ? off : 0);
                int terrain = height(wx, wz);

                int cellIdx = CellOf(wx, wz);
                int poolDepth = net.FilledLevel[cellIdx] - net.Height[cellIdx];
                bool pooled = poolDepth > 0 && poolDepth <= maxLakeDepth;

                // Depth decoupled from a width-3 gate (#474): brooks are 1 deep, anything that has
                // gathered flow runs 2, trunks 3 — deep enough that a river is swimmable, not wadable.
                int channelDepth = width >= 4 ? 3 : width >= 2 ? 2 : 1;

                // A pooled reach is a lake: it always stays on the surface, whatever the region says.
                int cover = pooled ? 0 : RampCover(s);
                bool underground = cover > 0;
                // The column where the roof first closes (diving) or last opens (rising) keeps an open shaft
                // up to the ground: the swallow hole the river vanishes into, and the spring it returns from.
                bool mouth = underground
                    && ((s > 0 && RampCover(s - 1) == 0) || (s < steps && RampCover(s + 1) == 0));

                int surface, bed;
                int roofY = 0;
                if (pooled)
                {
                    surface = net.FilledLevel[cellIdx]; // flat pool surface
                    bed = net.Height[cellIdx] - 1;
                }
                else if (underground)
                {
                    // The passage hangs a constant cover under the terrain, so the water still descends
                    // exactly as the ground does — the network's downhill guarantee carries over unchanged.
                    // The terrain it hangs under is the LOWEST ground across the cross-section and its bank
                    // ring (part 5): a centerline on a karst pinnacle with the floor forty below beside it
                    // would otherwise put the band's water above its neighbours' ground — an open hillside.
                    int ground = terrain;
                    for (int o = -half - 1; o <= half + 1; o++)
                    {
                        int gx = axis == 0 ? wx : wx + o;
                        int gz = axis == 0 ? wz + o : wz;
                        int g = height(gx, gz);
                        if (g < ground) ground = g;
                    }

                    roofY = mouth ? terrain : ground - cover;
                    surface = ground - cover - sunkHeadroom;
                    bed = surface - channelDepth;
                }
                else
                {
                    surface = terrain;                  // thin sheet following the ground (no floating wall)
                    bed = terrain - channelDepth;
                }

                // Waterfall (#475): inside a network-flagged cascade cell a 3-block sheer step fires; far
                // from one it still takes the old 5-block cliff, so gentle slopes never sprout water pillars.
                int drop = prevTerrain - terrain;
                int minDrop = fallCells.Contains(cellIdx) ? 3 : waterfallMinDrop + 1;
                int waterfallDrop = !underground && drop >= minDrop ? drop : 0;
                prevTerrain = terrain;

                // Centerline + perpendicular band (flat cross-section at the centerline's surface).
                for (int o = -half; o <= half; o++)
                {
                    int sx = axis == 0 ? wx : wx + o;
                    int sz = axis == 0 ? wz + o : wz;
                    Stamp(sx, sz, surface, bed, o == 0 ? waterfallDrop : 0, axis, underground, roofY, mouth);
                    if (pooled)
                    {
                        pooledCols[(WorldConstants.WrapX(sx, circumference), WorldConstants.WrapZ(sz, circumference))] = cellIdx;
                    }
                }

                // Floodplain (generation 3): the dry ground either side of a trunk reach, where it lies within a
                // block of the water — the column phase paints it mud and floods a share of it one deep.
                if (trunk && !pooled && !underground)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        for (int o = half + 1; o <= half + plainWidth; o++)
                        {
                            int fx = axis == 0 ? wx : wx + side * o;
                            int fz = axis == 0 ? wz + side * o : wz;
                            if (System.Math.Abs(height(fx, fz) - surface) <= 2)
                            {
                                floodplain.Add((WorldConstants.WrapX(fx, circumference), WorldConstants.WrapZ(fz, circumference)));
                            }
                        }
                    }
                }

                // Oxbow (generation 3): at the meander's apex a quarter of the bends leave a cut-off pool on the
                // outer side — a still crescent of 1-deep water two to four blocks beyond the channel, on ground
                // level with the reach. Stamped like any reach column; the precedence rules above apply.
                if (oxbow && !pooled && !underground && System.Math.Abs(off) == meanderAmp)
                {
                    for (int o = half + 2; o <= half + 4; o++)
                    {
                        int ox = axis == 0 ? wx : wx + meanderSign * o;
                        int oz = axis == 0 ? wz + meanderSign * o : wz;
                        int ot = height(ox, oz);
                        if (System.Math.Abs(ot - surface) <= 1)
                        {
                            Stamp(ox, oz, ot, ot - 1, 0, axis);
                            morphologyColumns++;
                        }
                    }
                }

                // Banks (generation 3): a walkable ledge around an underground channel — solid up to the
                // waterline, air from there to the roof. That is also what SEALS the water sideways: every
                // 4-neighbour of a water column at the water's own height is rock (a bank is shielded from the
                // cave carver) or the passage itself, never an unshielded column a cave may have opened. A
                // ring rather than two flanks, because a diagonal stroke has water neighbours off its axis too.
                // Not at a mouth, where the shaft should stay as narrow as the channel itself.
                if (underground && !mouth)
                {
                    for (int o = -half; o <= half; o++)
                    {
                        int sx = axis == 0 ? wx : wx + o;
                        int sz = axis == 0 ? wz + o : wz;
                        Stamp(sx + 1, sz, surface, surface, 0, axis, underground: true, roofY);
                        Stamp(sx - 1, sz, surface, surface, 0, axis, underground: true, roofY);
                        Stamp(sx, sz + 1, surface, surface, 0, axis, underground: true, roofY);
                        Stamp(sx, sz - 1, surface, surface, 0, axis, underground: true, roofY);
                    }
                }
            }

            // Deltas (generation 3): at a trunk's sea outlet 2–4 extra half-width strokes fan out ±27–45° for one
            // or two cells, their beds a single block deep, and the ground between them is floodplain. Rotation
            // is a hash-drawn (6, ±k) unit vector — trig-free. Zero strokes on every classic build.
            if (distributaries > 0 && net.IsSea[d])
            {
                ulong fh = Noise.Hash(0xDE17A, c, 0, d);
                int fans = System.Math.Min(distributaries, gathered ? 2 + (int)(fh % 3UL) : 2); // a lone brook forks in two
                double len0 = System.Math.Sqrt((double)(ddx * ddx + ddz * ddz));
                int fanWidth = System.Math.Max(1, width / 2);
                int fanHalf = fanWidth / 2;
                for (int k = 0; k < fans; k++)
                {
                    int side = (k & 1) == 0 ? 1 : -1;
                    int kk = 3 + (int)((fh >> (8 + k * 4)) & 3UL); // 3..6 → 27°..45°
                    double rl = System.Math.Sqrt(36.0 + kk * kk);
                    double rc = 6.0 / rl, rs = side * kk / rl;
                    double ux = ddx / len0, uz = ddz / len0;
                    double fx = ux * rc - uz * rs, fz = ux * rs + uz * rc;
                    int fanSteps = steps * (1 + (int)((fh >> (20 + k)) & 1UL));
                    byte fanAxis = (byte)(System.Math.Abs(fx) >= System.Math.Abs(fz) ? 0 : 1);
                    for (int s = 0; s <= fanSteps; s++)
                    {
                        int wx = cx + (int)System.Math.Round(fx * s);
                        int wz = cz + (int)System.Math.Round(fz * s);
                        int terrain = height(wx, wz);
                        for (int o = -fanHalf; o <= fanHalf; o++)
                        {
                            int sx = fanAxis == 0 ? wx : wx + o;
                            int sz = fanAxis == 0 ? wz + o : wz;
                            Stamp(sx, sz, terrain, terrain - 1, 0, fanAxis);
                            morphologyColumns++;
                        }

                        for (int side2 = -1; side2 <= 1; side2 += 2)
                        {
                            for (int o = fanHalf + 1; o <= fanHalf + 3; o++)
                            {
                                int px = fanAxis == 0 ? wx : wx + side2 * o;
                                int pz = fanAxis == 0 ? wz + side2 * o : wz;
                                if (System.Math.Abs(height(px, pz) - terrain) <= 1)
                                {
                                    floodplain.Add((WorldConstants.WrapX(px, circumference), WorldConstants.WrapZ(pz, circumference)));
                                }
                            }
                        }
                    }
                }
            }
        }

        // A floodplain column is DRY ground: what a later stroke turned into a river column leaves the set.
        floodplain.RemoveWhere(cols.ContainsKey);

        var lakeShore = BuildLakeShores(net, height, circumference, cols, pooledCols, lakeShoreWidth, minLakeShoreColumns);
        return new RiverField(cols, lakeShore, pooledCols, floodplain, morphologyColumns, circumference, waterfalls, fillFluid)
        {
            Morphology = sinuosity > 0.0 || distributaries > 0 || floodplainWidth > 0,
            OutletStrokeCount = outletStrokes,
            TrunkStrokeCount = trunkStrokes,
        };
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

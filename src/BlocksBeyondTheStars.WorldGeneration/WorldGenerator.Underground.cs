// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>caverns, tunnels, interior variety, ore slots and samplers (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    // --- Underground mega-caverns (#707): rare ellipsoid voids 60–140 wide deep below the surface,
    // some holding a still lake (water above the lava table, molten below). Crystal glints stud the
    // shell so the find reads as a wonder, not a glitch. ---
    private const double CavernCellSize = 1700.0;
    private const double CavernChance = 0.22;
    private const double CavernMaxRx = 70.0;

    private bool HasCaverns(PlanetType planet)
        => !planet.Void && planet.CaveThreshold > 0.0;

    /// <summary>The mega-cavern's vertical air span at this column (#707), with the lake surface (or
    /// int.MinValue when this cavern rolled dry). False when no cavern covers the column.</summary>
    public bool TryGetCavernSpan(PlanetType planet, int worldX, int worldZ,
        out int yLo, out int yHi, out int lakeY)
    {
        yLo = yHi = 0;
        lakeY = int.MinValue;
        var w = WonderFor(planet); // #712
        if (!w.Caverns)
        {
            return false;
        }

        long seed = w.Seed;
        if (!TryGetHotspot(seed ^ 0x0CAFE27A, CavernCellSize, CavernChance, CavernMaxRx + 10.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double rx = 30.0 + ((h >> 16) & 0x3FF) / 1023.0 * (CavernMaxRx - 30.0); // 30..70
        double rz = rx * (0.75 + ((h >> 26) & 0xFF) / 255.0 * 0.5);             // slightly oval
        double ry = 14.0 + ((h >> 34) & 0x3FF) / 1023.0 * 14.0;                 // 14..28 tall
        double q = 1.0 - (dx / rx) * (dx / rx) - (dz / rz) * (dz / rz);
        if (q <= 0.0)
        {
            return false;
        }

        int cy = planet.BaseHeight - 90 - (int)((h >> 44) & 0x3F); // 90..153 below base
        double half = ry * System.Math.Sqrt(q);
        yLo = cy - (int)half;
        yHi = cy + (int)half;
        if (((h >> 50) & 0x1) != 0)
        {
            lakeY = cy - (int)(ry * (0.35 + ((h >> 52) & 0x3) * 0.05)); // lake fills the lower bowl
        }

        return true;
    }

    // ================= Tunnel carver (#708) =================
    // One deterministic worm carver: each hotspot cell may host a seeded polyline whose capsule radius
    // varies along its length. Carved during the vertical fill like the blob caves — but tunnels may
    // reach the surface, so caves finally have MOUTHS (#709). On volcano worlds a share of the worms
    // roll wide and smooth: lava tubes, with occasional skylight shafts where the roof thins.
    private const double TunnelCellSize = 700.0;
    private const double TunnelChance = 0.5;
    private const double TunnelMargin = 320.0;
    private const int TunnelMaxSpans = 6;

    private bool HasTunnels(PlanetType planet)
        => !planet.Void && planet.CaveThreshold > 0.0;

    /// <summary>One capsule segment of a tunnel worm (#708), in hotspot-cell-local X/Z (Y absolute).</summary>
    private readonly struct TunnelSeg
    {
        public readonly double X0, Y0, Z0, X1, Y1, Z1, R;

        public TunnelSeg(double x0, double y0, double z0, double x1, double y1, double z1, double r)
        {
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1; R = r;
        }
    }

    // #712 perf: the worm polyline is a pure function of the CELL hash, yet it was rebuilt (dozens of
    // xorshift rolls + clamps) for EVERY COLUMN of every tunnel-bearing cell — half the map. Cached per
    // cell (static dict + a lock-free single-slot instance fast path; a chunk's 256 columns share one
    // cell), so per column only the cheap distance checks remain. Output is bit-identical: the segment
    // stream never depended on the queried column.
    private static readonly System.Collections.Generic.Dictionary<(long, ulong), TunnelSeg[]> _tunnelSegCache = new();
    private static readonly object _tunnelSegLock = new object();
    private (long, ulong) _tunnelSegKey;
    private TunnelSeg[]? _tunnelSegs;

    private TunnelSeg[] TunnelSegmentsFor(PlanetType planet, WonderProfile w, ulong h)
    {
        var key = (w.Seed, h);
        if (_tunnelSegs is { } fast && _tunnelSegKey == key)
        {
            return fast;
        }

        lock (_tunnelSegLock)
        {
            if (!_tunnelSegCache.TryGetValue(key, out var segsArr))
            {
                // Build the worm from its cell hash — an xorshift stream keeps the rolls cheap + portable.
                ulong s = h | 1UL;
                double Next()
                {
                    s ^= s << 13;
                    s ^= s >> 7;
                    s ^= s << 17;
                    return (s & 0xFFFFF) / 1048576.0;
                }

                bool tube = w.Volcanoes && Next() < 0.3; // lava tubes: wider, smoother, shallower (#709)
                int segs = tube ? 5 + (int)(Next() * 4) : 6 + (int)(Next() * 7);
                double px = 0.0, pz = 0.0;
                double py = planet.BaseHeight - (tube ? 4.0 + Next() * 10.0 : 8.0 + Next() * 26.0);
                double vx = Next() * 2.0 - 1.0, vz = Next() * 2.0 - 1.0;
                double vlen = System.Math.Sqrt(vx * vx + vz * vz);
                if (vlen < 0.2) { vx = 1.0; vz = 0.0; vlen = 1.0; }
                vx /= vlen;
                vz /= vlen;

                var list = new System.Collections.Generic.List<TunnelSeg>(segs * 2);
                for (int i = 0; i < segs; i++)
                {
                    double len = 24.0 + Next() * 20.0;
                    double drift = tube ? 0.15 : 0.35;
                    double vy = (Next() - 0.55) * drift; // worms trend gently downward
                    double qx = px + vx * len, qz = pz + vz * len, qy = py + vy * len;
                    double lim = TunnelMargin - 24.0;
                    qx = System.Math.Clamp(qx, -lim, lim);
                    qz = System.Math.Clamp(qz, -lim, lim);
                    double radius = tube ? 4.5 + Next() * 2.0 : 2.5 + Next() * 2.0;
                    list.Add(new TunnelSeg(px, py, pz, qx, qy, qz, radius));

                    // Skylight shaft (#709): sometimes the roof opens to the sky — a thin vertical worm
                    // from the segment end up past the surface, so tunnels get real MOUTHS. (0.22 since
                    // the 2026-08-03 visibility tuning: mouths are how players FIND the tunnels.)
                    if (Next() < 0.22)
                    {
                        list.Add(new TunnelSeg(qx, qy, qz, qx, planet.BaseHeight + 60.0, qz, 1.7));
                    }

                    px = qx; py = qy; pz = qz;
                    double turn = (Next() - 0.5) * 0.9;
                    double nvx = vx + turn * -vz, nvz = vz + turn * vx; // cheap heading drift, no trig
                    double nl = System.Math.Sqrt(nvx * nvx + nvz * nvz);
                    vx = nvx / nl;
                    vz = nvz / nl;
                }

                segsArr = list.ToArray();
                if (_tunnelSegCache.Count >= 512)
                {
                    _tunnelSegCache.Clear(); // soft cap — a few hundred bytes per cell
                }

                _tunnelSegCache[key] = segsArr;
            }

            _tunnelSegs = segsArr;
            _tunnelSegKey = key;
            return segsArr;
        }
    }

    /// <summary>Computes this column's tunnel-carve y-spans (#708) into <paramref name="spans"/> and
    /// returns the count. Deterministic per (seed, column); the cell's worm polyline comes from the
    /// per-cell cache (#712), so per column only capsule distance checks run.</summary>
    public int TunnelSpans(PlanetType planet, int worldX, int worldZ, System.Span<(int Lo, int Hi)> spans)
    {
        var w = WonderFor(planet); // #712
        if (!w.Tunnels)
        {
            return 0;
        }

        if (!TryGetHotspot(w.Seed ^ 0x7A22E1, TunnelCellSize, TunnelChance, TunnelMargin,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0;
        }

        var segs = TunnelSegmentsFor(planet, w, h);
        int n = 0;
        for (int i = 0; i < segs.Length && n < spans.Length; i++)
        {
            ref readonly var sg = ref segs[i];
            AddSegmentSpan(sg.X0, sg.Y0, sg.Z0, sg.X1, sg.Y1, sg.Z1, sg.R, dx, dz, spans, ref n);
        }

        return n;
    }

    /// <summary>Adds the y-span where the capsule (p→q, radius r) covers the column at local offset
    /// (dx, dz) — sampled at 8 points along the segment (the worm is organic; exactness buys nothing).</summary>
    private static void AddSegmentSpan(double pxx, double pyy, double pzz, double qx, double qy, double qz,
        double r, double dx, double dz, System.Span<(int Lo, int Hi)> spans, ref int n)
    {
        // #712 perf: XZ bounding reject before the 8-point sampling — most columns of a tunnel cell are
        // nowhere near any given segment. Pure shortcut, identical output.
        if (dx < System.Math.Min(pxx, qx) - r || dx > System.Math.Max(pxx, qx) + r
            || dz < System.Math.Min(pzz, qz) - r || dz > System.Math.Max(pzz, qz) + r)
        {
            return;
        }

        double lo = double.MaxValue, hi = double.MinValue;
        for (int k = 0; k <= 8; k++)
        {
            double t = k / 8.0;
            double sx = pxx + (qx - pxx) * t;
            double sz = pzz + (qz - pzz) * t;
            double d2 = (dx - sx) * (dx - sx) + (dz - sz) * (dz - sz);
            if (d2 > r * r)
            {
                continue;
            }

            double sy = pyy + (qy - pyy) * t;
            double dy = System.Math.Sqrt(r * r - d2);
            if (sy - dy < lo) { lo = sy - dy; }
            if (sy + dy > hi) { hi = sy + dy; }
        }

        if (hi < lo || n >= spans.Length)
        {
            return;
        }

        spans[n++] = ((int)System.Math.Floor(lo), (int)System.Math.Ceiling(hi));
    }

    // #1527: the two non-ore noise fields of the y-loop and their sampler slots (ore veins follow at 2 + 2i).
    private const int CaveSampler = 0;
    private const int LavaSampler = 1;

    /// <summary>ValueT for one column and field through its lazily built lattice sampler (#1527) — the same
    /// value <see cref="ValueT"/> returns, without the per-voxel trig and corner hashes.</summary>
    private double SampleField(TorusColumnSampler[] samplers, int slot, long seed, int worldX, int worldZ,
        double scaleX, double scaleY, double scaleZ, int worldY)
    {
        ref var s = ref samplers[slot];
        if (!s.Ready)
        {
            s = new TorusColumnSampler(seed, worldX, worldZ, _circumference, LatPeriod, scaleX, scaleY, scaleZ);
        }

        return s.Sample(worldY);
    }

    /// <summary>#1527: a vein's per-chunk invariants — the depth band, the shallow/deep split, the rarity×richness
    /// product (the first factor of the original <c>rarity * richness * depthBonus * scale</c>, so the association
    /// is unchanged), the field seeds and the resolved block.</summary>
    private readonly struct OreSlot
    {
        public readonly int MinDepth, MaxDepth;
        public readonly double Scale, Cap, RarityRichness;
        public readonly bool Shallow;
        public readonly long CoarseSeed, FineSeed;
        public readonly BlockId? Block;

        public OreSlot(int minDepth, int maxDepth, double scale, double cap, double rarityRichness, bool shallow,
            long coarseSeed, long fineSeed, BlockId? block)
        {
            MinDepth = minDepth;
            MaxDepth = maxDepth;
            Scale = scale;
            Cap = cap;
            RarityRichness = rarityRichness;
            Shallow = shallow;
            CoarseSeed = coarseSeed;
            FineSeed = fineSeed;
            Block = block;
        }
    }

    private OreSlot[] BuildOreSlots(PlanetType planet, long seed, double richness)
    {
        var slots = new OreSlot[planet.Ores.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            var ore = planet.Ores[i];
            bool shallow = ore.MinDepth <= 8;
            double rarity = ore.RareTier ? ore.Rarity * _frontierOreBoost : ore.Rarity;
            slots[i] = new OreSlot(ore.MinDepth, ore.MaxDepth, shallow ? 0.30 : 0.15, shallow ? 0.08 : 0.05,
                rarity * richness, shallow, seed + 100 + i * 31, seed + OreFineFieldSalt + i * 31,
                _content.GetBlock(ore.Block)?.NumericId);
        }

        return slots;
    }

    // --- Per-world interior variety (item 21): two worlds of the same TYPE still differ underground — one is
    // honeycombed with caves, the next nearly solid; one is ore-rich, the next lean; and the deep crust turns
    // to dark basalt at a depth that varies per world. All deterministic from the world seed. ---

    // (The old PerWorldCaveThreshold clamp lived here — replaced by the per-world quantile calibration in
    // BuildCalibration (#472): the data threshold now maps to a target carve FRACTION, jittered per world.)

    /// <summary>This world's ore-richness multiplier (1.2×..2.2× the planet's vein rarities) — some worlds are
    /// rich strikes, others lean, so the interior payoff varies even on the same planet type. Raised again from
    /// 0.85×..1.6× (itself up from 0.7×..1.4×) so diggable ore is noticeably more common on every planet type —
    /// new players kept reporting they "couldn't find any" (Severin playtests #1 and #2). The per-ore kept-fraction
    /// is still clamped to 0.95 in <see cref="SelectOre"/>, so even the richest worlds don't flood.</summary>
    private static double PerWorldOreRichness(long seed)
        => 1.2 + (double)((ulong)(seed ^ 0x0670EL) % 1000UL) / 1000.0 * 1.0;

    private static readonly string[] MantleRocks = { "basalt", "deepslate", "granite" };

    /// <summary>Depth below which this world's crust turns to a deep "mantle" rock — basalt, deepslate or granite,
    /// CHOSEN per world — instead of the surface stone, so the interior MATERIAL (not just cave/ore density)
    /// differs from world to world. ~1/4 of worlds keep a plain stone crust to the bottom.
    /// <see cref="int.MaxValue"/> = no mantle on this world.</summary>
    private int PerWorldMantle(long seed, int floorDepth, out BlockId mantleId)
    {
        uint pick = (uint)((ulong)(seed ^ 0x0DEE9L) % 1000UL);
        mantleId = _content.GetBlock(MantleRocks[pick % (uint)MantleRocks.Length])?.NumericId ?? BlockId.Air;
        if (mantleId.IsAir || pick < 250)
        {
            return int.MaxValue; // ~1/4 of worlds: solid stone crust all the way down (no distinct mantle)
        }

        // The mantle starts somewhere in the lower half of the foundation (varies per world).
        int lo = System.Math.Max(40, floorDepth / 2);
        int span = System.Math.Max(1, floorDepth - FloorBandThickness - lo);
        return lo + (int)((ulong)(seed ^ 0x0DA27L) % (ulong)span);
    }

    /// <summary>This column's topsoil thickness — the surface + sub-surface depth before the crust turns to stone.
    /// Instead of the planet's flat <see cref="PlanetType.SurfaceDepth"/> everywhere, a coarse 2D noise rolls it
    /// between 1 and that value, so the stone/ore boundary undulates: in the thin patches ore-bearing stone reaches
    /// within a block or two of the surface (shallow digging is sometimes rewarded), while other patches keep the
    /// full topsoil. Constant over Y (per-column). (Severin/user playtest #2 — "dug 2 blocks, only stone/soil, no ore".)</summary>
    private int VariedSurfaceDepth(PlanetType planet, long seed, int worldX, int worldZ)
    {
        int baseDepth = planet.SurfaceDepth;
        if (baseDepth <= 1)
        {
            return baseDepth;
        }

        double n = ValueT(seed + 5150, worldX, 0.0, worldZ, 18.0, 1.0, 18.0); // 0..1, broad smooth patches
        return 1 + (int)System.Math.Round(n * (baseDepth - 1));
    }

    // Two-scale veins (#1024): the sprinkle field's block scale and its seed offset. The offset keeps the
    // fine fields (salt + i*31) clear of the coarse fields (100 + i*31) and every other worldgen salt.
    private const double OreFineScale = 4.5;
    private const long OreFineFieldSalt = 61000;

    private BlockId SelectOre(WorldCalibration calib, OreSlot[] ores, TorusColumnSampler[] samplers,
        int x, int z, int y, int depth, BlockId fallback)
    {
        for (int i = 0; i < ores.Length; i++)
        {
            var ore = ores[i];
            if (depth < ore.MinDepth || depth > ore.MaxDepth)
            {
                continue;
            }

            double depthBonus = 1.0 + 0.6 * System.Math.Min(1.0, depth / 600.0);
            double frac = System.Math.Clamp(ore.RarityRichness * depthBonus * ore.Scale, 0.0, ore.Cap);
            if (frac <= 0.0)
            {
                continue;
            }

            bool hit;
            if (ore.Shallow)
            {
                double half = frac * 0.5;
                double coarseThr = calib.OreCdf[(int)((1.0 - half) * (calib.OreCdf.Length - 1))];
                hit = SampleField(samplers, 2 + i * 2, ore.CoarseSeed, x, z, 9.0, 9.0, 9.0, y) > coarseThr;
                if (!hit)
                {
                    double fineThr = calib.OreFineCdf[(int)((1.0 - half) * (calib.OreFineCdf.Length - 1))];
                    hit = SampleField(samplers, 3 + i * 2, ore.FineSeed, x, z, OreFineScale, OreFineScale, OreFineScale, y) > fineThr;
                }
            }
            else
            {
                double threshold = calib.OreCdf[(int)((1.0 - frac) * (calib.OreCdf.Length - 1))];
                hit = SampleField(samplers, 2 + i * 2, ore.CoarseSeed, x, z, 9.0, 9.0, 9.0, y) > threshold;
            }

            if (hit && ore.Block.HasValue)
            {
                return ore.Block.Value; // an unknown vein block falls through to the next vein, as before
            }
        }

        return fallback;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>impact craters of airless bodies: profiles, chains, ejecta rays, floor metal (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    // --- impact-crater field (item 33): seam-safe round basins via an FBM mask (the B7 pond-mask approach),
    // each ringed by a raised ejecta rim. Pure noise → deterministic and wraps across the X seam.
    // The numbers below are the CENTRE of each range; the actual values are rolled per body from its identity
    // salt (#518, see CraterProfileFor), so one rock is a saturated, deeply cratered ruin and the next a
    // near-smooth pebble with a few shallow dishes. ---

    /// <summary>One body's crater character — rolled once per body from its seed and then shared by every
    /// column of that world (#518). Replaces the five global constants that made every airless body look
    /// the same.</summary>
    private readonly struct CraterProfile
    {
        public readonly double Threshold;  // mask above this is inside a crater (lower ⇒ more, larger basins)
        public readonly double Band;       // mask range from the rim (0) to the deepest centre (1)
        public readonly double MaxDepth;   // bowl depth at the centre (blocks)
        public readonly double RimHeight;  // raised ejecta lip at the crater edge (blocks)
        public readonly double RimBand;    // mask range outside the rim where the lip fades back to flat
        public readonly double Flatness;   // how much of the base swell survives between craters (× amplitude)
        public readonly bool ComplexPeaks; // #699: big basins rebound a central peak (rock's own character)
        public readonly bool Terraced;     // #699: bowl walls step down in ring terraces instead of one slope

        public CraterProfile(double threshold, double band, double maxDepth, double rimHeight, double rimBand,
            double flatness, bool complexPeaks, bool terraced)
        {
            Threshold = threshold;
            Band = band;
            MaxDepth = maxDepth;
            RimHeight = rimHeight;
            RimBand = rimBand;
            Flatness = flatness;
            ComplexPeaks = complexPeaks;
            Terraced = terraced;
        }
    }

    // STATIC cache like the world calibration: the profile is a pure function of the body seed, and the
    // client bakes fresh generators per preview texture. Tiny structs, so the cap can be generous.
    private static readonly System.Collections.Generic.Dictionary<long, CraterProfile> _craterProfiles = new();
    private static readonly object _craterProfileLock = new object();

    /// <summary>This body's crater character, rolled from its seed (world seed + planet type + body salt) so
    /// every asteroid and airless moon gets its own relief — and always the same one.</summary>
    private static CraterProfile CraterProfileFor(long seed)
    {
        lock (_craterProfileLock)
        {
            if (_craterProfiles.TryGetValue(seed, out var cached))
            {
                return cached;
            }

            double R(long salt) => Noise.Value01(seed + salt, 17, 31, 53);
            var p = new CraterProfile(
                threshold: 0.66 - 0.14 * R(0x0C1A),   // 0.52 (pounded) .. 0.66 (sparsely pocked)
                band: 0.12 + 0.10 * R(0x0C1B),        // narrow, steep basins .. broad, gentle ones
                maxDepth: 5.0 + 7.0 * R(0x0C1C),      // 5 .. 12 blocks at the centre
                rimHeight: 0.8 + 2.4 * R(0x0C1D),     // barely-there lip .. a sharp ejecta wall
                rimBand: 0.05 + 0.05 * R(0x0C1E),
                flatness: 0.18 + 0.27 * R(0x0C1F),    // billiard-table regolith .. noticeably rolling ground
                complexPeaks: R(0x0C20) < 0.45,       // #699: ~45 % of bodies rebound central peaks
                terraced: R(0x0C21) < 0.40);          // #699: ~40 % step their bowl walls

            if (_craterProfiles.Count >= 512)
            {
                _craterProfiles.Clear(); // soft cap — a few dozen bytes each
            }

            _craterProfiles[seed] = p;
            return p;
        }
    }

    /// <summary>Height offset (blocks) for the impact-crater field at a column: a smooth bowl inside each basin
    /// (deepening toward its centre) ringed by a raised rim, scattered across otherwise-flat ground (item 33).</summary>
    private double CraterCarve(long seed, int worldX, int worldZ, PlanetType planet)
    {
        var p = CraterProfileFor(seed);
        double mask = FbmT(seed + 0x6A17, worldX, worldZ, planet.TerrainScale * 1.7, octaves: 3);
        double d = mask - p.Threshold;
        if (d >= 0.0)
        {
            // Inside the basin: a smooth bowl down to -MaxDepth, with a rim lip right at the edge.
            double t = System.Math.Min(1.0, d / p.Band);
            double s = t * t * (3.0 - 2.0 * t); // smoothstep deepening
            if (p.Terraced && p.MaxDepth >= 7.0)
            {
                // #699: quantise part of the slope into ring terraces (real complex-crater wall slumping).
                double q = System.Math.Round(s * 3.0) / 3.0;
                s = s * 0.45 + q * 0.55;
            }

            double bowl = -p.MaxDepth * s;
            if (p.ComplexPeaks && p.MaxDepth >= 7.0)
            {
                // #699: the far interior of the BIG basins (mask well past the rim band) rebounds a central
                // peak — small dishes never reach this zone, so simple craters stay simple.
                double big = (d - p.Band) / p.Band;
                if (big > 0.0)
                {
                    double pk = System.Math.Min(1.0, big);
                    bowl += p.MaxDepth * 0.55 * (pk * pk * (3.0 - 2.0 * pk));
                }
            }

            double lip = p.RimHeight * System.Math.Max(0.0, 1.0 - t * 4.0); // a lip at the edge, gone a quarter in
            return bowl + lip;
        }

        // Just outside the rim: the raised ejecta lip, peaking at the edge and fading back to flat ground.
        double o = System.Math.Min(1.0, -d / p.RimBand);
        return p.RimHeight * (1.0 - o);
    }

    // --- Crater chains + ejecta rays (#699): a string of aligned secondary bowls marching away from a
    // primary impact, and bright radial rays repainted around the primary. Hotspot-cell features on
    // cratered bodies, layered ON TOP of the primary FBM crater field. Trig-free (integer direction
    // vectors, dot-product ray tests), so golden-hash tests stay portable. ---
    private const double ChainCellSize = 420.0;
    private const double ChainChance = 0.30;
    private const double ChainMargin = 200.0;
    private static readonly int[] ChainDirX = { 1, 0, 1, 1, 2, 1, 2, 1 };
    private static readonly int[] ChainDirZ = { 0, 1, 1, -1, 1, 2, -1, -2 };

    /// <summary>The crater chain's height contribution at a column (#699): 3–6 bowls of shrinking radius
    /// spaced along a rolled direction, each a smoothstep dish with a small rim lip.</summary>
    private double CraterChainCarve(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x0C4A17, ChainCellSize, ChainChance, ChainMargin,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        int count = 3 + (int)((h >> 20) & 0x3);            // 3..6 bowls
        double spacing = 18.0 + ((h >> 24) & 0x7);         // 18..25 apart
        int di = (int)((h >> 28) & 0x7);
        double dirLen = System.Math.Sqrt((double)(ChainDirX[di] * ChainDirX[di] + ChainDirZ[di] * ChainDirZ[di]));
        double ux = ChainDirX[di] / dirLen, uz = ChainDirZ[di] / dirLen;
        double r0 = 9.0 + ((h >> 32) & 0x7);               // primary radius 9..16
        double depth0 = 5.0 + ((h >> 36) & 0x3);           // primary depth 5..8

        double carve = 0.0;
        double radius = r0, depth = depth0;
        for (int i = 0; i < count; i++)
        {
            double cx = ux * spacing * i, cz = uz * spacing * i;
            double bx = dx - cx, bz = dz - cz;
            double dist = System.Math.Sqrt(bx * bx + bz * bz);
            if (dist <= radius)
            {
                double t = 1.0 - dist / radius;
                double s = t * t * (3.0 - 2.0 * t);
                double bowl = -depth * s + 1.0 * System.Math.Max(0.0, 1.0 - t * 4.0); // dish + rim lip
                if (bowl < carve)
                {
                    carve = bowl; // overlapping bowls take the deepest, not the sum
                }
            }

            radius *= 0.82;
            depth *= 0.85;
        }

        return carve;
    }

    /// <summary>True when this column lies on one of the primary crater's bright ejecta rays (#699) —
    /// Generate repaints those cells with the body's deep rock for contrast. Rays are dot-product cones
    /// around 5–8 rolled integer directions, reaching 1.3–4.5 primary radii out.</summary>
    public bool CraterRayAt(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet); // #712
        if (!w.Cratered)
        {
            return false;
        }

        long seed = w.Seed;
        if (!TryGetHotspot(seed ^ 0x0C4A17, ChainCellSize, ChainChance, ChainMargin,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double r0 = 9.0 + ((h >> 32) & 0x7);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist < r0 * 1.3 || dist > r0 * 4.5)
        {
            return false;
        }

        ulong h2 = h * 0x9E3779B97F4A7C15UL;
        int rays = 5 + (int)(h2 & 0x3); // 5..8 rays
        double nx = dx / dist, nz = dz / dist;
        for (int i = 0; i < rays; i++)
        {
            // Each ray's direction comes from its own hash bits — normalized integer-ish vectors, no trig.
            ulong rh = h2 >> (4 + i * 7);
            double rx = ((rh & 0xF) / 15.0) * 2.0 - 1.0;
            double rz = (((rh >> 4) & 0x7) / 7.0) * 2.0 - 1.0;
            double rl = System.Math.Sqrt(rx * rx + rz * rz);
            if (rl < 0.2)
            {
                continue;
            }

            double len = 0.55 + ((rh >> 7) & 0x3) / 3.0 * 0.45; // per-ray reach 55–100 % of the max
            if (dist > r0 * 4.5 * len)
            {
                continue;
            }

            if (nx * (rx / rl) + nz * (rz / rl) > 0.965)
            {
                return true;
            }
        }

        return false;
    }

    // Rare metals exposed as small clumps on deep crater floors — the reward for exploring craters (item 33).
    private const double CraterFloorMinDepth = 4.0;     // only craters at least this deep host metal
    private const double CraterMetalRegion = 0.55;      // per-crater gate: only SOME craters are metal-bearing
    private const double CraterMetalThreshold = 0.58;   // clump mask (within a metal crater) → a few scattered lumps
    private static readonly string[] CraterFloorMetals =
    {
        "titanium_ore", "gold_ore", "platinum_ore", "cobalt_ore", "uranium_ore", "tungsten_ore", "neodymium_ore",
    };

    /// <summary>For a cratered world, the rare-metal block to expose at a surface crater-floor column if this
    /// crater is metal-bearing and a clump roll hits — else null. Only SOME craters carry metal, and then only a
    /// few small clumps on the deeper floor (item 33).</summary>
    private BlockId? CraterFloorMetal(PlanetType planet, long seed, int worldX, int worldZ)
    {
        // "Deep enough to be worth climbing into" is relative to how deep THIS body's craters get (#518) —
        // an absolute 4-block gate would leave a shallow-cratered rock with no exposed metal at all.
        double floorDepth = System.Math.Min(CraterFloorMinDepth, CraterProfileFor(seed).MaxDepth * 0.55);
        if (CraterCarve(seed, worldX, worldZ, planet) > -floorDepth)
        {
            return null; // not a deep crater floor
        }

        // Per-crater gate: a coarse mask (larger than the crater spacing → ~constant within one crater, varying
        // between craters) leaves most craters bare and only some metal-bearing.
        double region = FbmT(seed + 0x51A2, worldX, worldZ, planet.TerrainScale * 3.5, octaves: 2);
        if (region < CraterMetalRegion)
        {
            return null; // this crater holds no metal
        }

        // Within a metal-bearing crater, a small-scale clump mask scatters a few lumps (high freq → tiny clumps).
        double clump = FbmT(seed + 0x51A3, worldX, worldZ, planet.TerrainScale * 0.22, octaves: 2);
        if (clump < CraterMetalThreshold)
        {
            return null;
        }

        int pick = (int)(Noise.Value01(seed + 0x51A4, WorldConstants.WrapX(worldX, _circumference), 5, Wz(worldZ))
                         * CraterFloorMetals.Length);
        if (pick >= CraterFloorMetals.Length)
        {
            pick = CraterFloorMetals.Length - 1;
        }

        return _content.GetBlock(CraterFloorMetals[pick])?.NumericId;
    }
}

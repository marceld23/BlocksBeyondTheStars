// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>per-world calibration: sea percentile, cave/ore quantiles, lava table, altitude climate, ice (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    // --- Per-world calibration (#472/#473/#476): measured once per world instead of hand-tuned constants.
    // The old sea formula guessed against the raw Amplitude while the height function scales it 0.18–1.9×
    // per style, so most watery worlds had NO sea at all and the ocean type drowned 99.99 % of its surface.
    // The old cave/ore thresholds were tuned against the original 3D noise; the torus sampler halved the
    // field's σ and pushed them unreachably far into the tail (caves + ore became corner speckle). Both are
    // the same class of bug — a constant assuming a distribution the field doesn't have — so both are fixed
    // the same way: sample the ACTUAL distribution once per world and place thresholds by quantile. ---
    private sealed class WorldCalibration
    {
        public int SeaLevel = int.MinValue;   // int.MinValue = dry world
        public BlockId SeaFluid;
        public int[] SortedHeights = System.Array.Empty<int>(); // coarse whole-world surface sample, sorted
        public int MinHeight, MaxHeight;
        public int AltLo, AltHi;              // 2nd/98th height percentiles: the altitude-biome span. The
                                              // absolute extremes are landmark summits/rift floors (#578) —
                                              // normalising biomes against those would compress the whole
                                              // ordinary surface into the middle entries.
        public double CaveThreshold;          // quantile-calibrated (0 = caves disabled)
        public double[] OreCdf = System.Array.Empty<double>();  // sorted ore-field samples (empirical CDF)
        public double[] OreFineCdf = System.Array.Empty<double>(); // ditto for the fine sprinkle field (#1024)
        public int LavaTableDepth = int.MaxValue; // cave cells deeper than this fill with lava (#472/#477 L-A)
        public double BaseTemperature;        // planet base + per-world variation (°C) — worldgen-static part
        public double LapsePerBlock;          // °C lost per block above the reference altitude (#476)
        public int TempRefY;                  // reference altitude: sea level, else BaseHeight
    }

    // STATIC cache: the calibration is a pure function of (world seed, planet, circumference, cratered,
    // body salt), so it is safe — and important — to share across generator instances: the client bakes a
    // fresh WorldGenerator per minimap/orbit texture and the tests spin up hundreds, each of which would
    // otherwise re-sample ~17k heights + 2×4096 field points.
    private static readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool), WorldCalibration> _calibs = new();
    private static readonly object _calibLock = new object();
    private static readonly System.Collections.Generic.Queue<(long, string, int, bool, long, bool)> _calibOrder = new();

    private WorldCalibration CalibFor(PlanetType planet)
    {
        var key = (_worldSeed, planet.Key, _circumference, _crateredWorld, _locationSalt, _continentsEnabled);
        lock (_calibLock)
        {
            if (_calibs.TryGetValue(key, out var cached))
            {
                return cached;
            }

            WorldCalibration calib;
            bool outer = !_calibrating;
            _calibrating = true; // #1631: cones sample without the sea-mount lift while the sea is unknown
            try
            {
                calib = BuildCalibration(planet);
            }
            finally
            {
                _calibrating = !outer;
            }

            EvictOldest(_calibs, _calibOrder, 64); // #1527: oldest-out — entries are ~150 KB each
            _calibs[key] = calib;
            _calibOrder.Enqueue(key);
            if (outer)
            {
                InvalidateColumnCaches(); // #1631: drop the un-lifted cone columns memoised during the sample
            }

            return calib;
        }
    }

    private WorldCalibration BuildCalibration(PlanetType planet)
    {
        long seed = PlanetSeed(planet);
        var c = new WorldCalibration();
        double R01(long salt) => (double)((ulong)(seed ^ salt) % 10000UL) / 10000.0;

        // 1) Whole-world height sample (coarse but torus-complete) — the basis for the percentile sea level
        //    and the altitude normalisation. ~17k samples on the default world; cached per world afterwards.
        //    Sampled through the FULL SurfaceHeight (#577/#578): landmark overlays (volcanoes, massifs,
        //    buttes, rifts) count toward MinHeight/MaxHeight, so the snow-possible gate sees a massif's
        //    summit on an otherwise warm world and the sea percentile knows about flooded rift floors.
        int period = LatPeriod;
        int stepX = System.Math.Max(8, _circumference / 188);
        int stepZ = System.Math.Max(8, period / 94);
        var hs = new System.Collections.Generic.List<int>((_circumference / stepX + 1) * (period / stepZ + 1));
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < _circumference; x += stepX)
                hs.Add(SurfaceHeight(planet, x, z));
        hs.Sort();
        c.SortedHeights = hs.ToArray();
        c.MinHeight = c.SortedHeights[0];
        c.MaxHeight = c.SortedHeights[c.SortedHeights.Length - 1];
        c.AltLo = c.SortedHeights[(int)(0.02 * (c.SortedHeights.Length - 1))];
        c.AltHi = c.SortedHeights[(int)(0.98 * (c.SortedHeights.Length - 1))];

        // 2) Sea level by height percentile (#473): waterAbundance now really means "roughly this fraction
        //    of the world floods" — on every terrain style and every drama roll. Ocean-class worlds
        //    (abundance ≥ 1) roll their land fraction per world instead (decision #3): some are near-solid
        //    water, some archipelagos. Water still beats lava; airless worlds stay dry.
        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        double lavaAb = planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0);

        // Continents (#704): on a continental world the sea's job is to FILL THE BASINS — the flood target
        // becomes the basin share (1 − rolled land fraction), so the waterline settles at/near the shelf
        // edge regardless of how the threshold→area mapping behaves. The percentile machinery makes it exact.
        var continent = ContinentProfileFor(planet, seed);
        if (waterAb > 0.0 && _content.GetBlock("water") is { } water)
        {
            double frac = continent.Active
                ? System.Math.Clamp((1.0 - continent.LandFrac) * 0.97, 0.35, 0.75)
                : waterAb >= 1.0
                    ? 0.78 + 0.19 * R01(0x5EA01)   // ocean-class band: 78–97 % water (islands guaranteed)
                    : System.Math.Clamp(0.06 + 0.40 * waterAb + (R01(0x5EA02) - 0.5) * 0.08, 0.02, 0.60);
            c.SeaLevel = QuantileLevel(c.SortedHeights, frac);
            c.SeaFluid = water.NumericId;
        }
        else if (lavaAb > 0.0 && _content.GetBlock("lava") is { } lava)
        {
            // Only dry volcanic/airless worlds pool a lava sea (B54: visible across low + mid terrain).
            // A continental lava world (#704) floods its basins the same way — basalt continents.
            double frac = continent.Active
                ? System.Math.Clamp((1.0 - continent.LandFrac) * 0.97, 0.35, 0.75)
                : System.Math.Clamp(0.30 * lavaAb + (R01(0x5EA03) - 0.5) * 0.06, 0.05, 0.55);
            c.SeaLevel = QuantileLevel(c.SortedHeights, frac);
            c.SeaFluid = lava.NumericId;
        }

        // 3) Cave threshold by field quantile (#472): the data's caveThreshold maps to a target carve
        //    fraction (lower data value = cavier world, as before), jittered per world, then converted to
        //    whatever raw threshold the ACTUAL torus field needs to carve that fraction.
        if (planet.CaveThreshold > 0.0)
        {
            double carve = System.Math.Clamp(0.5 * (0.90 - planet.CaveThreshold), 0.02, 0.18);
            carve = System.Math.Clamp(carve + (R01(0x0CA7E) - 0.5) * 0.06, 0.015, 0.22);
            var caveCdf = FieldSamplesSorted(seed + 7777, 22.0, 16.0, 22.0);
            c.CaveThreshold = caveCdf[(int)((1.0 - carve) * (caveCdf.Length - 1))];
        }

        // 4) Ore field CDF (#472): SelectOre turns each vein's rarity into a quantile of this, so `rarity`
        //    finally IS the kept fraction (the multiplier bumps never fixed this because the knob was broken).
        //    The fine sprinkle field (#1024) gets its own CDF: in theory both fields share one distribution,
        //    but the thresholds sit in the far tail where the 4096-sample quantile is noisy — measuring each
        //    field keeps the kept fraction exact for both.
        c.OreCdf = FieldSamplesSorted(seed + 100, 9.0, 9.0, 9.0, samples: 16384);
        c.OreFineCdf = FieldSamplesSorted(seed + OreFineFieldSalt, OreFineScale, OreFineScale, OreFineScale, samples: 16384);

        // 5) Deep lava table (#472/#477 L-A): carved cave cells below this depth fill with molten rock — the
        //    danger half to the now-reachable deep ore bands. Kept below the cave-fauna scan (surface−49).
        c.LavaTableDepth = 64 + (int)((ulong)(seed ^ 0x1A7AB1EL) % 65UL); // 64..128

        // 6) Altitude climate (#476; survival-relevant since #666): a per-world temperature base + lapse. The
        //    reference altitude is the (repaired) sea level so "warm at the coast, frozen on the peaks".
        c.TempRefY = c.SeaLevel != int.MinValue ? c.SeaLevel : planet.BaseHeight;
        c.BaseTemperature = planet.BaseTemperature + (R01(0x7E3BL) - 0.5) * 12.0; // per-world ±6 °C
        c.LapsePerBlock = 0.5 + 0.3 * R01(0x1A65EL); // 0.5..0.8 °C per block — snow caps land on the
                                                     // upper third of a temperate world's peaks (measured)
        return c;
    }

    /// <summary>The sea level that floods ≈<paramref name="frac"/> of the sampled columns. Integer terrain
    /// heights tie heavily (the base-height plateau, mesa decks, flats), so a naive rank quantile can
    /// overshoot the target by half the world — instead, pick the candidate level whose ACTUAL flooded
    /// fraction P(surface &lt; L) lands closest to the target.</summary>
    private static int QuantileLevel(int[] sortedHeights, double frac)
    {
        int n = sortedHeights.Length;
        double target = System.Math.Clamp(frac, 0.0, 1.0);
        int best = sortedHeights[0]; // floods nothing
        double bestErr = target;
        int i = 0;
        while (i < n)
        {
            int v = sortedHeights[i];
            int j = i;
            while (j < n && sortedHeights[j] == v)
            {
                j++;
            }

            // Candidate level v+1 floods everything ≤ v, i.e. j/n of the sampled world.
            double err = System.Math.Abs((double)j / n - target);
            if (err < bestErr)
            {
                bestErr = err;
                best = v + 1;
            }

            i = j;
        }

        return best;
    }

    /// <summary>Sorted samples of a ValueT field over this world's domain — its empirical CDF. Thresholds
    /// derived from this stay meaningful no matter how many interpolation axes the torus sampler stacks.
    /// Ore thresholds sit at 98.5–99.5+ % quantiles where a 4096-sample estimate drifts a vein's kept
    /// fraction by ±25 % between worlds — those callers pass a larger <paramref name="samples"/> (#1024);
    /// the cave carve fraction (2–22 %) is fine at the default.</summary>
    private double[] FieldSamplesSorted(long fieldSeed, double scaleX, double scaleY, double scaleZ,
        int samples = 4096)
    {
        int N = samples;
        var vals = new double[N];
        int period = LatPeriod;
        for (int i = 0; i < N; i++)
        {
            double u1 = Noise.Value01(fieldSeed ^ 0x5A11, i, 1, 0);
            double u2 = Noise.Value01(fieldSeed ^ 0x5A11, i, 2, 0);
            double u3 = Noise.Value01(fieldSeed ^ 0x5A11, i, 3, 0);
            double x = u1 * _circumference;
            double y = -2100.0 + u2 * 2180.0; // the FULL depth band caves/ore occupy (#580: floors reach ~-1990)
            double z = -period / 2.0 + u3 * period;
            vals[i] = ValueT(fieldSeed, x, y, z, scaleX, scaleY, scaleZ);
        }

        System.Array.Sort(vals);
        return vals;
    }

    /// <summary>Air temperature (°C) at a world Y for this planet — the per-world base minus the altitude
    /// lapse above the reference level (sea level, else BaseHeight). Worldgen-static: the server layers
    /// weather + day/night on top (#476). Since #666 this also feeds the survival temperature hazard
    /// (decision #7 — "temperature stays cosmetic" — was revised by the user on 2026-08-02).</summary>
    public double AirTemperatureAt(PlanetType planet, int worldY)
    {
        var c = CalibFor(planet);
        // long math: int.MinValue is the "no position" sentinel and must not overflow into a hot reading.
        return c.BaseTemperature - c.LapsePerBlock * System.Math.Max(0L, (long)worldY - c.TempRefY);
    }

    /// <summary>Year-round mean the ground settles to a few blocks below the surface — the "dig in to
    /// escape the weather" temperature every world shares (#667).</summary>
    public const double GroundComfortC = 10.0;

    /// <summary>Depth below the generated surface at which the ground temperature fully takes over (#667).</summary>
    public const int GroundComfortDepthBlocks = 24;

    /// <summary>How far underground a position is, 0..1: 0 at/above the generated surface, 1 at
    /// <see cref="GroundComfortDepthBlocks"/>+ below it. The server blends the surface climate toward
    /// <see cref="GroundComfortC"/> by this factor, so caves are milder than an ice world's surface and
    /// cooler than a lava world's — while the deep lava table keeps real heat sources dangerous (#667).
    /// Uses the GENERATED surface height: a player-dug pit still counts as "below the original surface",
    /// which is the intent (their hole IS the shelter).</summary>
    public double UndergroundFactor(PlanetType planet, int worldX, int worldY, int worldZ)
    {
        int depth = SurfaceHeight(planet, worldX, worldZ) - worldY;
        return System.Math.Clamp(depth / (double)GroundComfortDepthBlocks, 0.0, 1.0);
    }

    /// <summary>Surface temperature (°C) of a column — its surface altitude fed through the lapse.</summary>
    public double SurfaceTemperatureAt(PlanetType planet, int worldX, int worldZ)
        => AirTemperatureAt(planet, SurfaceHeight(planet, worldX, worldZ));

    private static double TempAt(WorldCalibration c, int worldY)
        => c.BaseTemperature - c.LapsePerBlock * System.Math.Max(0, worldY - c.TempRefY);

    private const double SnowLineC = 0.0;   // below this surface temperature the ground gets a snow cover
    private const double IceLineC = -14.0;  // …and below this it freezes to solid ice
    private const double TreeLineC = -4.0;  // no trees / giant mushrooms above the tree line
    private const double FloraFadeHiC = 4.0, FloraFadeLoC = -8.0; // flora density ramps to zero across this band

    // Frozen water (#494): below the snow line a water body carries a floating ice sheet that thickens
    // with the cold; below DeepFreezeC at the waterline it freezes through to the seabed. A sheet of
    // LandableIceSheet+ blocks is treated as land by the surface-water queries (ships may land on it).
    private const double DeepFreezeC = -32.0; // waterline temperature below which a body freezes solid
    private const int MaxIceSheet = 4;        // thickest floating sheet on a merely-cold world (blocks)
    private const int LandableIceSheet = 3;   // a sheet this thick counts as land, not water

    /// <summary>Flora density multiplier for the cold: 1 in the warm lowlands, fading to 0 toward the ice.</summary>
    private static double ColdFloraFactor(WorldCalibration c, int surfaceY)
    {
        double t = TempAt(c, surfaceY);
        return System.Math.Clamp((t - FloraFadeLoC) / (FloraFadeHiC - FloraFadeLoC), 0.0, 1.0);
    }

    /// <summary>True when this world can freeze water at all (#494) — the snow pass's gate plus the ice
    /// block itself. A cheap whole-world precheck: if even the highest point stays warm, no column can.</summary>
    private bool CanFreezeWater(PlanetType planet, WorldCalibration calib)
    {
        bool airlessBody = planet.Cratered || _crateredWorld;
        bool hasAtmosphere = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        return hasAtmosphere && !airlessBody
            && !(_content.GetBlock("snow")?.NumericId ?? BlockId.Air).IsAir
            && !(_content.GetBlock("ice")?.NumericId ?? BlockId.Air).IsAir
            && TempAt(calib, calib.MaxHeight) < SnowLineC + 2.0;
    }

    /// <summary>Ice-sheet thickness (blocks, 0 = open water) for a water column whose surface sits at
    /// <paramref name="waterTop"/> (#494): 0 above the freeze line, then 1 block per started 7 °C below
    /// it (capped at <see cref="MaxIceSheet"/>), and the full <paramref name="depth"/> below
    /// <see cref="DeepFreezeC"/> — or whenever the sheet would reach the seabed anyway (shallow ponds
    /// freeze through). Dithered with the snow pass's noise shape so the freeze edge wanders raggedly
    /// instead of cutting a temperature contour.</summary>
    private int IceSheetThickness(WorldCalibration calib, long seed, int worldX, int worldZ, int waterTop, int depth)
    {
        double surfT = TempAt(calib, waterTop)
            + (FbmT(seed + 0x1CE0, worldX, worldZ, 24.0, octaves: 2) - 0.5) * 3.0;
        if (surfT >= SnowLineC)
        {
            return 0;
        }

        if (surfT < DeepFreezeC)
        {
            return depth; // frozen through, down to the seabed
        }

        int sheet = 1 + (int)((SnowLineC - surfT) / 7.0);
        return System.Math.Min(System.Math.Min(sheet, MaxIceSheet), depth);
    }

    /// <summary>The generated ice on the water column at (x,z): 0 for a dry/lava/warm column, the sheet
    /// thickness on a frozen one — equal to the full water depth when the body is frozen through (#494).
    /// Mirrors exactly what <see cref="Generate"/> fills, like the other surface-water queries.</summary>
    public int SurfaceIceThickness(PlanetType planet, int worldX, int worldZ)
    {
        var calib = CalibFor(planet);
        if (!CanFreezeWater(planet, calib)) // cheap whole-world gate first — warm worlds pay nothing
        {
            return 0;
        }

        return TryGetRawWaterColumn(planet, worldX, worldZ, out int waterTopY, out int seabedY)
            ? IceSheetThickness(calib, PlanetSeed(planet), worldX, worldZ, waterTopY, waterTopY - seabedY)
            : 0;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Generation-1 whole-planet baseline regimes and the per-world relief rolls (#1645): style pools,
/// scale jitter, biome relief multipliers, tilted / stepped / ridged worlds (partial of <see cref="WorldGenerator"/>).</summary>
public sealed partial class WorldGenerator
{
    // --- Baseline regimes: rare rolls that shape the WHOLE planet under styles and landmarks, like the
    // escarpment (#702). Generation 1 only — the profile never sets them on a generation-0 world. ---
    private const double TiltChance = 20 / 256.0;        // ~8 % of eligible worlds
    private const double SteppedChance = 8 / 256.0;      // ~3 %: a second escarpment → three storeys
    private const double EquatorRidgeChance = 8 / 256.0; // ~3 %: the Iapetus ridge
    private const long SteppedSeedSalt = 0x57E99ED1;     // the second escarpment's own latitude / step / band

    /// <summary>Solid ground with a real surface: the regimes never touch sky worlds, void interiors or
    /// cratered regolith (whose flat identity is the point).</summary>
    private bool RegimeGround(PlanetType planet)
        => !planet.FloatingIslands && !planet.Void && !planet.Cratered && !_crateredWorld;

    private bool HasTilt(PlanetType planet, long seed)
        => RegimeGround(planet) && (Noise.Hash(seed ^ 0x71170001, 3, 5, 7) & 0xFF) < 256 * TiltChance;

    private bool HasStepped(PlanetType planet, long seed)
        => RegimeGround(planet) && (Noise.Hash(seed ^ 0x57E99ED0, 2, 6, 4) & 0xFF) < 256 * SteppedChance;

    private bool HasEquatorRidge(PlanetType planet, long seed)
        => RegimeGround(planet) && (Noise.Hash(seed ^ 0x0E9A70A1, 5, 3, 9) & 0xFF) < 256 * EquatorRidgeChance;

    /// <summary>Tilted world: one hemisphere sits 20–40 blocks higher than the other, changing gradually
    /// across the latitudes. A sine of the latitude (period = the north–south wrap) so the torus closes;
    /// the biome altitude blend then reads the low half as lowlands and the high half as uplands.</summary>
    private double TiltOffset(long seed, int worldZ)
    {
        ulong h = Noise.Hash(seed ^ 0x71170002, 1, 1, 1);
        double amp = 20.0 + ((h >> 8) & 0x3FF) / 1023.0 * 20.0;     // 20..40
        double phase = ((h >> 18) & 0x3FF) / 1023.0 * System.Math.PI * 2.0;
        return amp * System.Math.Sin(System.Math.PI * 2.0 * worldZ / LatPeriod + phase);
    }

    private const double EquatorRidgeMeanderFrac = 0.06; // meander amplitude as a fraction of the latitude period
    private const double EquatorRidgeMaxHalfWidth = 22.0;

    /// <summary>The equatorial ridge's (positive) height contribution (#1645): a 30–60-block wall 24–44 wide
    /// girdling the planet along X at a rolled latitude, meandering like the mega-rift (an X-only torus FBM,
    /// so the ridge closes on itself after a circumnavigation). A cheap band reject keeps far columns free.</summary>
    private double EquatorialRidgeOffset(long seed, int worldX, int worldZ)
    {
        ulong h = Noise.Hash(seed ^ 0x0E9A70A2, 4, 2, 8);
        int period = LatPeriod;
        double z0 = ((((h >> 8) & 0x3FF) / 1023.0) - 0.5) * period;
        double halfWidth = 12.0 + ((h >> 18) & 0x3FF) / 1023.0 * (EquatorRidgeMaxHalfWidth - 12.0); // 12..22
        double height = 30.0 + ((h >> 28) & 0x3FF) / 1023.0 * 30.0;                                 // 30..60

        double band = period * EquatorRidgeMeanderFrac + EquatorRidgeMaxHalfWidth * 1.5;
        double dz0 = WorldConstants.WrapDeltaZ(worldZ - z0, _circumference);
        if (System.Math.Abs(dz0) > band)
        {
            return 0.0;
        }

        double meander = (FbmT(seed + 0x0E9A70A3, worldX, 0.0, _circumference / 6.0, octaves: 2) - 0.5)
            * 2.0 * (period * EquatorRidgeMeanderFrac);
        double dz = WorldConstants.WrapDeltaZ(worldZ - z0 - meander, _circumference);
        double u = System.Math.Abs(dz) / halfWidth;
        if (u >= 1.0)
        {
            return 0.0;
        }

        double t = 1.0 - u * u;              // rounded crest
        return height * System.Math.Pow(t, 1.5);
    }

    /// <summary>The regimes rolled for this world (tests).</summary>
    internal (bool Tilted, bool Stepped, bool EquatorRidge, bool Escarpment) RegimesForTest(PlanetType planet)
    {
        var w = WonderFor(planet);
        return (w.Tilted, w.Stepped, w.EquatorRidge, w.Escarpment);
    }

    /// <summary>The tilt offset at a latitude (tests).</summary>
    internal double TiltOffsetForTest(PlanetType planet, int worldZ) => TiltOffset(WonderFor(planet).Seed, worldZ);

    /// <summary>The equatorial ridge offset at a column (tests).</summary>
    internal double EquatorialRidgeOffsetForTest(PlanetType planet, int worldX, int worldZ)
        => EquatorialRidgeOffset(WonderFor(planet).Seed, worldX, worldZ);

    // --- Per-world relief rolls (#1645): the style pick, the scale jitter and the biome multipliers, resolved
    // once in WonderFor. Generation-0 worlds take the classic values (single type style, type scale, no
    // multipliers) so every existing world stays byte-identical. ---

    /// <summary>The styles this world lays out as regions (#1645): 1–3 picks from the type's
    /// <see cref="PlanetType.TerrainStyles"/> pool (Fisher–Yates seeded like the biome subset; a pool of one,
    /// or no pool, keeps the type's single <see cref="PlanetType.TerrainStyle"/>). Lowered once.</summary>
    private static string[] PickStyles(PlanetType planet, long seed, string loweredStyle)
    {
        var pool = new System.Collections.Generic.List<string>();
        foreach (var s in planet.TerrainStyles)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            string lowered = s.Trim().ToLowerInvariant();
            if (!pool.Contains(lowered))
            {
                pool.Add(lowered);
            }
        }

        if (pool.Count == 0)
        {
            return loweredStyle.Length != 0 ? new[] { loweredStyle } : System.Array.Empty<string>();
        }

        if (pool.Count == 1)
        {
            return new[] { pool[0] };
        }

        // 25 % one style, 45 % two, 30 % three (capped by the pool) — most worlds mix, a quarter stay whole.
        ulong u = Noise.Hash(seed ^ 0x57F00, 4, 4, 2);
        double roll = (u & 0x3FF) / 1023.0;
        int k = roll < 0.25 ? 1 : roll < 0.70 ? 2 : 3;
        k = System.Math.Min(k, pool.Count);

        var order = pool.ToArray();
        var rng = new DeterministicRandom((seed ^ 0x57F07) * 2654435761L);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Range(0, i);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var picked = new string[k];
        System.Array.Copy(order, picked, k);
        return picked;
    }

    /// <summary>The per-world relief wavelength (#1645): the type's TerrainScale × 0.75–1.35, so two worlds of
    /// one type roll different hill spacing / dune pitch.</summary>
    private static double ScaleJitterFor(PlanetType planet, long seed)
    {
        ulong u = Noise.Hash(seed ^ 0x5CA1E, 3, 1, 4);
        return planet.TerrainScale * (0.75 + 0.6 * ((u & 0x3FF) / 1023.0));
    }

    /// <summary>The resolved biomes' relief multipliers in biome order (#1645), or null when the world has a
    /// single biome or every multiplier is 1 — the hot path then skips the extra field sample.</summary>
    private double[]? ReliefMulsFor(PlanetType planet)
    {
        var biomes = ResolveBiomes(planet);
        if (biomes.Count <= 1)
        {
            return null;
        }

        bool any = false;
        var muls = new double[biomes.Count];
        for (int i = 0; i < biomes.Count; i++)
        {
            muls[i] = biomes[i].ReliefMul;
            any |= System.Math.Abs(muls[i] - 1.0) > 1e-9;
        }

        return any ? muls : null;
    }

    /// <summary>The styles this world lays out (tests): empty on an archetype-blend world.</summary>
    internal string[] StylesForTest(PlanetType planet) => WonderFor(planet).Styles;

    /// <summary>The relief wavelength of this world (tests).</summary>
    internal double ScaleForTest(PlanetType planet) => WonderFor(planet).Scale;

    /// <summary>Whether the style×archetype hybrid fade runs on this world (tests).</summary>
    internal bool HybridEligibleForTest(PlanetType planet) => WonderFor(planet).HybridEligible;
}

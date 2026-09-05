// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1645 (landscape variety 2/6): relief variety on terrain-generation-1 worlds — regional style pools,
/// per-world scale jitter, biome relief multipliers, the new styles, the larger archetype pool and the rare
/// baseline regimes. Generation-0 worlds must take none of it (the goldens prove the bytes; these tests prove
/// the rolls and the shapes).
/// </summary>
public sealed class LandscapeReliefTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        if (generation > 0)
        {
            gen.SetTerrainGeneration(generation);
        }

        return gen;
    }

    private static IEnumerable<PlanetType> PooledTypes()
        => Content.Planets.Values.Where(p => p.TerrainStyles.Count > 1);

    // ---------- generation 0 stays classic ----------

    [Fact]
    public void GenerationZero_KeepsTheSingleTypeStyle_TheTypeScale_AndNoRegimes()
    {
        foreach (var planet in Content.Planets.Values)
        {
            var gen = Gen(4711, 0);
            var styles = gen.StylesForTest(planet);
            string expected = planet.TerrainStyle?.ToLowerInvariant() ?? string.Empty;
            if (expected.Length == 0)
            {
                Assert.Empty(styles);
            }
            else
            {
                Assert.Equal(new[] { expected }, styles);
            }

            Assert.Equal(planet.TerrainScale, gen.ScaleForTest(planet));
            Assert.Equal(8, gen.ArchetypePoolForTest(planet));
            Assert.Equal(1.0, gen.ReliefMulAtForTest(planet, 123, 456));
            var (tilted, stepped, ridge, _) = gen.RegimesForTest(planet);
            Assert.False(tilted || stepped || ridge, planet.Key);
        }
    }

    // ---------- style pools ----------

    [Fact]
    public void EveryPoolEntry_AppearsOnSomeWorld_AndPicksAreOneToThree()
    {
        foreach (var planet in PooledTypes())
        {
            var seen = new HashSet<string>();
            for (long s = 1; s <= 80; s++)
            {
                var styles = Gen(s * 7919 + 3, 1).StylesForTest(planet);
                Assert.InRange(styles.Length, 1, Math.Min(3, planet.TerrainStyles.Count));
                Assert.Equal(styles.Length, styles.Distinct().Count());
                foreach (var st in styles)
                {
                    Assert.Contains(st, planet.TerrainStyles.Select(x => x.ToLowerInvariant()));
                    seen.Add(st);
                }
            }

            foreach (var pooled in planet.TerrainStyles)
            {
                Assert.Contains(pooled.ToLowerInvariant(), seen);
            }
        }
    }

    [Fact]
    public void EveryPooledStyle_IsAStyleTheGeneratorKnows()
    {
        var known = WorldGenerator.KnownTerrainStyles.ToHashSet();
        foreach (var planet in Content.Planets.Values)
        {
            if (!string.IsNullOrEmpty(planet.TerrainStyle))
            {
                Assert.Contains(planet.TerrainStyle.ToLowerInvariant(), known);
            }

            foreach (var s in planet.TerrainStyles)
            {
                Assert.Contains(s.ToLowerInvariant(), known);
            }
        }
    }

    [Fact]
    public void IdentityStyles_StayPure_OnlyAsTheSolePick()
    {
        // ocean pool = flats + archipelago: a sole "flats" world never fades to the archetype blend; a mixed
        // world does (the hybrid fade is what puts hills between the island fields).
        var ocean = Content.Planets["ocean"];
        bool sawSole = false, sawMixed = false;
        for (long s = 1; s <= 120 && !(sawSole && sawMixed); s++)
        {
            var gen = Gen(s * 104729 + 11, 1);
            var styles = gen.StylesForTest(ocean);
            if (styles.Length == 1 && styles[0] == "flats")
            {
                Assert.False(gen.HybridEligibleForTest(ocean));
                sawSole = true;
            }
            else if (styles.Length > 1)
            {
                Assert.True(gen.HybridEligibleForTest(ocean));
                sawMixed = true;
            }
        }

        Assert.True(sawSole && sawMixed, $"sole {sawSole}, mixed {sawMixed}");
    }

    [Fact]
    public void StyleRegions_CoverEveryRolledStyle_OnAMultiStyleWorld()
    {
        var desert = Content.Planets["desert"];
        WorldGenerator? gen = null;
        for (long s = 1; s <= 200 && gen is null; s++)
        {
            var g = Gen(s * 6151 + 5, 1);
            if (g.StylesForTest(desert).Length == 3)
            {
                gen = g;
            }
        }

        Assert.NotNull(gen);
        var counts = new Dictionary<string, int>();
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += 61)
            for (int x = 0; x < circ; x += 67)
            {
                string st = gen!.StyleAtForTest(desert, x, z);
                counts[st] = counts.GetValueOrDefault(st) + 1;
            }

        foreach (var st in gen!.StylesForTest(desert))
        {
            Assert.True(counts.GetValueOrDefault(st) > 0, $"style {st} has no region");
        }
    }

    // ---------- scale jitter ----------

    [Fact]
    public void ScaleJitter_StaysInTheDesignedRange_AndVariesPerWorld()
    {
        var jungle = Content.Planets["jungle"];
        var seen = new HashSet<double>();
        for (long s = 1; s <= 60; s++)
        {
            double scale = Gen(s * 31 + 7, 1).ScaleForTest(jungle);
            Assert.InRange(scale, jungle.TerrainScale * 0.75 - 1e-9, jungle.TerrainScale * 1.35 + 1e-9);
            seen.Add(Math.Round(scale, 3));
        }

        Assert.True(seen.Count > 20, $"only {seen.Count} distinct scales");
    }

    // ---------- biome relief ----------

    [Fact]
    public void ReliefMul_DampsTheMarsh_AndBoostsTheStone()
    {
        // karst: mud 0.35 · grass 1.0 · stone 1.5 — the relief amplitude under the marsh regions must come out
        // clearly smaller than under the stone regions on the same world (default mode: no continents, no
        // escarpment baseline to confound the comparison).
        var karst = Content.Planets["karst"];
        double flatSum = 0, ruggedSum = 0;
        int flatN = 0, ruggedN = 0;
        for (long s = 1; s <= 6; s++)
        {
            var gen = Gen(s * 2003 + 1, 1);
            var (tilted, stepped, ridge, esc) = gen.RegimesForTest(karst);
            if (tilted || stepped || ridge || esc)
            {
                continue; // a baseline regime would shift the mean height; skip those seeds
            }

            for (int z = -1200; z < 1200; z += 23)
                for (int x = 0; x < 2400; x += 29)
                {
                    double mul = gen.ReliefMulAtForTest(karst, x, z);
                    double dev = Math.Abs(gen.SurfaceHeight(karst, x, z) - karst.BaseHeight);
                    if (mul <= 0.5)
                    {
                        flatSum += dev;
                        flatN++;
                    }
                    else if (mul >= 1.4)
                    {
                        ruggedSum += dev;
                        ruggedN++;
                    }
                }
        }

        Assert.True(flatN > 200 && ruggedN > 200, $"samples flat {flatN}, rugged {ruggedN}");
        double flat = flatSum / flatN, rugged = ruggedSum / ruggedN;
        Assert.True(flat * 1.8 < rugged, $"marsh relief {flat:F2} vs stone relief {rugged:F2}");
    }

    [Fact]
    public void ReliefMul_IsContinuous_AcrossBiomeRegionBoundaries()
    {
        var karst = Content.Planets["karst"];
        var gen = Gen(99, 1);
        double prev = gen.ReliefMulAtForTest(karst, 0, 0);
        for (int x = 1; x < 6000; x++)
        {
            double cur = gen.ReliefMulAtForTest(karst, x, 0);
            Assert.True(Math.Abs(cur - prev) < 0.2, $"relief multiplier jumps {prev:F3} → {cur:F3} at x={x}");
            prev = cur;
        }
    }

    // ---------- new styles ----------

    [Fact]
    public void Archipelago_RaisesIslandDomes_OverAFlatFloor()
    {
        var ocean = Content.Planets["ocean"];
        var gen = Gen(2026, 1);
        int raised = 0, total = 0;
        for (int z = -1500; z < 1500; z += 17)
            for (int x = 0; x < 3000; x += 19)
            {
                double o = gen.StyledOffsetForTest("archipelago", ocean, x, z);
                total++;
                if (o > ocean.Amplitude * 0.6)
                {
                    raised++;
                }
            }

        double frac = raised / (double)total;
        Assert.InRange(frac, 0.03, 0.40);
    }

    [Fact]
    public void Downs_RollGently_NeverStep()
    {
        var savanna = Content.Planets["savanna"];
        var gen = Gen(77, 1);
        for (int x = 0; x < 3000; x++)
        {
            double a = gen.StyledOffsetForTest("downs", savanna, x, 40);
            double b = gen.StyledOffsetForTest("downs", savanna, x + 1, 40);
            Assert.True(Math.Abs(a - b) <= 1.5, $"downs slope {Math.Abs(a - b):F2} at x={x}");
        }
    }

    [Fact]
    public void Terraces_QuantiseIntoFlatDecks()
    {
        var tablelands = Content.Planets["tablelands"];
        var gen = Gen(5, 1);
        double step = Math.Max(2.0, tablelands.Amplitude * 0.12);
        for (int x = 0; x < 2000; x += 3)
        {
            double o = gen.StyledOffsetForTest("terraces", tablelands, x, -300);
            double r = o / step;
            Assert.True(Math.Abs(r - Math.Round(r)) < 1e-6, $"terrace offset {o} is not a whole deck");
        }
    }

    [Fact]
    public void Shattered_CutsDeepRifts_SomewhereInEveryCell()
    {
        var corrupted = Content.Planets["corrupted"];
        var gen = Gen(31337, 1);
        double deepest = 0.0;
        for (int z = -900; z < 900; z += 7)
            for (int x = 0; x < 1800; x += 7)
            {
                deepest = Math.Min(deepest, gen.StyledOffsetForTest("shattered", corrupted, x, z));
            }

        Assert.True(deepest < -corrupted.Amplitude * 1.5, $"no rift found, deepest {deepest:F1}");
    }

    [Theory]
    [InlineData("desert")]
    [InlineData("highland")]
    [InlineData("ocean")]
    [InlineData("tundra")]
    [InlineData("corrupted")]
    public void GenerationOneWorlds_AreDeterministic_SeamFree_AndMemoConsistent(string key)
    {
        var planet = Content.Planets[key];
        var a = Gen(8080, 1);
        var b = Gen(8080, 1);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += 131)
            for (int x = 0; x < circ; x += 173)
            {
                int h = a.SurfaceHeight(planet, x, z);
                Assert.Equal(h, b.SurfaceHeight(planet, x, z));
                Assert.Equal(h, a.SurfaceHeightUncached(planet, x, z));
                Assert.Equal(h, a.SurfaceHeight(planet, x + circ, z));
                Assert.Equal(h, a.SurfaceHeight(planet, x, z + period));
                // Negative world Y is legal (a gen-0 mega-rift floor already goes there); the ceiling is the
                // MaxNaturalSurfaceY clamp under the atmosphere line.
                Assert.InRange(h, -400, 288);
            }
    }

    // ---------- archetype pool ----------

    [Fact]
    public void ArchetypePool_IsElevenOnGenerationOne()
    {
        var lava = Content.Planets["lava"]; // archetype-blend type (no style)
        Assert.Equal(8, Gen(1, 0).ArchetypePoolForTest(lava));
        Assert.Equal(11, Gen(1, 1).ArchetypePoolForTest(lava));
        Assert.Empty(Gen(1, 1).StylesForTest(lava));
    }

    // ---------- baseline regimes ----------

    [Fact]
    public void Regimes_RollRarely_AndEachAppearsSomewhere()
    {
        var savanna = Content.Planets["savanna"];
        int tilted = 0, stepped = 0, ridged = 0;
        const int worlds = 400;
        for (long s = 1; s <= worlds; s++)
        {
            var (t, st, r, esc) = Gen(s * 6151 + 1, 1).RegimesForTest(savanna);
            tilted += t ? 1 : 0;
            stepped += st ? 1 : 0;
            ridged += r ? 1 : 0;
            if (st)
            {
                Assert.True(esc, "a stepped world always carries the first escarpment too");
            }
        }

        Assert.InRange(tilted, 10, 80);
        Assert.InRange(stepped, 2, 40);
        Assert.InRange(ridged, 2, 40);
    }

    [Fact]
    public void Regimes_NeverRoll_OnSkyOrCrateredWorlds()
    {
        foreach (var key in new[] { "skylands", "asteroid", "orbital_station" })
        {
            var planet = Content.Planets[key];
            for (long s = 1; s <= 150; s++)
            {
                var (t, st, r, _) = Gen(s * 6151 + 1, 1).RegimesForTest(planet);
                Assert.False(t || st || r, key);
            }
        }
    }

    [Fact]
    public void TiltedWorld_HasAHighAndALowHemisphere()
    {
        var savanna = Content.Planets["savanna"];
        WorldGenerator? gen = null;
        for (long s = 1; s <= 400 && gen is null; s++)
        {
            var g = Gen(s * 6151 + 1, 1);
            if (g.RegimesForTest(savanna).Tilted)
            {
                gen = g;
            }
        }

        Assert.NotNull(gen);
        int period = WorldConstants.LatitudePeriodFor(WorldConstants.Circumference);
        double lo = double.MaxValue, hi = double.MinValue;
        for (int z = -period / 2; z < period / 2; z += 32)
        {
            double o = gen!.TiltOffsetForTest(savanna, z);
            lo = Math.Min(lo, o);
            hi = Math.Max(hi, o);
        }

        Assert.True(hi >= 19.0 && lo <= -19.0, $"tilt hi {hi:F1} lo {lo:F1}");
        Assert.Equal(gen!.TiltOffsetForTest(savanna, 100), gen.TiltOffsetForTest(savanna, 100 + period), 6);
    }

    [Fact]
    public void EquatorialRidge_GirdlesThePlanet()
    {
        var savanna = Content.Planets["savanna"];
        WorldGenerator? gen = null;
        for (long s = 1; s <= 600 && gen is null; s++)
        {
            var g = Gen(s * 6151 + 1, 1);
            if (g.RegimesForTest(savanna).EquatorRidge)
            {
                gen = g;
            }
        }

        Assert.NotNull(gen);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        // At every longitude sampled the ridge exists at SOME latitude (a closed girdle), and it is tall.
        for (int x = 0; x < circ; x += 397)
        {
            double best = 0.0;
            for (int z = -period / 2; z < period / 2; z += 4)
            {
                best = Math.Max(best, gen!.EquatorialRidgeOffsetForTest(savanna, x, z));
            }

            Assert.True(best >= 25.0, $"ridge missing at x={x} (best {best:F1})");
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1646 (landscape variety 3/6): the generation-1 landmark families, overhang bands and underground finds.
/// Each family is probed on a world where its gate is open — the feature exists somewhere on the scanned
/// area and its offset lies in the designed range — and every gate is closed on generation 0.
/// </summary>
public sealed class LandscapeLandmarksTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] Gen1Rows =
    {
        "shield-volcano", "impact-basin", "glacial-trough", "yardangs", "drumlin-field", "inselberg",
        "star-dunes", "mud-volcanoes", "sinkhole-chain", "maar", "mushroom-rock", "glacier-tongue",
    };

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true); // every new world since #1631 — the shield volcanoes ride on the volcano gate
        if (generation > 0)
        {
            gen.SetTerrainGeneration(generation);
        }

        return gen;
    }

    /// <summary>Scans a grid for the row's offset extremes: (min, max, hit count).</summary>
    private static (double Min, double Max, int Hits) Scan(WorldGenerator gen, string row, PlanetType planet, int step)
    {
        double min = 0.0, max = 0.0;
        int hits = 0;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < circ; x += step)
            {
                double o = gen.LandmarkOffsetForTest(row, planet, x, z);
                if (o != 0.0)
                {
                    hits++;
                    min = Math.Min(min, o);
                    max = Math.Max(max, o);
                }
            }

        return (min, max, hits);
    }

    /// <summary>The first of up to <paramref name="tries"/> seeds whose world carries the row somewhere.</summary>
    private static WorldGenerator? FindWorldWith(string row, PlanetType planet, int step, int tries, out (double Min, double Max, int Hits) scan)
    {
        scan = default;
        for (long s = 1; s <= tries; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            scan = Scan(gen, row, planet, step);
            if (scan.Hits > 0)
            {
                return gen;
            }
        }

        return null;
    }

    // ---------- generation 0 stays classic ----------

    [Fact]
    public void GenerationZero_ActivatesNoneOfTheNewRows_Bands_OrFinds()
    {
        foreach (var planet in Content.Planets.Values)
        {
            var gen = Gen(4711, 0);
            var order = gen.LandmarkOrderForTest(planet);
            foreach (var row in Gen1Rows)
            {
                Assert.DoesNotContain(row, order);
            }

            Assert.False(gen.StrataForTest(planet), planet.Key);
            Assert.False(gen.TryGetGeodeSpanForTest(planet, 10, 10, out _, out _, out _, out _), planet.Key);
        }
    }

    [Fact]
    public void LandmarkTable_AppendsTheNewRows_AfterTheClassicOnes()
    {
        var rocky = Content.Planets["rocky"];
        var order = Gen(1, 1).LandmarkOrderForTest(rocky);
        int lastClassic = Array.LastIndexOf(order, "mega-rift");
        if (lastClassic < 0)
        {
            lastClassic = Array.LastIndexOf(order, "rift");
        }

        Assert.True(lastClassic >= 0);
        foreach (var row in Gen1Rows)
        {
            int i = Array.IndexOf(order, row);
            if (i >= 0)
            {
                Assert.True(i > lastClassic, $"{row} precedes a classic row");
            }
        }

        Assert.Contains("inselberg", order);      // rocky carries the tag
        Assert.Contains("mushroom-rock", order);  // buttes tag
        Assert.DoesNotContain("yardangs", order); // no wind tag on rocky
    }

    // ---------- landmark shapes ----------

    [Theory]
    [InlineData("shield-volcano", "lava", 24, 15.0, 45.0, -8.0, 0.0)]
    [InlineData("impact-basin", "savanna", 16, 0.0, 7.0, -36.0, -14.0)]
    [InlineData("glacial-trough", "tundra", 16, 0.0, 0.0, -46.0, -18.0)]
    [InlineData("yardangs", "desert", 12, 3.0, 13.0, 0.0, 0.0)]
    [InlineData("drumlin-field", "tundra", 12, 3.0, 11.0, 0.0, 0.0)]
    [InlineData("inselberg", "rocky", 16, 35.0, 95.0, 0.0, 0.0)]
    [InlineData("star-dunes", "desert", 8, 10.0, 32.0, 0.0, 0.0)]
    [InlineData("mud-volcanoes", "jungle", 6, 1.5, 7.0, 0.0, 0.0)]
    [InlineData("sinkhole-chain", "jungle", 3, 0.0, 0.0, -21.0, -9.0)]
    [InlineData("maar", "savanna", 6, 0.5, 2.5, -15.0, -6.0)]
    [InlineData("mushroom-rock", "desert", 2, 4.0, 9.0, 0.0, 0.0)]
    public void Row_AppearsOnAnEligibleWorld_WithinItsDesignedRange(string row, string key, int step, double maxLo, double maxHi, double minLo, double minHi)
    {
        var planet = Content.Planets[key];
        var gen = FindWorldWith(row, planet, step, 40, out var scan);
        Assert.NotNull(gen);
        Assert.True(scan.Hits > 0, $"{row} never appears on {key}");
        Assert.InRange(scan.Max, maxLo, maxHi);
        Assert.InRange(scan.Min, minLo, minHi);
    }

    [Fact]
    public void Inselberg_PaintsItsDomeGranite()
    {
        var rocky = Content.Planets["rocky"];
        var granite = Content.GetBlock("granite")!.NumericId;
        var gen = FindWorldWith("inselberg", rocky, 16, 40, out _);
        Assert.NotNull(gen);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int painted = 0, domeCols = 0;
        for (int z = -period / 2; z < period / 2; z += 16)
            for (int x = 0; x < circ; x += 16)
            {
                double o = gen!.LandmarkOffsetForTest("inselberg", rocky, x, z);
                if (o > 3.0)
                {
                    domeCols++;
                    if (gen.LandmarkPaintForTest("inselberg", rocky, x, z) == granite)
                    {
                        painted++;
                    }
                }
                else if (o == 0.0)
                {
                    Assert.Null(gen.LandmarkPaintForTest("inselberg", rocky, x, z));
                }
            }

        Assert.True(domeCols > 0 && painted == domeCols, $"dome columns {domeCols}, granite {painted}");
    }

    [Fact]
    public void GlacierTongue_PaintsIce_OnOneFlankSectorOfAColdMassif()
    {
        var ice = Content.Planets["ice"];
        var iceBlock = Content.GetBlock("ice")!.NumericId;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (long s = 1; s <= 120; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            if (!gen.LandmarkOrderForTest(ice).Contains("glacier-tongue"))
            {
                continue;
            }

            int painted = 0, massifCols = 0;
            for (int z = -period / 2; z < period / 2; z += 12)
                for (int x = 0; x < circ; x += 12)
                {
                    if (gen.LandmarkOffsetForTest("massif", ice, x, z) > 5.0)
                    {
                        massifCols++;
                        if (gen.LandmarkPaintForTest("glacier-tongue", ice, x, z) == iceBlock)
                        {
                            painted++;
                        }
                    }
                }

            if (massifCols > 0)
            {
                Assert.True(painted > 0 && painted < massifCols, $"tongue covers {painted} of {massifCols} massif columns");
                return;
            }
        }

        Assert.Fail("no cold massif world found in 120 seeds");
    }

    // ---------- one overlay per column, clamp ----------

    [Theory]
    [InlineData("rocky")]
    [InlineData("tundra")]
    [InlineData("desert")]
    [InlineData("jungle")]
    [InlineData("ice")]
    public void GenerationOneWorlds_StayClamped_Deterministic_AndSeamFree(string key)
    {
        var planet = Content.Planets[key];
        var a = Gen(9090, 1);
        var b = Gen(9090, 1);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += 97)
            for (int x = 0; x < circ; x += 131)
            {
                int h = a.SurfaceHeight(planet, x, z);
                Assert.True(h <= 288, $"{key} ({x},{z}) = {h} pokes above the clamp");
                Assert.Equal(h, b.SurfaceHeight(planet, x, z));
                Assert.Equal(h, a.SurfaceHeightUncached(planet, x, z));
                Assert.Equal(h, a.SurfaceHeight(planet, x + circ, z));
                Assert.Equal(h, a.SurfaceHeight(planet, x, z + period));
            }
    }

    // ---------- bands ----------

    private static int CountBands(WorldGenerator gen, PlanetType planet, Func<int, int, bool> where, int step, out int sampled)
    {
        var bands = new WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int count = 0;
        sampled = 0;
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < circ; x += step)
            {
                if (!where(x, z))
                {
                    continue;
                }

                sampled++;
                int n = gen.GetExtraBands(planet, x, z, bands);
                for (int i = 0; i < n; i++)
                {
                    if (bands[i].Kind == WorldGenerator.BandKind.Cap)
                    {
                        count++;
                    }
                }
            }

        return count;
    }

    [Fact]
    public void NaturalBridge_SpansARift_AtRimLevel()
    {
        var savanna = Content.Planets["savanna"];
        for (long s = 1; s <= 200; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            var rift = Scan(gen, "rift", savanna, 8);
            if (rift.Hits == 0)
            {
                continue;
            }

            // Somewhere over the gorge (rift offset < 0) a Cap band hangs whose top is the pre-rift ground.
            var bands = new WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            int found = 0;
            for (int z = -period / 2; z < period / 2 && found == 0; z += 2)
                for (int x = 0; x < circ; x += 2)
                {
                    if (gen.LandmarkOffsetForTest("rift", savanna, x, z) >= -5.0)
                    {
                        continue;
                    }

                    int n = gen.GetExtraBands(savanna, x, z, bands);
                    for (int i = 0; i < n; i++)
                    {
                        if (bands[i].Kind == WorldGenerator.BandKind.Cap && bands[i].Top > gen.SurfaceHeight(savanna, x, z) + 5)
                        {
                            found++;
                        }
                    }
                }

            if (found > 0)
            {
                return;
            }
        }

        Assert.Fail("no rift world with a natural bridge found in 200 seeds");
    }

    [Fact]
    public void MushroomRocks_CapTheirStems()
    {
        var desert = Content.Planets["desert"];
        var gen = FindWorldWith("mushroom-rock", desert, 2, 40, out _);
        Assert.NotNull(gen);
        int caps = CountBands(gen!, desert, (x, z) => gen.LandmarkOffsetForTest("mushroom-rock", desert, x, z) > 3.0, 2, out int stems);
        Assert.True(stems > 0 && caps >= stems, $"stems {stems}, caps {caps}");
    }

    [Fact]
    public void IceCornices_HangOffCrests_OnColdWorlds()
    {
        var ice = Content.Planets["ice"];
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            var bands = new WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z += 5)
                for (int x = 0; x < circ; x += 5)
                {
                    int n = gen.GetExtraBands(ice, x, z, bands);
                    for (int i = 0; i < n; i++)
                    {
                        int h = gen.SurfaceHeight(ice, x, z);
                        if (bands[i].Kind == WorldGenerator.BandKind.Cap && bands[i].Top >= h + 6 && bands[i].Top - bands[i].Bottom == 1)
                        {
                            return; // a cornice: a thin lip well above this column's ground
                        }
                    }
                }
        }

        Assert.Fail("no ice cornice found in 60 ice worlds");
    }

    // ---------- underground ----------

    [Fact]
    public void Geodes_AreHollowCrystalSpheres_BelowTheSurface()
    {
        var crystal = Content.Planets["crystal"];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z += 3)
                for (int x = 0; x < circ; x += 3)
                {
                    if (!gen.TryGetGeodeSpanForTest(crystal, x, z, out int lo, out int hi, out int inLo, out int inHi))
                    {
                        continue;
                    }

                    Assert.True(hi - lo <= 2 * 14 + 1);
                    Assert.True(hi < gen.SurfaceHeight(crystal, x, z) - 10, "geode breaks the surface");
                    if (inHi >= inLo)
                    {
                        Assert.True(inLo > lo && inHi < hi, "the hollow must sit inside the shell");
                        return;
                    }
                }
        }

        Assert.Fail("no geode with a hollow found in 40 crystal worlds");
    }

    [Fact]
    public void Strata_LayGraniteBands_InTheUpperCrust_OnGenerationOneOnly()
    {
        var rocky = Content.Planets["rocky"];
        var granite = Content.GetBlock("granite")!.NumericId;
        int CountGranite(WorldGenerator gen)
        {
            int count = 0;
            int cs = WorldConstants.ChunkSize;
            for (int cx = 0; cx < 12; cx++)
                for (int cz = 0; cz < 12; cz++)
                {
                    int sx = cx * cs * 7, sz = cz * cs * 5 - 900;
                    int surfaceCy = WorldConstants.WorldToChunk(gen.SurfaceHeight(rocky, sx, sz));
                    var chunk = gen.Generate(rocky, new ChunkCoord(WorldConstants.WorldToChunk(sx), surfaceCy - 1, WorldConstants.WorldToChunk(sz)));
                    for (int x = 0; x < cs; x++)
                        for (int y = 0; y < cs; y++)
                            for (int z = 0; z < cs; z++)
                            {
                                if (chunk.Get(x, y, z) == granite)
                                {
                                    count++;
                                }
                            }
                }

            return count;
        }

        Assert.Equal(0, CountGranite(Gen(77, 0)));
        Assert.True(CountGranite(Gen(77, 1)) > 50, "generation 1 rocky world shows no strata");
    }

    [Fact]
    public void AquiferCaverns_HoldLakes_OnWetGenerationOneWorlds()
    {
        var jungle = Content.Planets["jungle"];
        int lakes = 0, caverns = 0;
        for (long s = 1; s <= 30; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z += 40)
                for (int x = 0; x < circ; x += 40)
                {
                    if (gen.TryGetCavernSpan(jungle, x, z, out _, out _, out int lakeY))
                    {
                        caverns++;
                        if (lakeY != int.MinValue)
                        {
                            lakes++;
                        }
                    }
                }
        }

        Assert.True(caverns > 20, $"only {caverns} cavern columns sampled");
        Assert.True(lakes * 10 >= caverns * 7, $"only {lakes} of {caverns} cavern columns hold a lake");
    }
}

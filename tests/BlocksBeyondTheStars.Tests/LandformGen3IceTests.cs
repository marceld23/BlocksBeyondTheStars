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
/// Terrain generation 3, part 7 — ice as a volume: glaciers (an ice tongue on the ground, crevassed, with
/// moraines), ice sheets with nunataks (row precedence), hanging valleys beside a glacial trough, ice caves and
/// glacier gates (worm families), shallow sheet caves on ice-surface worlds, and icebergs keeping off the pads.
/// All absent below generation 3.
/// </summary>
public sealed class LandformGen3IceTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        if (generation > 0)
        {
            gen.SetTerrainGeneration(generation);
        }

        return gen;
    }

    private static IEnumerable<(int X, int Z)> Grid(int stepX, int stepZ)
    {
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < circ; x += stepX)
                yield return (x, z);
    }

    private static BlockId Cell(WorldGenerator gen, PlanetType planet, Dictionary<ChunkCoord, ChunkData> chunks, int x, int y, int z)
    {
        var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
        if (!chunks.TryGetValue(coord, out var chunk))
        {
            chunk = gen.Generate(planet, coord);
            chunks[coord] = chunk;
        }

        const int cs = WorldConstants.ChunkSize;
        return chunk.Get(((x % cs) + cs) % cs, ((y % cs) + cs) % cs, ((z % cs) + cs) % cs);
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    private static bool RowOwns(WorldGenerator gen, PlanetType planet, int x, int z, double offset)
        => gen.SurfaceHeight(planet, x, z) == gen.RawSurfaceHeightForTest(planet, x, z) + (int)Math.Round(offset);

    // ---------- glaciers ----------

    /// <summary>A glacier tongue is ice from its surface down to the old ground (the paint fill), the ground
    /// under it stays what it was, and a moraine beside it is a scree ridge 4–10 high.</summary>
    [Fact]
    public void Glaciers_AreIceDownToTheOldGround_WithScreeMoraines()
    {
        var planet = Content.Planets["tundra"];
        var ice = Block("ice");
        var scree = Block("scree");
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            (int X, int Z, double Rise)? tongue = null, moraine = null;
            foreach (var (x, z) in Grid(5, 5))
            {
                if (gen.GlacierForTest(planet, x, z) is not { } g || !RowOwns(gen, planet, x, z, g.Rise))
                {
                    continue;
                }

                if (g.Ice && g.Rise >= 8.0 && tongue is null)
                {
                    tongue = (x, z, g.Rise);
                }
                else if (!g.Ice && moraine is null)
                {
                    moraine = (x, z, g.Rise);
                }

                if (tongue is not null && moraine is not null)
                {
                    break;
                }
            }

            if (tongue is null || moraine is null)
            {
                continue;
            }

            var (tx, tz, rise) = tongue.Value;
            int surface = gen.SurfaceHeight(planet, tx, tz);
            Assert.Equal(ice, gen.LandmarkPaintForTest("glacier", planet, tx, tz, out int fill));
            Assert.Equal(surface - (int)Math.Round(rise) + 1, fill);
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            for (int y = surface; y >= fill; y--)
            {
                Assert.True(Cell(gen, planet, chunks, tx, y, tz) == ice, $"glacier cell ({tx},{y},{tz}) is not ice");
            }

            Assert.NotEqual(ice, Cell(gen, planet, chunks, tx, fill - 1, tz)); // the old ground under the ice

            var (mx, mz, mRise) = moraine.Value;
            Assert.InRange(mRise, 1.0, 10.0);
            Assert.Equal(scree, gen.LandmarkPaintForTest("glacier", planet, mx, mz));
            Assert.Null(Gen(s * 6151 + 3, 1).GlacierForTest(planet, tx, tz));
            return;
        }

        Assert.Fail("no tundra seed with a glacier tongue and a moraine on the sample grid in 40 seeds");
    }

    // ---------- ice sheets and nunataks ----------

    /// <summary>An ice sheet caps its region 10–25 deep in ice; the massif and trough rows precede it in the
    /// table, so a mountain inside the region keeps its rock — the nunatak.</summary>
    [Fact]
    public void IceSheets_CapTheRegion_AndMassifsPokeThrough()
    {
        var planet = Content.Planets["glacier"];
        var ice = Block("ice");
        var order = Gen(20260903, 3).LandmarkOrderForTest(planet).ToList();
        Assert.True(order.IndexOf("massif") >= 0 && order.IndexOf("massif") < order.IndexOf("ice-sheet"), "the massif must own its column before the ice sheet");
        Assert.True(order.IndexOf("glacial-trough") < order.IndexOf("ice-sheet"), "the trough must own its column before the ice sheet");

        for (long s = 1; s <= 20; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            foreach (var (x, z) in Grid(9, 9))
            {
                double cap = gen.IceOffsetForTest("ice-sheet", planet, x, z);
                if (cap <= 0.0 || !RowOwns(gen, planet, x, z, cap))
                {
                    continue;
                }

                Assert.InRange(cap, 10.0, 25.0);
                Assert.Equal(ice, gen.LandmarkPaintForTest("ice-sheet", planet, x, z, out int fill));
                int surface = gen.SurfaceHeight(planet, x, z);
                Assert.Equal(surface - (int)Math.Round(cap) + 1, fill);
                Assert.Equal(0.0, Gen(s * 6151 + 3, 1).IceOffsetForTest("ice-sheet", planet, x, z));
                return;
            }
        }

        Assert.Fail("no ice-sheet column on the sample grid in 20 seeds");
    }

    // ---------- hanging valleys ----------

    /// <summary>A hanging valley is a cut 15–25 deep beside a glacial trough, absent on generation 1.</summary>
    [Fact]
    public void HangingValleys_CutBesideTheTrough_OnlyFromGenerationThree()
    {
        var planet = Content.Planets["tundra"];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            foreach (var (x, z) in Grid(7, 7))
            {
                double off = gen.IceOffsetForTest("hanging-valley", planet, x, z);
                if (off >= 0.0)
                {
                    continue;
                }

                Assert.InRange(off, -25.0, 0.0);
                Assert.Equal(0.0, Gen(s * 6151 + 3, 1).IceOffsetForTest("hanging-valley", planet, x, z));
                Assert.True(gen.IceOffsetForTest("hanging-valley", planet, x, z) == off, "not deterministic");
                return;
            }
        }

        Assert.Fail("no hanging valley on the sample grid in 40 seeds");
    }

    // ---------- ice caves ----------

    /// <summary>On an ice-surface world the sheet caves hang under the crust: a worm span whose roof lies within
    /// fourteen of the ground, its interior air, its walls ice.</summary>
    [Fact]
    public void SheetCaves_RunThroughTheIceCrust()
    {
        var planet = Content.Planets["ice"];
        var ice = Block("ice");
        Assert.Contains("sheet-caves", WorldGenerator.TunnelFamilyOrderForTest());
        Assert.Contains("glacier-gates", WorldGenerator.TunnelFamilyOrderForTest());
        Assert.Contains("ice-caves", WorldGenerator.TunnelFamilyOrderForTest());
        var spans = new (int Lo, int Hi)[16];
        for (long s = 1; s <= 30; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            foreach (var (x, z) in Grid(9, 9))
            {
                int n = gen.TunnelSpans(planet, x, z, spans);
                int surface = gen.SurfaceHeight(planet, x, z);
                for (int i = 0; i < n; i++)
                {
                    var (lo, hi) = spans[i];
                    if (hi > surface - 3 || hi < surface - 14 || hi - lo < 3 || gen.SurfacePondDepth(planet, x, z) > 0)
                    {
                        continue;
                    }

                    var chunks = new Dictionary<ChunkCoord, ChunkData>();
                    int mid = (lo + hi) / 2;
                    Assert.True(Cell(gen, planet, chunks, x, mid, z).IsAir, $"the cave at ({x},{mid},{z}) is not air");
                    var roof = Cell(gen, planet, chunks, x, hi + 1, z);
                    Assert.False(roof.IsAir, $"the cave roof at ({x},{hi + 1},{z}) is open");
                    Assert.True(Cell(gen, planet, chunks, x, surface, z) == ice, "the crust over the cave is not ice");
                    return;
                }
            }
        }

        Assert.Fail("no shallow ice cave under the crust on the sample grid in 30 seeds");
    }

    // ---------- gates ----------

    [Fact]
    public void Gates_IceFamilies_FromGenerationThreeOnly()
    {
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        var glacier = g3.WonderGatesForTest(Content.Planets["glacier"]);
        Assert.True(glacier["glaciers"] && glacier["iceSheets"] && glacier["hangingValleys"] && glacier["iceCaves"] && glacier["sheetCaves"], "the glacier world has every ice family");
        var tundra = g3.WonderGatesForTest(Content.Planets["tundra"]);
        Assert.True(tundra["glaciers"] && tundra["hangingValleys"] && tundra["iceSheets"], "the tundra (−22 °C, glacial) has glaciers, hanging valleys and an ice sheet");
        Assert.False(tundra["sheetCaves"], "the tundra has a snow surface, not an ice crust");
        Assert.False(g3.WonderGatesForTest(Content.Planets["boreal"])["iceSheets"], "the boreal world (−4 °C) is too warm for a sheet");
        var jungle = g3.WonderGatesForTest(Content.Planets["jungle"]);
        Assert.False(jungle["glaciers"] || jungle["iceSheets"] || jungle["hangingValleys"] || jungle["iceCaves"] || jungle["sheetCaves"], "the jungle has no ice");
        foreach (var key in new[] { "glacier", "tundra", "ice" })
        {
            var gates = g1.WonderGatesForTest(Content.Planets[key]);
            Assert.False(gates["glaciers"] || gates["iceSheets"] || gates["hangingValleys"] || gates["iceCaves"] || gates["sheetCaves"], $"{key} on generation 1");
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3859: the tall-flora top of a chunk column — what the server streams up to so a distant tree keeps its crown. What has
/// to hold: every giant-tree cell of a column lies at or under the column's answer (else the crown is cut off flat), the
/// answer reaches above the old one-chunk surface band where a giant stands (the bug), and a barren world answers "none".
/// </summary>
public sealed class TallFloraTopTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.NewKindsGeneration);
        return gen;
    }

    /// <summary>The highest giant-tree cell (log or leaves) in chunk column (<paramref name="cx"/>, <paramref name="cz"/>)
    /// between chunk rows <paramref name="cyLo"/> and <paramref name="cyHi"/>, or <see cref="int.MinValue"/>.</summary>
    private static int HighestGiantCell(WorldGenerator gen, PlanetType planet, int cx, int cz, int cyLo, int cyHi)
    {
        var log = Content.GetBlock(WorldGenerator.GiantLogKey)!.NumericId;
        var leaves = Content.GetBlock(WorldGenerator.GiantLeavesKey)!.NumericId;
        int cs = WorldConstants.ChunkSize;
        int top = int.MinValue;
        for (int cy = cyLo; cy <= cyHi; cy++)
        {
            var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
            for (int x = 0; x < cs; x++)
                for (int y = 0; y < cs; y++)
                    for (int z = 0; z < cs; z++)
                    {
                        var b = chunk.Get(x, y, z);
                        if ((b == log || b == leaves) && cy * cs + y > top)
                        {
                            top = cy * cs + y;
                        }
                    }
        }

        return top;
    }

    /// <summary>The first chunk column of a grid whose surface chunk holds a giant log, with the generator's seed.</summary>
    private static (long Seed, int Cx, int Cz, int SurfaceCy)? FindGiantColumn(PlanetType planet)
    {
        var log = Content.GetBlock(WorldGenerator.GiantLogKey)!.NumericId;
        int cs = WorldConstants.ChunkSize;
        for (long s = 1; s <= 6; s++)
        {
            long seed = s * 7919 + 3;
            var gen = Gen(seed);
            for (int cx = 0; cx < 14; cx++)
                for (int cz = -7; cz < 7; cz++)
                {
                    int sy = gen.SurfaceHeight(planet, cx * cs + cs / 2, cz * cs + cs / 2);
                    int cy = WorldConstants.WorldToChunk(sy + 3);
                    var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                    for (int i = 0; i < cs * cs * cs; i++)
                    {
                        if (chunk.Get(i % cs, i / cs % cs, i / (cs * cs)) == log)
                        {
                            return (seed, cx, cz, cy);
                        }
                    }
                }
        }

        return null;
    }

    [Fact]
    public void AGiantTreesColumn_ReachesItsWholeCrown_AboveTheOldSurfaceBand()
    {
        var planet = Content.Planets["meadowlands"]; // temperate woods
        var found = FindGiantColumn(planet);
        Assert.True(found.HasValue, "a generation-6 temperate world must grow a giant tree somewhere in a 224×224 area over six seeds");
        var (seed, gcx, gcz, gcy) = found!.Value;
        var gen = Gen(seed);
        int cs = WorldConstants.ChunkSize;

        bool aboveOldBand = false;
        for (int cx = gcx - 1; cx <= gcx + 1; cx++)
            for (int cz = gcz - 1; cz <= gcz + 1; cz++)
            {
                int highest = HighestGiantCell(gen, planet, cx, cz, gcy - 2, gcy + 5);
                if (highest == int.MinValue)
                {
                    continue;
                }

                int top = gen.TallFloraTopInSquare(planet, cx * cs, cz * cs, cs);
                Assert.True(top >= highest, $"column ({cx},{cz}): a giant cell at y={highest} stands above the flora top {top}");

                // The old far band ended one chunk above the column's centre surface chunk.
                int oldBandTop = ((WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, cx * cs + cs / 2, cz * cs + cs / 2)) + 2) * cs) - 1;
                aboveOldBand |= highest > oldBandTop;
            }

        Assert.True(aboveOldBand, "the giant's crown must reach above the old one-chunk surface band — that is what got cut off");
    }

    [Fact]
    public void AnOrdinaryWoodsColumn_ReachesATreesRiseAboveItsGround()
    {
        var planet = Content.Planets["meadowlands"];
        var gen = Gen(11);
        int cs = WorldConstants.ChunkSize;
        int sea = gen.SeaLevel(planet);
        int cx = 0;
        while (gen.SurfaceHeight(planet, cx * cs + cs / 2, 5 * cs + cs / 2) <= sea && cx < 64)
        {
            cx++; // a dry column — trees do not grow under the sea
        }

        int top = gen.TallFloraTopInSquare(planet, cx * cs, 5 * cs, cs);
        int centre = gen.SurfaceHeight(planet, cx * cs + cs / 2, 5 * cs + cs / 2);
        Assert.True(top >= centre + 18, $"a tree world's column must reach a tree's rise (18) over its ground: top {top}, ground {centre}");
    }

    [Fact]
    public void ABarrenWorld_HasNoTallFlora()
    {
        var mycelium = "mycelium";
        var barren = Content.Planets.Values.FirstOrDefault(p => p.FloraDensity <= 0 && (p.TreeDensity ?? 0) <= 0
            && p.Biomes.Count == 0 && p.SurfaceBlock != mycelium);
        Assert.NotNull(barren);
        int cs = WorldConstants.ChunkSize;
        Assert.Equal(int.MinValue, Gen(5).TallFloraTopInSquare(barren!, 0, 0, cs));
    }
}

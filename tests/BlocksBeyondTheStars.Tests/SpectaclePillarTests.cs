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
/// Pillar islands (#2336): a stem carries a crown wider than itself with air between the crown's underside and the
/// ground, every balcony overlaps its stem below the crown, a two-stem crown covers both stem tops, the crown band
/// writes the biome's ground over rock, the pad probe refuses a crown column — and nothing of it exists below
/// terrain generation 21.
/// </summary>
public sealed class SpectaclePillarTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static (WorldGenerator Gen, PlanetType Planet, List<(int[] StemX, int[] StemZ, double StemR, int StemTop, int CrownX, int CrownZ, double CrownRx, double CrownRz, int CrownTop, int Balconies, bool TwoStorey, bool Pond)> Islands)
        FindIslands(string type, Func<(int[] StemX, int[] StemZ, double StemR, int StemTop, int CrownX, int CrownZ, double CrownRx, double CrownRz, int CrownTop, int Balconies, bool TwoStorey, bool Pond), bool>? pick = null)
    {
        var planet = Content.Planets[type];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, WorldDescription.SpectacleGeneration);
            // Only islands whose stems the pillar row owns: an earlier landmark row (a rift, a cenote, a volcano) that
            // happens to cover a stem column keeps it — one landmark per column — and such an island never stands.
            var islands = gen.PillarIslandsForTest(planet)
                .Where(i => Enumerable.Range(0, i.StemX.Length).All(k => gen.SurfaceHeight(planet, i.StemX[k], i.StemZ[k]) == i.StemTop))
                .ToList();
            if (pick is not null)
            {
                islands = islands.Where(pick).ToList();
            }

            if (islands.Count > 0)
            {
                return (gen, planet, islands);
            }
        }

        Assert.Fail($"no pillar island found on 40 {type} worlds");
        return default;
    }

    [Fact]
    public void AStem_CarriesACrownWiderThanItself_WithAirUnderTheRim()
    {
        var (gen, planet, islands) = FindIslands("karst", i => i.StemX.Length == 1);
        var island = islands[0];
        int sx = island.StemX[0], sz = island.StemZ[0];

        // The stem top is level with the crown's seat, and it is a tower: the ground around the foot lies far below.
        Assert.Equal(island.StemTop, gen.SurfaceHeight(planet, sx, sz));
        int footX = sx + (int)Math.Round(island.StemR) + 2;
        Assert.True(gen.SurfaceHeight(planet, footX, sz) < island.StemTop - 20, "the pillar stands at least twenty over its foot");

        // The crown reaches past the stem, and under its rim there is air down to the ground.
        int rimX = sx + (int)Math.Round(island.StemR) + 3;
        Assert.True(island.CrownRx > island.StemR + 2, "a crown is wider than its stem");
        Assert.True(gen.TryGetHighestBand(planet, rimX, sz, out var rim), "the crown covers the column beside the stem");
        Assert.Equal(WorldGenerator.BandKind.Crown, rim.Kind);
        Assert.True(rim.Bottom > gen.SurfaceHeight(planet, rimX, sz) + 10, "air between the crown's underside and the ground");
        Assert.True(rim.Top >= island.StemTop + 5, "the crown is a thick lens, not a lid");

        // The pad planner refuses the crown column and the stem column alike.
        Assert.True(gen.ColumnHasBandAbove(planet, rimX, sz));
        Assert.True(gen.ColumnHasBandAbove(planet, sx, sz));
    }

    [Fact]
    public void ATwoStemCrown_CoversBothStemTops_AndTheyAreLevel()
    {
        var (gen, planet, islands) = FindIslands("karst", i => i.StemX.Length == 2);
        var island = islands[0];
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(island.StemTop, gen.SurfaceHeight(planet, island.StemX[i], island.StemZ[i]));
            Assert.True(gen.TryGetHighestBand(planet, island.StemX[i], island.StemZ[i], out var band), "the crown stands on each stem");
            Assert.Equal(island.CrownTop, band.Top);
        }

        // The midpoint between the stems is bridged by the crown and open underneath.
        Assert.True(gen.TryGetHighestBand(planet, island.CrownX, island.CrownZ, out var mid));
        Assert.Equal(island.CrownTop, mid.Top);
        Assert.True(mid.Bottom > gen.SurfaceHeight(planet, island.CrownX, island.CrownZ) + 10);
    }

    [Fact]
    public void Balconies_OverlapTheirStem_BelowTheCrown()
    {
        var (gen, planet, islands) = FindIslands("rocky", i => i.Balconies > 0 && i.StemX.Length == 1);
        var island = islands[0];
        int sx = island.StemX[0], sz = island.StemZ[0];
        int r = (int)Math.Round(island.StemR);
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];

        int balconyColumns = 0;
        foreach (var (dx, dz) in new[] { (r + 2, 0), (-r - 2, 0), (0, r + 2), (0, -r - 2), (r + 1, r + 1), (-r - 1, r + 1), (r + 1, -r - 1), (-r - 1, -r - 1) })
        {
            int n = gen.GetExtraBands(planet, sx + dx, sz + dz, bands);
            for (int b = 0; b < n; b++)
            {
                if (bands[b].Kind == WorldGenerator.BandKind.Crown && bands[b].Top < island.StemTop - 4)
                {
                    balconyColumns++;
                    Assert.True(bands[b].Top > gen.SurfaceHeight(planet, sx + dx, sz + dz), "a balcony hangs above the ground");
                    Assert.True(bands[b].Top - bands[b].Bottom + 1 is >= 2 and <= 4, "a balcony is two to four thick");
                }
            }
        }

        Assert.True(balconyColumns > 0, "a stem with balconies carries a balcony band right beside its wall");
    }

    [Fact]
    public void TheCrownBand_WritesTheBiomesGroundOverRock()
    {
        var (gen, planet, islands) = FindIslands("karst", i => i.StemX.Length == 1 && !i.Pond);
        var island = islands[0];
        int x = island.StemX[0] + (int)Math.Round(island.StemR) + 3, z = island.StemZ[0];
        Assert.True(gen.TryGetHighestBand(planet, x, z, out var crown));

        var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(crown.Top), WorldConstants.WorldToChunk(z));
        var chunk = gen.Generate(planet, coord);
        int lx = WorldConstants.WorldToLocal(x), lz = WorldConstants.WorldToLocal(z);
        BlockId At(int y) => y >= coord.Y * 16 && y < coord.Y * 16 + 16 ? chunk.Get(lx, y - coord.Y * 16, lz) : gen.Generate(planet, new ChunkCoord(coord.X, WorldConstants.WorldToChunk(y), coord.Z)).Get(lx, WorldConstants.WorldToLocal(y), lz);

        var stone = Content.GetBlock("stone")!.NumericId;
        var top = At(crown.Top);
        var sub = At(crown.Top - 1);
        // The crown wears a biome's ground of its own height (grass / dirt, mud, or snow on a cold crown) over rock.
        var ice = Content.GetBlock("ice")!.NumericId;
        var surfaces = planet.Biomes.Select(b => Content.GetBlock(b.SurfaceBlock)!.NumericId).Append(Content.GetBlock("snow")!.NumericId).Append(ice).ToList();
        var subs = planet.Biomes.Select(b => Content.GetBlock(b.SubSurfaceBlock)!.NumericId).Append(ice).ToList();
        string Name(BlockId id) => Content.Blocks.Values.FirstOrDefault(b => b.NumericId == id)?.Key ?? id.ToString();
        Assert.True(surfaces.Contains(top), $"crown top is {Name(top)}");
        Assert.True(subs.Contains(sub), $"crown sub is {Name(sub)}");
        Assert.Equal(stone, At(crown.Top - 4)); // the band's material: karst has no sandstone tag → the deep block
        Assert.True(At(crown.Top + 1).IsAir || At(crown.Top + 1) != stone, "air or a plant above the meadow");
    }

    [Fact]
    public void NothingBelowGenerationTwentyOne_AndNothingOffTheTag()
    {
        var planet = Content.Planets["karst"];
        var g20 = Gen(6154, WorldDescription.SpectacleGeneration - 1);
        Assert.Empty(g20.PillarIslandsForTest(planet));
        Assert.DoesNotContain("pillar-island", g20.LandmarkOrderForTest(planet));

        var g21 = Gen(6154, WorldDescription.SpectacleGeneration);
        Assert.Contains("pillar-island", g21.LandmarkOrderForTest(planet));
        Assert.DoesNotContain("pillar-island", g21.LandmarkOrderForTest(Content.Planets["ice"])); // no `pillars` tag
        Assert.DoesNotContain("pillar-island", g21.LandmarkOrderForTest(Content.Planets["skylands"])); // a sky world never
    }
}

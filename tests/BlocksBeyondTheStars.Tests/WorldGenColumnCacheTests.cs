// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1526/#1527: the world generator memoises per-column work (surface height, forest mask, the whole column
/// phase of Generate) and samples the torus noise fields through per-column lattices. None of it may change a
/// single block — the goldens pin the chunks, these tests pin the mechanisms: the lattice sampler is bit-identical
/// to <see cref="Noise.ValueTorus"/>, a warm generator produces exactly what a cold one does, and a world-mode
/// change drops every memo.
/// </summary>
public class WorldGenColumnCacheTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void TorusColumnSampler_IsBitIdenticalToValueTorus()
    {
        var rng = new Random(20260904);
        (double X, double Y, double Z)[] scales = { (22.0, 16.0, 22.0), (9.0, 9.0, 9.0), (4.5, 4.5, 4.5), (56.0, 40.0, 56.0), (18.0, 1.0, 18.0) };
        int[] circumferences = { 800, 5472, 6000, 12000 };
        int compared = 0;
        for (int trial = 0; trial < 400; trial++)
        {
            long seed = rng.NextInt64(long.MinValue, long.MaxValue);
            int circ = circumferences[rng.Next(circumferences.Length)];
            int lat = WorldConstants.LatitudePeriodFor(circ);
            var (sx, sy, sz) = scales[rng.Next(scales.Length)];
            int x = rng.Next(-circ, 2 * circ);
            int z = rng.Next(-lat, 2 * lat);
            var sampler = new TorusColumnSampler(seed, x, z, circ, lat, sx, sy, sz);

            // A monotone sweep (the y-loop), then random jumps (the loop crossing lattice rows both ways).
            int y0 = rng.Next(-2100, 300);
            for (int y = y0; y < y0 + 40; y++)
            {
                Check(seed, x, y, z, circ, lat, sx, sy, sz, ref sampler);
                compared++;
            }

            for (int k = 0; k < 20; k++)
            {
                Check(seed, x, rng.Next(-2200, 320), z, circ, lat, sx, sy, sz, ref sampler);
                compared++;
            }
        }

        Assert.Equal(400 * 60, compared);

        static void Check(long seed, int x, int y, int z, int circ, int lat, double sx, double sy, double sz, ref TorusColumnSampler sampler)
        {
            double expected = Noise.ValueTorus(seed, x, y, z, circ, lat, sx, sy, sz);
            double actual = sampler.Sample(y);
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        }
    }

    [Theory]
    [InlineData("varied", 424242L)]
    [InlineData("jungle", 20260903L)]
    [InlineData("ice", 7L)]
    public void SurfaceHeight_CachedPath_EqualsTheUncachedOne_AndForgetsOnWorldModeChange(string planetKey, long seed)
    {
        var planet = Content.GetPlanet(planetKey)!;
        var gen = new WorldGenerator(seed, Content);
        (int X, int Z)[] columns = { (0, 0), (100, 37), (-200, 150), (5999, 12), (6000, 12), (3000, -700) };

        foreach (var (x, z) in columns)
        {
            int uncached = gen.SurfaceHeightUncached(planet, x, z);
            Assert.Equal(uncached, gen.SurfaceHeight(planet, x, z));
            Assert.Equal(uncached, gen.SurfaceHeight(planet, x, z)); // the memo hit
        }

        gen.SetWorldMode(5472, cratered: false, landingPads: null, locationId: "cache-test:body");
        foreach (var (x, z) in columns)
        {
            Assert.Equal(gen.SurfaceHeightUncached(planet, x, z), gen.SurfaceHeight(planet, x, z));
        }

        gen.SetWorldMode(800, cratered: true, landingPads: null, locationId: "cache-test:moon");
        foreach (var (x, z) in columns)
        {
            Assert.Equal(gen.SurfaceHeightUncached(planet, x, z), gen.SurfaceHeight(planet, x, z));
        }
    }

    [Theory]
    [InlineData("varied", 424242L, 0)]
    [InlineData("varied", 424242L, 5472)]
    [InlineData("jungle", 20260903L, 0)]
    [InlineData("rocky", 1L, 0)]
    [InlineData("ocean", 424242L, 0)]
    [InlineData("asteroid", 14L, 800)]
    public void WarmGenerator_ProducesWhatAColdOneDoes_ForEveryStackedChunk(string planetKey, long seed, int circumference)
    {
        var planet = Content.GetPlanet(planetKey)!;
        var warm = new WorldGenerator(seed, Content);
        if (circumference > 0)
        {
            warm.SetWorldMode(circumference, cratered: planetKey == "asteroid", landingPads: null, locationId: "cache-test:" + planetKey);
        }

        (int X, int Z)[] columns = { (0, 0), (100, 37), (-200, 150) };
        int hashedChunks = 0;
        foreach (var (x, z) in columns)
        {
            int cx = WorldConstants.WorldToChunk(x), cz = WorldConstants.WorldToChunk(z);
            int surfaceCy = WorldConstants.WorldToChunk(warm.SurfaceHeight(planet, x, z));
            // Deep → surface → high, then the surface chunk AGAIN: the second pass is served entirely from the
            // column memos and the samplers, the first pass built them.
            foreach (int cy in new[] { surfaceCy - 6, surfaceCy - 3, surfaceCy, surfaceCy + 2, surfaceCy + 5, surfaceCy })
            {
                var cold = new WorldGenerator(seed, Content);
                if (circumference > 0)
                {
                    cold.SetWorldMode(circumference, cratered: planetKey == "asteroid", landingPads: null, locationId: "cache-test:" + planetKey);
                }

                var coord = new ChunkCoord(cx, cy, cz);
                ulong expected = WorldGenerationGoldenTests.HashChunk(cold.Generate(planet, coord));
                ulong actual = WorldGenerationGoldenTests.HashChunk(warm.Generate(planet, coord));
                Assert.True(expected == actual, $"{planetKey} chunk {coord}: warm generator differs from a cold one");
                hashedChunks++;
            }
        }

        Assert.Equal(18, hashedChunks);
        Assert.True(warm.CachedColumnProfiles >= 3 * 256, "the column profiles were not memoised");
    }

    [Fact]
    public void WorldModeChange_DropsTheColumnProfiles()
    {
        var planet = Content.GetPlanet("varied")!;
        var gen = new WorldGenerator(424242, Content);
        gen.Generate(planet, new ChunkCoord(0, 3, 0));
        Assert.Equal(256, gen.CachedColumnProfiles);

        gen.SetWorldMode(5472, cratered: false, landingPads: null, locationId: "cache-test:body");
        Assert.Equal(0, gen.CachedColumnProfiles);

        // ...and the new mode's chunk equals a cold generator's chunk in that mode.
        var cold = new WorldGenerator(424242, Content);
        cold.SetWorldMode(5472, cratered: false, landingPads: null, locationId: "cache-test:body");
        var coord = new ChunkCoord(0, 3, 0);
        Assert.Equal(WorldGenerationGoldenTests.HashChunk(cold.Generate(planet, coord)),
            WorldGenerationGoldenTests.HashChunk(gen.Generate(planet, coord)));
    }
}

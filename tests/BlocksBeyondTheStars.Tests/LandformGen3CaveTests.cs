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
/// Terrain generation 3, part 3 — the caves: dripstone hanging from the roofs and rising from the floors of
/// tunnels and caverns on wet karst / wetland worlds, and the karst cathedrals (caverns up to 40 tall instead
/// of 28). Both are absent below generation 3, so every classic and generation-1 cave is untouched.
/// </summary>
public sealed class LandformGen3CaveTests
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

    /// <summary>A worm span on a karst world that takes dripstone: both lengths non-zero, room for both plus an
    /// air cell, rock above the roof, above the lava table (so the molten-pocket rule cannot claim a cell), and
    /// not an underground river's passage (its floor is water). Null when the seed has none on the sample grid.</summary>
    private static (int X, int Z, int Lo, int Hi, int Down, int Up)? FindDrippingSpan(WorldGenerator gen, PlanetType planet, bool bothEnds)
    {
        var spans = new (int Lo, int Hi)[16];
        foreach (var (x, z) in Grid(13, 11))
        {
            var (down, up) = gen.DripstoneForTest(planet, x, z);
            if (down == 0 || (bothEnds && up == 0))
            {
                continue;
            }

            int n = gen.TunnelSpans(planet, x, z, spans);
            if (n == 0 || gen.TryGetUndergroundRiver(planet, x, z, out _, out _, out _))
            {
                continue;
            }

            int surface = gen.SurfaceHeight(planet, x, z);
            for (int i = 0; i < n; i++)
            {
                var (lo, hi) = spans[i];
                if (hi - lo >= down + up + 2 && hi < surface - 1 && surface - lo < 60)
                {
                    return (x, z, lo, hi, down, up);
                }
            }
        }

        return null;
    }

    // ---------- dripstone in tunnels ----------

    /// <summary>Tropfsteinhöhlen: in a worm tunnel the cells under the roof are dripstone for the column's
    /// hang length, the cells above the floor for its rise length, and the middle stays air. On a karst world
    /// the dripstone block is salt, the white of the travertine repaint.</summary>
    [Fact]
    public void Dripstone_HangsFromRoofs_AndRisesFromFloors()
    {
        var planet = Content.Planets["karst"];
        var salt = Content.GetBlock("salt")!.NumericId;
        (int X, int Z, int Lo, int Hi, int Down, int Up)? hit = null;
        WorldGenerator gen = null!;
        for (long s = 1; s <= 40 && hit is null; s++)
        {
            gen = Gen(s * 6151 + 3, 3);
            hit = FindDrippingSpan(gen, planet, bothEnds: true);
        }

        Assert.True(hit.HasValue, "no worm span with dripstone at both ends on the sample grid in 40 seeds");
        var (x, z, lo, hi, down, up) = hit!.Value;
        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        for (int k = 0; k < down; k++)
        {
            Assert.True(Cell(gen, planet, chunks, x, hi - k, z) == salt, $"roof cell {k} below Hi={hi} at ({x},{z}) is not dripstone");
        }

        for (int k = 0; k < up; k++)
        {
            Assert.True(Cell(gen, planet, chunks, x, lo + k, z) == salt, $"floor cell {k} above Lo={lo} at ({x},{z}) is not dripstone");
        }

        int mid = (lo + up + hi - down) / 2;
        Assert.True(Cell(gen, planet, chunks, x, mid, z).IsAir, $"the cell between the formations at y={mid} is not air");
    }

    /// <summary>The generation-1 world with the same seed has no dripstone in any of its spans: a tunnel cell is
    /// air or a lava pocket, never the dripstone block. (Sampled over the first thirty spans found.)</summary>
    [Fact]
    public void Dripstone_IsAbsent_OnGenerationOne()
    {
        var planet = Content.Planets["karst"];
        var salt = Content.GetBlock("salt")!.NumericId;
        var gen = Gen(20260903, 1);
        Assert.Equal((0, 0), gen.DripstoneForTest(planet, 10, 10));

        var spans = new (int Lo, int Hi)[16];
        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        int seen = 0;
        foreach (var (x, z) in Grid(13, 11))
        {
            int n = gen.TunnelSpans(planet, x, z, spans);
            for (int i = 0; i < n && seen < 30; i++, seen++)
            {
                var (lo, hi) = spans[i];
                for (int y = lo; y <= hi; y++)
                {
                    Assert.True(Cell(gen, planet, chunks, x, y, z) != salt, $"generation 1 has a salt cell inside a span at ({x},{y},{z})");
                }
            }

            if (seen >= 30)
            {
                break;
            }
        }

        Assert.True(seen >= 30, "fewer than thirty spans on the sample grid");
    }

    // ---------- dripstone in caverns ----------

    /// <summary>A mega-cavern takes the same formations at its own roof and floor; the floor spike stands even
    /// where the cavern's lake would fill the cell (a rock rising out of the water).</summary>
    [Fact]
    public void Dripstone_StandsInCaverns()
    {
        var planet = Content.Planets["karst"];
        var salt = Content.GetBlock("salt")!.NumericId;
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            foreach (var (x, z) in Grid(9, 9))
            {
                if (!gen.TryGetCavernSpan(planet, x, z, out int lo, out int hi, out _))
                {
                    continue;
                }

                var (down, up) = gen.DripstoneForTest(planet, x, z);
                if (down == 0 || up == 0 || hi - lo < down + up + 2)
                {
                    continue;
                }

                var chunks = new Dictionary<ChunkCoord, ChunkData>();
                for (int k = 0; k < down; k++)
                {
                    Assert.True(Cell(gen, planet, chunks, x, hi - k, z) == salt, $"cavern roof cell {k} at ({x},{z}) is not dripstone");
                }

                for (int k = 0; k < up; k++)
                {
                    Assert.True(Cell(gen, planet, chunks, x, lo + k, z) == salt, $"cavern floor cell {k} at ({x},{z}) is not dripstone");
                }

                Assert.True(Cell(gen, planet, chunks, x, hi - down, z) != salt, "the cell under the roof spike is dripstone too");
                return;
            }
        }

        Assert.Fail("no cavern column with dripstone at both ends in 40 seeds");
    }

    // ---------- karst cathedrals ----------

    /// <summary>Höhlenkathedralen: on a karst world a generation-3 cavern can stand taller than any generation-1
    /// cavern (28 half-height → 56 tall at most); on generation 1 it never does, on a non-karst world neither.</summary>
    [Fact]
    public void KarstCaverns_AreTaller_OnlyFromGenerationThree()
    {
        var karst = Content.Planets["karst"];
        var highland = Content.Planets["highland"];
        int Tallest(WorldGenerator gen, PlanetType planet)
        {
            int tallest = 0;
            foreach (var (x, z) in Grid(5, 5))
            {
                if (gen.TryGetCavernSpan(planet, x, z, out int lo, out int hi, out _))
                {
                    tallest = Math.Max(tallest, hi - lo);
                }
            }

            return tallest;
        }

        bool cathedral = false;
        for (long s = 1; s <= 12; s++)
        {
            long seed = s * 6151 + 3;
            Assert.True(Tallest(Gen(seed, 1), karst) <= 56, $"a generation-1 cavern taller than 56 (seed {seed})");
            Assert.True(Tallest(Gen(seed, 3), highland) <= 56, $"a non-karst generation-3 cavern taller than 56 (seed {seed})");
            cathedral |= Tallest(Gen(seed, 3), karst) > 56;
        }

        Assert.True(cathedral, "no karst cavern taller than 56 in 12 seeds");
    }

    // ---------- gate ----------

    /// <summary>The gate: wet karst / wetland worlds from generation 3 only — never the dry, the cratered or
    /// the highland control world.</summary>
    [Fact]
    public void Dripstone_Gate_KarstAndWetlandWorlds_FromGenerationThree()
    {
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        foreach (var key in new[] { "karst", "jungle", "swamp", "ocean", "boreal", "archipelago" })
        {
            Assert.True(g3.WonderGatesForTest(Content.Planets[key])["dripstone"], $"{key} should drip on generation 3");
            Assert.False(g1.WonderGatesForTest(Content.Planets[key])["dripstone"], $"{key} must not drip on generation 1");
        }

        foreach (var key in new[] { "highland", "desert", "rocky", "lava", "moon" }.Where(Content.Planets.ContainsKey))
        {
            Assert.False(g3.WonderGatesForTest(Content.Planets[key])["dripstone"], $"{key} must not drip");
        }
    }
}

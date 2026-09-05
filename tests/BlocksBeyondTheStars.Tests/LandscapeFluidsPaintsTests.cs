// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1647 (landscape variety 4/6): the five new blocks, the generation-1 water / lava bodies and the surface
/// paints. The body tests prove the ONE rule that matters — what the column phase fills is exactly what the
/// surface-water helpers report — plus the presence and shape of each body and paint on an eligible world,
/// and that generation 0 sees none of it.
/// </summary>
public sealed class LandscapeFluidsPaintsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] NewBlocks = { "moss_stone", "tar", "bone", "sandstone", "scree" };

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

    // ---------- content ----------

    [Fact]
    public void NewBlocks_Resolve_WithItems_Locales_AndTextures()
    {
        string texDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");
        foreach (var key in NewBlocks)
        {
            var block = Content.GetBlock(key);
            Assert.NotNull(block);
            Assert.Equal("terrain", block!.Category);
            foreach (var drop in block.Drops)
            {
                Assert.NotNull(Content.GetItem(drop.Item));
            }

            var item = Content.GetItem(key);
            Assert.NotNull(item);
            Assert.Equal(key, item!.PlacesBlock);
            Assert.True(File.Exists(Path.Combine(texDir, key + ".bytes")), $"{key}.bytes missing");
        }

        foreach (var locale in new[] { "en", "de" })
        {
            var text = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", locale + ".json"));
            foreach (var key in NewBlocks)
            {
                Assert.Contains($"\"block.{key}.name\"", text);
                Assert.Contains($"\"item.{key}.name\"", text);
            }
        }
    }

    // ---------- generation 0 ----------

    [Fact]
    public void GenerationZero_HasNoNewBodies_AndNoNewBlocksOnTheSurface()
    {
        var ids = NewBlocks.Select(k => Content.GetBlock(k)!.NumericId).ToHashSet();
        foreach (var key in new[] { "jungle", "desert", "ocean", "highland", "swamp" })
        {
            var planet = Content.Planets[key];
            var gen = Gen(4711, 0);
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z += 257)
                for (int x = 0; x < circ; x += 311)
                {
                    Assert.Null(gen.Gen1WaterForTest(planet, x, z));
                    Assert.Equal(0, gen.SurfaceGen1WaterDepth(planet, x, z));
                    var (marsh, ring, playa) = gen.Gen1PaintRegionsForTest(planet, x, z);
                    Assert.False(marsh || ring || playa);
                }

            // A surface chunk of the classic world never contains one of the new blocks.
            var chunk = gen.Generate(planet, new ChunkCoord(3, WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, 100, 37)), 1));
            int cs = WorldConstants.ChunkSize;
            for (int x = 0; x < cs; x++)
                for (int y = 0; y < cs; y++)
                    for (int z = 0; z < cs; z++)
                    {
                        Assert.DoesNotContain(chunk.Get(x, y, z), ids);
                    }
        }
    }

    // ---------- helper agreement ----------

    /// <summary>For every sampled column: the helper's water column == the generated column's water cells.</summary>
    private static void AssertHelpersAgree(WorldGenerator gen, PlanetType planet, int step, out int waterColumns)
    {
        var water = Content.GetBlock("water")!.NumericId;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        waterColumns = 0;
        var chunks = new Dictionary<(int, int, int), ChunkData>();
        ChunkData ChunkAt(int x, int y, int z)
        {
            var c = (WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
            if (!chunks.TryGetValue(c, out var chunk))
            {
                chunks[c] = chunk = gen.Generate(planet, new ChunkCoord(c.Item1, c.Item2, c.Item3));
            }

            return chunk;
        }

        int cs = WorldConstants.ChunkSize;
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < circ; x += step)
            {
                int surface = gen.SurfaceHeight(planet, x, z);
                bool helperWater = gen.TryGetWaterSurface(planet, x, z, out int top, out int bed);
                // The generated cell just above the reported bed must be water, and the cell above the reported
                // top must not be; a column the helper calls dry has no water right above its surface.
                if (helperWater)
                {
                    waterColumns++;
                    var probe = ChunkAt(x, bed + 1, z);
                    var cell = probe.Get(((x % cs) + cs) % cs, ((bed + 1) % cs + cs) % cs, ((z % cs) + cs) % cs);
                    // The fill may put aquatic flora (kelp, coral, seagrass, lily) or an ice sheet into that water cell.
                    var def = Content.Blocks.Values.FirstOrDefault(b => b.NumericId == cell);
                    bool wet = cell == water || def?.Key == "ice" || (def?.Category == "flora");
                    Assert.True(wet, $"{planet.Key} ({x},{z}): helper reports water over bed {bed}, generated cell is {def?.Key ?? cell.ToString()}");
                    var above = ChunkAt(x, top + 1, z);
                    Assert.True(above.Get(((x % cs) + cs) % cs, ((top + 1) % cs + cs) % cs, ((z % cs) + cs) % cs) != water,
                        $"{planet.Key} ({x},{z}): water generated above the helper's top {top}");
                }
                else
                {
                    var probe = ChunkAt(x, surface + 1, z);
                    var cell = probe.Get(((x % cs) + cs) % cs, ((surface + 1) % cs + cs) % cs, ((z % cs) + cs) % cs);
                    Assert.True(cell != water, $"{planet.Key} ({x},{z}): generated water at {surface + 1} but the helper says dry");
                }
            }
    }

    [Theory]
    [InlineData("swamp", 101)]   // marsh sheets
    [InlineData("desert", 53)]   // oases + playas
    [InlineData("jungle", 127)]  // hot springs, marshes, maars
    [InlineData("tundra", 131)]  // tarns
    public void SurfaceWaterHelpers_AgreeWithTheGeneratedColumns_OnGenerationOneWorlds(string key, int step)
    {
        var planet = Content.Planets[key];
        int totalWater = 0;
        for (long s = 1; s <= 3; s++)
        {
            AssertHelpersAgree(Gen(s * 6151 + 3, 1), planet, step, out int n);
            totalWater += n;
        }

        Assert.True(totalWater > 0, $"no water column sampled on {key}");
    }

    // ---------- the bodies exist ----------

    private static int CountBodies(PlanetType planet, string fluid, int step, int seeds, Func<(int Top, int Bed, string Fluid), bool>? where = null)
    {
        int count = 0;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (long s = 1; s <= seeds; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            for (int z = -period / 2; z < period / 2; z += step)
                for (int x = 0; x < circ; x += step)
                {
                    var body = gen.Gen1WaterForTest(planet, x, z);
                    if (body is { } b && b.Fluid == fluid && (where is null || where(b)))
                    {
                        count++;
                    }
                }
        }

        return count;
    }

    [Fact]
    public void Marshes_SpreadOneDeepSheets_OverWetlandFlats()
    {
        var swamp = Content.Planets["swamp"];
        int sheets = CountBodies(swamp, "water", 23, 2, b => b.Top - b.Bed == 1);
        Assert.True(sheets > 100, $"only {sheets} marsh-sheet columns on the swamp");
    }

    [Fact]
    public void Oases_PondInTheDesert_WithAGrassRing()
    {
        var desert = Content.Planets["desert"];
        var grass = Content.GetBlock("grass")!.NumericId;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            for (int z = -period / 2; z < period / 2; z += 7)
                for (int x = 0; x < circ; x += 7)
                {
                    var (_, ring, _) = gen.Gen1PaintRegionsForTest(desert, x, z);
                    if (!ring)
                    {
                        continue;
                    }

                    // A ring column is painted grass on the surface, and the pond lies within 14 blocks.
                    int sy = gen.SurfaceHeight(desert, x, z);
                    var chunk = gen.Generate(desert, new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(sy), WorldConstants.WorldToChunk(z)));
                    int cs = WorldConstants.ChunkSize;
                    var top = chunk.Get(((x % cs) + cs) % cs, ((sy % cs) + cs) % cs, ((z % cs) + cs) % cs);
                    bool pondNear = false;
                    for (int dx = -14; dx <= 14 && !pondNear; dx += 2)
                        for (int dz = -14; dz <= 14 && !pondNear; dz += 2)
                        {
                            pondNear = gen.SurfaceGen1WaterDepth(desert, x + dx, z + dz) > 0;
                        }

                    Assert.True(pondNear, $"oasis ring at ({x},{z}) has no pond nearby");
                    if (top == grass)
                    {
                        return; // ring painted (a beach/snow paint may win on odd columns — one green column proves it)
                    }
                }
        }

        Assert.Fail("no oasis ring column found in 40 desert worlds");
    }

    [Fact]
    public void CalderaAndShieldLakes_HoldLava_OnVolcanicWorlds()
    {
        var lavaWorld = Content.Planets["lava"];
        int lava = CountBodies(lavaWorld, "lava", 29, 12);
        Assert.True(lava > 0, "no lava lake found on 12 lava worlds");
    }

    [Fact]
    public void Playas_PaintSalt_OnDesertFlats()
    {
        var desert = Content.Planets["desert"];
        var salt = Content.GetBlock("salt")!.NumericId;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            for (int z = -period / 2; z < period / 2; z += 11)
                for (int x = 0; x < circ; x += 11)
                {
                    var (_, _, playa) = gen.Gen1PaintRegionsForTest(desert, x, z);
                    if (!playa || gen.SurfaceGen1WaterDepth(desert, x, z) > 0)
                    {
                        continue;
                    }

                    int sy = gen.SurfaceHeight(desert, x, z);
                    var chunk = gen.Generate(desert, new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(sy), WorldConstants.WorldToChunk(z)));
                    int cs = WorldConstants.ChunkSize;
                    if (chunk.Get(((x % cs) + cs) % cs, ((sy % cs) + cs) % cs, ((z % cs) + cs) % cs) == salt)
                    {
                        return;
                    }
                }
        }

        Assert.Fail("no salt-painted playa column found in 40 desert worlds");
    }

    // ---------- paints ----------

    private static int CountSurfaceBlock(WorldGenerator gen, PlanetType planet, BlockId block, int chunksPerAxis, int stride)
    {
        int count = 0;
        int cs = WorldConstants.ChunkSize;
        for (int cx = 0; cx < chunksPerAxis; cx++)
            for (int cz = 0; cz < chunksPerAxis; cz++)
            {
                int sx = cx * cs * stride, sz = cz * cs * stride - 800;
                int surfaceCy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, sx, sz));
                foreach (int cy in new[] { surfaceCy, surfaceCy + 1 })
                {
                    var chunk = gen.Generate(planet, new ChunkCoord(WorldConstants.WorldToChunk(sx), cy, WorldConstants.WorldToChunk(sz)));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                            for (int y = 0; y < cs; y++)
                            {
                                if (chunk.Get(x, y, z) == block)
                                {
                                    count++;
                                }
                            }
                }
            }

        return count;
    }

    [Fact]
    public void Scree_CoversSteepSlopes_OnGenerationOne_Only()
    {
        var highland = Content.Planets["highland"];
        var scree = Content.GetBlock("scree")!.NumericId;
        Assert.Equal(0, CountSurfaceBlock(Gen(77, 0), highland, scree, 8, 5));
        int gen1 = 0;
        for (long s = 1; s <= 6 && gen1 == 0; s++)
        {
            gen1 = CountSurfaceBlock(Gen(s * 977 + 1, 1), highland, scree, 8, 5);
        }

        Assert.True(gen1 > 0, "no scree on six generation-1 highland worlds");
    }

    [Fact]
    public void MossStone_GrowsOnTheRock_OfTemperateWetWorlds()
    {
        var varied = Content.Planets["varied"]; // temperate, water 0.55, an alpine stone biome
        var moss = Content.GetBlock("moss_stone")!.NumericId;
        int count = 0;
        for (long s = 1; s <= 6 && count == 0; s++)
        {
            count = CountSurfaceBlock(Gen(s * 977 + 1, 1), varied, moss, 8, 5);
        }

        Assert.True(count > 0, "no moss stone on six generation-1 varied worlds");
    }

    [Fact]
    public void Strata_UseSandstone_NowThatTheBlockExists()
    {
        var rocky = Content.Planets["rocky"];
        var sandstone = Content.GetBlock("sandstone")!.NumericId;
        int cs = WorldConstants.ChunkSize;
        int count = 0;
        var gen = Gen(77, 1);
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
                            if (chunk.Get(x, y, z) == sandstone)
                            {
                                count++;
                            }
                        }
            }

        Assert.True(count > 50, $"only {count} sandstone strata cells");
    }

    [Fact]
    public void LavaRivers_RouteOnEveryVolcanicType()
    {
        foreach (var planet in Content.Planets.Values.Where(p => p.HasTag(TerrainTag.Volcanic)))
        {
            var gen = Gen(5, 1);
            int lavaColumns = 0;
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2 && lavaColumns == 0; z += 17)
                for (int x = 0; x < circ; x += 19)
                {
                    if (gen.SurfaceHeight(planet, x, z) > gen.SeaLevel(planet) && gen.TryGetLavaSurface(planet, x, z, out _, out _))
                    {
                        lavaColumns++;
                        break;
                    }
                }

            Assert.True(lavaColumns > 0, $"{planet.Key}: no lava river / lake column above the sea");
        }
    }
}

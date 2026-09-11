// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// #1783: the giant trees — a separate generation-6 stamp pass on its own block pair. What has to hold: every shape
/// stays inside the pass's own scan margin and rise (a tree straddling a chunk edge is complete from either side),
/// the trunk is genuinely thick and rooted, a generation-6 wood actually grows one, a generation-5 wood never does,
/// the same chunk generates identically from a fresh generator, and the species is its own coined name.
/// </summary>
public sealed class GiantTreeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    [Fact]
    public void EveryShape_StaysInsideThePassEnvelope_AndIsThickAndRooted()
    {
        var (margin, rise) = WorldGenerator.GiantTreeEnvelopeForTest;
        foreach (var shape in new[] { WorldGenerator.GiantTreeShape.Broadleaf, WorldGenerator.GiantTreeShape.Conifer, WorldGenerator.GiantTreeShape.Jungle })
        {
            for (int v = 0; v <= 8; v++)
            {
                double sizeF = 3.0 + v * 0.25; // the 3..5 band the stamp rolls
                var cells = WorldGenerator.BuildGiantTreeForTest(shape, sizeF, v * 131 + 7);
                Assert.NotEmpty(cells);
                Assert.Contains(cells, c => c.Log);
                Assert.Contains(cells, c => !c.Log);
                foreach (var (dx, dy, dz, _) in cells)
                {
                    Assert.InRange(dx, -margin, margin);
                    Assert.InRange(dz, -margin, margin);
                    Assert.InRange(dy, 1, rise);
                }

                // The root row is a solid trunk×trunk square of logs (plus the jungle's buttress roots around it).
                int trunk = System.Math.Clamp((int)System.Math.Round(sizeF), 3, 5);
                int rootLogs = cells.Count(c => c.Log && c.Dy == 1);
                Assert.True(rootLogs >= trunk * trunk, $"{shape} at {sizeF}: {rootLogs} root logs for a {trunk}×{trunk} trunk");

                // ...and the trunk really is tall: at least 21 log rows straight up the column.
                int column = cells.Count(c => c.Log && c.Dx == 0 && c.Dz == 0);
                Assert.True(column >= 21, $"{shape} at {sizeF}: only {column} trunk cells");
            }
        }
    }

    [Fact]
    public void ThemeShape_FollowsTheWoods()
    {
        Assert.Equal(WorldGenerator.GiantTreeShape.Broadleaf, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("temperate").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.Conifer, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("tundra").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.Conifer, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("alpine").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.Jungle, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("tropical").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.Broadleaf, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("swamp").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.None, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("desert").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.None, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("fungal").PaletteFor(6)));
        Assert.Equal(WorldGenerator.GiantTreeShape.None, WorldGenerator.GiantShapeFor(FloraThemes.Resolve("crystal").PaletteFor(6)));
    }

    /// <summary>Scans a grid of chunks around the origin for the first chunk holding a giant log.</summary>
    private static ChunkCoord? FindGiantChunk(WorldGenerator gen, PlanetType planet, BlockId giantLog, int gridChunks)
    {
        int cs = WorldConstants.ChunkSize;
        for (int cx = 0; cx < gridChunks; cx++)
            for (int cz = 0; cz < gridChunks; cz++)
            {
                int sx = cx * cs + cs / 2, sz = cz * cs + cs / 2 - gridChunks * cs / 2;
                int sy = gen.SurfaceHeight(planet, sx, sz);
                var coord = new ChunkCoord(WorldConstants.WorldToChunk(sx), WorldConstants.WorldToChunk(sy + 3), WorldConstants.WorldToChunk(sz));
                var chunk = gen.Generate(planet, coord);
                for (int x = 0; x < cs; x++)
                    for (int y = 0; y < cs; y++)
                        for (int z = 0; z < cs; z++)
                        {
                            if (chunk.Get(x, y, z) == giantLog)
                            {
                                return coord;
                            }
                        }
            }

        return null;
    }

    [Fact]
    public void AGenerationSixWood_GrowsAGiant_AndTheSameChunkRegeneratesIdentically()
    {
        var planet = Content.Planets["meadowlands"]; // temperate woods
        var giantLog = Content.GetBlock(WorldGenerator.GiantLogKey)!.NumericId;
        var giantLeaves = Content.GetBlock(WorldGenerator.GiantLeavesKey)!.NumericId;
        ChunkCoord? found = null;
        long foundSeed = 0;
        for (long seed = 1; seed <= 6 && found == null; seed++)
        {
            found = FindGiantChunk(Gen(seed * 7919 + 3, WorldDescription.NewKindsGeneration), planet, giantLog, 14);
            foundSeed = seed * 7919 + 3;
        }

        Assert.True(found.HasValue, "a generation-6 temperate world must grow a giant tree somewhere in a 224×224 area over six seeds");

        // Determinism: a fresh generator produces the identical chunk, cell for cell — and the chunk above it too,
        // which is where the crown lives (it is stamped by that chunk's own pass, not copied from below).
        var a = Gen(foundSeed, WorldDescription.NewKindsGeneration);
        var b = Gen(foundSeed, WorldDescription.NewKindsGeneration);
        int cs = WorldConstants.ChunkSize;
        int leaves = 0;
        foreach (var coord in new[] { found.Value, new ChunkCoord(found.Value.X, found.Value.Y + 1, found.Value.Z), new ChunkCoord(found.Value.X, found.Value.Y + 2, found.Value.Z) })
        {
            var ca = a.Generate(planet, coord);
            var cb = b.Generate(planet, coord);
            for (int x = 0; x < cs; x++)
                for (int y = 0; y < cs; y++)
                    for (int z = 0; z < cs; z++)
                    {
                        Assert.Equal(ca.Get(x, y, z), cb.Get(x, y, z));
                        if (ca.Get(x, y, z) == giantLeaves)
                        {
                            leaves++;
                        }
                    }
        }

        Assert.True(leaves > 0, "the stacked chunks above the trunk must carry the giant crown");
    }

    [Fact]
    public void AGenerationFiveWood_NeverGrowsOne()
    {
        var planet = Content.Planets["meadowlands"];
        var giantLog = Content.GetBlock(WorldGenerator.GiantLogKey)!.NumericId;
        for (long seed = 1; seed <= 3; seed++)
        {
            Assert.Null(FindGiantChunk(Gen(seed * 7919 + 3, WorldDescription.NewKindsGeneration - 1), planet, giantLog, 10));
        }
    }

    [Fact]
    public void TheGiantSpecies_IsItsOwnName_AndDeterministic()
    {
        var planet = Content.Planets["meadowlands"];
        var giant = TreeGenerator.GenerateGiant(planet, 123456);
        var tree = TreeGenerator.Generate(planet, 123456);
        Assert.NotNull(giant);
        Assert.NotNull(tree);
        Assert.Equal("tr1", giant!.Id);
        Assert.Equal("tr0", tree!.Id);
        Assert.NotEqual(tree.Name, giant.Name);
        Assert.Equal(giant.Name, TreeGenerator.GenerateGiant(planet, 123456)!.Name);
        Assert.Null(TreeGenerator.GenerateGiant(Content.Planets["asteroid"], 1)); // nothing grows without air
    }

    [Fact]
    public void TheGiantBlocks_AreDefined_AndTheLeavesAreFoliage()
    {
        var log = Content.GetBlock(WorldGenerator.GiantLogKey);
        var leaves = Content.GetBlock(WorldGenerator.GiantLeavesKey);
        Assert.NotNull(log);
        Assert.NotNull(leaves);
        Assert.True(log!.Flammable);
        Assert.True(leaves!.Flammable);
        Assert.Contains(log.Drops, d => d.Item == "wood_log"); // a felled giant is ordinary wood in the hand
        Assert.Empty(leaves.Drops);
    }
}

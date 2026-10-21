// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Fifi plant on about every third world (#2859, terrain generation 29): it stood in groves on every green world — about
/// 10 000 plants with glowing blossoms each, on deserts the most visible "tree" of all. A per-world roll from the roster seed
/// now decides whether it grows at all; where it grows, it grows exactly as before, and every older world keeps it.
/// </summary>
public sealed class FifiWorldShareTests
{
    private const int Gen = WorldDescription.TreeVarietyGeneration;

    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void AboutEveryThirdWorld_GrowsTheFifiPlant_AndEveryOlderWorldDoes()
    {
        Assert.Equal(1.0 / 3.0, FifiPlantRules.WorldShare);
        int worlds = 0, growing = 0;
        foreach (var key in new[] { "meadowlands", "desert", "jungle", "fungal", "corrupted" })
        {
            var planet = _content.GetPlanet(key)!;
            for (int i = 0; i < 1000; i++)
            {
                long rosterSeed = WorldGenerator.RosterSeedFor(20261020, "fifi:" + i);
                worlds++;
                growing += FifiPlantRules.GrowsOnWorld(planet, rosterSeed, Gen) ? 1 : 0;
                Assert.True(FifiPlantRules.GrowsOnWorld(planet, rosterSeed, Gen - 1));
                Assert.True(FifiPlantRules.GrowsOnWorld(planet, rosterSeed, WorldDescription.FifiPlantGeneration));
            }
        }

        Assert.InRange(growing / (double)worlds, 0.30, 0.37);
    }

    [Fact]
    public void AWorldWithoutTheRoll_GrowsNoFifiPlant_AndAWorldWithIt_GrowsTheGrovesAsBefore()
    {
        var meadow = _content.GetPlanet("meadowlands")!;
        const long seed = 7;
        string? grows = null, bare = null;
        for (int i = 0; grows is null || bare is null; i++)
        {
            string location = "fifi-world:" + i;
            if (FifiPlantRules.GrowsOnWorld(meadow, WorldGenerator.RosterSeedFor(seed, location), Gen))
            {
                grows ??= location;
            }
            else
            {
                bare ??= location;
            }
        }

        WorldGenerator Make(string location, int generation)
        {
            var gen = new WorldGenerator(seed, _content);
            gen.SetWorldMode(WorldConstants.Circumference, false, null, location);
            gen.SetTerrainGeneration(generation);
            return gen;
        }

        // Where it grows: the very groves of the older generation.
        var before = Make(grows!, Gen - 1).FifiPlantsForTest(meadow, -192, -192, 384);
        var after = Make(grows!, Gen).FifiPlantsForTest(meadow, -192, -192, 384);
        Assert.True(before.Count >= 10, $"only {before.Count} Fifi plants on the older meadow");
        Assert.Equal(before.Select(p => (p.X, p.Z, p.SurfaceY)), after.Select(p => (p.X, p.Z, p.SurfaceY)));

        // Where it does not: no plant, and no part of one in the chunk the older world grew one in.
        var olderBare = Make(bare!, Gen - 1).FifiPlantsForTest(meadow, -192, -192, 384);
        Assert.True(olderBare.Count >= 10, $"only {olderBare.Count} Fifi plants on the older meadow");
        var bareGen = Make(bare!, Gen);
        Assert.Empty(bareGen.FifiPlantsForTest(meadow, -192, -192, 384));
        var fifiIds = FifiPlant.PartKeys.Select(k => _content.GetBlock(k)!.NumericId.Value).ToHashSet();
        foreach (var p in olderBare.Take(3))
        {
            var chunk = bareGen.Generate(meadow, new ChunkCoord(WorldConstants.WorldToChunk(p.X), WorldConstants.WorldToChunk(p.SurfaceY + 3),
                WorldConstants.WorldToChunk(p.Z)));
            int cs = WorldConstants.ChunkSize;
            for (int x = 0; x < cs; x++)
                for (int y = 0; y < cs; y++)
                    for (int z = 0; z < cs; z++)
                    {
                        Assert.DoesNotContain(chunk.Get(x, y, z).Value, fifiIds);
                    }
        }
    }
}

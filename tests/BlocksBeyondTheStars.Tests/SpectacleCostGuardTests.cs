// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The spectacle package's cost guard (#2331): a generation-21 chunk of a world that carries the families as finds
/// (karst: pillar islands, daylight halls, portals) must not get meaningfully dearer than its generation-3 twin —
/// the client bakes columns on its main thread, and a hosted world's container has a fixed fence. The same rule
/// as the generation-3 guard (1.3× + 250 ms), the same defences: the real-time collection and the fastest of
/// three interleaved rounds. Slow tier: main only.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)]
public sealed class SpectacleCostGuardTests
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
    [Trait("Category", "Slow")]
    public void GenerationTwentyOne_ChunkCost_StaysNearGenerationThree()
    {
        var planet = Content.Planets["karst"];
        long Measure(int generation)
        {
            var gen = Gen(424242, generation);
            gen.Generate(planet, new ChunkCoord(0, WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, 0, 0)), 0)); // warm-up
            var sw = Stopwatch.StartNew();
            for (int cx = 0; cx < 4; cx++)
                for (int cz = 0; cz < 4; cz++)
                {
                    int cy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, cx * 16, cz * 16));
                    gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                    gen.Generate(planet, new ChunkCoord(cx, cy - 1, cz));
                }

            return sw.ElapsedMilliseconds;
        }

        long t3 = long.MaxValue, t21 = long.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            t3 = Math.Min(t3, Measure(3));
            t21 = Math.Min(t21, Measure(WorldDescription.SpectacleGeneration));
        }

        Assert.True(t21 <= 1.3 * t3 + 250,
            $"generation 21 took {t21} ms against {t3} ms for generation 3 (fastest of 3 rounds each)");
    }
}

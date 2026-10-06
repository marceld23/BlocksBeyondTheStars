// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The spectacle package's placement probes (#2334): a band over a column and a void under it are what the pad
/// planner and the structure placer ask before seating a footprint — and only on a generation-21 world, so every
/// older world's pinned pads and frozen placements never move.
/// </summary>
public sealed class SpectaclePlacementTests
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
    public void ColumnHasBandAbove_SeesASkyIsland_OnGenerationTwentyOneOnly()
    {
        var planet = Content.Planets["skylands"];
        var g21 = Gen(424242, WorldDescription.SpectacleGeneration);
        var g20 = Gen(424242, WorldDescription.SpectacleGeneration - 1);

        int covered = 0, open = 0;
        for (int x = 0; x < 1500 && covered < 10; x += 9)
            for (int z = -700; z < 700 && covered < 10; z += 13)
            {
                bool island = g21.TryGetHighestBand(planet, x, z, out var band) && band.Top > g21.SurfaceHeight(planet, x, z);
                Assert.Equal(island, g21.ColumnHasBandAbove(planet, x, z));
                Assert.False(g20.ColumnHasBandAbove(planet, x, z), "an older generation never reports a band");
                if (island)
                {
                    covered++;
                }
                else
                {
                    open++;
                }
            }

        Assert.True(covered > 0, "a sky world has islands");
        Assert.True(open > 0, "and open ground between them");
    }

    [Fact]
    public void ColumnHasVoidBelow_SeesAMegaCavern_WithinItsDepth_AndNotAbove()
    {
        var planet = Content.Planets["karst"];

        // A mega-cavern is a 22 % roll per 1700-block cell — walk seeds and the whole surface until one is found.
        bool found = false;
        for (long seed = 1; seed <= 30 && !found; seed++)
        {
            var g21 = Gen(seed * 7919 + 11, WorldDescription.SpectacleGeneration);
            var g20 = Gen(seed * 7919 + 11, WorldDescription.SpectacleGeneration - 1);
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int x = 0; x < circ && !found; x += 13)
                for (int z = -period / 2; z < period / 2 && !found; z += 11)
                {
                    if (!g21.TryGetCavernSpan(planet, x, z, out int lo, out int hi, out _))
                    {
                        continue;
                    }

                    int surface = g21.SurfaceHeight(planet, x, z);
                    Assert.True(hi < surface, "a classic mega-cavern lies under the ground");
                    Assert.True(g21.ColumnHasVoidBelow(planet, x, z, surface - lo + 2), "the hall is inside the probed depth");
                    Assert.False(g21.ColumnHasVoidBelow(planet, x, z, 3), "three cells of rock over the hall are solid");
                    Assert.False(g20.ColumnHasVoidBelow(planet, x, z, surface - lo + 2), "an older generation never reports a void");
                    Assert.False(g21.FootprintClear(planet, x - 4, z - 4, 9, 9, surface - lo + 2));
                    found = true;
                }
        }

        Assert.True(found, "thirty karst worlds carry at least one mega-cavern");
    }

    [Fact]
    public void FootprintClear_RejectsABoxUnderAnIsland_AndAcceptsOpenGround()
    {
        var planet = Content.Planets["skylands"];
        var g21 = Gen(424242, WorldDescription.SpectacleGeneration);

        bool rejected = false, accepted = false;
        for (int x = 0; x < 1500 && !(rejected && accepted); x += 9)
            for (int z = -700; z < 700 && !(rejected && accepted); z += 13)
            {
                bool clear = g21.FootprintClear(planet, x - 3, z - 3, 7, 7, 6);
                bool centreCovered = g21.ColumnHasBandAbove(planet, x, z);
                if (centreCovered)
                {
                    Assert.False(clear, "a box whose centre stands under an island is never clear");
                    rejected = true;
                }
                else if (clear)
                {
                    accepted = true;
                }
            }

        Assert.True(rejected && accepted);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pure rules of the hidden star map (#3997): how many neighbours a first entry charts, and "the nearest".</summary>
public sealed class StarChartRulesTests
{
    [Fact]
    public void NeighbourCount_IsOneToFour_AndTheSameForTheSameSystem()
    {
        for (long seed = 1; seed <= 50; seed++)
        {
            for (int i = 0; i < 64; i++)
            {
                int n = StarChartRules.NeighbourCount(seed, StarSystemIds.Rolled(i), isHome: false);
                Assert.InRange(n, 1, 4);
                Assert.Equal(n, StarChartRules.NeighbourCount(seed, StarSystemIds.Rolled(i), isHome: false));
            }
        }
    }

    [Fact]
    public void NeighbourCount_FollowsTheWeights_OneIsRare()
    {
        var counts = new int[5];
        int total = 0;
        for (long seed = 1; seed <= 400; seed++)
        {
            for (int i = 0; i < 64; i++)
            {
                counts[StarChartRules.NeighbourCount(seed, StarSystemIds.Rolled(i), isHome: false)]++;
                total++;
            }
        }

        // 10 / 35 / 35 / 20 % — within two points over 25,600 rolls.
        Assert.InRange(counts[1] * 100.0 / total, 8.0, 12.0);
        Assert.InRange(counts[2] * 100.0 / total, 33.0, 37.0);
        Assert.InRange(counts[3] * 100.0 / total, 33.0, 37.0);
        Assert.InRange(counts[4] * 100.0 / total, 18.0, 22.0);
    }

    [Fact]
    public void NeighbourCount_HomeChartsAtLeastTwo()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            Assert.InRange(StarChartRules.NeighbourCount(seed, "sys0", isHome: true), StarChartRules.HomeMinimum, 4);
        }
    }

    [Fact]
    public void Nearest_OrdersByDistance_ThenById()
    {
        var systems = new[]
        {
            new StarSystem { Id = "sys3", MapX = 10, MapY = 0 },
            new StarSystem { Id = "sys1", MapX = 0, MapY = 10 }, // as far as sys3 — the lower id wins the tie
            new StarSystem { Id = "sys2", MapX = 5, MapY = 0 },
            new StarSystem { Id = "sys4", MapX = 100, MapY = 100 },
        };

        var picked = StarChartRules.Nearest(systems, 0, 0, 3);

        Assert.Equal(new[] { "sys2", "sys1", "sys3" }, picked.Select(s => s.Id));
        Assert.Empty(StarChartRules.Nearest(systems, 0, 0, 0));
    }
}

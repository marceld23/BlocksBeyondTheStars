// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3123/#3124: star colours are dealt per galaxy — every block of four procedural systems holds one cool, one
/// warm and two calm stars — from a ramp with strong blue and red extremes, and a star never blends out of its class.</summary>
public sealed class StarColorsTests
{
    /// <summary>Blue or blue-white: more blue than red.</summary>
    internal static bool IsCool(int rgb) => (rgb & 0xFF) > ((rgb >> 16) & 0xFF);

    /// <summary>Orange, red-orange or red dwarf: green at or below the orange anchor (<c>FFBC6E</c>).</summary>
    internal static bool IsWarm(int rgb) => !IsCool(rgb) && ((rgb >> 8) & 0xFF) <= 0xBC;

    /// <summary>White, sun-like or yellow: green at or above the yellow anchor (<c>FFE2A0</c>).</summary>
    private static bool IsCalm(int rgb) => !IsCool(rgb) && ((rgb >> 8) & 0xFF) >= 0xE2;

    private static int Star(long seed, int index) => StarColors.ForSystem(seed, "sys" + index, "Name " + index);

    [Fact]
    public void EveryBlockOfFour_HoldsOneCoolOneWarmAndTwoCalmStars()
    {
        for (long seed = 1; seed <= 400; seed++)
        {
            for (int block = 0; block < 3; block++)
            {
                var stars = Enumerable.Range(block * StarColors.BlockSize, StarColors.BlockSize).Select(i => Star(seed, i)).ToList();
                Assert.Equal(1, stars.Count(IsCool));
                Assert.Equal(1, stars.Count(IsWarm));
                Assert.Equal(2, stars.Count(IsCalm));
            }
        }
    }

    [Fact]
    public void TheDealIsShuffledPerWorld_EverySlotSeesEveryClass()
    {
        // Not a fixed pattern (sys0 always blue …): over many worlds the start system takes every class.
        var starts = Enumerable.Range(1, 200).Select(seed => Star(seed, 0)).ToList();
        Assert.Contains(starts, IsCool);
        Assert.Contains(starts, IsWarm);
        Assert.Contains(starts, IsCalm);
        Assert.InRange(starts.Count(IsCalm), 70, 130); // two calm cards in four
    }

    [Fact]
    public void AStar_DependsOnlyOnTheSeedAndItsIndex_SoAGrowingGalaxyKeepsItsColours()
    {
        Assert.Equal(StarColors.ForSystem(42, "sys5", "Kel"), StarColors.ForSystem(42, "sys5", "Renamed"));
        Assert.NotEqual(
            Enumerable.Range(0, 12).Select(i => Star(1, i)),
            Enumerable.Range(0, 12).Select(i => Star(2, i)));
    }

    [Fact]
    public void TheRampReachesRealBlueAndARedDwarf()
    {
        var all = Enumerable.Range(1, 200).SelectMany(seed => Enumerable.Range(0, 12).Select(i => Star(seed, i))).ToList();
        Assert.Contains(all, rgb => ((rgb >> 16) & 0xFF) < 0xA4 && (rgb & 0xFF) == 0xFF); // blue, not light blue
        Assert.Contains(all, rgb => ((rgb >> 8) & 0xFF) < 0x84);                           // red dwarf, not orange
        Assert.All(all, rgb => Assert.True(IsCool(rgb) ^ IsWarm(rgb) ^ IsCalm(rgb), rgb.ToString("X6")));
    }

    [Fact]
    public void ASystemOutsideTheProceduralRange_TakesAStarOfTheSameRamp()
    {
        // The Guardian finale and a location outside the galaxy (a ship interior resolves to no system).
        int finale = StarColors.ForSystem(7, "guardian_finale", "Guardian Core");
        int none = StarColors.ForSystem(7, null, null);
        Assert.Equal(finale, StarColors.ForSystem(7, "guardian_finale", "Guardian Core"));
        Assert.True(IsCool(finale) || IsWarm(finale) || IsCalm(finale));
        Assert.True(IsCool(none) || IsWarm(none) || IsCalm(none));
    }
}

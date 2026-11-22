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

    // --- The rare red and blue suns (worlds of generation 48 and later) ---

    private const int Rare = WorldDescription.RareStarsGeneration;

    /// <summary>A red sun: the red-dwarf end of the ramp, not orange.</summary>
    internal static bool IsRed(int rgb) => !IsCool(rgb) && ((rgb >> 8) & 0xFF) < 0x84;

    /// <summary>A blue sun: real blue, not blue-white.</summary>
    internal static bool IsBlue(int rgb) => IsCool(rgb) && ((rgb >> 16) & 0xFF) < 0xA4;

    /// <summary>An ordinary sun: white to orange, never bluish and never red.</summary>
    private static bool IsOrdinary(int rgb) => !IsCool(rgb) && ((rgb >> 8) & 0xFF) >= 0xBC;

    private static int NewStar(long seed, int index, int count = 12)
        => StarColors.ForSystem(seed, "sys" + index, "Name " + index, Rare, count);

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(32)]
    public void EveryGalaxy_HoldsAtLeastOneRedAndOneBlueSun_AmongTheSystemsItStartsWith(int count)
    {
        for (long seed = 1; seed <= 1000; seed++)
        {
            var stars = Enumerable.Range(0, count).Select(i => NewStar(seed, i, count)).ToList();
            Assert.Contains(stars, IsRed);
            Assert.Contains(stars, IsBlue);
            Assert.All(stars, rgb => Assert.True(IsRed(rgb) ^ IsBlue(rgb) ^ IsOrdinary(rgb), rgb.ToString("X6")));
        }
    }

    [Fact]
    public void AFurtherRedOrBlueSun_IsRare()
    {
        const int Seeds = 2000;
        int moreRed = 0, moreBlue = 0, special = 0;
        for (long seed = 1; seed <= Seeds; seed++)
        {
            var stars = Enumerable.Range(0, 12).Select(i => NewStar(seed, i)).ToList();
            int red = stars.Count(IsRed), blue = stars.Count(IsBlue);
            moreRed += red > 1 ? 1 : 0;
            moreBlue += blue > 1 ? 1 : 0;
            special += red + blue;
        }

        // Ten other systems at 2 % each: about 18 % of the galaxies start with a second red sun, the same for blue.
        Assert.InRange(moreRed / (double)Seeds, 0.10, 0.27);
        Assert.InRange(moreBlue / (double)Seeds, 0.10, 0.27);
        Assert.InRange(special / (double)(Seeds * 12), 0.17, 0.23); // two promised + 0.4 expected of twelve
    }

    [Fact]
    public void ThePromisedSuns_AreTwoDifferentSystems_SpreadOverTheGalaxy()
    {
        var reds = new HashSet<int>();
        var blues = new HashSet<int>();
        for (long seed = 1; seed <= 600; seed++)
        {
            var (red, blue) = StarColors.PromisedSuns(seed, 12);
            Assert.NotEqual(red, blue);
            Assert.InRange(red, 0, 11);
            Assert.InRange(blue, 0, 11);
            reds.Add(red);
            blues.Add(blue);
        }

        Assert.Equal(12, reds.Count);  // not always the same system: every one of the twelve carries it in some world
        Assert.Equal(12, blues.Count);
    }

    [Fact]
    public void AGalaxyOfOneSystem_GetsItsSecondPromisedSunWithItsFirstFrontierSystem()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var (red, blue) = StarColors.PromisedSuns(seed, 1);
            Assert.Equal(new[] { 0, 1 }, new[] { red, blue }.OrderBy(i => i));
            Assert.True(IsRed(NewStar(seed, red, 1)));
            Assert.True(IsBlue(NewStar(seed, blue, 1)));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(12)]
    public void TheStartSystem_NeverCarriesARedOrABlueSun_AndTheGalaxyStillHoldsBoth(int count)
    {
        for (long seed = 1; seed <= 300; seed++)
        {
            for (int home = 0; home < count; home++)
            {
                string homeId = "sys" + home;
                int Sun(int i) => StarColors.ForSystem(seed, "sys" + i, "Name " + i, Rare, count, homeId);

                Assert.True(IsOrdinary(Sun(home)), $"seed {seed}, home {home}: {Sun(home):X6}");
                var (red, blue) = StarColors.PromisedSuns(seed, count, home);
                Assert.NotEqual(red, blue);
                Assert.NotEqual(home, red);
                Assert.NotEqual(home, blue);
                Assert.True(IsRed(Sun(red)));
                Assert.True(IsBlue(Sun(blue)));
                // Both lie among the systems the galaxy starts with, or — a galaxy too small to hold them beside its
                // start system — among its first frontier systems.
                Assert.InRange(red, 0, System.Math.Max(count, 3) - 1);
                Assert.InRange(blue, 0, System.Math.Max(count, 3) - 1);
            }
        }
    }

    [Fact]
    public void WithoutAKnownStartSystem_ThePromisedSunsAreWhatTheyWereBefore()
    {
        for (long seed = 1; seed <= 100; seed++)
        {
            Assert.Equal(StarColors.PromisedSuns(seed, 12), StarColors.PromisedSuns(seed, 12, -1));
            Assert.Equal(NewStar(seed, 3), StarColors.ForSystem(seed, "sys3", "Name 3", Rare, 12, null));
        }
    }

    [Fact]
    public void ANewStar_DependsOnlyOnTheSeedItsIndexAndTheStartCount_SoAGrowingGalaxyKeepsItsColours()
    {
        Assert.Equal(StarColors.ForSystem(42, "sys5", "Kel", Rare, 12), StarColors.ForSystem(42, "sys5", "Renamed", Rare, 12));
        Assert.NotEqual(
            Enumerable.Range(0, 12).Select(i => NewStar(1, i)),
            Enumerable.Range(0, 12).Select(i => NewStar(2, i)));

        // Frontier systems beyond the start count: ordinary suns, a red or blue one only now and then.
        var frontier = Enumerable.Range(1, 200).SelectMany(seed => Enumerable.Range(12, 20).Select(i => NewStar(seed, i))).ToList();
        Assert.All(frontier, rgb => Assert.True(IsRed(rgb) ^ IsBlue(rgb) ^ IsOrdinary(rgb), rgb.ToString("X6")));
        Assert.InRange(frontier.Count(rgb => !IsOrdinary(rgb)) / (double)frontier.Count, 0.02, 0.07);
    }

    [Fact]
    public void AnOlderWorld_KeepsItsDealtStarsExactly()
    {
        for (long seed = 1; seed <= 100; seed++)
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(Star(seed, i), StarColors.ForSystem(seed, "sys" + i, "Name " + i, Rare - 1, 12));
            }
        }

        Assert.Equal(StarColors.ForSystem(7, null, null), StarColors.ForSystem(7, null, null, Rare - 1, 12));
    }

    [Fact]
    public void ASystemOutsideTheProceduralRange_IsAnOrdinarySunOrRarelyARedOrBlueOne()
    {
        var finales = Enumerable.Range(1, 500).Select(seed => StarColors.ForSystem(seed, "guardian_finale", "Guardian Core", Rare, 12)).ToList();
        Assert.All(finales, rgb => Assert.True(IsRed(rgb) ^ IsBlue(rgb) ^ IsOrdinary(rgb), rgb.ToString("X6")));
        Assert.InRange(finales.Count(IsOrdinary), 450, 500);
        Assert.Equal(StarColors.ForSystem(7, null, null, Rare, 12), StarColors.ForSystem(7, null, null, Rare, 12));
    }
}

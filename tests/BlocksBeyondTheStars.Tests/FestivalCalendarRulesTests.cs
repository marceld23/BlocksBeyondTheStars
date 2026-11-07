// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pure rules of the festival day calendars (#3315), on the shipped Advent calendar: the player's local day
/// (their own midnight, whatever the server's clock), a calendar that waits (one more door each day from 1 December, missed
/// doors open through 6 January, nothing before 1 December or after 6 January), the forced festival with every door, and
/// the seeded door contents — the same on every reconnect, different per player, specials fixed.</summary>
public sealed class FestivalCalendarRulesTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static FestivalCalendarDefinition Advent => Content.Value.FindFestivalCalendar("advent")!;

    private static DateTime Utc(int y, int m, int d, int h = 12, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static CalendarDay Day(DateTime utc, int offset = 0, bool forced = false)
        => FestivalCalendarRules.Evaluate(Advent, FestivalCalendarRules.LocalDate(utc, offset), forced);

    // ---------------- the local day ----------------

    [Fact]
    public void TheFirstDoor_ComesAtThePlayersOwnMidnight_EastAndWestOfUtc()
    {
        // Tokyo (+9 h): 1 December begins at 15:00 UTC on 30 November.
        Assert.False(Day(Utc(2031, 11, 30, 14, 59), +540).Active);
        var tokyo = Day(Utc(2031, 11, 30, 15, 0), +540);
        Assert.True(tokyo.Active);
        Assert.Equal(1, tokyo.Available);
        Assert.Equal(2031, tokyo.Season);

        // Hawaii (−10 h): 1 December begins at 10:00 UTC on 1 December.
        Assert.False(Day(Utc(2031, 12, 1, 9, 59), -600).Active);
        Assert.Equal(1, Day(Utc(2031, 12, 1, 10, 0), -600).Available);

        // The same instant is door 2 in Tokyo (00:30 on the 2nd) and door 1 in Hawaii (05:30 on the 1st).
        var instant = Utc(2031, 12, 1, 15, 30);
        Assert.Equal(2, Day(instant, +540).Available);
        Assert.Equal(1, Day(instant, -600).Available);
    }

    [Fact]
    public void ADoorComesEveryLocalMidnight_AtExactlyZeroHundred()
    {
        Assert.Equal(1, Day(Utc(2031, 12, 1, 0, 0)).Available);
        Assert.Equal(1, Day(Utc(2031, 12, 1, 23, 59)).Available);
        Assert.Equal(2, Day(Utc(2031, 12, 2, 0, 0)).Available);
        Assert.Equal(24, Day(Utc(2031, 12, 24, 0, 0)).Available);
    }

    [Fact]
    public void TheOffset_IsClampedToFourteenHours()
    {
        Assert.Equal(840, FestivalCalendarRules.ClampUtcOffset(5000));
        Assert.Equal(-840, FestivalCalendarRules.ClampUtcOffset(-5000));
        Assert.Equal(60, FestivalCalendarRules.ClampUtcOffset(60));
        Assert.Equal(new DateTime(2031, 12, 2), FestivalCalendarRules.LocalDate(Utc(2031, 12, 1, 12, 0), 100_000)); // +14 h at most
    }

    // ---------------- a calendar that waits ----------------

    [Fact]
    public void MissedDoors_Wait_ThroughTheSixthOfJanuary_AndTheCalendarCloses_OnTheSeventh()
    {
        var boxingDay = Day(Utc(2031, 12, 26));
        Assert.Equal(24, boxingDay.Available);
        Assert.Equal(2031, boxingDay.Season);

        var lastDay = Day(Utc(2032, 1, 6, 23, 59));
        Assert.True(lastDay.Active);
        Assert.Equal(2031, lastDay.Season); // the new year still opens the old calendar
        Assert.Equal(24, lastDay.Available);

        Assert.False(Day(Utc(2032, 1, 7, 0, 0)).Active);
        Assert.True(Day(Utc(2032, 1, 7, 5, 0), -600).Active); // still the 6th in Hawaii
        Assert.False(Day(Utc(2032, 1, 6, 12, 0), +840).Active); // already the 7th on Kiribati
        Assert.Equal("2032-01-06", FestivalCalendarRules.CatchUpUntil(Advent, 2031));
    }

    [Fact]
    public void BeforeTheFirstOfDecember_ThereIsNoCalendar()
    {
        Assert.False(Day(Utc(2031, 11, 30, 23, 59)).Active);
        Assert.False(Day(Utc(2031, 7, 15)).Active);
        Assert.Equal(0, Day(Utc(2031, 11, 30)).Available);
    }

    [Fact]
    public void AForcedFestival_ShowsEveryDoor_InTheSeasonOfTheLocalDate()
    {
        var july = Day(Utc(2031, 7, 15), forced: true);
        Assert.True(july.Active);
        Assert.Equal(24, july.Available);
        Assert.Equal(2030, july.Season); // before this year's 1 December → last year's season

        var december = Day(Utc(2031, 12, 3), forced: true);
        Assert.Equal(24, december.Available);
        Assert.Equal(2031, december.Season);
    }

    // ---------------- the doors ----------------

    [Fact]
    public void ADoor_RollsTheSame_EveryTime_AndDiffersBetweenPlayersAndWorlds()
    {
        for (int door = 1; door <= 24; door++)
        {
            Assert.Equal(Describe(Roll("Ada", door)), Describe(Roll("Ada", door)));
        }

        var ada = Enumerable.Range(1, 24).Select(d => Describe(Roll("Ada", d))).ToList();
        var ben = Enumerable.Range(1, 24).Select(d => Describe(Roll("Ben", d))).ToList();
        var otherWorld = Enumerable.Range(1, 24).Select(d => Describe(FestivalCalendarRules.RollDoor(Advent, 99, "Ada", 2031, d))).ToList();
        var nextYear = Enumerable.Range(1, 24).Select(d => Describe(FestivalCalendarRules.RollDoor(Advent, 4242, "Ada", 2032, d))).ToList();
        Assert.NotEqual(ada, ben);
        Assert.NotEqual(ada, otherWorld);
        Assert.NotEqual(ada, nextYear);
    }

    [Fact]
    public void AnOrdinaryDoor_HoldsTwoDifferentPoolItems_WithinTheirRanges()
    {
        var pool = Advent.Pool.ToDictionary(e => e.Item, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string player in new[] { "Ada", "Ben", "Cleo", "Dan", "Eli", "Fin", "Gus", "Hal" })
        {
            for (int door = 1; door <= 24; door++)
            {
                if (FestivalCalendarRules.SpecialOf(Advent, door) is not null)
                {
                    continue;
                }

                var roll = Roll(player, door);
                Assert.Equal(string.Empty, roll.Cosmetic);
                Assert.Equal(Advent.PicksPerDoor, roll.Items.Count);
                Assert.Equal(roll.Items.Count, roll.Items.Select(i => i.Item).Distinct(StringComparer.Ordinal).Count());
                foreach (var item in roll.Items)
                {
                    Assert.InRange(item.Count, pool[item.Item].Min, pool[item.Item].Max);
                    seen.Add(item.Item);
                }
            }
        }

        Assert.True(seen.Count >= pool.Count / 2, $"only {seen.Count} of {pool.Count} pool items ever came out");
    }

    [Fact]
    public void TheSpecialDoors_GiveExactlyTheirSpecial()
    {
        var six = Roll("Ada", 6);
        Assert.Equal("skin:christmas", six.Cosmetic);
        Assert.Empty(six.Items);

        var twelve = Roll("Ben", 12);
        Assert.Equal("hudframe:christmas_lights", twelve.Cosmetic);
        Assert.Empty(twelve.Items);

        var bell = Assert.Single(Roll("Cleo", 24).Items);
        Assert.Equal("star_bell", bell.Item);
        Assert.Equal(1, bell.Count);
        Assert.Equal(string.Empty, Roll("Cleo", 24).Cosmetic);
    }

    [Fact]
    public void TheMilestones_NameCalendarSeasonAndDoor_AndOnlyEarlierSeasonsAreOld()
    {
        Assert.Equal("calendar:advent:2031:7", FestivalCalendarRules.DoorMilestone("advent", 2031, 7));
        Assert.Equal("calendar:advent:2031:greeted", FestivalCalendarRules.GreetedMilestone("advent", 2031));
        Assert.True(FestivalCalendarRules.IsOlderSeason("calendar:advent:2030:7", "advent", 2031));
        Assert.True(FestivalCalendarRules.IsOlderSeason("calendar:advent:2030:greeted", "advent", 2031));
        Assert.False(FestivalCalendarRules.IsOlderSeason("calendar:advent:2031:7", "advent", 2031));
        Assert.False(FestivalCalendarRules.IsOlderSeason("calendar:other:2020:7", "advent", 2031));
        Assert.False(FestivalCalendarRules.IsOlderSeason("cosmetic:skin:christmas", "advent", 2031));

        var milestones = new HashSet<string> { "calendar:advent:2031:1", "calendar:advent:2031:24", "calendar:advent:2030:2" };
        Assert.Equal(1 | (1 << 23), FestivalCalendarRules.OpenedMask(Advent, 2031, milestones));
    }

    private static CalendarDoorRoll Roll(string player, int door) => FestivalCalendarRules.RollDoor(Advent, 4242, player, 2031, door);

    private static string Describe(CalendarDoorRoll roll)
        => roll.Cosmetic + "|" + string.Join(",", roll.Items.Select(i => i.Item + "x" + i.Count));
}

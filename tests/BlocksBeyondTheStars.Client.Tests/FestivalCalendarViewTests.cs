// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The client's reading of a festival day calendar (#3315, #3316): door states, the waiting count, the grid order,
/// the local UTC offset and the dates the calendar window names.</summary>
public sealed class FestivalCalendarViewTests
{
    private static FestivalCalendarState State(int available, int openedMask, bool active = true, int doors = 24)
        => new() { Calendar = "advent", Festival = "christmas", Active = active, Season = 2031, Doors = doors, Available = available, OpenedMask = openedMask };

    private static int Mask(params int[] doors) => doors.Aggregate(0, (mask, door) => mask | (1 << (door - 1)));

    [Fact]
    public void Doors_AreOpen_Available_OrLater()
    {
        var state = State(available: 5, openedMask: Mask(1, 3));
        Assert.Equal(CalendarDoorStatus.Open, FestivalCalendarView.Status(state, 1));
        Assert.Equal(CalendarDoorStatus.Available, FestivalCalendarView.Status(state, 2));
        Assert.Equal(CalendarDoorStatus.Open, FestivalCalendarView.Status(state, 3));
        Assert.Equal(CalendarDoorStatus.Available, FestivalCalendarView.Status(state, 5));
        Assert.Equal(CalendarDoorStatus.Later, FestivalCalendarView.Status(state, 6));
        Assert.Equal(CalendarDoorStatus.Later, FestivalCalendarView.Status(state, 24));
        Assert.Equal(CalendarDoorStatus.Later, FestivalCalendarView.Status(null, 1));
    }

    [Fact]
    public void Waiting_CountsTheClosedOpenableDoors_AndNothingWhileInactive()
    {
        Assert.Equal(3, FestivalCalendarView.Waiting(State(available: 5, openedMask: Mask(1, 3))));
        Assert.Equal(0, FestivalCalendarView.Waiting(State(available: 2, openedMask: Mask(1, 2))));
        Assert.Equal(24, FestivalCalendarView.Waiting(State(available: 24, openedMask: 0)));
        Assert.Equal(0, FestivalCalendarView.Waiting(State(available: 24, openedMask: 0, active: false)));
        Assert.Equal(0, FestivalCalendarView.Waiting(null));

        // Door 24 is the sign bit of the mask's int — still counted right.
        Assert.Equal(1, FestivalCalendarView.Waiting(State(available: 24, openedMask: Mask(Enumerable.Range(1, 23).ToArray()))));
        Assert.Equal(2, FestivalCalendarView.Waiting(State(available: 32, openedMask: Mask(Enumerable.Range(1, 30).ToArray()), doors: 32)));
    }

    [Fact]
    public void TheEntryPoints_PickTheActiveCalendarWithDoorsWaiting_ThenByKey()
    {
        var calendars = new Dictionary<string, FestivalCalendarState>
        {
            ["zeta"] = new() { Calendar = "zeta", Active = true, Doors = 3, Available = 3 },
            ["advent"] = new() { Calendar = "advent", Active = true, Doors = 3, Available = 1, OpenedMask = 1 },
            ["alpha"] = new() { Calendar = "alpha", Active = false, Doors = 3, Available = 3 },
        };
        Assert.Equal("zeta", FestivalCalendarView.FirstActive(calendars)!.Calendar);
        Assert.Equal(3, FestivalCalendarView.TotalWaiting(calendars.Values));

        calendars["zeta"].OpenedMask = Mask(1, 2, 3);
        Assert.Equal("advent", FestivalCalendarView.FirstActive(calendars)!.Calendar);

        calendars["advent"].Active = false;
        calendars["zeta"].Active = false;
        Assert.Null(FestivalCalendarView.FirstActive(calendars));
        Assert.Null(FestivalCalendarView.FirstActive(null));
    }

    [Fact]
    public void TheGrid_FollowsAValidLayout_ElseCountsUp()
    {
        int[] shuffled = { 3, 1, 4, 2 };
        Assert.Equal(shuffled, FestivalCalendarView.GridOrder(shuffled, 4));
        Assert.Equal(new[] { 1, 2, 3, 4 }, FestivalCalendarView.GridOrder(new[] { 1, 1, 2, 3 }, 4)); // a door twice
        Assert.Equal(new[] { 1, 2, 3, 4 }, FestivalCalendarView.GridOrder(new[] { 1, 2, 3, 5 }, 4)); // a door out of range
        Assert.Equal(new[] { 1, 2, 3, 4 }, FestivalCalendarView.GridOrder(new[] { 1, 2, 3 }, 4));    // too short
        Assert.Equal(new[] { 1, 2, 3 }, FestivalCalendarView.GridOrder(null, 3));

        Assert.Equal(6, FestivalCalendarView.Columns(24));
        Assert.Equal(4, FestivalCalendarView.Rows(24));
        Assert.Equal(1, FestivalCalendarView.Columns(1));
        Assert.Equal(0, FestivalCalendarView.Rows(0));
    }

    [Fact]
    public void TheShippedAdventCalendar_HasA6By4LayoutOfAll24Doors()
    {
        var content = BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var advent = content.FindFestivalCalendar("advent");
        if (advent == null)
        {
            return; // the calendar file is optional content; its own tests guard it
        }

        var order = FestivalCalendarView.GridOrder(advent.Layout, advent.Doors);
        Assert.Equal(Enumerable.Range(1, advent.Doors), order.OrderBy(d => d));
        Assert.Equal(6, FestivalCalendarView.Columns(advent.Doors));
        Assert.Equal(4, FestivalCalendarView.Rows(advent.Doors));
    }

    [Fact]
    public void ContentOf_FindsTheOpenedDoorsContents()
    {
        var state = State(available: 3, openedMask: Mask(1, 2));
        state.Contents = new[]
        {
            new CalendarDoorContent { Door = 2, Items = new[] { "iron_ore" }, Counts = new[] { 3 } },
            new CalendarDoorContent { Door = 1, Cosmetic = "skin:christmas" },
        };
        Assert.Equal("skin:christmas", FestivalCalendarView.ContentOf(state, 1)!.Cosmetic);
        Assert.Equal("iron_ore", FestivalCalendarView.ContentOf(state, 2)!.Items[0]);
        Assert.Null(FestivalCalendarView.ContentOf(state, 3));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(-300, -300)]
    [InlineData(330, 330)]   // India, half an hour
    [InlineData(900, 840)]   // clamped to +14 h
    [InlineData(-2000, -840)]
    public void TheUtcOffset_IsClampedToFourteenHours(int minutes, int expected)
    {
        Assert.Equal(expected, FestivalCalendarView.ClampUtcOffset(minutes));
        Assert.Equal(expected, FestivalCalendarView.UtcOffsetMinutes(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void TheBrowsersOffset_HasTheOtherSign()
    {
        Assert.Equal(60, FestivalCalendarView.FromBrowserTimezoneOffset(-60));  // Berlin in winter
        Assert.Equal(-300, FestivalCalendarView.FromBrowserTimezoneOffset(300)); // New York in winter
        Assert.Equal(840, FestivalCalendarView.FromBrowserTimezoneOffset(-1000));
    }

    [Fact]
    public void Days_AreParsed_AndNamedInTheGameLanguage()
    {
        Assert.True(FestivalCalendarView.TryParseDay("2032-01-06", out var day));
        Assert.Equal(new DateTime(2032, 1, 6), day);
        Assert.False(FestivalCalendarView.TryParseDay("6.1.2032", out _));
        Assert.False(FestivalCalendarView.TryParseDay(null, out _));

        Assert.Equal("6. Januar", FestivalCalendarView.FormatDay(day, "de"));
        Assert.Equal("6 January", FestivalCalendarView.FormatDay(day, "en"));
        Assert.Equal("2032-01-06", FestivalCalendarView.FormatDay(day, "xx")); // an unknown language: the ISO day
    }

    [Fact]
    public void DoorDay_IsDoorOnesDayPlusTheDoor()
    {
        var advent = new FestivalCalendarDefinition { Key = "advent", StartMonth = 12, StartDay = 1, Doors = 24 };
        Assert.Equal(new DateTime(2031, 12, 1), FestivalCalendarView.DoorDay(advent, 2031, 1));
        Assert.Equal(new DateTime(2031, 12, 24), FestivalCalendarView.DoorDay(advent, 2031, 24));
        Assert.Null(FestivalCalendarView.DoorDay(advent, 2031, 0));
        Assert.Null(FestivalCalendarView.DoorDay(new FestivalCalendarDefinition { StartMonth = 13, StartDay = 1 }, 2031, 1));
        Assert.Null(FestivalCalendarView.DoorDay(null, 2031, 1));
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The pure calendar behind the festivals (#3001): fixed, weekday and dated windows, windows over New Year, the
/// primary festival when several overlap, the upcoming list, and the parsers — on the shipped file and on synthetic rows.</summary>
public sealed class FestivalCalendarTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    private static FestivalDefinition Fixed(string key, int month, int day, int before = 0, int after = 0, string theme = "lights")
        => new() { Key = key, Theme = theme, Glyph = "✧", Window = new FestivalWindow { Kind = FestivalWindow.KindFixed, Month = month, Day = day, DaysBefore = before, DaysAfter = after } };

    [Fact]
    public void AFixedWindow_IsOnFromDaysBeforeToDaysAfter_AndOffAroundIt()
    {
        var halloween = Content.Value.FindFestival("halloween")!;
        Assert.Contains(halloween, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 10, 24)));
        Assert.Contains(halloween, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 10, 31)));
        Assert.Contains(halloween, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 11, 1)));
        Assert.DoesNotContain(halloween, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 10, 23)));
        Assert.DoesNotContain(halloween, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 11, 2)));
    }

    [Fact]
    public void AWindowOverNewYear_BelongsToBothYears()
    {
        var kwanzaa = Content.Value.FindFestival("kwanzaa")!;
        Assert.Contains(kwanzaa, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 12, 31)));
        Assert.Contains(kwanzaa, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2027, 1, 1)));
        Assert.DoesNotContain(kwanzaa, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2027, 1, 2)));
    }

    [Fact]
    public void AWeekdayWindow_FindsTheFirstWeekdayOnOrAfterTheAnchor()
    {
        var all = Content.Value.Festivals;
        // Thanksgiving (US) 2026: the fourth Thursday of November is the 26th; the file widens it by one day before.
        var us = Content.Value.FindFestival("thanksgiving_us")!;
        Assert.Contains(us, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 26)));
        Assert.Contains(us, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 25)));
        Assert.DoesNotContain(us, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 27)));
        Assert.DoesNotContain(us, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 19)));
        // Erntedank 2026: the first Sunday of October is the 4th.
        var erntedank = Content.Value.FindFestival("erntedank")!;
        Assert.Contains(erntedank, FestivalCalendar.ActiveOn(all, Utc(2026, 10, 4)));
        Assert.DoesNotContain(erntedank, FestivalCalendar.ActiveOn(all, Utc(2026, 10, 1)));
        // Advent 2026 starts on Sunday 29 November and runs to 23 December (the fixed end).
        var advent = Content.Value.FindFestival("advent")!;
        Assert.DoesNotContain(advent, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 28)));
        Assert.Contains(advent, FestivalCalendar.ActiveOn(all, Utc(2026, 11, 29)));
        Assert.Contains(advent, FestivalCalendar.ActiveOn(all, Utc(2026, 12, 23)));
        Assert.DoesNotContain(advent, FestivalCalendar.ActiveOn(all, Utc(2026, 12, 24)));
    }

    [Fact]
    public void ADatedWindow_UsesItsRangesInclusively()
    {
        var diwali = Content.Value.FindFestival("diwali")!;
        Assert.Contains(diwali, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 11, 6)));
        Assert.Contains(diwali, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 11, 10)));
        Assert.DoesNotContain(diwali, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 11, 11)));
        Assert.Contains(diwali, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2027, 10, 29)));
        // Hanukkah 2027 runs over New Year.
        var hanukkah = Content.Value.FindFestival("hanukkah")!;
        Assert.Contains(hanukkah, FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2028, 1, 1)));
    }

    [Fact]
    public void Overlap_PutsTheFestivalThatStartedLastFirst_TiesKeepTheFileOrder()
    {
        var active = FestivalCalendar.ActiveOn(Content.Value.Festivals, Utc(2026, 12, 26));
        Assert.Equal("kwanzaa", active[0].Key); // started today; Christmas started on the 24th
        Assert.Contains(active, f => f.Key == "christmas");

        var a = Fixed("a", 5, 5);
        var b = Fixed("b", 5, 5);
        var tie = FestivalCalendar.ActiveOn(new[] { a, b }, Utc(2030, 5, 5));
        Assert.Equal(new[] { "a", "b" }, tie.Select(f => f.Key));
    }

    [Fact]
    public void Upcoming_ListsWhatStartsWithinTheHorizon_SoonestFirst_WithoutWhatIsOn()
    {
        var upcoming = FestivalCalendar.Upcoming(Content.Value.Festivals, Utc(2026, 10, 30), 14);
        Assert.DoesNotContain(upcoming, s => s.Festival.Key == "halloween"); // on right now
        Assert.Equal("dia_de_muertos", upcoming[0].Festival.Key);
        Assert.Equal(Utc(2026, 11, 1).Date, upcoming[0].Start);
        Assert.Contains(upcoming, s => s.Festival.Key == "diwali" && s.Start == Utc(2026, 11, 6).Date);
        Assert.Contains(upcoming, s => s.Festival.Key == "st_martin");
        Assert.DoesNotContain(upcoming, s => s.Festival.Key == "world_childrens_day"); // 20 November: past the horizon
        Assert.True(upcoming.Zip(upcoming.Skip(1), (x, y) => x.Start <= y.Start).All(ok => ok));
    }

    [Fact]
    public void Parsers_AcceptDatesAndWeekdayNames_AndRefuseNumbersAndJunk()
    {
        Assert.True(FestivalCalendar.TryParseDate("2027-02-06", out var d) && d == new DateTime(2027, 2, 6, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(FestivalCalendar.TryParseDate("06.02.2027", out _));
        Assert.False(FestivalCalendar.TryParseDate(null, out _));
        Assert.True(FestivalCalendar.TryParseWeekday("sunday", out var wd) && wd == DayOfWeek.Sunday);
        Assert.False(FestivalCalendar.TryParseWeekday("3", out _));
        Assert.False(FestivalCalendar.TryParseWeekday("", out _));
        Assert.False(FestivalCalendar.TryParseWeekday("Someday", out _));
    }

    [Fact]
    public void AMalformedWindow_YieldsNoSpans()
    {
        var bad = Fixed("bad", 13, 40);
        Assert.Empty(FestivalCalendar.SpansAround(bad, 2027));
        var unknown = new FestivalDefinition { Key = "u", Theme = "lights", Glyph = "✧", Window = new FestivalWindow { Kind = "dates", Ranges = new[] { new[] { "nope", "2027-01-01" } } } };
        Assert.Empty(FestivalCalendar.SpansAround(unknown, 2027));
        Assert.Empty(FestivalCalendar.ActiveOn(new[] { bad, unknown }, Utc(2027, 1, 1)));
    }
}

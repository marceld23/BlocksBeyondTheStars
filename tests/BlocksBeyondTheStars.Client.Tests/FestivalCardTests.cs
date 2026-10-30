// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The main menu's festival card and the HUD chip text (#3003), on the shipped calendar.</summary>
public sealed class FestivalCardTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 9, 0, 0, DateTimeKind.Utc);

    private static string L(string key) => Content.CreateLocalizer(GameLocale.English).Get(key);

    [Fact]
    public void OnAFestivalDay_TheCardNamesIt_AndTheChipShowsGlyphAndName()
    {
        var active = FestivalCard.Active(Content, Utc(2031, 10, 31));
        Assert.Single(active);
        Assert.Equal("halloween", active[0].Festival.Key);
        Assert.Equal("Today: ✦ Halloween", FestivalCard.Headline(active[0], L));
        Assert.True(FestivalCard.ShouldShow(Content, Utc(2031, 10, 31), celebrated: null));
        Assert.False(FestivalCard.ShouldShow(Content, Utc(2031, 10, 31), new HashSet<string>())); // #3021: "None"
        Assert.Equal("✦ Halloween", FestivalCard.Chip(Content, new[] { "halloween" }, L));
        Assert.Equal(string.Empty, FestivalCard.Chip(Content, Array.Empty<string>(), L));
    }

    [Fact]
    public void BetweenFestivals_TheCardShowsTheNextOneWithinTwoWeeks_OrNothing()
    {
        // 30 October 2031: Halloween is on; its card line is the active one, the next upcoming is Día de los Muertos.
        var next = FestivalCard.Next(Content, Utc(2031, 10, 30));
        Assert.NotNull(next);
        Assert.Equal("dia_de_muertos", next!.Value.Festival.Key);
        Assert.Equal(2, next.Value.DaysUntil);
        Assert.Equal("In 2 days: ✿ Día de los Muertos", FestivalCard.Headline(next.Value, L));

        // 31 October: tomorrow.
        var tomorrow = FestivalCard.Next(Content, Utc(2031, 10, 31))!.Value;
        Assert.Equal("Tomorrow: ✿ Día de los Muertos", FestivalCard.Headline(tomorrow, L));

        // Mid-July 2031: nothing on, the next (Moon Landing Day on the 20th) is within two weeks.
        Assert.Empty(FestivalCard.Active(Content, Utc(2031, 7, 15)));
        Assert.Equal("moon_landing_day", FestivalCard.Next(Content, Utc(2031, 7, 15))!.Value.Festival.Key);

        // Late May 2031: nothing within two weeks → no card (International Children's Day on 1 June is 15 days off on 17 May).
        Assert.Null(FestivalCard.Next(Content, Utc(2031, 5, 17)));
        Assert.False(FestivalCard.ShouldShow(Content, Utc(2031, 5, 17), celebrated: null));
    }

    [Fact]
    public void TheMoreList_LooksSixtyDaysAhead_SoonestFirst()
    {
        var list = FestivalCard.Upcoming(Content, Utc(2031, 10, 30), FestivalCard.UpcomingHorizonDays);
        Assert.Equal("dia_de_muertos", list[0].Festival.Key);
        Assert.Contains(list, l => l.Festival.Key == "st_martin" && l.DaysUntil == 12);
        Assert.Contains(list, l => l.Festival.Key == "christmas" && l.DaysUntil == 55);
        Assert.DoesNotContain(list, l => l.Festival.Key == "halloween");
        Assert.True(list.Zip(list.Skip(1), (a, b) => a.DaysUntil <= b.DaysUntil).All(ok => ok));
    }

    [Fact]
    public void PrimaryEffects_AndBegan_FollowTheServersList()
    {
        var effects = FestivalCard.PrimaryEffects(Content, new[] { "hanami", "easter" }, Utc(2031, 3, 26), forced: false);
        Assert.NotNull(effects);
        Assert.Equal("petals", effects!.Motes);
        Assert.Null(FestivalCard.PrimaryEffects(Content, Array.Empty<string>(), Utc(2031, 3, 26), forced: false));
        Assert.Null(FestivalCard.PrimaryEffects(null, new[] { "hanami" }, Utc(2031, 3, 26), forced: false));

        // #3022: past Hanami's seven core days the mood rests — unless the festival was switched on by hand.
        Assert.Null(FestivalCard.PrimaryEffects(Content, new[] { "hanami" }, Utc(2031, 4, 8), forced: false));
        Assert.NotNull(FestivalCard.PrimaryEffects(Content, new[] { "hanami" }, Utc(2031, 4, 8), forced: true));
        Assert.NotNull(FestivalCard.PrimaryEffects(Content, new[] { "hanami" }, Utc(2031, 7, 15), forced: true)); // any day

        Assert.Equal(new[] { "kwanzaa" }, FestivalCard.Began(new[] { "christmas" }, new[] { "kwanzaa", "christmas" }));
        Assert.Empty(FestivalCard.Began(new[] { "christmas" }, new[] { "christmas" }));
        Assert.Equal(new[] { "christmas" }, FestivalCard.Began(null, new[] { "christmas" }));
        Assert.Empty(FestivalCard.Began(new[] { "christmas" }, Array.Empty<string>()));
    }

    [Fact]
    public void MyFestivals_TheCardNamesOnlyTheCelebratedOnes_AndTheListShowsThemAll()
    {
        // #3021: on 1 November 2031 Halloween's last day meets Día de los Muertos — a player who celebrates only Halloween
        // sees Halloween; one who celebrates nothing gets no card at all.
        var day = Utc(2031, 11, 1);
        var onlyHalloween = new HashSet<string> { "halloween" };
        Assert.Equal(new[] { "halloween" }, FestivalCard.Active(Content, day, onlyHalloween).Select(l => l.Festival.Key));
        Assert.Empty(FestivalCard.Active(Content, day, new HashSet<string>()));
        Assert.Null(FestivalCard.Next(Content, Utc(2031, 10, 30), onlyHalloween)); // the next celebrated one is a year off

        // #3022: Halloween (priority) leads Día de los Muertos for a player who celebrates both, though it started earlier.
        Assert.Equal("halloween", FestivalCard.Active(Content, day, null)[0].Festival.Key);

        // The "My festivals" list: every festival by its next start, today's first, then soonest first.
        var all = FestivalCard.Calendar(Content, Utc(2031, 10, 30));
        Assert.Equal("halloween", all[0].Festival.Key);
        Assert.Equal(0, all[0].DaysUntil);
        Assert.Contains(all, l => l.Festival.Key == "christmas");
        Assert.Contains(all, l => l.Festival.Key == "orthodox_christmas");
        Assert.Equal(Content.Festivals.Count(f => f.Window.Kind != "dates"), all.Count); // 2031: the dated ones are past the file's years
        Assert.Equal("on now", FestivalCard.When(all[0], L));
        Assert.Equal("in 2 days", FestivalCard.When(all.First(l => l.Festival.Key == "dia_de_muertos"), L));
    }
}

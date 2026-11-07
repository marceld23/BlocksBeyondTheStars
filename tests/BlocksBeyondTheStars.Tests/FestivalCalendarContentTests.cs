// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary><c>data/festival_calendars.json</c> and the templates its doors unlock (#3315, #3320, #3321): the shipped Advent
/// calendar as decided, its English and German texts, the validator that catches a broken calendar, frame or skin — and
/// the companion-only body plans (#3322) that no wild species may wear.</summary>
public sealed class FestivalCalendarContentTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    [Fact]
    public void TheAdventCalendar_IsShapedAsDecided()
    {
        var advent = Assert.Single(Shipped.Value.FestivalCalendars, c => c.Key == "advent");
        Assert.Equal("christmas", advent.Festival);
        Assert.Equal((12, 1), (advent.StartMonth, advent.StartDay));
        Assert.Equal(24, advent.Doors);
        Assert.Equal((1, 6), (advent.CatchUpMonth, advent.CatchUpDay));
        Assert.Equal(2, advent.PicksPerDoor);
        Assert.InRange(advent.Pool.Count, 18, 24);
        Assert.All(advent.Pool, e => Assert.NotNull(Shipped.Value.GetItem(e.Item)));
        Assert.Equal(new[] { 6, 12, 24 }, advent.Specials.Select(s => s.Door).OrderBy(d => d));
        Assert.Equal("skin:christmas", FestivalCalendarRules.SpecialOf(advent, 6)!.Cosmetic);
        Assert.Equal("hudframe:christmas_lights", FestivalCalendarRules.SpecialOf(advent, 12)!.Cosmetic);
        Assert.Equal("star_bell", Assert.Single(FestivalCalendarRules.SpecialOf(advent, 24)!.Items).Item);
        Assert.Equal(Enumerable.Range(1, 24), advent.Layout.OrderBy(d => d));
        Assert.NotEqual(Enumerable.Range(1, 24), advent.Layout); // shuffled like a real calendar
        Assert.True(Shipped.Value.CosmeticExists("hudframe:christmas_lights"));
        Assert.True(Shipped.Value.CosmeticExists("skin:christmas"));
        Assert.False(Shipped.Value.CosmeticExists("skin:no_such_skin"));
    }

    /// <summary>Needs the keys of this package in <c>data/locales/en.json</c> and <c>de.json</c>.</summary>
    [Fact]
    public void TheCalendarAndRudolpho_HaveTheirTexts_InEnglishAndGerman()
    {
        var en = Shipped.Value.CreateLocalizer(GameLocale.English);
        var de = Shipped.Value.CreateLocalizer(GameLocale.German);
        var keys = Shipped.Value.FestivalCalendars.SelectMany(c => new[] { c.NameKey, c.VegaKey })
            .Concat(new[]
            {
                "ui.calendar.bag_full", "item.star_bell.name", "item.star_bell.desc", "creature.reindeer.name", "ui.scan.reindeer",
                "srv.reindeer.surface_only", "srv.reindeer.step_outside", "srv.reindeer.already_active", "srv.reindeer.deployed",
                "srv.reindeer.sent_home", "srv.reindeer.no_food",
            });
        foreach (string key in keys)
        {
            Assert.True(en.Has(key), $"en.json lacks {key}");
            Assert.True(de.Has(key), $"de.json lacks {key}");
        }
    }

    [Fact]
    public void TheValidator_CatchesBrokenCalendars()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

        Assert.Contains("unknown festival", Problem(content, c => c.Festival = "no_such_festival"));
        Assert.Contains("doors must be", Problem(content, c => c.Doors = 32));
        Assert.Contains("not a date of every year", Problem(content, c => (c.StartMonth, c.StartDay) = (2, 29)));
        Assert.Contains("before the last door", Problem(content, c => (c.CatchUpMonth, c.CatchUpDay) = (12, 20)));
        Assert.Contains("unknown item", Problem(content, c => c.Pool[0].Item = "no_such_item"));
        Assert.Contains("1 ≤ min ≤ max", Problem(content, c => (c.Pool[0].Min, c.Pool[0].Max) = (5, 2)));
        Assert.Contains("1 ≤ min ≤ max", Problem(content, c => c.Pool[0].Weight = 0));
        Assert.Contains("twice", Problem(content, c => c.Pool[1].Item = c.Pool[0].Item));
        Assert.Contains("picksPerDoor", Problem(content, c => c.PicksPerDoor = 0));
        Assert.Contains("appear once", Problem(content, c => c.Specials[1].Door = c.Specials[0].Door));
        Assert.Contains("appear once", Problem(content, c => c.Specials[0].Door = 25));
        Assert.Contains("unknown cosmetic", Problem(content, c => c.Specials[0].Cosmetic = "hudframe:no_such_frame"));
        Assert.Contains("unknown item", Problem(content, c => c.Specials[2].Items[0].Item = "no_such_item"));
        Assert.Contains("neither a cosmetic nor items", Problem(content, c => (c.Specials[0].Cosmetic, c.Specials[0].Items) = (string.Empty, new List<ItemAmount>())));
        Assert.Contains("layout", Problem(content, c => c.Layout = c.Layout.Take(23).Append(1).ToArray()));
    }

    [Fact]
    public void TheValidator_ChecksTheHudFramesAndSkins()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        string tile = new('1', BodyPaint.ChunkPixels);

        content.SetHudFrames(new HudFrameCatalog { Frames = { new HudFrameDefinition { Key = "christmas_lights", Unlock = "calendar", Corner = tile, Top = tile, Side = tile, Bottom = tile } } });
        content.Validate(); // a full, valid frame

        Assert.Contains("corner tile", FrameProblem(content, f => f.Corner = tile.Substring(1)));
        Assert.Contains("top tile", FrameProblem(content, f => f.Top = new string('w', BodyPaint.ChunkPixels)));
        Assert.Contains("side tile", FrameProblem(content, f => f.Side = new string('A', BodyPaint.ChunkPixels))); // the editor writes lower case
        Assert.Contains("unlock", FrameProblem(content, f => f.Unlock = "shop"));
        Assert.Contains("twinkle colours", FrameProblem(content, f => f.TwinkleColors = new[] { "red" }));

        string torso = new('2', BodyPaint.ExpectedLength(BodyPaint.Torso));
        content.SetSkins(new SkinCatalog { Skins = { new SkinDefinition { Key = "christmas", Unlock = "calendar", Colors = new[] { "", "#c0392b", "#ffffff", "#2e7d32" }, Paint = new[] { torso, "", "", "" } } } });
        content.Validate(); // empty colour = keep the player's own; empty paint = unpainted

        Assert.Contains("colors", SkinProblem(content, s => s.Colors = new[] { "#ffffff", "#ffffff", "#ffffff" }));
        Assert.Contains("colors", SkinProblem(content, s => s.Colors = new[] { "white", "", "", "" }));
        Assert.Contains("paint must be", SkinProblem(content, s => s.Paint = new[] { torso }));
        Assert.Contains("paint part 1", SkinProblem(content, s => s.Paint = new[] { torso, torso, "", "" }));
    }

    [Fact]
    public void TheCompanionOnlyBodies_AreNeverWild()
    {
        Assert.True(GameContent.IsCompanionOnlyBodyPlan(CreatureBodyPlan.Robot));
        Assert.True(GameContent.IsCompanionOnlyBodyPlan(CreatureBodyPlan.Reindeer));
        Assert.False(GameContent.IsCompanionOnlyBodyPlan(CreatureBodyPlan.Standard));
        Assert.DoesNotContain(Shipped.Value.AuthoredCreatures.Values, a => GameContent.IsCompanionOnlyBodyPlan(a.BodyPlan));

        foreach (var planet in Shipped.Value.Planets.Values)
        {
            for (long seed = 1; seed <= 12; seed++)
            {
                var roster = CreatureGenerator.GenerateRoster(planet, seed * 7919, WorldDescription.CurrentTerrainGeneration, Shipped.Value.AuthoredCreaturesFor(planet));
                Assert.DoesNotContain(roster, s => GameContent.IsCompanionOnlyBodyPlan(s.BodyPlan));
            }
        }
    }

    private static string Problem(GameContent content, Action<FestivalCalendarDefinition> breakIt)
    {
        var copy = ShippedAdventCopy();
        breakIt(copy);
        content.SetFestivalCalendars(new FestivalCalendarCatalog { Calendars = { copy } });
        return Failure(content, () => content.SetFestivalCalendars(new FestivalCalendarCatalog { Calendars = { ShippedAdventCopy() } }));
    }

    /// <summary>A deep copy of the shipped Advent calendar, free to break.</summary>
    private static FestivalCalendarDefinition ShippedAdventCopy()
        => JsonSerializer.Deserialize<FestivalCalendarDefinition>(JsonSerializer.Serialize(Shipped.Value.FindFestivalCalendar("advent")!))!;

    private static string FrameProblem(GameContent content, Action<HudFrameDefinition> breakIt)
    {
        var frame = new HudFrameDefinition { Key = "christmas_lights", Unlock = "calendar" };
        breakIt(frame);
        content.SetHudFrames(new HudFrameCatalog { Frames = { frame } });
        return Failure(content, () => content.SetHudFrames(new HudFrameCatalog { Frames = { new HudFrameDefinition { Key = "christmas_lights" } } }));
    }

    private static string SkinProblem(GameContent content, Action<SkinDefinition> breakIt)
    {
        var skin = new SkinDefinition { Key = "christmas", Unlock = "calendar" };
        breakIt(skin);
        content.SetSkins(new SkinCatalog { Skins = { skin } });
        return Failure(content, () => content.SetSkins(new SkinCatalog { Skins = { new SkinDefinition { Key = "christmas" } } }));
    }

    /// <summary>The validator's complaints (one string), then the content put right again for the next case.</summary>
    private static string Failure(GameContent content, Action repair)
    {
        var ex = Assert.Throws<ContentValidationException>(content.Validate);
        repair();
        return string.Join("\n", ex.Problems);
    }
}

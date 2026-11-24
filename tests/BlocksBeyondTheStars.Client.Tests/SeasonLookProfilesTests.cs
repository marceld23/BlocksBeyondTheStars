// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The season look per profile and hemisphere (#3996): a summer/winter year has no autumn flash, a wet/dry year follows the
/// rain, one season shows its full look, and toward the tropics the hemisphere's look hands over to the wet/dry look without a
/// jump; the HUD shows "Winter · S" and the wet or dry season in the tropics.
/// </summary>
public sealed class SeasonLookProfilesTests
{
    private static WorldEnvironment Env(string season, float progress = 0.5f, string cycle = "", float strength = 1f,
        string hemisphere = "", float wetness = 0.5f)
        => new()
        {
            Season = season,
            SeasonProgress = progress,
            SeasonCycle = cycle,
            SeasonStrength = strength,
            SeasonHemisphere = hemisphere,
            SeasonWetness = wetness,
        };

    [Fact]
    public void AnOlderServer_OrAFourSeasonPlanet_LooksExactlyAsBefore()
    {
        foreach (string season in PlanetSeasons.Order)
        {
            for (float p = 0f; p <= 1f; p += 0.1f)
            {
                Assert.Equal(SeasonLook.Target(season, p), SeasonLook.LookFor(Env(season, p), evergreenFlora: false));
                Assert.Equal(SeasonLook.Target(season, p), SeasonLook.LookFor(Env(season, p, "four"), evergreenFlora: false));
            }
        }

        Assert.Equal((0f, 0f, 0f), SeasonLook.LookFor(Env(string.Empty), false));
        Assert.Equal("ui.season.winter", SeasonLook.HudKeys(Env("winter")).SeasonKey);
        Assert.Equal(string.Empty, SeasonLook.HudKeys(Env("winter")).HemisphereKey);
    }

    [Fact]
    public void ASummerWinterYear_TurnsWithoutAutumnLeaves()
    {
        for (float p = 0f; p <= 1f; p += 0.05f)
        {
            Assert.Equal(0f, SeasonLook.LookFor(Env("winter", p, "two"), false).Autumn);
            Assert.Equal(0f, SeasonLook.LookFor(Env("summer", p, "two"), false).Autumn);
        }

        Assert.Equal(1f, SeasonLook.LookFor(Env("winter", 0.5f, "two"), false).Winter, 3);
        Assert.Equal(1f, SeasonLook.LookFor(Env("summer", 0f, "two"), false).Winter, 3); // the frost still lies…
        Assert.Equal(0f, SeasonLook.LookFor(Env("summer", 0.3f, "two"), false).Winter, 3); // …and melts
    }

    [Fact]
    public void AWetDryYear_FollowsTheRain_AndOneSeasonShowsItsFullLook()
    {
        var dry = SeasonLook.LookFor(Env("dry", cycle: "rain", wetness: 0.15f), false);
        var wet = SeasonLook.LookFor(Env("wet", cycle: "rain", wetness: 0.85f), false);
        Assert.True(dry.Autumn > 0.3f && dry.Bloom == 0f, "the dry season yellows the plants");
        Assert.True(wet.Bloom > 0.25f && wet.Autumn == 0f, "the wet season lends bloom");
        Assert.Equal((0f, 0f, 0f), SeasonLook.LookFor(Env("wet", cycle: "rain", wetness: 0.5f), false));

        Assert.Equal(1f, SeasonLook.LookFor(Env("autumn", 0f, "one"), false).Autumn, 3);
        Assert.Equal(1f, SeasonLook.LookFor(Env("winter", 0f, "one"), false).Winter, 3);
        Assert.Equal((0f, 0f, 0f), SeasonLook.LookFor(Env("autumn", 0f, "one"), evergreenFlora: true));
    }

    [Fact]
    public void TowardTheTropics_TheLookHandsOverSmoothly_AndTheHudNamesTheWetOrDrySeason()
    {
        var previous = SeasonLook.LookFor(Env("winter", 0.5f, "four", 0f, "south", 0.2f), false);
        for (float s = 0.02f; s <= 1.0001f; s += 0.02f)
        {
            var look = SeasonLook.LookFor(Env("winter", 0.5f, "four", s, "south", 0.2f), false);
            Assert.True(Math.Abs(look.Winter - previous.Winter) < 0.03f && Math.Abs(look.Autumn - previous.Autumn) < 0.03f);
            previous = look;
        }

        var tropics = Env("winter", 0.5f, "four", 0.1f, "south", 0.2f);
        Assert.True(SeasonLook.InTropics(tropics));
        Assert.Equal(PlanetSeasons.Dry, SeasonLook.DisplaySeason(tropics));
        Assert.Equal(("ui.season.dry", string.Empty), SeasonLook.HudKeys(tropics));
        Assert.Equal("ui.season.tropics", SeasonLook.PlaceKey(tropics));
        Assert.False(SeasonLook.FrostFor(tropics, -5f)); // no winter frost in the tropics

        var south = Env("winter", 0.5f, "four", 1f, "south", 0.2f);
        Assert.Equal(("ui.season.winter", "ui.season.hemi_short.south"), SeasonLook.HudKeys(south));
        Assert.Equal("ui.season.hemi.south", SeasonLook.PlaceKey(south));
        Assert.True(SeasonLook.FrostFor(south, -5f));
        Assert.Equal(string.Empty, SeasonLook.PlaceKey(Env("winter")));
    }

    [Fact]
    public void TheGrade_FadesWithTheSeasonsStrength()
    {
        var full = SeasonLook.GradeFor(Env("winter", strength: 1f, hemisphere: "north"));
        var half = SeasonLook.GradeFor(Env("winter", strength: 0.6f, hemisphere: "north"));
        Assert.Equal(SeasonLook.Grade(PlanetSeasons.Winter).Warmth, full.Warmth, 4);
        Assert.Equal(SeasonLook.Grade(PlanetSeasons.Winter).Saturation, full.Saturation, 4);
        Assert.True(half.Warmth > full.Warmth && half.Saturation > full.Saturation);
        Assert.Equal(SeasonLook.Grade(PlanetSeasons.Dry), SeasonLook.GradeFor(Env("dry", cycle: "rain", wetness: 0.2f)));
    }
}

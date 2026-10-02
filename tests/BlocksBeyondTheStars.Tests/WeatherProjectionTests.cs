// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The shared per-position weather formula (#2174) and the per-world tints moved to Shared (#2170).</summary>
public sealed class WeatherProjectionTests
{
    private static WeatherEpisode Ladder(string state, int floor = 0, int ceiling = 3, bool dynamic = true)
    {
        var def = WeatherCatalog.Find(state)!;
        return new WeatherEpisode(state, def.Severity, floor, ceiling, 0.6f, 0.6f, dynamic);
    }

    [Fact]
    public void BiomeOffset_IsDeterministic_InRange_AndRotatesWithTheEra()
    {
        var seen = new HashSet<int>();
        for (int biome = 0; biome < 8; biome++)
        {
            int a = WeatherProjection.BiomeOffset(1234, biome, 2.0);
            Assert.Equal(a, WeatherProjection.BiomeOffset(1234, biome, 5.9)); // same era (0..6 days)
            Assert.InRange(a, -1, 2);
            seen.Add(a);
        }

        Assert.True(seen.Count > 1, "biomes must not all share one offset");
        bool rotated = false;
        for (int biome = 0; biome < 8 && !rotated; biome++)
        {
            rotated = WeatherProjection.BiomeOffset(1234, biome, 2.0) != WeatherProjection.BiomeOffset(1234, biome, 8.0);
        }

        Assert.True(rotated, "the next era must reshuffle at least one biome");
    }

    [Fact]
    public void At_ShiftsTheLadder_ByBiomeFrontAndSummit_WithinTheWorldsBand()
    {
        var clouds = Ladder("clouds");
        Assert.Equal("clouds", WeatherProjection.At(clouds, 0, 0, false).State);
        Assert.Equal("rain", WeatherProjection.At(clouds, 1, 0, false).State);
        Assert.Equal("storm", WeatherProjection.At(clouds, 1, 1, false).State);
        Assert.Equal("rain", WeatherProjection.At(clouds, 0, 0, true).State); // summit: one step wetter
        Assert.Equal("storm", WeatherProjection.At(clouds, 2, 2, true).State); // capped at the top of the ladder

        var overcast = Ladder("clouds", floor: 1);
        Assert.Equal("clouds", WeatherProjection.At(overcast, -1, 0, false).State); // never clear on an overcast world
        var airless = Ladder("clear", ceiling: 0);
        Assert.Equal("clear", WeatherProjection.At(airless, 2, 2, true).State); // never rain without air
    }

    [Fact]
    public void At_EventsAndFixedWorlds_IgnoreEveryPositionalShift()
    {
        var fog = new WeatherEpisode("fog", 1, 0, 3, 0.5f, 0.4f, true);
        Assert.Equal(("fog", 0.4f), WeatherProjection.At(fog, 2, 2, true));
        var cabin = new WeatherEpisode("clear", 0, 0, 3, 0f, 0f, false);
        Assert.Equal("clear", WeatherProjection.At(cabin, 2, 2, true).State);
        Assert.Equal("clouds", WeatherProjection.At(Ladder("clouds"), 2, 2, true, hasPosition: false).State);
    }

    [Fact]
    public void FrontBoost_WrapsAroundTheSeam()
    {
        var fronts = new[] { new WeatherFrontState(10, 50, 0, 2) };
        Assert.Equal(2, WeatherProjection.FrontBoost(fronts, 5990, 6000)); // 20 blocks the short way round
        Assert.Equal(2, WeatherProjection.FrontBoost(fronts, 60, 6000));
        Assert.Equal(0, WeatherProjection.FrontBoost(fronts, 61, 6000));
        Assert.Equal(0, WeatherProjection.FrontBoost(null, 0, 6000));
    }

    [Fact]
    public void Precipitation_FollowsTheClimate_TheTypeAndTheEvent()
    {
        var temperate = new PlanetType { SurfaceBlock = "grass" };
        Assert.Equal("none", WeatherProjection.Precipitation("clouds", temperate, 15f, "none"));
        Assert.Equal("rain", WeatherProjection.Precipitation("rain", temperate, 15f, "rain"));
        Assert.Equal("drizzle", WeatherProjection.Precipitation("rain", temperate, 15f, "drizzle"));
        Assert.Equal("snow", WeatherProjection.Precipitation("rain", temperate, -3f, "rain"));
        Assert.Equal("hail", WeatherProjection.Precipitation("storm", temperate, -20f, "rain"));
        Assert.Equal("ash", WeatherProjection.Precipitation("rain", temperate, 70f, "rain"));
        Assert.Equal("sandstorm", WeatherProjection.Precipitation("rain", new PlanetType { SurfaceBlock = "sand" }, 30f, "rain"));
        Assert.Equal("acid", WeatherProjection.Precipitation("storm", new PlanetType { Precipitation = "acid" }, 15f, "rain"));
        Assert.Equal("spores", WeatherProjection.Precipitation("spore_bloom", temperate, 15f, "none"));
    }

    [Fact]
    public void SkyRgb_UsesTheTypesAuthoredSky_ElseTheSeededHue()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var toxica = content.GetPlanet("toxica_maxima")!;
        Assert.Equal(toxica.SkyColor, AtmosphereTints.SkyRgb(42, "sys0-p1", toxica));
        var rocky = content.GetPlanet("rocky")!;
        Assert.Equal(AtmosphereTints.SkyRgb(42, "sys0-p1", rocky), AtmosphereTints.SkyRgb(42, "sys0-p1", rocky)); // deterministic
        var hues = new HashSet<int>();
        for (int i = 1; i <= 6; i++)
        {
            hues.Add(AtmosphereTints.SkyRgb(42, "sys0-p" + i, rocky));
        }

        Assert.True(hues.Count > 1, "two same-type worlds must be able to differ in sky colour");
    }
}

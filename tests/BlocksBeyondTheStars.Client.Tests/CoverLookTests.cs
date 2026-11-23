// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3635: the landing and departure cover's clouds are lit like the planet's own — dark at night and in a storm, the
/// daytime colour at a clear noon, a quarter of the world's mist colour — and the cover over the world you land on is
/// exactly what its cloud sea draws for the same hour and weather.
/// </summary>
public sealed class CoverLookTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private const int Cloud = 0xEDEFF2;
    private const int Sky = 0x7FB2E8;
    private const int WhiteSun = 0xFFFFFF;

    [Fact]
    public void TheDayCurve_IsTheSkys_AndTheLocalTimeAddsTheLongitude()
    {
        Assert.Equal(1f, CoverLook.DayOf(0.5), 4);   // noon
        Assert.Equal(0f, CoverLook.DayOf(0.0), 4);   // midnight
        Assert.Equal(0.5f, CoverLook.DayOf(0.25), 4); // sunrise
        Assert.Equal(0.5f, CoverLook.DayOf(0.75), 4); // sunset
        Assert.Equal(CoverLook.DayOf(0.5), CoverLook.DayOf(1.5), 4);

        Assert.Equal(0.5, CoverLook.LocalTime(0.3, 0.2), 6);
        Assert.Equal(0.1, CoverLook.LocalTime(0.9, 0.2), 6); // wraps past midnight
    }

    [Fact]
    public void AClearNoon_KeepsTheBaseColour_NightAndStormAreDarker()
    {
        MistRgb noon = CoverLook.DeckColour(Cloud, -1, 0f, WhiteSun, 1f, CloudDeck.Target("clear", 0.5f).Darken);
        MistRgb baseColour = MistRgb.FromPacked(Cloud);
        Assert.Equal(baseColour.R, noon.R, 3);
        Assert.Equal(baseColour.G, noon.G, 3);
        Assert.Equal(baseColour.B, noon.B, 3);

        MistRgb night = CoverLook.DeckColour(Cloud, -1, 0f, WhiteSun, 0f, 1f);
        MistRgb storm = CoverLook.DeckColour(Cloud, -1, 0f, WhiteSun, 1f, CloudDeck.Target("storm", 0.5f).Darken);
        Assert.True(night.Luma < noon.Luma * 0.4f, $"night {night.Luma:0.00} vs noon {noon.Luma:0.00}");
        Assert.True(storm.Luma < noon.Luma * 0.5f, $"storm {storm.Luma:0.00} vs noon {noon.Luma:0.00}");

        // The sky behind them goes dark at night (never black) and dims in the storm.
        MistRgb skyNoon = CoverLook.SkyColour(Sky, 1f, 1f);
        MistRgb skyNight = CoverLook.SkyColour(Sky, 0f, 1f);
        Assert.Equal(MistRgb.FromPacked(Sky).B, skyNoon.B, 3);
        Assert.True(skyNight.Luma < skyNoon.Luma * 0.3f);
        Assert.True(skyNight.Luma >= MistLook.NightFloor.Luma);
        Assert.True(CoverLook.SkyColour(Sky, 1f, 0.35f).Luma < skyNoon.Luma);
    }

    [Fact]
    public void TheMistsShare_AndALowSun_TintTheClouds()
    {
        const int Pink = 0xF2C4D8;
        MistRgb plain = CoverLook.DeckColour(Cloud, -1, CoverLook.MistShare, WhiteSun, 1f, 1f);
        MistRgb misty = CoverLook.DeckColour(Cloud, Pink, CoverLook.MistShare, WhiteSun, 1f, 1f);
        Assert.True(misty.R - misty.G > plain.R - plain.G + 0.03f, "a quarter of the pink mist shows");

        const int RedStar = 0xFF8040;
        MistRgb noon = CoverLook.DeckColour(Cloud, -1, 0f, RedStar, 1f, 1f);
        MistRgb dusk = CoverLook.DeckColour(Cloud, -1, 0f, RedStar, 0.5f, 1f);
        Assert.True(dusk.R / dusk.B > noon.R / noon.B, "the star's colour shows more at low sun");
    }

    /// <summary>The identity the landing asks for: the client's cover over the world you join — from the orbit look and
    /// the mist profile it computes itself — equals the cloud sea's colour from the environment the server sends, for any
    /// hour and the world's weather.</summary>
    [Fact]
    public void TheCoverOverAWorld_IsWhatItsCloudSeaDraws()
    {
        using var h = new ClientServerHarness(Content);
        StarMapData? map = null;
        WorldEnvironment? env = null;
        h.Client.StarMapReceived += m => map = m;
        h.Client.WorldEnvironmentReceived += m => env = m;
        h.Join();
        var join = h.JoinAccepted!;
        h.Client.SendRequestStarMap();
        Assert.True(h.PumpUntil(() => map != null && env != null, 30), "the star map and the environment must arrive");

        var here = map!.Systems.SelectMany(s => s.Bodies).Single(b => b.Id == map.ActiveLocationId);
        var planet = Content.GetPlanet(here.PlanetType!)!;
        var orbit = OrbitLook.For(planet, join.WorldSeed, here.Id, join.TerrainGeneration);
        Assert.True(orbit.HasAir);
        int mist = MistProfiles.For(join.WorldSeed, here.Id, planet, 1.0, airless: false, terrainGeneration: join.TerrainGeneration).ColorRgb;
        Assert.Equal(env!.MistColor, mist);
        Assert.Equal(here.Id, join.LocationId); // generation 48: the join names the body, the client builds its colour harmony from it

        foreach (float day in new[] { 0f, 0.3f, 0.7f, 1f })
        {
            var (cover, _) = CoverLook.For(orbit, mist, env.SunColor, day, env.Weather);
            MistRgb sea = CoverLook.DeckColour(env.CloudColor, env.MistColor, CoverLook.MistShare, env.SunColor, day,
                CloudDeck.Target(env.Weather, env.CloudDensity).Darken);
            Assert.Equal(sea.R, cover.R, 5);
            Assert.Equal(sea.G, cover.G, 5);
            Assert.Equal(sea.B, cover.B, 5);
        }
    }
}

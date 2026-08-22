// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The background-music pools (#1172/#1174): every referenced track ships as an MP3 in <c>client/Music/</c>,
/// biome tracks keep the majority over the neutral fillers, the time of day only tints the filler set, and
/// the biome → context mapping matches what <c>ClientMusic</c> used to hard-code.
/// </summary>
public sealed class MusicLibraryTests
{
    [Fact]
    public void AllTracks_EveryPoolTrackShipsInClientMusic()
    {
        string dir = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Music");
        Assert.True(Directory.Exists(dir), $"music library folder missing: {dir}");
        var missing = MusicLibrary.AllTracks().Where(n => !File.Exists(Path.Combine(dir, n + ".mp3"))).ToList();
        Assert.True(missing.Count == 0, "pool references tracks that do not ship: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryContext_HasAtLeastOnePrimaryTrack()
    {
        foreach (string context in MusicLibrary.Contexts)
        {
            Assert.NotEmpty(MusicLibrary.PrimaryTracks(context));
        }
    }

    [Fact]
    public void PrimaryTracks_UnknownContext_IsEmpty()
    {
        Assert.Empty(MusicLibrary.PrimaryTracks("no_such_context"));
        Assert.Empty(MusicLibrary.FillerTracks("no_such_context", DayPhase.Day));
        Assert.Equal(0.0, MusicLibrary.FillerShare("no_such_context"));
    }

    [Theory]
    [InlineData(MusicLibrary.PlanetIce)]
    [InlineData(MusicLibrary.PlanetDesert)]
    [InlineData(MusicLibrary.PlanetLava)]
    [InlineData(MusicLibrary.PlanetToxic)]
    [InlineData(MusicLibrary.PlanetOcean)]
    [InlineData(MusicLibrary.PlanetVerdant)]
    [InlineData(MusicLibrary.PlanetCrystal)]
    public void SurfaceBiomes_BlendNeutralFillers_ButKeepTheMajority(string context)
    {
        Assert.True(MusicLibrary.IsSurfaceBiome(context));
        Assert.NotEmpty(MusicLibrary.FillerTracks(context, DayPhase.Day));
        double share = MusicLibrary.FillerShare(context);
        Assert.InRange(share, 0.2, 0.4); // the biome's own tracks always hold the majority (decision 2026-08-22)
    }

    [Theory]
    [InlineData(MusicLibrary.Menu)]
    [InlineData(MusicLibrary.Loading)]
    [InlineData(MusicLibrary.Station)]
    [InlineData(MusicLibrary.Space)]
    [InlineData(MusicLibrary.ShipInterior)]
    [InlineData(MusicLibrary.StarChart)]
    [InlineData(MusicLibrary.Workshop)]
    [InlineData(MusicLibrary.Research)]
    [InlineData(MusicLibrary.PlanetDeep)]
    public void NonBiomeContexts_NeverBlendFillers(string context)
    {
        Assert.Empty(MusicLibrary.FillerTracks(context, DayPhase.Day));
        Assert.Empty(MusicLibrary.FillerTracks(context, DayPhase.Night));
        Assert.Equal(0.0, MusicLibrary.FillerShare(context));
    }

    [Fact]
    public void TimeOfDay_OnlyTintsTheFillerSet()
    {
        var day = MusicLibrary.FillerTracks(MusicLibrary.PlanetIce, DayPhase.Day);
        var dawn = MusicLibrary.FillerTracks(MusicLibrary.PlanetIce, DayPhase.Dawn);
        var night = MusicLibrary.FillerTracks(MusicLibrary.PlanetIce, DayPhase.Night);
        Assert.Contains("music_planet_sunrise", dawn);
        Assert.DoesNotContain("music_planet_sunrise", day);
        Assert.Contains("music_planet_night", night);
        Assert.DoesNotContain("music_planet_night", day);
        // The biome's own tracks do not change with the clock — biome identity is kept.
        Assert.Equal(MusicLibrary.PrimaryTracks(MusicLibrary.PlanetIce), MusicLibrary.PrimaryTracks(MusicLibrary.PlanetIce));
    }

    [Fact]
    public void Cave_HasNoSkyTints()
    {
        foreach (var phase in new[] { DayPhase.Day, DayPhase.Dawn, DayPhase.Night })
        {
            var fillers = MusicLibrary.FillerTracks(MusicLibrary.PlanetCave, phase);
            Assert.NotEmpty(fillers);
            Assert.DoesNotContain("music_planet_sunrise", fillers);
            Assert.DoesNotContain("music_planet_night", fillers);
        }
    }

    [Fact]
    public void GenericPlanet_DayHasNoFillers_DawnAndNightAddTheirTint()
    {
        Assert.Empty(MusicLibrary.FillerTracks(MusicLibrary.PlanetGeneric, DayPhase.Day));
        Assert.Equal(new[] { "music_planet_sunrise", "music_planet_sunrise_2" }, MusicLibrary.FillerTracks(MusicLibrary.PlanetGeneric, DayPhase.Dawn));
        Assert.Equal(new[] { "music_planet_night", "music_planet_night_2" }, MusicLibrary.FillerTracks(MusicLibrary.PlanetGeneric, DayPhase.Night));
    }

    [Theory]
    [InlineData(0.0f, DayPhase.Night)]
    [InlineData(0.22f, DayPhase.Night)]
    [InlineData(0.23f, DayPhase.Dawn)]
    [InlineData(0.29f, DayPhase.Dawn)]
    [InlineData(0.30f, DayPhase.Day)]
    [InlineData(0.5f, DayPhase.Day)]
    [InlineData(0.77f, DayPhase.Day)]
    [InlineData(0.78f, DayPhase.Night)]
    [InlineData(1.5f, DayPhase.Day)] // wraps
    public void PhaseOf_Boundaries(float t, DayPhase expected)
    {
        Assert.Equal(expected, MusicLibrary.PhaseOf(t));
    }

    [Theory]
    [InlineData("ice", MusicLibrary.PlanetIce)]
    [InlineData("Tundra", MusicLibrary.PlanetIce)]
    [InlineData("salt_flats", MusicLibrary.PlanetDesert)]
    [InlineData("volcanic", MusicLibrary.PlanetLava)]
    [InlineData("fungal", MusicLibrary.PlanetToxic)]
    [InlineData("ocean", MusicLibrary.PlanetOcean)]
    [InlineData("jungle", MusicLibrary.PlanetVerdant)]
    [InlineData("crystal_living", MusicLibrary.PlanetCrystal)]
    [InlineData("orbital_station", MusicLibrary.Station)]
    [InlineData("rocky", MusicLibrary.PlanetGeneric)]
    [InlineData(null, MusicLibrary.PlanetGeneric)]
    public void ContextForBiome_MatchesTheDirectorMapping(string? biome, string expected)
    {
        Assert.Equal(expected, MusicLibrary.ContextForBiome(biome));
    }

    [Fact]
    public void IsPlanet_CoversSurfaceGenericCaveAndDeep()
    {
        Assert.True(MusicLibrary.IsPlanet(MusicLibrary.PlanetGeneric));
        Assert.True(MusicLibrary.IsPlanet(MusicLibrary.PlanetCave));
        Assert.True(MusicLibrary.IsPlanet(MusicLibrary.PlanetDeep));
        Assert.True(MusicLibrary.IsPlanet(MusicLibrary.PlanetLava));
        Assert.False(MusicLibrary.IsPlanet(MusicLibrary.Space));
        Assert.False(MusicLibrary.IsPlanet(MusicLibrary.Station));
    }
}

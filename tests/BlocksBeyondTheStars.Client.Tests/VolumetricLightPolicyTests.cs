// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3060: the volumetric sun light runs only where there is sun, air and something to carry the beams — outdoors with
/// the haze, under a roof or canopy with a little dust, and aboard or at home with the room's dust (also in the cabin out
/// in space). <see cref="VolumetricLightPolicy"/> holds the rules; these pin them.
/// </summary>
public sealed class VolumetricLightPolicyTests
{
    private static VolumetricLightTarget Target(bool enabled = true, bool spaceView = false, bool menuOpen = false,
        bool air = true, bool hazeOn = true, bool indoors = false, bool sheltered = false, float sunUp = 0.5f,
        float underwater = 0f, float weather = 0f)
        => VolumetricLightPolicy.TargetFor(enabled, spaceView, menuOpen, air, hazeOn, indoors, sheltered, sunUp, underwater, weather);

    [Fact]
    public void OpenAirByDay_RunsAtFullStrength_WithoutDust()
    {
        var t = Target();
        Assert.Equal(1f, t.Strength, 3);
        Assert.Equal(VolumetricDust.None, t.Dust);
    }

    [Fact]
    public void SwitchFlightViewAndMenus_TurnItOff()
    {
        Assert.Equal(0f, Target(enabled: false).Strength);
        Assert.Equal(0f, Target(spaceView: true, indoors: true).Strength);
        Assert.Equal(0f, Target(menuOpen: true).Strength);
    }

    [Fact]
    public void NoAir_IsOff_UnlessIndoors()
    {
        Assert.Equal(0f, Target(air: false).Strength);                  // space, a station, an airless world
        Assert.Equal(0f, Target(air: false, sheltered: true).Strength); // an airless cave has no dust to light
        var cabin = Target(air: false, indoors: true, sheltered: true);  // the ship interior out in space
        Assert.Equal(1f, cabin.Strength, 3);
        Assert.Equal(VolumetricDust.Indoor, cabin.Dust);
    }

    [Fact]
    public void Dust_FollowsWhereTheCameraIs()
    {
        Assert.Equal(VolumetricDust.Indoor, Target(indoors: true, sheltered: true).Dust); // aboard a landed ship, at home
        Assert.Equal(VolumetricDust.Shelter, Target(sheltered: true).Dust);              // a cave mouth, a forest canopy
        Assert.Equal(VolumetricDust.None, Target(sheltered: false).Dust);                // the open air: the haze alone
    }

    [Fact]
    public void HazeSwitchedOff_LeavesOnlyTheDust()
    {
        Assert.Equal(0f, Target(hazeOn: false).Strength);
        Assert.Equal(VolumetricDust.Shelter, Target(hazeOn: false, sheltered: true).Dust);
        Assert.Equal(VolumetricDust.Indoor, Target(hazeOn: false, indoors: true).Dust);
    }

    [Fact]
    public void Sun_FadesInAboveTheHorizon()
    {
        Assert.Equal(0f, Target(sunUp: -0.3f).Strength); // night
        Assert.Equal(0f, Target(sunUp: 0f).Strength);    // the sun on the horizon
        float low = VolumetricLightPolicy.SunFade(0.02f);
        float mid = VolumetricLightPolicy.SunFade(0.05f);
        Assert.InRange(low, 0.01f, mid);
        Assert.InRange(mid, low, 0.99f);
        Assert.Equal(1f, VolumetricLightPolicy.SunFade(VolumetricLightPolicy.SunFadeFull), 3);
        Assert.Equal(1f, VolumetricLightPolicy.SunFade(1f), 3);
    }

    [Fact]
    public void Water_TurnsItOff_AndWeather_OnlyDimsIt()
    {
        Assert.Equal(0f, Target(underwater: 1f).Strength);
        Assert.Equal(0.5f, Target(underwater: 0.5f).Strength, 3);
        Assert.Equal(1f - VolumetricLightPolicy.WeatherDim, Target(weather: 1f).Strength, 3);
        Assert.True(Target(weather: 1f).Strength > 0f, "a storm dims the beams but does not switch them off");
    }
}

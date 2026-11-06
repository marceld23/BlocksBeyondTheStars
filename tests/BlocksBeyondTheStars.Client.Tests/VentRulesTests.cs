// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3275: the geyser vents' reach, budget and steam colour.</summary>
public sealed class VentRulesTests
{
    [Fact]
    public void Eruptions_PlayOnlyInsideTheSteamRange()
    {
        Assert.True(VentRules.EruptionRange < VentRules.SteamRange);
        Assert.True(VentRules.SteamRange >= 90f, "a vent is a landmark: its steam shows from afar");
    }

    [Fact]
    public void TheSteam_ThinsWithDistance_AndStopsBeyondItsRange()
    {
        Assert.Equal(1f, VentRules.SteamRateScale(0f));
        Assert.Equal(1f, VentRules.SteamRateScale(10f));
        Assert.True(VentRules.SteamRateScale(50f) < 1f);
        Assert.Equal(VentRules.FarRateShare, VentRules.SteamRateScale(VentRules.SteamRange), 3);
        Assert.Equal(0f, VentRules.SteamRateScale(VentRules.SteamRange + 1f));
    }

    [Fact]
    public void TheBudget_GrowsWithThePreset()
    {
        Assert.True(VentRules.MaxVents(0) < VentRules.MaxVents(1));
        Assert.True(VentRules.MaxVents(1) < VentRules.MaxVents(2));
        Assert.True(VentRules.MaxVents(2) < VentRules.MaxVents(3));
        Assert.Equal(VentRules.MaxVents(0), VentRules.MaxVents(-1));
    }

    [Fact]
    public void BiggerEruptions_LastLonger()
    {
        Assert.True(VentRules.EruptionSeconds(1f) > VentRules.EruptionSeconds(0.4f));
        Assert.InRange(VentRules.EruptionSeconds(0f), 1f, 2f);
    }

    [Fact]
    public void TheSteam_IsWhiteWithoutAMistColour_AndOnlyLightlyTintedWithOne()
    {
        var white = VentRules.SteamTint(-1);
        Assert.Equal(white.R, white.G);
        Assert.Equal(white.G, white.B);
        Assert.True(white.R > 0.9f);

        var pink = VentRules.SteamTint(0xFF4080);
        Assert.True(pink.R > pink.G && pink.R > pink.B, "a pink mist tints the steam pink");
        Assert.All(new[] { pink.R, pink.G, pink.B }, c => Assert.InRange(c, VentRules.MinChannel, 1f));

        var black = VentRules.SteamTint(0x000000);
        Assert.All(new[] { black.R, black.G, black.B }, c => Assert.True(c >= VentRules.MinChannel, "steam never turns dark"));
    }
}

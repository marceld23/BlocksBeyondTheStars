// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3272: the sea of clouds below a summit and the dome clouds that no longer hover overhead while you stand in a cloud.
/// <see cref="CloudSeaRules"/> decides from the camera height and the band (<c>CloudBaseY</c>..<c>CloudTopY</c>) and from
/// the weather's cover (<see cref="CloudBandRules.Cover"/>).
/// </summary>
public sealed class CloudSeaRulesTests
{
    private const float Base = 120f, Top = 150f;

    [Fact]
    public void TheDeck_ShowsOnlyFromAboveTheBand()
    {
        Assert.Equal(0f, CloudSeaRules.DeckVisibility(60f, Base, Top));                       // in the valley
        Assert.Equal(0f, CloudSeaRules.DeckVisibility(Base + 10f, Base, Top));                // inside the band
        Assert.Equal(0f, CloudSeaRules.DeckVisibility(Top - CloudSeaRules.HideBelow, Base, Top));
        Assert.Equal(1f, CloudSeaRules.DeckVisibility(Top + CloudSeaRules.ShowAbove, Base, Top)); // ~6 m above: all of it
        Assert.Equal(1f, CloudSeaRules.DeckVisibility(256f, Base, Top));                      // the top of the lift

        float last = 0f;
        for (float y = Top - 4f; y <= Top + 10f; y += 0.25f)
        {
            float v = CloudSeaRules.DeckVisibility(y, Base, Top);
            Assert.True(v >= last, $"the deck flickered at {y}");
            last = v;
        }
    }

    [Fact]
    public void AWorldWithoutABand_HasNoDeck_AndKeepsItsDomeClouds()
    {
        Assert.Equal(0f, CloudSeaRules.DeckVisibility(300f, 0f, 0f));                 // an older server: no band
        Assert.Equal(0f, CloudSeaRules.DeckVisibility(300f, 100f, 102f));             // thinner than a band
        Assert.Equal(1f, CloudSeaRules.DomeLowFade(300f, 0f, 0f));
        Assert.Equal(1f, CloudSeaRules.DomeLowFade(300f, 150f, 120f));                // top below base
    }

    [Fact]
    public void TheLowDomeClouds_FadeOutThroughTheBand()
    {
        Assert.Equal(1f, CloudSeaRules.DomeLowFade(60f, Base, Top));
        Assert.Equal(1f, CloudSeaRules.DomeLowFade(Base, Base, Top));
        Assert.InRange(CloudSeaRules.DomeLowFade((Base + Top) * 0.5f, Base, Top), 0.4f, 0.6f); // standing in the cloud
        Assert.Equal(0f, CloudSeaRules.DomeLowFade(Top, Base, Top));
        Assert.Equal(0f, CloudSeaRules.DomeLowFade(Top + 50f, Base, Top));                     // looking down on the sea
    }

    [Fact]
    public void AClearDay_HasNoDeck_AndWetWeather_ClosesIt()
    {
        float clear = CloudBandRules.Cover("clear", 1f, 0.8f);
        float clouds = CloudBandRules.Cover("clouds", 0.5f, 0.5f);
        float rain = CloudBandRules.Cover("rain", 1f, 0.5f);
        Assert.Equal(0f, CloudSeaRules.Opacity(clear));
        Assert.True(CloudSeaRules.Opacity(clouds) > 0f);
        Assert.Equal(1f, CloudSeaRules.Opacity(rain));

        // A higher cover lowers the noise threshold: more of the sheet is cloud, the holes close.
        Assert.True(CloudSeaRules.Threshold(rain) < CloudSeaRules.Threshold(clouds));
        Assert.True(CloudSeaRules.Threshold(0f) + CloudSeaRules.EdgeSoftness < 1.01f);
        Assert.True(CloudSeaRules.Threshold(1f) > 0f);
    }

    [Fact]
    public void TheDeck_NeedsMediumOnADesktop()
    {
        Assert.False(CloudSeaRules.Allowed(0, browser: false));
        Assert.False(CloudSeaRules.Allowed(1, browser: false));
        Assert.True(CloudSeaRules.Allowed(2, browser: false));
        Assert.True(CloudSeaRules.Allowed(3, browser: false));
        Assert.False(CloudSeaRules.Allowed(3, browser: true));
    }

    [Fact]
    public void TheRadius_FollowsTheFarPlane_WithinItsBounds()
    {
        Assert.Equal(CloudSeaRules.MinRadius, CloudSeaRules.Radius(100f));
        Assert.Equal(900f, CloudSeaRules.Radius(1000f), 3);
        Assert.Equal(CloudSeaRules.MaxRadius, CloudSeaRules.Radius(10000f));
    }
}

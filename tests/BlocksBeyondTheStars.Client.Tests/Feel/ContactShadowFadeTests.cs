// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>
/// The contact shadow's fade (#3738) and the flying animals' drop shadow (player report 2026-11-27: an air fish gliding ten
/// blocks over a slope read as a fish climbing the mountain — a shadow straight under it shows the gap to the ground).
/// </summary>
public sealed class ContactShadowFadeTests
{
    [Fact]
    public void GroundFigure_FullWhileStanding_GoneAtItsReach_Unchanged()
    {
        Assert.Equal(ContactShadowFade.MaxAlpha, ContactShadowFade.Alpha(0f, 1f, flier: false), 4);
        Assert.Equal(0f, ContactShadowFade.Alpha(ContactShadowFade.GroundReach, 1f, flier: false), 4);
        // #3738's curve: quadratic in the nearness (half-way up → a quarter).
        Assert.Equal(ContactShadowFade.MaxAlpha * 0.25f, ContactShadowFade.Alpha(ContactShadowFade.GroundReach / 2f, 1f, flier: false), 4);
        // In the daylight on a preset with sun shadows the caller's light is 0 → no blob; the sun's shadow grounds it.
        Assert.Equal(0f, ContactShadowFade.Alpha(0f, 0f, flier: false));
    }

    [Fact]
    public void Flier_TenBlocksUp_StillCastsAVisibleDropShadow_ByNightAndByDay()
    {
        // The reported air fish hovers 9.75 blocks over the ground: a walker's blob is long gone there (reach 5).
        Assert.Equal(0f, ContactShadowFade.Alpha(10f, 1f, flier: false));
        float night = ContactShadowFade.Alpha(10f, 1f, flier: true);
        float day = ContactShadowFade.Alpha(10f, 0f, flier: true); // sun-shadow preset by day: the caller's light is 0
        Assert.True(night > 0.2f, $"night drop shadow at 10 m: {night:F3}");
        Assert.True(day > 0.12f, $"day drop shadow at 10 m: {day:F3}");
        Assert.True(day < night);
    }

    [Fact]
    public void Flier_ThinsOutWithHeight_AndIsGoneAtItsReach()
    {
        float prev = float.MaxValue;
        for (float h = 0f; h <= ContactShadowFade.FlierReach; h += 1f)
        {
            float a = ContactShadowFade.Alpha(h, 1f, flier: true);
            Assert.True(a <= prev + 1e-6f, $"never darker higher up (h {h})");
            prev = a;
        }

        Assert.Equal(0f, ContactShadowFade.Alpha(ContactShadowFade.FlierReach, 1f, flier: true), 4);
        Assert.True(ContactShadowFade.SizeFactor(12f, flier: true) < ContactShadowFade.SizeFactor(0f, flier: true));
    }

    [Fact]
    public void Flier_SittingOnTheGroundByDay_GetsNoExtraBlob_LikeAWalker()
    {
        // A bird perched on the ground in the daylight is grounded by the sun's shadow alone; the day floor ramps in
        // only once it is in the air.
        Assert.Equal(0f, ContactShadowFade.Alpha(0f, 0f, flier: true), 4);
        Assert.Equal(ContactShadowFade.FlierDayStrength, ContactShadowFade.Light(ContactShadowFade.FlierAirborneHeight, 0f, flier: true), 4);
        Assert.Equal(0.8f, ContactShadowFade.Light(5f, 0.8f, flier: true), 4); // a brighter verdict (night) is kept
        Assert.Equal(0f, ContactShadowFade.Light(5f, 0f, flier: false));         // walkers never get the floor
    }
}

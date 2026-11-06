// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3265 (caves and nights far too bright): the eye adaptation's caps and rates (<see cref="EyeAdaptationRules"/>) and the
/// dark values the Sky publishes (<see cref="DarkLightRules"/>) — the dark floor and "Visibility in the dark", the models'
/// light in the world, the gentle home-base fill.
/// </summary>
public sealed class DarkAdaptationTests
{
    [Fact]
    public void TheDarkUnderground_EarnsOnlyTheUndergroundCap()
    {
        Assert.Equal(EyeAdaptationRules.UndergroundCapEv, EyeAdaptationRules.Target(0.03f, false, exposedToSky: false, 0f, false), 4);
        Assert.Equal(EyeAdaptationRules.UndergroundCapEvMax, EyeAdaptationRules.Target(0.03f, false, exposedToSky: false, 1f, false), 4);
    }

    [Fact]
    public void AMoonlessNightUnderTheOpenSky_EarnsLessThanACave()
        => Assert.Equal(EyeAdaptationRules.OpenSkyCapEv, EyeAdaptationRules.Target(0.03f, false, exposedToSky: true, 1f, false), 4);

    [Fact]
    public void DaylightAndALitRoom_EarnNothing()
    {
        Assert.Equal(0f, EyeAdaptationRules.Target(0.9f, false, true, 0f, false));
        Assert.Equal(0f, EyeAdaptationRules.Target(0.6f, false, false, 1f, false)); // a cabin's interior fill
    }

    [Fact]
    public void ALitLamp_EarnsLessThanTheDarkAroundIt()
    {
        float withLamp = EyeAdaptationRules.Target(0.03f, lampLit: true, exposedToSky: false, 0f, false);
        Assert.True(withLamp > 0f && withLamp < EyeAdaptationRules.UndergroundCapEv, $"lamp-lit boost {withLamp}");
        Assert.Equal(EyeAdaptationRules.DarkBoost(HeadlampRules.AdaptationBrightness, false), withLamp, 5);
    }

    [Fact]
    public void ReducedEffects_HalveTheCeiling()
        => Assert.Equal(EyeAdaptationRules.MaxEv * 0.5f, EyeAdaptationRules.Target(0f, false, false, 1f, reducedEffects: true), 4);

    [Fact]
    public void TheEyes_AdaptToTheDarkInAboutFiveSeconds_AndSquintBackQuickly()
    {
        float target = EyeAdaptationRules.UndergroundCapEv;
        float ev = 0f;
        for (int frame = 0; frame < 60; frame++)
        {
            ev = EyeAdaptationRules.Step(ev, target, 1f / 60f, false);
        }

        Assert.Equal(EyeAdaptationRules.DarkRate, ev, 4); // one second in: a fifth of the way
        for (int frame = 0; frame < 4 * 60; frame++)
        {
            ev = EyeAdaptationRules.Step(ev, target, 1f / 60f, false);
        }

        Assert.Equal(target, ev, 4); // five seconds in: there

        ev = EyeAdaptationRules.Step(ev, 0f, 0.1f, false);
        Assert.Equal(target - EyeAdaptationRules.LightRate * 0.1f, ev, 4); // a tenth of a second back in the light: 0.25 EV gone
        Assert.Equal(0.14f, EyeAdaptationRules.Step(0f, 1f, 1f, reducedEffects: true), 4);
    }

    [Fact]
    public void TheDarkFloor_FollowsVisibilityInTheDark()
    {
        Assert.Equal(DarkLightRules.DarkFloorMin, DarkLightRules.DarkFloor(0f));
        Assert.Equal(DarkLightRules.DarkFloorMax, DarkLightRules.DarkFloor(1f), 5);
        Assert.Equal(DarkLightRules.DarkFloorMax, DarkLightRules.DarkFloor(7f), 5); // clamped
    }

    [Fact]
    public void TheModels_FollowTheWorldLight_AndDarkenInACave()
    {
        Assert.Equal(1f, DarkLightRules.ModelLight(DarkLightRules.NoonLuma, 1f, 0f, 0f), 4);        // noon outside: the studio look
        Assert.Equal(DarkLightRules.ModelDarkMin, DarkLightRules.ModelLight(0f, 1f, 0f, 0f), 4);    // a moonless night
        Assert.Equal(DarkLightRules.ModelCave, DarkLightRules.ModelLight(DarkLightRules.NoonLuma, 0f, 0f, 0f), 4); // a cave at noon
        Assert.Equal(DarkLightRules.ModelDarkMin, DarkLightRules.ModelLight(0f, 0f, 0f, 0f), 4);    // never brighter than the night outside
        Assert.Equal(1f, DarkLightRules.ModelLight(0f, 0f, 1f, 0f), 4);                             // aboard: the cabin fill
        Assert.Equal(DarkLightRules.ModelDarkMax, DarkLightRules.ModelLight(0f, 0f, 0f, 1f), 4);    // "Visibility in the dark" lifts it
    }

    [Fact]
    public void TheHomeBaseFill_IsGentlerThanACabin()
        => Assert.InRange(DarkLightRules.HomeBaseFill, 0.2f, 0.4f);
}

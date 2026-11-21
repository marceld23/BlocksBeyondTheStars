// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>#3925: how hot the boost looks — it eases in fast, holds while the boost burns, falls back slower and never
/// leaves 0…1, whatever the frame rate.</summary>
[Trait("Suite", "ClientCore")]
public sealed class BoostLookTests
{
    private const float Frame = 1f / 60f;

    private static float Run(ref BoostLook look, float seconds, bool burning, float dt = Frame)
    {
        for (float t = 0f; t < seconds - 1e-5f; t += dt)
        {
            look.Tick(dt, burning);
        }

        return look.Value;
    }

    [Fact]
    public void ACold_Look_StartsAtZero_AndScalesNothing()
    {
        var look = default(BoostLook);
        Assert.Equal(0f, look.Value);
        Assert.Equal(1f, look.Scale(BoostLook.FlameEmission));
        Assert.Equal(1f, look.Scale(BoostLook.TrailRate, share: 1f));
    }

    [Fact]
    public void TheLook_EasesIn_OverAboutAFifthOfASecond()
    {
        var look = default(BoostLook);
        float half = Run(ref look, BoostLook.RiseSeconds / 2f, burning: true);
        Assert.InRange(half, 0.2f, 0.8f); // on its way, not snapped on
        float full = Run(ref look, BoostLook.RiseSeconds / 2f + Frame, burning: true);
        Assert.Equal(1f, full, 4);
    }

    [Fact]
    public void TheLook_EasesInGently_TheFirstFrameMovesItOnlyALittle()
    {
        var look = default(BoostLook);
        look.Tick(Frame, burning: true);
        Assert.InRange(look.Value, 0.001f, 0.15f);
    }

    [Fact]
    public void TheLook_HoldsAtFull_WhileTheBoostBurns()
    {
        var look = default(BoostLook);
        Run(ref look, 0.3f, burning: true);
        for (int i = 0; i < 600; i++)
        {
            Assert.Equal(1f, look.Tick(Frame, burning: true));
        }

        Assert.Equal(BoostLook.FlameEmission, look.Scale(BoostLook.FlameEmission), 4);
    }

    [Fact]
    public void TheLook_FallsBack_SlowerThanItRose()
    {
        var look = default(BoostLook);
        Run(ref look, 0.3f, burning: true);
        float afterRise = Run(ref look, BoostLook.RiseSeconds, burning: false);
        Assert.InRange(afterRise, 0.3f, 0.99f); // the time it took to rise has not put it out
        Run(ref look, BoostLook.FallSeconds, burning: false);
        Assert.Equal(0f, look.Value);
        Assert.Equal(1f, look.Scale(BoostLook.FlameSize));
    }

    [Theory]
    [InlineData(1f / 15f)]
    [InlineData(1f / 30f)]
    [InlineData(1f / 144f)]
    [InlineData(0.5f)] // a hitch longer than either ease
    public void TheLook_NeverOvershoots_AtAnyFrameRate(float dt)
    {
        var look = default(BoostLook);
        var rng = new System.Random(3925);
        float last = 0f;
        for (int i = 0; i < 2000; i++)
        {
            bool burning = rng.Next(4) != 0;
            float v = look.Tick(dt, burning);
            Assert.InRange(v, 0f, 1f);
            Assert.True(burning ? v >= last : v <= last, "the look only ever moves toward its goal");
            last = v;
        }
    }

    [Fact]
    public void ABrokenFrameTime_ChangesNothing()
    {
        var look = default(BoostLook);
        Run(ref look, BoostLook.RiseSeconds / 2f, burning: true);
        float before = look.Value;
        look.Tick(0f, burning: true);
        look.Tick(-1f, burning: false);
        look.Tick(float.NaN, burning: true);
        look.Tick(float.PositiveInfinity, burning: false);
        Assert.Equal(before, look.Value);
    }

    [Fact]
    public void Reset_TurnsTheLookCold()
    {
        var look = default(BoostLook);
        Run(ref look, 0.3f, burning: true);
        look.Reset();
        Assert.Equal(0f, look.Value);
    }

    [Fact]
    public void TheExtraParticles_FollowTheTiersShare()
    {
        var look = default(BoostLook);
        Run(ref look, 0.3f, burning: true);
        Assert.Equal(2f, look.Scale(BoostLook.TrailRate, share: 1f), 4);
        Assert.Equal(1.5f, look.Scale(BoostLook.TrailRate, share: 0.5f), 4);
        Assert.Equal(1f, look.Scale(BoostLook.TrailRate, share: 0f), 4);
        Assert.Equal(2f, look.Scale(BoostLook.TrailRate, share: 7f), 4); // never more than the full factor

        Assert.Equal(0f, BoostLook.ParticleShare(lowestTier: true, density: 0.35f));
        Assert.Equal(0.6f, BoostLook.ParticleShare(lowestTier: false, density: 0.6f), 4);
        Assert.Equal(1f, BoostLook.ParticleShare(lowestTier: false, density: 1f), 4);
        Assert.False(BoostLook.IgnitionBurst(lowestTier: true, density: 0.35f));
        Assert.True(BoostLook.IgnitionBurst(lowestTier: false, density: 0.3f));
    }

    [Fact]
    public void TheGaugePulse_IsGentle_AndSteadyWhenAsked()
    {
        for (float t = 0f; t < 3f; t += Frame)
        {
            Assert.InRange(BoostLook.BarPulse(t, steady: false), 0f, 1f);
            Assert.Equal(0f, BoostLook.BarPulse(t, steady: true));
        }

        Assert.Equal(0f, BoostLook.BarPulse(0f, steady: false), 4);
        Assert.Equal(1f, BoostLook.BarPulse(0.5f / BoostLook.BarPulseHz, steady: false), 4);
        Assert.Equal(0f, BoostLook.BarPulse(float.NaN, steady: false));
        Assert.True(BoostLook.BarPulseHz < 3f, "a pulse at or above 3 Hz counts as flashing");
    }

    [Fact]
    public void TheCameraNumbers_WidenOnIgnition_AndHoldLessWhileBurning()
    {
        Assert.True(BoostLook.IgnitionFovDegrees > BoostLook.BurnFovDegrees);
        Assert.InRange(BoostLook.IgnitionTrauma, 0.1f, 0.35f); // a small jolt, not an explosion
        Assert.InRange(BoostLook.DustShare, 0.1f, 0.5f);       // speed lines, not the hyperjump's star lines
    }
}

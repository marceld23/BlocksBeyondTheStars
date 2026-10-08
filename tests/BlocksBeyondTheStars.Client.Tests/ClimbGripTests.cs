// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The wall climber's grip (#2189): never shown, only felt. It lasts ~10 s of climbing up at 1 g, much longer on an
/// asteroid and shorter on a heavy planet; slippery walls tire it twice as fast and climbing gear takes a share of the
/// drain away; it slows the climb, then strains the body, then lets the climber slide; it refills only standing.
/// </summary>
public sealed class ClimbGripTests
{
    [Fact]
    public void AFreshGrip_IsFull_AndFeelsFine()
    {
        var grip = new ClimbGrip();

        Assert.Equal(1f, grip.Value);
        Assert.True(grip.CanGrab);
        Assert.False(grip.Exhausted);
        Assert.Equal(1f, grip.SpeedFactor);
        Assert.Equal(0f, grip.Strain);
    }

    [Fact]
    public void ClimbingUpAtOneG_LastsTenSeconds_AboutTwentyBlocks()
    {
        float seconds = ClimbGrip.SecondsFromFull(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f);

        Assert.Equal(10f, seconds, 3);
        Assert.Equal(20f, seconds * ClimbGrip.UpSpeed, 1);
    }

    [Fact]
    public void Gravity_DecidesHowFarYouClimb()
    {
        float asteroid = ClimbGrip.SecondsFromFull(ClimbMotion.Up, 0.45f, ClimbSurface.Normal, 0f);
        float moon = ClimbGrip.SecondsFromFull(ClimbMotion.Up, 0.7f, ClimbSurface.Normal, 0f);
        float heavy = ClimbGrip.SecondsFromFull(ClimbMotion.Up, 1.6f, ClimbSurface.Normal, 0f);

        Assert.True(asteroid > moon && moon > 10f && heavy < 10f);
        Assert.Equal(10f / 0.45f, asteroid, 2);
        Assert.Equal(6.25f, heavy, 2);
    }

    [Fact]
    public void HangingTiresLessThanClimbing_AndClimbingDownLeast()
    {
        float up = ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f);
        float side = ClimbGrip.DrainPerSecond(ClimbMotion.Side, 1f, ClimbSurface.Normal, 0f);
        float hang = ClimbGrip.DrainPerSecond(ClimbMotion.Hang, 1f, ClimbSurface.Normal, 0f);
        float down = ClimbGrip.DrainPerSecond(ClimbMotion.Down, 1f, ClimbSurface.Normal, 0f);

        Assert.True(up > side && side > hang && hang > down && down > 0f);
    }

    [Fact]
    public void SlipperyWalls_TireTwiceAsFast_AndGear_TakesAShareAway_ButNeverAll()
    {
        float normal = ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f);

        Assert.Equal(normal * 2f, ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Slippery, 0f), 5);
        Assert.Equal(normal * 0.6f, ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0.4f), 5); // gloves
        Assert.Equal(normal * 0.4f, ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 0.6f), 5); // claws
        Assert.True(ClimbGrip.DrainPerSecond(ClimbMotion.Up, 1f, ClimbSurface.Normal, 5f) > 0f);                 // capped
    }

    [Fact]
    public void ATiringGrip_SlowsTheClimb_ThenStrains_ThenSlides()
    {
        var grip = new ClimbGrip();

        grip.Drain(8f, ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f); // 0.2 left
        Assert.Equal(0.2f, grip.Value, 3);
        Assert.Equal(0.75f, grip.SpeedFactor, 3);
        Assert.Equal(0.2f, grip.Strain, 3);
        Assert.False(grip.CanGrab); // a new grab needs more than 0.2

        grip.Drain(60f, ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f);
        Assert.Equal(0f, grip.Value);
        Assert.True(grip.Exhausted);
        Assert.Equal(0.5f, grip.SpeedFactor, 3);
        Assert.Equal(1f, grip.Strain, 3);
    }

    [Fact]
    public void Standing_RefillsTheGrip_InASecondAndAHalf()
    {
        var grip = new ClimbGrip();
        grip.Drain(60f, ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f);

        grip.Refill(0.75f);
        Assert.Equal(0.5f, grip.Value, 3);

        grip.Refill(10f);
        Assert.Equal(1f, grip.Value);
    }

    [Fact]
    public void TheSlide_StaysFarBelowTheSafeLandingSpeed()
        => Assert.True(ClimbGrip.SlideSpeed < 14f / 2f); // the client's safe fall speed is 14 m/s at 1 g

    [Fact]
    public void LettingGoOfJump_Slides_ASpentHeldGripSlidesSlower_AHeldGripHolds()
    {
        // #2384: holding on is a held button.
        Assert.Equal(ClimbGrip.ReleaseSlideSpeed, ClimbGrip.SlideSpeedFor(holding: false, exhausted: false));
        Assert.Equal(ClimbGrip.ReleaseSlideSpeed, ClimbGrip.SlideSpeedFor(holding: false, exhausted: true));
        Assert.Equal(ClimbGrip.SlideSpeed, ClimbGrip.SlideSpeedFor(holding: true, exhausted: true));
        Assert.Equal(0f, ClimbGrip.SlideSpeedFor(holding: true, exhausted: false));
        Assert.Equal(4f, ClimbGrip.ReleaseSlideSpeed);
    }

    [Fact]
    public void TheReleaseSlide_NeverHurts_OnAnyWorld()
    {
        // The safe-landing speed scales with √gravity (14 m/s at 1 g); the lightest world the client allows is 0.2 g.
        float lightestSafeSpeed = 14f * (float)System.Math.Sqrt(0.2);
        Assert.True(ClimbGrip.ReleaseSlideSpeed < lightestSafeSpeed);
    }
}

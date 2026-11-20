// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3860: swimming no longer bounces. The climb-out hop fires only where a bank stands in front, the body floats at the
/// surface whether Jump is held or not, the Descend key dives, and a sprint swim lies level — at a speed between the plain
/// swim and the sprint swim.
/// </summary>
public sealed class SwimRulesTests
{
    private const float Up = 4f;
    private const float Sink = 1.5f;

    [Fact]
    public void OpenWater_NeverHops_WithJumpAndForwardHeld()
    {
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: true, ledgeAhead: false, bankAhead: false));
    }

    [Fact]
    public void ABank_InFront_MountsTheLand()
    {
        Assert.True(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: false, ledgeAhead: true, bankAhead: true)); // swim into a low bank
        Assert.True(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: true, ledgeAhead: false, bankAhead: true));  // Jump at a higher face
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: false, ledgeAhead: false, bankAhead: true)); // a face without Jump
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: false, jumpHeld: true, ledgeAhead: true, bankAhead: true));  // under water
        Assert.False(SwimRules.ClimbOutHop(pushing: false, atSurface: true, jumpHeld: true, ledgeAhead: true, bankAhead: true));  // not moving
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheSurface_TheBodyFloats_JumpOrNot(bool jump)
    {
        Assert.Equal(0f, SwimRules.VerticalTarget(atSurface: true, jumpHeld: jump, diveHeld: false, Up, Sink));
    }

    [Fact]
    public void BelowTheSurface_JumpRises_AndNothingHeldSinksSlowly()
    {
        Assert.Equal(Up, SwimRules.VerticalTarget(atSurface: false, jumpHeld: true, diveHeld: false, Up, Sink));
        Assert.Equal(-Sink, SwimRules.VerticalTarget(atSurface: false, jumpHeld: false, diveHeld: false, Up, Sink));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Descend_Dives_EvenAtTheSurfaceAndOverJump(bool atSurface, bool jump)
    {
        Assert.Equal(-SwimRules.DiveSpeed, SwimRules.VerticalTarget(atSurface, jump, diveHeld: true, Up, Sink));
    }

    [Fact]
    public void TheFloat_SettlesStifferThanTheSwim_SoARiseDoesNotPopOut()
    {
        Assert.Equal(SwimRules.SurfaceAccel, SwimRules.VerticalAccel(atSurface: true, diveHeld: false, swimAccel: 12f));
        Assert.Equal(12f, SwimRules.VerticalAccel(atSurface: false, diveHeld: false, swimAccel: 12f));
        Assert.Equal(12f, SwimRules.VerticalAccel(atSurface: true, diveHeld: true, swimAccel: 12f));

        // A rise at the swim-up speed stops within the band where the chest is wet and the head is dry (~0.8 m).
        float stop = Up * Up / (2f * SwimRules.SurfaceAccel);
        Assert.True(stop < 0.4f, $"a rise overshoots the waterline by {stop} m");
    }

    [Fact]
    public void ASprintSwim_LiesLevel_APlainSwimDoesNot()
    {
        const float walk = 6f, swimMul = 0.62f;
        Assert.False(SwimRules.LiesLevel(swimming: true, speed: walk * swimMul));
        Assert.True(SwimRules.LiesLevel(swimming: true, speed: walk * swimMul * SprintState.SpeedFactor));
        Assert.False(SwimRules.LiesLevel(swimming: false, speed: 9f)); // a sprint on land stays upright
    }
}

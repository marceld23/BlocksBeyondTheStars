// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The body's stamina (#3373): sprinting and climbing spend the same value; it comes back only at rest, after a short
/// pause and slowly; a spent stamina slows a climb, strains the body and lets a climber slide, and blocks a new sprint
/// until a quarter is back.
/// </summary>
public sealed class BodyStaminaTests
{
    [Fact]
    public void AFreshBody_IsFull_AndFeelsFine()
    {
        var stamina = new BodyStamina();

        Assert.Equal(1f, stamina.Value);
        Assert.True(stamina.Full);
        Assert.True(stamina.CanGrab);
        Assert.True(stamina.CanSprint);
        Assert.False(stamina.Exhausted);
        Assert.False(stamina.Spent);
        Assert.Equal(1f, stamina.SpeedFactor);
        Assert.Equal(0f, stamina.Strain);
    }

    [Fact]
    public void ATiringBody_SlowsTheClimb_ThenStrains_ThenIsSpent()
    {
        var stamina = new BodyStamina();

        stamina.Spend(0.8f); // 0.2 left
        Assert.Equal(0.2f, stamina.Value, 3);
        Assert.Equal(0.75f, stamina.SpeedFactor, 3);
        Assert.Equal(0.2f, stamina.Strain, 3);
        Assert.False(stamina.CanGrab); // a new grab needs more than 0.2
        Assert.True(stamina.CanSprint); // not run dry yet

        stamina.Spend(5f);
        Assert.Equal(0f, stamina.Value);
        Assert.True(stamina.Exhausted);
        Assert.True(stamina.Spent);
        Assert.False(stamina.CanSprint);
        Assert.Equal(0.5f, stamina.SpeedFactor, 3);
        Assert.Equal(1f, stamina.Strain, 3);
    }

    [Fact]
    public void Resting_WaitsASecond_ThenRefillsSlowly_InSixSeconds()
    {
        var stamina = new BodyStamina();
        stamina.Spend(1f);

        stamina.Rest(0.5f);
        Assert.Equal(0f, stamina.Value); // still catching breath

        stamina.Rest(0.5f); // the pause is over: this step already counts
        stamina.Rest(2.5f);
        Assert.Equal(0.5f, stamina.Value, 3); // 3 s of refill = half

        stamina.Rest(10f);
        Assert.Equal(1f, stamina.Value);
        Assert.True(stamina.Full);
    }

    [Fact]
    public void SpendingAgain_RestartsThePause()
    {
        var stamina = new BodyStamina();
        stamina.Spend(0.5f);
        stamina.Rest(3f);
        float before = stamina.Value;

        stamina.Spend(0.01f);
        stamina.Rest(0.9f);

        Assert.Equal(before - 0.01f, stamina.Value, 4);
    }

    [Fact]
    public void ARunDryBody_SprintsAgain_OnlyWithAQuarterBack()
    {
        var stamina = new BodyStamina();
        stamina.Spend(1f);
        stamina.Rest(0.99f); // the pause

        stamina.Rest(1f); // ~0.17
        Assert.True(stamina.Value is > 0f and < BodyStamina.SprintRecoverAt);
        Assert.True(stamina.Spent);
        Assert.False(stamina.CanSprint);

        stamina.Rest(1f); // ~0.33
        Assert.False(stamina.Spent);
        Assert.True(stamina.CanSprint);
    }

    [Fact]
    public void SprintAndClimb_ShareOneStamina()
    {
        var stamina = new BodyStamina();

        stamina.Spend(4f * SprintState.DrainPerSecond); // a 4 s run: half
        ClimbGrip.Drain(stamina, 3f, ClimbMotion.Up, 1f, ClimbSurface.Normal, 0f); // 3 s up a wall: 0.3

        Assert.Equal(0.2f, stamina.Value, 3);
        Assert.False(stamina.CanGrab); // no fresh grab after the long run and climb
    }

    [Fact]
    public void Reset_GivesAFreshBody()
    {
        var stamina = new BodyStamina();
        stamina.Spend(1f);

        stamina.Reset();

        Assert.Equal(1f, stamina.Value);
        Assert.False(stamina.Spent);
    }

    [Fact]
    public void NonsenseSteps_ChangeNothing()
    {
        var stamina = new BodyStamina();
        stamina.Spend(0.5f);

        stamina.Spend(-1f);
        stamina.Spend(float.NaN);
        stamina.Rest(-1f);
        stamina.Rest(float.NaN);

        Assert.Equal(0.5f, stamina.Value, 4);
    }
}

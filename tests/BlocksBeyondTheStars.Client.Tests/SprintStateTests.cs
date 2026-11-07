// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Sprinting on foot (#3374, #3376): the keyboard holds its key, the pad and touch switch it on while moving; a run needs
/// forward input, starts on the ground, survives a jump, and stops when the stamina runs out or the place forbids it. The
/// stamina bar shows only while the stamina is not full (#3375).
/// </summary>
public sealed class SprintStateTests
{
    [Fact]
    public void ARun_IsHalfAgainAsFast_AndLastsEightSeconds()
    {
        Assert.Equal(1.5f, SprintState.SpeedFactor);
        Assert.Equal(8f, 1f / SprintState.DrainPerSecond, 3);
    }

    [Fact]
    public void HoldingTheKey_RunsForward_OnTheGround()
    {
        var sprint = new SprintState();

        Assert.True(sprint.Step(keyHeld: true, togglePressed: false, 0f, 1f, grounded: true, allowed: true, canSprint: true));
        Assert.False(sprint.Step(keyHeld: false, togglePressed: false, 0f, 1f, grounded: true, allowed: true, canSprint: true));
    }

    [Fact]
    public void NoRun_SidewaysBackwardsOrStanding()
    {
        var sprint = new SprintState();

        Assert.False(sprint.Step(true, false, 1f, 0f, true, true, true));   // sideways
        Assert.False(sprint.Step(true, false, 0f, -1f, true, true, true));  // backwards
        Assert.False(sprint.Step(true, false, 0f, 0f, true, true, true));   // standing
        Assert.True(sprint.Step(true, false, 0.7f, 0.7f, true, true, true)); // diagonally forward is fine
    }

    [Fact]
    public void ARun_StartsOnTheGround_AndAJumpKeepsIt()
    {
        var sprint = new SprintState();

        Assert.False(sprint.Step(true, false, 0f, 1f, grounded: false, true, true)); // no start in the air
        Assert.True(sprint.Step(true, false, 0f, 1f, grounded: true, true, true));
        Assert.True(sprint.Step(true, false, 0f, 1f, grounded: false, true, true)); // the jump keeps the speed
    }

    [Fact]
    public void ARun_StopsWhereRunningIsNotAllowed_OrWhenTheStaminaIsGone()
    {
        var sprint = new SprintState();
        sprint.Step(true, false, 0f, 1f, true, true, true);

        Assert.False(sprint.Step(true, false, 0f, 1f, true, allowed: false, canSprint: true)); // crouched, swimming, …
        Assert.True(sprint.Step(true, false, 0f, 1f, true, true, true));
        Assert.False(sprint.Step(true, false, 0f, 1f, true, true, canSprint: false));
    }

    [Fact]
    public void TheSwitch_TurnsOnWhileMoving_AndOffWhenStopping()
    {
        var sprint = new SprintState();

        Assert.True(sprint.Step(false, togglePressed: true, 0f, 1f, true, true, true));
        Assert.True(sprint.Latched);
        Assert.True(sprint.Step(false, false, 0f, 1f, true, true, true)); // stays on without holding anything

        Assert.False(sprint.Step(false, false, 0f, 0f, true, true, true)); // stopped
        Assert.False(sprint.Latched);
        Assert.False(sprint.Step(false, false, 0f, 1f, true, true, true)); // walking on does not bring it back
    }

    [Fact]
    public void TheSwitch_DoesNothingWhileStanding_AndASecondPressTurnsItOff()
    {
        var sprint = new SprintState();

        Assert.False(sprint.Step(false, togglePressed: true, 0f, 0f, true, true, true));
        Assert.False(sprint.Latched);

        sprint.Step(false, true, 0f, 1f, true, true, true);
        Assert.False(sprint.Step(false, togglePressed: true, 0f, 1f, true, true, true));
        Assert.False(sprint.Latched);
    }

    [Fact]
    public void TheSwitch_TurnsOff_WhenTheStaminaRunsOut()
    {
        var sprint = new SprintState();
        sprint.Step(false, true, 0f, 1f, true, true, true);

        sprint.Step(false, false, 0f, 1f, true, true, canSprint: false);
        Assert.False(sprint.Latched);
        Assert.False(sprint.Step(false, false, 0f, 1f, true, true, canSprint: true)); // back: still off until pressed again
    }

    [Fact]
    public void Stop_EndsEverything()
    {
        var sprint = new SprintState();
        sprint.Step(true, true, 0f, 1f, true, true, true);

        sprint.Stop();

        Assert.False(sprint.Active);
        Assert.False(sprint.Latched);
    }

    [Theory]
    [InlineData(true, true, false, true, 0f, 1f, true)]    // the shared stick click while walking: the run
    [InlineData(true, true, false, true, 0f, 0f, false)]   // standing: the actions list
    [InlineData(true, true, false, false, 0f, 1f, false)]  // steering a speeder, an EVA, swimming …: the actions list
    [InlineData(false, true, false, true, 0f, 1f, false)]  // the player gave sprint its own button
    [InlineData(true, true, true, true, 0f, 1f, false)]    // a key or the touch ACT button opened it
    [InlineData(true, false, false, true, 0f, 1f, false)]  // no pad press at all
    public void ThePadsStickClick_RunsWhileMoving_AndOpensTheListWhileStanding(
        bool sameButton, bool padPressed, bool otherSourcePressed, bool runPossible, float strafe, float forward, bool expected)
        => Assert.Equal(expected, SprintState.PadPressIsSprint(sameButton, padPressed, otherSourcePressed, runPossible, strafe, forward));

    [Fact]
    public void TheBar_StartsHidden_ShowsWhileNotFull_AndFadesAfterASecondFull()
    {
        var bar = new StaminaBarFade();

        Assert.Equal(0f, bar.Step(0.1f, 1f, reducedMotion: false));
        bar.Step(1f, 0.9f, false);
        Assert.Equal(1f, bar.Alpha);

        bar.Step(0.5f, 1f, false); // full again: kept a moment
        Assert.Equal(1f, bar.Alpha);
        bar.Step(0.6f, 1f, false); // the hold is over, the fade starts
        bar.Step(StaminaBarFade.FadeSeconds, 1f, false);
        Assert.Equal(0f, bar.Alpha);
    }

    [Fact]
    public void TheBar_UnderReducedMotion_AppearsAndGoesAtOnce()
    {
        var bar = new StaminaBarFade();

        Assert.Equal(1f, bar.Step(0.01f, 0.5f, reducedMotion: true));
        bar.Step(0.01f, 1f, true);
        Assert.Equal(1f, bar.Alpha); // still the hold
        Assert.Equal(0f, bar.Step(StaminaBarFade.HoldFull, 1f, true));
    }
}

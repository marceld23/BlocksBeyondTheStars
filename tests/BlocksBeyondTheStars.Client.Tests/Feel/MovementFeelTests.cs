// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>
/// The feel rules of moving on foot and at the helm: the jump's edge grace and remembered tap, the walk's own start
/// and stop curve, the eye that stays behind on an auto-step, and the ship that spools up, coasts and leans.
/// </summary>
public sealed class MovementFeelTests
{
    private const float Frame = 1f / 60f;

    // ---------------- JumpAssist ----------------

    [Fact]
    public void WalkingOffAnEdge_StillJumps_ForAMoment()
    {
        var jump = new JumpAssist();
        jump.Tick(Frame, free: true, grounded: true, pressed: false);
        Assert.False(jump.CoyoteReady); // standing is no edge grace — the ground rule jumps

        jump.Tick(Frame, free: true, grounded: false, pressed: false);
        Assert.True(jump.CoyoteReady);

        for (float t = 0f; t < JumpAssist.CoyoteSeconds; t += Frame)
        {
            jump.Tick(Frame, free: true, grounded: false, pressed: false);
        }

        Assert.False(jump.CoyoteReady); // the grace is over: a real fall
    }

    [Fact]
    public void AfterAJump_ThereIsNoSecondJumpInTheAir()
    {
        var jump = new JumpAssist();
        jump.Tick(Frame, free: true, grounded: true, pressed: true);
        jump.Jumped();
        jump.Tick(Frame, free: true, grounded: false, pressed: false);

        Assert.False(jump.CoyoteReady);
    }

    [Fact]
    public void ATapJustBeforeLanding_IsRemembered_AnEarlyOneIsNot()
    {
        var jump = new JumpAssist();
        jump.Tick(Frame, free: true, grounded: false, pressed: true);
        jump.Tick(Frame, free: true, grounded: false, pressed: false);
        jump.Tick(Frame, free: true, grounded: true, pressed: false);
        Assert.True(jump.BufferedPress);

        jump.Jumped();
        Assert.False(jump.BufferedPress); // spent by the jump it caused

        var early = new JumpAssist();
        early.Tick(Frame, free: true, grounded: false, pressed: true);
        for (float t = 0f; t < JumpAssist.BufferSeconds + Frame; t += Frame)
        {
            early.Tick(Frame, free: true, grounded: false, pressed: false);
        }

        early.Tick(Frame, free: true, grounded: true, pressed: false);
        Assert.False(early.BufferedPress);
    }

    [Fact]
    public void ASpecialState_ClearsTheGraceAndTheTap()
    {
        var jump = new JumpAssist();
        jump.Tick(Frame, free: true, grounded: true, pressed: false);
        jump.Tick(Frame, free: true, grounded: false, pressed: true);
        Assert.True(jump.CoyoteReady);

        jump.Tick(Frame, free: false, grounded: false, pressed: false); // swam, climbed, flew …
        Assert.False(jump.CoyoteReady);
        Assert.False(jump.BufferedPress);

        jump.Tick(Frame, free: true, grounded: false, pressed: false); // back in free fall: no grace out of nowhere
        Assert.False(jump.CoyoteReady);
    }

    // ---------------- WalkInertia ----------------

    [Fact]
    public void OnTheGround_TheWalkStartsAndStopsWithinATenthOfASecond()
    {
        var walk = new WalkInertia();
        float t = 0f;
        while (walk.Y < 1f && t < 1f)
        {
            walk.Step(0f, 1f, Frame, firm: true);
            t += Frame;
        }

        Assert.InRange(t, WalkInertia.GroundStartSeconds - Frame, WalkInertia.GroundStartSeconds + (2f * Frame));

        t = 0f;
        while (walk.Y > 0f && t < 1f)
        {
            walk.Step(0f, 0f, Frame, firm: true);
            t += Frame;
        }

        Assert.InRange(t, WalkInertia.GroundStopSeconds - Frame, WalkInertia.GroundStopSeconds + (2f * Frame));
        Assert.Equal(0f, walk.Y); // stands exactly, no creeping remainder
    }

    [Fact]
    public void InTheAir_ALetGoInputFadesSlowly_SoAJumpCarriesOn()
    {
        var walk = new WalkInertia();
        for (int i = 0; i < 30; i++)
        {
            walk.Step(0f, 1f, Frame, firm: true);
        }

        for (float t = 0f; t < 0.1f; t += Frame)
        {
            walk.Step(0f, 0f, Frame, firm: false);
        }

        Assert.True(walk.Y > 0.5f, $"a tenth of a second into the air the body should still carry most of its speed, was {walk.Y}");
    }

    [Fact]
    public void TurningAround_IsAStop_NotAStart()
    {
        var walk = new WalkInertia();
        for (int i = 0; i < 30; i++)
        {
            walk.Step(1f, 0f, Frame, firm: true);
        }

        walk.Step(-1f, 0f, Frame, firm: true);
        float afterOneFrame = walk.X;

        Assert.Equal(1f - (Frame / WalkInertia.GroundStopSeconds), afterOneFrame, 4);
    }

    [Fact]
    public void TheInput_NeverLeavesItsRange_AndResetStands()
    {
        var walk = new WalkInertia();
        for (int i = 0; i < 60; i++)
        {
            walk.Step(5f, -5f, Frame, firm: true);
        }

        Assert.Equal(1f, walk.X);
        Assert.Equal(-1f, walk.Y);

        walk.Reset();
        Assert.Equal(0f, walk.X);
        Assert.Equal(0f, walk.Y);
    }

    // ---------------- StepSmoothing ----------------

    [Fact]
    public void AStairTread_IsAStep_ALiftIsNot()
    {
        Assert.True(StepSmoothing.IsStep(0.5f, Frame));      // half a block within one frame
        Assert.True(StepSmoothing.IsStep(0.5f, 1f / 20f));   // … even at twenty frames a second
        Assert.False(StepSmoothing.IsStep(0.05f, Frame));    // a lift at 3 m/s
        Assert.False(StepSmoothing.IsStep(0.15f, 1f / 20f)); // the same lift on a slow machine
        Assert.False(StepSmoothing.IsStep(0f, Frame));
        Assert.False(StepSmoothing.IsStep(-0.5f, Frame));    // stepping down is a small fall, not a step
    }

    [Fact]
    public void TheEye_StaysBehindByTheStep_AndCatchesUp()
    {
        var step = new StepSmoothing();
        step.Absorb(0.5f);
        Assert.Equal(-0.5f, step.Offset, 4);

        float t = 0f;
        while (step.Offset < 0f && t < 2f)
        {
            step.Tick(Frame);
            t += Frame;
        }

        Assert.Equal(0f, step.Offset);
        Assert.InRange(t, 0.1f, 0.6f);
    }

    [Fact]
    public void AFlightOfStairs_NeverDropsTheEyeFurtherThanTheCap()
    {
        var step = new StepSmoothing();
        for (int i = 0; i < 6; i++)
        {
            step.Absorb(0.5f);
        }

        Assert.Equal(-StepSmoothing.MaximumLag, step.Offset, 4);

        step.Reset();
        Assert.Equal(0f, step.Offset);
    }

    // ---------------- FlightFeel ----------------

    [Fact]
    public void TheShip_SpoolsUpFasterThanItCoastsOut()
    {
        float up = 0f, frames = 0f;
        while (up < 1f)
        {
            up += FlightFeel.SpeedStep(Frame, thrusting: true);
            frames++;
        }

        Assert.InRange(frames * Frame, FlightFeel.SpoolSeconds - Frame, FlightFeel.SpoolSeconds + (2f * Frame));
        Assert.True(FlightFeel.SpeedStep(Frame, thrusting: false) < FlightFeel.SpeedStep(Frame, thrusting: true));
        Assert.Equal(0f, FlightFeel.SpeedStep(0f, thrusting: true));
    }

    [Fact]
    public void TheHull_LeansIntoTheTurn_UpToTheCap()
    {
        Assert.Equal(0f, FlightFeel.BankTarget(0f));
        Assert.True(FlightFeel.BankTarget(40f) > 0f);
        Assert.True(FlightFeel.BankTarget(-40f) < 0f);
        Assert.Equal(FlightFeel.MaxBankDegrees, FlightFeel.BankTarget(10_000f));
        Assert.Equal(-FlightFeel.MaxBankDegrees, FlightFeel.BankTarget(-10_000f));
    }

    [Fact]
    public void Following_CoversAShareOfTheWay_IndependentOfTheFrameRate()
    {
        float one = FlightFeel.Follow(FlightFeel.CameraFollowRate, 1f / 30f);
        float half = FlightFeel.Follow(FlightFeel.CameraFollowRate, 1f / 60f);
        float twoHalves = 1f - ((1f - half) * (1f - half));

        Assert.Equal(one, twoHalves, 4);
        Assert.Equal(0f, FlightFeel.Follow(FlightFeel.CameraFollowRate, 0f));
        Assert.InRange(one, 0f, 1f);
    }
}

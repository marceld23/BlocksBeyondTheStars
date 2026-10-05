// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The energy rope (#2320): shot at a block face it sticks; holding the pull reels the body to a target in front of
/// the anchor; the arrival depends on the face (land on a top, pull up over a free ledge, hang anywhere else); the
/// winch holds when the button goes up; a slack rope catches a fall but not a jump; the rope snaps when the anchor
/// is too far, out of sight, or the body cannot move; letting go never leaves a fall that hurts.
/// </summary>
public sealed class RopeRigTests
{
    private static readonly Vector3f Up = new(0f, 1f, 0f);
    private static readonly Vector3f West = new(-1f, 0f, 0f); // the outward normal of a wall's west face

    private static bool Air(int x, int y, int z) => false;

    /// <summary>A wall whose top is at y = 10: everything at or below is solid.</summary>
    private static bool WallTop10(int x, int y, int z) => y <= 10;

    private static RopeStep Pull(RopeRig rig, ref Vector3f feet, float seconds, float dt = 0.02f, bool sight = true)
    {
        var last = RopeStep.NotOwned;
        int steps = (int)Math.Round(seconds / dt);
        for (int i = 0; i < steps; i++)
        {
            last = rig.Step(feet, dt, pullHeld: true, grounded: false, fallDrop: 0f, sightClear: sight, steer: Vector3f.Zero);
            if (last.Owns)
            {
                feet = new Vector3f(feet.X + (last.Velocity.X * dt), feet.Y + (last.Velocity.Y * dt), feet.Z + (last.Velocity.Z * dt));
            }

            if (last.Event != RopeEvent.None)
            {
                return last;
            }
        }

        return last;
    }

    [Fact]
    public void AFaceIsReadFromItsNormal()
    {
        Assert.Equal(RopeFace.Top, RopeRig.FaceOf(Up));
        Assert.Equal(RopeFace.Bottom, RopeRig.FaceOf(new Vector3f(0f, -1f, 0f)));
        Assert.Equal(RopeFace.Side, RopeRig.FaceOf(West));
        Assert.Equal(RopeFace.Side, RopeRig.FaceOf(new Vector3f(0f, 0f, 1f)));
    }

    [Fact]
    public void AShotAtATopFace_PullsTheFeetOntoTheBlock_AndLands()
    {
        var rig = new RopeRig();
        var anchor = new Vector3f(10.2f, 21f, 5.5f); // the top face of the block (10, 20, 5), near its west edge
        rig.Attach(anchor, Up, new Vector3i(10, 20, 5), 24f, Air);

        Assert.Equal(RopeState.Attached, rig.State);
        Assert.Equal(RopeArrival.Land, rig.Arrival);
        Assert.Equal(21.02f, rig.Target.Y, 2);
        Assert.True(rig.Target.X > anchor.X && rig.Target.X < 10.5f, "the feet are moved in from the edge toward the block's centre");

        var feet = new Vector3f(2f, 12f, 5.5f);
        var step = Pull(rig, ref feet, 3f);

        Assert.Equal(RopeEvent.Landed, step.Event);
        Assert.Equal(RopeState.Idle, rig.State);
        Assert.True(Math.Abs(feet.X - rig.Target.X) < 0.5f && Math.Abs(feet.Y - rig.Target.Y) < 0.5f, $"arrived at the target, feet {feet}");
    }

    [Fact]
    public void AShotAtAWallWithAFreeTop_WindsTheHandsToTheEdge_ThenPullsUp()
    {
        var rig = new RopeRig();
        var anchor = new Vector3f(10f, 8.3f, 5.5f); // low on the west face of the wall block (10, 8, 5); the wall's top is y 11
        rig.Attach(anchor, West, new Vector3i(10, 8, 5), 24f, (x, y, z) => y <= 8);

        Assert.Equal(RopeArrival.PullUp, rig.Arrival);
        Assert.Equal(9f - RopeRules.LedgeGrip - RopeRules.HandHeight, rig.Target.Y, 3); // hands just under the top edge
        Assert.Equal(10f - RopeRules.StandOff, rig.Target.X, 3);                         // the body off the wall
        Assert.Equal(new Vector3f(10.5f, 9f, 5.5f), rig.PullUpTarget);
        Assert.True(rig.PullUpTarget.Y - rig.Target.Y <= RopeRules.HandHeight + 0.5f, "the pull-up rise stays within the climber's reach");

        var feet = new Vector3f(2f, 3f, 5.5f);
        var step = Pull(rig, ref feet, 4f);

        Assert.Equal(RopeEvent.PullUp, step.Event);
        Assert.Equal(RopeState.Idle, rig.State);
    }

    [Fact]
    public void AShotMidWall_OrUnderACeiling_EndsHanging()
    {
        var rig = new RopeRig();
        rig.Attach(new Vector3f(10f, 8.5f, 5.5f), West, new Vector3i(10, 8, 5), 24f, WallTop10);
        Assert.Equal(RopeArrival.Hang, rig.Arrival);
        Assert.Equal(8.5f - RopeRules.HandHeight, rig.Target.Y, 3); // the hands stay at the hit

        var feet = new Vector3f(4f, 7f, 5.5f);
        var step = Pull(rig, ref feet, 3f);
        Assert.Equal(RopeEvent.Hang, step.Event);
        Assert.Equal(RopeState.Hanging, rig.State);

        // Hanging holds: no velocity, the rig owns the body.
        var hold = rig.Step(feet, 0.02f, pullHeld: false, grounded: false, fallDrop: 0f, sightClear: true, steer: Vector3f.Zero);
        Assert.True(hold.Owns);
        Assert.Equal(Vector3f.Zero, hold.Velocity);

        var ceiling = new RopeRig();
        ceiling.Attach(new Vector3f(10.5f, 20f, 5.5f), new Vector3f(0f, -1f, 0f), new Vector3i(10, 20, 5), 24f, Air);
        Assert.Equal(RopeArrival.Hang, ceiling.Arrival);
        Assert.Equal(20f - RopeRules.HeadRoom, ceiling.Target.Y, 3);
    }

    [Fact]
    public void ThePullWindsUp_ThenRunsAtTheReelSpeed_AndSteersALittle()
    {
        var rig = new RopeRig();
        rig.Attach(new Vector3f(30.5f, 1f, 0.5f), Up, new Vector3i(30, 0, 0), 40f, Air); // the top face's centre: a pull straight along x
        var feet = new Vector3f(0f, 0f, 0.5f);

        var first = rig.Step(feet, 0.02f, pullHeld: true, grounded: true, fallDrop: 0f, sightClear: true, steer: Vector3f.Zero);
        Assert.True(first.Owns);
        float v0 = Len(first.Velocity);
        Assert.True(v0 < RopeRules.PullSpeed * 0.2f, $"the reel winds up instead of yanking: {v0}");

        var cruise = Pull(rig, ref feet, 0.5f);
        Assert.Equal(RopeEvent.None, cruise.Event);
        Assert.Equal(RopeRules.PullSpeed, Len(cruise.Velocity), 1);
        Assert.True(cruise.Velocity.X > 9f, "straight at the anchor");

        var steered = rig.Step(feet, 0.02f, pullHeld: true, grounded: false, fallDrop: 0f, sightClear: true, steer: new Vector3f(0f, 0f, 6f));
        Assert.Equal(6f * RopeRules.SteerShare, steered.Velocity.Z, 2);
    }

    [Fact]
    public void LettingGoOfThePull_MakesTheWinchHold_AndAFloorMakesTheRopeSlack()
    {
        var rig = new RopeRig();
        rig.Attach(new Vector3f(30f, 1f, 0f), Up, new Vector3i(30, 0, 0), 40f, Air);
        var feet = new Vector3f(0f, 0f, 0f);
        Pull(rig, ref feet, 0.5f);
        Assert.Equal(RopeState.Pulling, rig.State);

        var held = rig.Step(feet, 0.02f, pullHeld: false, grounded: false, fallDrop: 0f, sightClear: true, steer: Vector3f.Zero);
        Assert.Equal(RopeState.Hanging, rig.State);
        Assert.True(held.Owns);
        Assert.Equal(Vector3f.Zero, held.Velocity);

        var floor = rig.Step(feet, 0.02f, pullHeld: false, grounded: true, fallDrop: 0f, sightClear: true, steer: Vector3f.Zero);
        Assert.Equal(RopeState.Attached, rig.State);
        Assert.False(floor.Owns, "with a floor under the feet the player walks; the rope hangs slack");
    }

    [Fact]
    public void ASlackRope_LetsYouJump_ButCatchesARealFall()
    {
        var rig = new RopeRig();
        rig.Attach(new Vector3f(10f, 20f, 0f), Up, new Vector3i(10, 19, 0), 24f, Air);
        var feet = new Vector3f(10f, 10f, 0f);

        var jump = rig.Step(feet, 0.02f, pullHeld: false, grounded: false, fallDrop: 0.8f, sightClear: true, steer: Vector3f.Zero);
        Assert.False(jump.Owns, "a hop stays a hop");
        Assert.Equal(RopeState.Attached, rig.State);

        var fall = rig.Step(feet, 0.02f, pullHeld: false, grounded: false, fallDrop: RopeRules.CatchDrop + 0.1f, sightClear: true, steer: Vector3f.Zero);
        Assert.True(fall.Owns);
        Assert.Equal(RopeEvent.Hang, fall.Event);
        Assert.Equal(RopeState.Hanging, rig.State);
    }

    [Fact]
    public void TheRopeSnaps_WhenTooFar_OrOutOfSightForLong_OrStuck()
    {
        var rig = new RopeRig();
        rig.Attach(new Vector3f(10f, 1f, 0f), Up, new Vector3i(10, 0, 0), 24f, Air);
        var far = rig.Step(new Vector3f(10f + 24f + RopeRules.SnapSlack + 0.5f, 0f, 0f), 0.02f, false, true, 0f, true, Vector3f.Zero);
        Assert.Equal(RopeEvent.Snapped, far.Event);
        Assert.Equal(RopeState.Idle, rig.State);

        rig.Attach(new Vector3f(10f, 1f, 0f), Up, new Vector3i(10, 0, 0), 24f, Air);
        var feet = new Vector3f(0f, 0f, 0f);
        var blink = rig.Step(feet, 0.1f, true, false, 0f, sightClear: false, Vector3f.Zero);
        Assert.Equal(RopeEvent.None, blink.Event); // a short occlusion is forgiven
        var cut = Pull(rig, ref feet, 1f, 0.1f, sight: false);
        Assert.Equal(RopeEvent.Snapped, cut.Event);

        // Stuck: the body does not move although the reel runs → the winch holds (a hang), the rope stays.
        var stuck = new RopeRig();
        stuck.Attach(new Vector3f(10f, 1f, 0f), Up, new Vector3i(10, 0, 0), 24f, Air);
        var pinned = new Vector3f(0f, 0f, 0f);
        var last = RopeStep.NotOwned;
        for (int i = 0; i < 40 && last.Event == RopeEvent.None; i++)
        {
            last = stuck.Step(pinned, 0.05f, true, false, 0f, true, Vector3f.Zero); // the feet never advance
        }

        Assert.Equal(RopeEvent.Hang, last.Event);
        Assert.Equal(RopeState.Hanging, stuck.State);
    }

    [Fact]
    public void LettingGo_NeverLeavesAFallThatHurts()
    {
        Assert.Equal(-7f, RopeRig.CappedFall(-30f, 14f));
        Assert.Equal(-3f, RopeRig.CappedFall(-3f, 14f));
        Assert.Equal(4f, RopeRig.CappedFall(4f, 14f));
        Assert.Equal(0f, RopeRig.CappedFall(-5f, 0f));
    }

    [Fact]
    public void TheDrawnRope_KeepsItsEnds_AndDroopsOnlyWhenSlack()
    {
        var from = new Vector3f(0f, 2f, 0f);
        var to = new Vector3f(10f, 6f, 0f);
        var taut = new Vector3f[RopeLine.PointCount];
        var slack = new Vector3f[RopeLine.PointCount];
        RopeLine.Points(from, to, slack: false, phase: 1.3f, taut);
        RopeLine.Points(from, to, slack: true, phase: 1.3f, slack);

        Assert.Equal(from, taut[0]);
        Assert.Equal(to, taut[RopeLine.PointCount - 1]);
        Assert.Equal(from, slack[0]);
        Assert.Equal(to, slack[RopeLine.PointCount - 1]);

        int mid = RopeLine.PointCount / 2;
        float chord = (from.Y + to.Y) * 0.5f;
        Assert.True(Math.Abs(taut[mid].Y - chord) < 0.05f, "a loaded rope is straight (a wobble at most)");
        Assert.True(slack[mid].Y < chord - 0.3f, "a slack rope droops in the middle");

        var distinct = new HashSet<float>();
        foreach (var p in taut)
        {
            distinct.Add(p.X);
        }

        Assert.Equal(RopeLine.PointCount, distinct.Count);
        Assert.Throws<ArgumentException>(() => RopeLine.Points(from, to, false, 0f, new Vector3f[2]));
    }

    private static float Len(Vector3f v) => (float)Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
}

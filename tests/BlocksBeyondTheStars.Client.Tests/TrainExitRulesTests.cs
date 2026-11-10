// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3458/#3459: leaving a train — a walk-off keeps its own spot near the server's point (no "Stabilisiere Position"),
/// pressing into the door of a halted train leaves after a moment, and jumping off a moving one keeps half its speed
/// (capped) plus a hop.
/// </summary>
public sealed class TrainExitRulesTests
{
    private const int Circ = 2048;

    [Fact]
    public void AWalkOff_KeepsItsOwnSpot_NearTheServersPoint_ButSnapsWhenFarOff()
    {
        var left = new Vector3f(100.4f, 70.7f, 50.2f);
        Assert.False(TrainExitRules.SnapToServer(true, left, new Vector3f(101.2f, 70.7f, 50.6f), Circ));
        Assert.True(TrainExitRules.SnapToServer(true, left, new Vector3f(103.0f, 70.7f, 50.2f), Circ));
        Assert.True(TrainExitRules.SnapToServer(true, left, new Vector3f(100.4f, 72.5f, 50.2f), Circ));
    }

    [Fact]
    public void AJumpOffAMovingTrain_ToleratesTheWagonRunningOnWhileTheReportTravels()
    {
        var left = new Vector3f(100f, 70.7f, 50f);
        var trailing = new Vector3f(105f, 70.7f, 50f); // the server's wagon ran on 5 blocks before the report arrived
        Assert.True(TrainExitRules.SnapToServer(true, left, trailing, Circ));
        Assert.False(TrainExitRules.SnapToServer(true, left, trailing, Circ, TrainExitRules.WalkOffTolerance(24f)));
        Assert.Equal(TrainExitRules.KeepOwnSpotTolerance, TrainExitRules.WalkOffTolerance(0f));
        Assert.Equal(TrainExitRules.KeepOwnSpotTolerance, TrainExitRules.WalkOffTolerance(float.NaN));
    }

    [Fact]
    public void AnFSetDown_AlwaysMovesTheBody()
        => Assert.True(TrainExitRules.SnapToServer(false, new Vector3f(10f, 70f, 10f), new Vector3f(10f, 70f, 10f), Circ));

    [Fact]
    public void TheKeepRule_ReadsAcrossTheLongitudeSeam()
    {
        var left = new Vector3f(Circ - 0.3f, 70f, 5f);
        Assert.False(TrainExitRules.SnapToServer(true, left, new Vector3f(0.4f, 70f, 5f), Circ));
    }

    [Fact]
    public void AJumpOff_KeepsHalfTheSpeed_CappedAt12_WithAHop()
    {
        var slow = TrainExitRules.JumpPush(new Vector3f(8f, 0f, 0f));
        Assert.Equal(4f, slow.X, 3);
        Assert.Equal(0f, slow.Z, 3);
        Assert.Equal(TrainExitRules.JumpHop, slow.Y, 3);

        var fast = TrainExitRules.JumpPush(new Vector3f(0f, 0f, -30f));
        Assert.Equal(-TrainExitRules.JumpCap, fast.Z, 3);
        Assert.Equal(0f, fast.X, 3);

        var diagonal = TrainExitRules.JumpPush(new Vector3f(12f, 3f, 16f)); // 20 b/s horizontally → 10, the climb ignored
        Assert.Equal(6f, diagonal.X, 3);
        Assert.Equal(8f, diagonal.Z, 3);
        Assert.Equal(TrainExitRules.JumpHop, diagonal.Y, 3);
    }

    [Fact]
    public void AStandingWagon_GivesNoPush()
    {
        var none = TrainExitRules.JumpPush(new Vector3f(0.5f, 0f, 0.3f));
        Assert.Equal(0f, none.X);
        Assert.Equal(0f, none.Y);
        Assert.Equal(0f, none.Z);
        Assert.Equal(0f, TrainExitRules.JumpPush(new Vector3f(float.NaN, 0f, 0f)).Y);
    }

    [Fact]
    public void PressingIntoTheDoor_NeedsTheDoorSpan_TheWall_AndAnOutwardPush()
    {
        float wall = RailRules.RiderHalfWidth;
        Assert.True(TrainExitRules.PressingIntoDoor(new Vector3f(wall, 0f, 0.3f), 1f, 0f));
        Assert.True(TrainExitRules.PressingIntoDoor(new Vector3f(-wall, 0f, -0.9f), -0.8f, 0.4f));
        Assert.False(TrainExitRules.PressingIntoDoor(new Vector3f(wall, 0f, 2f), 1f, 0f), "beside the door: a window");
        Assert.False(TrainExitRules.PressingIntoDoor(new Vector3f(0.2f, 0f, 0f), 1f, 0f), "in the middle of the wagon");
        Assert.False(TrainExitRules.PressingIntoDoor(new Vector3f(wall, 0f, 0f), -1f, 0f), "walking inward");
        Assert.False(TrainExitRules.PressingIntoDoor(new Vector3f(wall, 0f, 0f), 0.4f, 0.9f), "walking along the wagon");
        Assert.False(TrainExitRules.PressingIntoDoor(new Vector3f(wall, 0f, 0f), 0.1f, 0f), "barely touching the stick");
    }

    [Fact]
    public void OnlyABodyStuckInTheDoor_CountsAsPushing_NotOneWalkingOut()
    {
        const float dt = 1f / 60f;
        Assert.True(TrainExitRules.Stalled(1.10f, 1.10f, dt));          // against the platform's face
        Assert.True(TrainExitRules.Stalled(1.10f, 1.105f, dt));         // creeping along a fraction
        Assert.False(TrainExitRules.Stalled(1.00f, 1.00f + (4.3f * dt), dt)); // walking out at 4.3 b/s
        Assert.True(TrainExitRules.Stalled(1f, 2f, 0f));                // no time passed: no evidence of walking
    }

    [Fact]
    public void TheDoorPush_LeavesAfterAMoment_OnlyWhileHalted()
    {
        float held = 0f;
        held = TrainExitRules.StepDoorPush(held, true, true, 0.1f);
        Assert.False(TrainExitRules.DoorPushDone(held));
        held = TrainExitRules.StepDoorPush(held, true, true, 0.11f);
        Assert.True(TrainExitRules.DoorPushDone(held));
        Assert.Equal(0f, TrainExitRules.StepDoorPush(held, true, false, 0.1f)); // the train set off: start over
        Assert.Equal(0f, TrainExitRules.StepDoorPush(held, false, true, 0.1f)); // let go: start over
    }
}

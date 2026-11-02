// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3095: the equip flourishes (a drill's test spin, a gun's cosmetic reload, a blade's twirl), the drill bit that
/// spins while mining, and the robots' idle fidgets. These pin that every flourish is short and starts and ends at rest,
/// that it can always be cut short, that only the blade repeats while idle, which boxes of each drill in the data are its
/// bit, and that a robot only fidgets once it has stood still.</summary>
public sealed class HeldFlourishTests
{
    private const float Frame = 1f / 60f;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
    private static readonly HeldItemShapes.Rgb Tint = new HeldItemShapes.Rgb(0.6f, 0.6f, 0.6f);

    private static readonly HeldFlourishKind[] Flourishes =
    {
        HeldFlourishKind.DrillSpinUp, HeldFlourishKind.GunReload, HeldFlourishKind.BladeTwirl,
    };

    [Theory]
    [InlineData("Drill", HeldFlourishKind.DrillSpinUp)]
    [InlineData("Gun", HeldFlourishKind.GunReload)]
    [InlineData("Blade", HeldFlourishKind.BladeTwirl)]
    [InlineData("Block", HeldFlourishKind.None)]
    [InlineData("Hand", HeldFlourishKind.None)]
    [InlineData("Gloves", HeldFlourishKind.None)]
    [InlineData("Scanner", HeldFlourishKind.None)]
    [InlineData(null, HeldFlourishKind.None)]
    public void EachKind_HasItsFlourish(string? kind, HeldFlourishKind expected)
        => Assert.Equal(expected, HeldFlourishes.ForKind(kind));

    [Fact]
    public void EveryFlourish_IsShort_HasASound_AndStartsAndEndsAtRest()
    {
        foreach (var kind in Flourishes)
        {
            float seconds = HeldFlourishes.Seconds(kind);
            Assert.InRange(seconds, 0.3f, HeldFlourishes.MaxSeconds);
            Assert.False(string.IsNullOrEmpty(HeldFlourishes.Cue(kind)));
            Assert.True(HeldFlourishes.PoseAt(kind, 0f).IsRest, $"{kind} at its start");
            Assert.True(HeldFlourishes.PoseAt(kind, 1f).IsRest, $"{kind} at its end");
            Assert.False(HeldFlourishes.PoseAt(kind, 0.5f).IsRest, $"{kind} moves in the middle");
        }

        Assert.Equal(0f, HeldFlourishes.Seconds(HeldFlourishKind.None));
        Assert.Null(HeldFlourishes.Cue(HeldFlourishKind.None));
    }

    [Fact]
    public void EveryFlourish_MovesSmoothly_NoJumpBetweenFrames()
    {
        foreach (var kind in Flourishes)
        {
            float seconds = HeldFlourishes.Seconds(kind);
            var last = HeldFlourishes.PoseAt(kind, 0f);
            for (float s = Frame; s <= seconds; s += Frame)
            {
                var pose = HeldFlourishes.PoseAt(kind, s / seconds);
                Assert.True((pose.Offset - last.Offset).Length() < 0.02f, $"{kind} offset jumps at {s:0.00} s");
                Assert.True((pose.Euler - last.Euler).Length() < 12f, $"{kind} turn jumps at {s:0.00} s");
                float twirl = Math.Abs(pose.Twirl - last.Twirl);
                Assert.True(Math.Min(twirl, 360f - twirl) < 40f, $"{kind} twirl jumps at {s:0.00} s");
                last = pose;
            }
        }
    }

    [Fact]
    public void TheDrill_RunsItsBit_TheBlade_TurnsOnceRound_TheGun_DoesNeither()
    {
        var drill = Sample(HeldFlourishKind.DrillSpinUp);
        Assert.Contains(drill, p => p.BitDriven);
        Assert.Contains(drill, p => p.Buzz > 0.5f);
        Assert.All(drill, p => Assert.Equal(0f, p.Twirl));

        var blade = Sample(HeldFlourishKind.BladeTwirl);
        Assert.InRange(blade.Max(p => p.Twirl), 340f, 360f);
        Assert.All(blade, p => Assert.False(p.BitDriven));

        var gun = Sample(HeldFlourishKind.GunReload);
        Assert.All(gun, p => Assert.False(p.BitDriven));
        Assert.All(gun, p => Assert.Equal(0f, p.Twirl));
        Assert.Contains(gun, p => p.Offset.Z < -0.04f); // the rack pulls the gun back
        Assert.Contains(gun, p => p.Euler.Z > 30f);     // tipped onto its side
    }

    [Fact]
    public void AFlourish_PlaysOnce_AndUsingTheItem_CutsItShort()
    {
        var flourish = new HeldFlourish();
        Assert.False(flourish.Active);
        Assert.True(flourish.Pose.IsRest);
        Assert.True(flourish.Begin(HeldFlourishKind.GunReload));
        Run(flourish, HeldFlourishes.GunSeconds * 0.5f);
        Assert.True(flourish.Active);
        Assert.False(flourish.Pose.IsRest);

        flourish.Cancel(); // the player fires
        Assert.False(flourish.Active);
        Assert.True(flourish.Pose.IsRest);

        flourish.Begin(HeldFlourishKind.DrillSpinUp);
        Run(flourish, HeldFlourishes.DrillSeconds + Frame);
        Assert.False(flourish.Active);
        Assert.True(flourish.Pose.IsRest);

        Assert.False(flourish.Begin(HeldFlourishKind.None));
        Assert.False(flourish.Active);
    }

    [Fact]
    public void OnlyTheBlade_IsTwirledAgain_AfterARandomPause_WhileTheHandIsIdle()
    {
        Assert.True(HeldFlourishes.IdleRepeats(HeldFlourishKind.BladeTwirl));
        Assert.False(HeldFlourishes.IdleRepeats(HeldFlourishKind.GunReload));
        Assert.False(HeldFlourishes.IdleRepeats(HeldFlourishKind.DrillSpinUp));
        Assert.Equal(HeldFlourishes.IdleMinSeconds, HeldFlourishes.IdleGap(0.0));
        Assert.Equal(HeldFlourishes.IdleMaxSeconds, HeldFlourishes.IdleGap(1.0));
        Assert.Equal(HeldFlourishes.IdleMaxSeconds, HeldFlourishes.IdleGap(5.0));

        var flourish = new HeldFlourish();
        Assert.Equal(-1f, IdleWait(flourish, HeldFlourishKind.GunReload, 0.0, 40f));
        float wait = IdleWait(flourish, HeldFlourishKind.BladeTwirl, 0.5, 40f);
        Assert.InRange(wait, HeldFlourishes.IdleGap(0.5) - (2 * Frame), HeldFlourishes.IdleGap(0.5) + (2 * Frame));

        // Moving (not idle) for a moment restarts the wait.
        var again = new HeldFlourish();
        IdleWait(again, HeldFlourishKind.BladeTwirl, 0.0, HeldFlourishes.IdleMinSeconds - 1f);
        Assert.False(again.IdleDue(Frame, idle: false, HeldFlourishKind.BladeTwirl, 0.0));
        Assert.InRange(IdleWait(again, HeldFlourishKind.BladeTwirl, 0.0, 40f), HeldFlourishes.IdleMinSeconds - (2 * Frame), 40f);
    }

    /// <summary>The drills of the data and the kind's own model each have a bit that spins; the wooden pickaxe (a drill by its
    /// tool kind) has none.</summary>
    [Fact]
    public void EveryDrillInTheData_HasABitOnItsAxis_ThePickaxeNone()
    {
        var basic = HeldItemShapes.Parts("Drill", Tint)!;
        Assert.Equal(new[] { 1 }, HeldDrillBit.SpinParts(basic)); // the default bit, never the body or the grip

        var drills = Content.Items.Values.Where(i => i.Tool?.Kind == ToolKind.Drill && i.HeldModel is { Count: > 0 }).ToList();
        Assert.NotEmpty(drills);
        foreach (var item in drills)
        {
            var parts = HeldItemShapes.Parts("Drill", Tint, item.HeldModel)!;
            var bit = HeldDrillBit.SpinParts(parts);
            if (item.Key == "wood_pickaxe")
            {
                Assert.Empty(bit);
                continue;
            }

            Assert.NotEmpty(bit);
            float front = parts.Max(p => p.Position.Z + (p.Size.Z * 0.5f));
            Assert.Contains(bit, i => Math.Abs(parts[i].Position.Z + (parts[i].Size.Z * 0.5f) - front) < 1e-4f);
            foreach (int i in bit)
            {
                Assert.InRange(Math.Abs(parts[i].Position.X), 0f, 0.012f);
                Assert.InRange(Math.Abs(parts[i].Position.Y), 0f, 0.012f);
                Assert.True(parts[i].Position.Z > 0.1f, $"{item.Key}: box {i} of the bit sits by the hand");
            }

            Assert.True(bit.Count < parts.Count, $"{item.Key}: the whole drill would spin");
        }
    }

    [Fact]
    public void AModelWithoutAnAxialFront_HasNoBit()
    {
        Assert.Empty(HeldDrillBit.SpinParts(Array.Empty<HeldItemShapes.Part>()));
        Assert.Empty(HeldDrillBit.SpinParts(HeldItemShapes.Parts("Blade", Tint)!)); // the blade's front sits above the axis
        var flat = new List<HeldItemShapes.Part>
        {
            new HeldItemShapes.Part(new System.Numerics.Vector3(0f, 0f, 0.1f), new System.Numerics.Vector3(0.3f, 0.04f, 0.3f), Tint),
        };
        Assert.Empty(HeldDrillBit.SpinParts(flat)); // a flat paddle is not round
    }

    [Fact]
    public void TheBit_SpinsUpQuickly_AndCoastsDown()
    {
        float speed = 0f;
        for (float t = 0f; t < HeldDrillBit.SpinUpSeconds + Frame; t += Frame)
        {
            speed = HeldDrillBit.NextSpeed(speed, driven: true, Frame);
        }

        Assert.Equal(1f, speed);
        speed = HeldDrillBit.NextSpeed(speed, driven: false, HeldDrillBit.CoastSeconds * 0.5f);
        Assert.InRange(speed, 0.45f, 0.55f);
        speed = HeldDrillBit.NextSpeed(speed, driven: false, HeldDrillBit.CoastSeconds);
        Assert.Equal(0f, speed);
        Assert.Equal(0.3f, HeldDrillBit.NextSpeed(0.3f, driven: true, 0f));
    }

    [Fact]
    public void ARobot_FidgetsOnlyAfterStandingStill_AndFadesOutWhenItMoves()
    {
        var idle = new RobotIdle(seed: 42);
        for (float t = 0f; t < RobotIdle.SettleSeconds - 0.1f; t += Frame)
        {
            Assert.True(idle.Step(Frame, idle: true).IsRest);
            Assert.Equal(RobotFlourishKind.None, idle.Current);
        }

        // Walking never fidgets, however long.
        var walking = new RobotIdle(seed: 7);
        for (float t = 0f; t < 60f; t += Frame)
        {
            Assert.True(walking.Step(Frame, idle: false).IsRest);
        }

        // Standing still, it fidgets within settle + the longest gap, and keeps doing so now and then.
        var kinds = new HashSet<RobotFlourishKind>();
        var robot = new RobotIdle(seed: 3);
        bool moved = false;
        for (float t = 0f; t < 240f; t += Frame)
        {
            moved |= !robot.Step(Frame, idle: true).IsRest;
            if (robot.Current != RobotFlourishKind.None)
            {
                kinds.Add(robot.Current);
            }
        }

        Assert.True(moved);
        Assert.Equal(3, kinds.Count);

        // A flourish caught by the robot moving off fades out quickly instead of snapping.
        var caught = new RobotIdle(seed: 5);
        caught.Begin(RobotFlourishKind.LookAtHands);
        var mid = Step(caught, RobotIdle.Seconds(RobotFlourishKind.LookAtHands) * 0.5f, idle: true);
        var fading = caught.Step(Frame, idle: false);
        Assert.True(Math.Abs(fading.ArmPitchL) < Math.Abs(mid.ArmPitchL) + 1f);
        Step(caught, RobotIdle.FadeSeconds + Frame, idle: false);
        Assert.Equal(RobotFlourishKind.None, caught.Current);
    }

    [Fact]
    public void EveryRobotFlourish_StartsAndEndsAtRest_AndLooksAtTheHandsWithBothArmsUp()
    {
        foreach (var kind in new[] { RobotFlourishKind.LookAtHands, RobotFlourishKind.ShoulderRoll, RobotFlourishKind.ScanAround })
        {
            Assert.True(RobotIdle.PoseAt(kind, 0f).IsRest);
            Assert.True(RobotIdle.PoseAt(kind, 1f).IsRest);
            Assert.False(RobotIdle.PoseAt(kind, 0.4f).IsRest);
            Assert.InRange(RobotIdle.Seconds(kind), 1f, 2.5f);
        }

        var look = RobotIdle.PoseAt(RobotFlourishKind.LookAtHands, 0.5f);
        Assert.True(look.ArmPitchL < -50f && look.ArmPitchR < -50f, "both claws come up in front");
        Assert.True(look.HeadPitch > 20f, "the head bends down to them");
        Assert.True(Math.Abs(RobotIdle.PoseAt(RobotFlourishKind.ScanAround, 0.25f).HeadYaw) > 40f, "the scan looks far round");
    }

    private static List<HeldFlourishPose> Sample(HeldFlourishKind kind)
    {
        var poses = new List<HeldFlourishPose>();
        for (float t = 0f; t <= 1f; t += 0.01f)
        {
            poses.Add(HeldFlourishes.PoseAt(kind, t));
        }

        return poses;
    }

    private static void Run(HeldFlourish flourish, float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame)
        {
            flourish.Step(Frame);
        }
    }

    /// <summary>Seconds of idle until the flourish is due (−1 when it never is within <paramref name="limit"/>).</summary>
    private static float IdleWait(HeldFlourish flourish, HeldFlourishKind held, double unit, float limit)
    {
        for (float t = Frame; t <= limit; t += Frame)
        {
            if (flourish.IdleDue(Frame, idle: true, held, unit))
            {
                return t;
            }
        }

        return -1f;
    }

    private static RobotFlourishPose Step(RobotIdle robot, float seconds, bool idle)
    {
        var pose = default(RobotFlourishPose);
        for (float t = 0f; t < seconds; t += Frame)
        {
            pose = robot.Step(Frame, idle);
        }

        return pose;
    }
}

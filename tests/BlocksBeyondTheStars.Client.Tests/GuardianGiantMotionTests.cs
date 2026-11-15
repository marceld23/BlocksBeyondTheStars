// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3676: the guardian giant's motion set. The rise starts and the lie-down and the collapse end exactly in the lying pose
/// (the block body), every warning is on screen before its move's impact, the walk keeps the planted sole on the ground and
/// still, and no move at any time gives a broken number.
/// </summary>
public sealed class GuardianGiantMotionTests
{
    private static GuardianGiantFrame At(GuardianGiantMove move, float t) => GuardianGiantMotion.Evaluate(move, t, 0f, 0f);

    private static float Duration(GuardianGiantMove move) => GuardianGiantRules.Spec(move).Duration;

    [Fact]
    public void TheRise_StartsExactlyLying_AndEndsExactlyStanding()
    {
        AssertAtRest(At(GuardianGiantMove.Rise, 0f), rise: 0f);
        AssertAtRest(At(GuardianGiantMove.Rise, Duration(GuardianGiantMove.Rise)), rise: 1f);
        Assert.True(At(GuardianGiantMove.Rise, 1.2f).TorsoPitch > 40f, "it sits up on its arms before it pushes up");
    }

    [Fact]
    public void TheLieDownAndTheCollapse_EndExactlyLying()
    {
        AssertAtRest(At(GuardianGiantMove.LieDown, 0f), rise: 1f);
        AssertAtRest(At(GuardianGiantMove.LieDown, Duration(GuardianGiantMove.LieDown)), rise: 0f);
        Assert.Equal(1f, At(GuardianGiantMove.Collapse, 0f).Rise, 4);
        AssertAtRest(At(GuardianGiantMove.Collapse, Duration(GuardianGiantMove.Collapse)), rise: 0f);
        AssertAtRest(At(GuardianGiantMove.Collapse, Duration(GuardianGiantMove.Collapse) + 5f), rise: 0f);

        var kneeling = At(GuardianGiantMove.Collapse, 0.9f);
        Assert.True(kneeling.LegLPitch > 40f && kneeling.RootY < -1f, "it sinks to its knees first");
        Assert.True(At(GuardianGiantMove.Collapse, GuardianGiantRules.Spec(GuardianGiantMove.Collapse).Impact).Rise < 0.01f,
            "it lies on the ground when the impact lands");
    }

    [Fact]
    public void TheStomp_RaisesTheFootDuringTheWarning_AndSlamsItAtTheImpact()
    {
        var s = GuardianGiantRules.Spec(GuardianGiantMove.Stomp);
        Assert.True(At(GuardianGiantMove.Stomp, 0.75f * s.Impact).LegRPitch < -50f);
        Assert.True(Math.Abs(At(GuardianGiantMove.Stomp, s.Impact).LegRPitch) < 0.5f);
        Assert.Equal(0f, At(GuardianGiantMove.Stomp, 0.75f * s.Impact).LegLPitch, 3); // it stands on the other leg
    }

    [Fact]
    public void TheJumpAttack_CrouchesFirst_FliesBetweenTakeOffAndImpact()
    {
        var s = GuardianGiantRules.Spec(GuardianGiantMove.JumpAttack);
        float takeOff = GuardianGiantRules.JumpTakeOff;
        var crouch = At(GuardianGiantMove.JumpAttack, 0.8f * takeOff);
        Assert.Equal(-1f, crouch.Air);
        Assert.True(crouch.RootY < -0.5f, "the crouch is the warning");
        Assert.Equal(0f, At(GuardianGiantMove.JumpAttack, takeOff).Air, 4);
        Assert.Equal(0.5f, At(GuardianGiantMove.JumpAttack, (takeOff + s.Impact) * 0.5f).Air, 3);
        Assert.Equal(1f, At(GuardianGiantMove.JumpAttack, s.Impact).Air, 4);
        Assert.Equal(-1f, At(GuardianGiantMove.JumpAttack, s.Impact + 0.1f).Air);
    }

    [Fact]
    public void TheLaser_GlowsThroughItsWarning_AndBurnsForTheBeam()
    {
        var s = GuardianGiantRules.Spec(GuardianGiantMove.Laser);
        Assert.Equal(0f, At(GuardianGiantMove.Laser, 0f).Visor, 3);
        Assert.True(At(GuardianGiantMove.Laser, 0.5f * s.Warning).Visor is > 0.2f and < 0.9f);
        Assert.Equal(1f, At(GuardianGiantMove.Laser, s.Impact + (0.5f * GuardianGiantRules.LaserBeamSeconds)).Visor, 3);
        Assert.Equal(0f, At(GuardianGiantMove.Laser, s.Duration).Visor, 3);
        Assert.True(At(GuardianGiantMove.Laser, s.Impact).LookAtAim > 0.99f);
    }

    [Fact]
    public void TheThrow_WindsUpOverheadBeforeTheRelease_AndFollowsThrough()
    {
        float release = GuardianGiantRules.ThrowRelease;
        Assert.True(At(GuardianGiantMove.Throw, 0.25f).TorsoPitch > 20f, "it bends down to tear the rock out");
        Assert.True(At(GuardianGiantMove.Throw, release - 0.1f).ArmRPitch < -150f, "the arm is up behind the head");
        Assert.True(At(GuardianGiantMove.Throw, release + 0.15f).ArmRPitch > -120f, "and swings forward after the release");
        Assert.True(GuardianGiantMotion.RockGrab < release);
    }

    [Fact]
    public void TheSummonsTheHopAndTheSearch_ShowWhatTheyDo()
    {
        var summon = At(GuardianGiantMove.Summon, GuardianGiantRules.Spec(GuardianGiantMove.Summon).Impact);
        Assert.True(summon.ArmLPitch < -150f && summon.ArmRPitch < -150f && summon.Flare > 0.9f);
        var hop = GuardianGiantRules.Spec(GuardianGiantMove.Hop);
        Assert.True(At(GuardianGiantMove.Hop, (hop.Warning + hop.Impact) * 0.5f).RootY > 1.5f);
        Assert.True(At(GuardianGiantMove.Search, 2f).SearchLight > 0.99f);
        Assert.True(At(GuardianGiantMove.Enrage, 0.75f).Shudder > 0.9f);
    }

    [Fact]
    public void TheWalk_KeepsThePlantedSoleOnTheGround_AndStill()
    {
        float speed = GuardianGiantRules.WalkSpeed, dt = 0.01f, walk = 0.13f, body = 0f;
        float rate = GuardianGiantMotion.WalkCycleRate(speed);
        Assert.True(rate > 0.2f && rate < 1f, $"a heavy, slow stride ({rate} strides/s)");
        var prev = Sole(GuardianGiantMotion.Evaluate(GuardianGiantMove.Walk, 0f, walk, 1f), body);
        for (int i = 1; i < 800; i++)
        {
            walk += dt * rate;
            body += dt * speed;
            var f = GuardianGiantMotion.Evaluate(GuardianGiantMove.Walk, i * dt, walk, 1f);
            var now = Sole(f, body);
            float lowest = float.MaxValue;
            for (int leg = 0; leg < 2; leg++)
            {
                if (!now.Planted[leg])
                {
                    continue;
                }

                lowest = Math.Min(lowest, now.Height[leg]);
                Assert.True(now.Height[leg] > -1e-3f, $"step {i}: the planted sole sinks into the ground ({now.Height[leg]})");
                if (prev.Planted[leg])
                {
                    Assert.True(Math.Abs(now.Forward[leg] - prev.Forward[leg]) < 2e-3f, $"step {i}: the planted sole slides");
                }
            }

            Assert.True(Math.Abs(lowest) < 1e-3f, $"step {i}: no sole on the ground ({lowest})");
            prev = now;
        }
    }

    [Fact]
    public void EveryMove_AtAnyTime_GivesFiniteNumbers()
    {
        foreach (GuardianGiantMove move in Enum.GetValues(typeof(GuardianGiantMove)))
        {
            for (float t = 0f; t < Duration(move) + 2f; t += 0.07f)
            {
                var f = GuardianGiantMotion.Evaluate(move, t, t * 0.4f, 0.7f);
                foreach (float v in new[] { f.Rise, f.RootY, f.TorsoPitch, f.TorsoYaw, f.TorsoRoll, f.HeadPitch, f.HeadYaw, f.ArmLPitch,
                             f.ArmLRoll, f.ArmRPitch, f.ArmRRoll, f.LegLPitch, f.LegRPitch, f.LegLLift, f.LegRLift, f.Shudder, f.Flare,
                             f.Visor, f.SearchLight, f.LookAtAim })
                {
                    Assert.False(float.IsNaN(v) || float.IsInfinity(v), $"{move} at {t}");
                }
            }
        }
    }

    [Fact]
    public void TheBlend_RunsFromTheOldPoseToTheNew()
    {
        var a = At(GuardianGiantMove.Summon, 1.2f);
        var b = GuardianGiantFrame.Standing;
        Assert.Equal(a.ArmLPitch, GuardianGiantFrame.Lerp(a, b, 0f).ArmLPitch, 4);
        Assert.Equal(b.ArmLPitch, GuardianGiantFrame.Lerp(a, b, 1f).ArmLPitch, 4);
        Assert.Equal(a.ArmLPitch * 0.5f, GuardianGiantFrame.Lerp(a, b, 0.5f).ArmLPitch, 3);
        var jump = At(GuardianGiantMove.JumpAttack, 1.5f);
        Assert.Equal(-1f, GuardianGiantFrame.Lerp(jump, b, 0.6f).Air);
        Assert.Equal(jump.Air, GuardianGiantFrame.Lerp(jump, b, 0.4f).Air);
    }

    /// <summary>Each leg's sole: its height over the ground (the hip stands <see cref="GuardianGiantPose.LegLength"/> plus the
    /// root's offset plus the hip hike above it, the rigid leg reaches down by the cosine of its pitch), how far forward of the
    /// world origin it is, and whether it is planted (no hike).</summary>
    private static (float[] Height, float[] Forward, bool[] Planted) Sole(in GuardianGiantFrame f, float body)
    {
        float l = GuardianGiantPose.LegLength;
        float[] pitch = { f.LegLPitch, f.LegRPitch };
        float[] lift = { f.LegLLift, f.LegRLift };
        var height = new float[2];
        var forward = new float[2];
        var planted = new bool[2];
        for (int i = 0; i < 2; i++)
        {
            double rad = pitch[i] * Math.PI / 180.0;
            height[i] = l + f.RootY + lift[i] - (l * (float)Math.Cos(rad));
            forward[i] = body - (l * (float)Math.Sin(rad)); // a positive pitch swings the foot backwards
            planted[i] = lift[i] <= 0f;
        }

        return (height, forward, planted);
    }

    private static void AssertAtRest(in GuardianGiantFrame f, float rise)
    {
        Assert.Equal(rise, f.Rise, 4);
        foreach (float v in new[] { f.RootY, f.TorsoPitch, f.TorsoYaw, f.TorsoRoll, f.HeadPitch, f.HeadYaw, f.ArmLPitch, f.ArmLRoll,
                     f.ArmRPitch, f.ArmRRoll, f.LegLPitch, f.LegRPitch, f.LegLLift, f.LegRLift })
        {
            Assert.Equal(0f, v, 4);
        }

        Assert.Equal(-1f, f.Air);
    }
}

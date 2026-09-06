// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Two-bone leg IK. The load-bearing test is the round trip: run the solved angles back through forward
/// kinematics and the foot must land on the target it was asked for. That checks the solver against the
/// rig's actual frame convention rather than against remembered numbers, which is what a float golden
/// would do (and Windows and Linux libm disagree in the last bits anyway).
/// </summary>
public sealed class CreatureIkTests
{
    private const float Upper = 0.55f;
    private const float Lower = 0.45f;

    /// <summary>Where the foot actually ends up for a solution — the rig applies
    /// <c>Euler(HipPitch, HipYaw, 0)</c> to the hip and <c>Euler(Knee, 0, 0)</c> to the knee, both hanging
    /// their segment along -Y.</summary>
    private static (double X, double Y, double Z) Forward(LegSolution s, float upper, float lower)
    {
        double p = s.HipPitchDeg * Math.PI / 180.0;
        double k = (s.HipPitchDeg + s.KneeDeg) * Math.PI / 180.0;
        double yaw = s.HipYawDeg * Math.PI / 180.0;

        double y = -upper * Math.Cos(p) - lower * Math.Cos(k);
        double z = -upper * Math.Sin(p) - lower * Math.Sin(k);
        return (z * Math.Sin(yaw), y, z * Math.Cos(yaw));
    }

    private static void AssertReaches(float x, float y, float z, int kneeSign)
    {
        var s = CreatureIk.SolveTwoBone(x, y, z, Upper, Lower, kneeSign);
        var (fx, fy, fz) = Forward(s, Upper, Lower);

        Assert.False(s.Overreached, "this target is inside the leg's reach");
        Assert.True(Math.Abs(fx - x) < 1e-3, $"x: wanted {x}, got {fx}");
        Assert.True(Math.Abs(fy - y) < 1e-3, $"y: wanted {y}, got {fy}");
        Assert.True(Math.Abs(fz - z) < 1e-3, $"z: wanted {z}, got {fz}");
    }

    [Fact]
    public void TheFootLandsOnTheTarget_StraightDown()
    {
        AssertReaches(0f, -0.8f, 0f, kneeSign: 1);
        AssertReaches(0f, -0.8f, 0f, kneeSign: -1);
    }

    [Fact]
    public void TheFootLandsOnTheTarget_ForwardBackwardAndSideways()
    {
        foreach (int sign in new[] { 1, -1 })
        {
            AssertReaches(0f, -0.75f, 0.3f, sign);   // reaching forward — the plant at the front of a stride
            AssertReaches(0f, -0.75f, -0.3f, sign);  // trailing behind — the push-off at the end of it
            AssertReaches(0.25f, -0.8f, 0f, sign);   // out to the side — a splayed stance or a turn
            AssertReaches(-0.25f, -0.8f, 0f, sign);
            AssertReaches(0.2f, -0.7f, 0.25f, sign); // all three at once
        }
    }

    [Fact]
    public void TheFootLandsOnTheTarget_AcrossTheWholeReachableVolume()
    {
        // A sweep, because a solver that only works for the cases someone thought to write down is not a
        // solver. Every sample inside the reach must round-trip.
        float reach = CreatureIk.MaxReach(Upper, Lower);
        int checkedSamples = 0;
        for (float x = -0.6f; x <= 0.6f; x += 0.15f)
        {
            for (float y = -0.95f; y <= -0.15f; y += 0.16f)
            {
                for (float z = -0.6f; z <= 0.6f; z += 0.15f)
                {
                    if (Math.Sqrt(x * x + y * y + z * z) >= reach * 0.97f)
                    {
                        continue; // outside the reach is the clamp test's job
                    }

                    AssertReaches(x, y, z, kneeSign: 1);
                    checkedSamples++;
                }
            }
        }

        Assert.True(checkedSamples > 200, $"the sweep should cover the volume, only hit {checkedSamples}");
    }

    [Fact]
    public void AStraightDownTargetAtFullReachLeavesTheLegAlmostStraight()
    {
        var s = CreatureIk.SolveTwoBone(0f, -CreatureIk.MaxReach(Upper, Lower), 0f, Upper, Lower, 1);
        Assert.InRange(Math.Abs(s.KneeDeg), 0f, 25f);   // nearly straight …
        Assert.NotEqual(0f, s.KneeDeg);                  // … but never locked
        Assert.InRange(Math.Abs(s.HipPitchDeg), 0f, 15f);
    }

    [Fact]
    public void ACloseTargetFoldsTheKneeHard()
    {
        var far = CreatureIk.SolveTwoBone(0f, -0.95f, 0f, Upper, Lower, 1);
        var near = CreatureIk.SolveTwoBone(0f, -0.45f, 0f, Upper, Lower, 1);
        Assert.True(Math.Abs(near.KneeDeg) > Math.Abs(far.KneeDeg) + 40f,
            "pulling the foot up under the body has to fold the knee");
    }

    [Fact]
    public void TheKneeSignDecidesWhichWayTheLegBends()
    {
        var elbow = CreatureIk.SolveTwoBone(0f, -0.7f, 0f, Upper, Lower, 1);
        var stifle = CreatureIk.SolveTwoBone(0f, -0.7f, 0f, Upper, Lower, -1);

        Assert.True(elbow.KneeDeg > 0f);
        Assert.True(stifle.KneeDeg < 0f);
        Assert.Equal(Math.Abs(elbow.KneeDeg), Math.Abs(stifle.KneeDeg), 3);
        Assert.Equal(-elbow.HipPitchDeg, stifle.HipPitchDeg, 3); // mirrored compensation at the hip
    }

    [Fact]
    public void LeftAndRightTargetsMirrorEachOther()
    {
        var right = CreatureIk.SolveTwoBone(0.3f, -0.7f, 0.1f, Upper, Lower, 1);
        var left = CreatureIk.SolveTwoBone(-0.3f, -0.7f, 0.1f, Upper, Lower, 1);

        Assert.Equal(-right.HipYawDeg, left.HipYawDeg, 3);
        Assert.Equal(right.HipPitchDeg, left.HipPitchDeg, 3);
        Assert.Equal(right.KneeDeg, left.KneeDeg, 3);
    }

    [Fact]
    public void AnUnreachableTargetIsPulledInAndFlagged()
    {
        var s = CreatureIk.SolveTwoBone(0f, -5f, 0f, Upper, Lower, 1);
        Assert.True(s.Overreached);

        var (fx, fy, fz) = Forward(s, Upper, Lower);
        double reached = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        Assert.Equal(CreatureIk.MaxReach(Upper, Lower), reached, 3); // stretched as far as it goes …
        Assert.True(fy < 0f);                                        // … and still pointing at the target
    }

    [Fact]
    public void DegenerateInputNeverProducesNaN()
    {
        foreach (var s in new[]
        {
            CreatureIk.SolveTwoBone(0f, 0f, 0f, Upper, Lower, 1),       // the target is the hip itself
            CreatureIk.SolveTwoBone(0f, 0.5f, 0f, Upper, Lower, 1),     // above the hip
            CreatureIk.SolveTwoBone(0f, -0.5f, 0f, 0f, 0f, 1),          // a leg with no bones
            CreatureIk.SolveTwoBone(1e-9f, -1e-9f, 1e-9f, Upper, Lower, -1),
        })
        {
            Assert.False(float.IsNaN(s.HipPitchDeg) || float.IsNaN(s.HipYawDeg) || float.IsNaN(s.KneeDeg));
            Assert.False(float.IsInfinity(s.HipPitchDeg) || float.IsInfinity(s.HipYawDeg) || float.IsInfinity(s.KneeDeg));
        }
    }

    [Fact]
    public void UnevenBoneLengthsStillReachTheTarget()
    {
        // Titans and many-legged bodies do not use the same 55/45 split as everything else.
        foreach ((float u, float l) in new[] { (0.8f, 0.2f), (0.2f, 0.8f), (1.6f, 1.4f) })
        {
            var s = CreatureIk.SolveTwoBone(0.1f, -(u + l) * 0.7f, 0.15f, u, l, 1);
            var (fx, fy, fz) = Forward(s, u, l);
            Assert.True(Math.Abs(fx - 0.1f) < 1e-3);
            Assert.True(Math.Abs(fy + (u + l) * 0.7f) < 1e-3);
            Assert.True(Math.Abs(fz - 0.15f) < 1e-3);
        }
    }
}

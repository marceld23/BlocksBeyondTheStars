// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The creature gait mathematics: which foot leaves the ground when, and — the point of the whole type —
/// that a planted foot travels backwards at the body's own speed instead of sliding over the ground.
/// Assertions are on ranges, orderings and invariants, never on trig-derived float goldens (Windows and
/// Linux libm disagree in the last bits).
/// </summary>
public sealed class CreatureGaitTests
{
    // --- footfall patterns ---

    [Fact]
    public void Walk_PutsEveryFootOnItsOwnQuarterOfTheCycle()
    {
        // A quadruped's lateral-sequence walk: four feet, four distinct quarters, so three are always down.
        var offsets = Quadruped(Gait.Walk);
        Assert.Equal(4, offsets.Values.Distinct().Count());

        // The hind foot of a side leads its own front foot (that IS the lateral sequence).
        Assert.True(offsets[(0, 1)] < offsets[(0, 0)]);
        Assert.True(offsets[(1, 1)] < offsets[(1, 0)]);

        // ...and the right side follows the left by half a cycle.
        Assert.Equal(0.5f, offsets[(1, 1)] - offsets[(0, 1)], 3);
    }

    [Fact]
    public void Trot_PairsTheDiagonals()
    {
        var o = Quadruped(Gait.Trot);
        Assert.Equal(o[(0, 0)], o[(1, 1)]);                  // left-front with right-hind
        Assert.Equal(o[(1, 0)], o[(0, 1)]);                  // right-front with left-hind
        Assert.Equal(0.5f, Math.Abs(o[(0, 0)] - o[(1, 0)]), 3); // the couplets are half a cycle apart
    }

    [Fact]
    public void Bound_PairsFrontWithFrontAndHindWithHind()
    {
        var o = Quadruped(Gait.Bound);
        Assert.Equal(o[(0, 0)], o[(1, 0)]);                  // both front feet together
        Assert.Equal(o[(0, 1)], o[(1, 1)]);                  // both hind feet together
        Assert.Equal(0.5f, Math.Abs(o[(0, 0)] - o[(0, 1)]), 3);
    }

    [Fact]
    public void Tripod_SplitsSixLegsIntoTwoAlternatingTriples()
    {
        // The insect gait: left-front, right-middle, left-hind lift together; the other three carry.
        var groups = new Dictionary<float, int>();
        for (int row = 0; row < 3; row++)
        {
            for (int side = 0; side < 2; side++)
            {
                float p = CreatureGait.PhaseOffset(Gait.Tripod, side, row, 3);
                groups[p] = groups.TryGetValue(p, out int n) ? n + 1 : 1;
            }
        }

        Assert.Equal(2, groups.Count);
        Assert.All(groups.Values, n => Assert.Equal(3, n)); // three legs in each tripod
        Assert.Equal(0.5f, Math.Abs(groups.Keys.First() - groups.Keys.Last()), 3);
    }

    [Fact]
    public void Metachronal_RunsAWaveFromTheRearForward()
    {
        // Eight legs, one side: the rear leg steps first and the wave travels toward the head.
        var byRow = Enumerable.Range(0, 4)
            .Select(row => CreatureGait.PhaseOffset(Gait.Metachronal, 0, row, 4))
            .ToArray();

        for (int row = 3; row > 0; row--)
        {
            Assert.True(byRow[row] < byRow[row - 1], $"row {row} must lead row {row - 1}");
        }
    }

    [Fact]
    public void PhaseOffsets_StayInTheUnitCycle_ForEveryBodyWeBuild()
    {
        foreach (Gait gait in Enum.GetValues(typeof(Gait)))
        {
            for (int rows = 1; rows <= 4; rows++)
            {
                for (int row = 0; row < rows; row++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        float p = CreatureGait.PhaseOffset(gait, side, row, rows);
                        Assert.InRange(p, 0f, 0.9999f);
                    }
                }
            }
        }
    }

    [Fact]
    public void PhaseOffsets_SurviveOutOfRangeInput()
    {
        // The renderer must never be able to produce a NaN pose from a malformed rig.
        Assert.InRange(CreatureGait.PhaseOffset(Gait.Walk, 5, -3, 0), 0f, 0.9999f);
        Assert.InRange(CreatureGait.PhaseOffset(Gait.Metachronal, 1, 9, 2), 0f, 0.9999f);
    }

    // --- the stance sweep: the anti-skate guarantee ---

    [Fact]
    public void Stance_SweepsTheFootBackwardsMonotonically()
    {
        float duty = CreatureGait.DutyFactor(Gait.Walk);
        float prev = float.MaxValue;
        for (int i = 0; i <= 40; i++)
        {
            float phase = duty * i / 40f * 0.999f;
            var pose = CreatureGait.Evaluate(phase, duty, 30f);
            Assert.True(pose.SwingDeg < prev, "the planted foot must travel backwards without pausing");
            Assert.Equal(0f, pose.Lift01);  // a planted foot is never lifted
            Assert.Equal(0f, pose.Fold01);  // ...nor folded
            prev = pose.SwingDeg;
        }
    }

    [Fact]
    public void Stance_MovesTheFootAtAConstantGroundSpeed()
    {
        // This is the whole reason the type exists: with a linear ANGLE sweep the foot's ground velocity
        // varies by ~15 % across the stance (that residual is the skate). The angle is an arcsine, so the
        // ground position is linear — every sample step must cover the same distance.
        const float legLength = 1.2f;
        float duty = CreatureGait.DutyFactor(Gait.Trot);
        var steps = new List<float>();
        float prevX = FootX(CreatureGait.Evaluate(0f, duty, 30f), legLength);
        for (int i = 1; i <= 24; i++)
        {
            float x = FootX(CreatureGait.Evaluate(duty * i / 24f * 0.999f, duty, 30f), legLength);
            steps.Add(prevX - x);
            prevX = x;
        }

        float min = steps.Min(), max = steps.Max();
        Assert.True(min > 0f, "every stance sample must move the foot backwards");
        Assert.True(max / min < 1.05f, $"stance foot speed must be near-constant (spread {max / min:F3})");
    }

    [Fact]
    public void Stance_CoversExactlyOneStrideLength()
    {
        // The renderer divides body speed by StrideLength to get the cycle rate; if the swept chord did not
        // match that length, every step would still slide by the difference.
        const float legLength = 0.9f;
        const float amp = 26f;
        float duty = CreatureGait.DutyFactor(Gait.Walk);
        float front = FootX(CreatureGait.Evaluate(0f, duty, amp), legLength);
        float back = FootX(CreatureGait.Evaluate(duty * 0.9999f, duty, amp), legLength);

        Assert.Equal(CreatureGait.StrideLength(legLength, amp), front - back, 3);
    }

    [Fact]
    public void Swing_LiftsAndFoldsTheLegAndReturnsItToTheFront()
    {
        float duty = CreatureGait.DutyFactor(Gait.Walk);
        var mid = CreatureGait.Evaluate(duty + (1f - duty) * 0.5f, duty, 30f);
        Assert.InRange(mid.Lift01, 0.9f, 1f);   // the foot is clear of the ground at mid-swing
        Assert.InRange(mid.Fold01, 0.9f, 1f);   // ...and the knee is folded

        var late = CreatureGait.Evaluate(0.999f, duty, 30f);
        Assert.InRange(late.SwingDeg, 29f, 30.01f); // back to fully forward, ready to plant
    }

    [Fact]
    public void Evaluate_IsContinuousAcrossBothPhaseBoundaries()
    {
        foreach (Gait gait in Enum.GetValues(typeof(Gait)))
        {
            float duty = CreatureGait.DutyFactor(gait);
            var stanceEnd = CreatureGait.Evaluate(duty - 1e-4f, duty, 30f);
            var swingStart = CreatureGait.Evaluate(duty + 1e-4f, duty, 30f);
            Assert.True(Math.Abs(stanceEnd.SwingDeg - swingStart.SwingDeg) < 1f, $"{gait}: stance→swing jump");
            Assert.True(Math.Abs(stanceEnd.Lift01 - swingStart.Lift01) < 0.05f, $"{gait}: lift jump");

            var swingEnd = CreatureGait.Evaluate(0.9999f, duty, 30f);
            var stanceStart = CreatureGait.Evaluate(0f, duty, 30f);
            Assert.True(Math.Abs(swingEnd.SwingDeg - stanceStart.SwingDeg) < 1f, $"{gait}: swing→stance jump");
            Assert.True(swingEnd.Lift01 < 0.05f, $"{gait}: the foot must be down again before it plants");
        }
    }

    [Fact]
    public void Evaluate_WrapsPhaseAndClampsAmplitude_WithoutProducingNaN()
    {
        foreach (float phase in new[] { -3.7f, -0.2f, 0f, 1f, 4.25f })
        {
            var pose = CreatureGait.Evaluate(phase, 0.5f, 500f);
            Assert.False(float.IsNaN(pose.SwingDeg) || float.IsNaN(pose.Lift01) || float.IsNaN(pose.Fold01));
            Assert.InRange(pose.SwingDeg, -81f, 81f);
            Assert.InRange(pose.Lift01, 0f, 1f);
        }

        var degenerate = CreatureGait.Evaluate(0.3f, 0f, 0f); // duty clamps up, amplitude clamps to a still leg
        Assert.False(float.IsNaN(degenerate.SwingDeg));
    }

    // --- speed coupling ---

    [Fact]
    public void CycleRate_IsSpeedOverStride_SoTwiceAsFastMeansTwiceTheSteps()
    {
        float stride = CreatureGait.StrideLength(1f, 30f);
        float slow = CreatureGait.CycleRate(1f, stride);
        float fast = CreatureGait.CycleRate(2f, stride);

        Assert.Equal(2f * slow, fast, 3);
        Assert.Equal(0f, CreatureGait.CycleRate(0f, stride));           // standing still: no cycle at all
        Assert.True(CreatureGait.CycleRate(999f, stride) <= 6f);        // and a runaway speed cannot blur it
        Assert.True(CreatureGait.CycleRate(2f, 0f) > 0f);               // a degenerate stride still animates
    }

    [Fact]
    public void StrideLength_GrowsWithLegLengthAndAmplitude()
    {
        Assert.True(CreatureGait.StrideLength(2f, 30f) > CreatureGait.StrideLength(1f, 30f));
        Assert.True(CreatureGait.StrideLength(1f, 40f) > CreatureGait.StrideLength(1f, 20f));
        Assert.True(CreatureGait.StrideLength(0f, 30f) > 0f); // never zero — it is a divisor
    }

    [Fact]
    public void LongerLegsTakeFewerStepsForTheSameGroundSpeed()
    {
        // The scale cue that #638 hand-tuned with CadenceScale now falls out of the geometry.
        float titan = CreatureGait.CycleRate(3f, CreatureGait.StrideLength(2.4f, 26f));
        float sheep = CreatureGait.CycleRate(3f, CreatureGait.StrideLength(0.6f, 26f));
        Assert.True(titan < sheep * 0.5f, "a titan must stride far slower than a sheep at the same speed");
    }

    // --- gait selection ---

    [Fact]
    public void Select_MovesUpThroughTheGaitsAsSpeedRises()
    {
        Assert.Equal(Gait.Walk, CreatureGait.Select(MotionClass.Walker, 4, giant: false, 0.1f));
        Assert.Equal(Gait.Trot, CreatureGait.Select(MotionClass.Walker, 4, giant: false, 0.5f));
        Assert.Equal(Gait.Bound, CreatureGait.Select(MotionClass.Walker, 4, giant: false, 0.95f));
    }

    [Fact]
    public void Select_KeepsGiantsAndCrawlersOutOfTheBound()
    {
        Assert.Equal(Gait.Walk, CreatureGait.Select(MotionClass.Walker, 4, giant: true, 0.5f));
        Assert.Equal(Gait.Trot, CreatureGait.Select(MotionClass.Walker, 4, giant: true, 0.95f));
        Assert.Equal(Gait.Trot, CreatureGait.Select(MotionClass.Crawler, 4, giant: false, 0.95f));
    }

    [Fact]
    public void Select_GivesManyLeggedBodiesTheInsectPatterns()
    {
        Assert.Equal(Gait.Metachronal, CreatureGait.Select(MotionClass.Crawler, 8, giant: false, 0.1f));
        Assert.Equal(Gait.Tripod, CreatureGait.Select(MotionClass.Crawler, 6, giant: false, 0.8f));
    }

    [Fact]
    public void Select_GivesSwimmersAndLeglessBodiesThePaddle()
    {
        Assert.Equal(Gait.Paddle, CreatureGait.Select(MotionClass.Swimmer, 0, giant: false, 0.5f));
        Assert.Equal(Gait.Paddle, CreatureGait.Select(MotionClass.Swimmer, 4, giant: false, 0.5f));
        Assert.Equal(Gait.Paddle, CreatureGait.Select(MotionClass.Crawler, 0, giant: false, 0.5f));
    }

    [Fact]
    public void Select_OnlyEverReturnsAGaitTheBodyIsAllowed()
    {
        int[] legCounts = { 0, 2, 4, 6, 8 };
        MotionClass[] classes =
        {
            MotionClass.Walker, MotionClass.Crawler, MotionClass.Flier, MotionClass.Hoverer, MotionClass.Swimmer,
        };

        foreach (var cls in classes)
        {
            foreach (int legs in legCounts)
            {
                foreach (bool giant in new[] { false, true })
                {
                    for (float speed = 0f; speed <= 1.001f; speed += 0.05f)
                    {
                        var gait = CreatureGait.Select(cls, legs, giant, speed);
                        Assert.True(CreatureGait.IsAllowed(gait, cls, legs, giant),
                            $"{cls}/{legs} legs/giant={giant} at {speed:F2} chose {gait}");
                    }
                }
            }
        }
    }

    // --- body motion ---

    [Fact]
    public void DutyFactors_KeepTheSlowGaitsStableAndTheFastOnesAirborne()
    {
        Assert.True(CreatureGait.DutyFactor(Gait.Walk) > 0.5f);        // more than one foot down per pair
        Assert.True(CreatureGait.DutyFactor(Gait.Metachronal) > 0.6f); // a crawler keeps most legs planted
        Assert.True(CreatureGait.DutyFactor(Gait.Bound) < 0.5f);       // a bound has real airborne moments

        foreach (Gait gait in Enum.GetValues(typeof(Gait)))
        {
            Assert.InRange(CreatureGait.DutyFactor(gait), 0.3f, 0.8f);
        }
    }

    [Fact]
    public void BodyBob_DipsTwicePerStride_ExceptInABound()
    {
        Assert.Equal(2, ZeroCrossings(p => CreatureGait.BodyBob(p, Gait.Trot)) / 2);
        Assert.Equal(1, ZeroCrossings(p => CreatureGait.BodyBob(p, Gait.Bound)) / 2);
        Assert.Equal(0f, CreatureGait.BodyBob(0.3f, Gait.Paddle)); // a swimmer has no footfalls to bob on
    }

    [Fact]
    public void BodyMotion_StaysNormalised()
    {
        foreach (Gait gait in Enum.GetValues(typeof(Gait)))
        {
            for (float p = 0f; p < 1f; p += 0.01f)
            {
                Assert.InRange(CreatureGait.BodyBob(p, gait), -1.001f, 1.001f);
                Assert.InRange(CreatureGait.BodyRoll(p, gait), -1.001f, 1.001f);
            }
        }

        // Only the gaits with a left/right support asymmetry roll; a bound and a tripod stay level.
        Assert.Equal(0f, CreatureGait.BodyRoll(0.25f, Gait.Bound));
        Assert.Equal(0f, CreatureGait.BodyRoll(0.25f, Gait.Tripod));
        Assert.NotEqual(0f, CreatureGait.BodyRoll(0.25f, Gait.Walk));
    }

    // --- helpers ---

    /// <summary>Phase offsets of a four-legged body keyed by (side, row); side 0 = left, row 0 = front.</summary>
    private static Dictionary<(int Side, int Row), float> Quadruped(Gait gait)
    {
        var map = new Dictionary<(int, int), float>();
        for (int side = 0; side < 2; side++)
        {
            for (int row = 0; row < 2; row++)
            {
                map[(side, row)] = CreatureGait.PhaseOffset(gait, side, row, 2);
            }
        }

        return map;
    }

    /// <summary>Where the foot sits along the body axis for a pose, given the leg length.</summary>
    private static float FootX(LegPose pose, float legLength)
        => legLength * (float)Math.Sin(pose.SwingDeg * Math.PI / 180.0);

    private static int ZeroCrossings(Func<float, float> f)
    {
        int crossings = 0;
        float prev = f(0f);
        for (int i = 1; i <= 2000; i++)
        {
            float v = f(i / 2000f);
            if ((prev < 0f && v >= 0f) || (prev > 0f && v <= 0f))
            {
                crossings++;
            }

            prev = v;
        }

        return crossings;
    }
}

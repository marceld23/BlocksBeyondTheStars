// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3444 (Lyxette: "the train is too slow to connect a farther city"): the train's speed profile, pure. The settings are
/// top speeds 8 / 16 / 24; a train eases in, cruises by the length of its leg (a short hop stays gentle, a long leg
/// reaches the top speed), brakes into its target so it arrives at a walking pace, follows a lowered limit without a
/// jolt, and slows for sharp bends — before it reaches them.
/// </summary>
public sealed class RailMotionTests
{
    private const double Tick = 1.0 / 15.0;

    [Fact]
    public void TheSettings_AreTopSpeeds_8_16_24_AndANewCabStartsAtTheMiddleOne()
    {
        Assert.Equal(8f, RailMotion.TopSpeed(1));
        Assert.Equal(16f, RailMotion.TopSpeed(2));
        Assert.Equal(24f, RailMotion.TopSpeed(3));
        Assert.Equal(8f, RailMotion.TopSpeed(0)); // clamped
        Assert.Equal(24f, RailMotion.TopSpeed(9));
        Assert.Equal(24f, RailMotion.MaxSpeed);
        Assert.Equal(2, RailRules.NewCabSpeed);
        Assert.Equal(2, RailRules.PublicTrainSpeed);
    }

    [Fact]
    public void ATrain_EasesIn_FromRest()
    {
        float v = 0f;
        for (int i = 0; i < 15; i++)
        {
            float next = RailMotion.NextSpeed(v, 16f, Tick);
            Assert.True(next - v <= (RailMotion.Acceleration * Tick) + 1e-4, $"tick {i}: {v} → {next} is no ease-in");
            v = next;
        }

        Assert.Equal(RailMotion.Acceleration, v, 2); // one second: 4 blocks/s
        for (int i = 0; i < 15 * 10; i++)
        {
            v = RailMotion.NextSpeed(v, 16f, Tick);
        }

        Assert.Equal(16f, v, 3); // and it never runs past the limit
    }

    [Fact]
    public void TheCruise_FollowsTheLeg_AShortHopStaysGentle_ALongLegReachesTheTopSpeed()
    {
        Assert.True(RailMotion.CruiseSpeed(40f, 24f) <= 8f, "a 40-block hop in town tops out at about 7");
        Assert.Equal(RailMotion.MinCruise, RailMotion.CruiseSpeed(6f, 24f)); // a few blocks still move on briskly
        Assert.Equal(24f, RailMotion.CruiseSpeed(150f, 24f));
        Assert.Equal(16f, RailMotion.CruiseSpeed(150f, 16f)); // never past the setting
        Assert.Equal(16f, RailMotion.CruiseSpeed(float.PositiveInfinity, 16f)); // a loop without stops: the top speed
        Assert.Equal(8f, RailMotion.CruiseSpeed(float.NaN, 8f));
        Assert.True(RailMotion.CruiseSpeed(60f, 24f) < RailMotion.CruiseSpeed(120f, 24f), "a longer leg runs faster");

        // Run a whole leg (accelerating, cruising, braking): a 40-block hop stays at or under 8, a 160-block leg reaches 24.
        Assert.True(RunLeg(40f, 24f).Peak <= 8f);
        Assert.True(RunLeg(160f, 24f).Peak >= 23.5f, $"a 160-block leg peaked at {RunLeg(160f, 24f).Peak}");
    }

    [Fact]
    public void Braking_ArrivesAtTheTarget_AtAWalkingPace_WithoutOvershooting()
    {
        foreach (float leg in new[] { 12f, 40f, 96f, 300f })
        {
            var (peak, arrival, steps) = RunLeg(leg, 24f);
            Assert.True(peak > RailMotion.ArrivalSpeed, $"leg {leg}: it never got going ({peak})");
            // The last step's speed: the braking curve at under one step from the target — a walking pace.
            Assert.True(arrival <= RailMotion.ArrivalSpeed + (2f * RailMotion.Acceleration * (float)Tick),
                $"leg {leg}: it came in at {arrival} blocks/s");
            Assert.True(steps < 15 * 60, $"leg {leg}: it never arrived");
        }

        Assert.Equal(RailMotion.ArrivalSpeed, RailMotion.BrakingSpeed(0f), 4);
        Assert.Equal(RailMotion.ArrivalSpeed, RailMotion.BrakingSpeed(-3f), 4);
        Assert.True(float.IsPositiveInfinity(RailMotion.BrakingSpeed(float.PositiveInfinity)));
    }

    [Fact]
    public void ALoweredLimit_IsFollowedWithoutAJolt_AndTheTopSpeedIsNeverPassed()
    {
        float v = 24f;
        for (int i = 0; i < 60; i++)
        {
            float next = RailMotion.NextSpeed(v, 8f, Tick); // the driver picked setting 1 at full speed
            Assert.True(v - next <= (RailMotion.ServiceBrake * Tick) + 1e-4, $"tick {i}: {v} → {next} is a jolt");
            Assert.True(next >= 8f - 1e-4f);
            v = next;
        }

        Assert.Equal(8f, v, 3);
        Assert.Equal(16f, RailMotion.Limit(16f, float.PositiveInfinity, float.PositiveInfinity, 30f));
        Assert.Equal(6f, RailMotion.Limit(24f, float.PositiveInfinity, float.PositiveInfinity, 6f)); // the bend wins
        Assert.Equal(0f, RailMotion.NextSpeed(float.NaN, float.NaN, double.NaN));
    }

    [Fact]
    public void ALoopWithoutStops_CruisesAtTheTopSpeed()
    {
        float v = 0f;
        for (int i = 0; i < 15 * 10; i++)
        {
            v = RailMotion.NextSpeed(v, RailMotion.Limit(24f, float.PositiveInfinity, float.PositiveInfinity, RailMotion.MaxSpeed), Tick);
        }

        Assert.Equal(24f, v, 3);
    }

    [Fact]
    public void Gap_MeasuresAheadTheWayTheTrainRuns_AndWrapsOnALoop()
    {
        Assert.Equal(10f, RailMotion.Gap(20f, 30f, 1, 100f, closed: false));
        Assert.Equal(-10f, RailMotion.Gap(20f, 30f, -1, 100f, closed: false)); // behind it
        Assert.Equal(90f, RailMotion.Gap(20f, 10f, 1, 100f, closed: true)); // round the loop
        Assert.Equal(10f, RailMotion.Gap(20f, 10f, -1, 100f, closed: true));
        Assert.Equal(0f, RailMotion.Gap(20f, 20f, 1, 100f, closed: true));
    }

    [Fact]
    public void ABend_IsTakenSlowly_TheLimitSpreadsAhead_AndAStraightLineRunsFlatOut()
    {
        // A circle of radius 20: v = √(4 · 20) ≈ 8.9 blocks/s all the way round.
        var circle = new List<Vector3f>();
        for (int i = 0; i < 16; i++)
        {
            double a = i * Math.PI * 2.0 / 16.0;
            circle.Add(new Vector3f((float)(20.0 * Math.Cos(a)) + 100f, 60f, (float)(20.0 * Math.Sin(a)) + 100f));
        }

        var loop = RailCurveLimits.Build(RailSpline.Build(circle, closed: true, WorldConstants.Circumference));
        float expect = (float)Math.Sqrt(RailMotion.LateralAcceleration * 20f);
        for (float s = 0f; s < loop.Spline.TotalArc; s += 5f)
        {
            Assert.InRange(loop.At(s), expect * 0.8f, expect * 1.15f);
        }

        // A straight run with one sharp corner (90°) in the middle: slow at the corner, the limit rising away from it no
        // faster than the braking curve, flat out far from it.
        var corner = RailCurveLimits.Build(RailSpline.Build(
            new[] { new Vector3f(0.5f, 60f, 0.5f), new Vector3f(60.5f, 60f, 0.5f), new Vector3f(120.5f, 60f, 0.5f), new Vector3f(120.5f, 60f, 60.5f), new Vector3f(120.5f, 60f, 120.5f) },
            closed: false, WorldConstants.Circumference));
        float total = corner.Spline.TotalArc;
        float atCorner = corner.MinOver(total * 0.5f - 4f, total * 0.5f + 4f);
        Assert.True(atCorner < 12f, $"the corner is taken at {atCorner}");
        Assert.Equal(RailMotion.MaxSpeed, corner.At(10f), 3);
        Assert.Equal(RailMotion.MaxSpeed, corner.At(total - 10f), 3);
        for (float s = 0f; s + RailCurveLimits.Step <= total; s += RailCurveLimits.Step)
        {
            float a = corner.At(s), b = corner.At(s + RailCurveLimits.Step);
            float rise = 2f * RailMotion.Acceleration * RailCurveLimits.Step * 2f; // two samples per step at most
            Assert.True((b * b) <= (a * a) + rise + 0.01f && (a * a) <= (b * b) + rise + 0.01f, $"the limit jumps at arc {s}: {a} → {b}");
        }

        // The limit spreads ahead of the corner: a train braking on the curve reaches the corner at its limit.
        float before = corner.At(total * 0.5f - 30f);
        Assert.True(before < RailMotion.MaxSpeed && before > atCorner, $"30 blocks before the corner the limit is {before}");
    }

    [Fact]
    public void AGentleCurveOnLongSpans_RunsAtTheTopSpeed_AndShorterSpansReadTheirTrueRadius()
    {
        // The spline is a polyline of ten segments per span — 6.4 blocks on a 64-block linker span. The old 4-block chord read
        // its corners as tight bends: 64-block spans turning 15° per pylon (radius ≈ 245) were held to 19 blocks/s near the
        // line's ends, 32-block spans (radius ≈ 123) to 20.5 in the middle. A chord of at least twice the segment reads the
        // curve the polyline approximates.
        Assert.True(RailCurveLimits.ChordAt(Bend(64f, 15.0), 100f) >= 2f * 6.4f - 0.5f);
        var wide = RailCurveLimits.Build(Bend(64f, 15.0));
        for (float s = 0f; s <= wide.Spline.TotalArc; s += 1f)
        {
            Assert.Equal(RailMotion.MaxSpeed, wide.At(s), 3); // the whole line, its end spans too
        }

        var closer = RailCurveLimits.Build(Bend(32f, 15.0));
        float expect = (float)Math.Sqrt(RailMotion.LateralAcceleration * 32.0 / (2.0 * Math.Sin(7.5 * Math.PI / 180.0))); // ≈ 22.1
        ForTheMiddle(closer.Spline, s => Assert.InRange(closer.At(s), expect * 0.95f, expect * 1.05f));
    }

    [Fact]
    public void AGentleHill_RunsAtTheTopSpeed_WhileASharpCrestStillSlows()
    {
        // Crests and dips are held to the vertical bound (8 blocks/s², twice the sideways one): a 1-in-8 grade over 32-block
        // spans (crest and dip radius ≈ 80) runs flat out, where the sideways bound alone held it to about 18.
        var hill = Hill(32f, 4f);
        var limits = RailCurveLimits.Build(hill);
        float sharpest = 0f;
        for (float s = 0f; s <= hill.TotalArc; s += 1f)
        {
            Assert.Equal(RailMotion.MaxSpeed, limits.At(s), 3);
            var (sideways, vertical) = RailCurveLimits.Curvature(hill, s);
            Assert.Equal(0f, sideways, 5); // straight, seen from above
            sharpest = Math.Max(sharpest, vertical);
        }

        Assert.True(sharpest > 0f && RailMotion.CurveSpeed(sharpest) < 20f, $"the crest's curvature {sharpest} is no crest at all");

        // A sharp crest — 10 blocks up and down again over 16-block spans — is still taken slowly.
        var sharp = RailCurveLimits.Build(Hill(16f, 10f));
        float slowest = RailMotion.MaxSpeed;
        for (float s = 0f; s <= sharp.Spline.TotalArc; s += 1f)
        {
            slowest = Math.Min(slowest, sharp.At(s));
        }

        Assert.True(slowest < 12f, $"the sharp crest is taken at {slowest} blocks/s");
    }

    [Fact]
    public void TheBendLimit_IsTheLowerOfTheSidewaysAndTheVerticalBound()
    {
        Assert.Equal(8f, RailMotion.CurveSpeed(1f / 16f), 3); // a sideways bend of radius 16
        Assert.Equal(8f, RailMotion.CrestSpeed(1f / 8f), 3); // a crest of radius 8
        Assert.Equal(24f, RailMotion.CrestSpeed(1f / 72f), 3); // radius 72: the top speed
        Assert.Equal(16f, RailMotion.CurveSpeed(1f / 64f, 1f / 72f), 3); // the bend wins
        Assert.Equal(8f, RailMotion.CurveSpeed(1f / 64f, 1f / 8f), 3); // the crest wins
        Assert.Equal(RailMotion.MaxSpeed, RailMotion.CurveSpeed(0f, float.NaN));
    }

    [Fact]
    public void AStopTooCloseToBrakeFor_IsPassed_ButAnOrdinaryArrivalNeverIs()
    {
        Assert.False(RailMotion.CanArriveAt(24f, 3f)); // the autopilot switched on 3 blocks before a stop at full speed
        Assert.False(RailMotion.CanArriveAt(24f, 30f));
        Assert.True(RailMotion.CanArriveAt(24f, 40f)); // the service brake takes 24 → a walking pace in ≈ 34 blocks
        Assert.True(RailMotion.CanArriveAt(RailMotion.MaxHaltSpeed, 0f));
        Assert.True(RailMotion.CanArriveAt(0f, 0f));
        Assert.True(RailMotion.CanArriveAt(float.NaN, 1f));

        // A train braking into its stop on the curve is never told the stop is out of reach — on any tick, the last one too,
        // whatever the tick length.
        foreach (double tick in new[] { 1.0 / 30.0, 1.0 / 15.0, 0.1, 0.25 })
        {
            foreach (float leg in new[] { 6f, 40f, 300f })
            {
                float v = 0f, covered = 0f;
                for (int i = 0; covered < leg && i < 10_000; i++)
                {
                    float remaining = leg - covered;
                    Assert.True(RailMotion.CanArriveAt(v, remaining), $"tick {tick}, leg {leg}: passed at {remaining} blocks, {v} blocks/s");
                    v = RailMotion.NextSpeed(v, RailMotion.Limit(24f, leg, remaining, RailMotion.MaxSpeed), tick);
                    covered += Math.Min(v * (float)tick, remaining);
                }
            }
        }
    }

    /// <summary>Runs a train from rest over a leg toward a target <paramref name="leg"/> blocks ahead under a top speed: the
    /// peak, the speed it came in at (the last step), and the ticks it took. The last step is cut onto the target, as the
    /// server does.</summary>
    private static (float Peak, float Arrival, int Steps) RunLeg(float leg, float cap)
    {
        float v = 0f, covered = 0f, peak = 0f;
        int steps = 0;
        while (covered < leg && steps < 15 * 60)
        {
            float remaining = leg - covered;
            v = RailMotion.NextSpeed(v, RailMotion.Limit(cap, leg, remaining, RailMotion.MaxSpeed), Tick);
            peak = Math.Max(peak, v);
            covered += Math.Min(v * (float)Tick, remaining);
            steps++;
        }

        return (peak, v, steps);
    }

    /// <summary>An open line of 13 pylons <paramref name="span"/> blocks apart, turning <paramref name="turnDegrees"/> at each
    /// (a polygon on a circle of radius span / (2·sin(turn / 2))).</summary>
    private static RailSpline Bend(float span, double turnDegrees)
    {
        var pts = new List<Vector3f>();
        double heading = 0.0;
        float x = 0.5f, z = 0.5f;
        for (int i = 0; i <= 12; i++)
        {
            pts.Add(new Vector3f(x, 70f, z));
            heading += turnDegrees * Math.PI / 180.0;
            x += (float)(span * Math.Cos(heading));
            z += (float)(span * Math.Sin(heading));
        }

        return RailSpline.Build(pts, closed: false, WorldConstants.Circumference);
    }

    /// <summary>A straight open line of 13 pylons <paramref name="span"/> blocks apart that climbs <paramref name="rise"/>
    /// blocks per span for three spans, falls for three, and again.</summary>
    private static RailSpline Hill(float span, float rise)
    {
        float[] steps = { 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 2, 1, 0 };
        var pts = new List<Vector3f>();
        for (int i = 0; i < steps.Length; i++)
        {
            pts.Add(new Vector3f(0.5f + (i * span), 70f + (steps[i] * rise), 0.5f));
        }

        return RailSpline.Build(pts, closed: false, WorldConstants.Circumference);
    }

    /// <summary>Checks the middle of a line (30 % to 70 % of its arc), clear of an open line's end spans, every block.</summary>
    private static void ForTheMiddle(RailSpline spline, Action<float> check)
    {
        for (float s = spline.TotalArc * 0.3f; s <= spline.TotalArc * 0.7f; s += 1f)
        {
            check(s);
        }
    }
}

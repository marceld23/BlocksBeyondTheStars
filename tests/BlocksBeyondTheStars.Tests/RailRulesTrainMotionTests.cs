// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The monorail train's motion rules (#2741), pure: the arc runs inside the cab's range and bounces off an open line's
/// ends without a jump (only the direction flips), a loop wraps, the leading wagon is the cab one way and the last wagon
/// the other, and the wagons hang behind the cab by the train's fixed facing.
/// </summary>
public sealed class RailRulesTrainMotionTests
{
    private static readonly float Span = RailRules.TrainSpan(2); // a cab and one wagon

    [Fact]
    public void TheSpan_AndTheCabRange_FollowTheFacing()
    {
        Assert.Equal(0f, RailRules.TrainSpan(1));
        Assert.Equal(RailRules.WagonLength + RailRules.WagonGap, Span, 4);
        Assert.Equal((Span, 50f), RailRules.CabRange(50f, Span, 1));
        Assert.Equal((0f, 50f - Span), RailRules.CabRange(50f, Span, -1));

        // A train longer than its line: the cab is pinned at the far end of the way it faces.
        Assert.Equal((10f, 10f), RailRules.CabRange(10f, RailRules.TrainSpan(4), 1));
        Assert.Equal((0f, 0f), RailRules.CabRange(10f, RailRules.TrainSpan(4), -1));
    }

    [Fact]
    public void Advance_RunsAlong_AndBouncesOffTheEnd_TheRestOfTheStepRunningBack()
    {
        // Facing +1: the cab's range is [span, 50]. One block short of the end, three blocks of travel: one up, two back.
        var (arc, dir) = RailRules.Advance(49f, 1, 1, 3f, 50f, Span, closed: false);
        Assert.Equal(48f, arc, 4);
        Assert.Equal(-1, dir);

        // Reaching the end exactly turns it round there.
        (arc, dir) = RailRules.Advance(48f, 1, 1, 2f, 50f, Span, closed: false);
        Assert.Equal(50f, arc, 4);
        Assert.Equal(-1, dir);

        // Facing −1: the range is [0, 50 − span]; the bounce at the start.
        (arc, dir) = RailRules.Advance(1f, -1, -1, 3f, 50f, Span, closed: false);
        Assert.Equal(2f, arc, 4);
        Assert.Equal(1, dir);

        // Running backwards (direction against the facing) bounces at the range's lower bound, the span, not at 0.
        (arc, dir) = RailRules.Advance(Span + 1f, -1, 1, 3f, 50f, Span, closed: false);
        Assert.Equal(Span + 2f, arc, 4);
        Assert.Equal(1, dir);

        // An ordinary step.
        (arc, dir) = RailRules.Advance(20f, -1, 1, 0.5f, 50f, Span, closed: false);
        Assert.Equal(19.5f, arc, 4);
        Assert.Equal(-1, dir);
    }

    [Fact]
    public void Advance_NeverJumps_AndSplitStepsEqualOneStep()
    {
        // Many small steps across several reversals: no step moves the arc more than the step's distance, the arc stays in
        // the range, and the result equals one big step of the same total distance.
        const float total = 37f, step = 0.37f;
        var (lo, hi) = RailRules.CabRange(total, Span, 1);
        float arc = 20f;
        int dir = 1;
        int flips = 0;
        for (int i = 0; i < 400; i++)
        {
            var (next, nextDir) = RailRules.Advance(arc, dir, 1, step, total, Span, closed: false);
            Assert.True(Math.Abs(next - arc) <= step + 1e-4f, $"step {i} jumped from {arc} to {next}");
            Assert.InRange(next, lo, hi);
            flips += nextDir != dir ? 1 : 0;
            arc = next;
            dir = nextDir;
        }

        Assert.True(flips >= 4, $"only {flips} reversals");
        var (once, onceDir) = RailRules.Advance(20f, 1, 1, 400 * step, total, Span, closed: false);
        Assert.True(Math.Abs(arc - once) < 0.05f, $"small steps ended at {arc}, one step at {once}");
        Assert.Equal(dir, onceDir);
    }

    [Fact]
    public void Advance_OnALoop_Wraps_AndNeverTurnsRound()
    {
        var (arc, dir) = RailRules.Advance(98f, 1, 1, 5f, 100f, Span, closed: true);
        Assert.Equal(3f, arc, 3);
        Assert.Equal(1, dir);

        (arc, dir) = RailRules.Advance(2f, -1, 1, 5f, 100f, Span, closed: true);
        Assert.Equal(97f, arc, 3);
        Assert.Equal(-1, dir);
    }

    [Fact]
    public void Advance_BringsAnArcOutsideItsRangeBack_AndIgnoresABadStep()
    {
        // A wagon coupled while the cab stood at the line's start: the arc comes back into the range first.
        var (arc, dir) = RailRules.Advance(0f, 1, 1, 0f, 50f, Span, closed: false);
        Assert.Equal(Span, arc, 4);
        Assert.Equal(1, dir);

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -3f })
        {
            (arc, dir) = RailRules.Advance(20f, 1, 1, bad, 50f, Span, closed: false);
            Assert.Equal(20f, arc);
            Assert.Equal(1, dir);
        }

        // No room to run: the train stands at its one spot.
        (arc, _) = RailRules.Advance(5f, 1, 1, 2f, 10f, RailRules.TrainSpan(4), closed: false);
        Assert.Equal(10f, arc);
    }

    [Fact]
    public void TheLeadingWagon_IsTheCabOneWay_AndTheLastWagonTheOther()
    {
        Assert.Equal(30f, RailRules.LeadArc(30f, 1, 1, Span));
        Assert.Equal(30f - Span, RailRules.LeadArc(30f, -1, 1, Span), 4);
        Assert.Equal(30f, RailRules.LeadArc(30f, -1, -1, Span));
        Assert.Equal(30f + Span, RailRules.LeadArc(30f, 1, -1, Span), 4);

        // A halt puts the leading wagon on the stop, whichever way round.
        foreach (int direction in new[] { -1, 1 })
            foreach (int facing in new[] { -1, 1 })
            {
                float arc = RailRules.ArcForLeadAt(25f, direction, facing, Span);
                Assert.Equal(25f, RailRules.LeadArc(arc, direction, facing, Span), 4);
            }
    }

    [Fact]
    public void TheWagons_HangBehindTheCabByTheFacing_AndAnOldServersTrainFacesItsDirection()
    {
        Assert.Equal(30f - Span, RailRules.WagonArc(30f, 1, 1), 4);
        Assert.Equal(30f + 2 * Span, RailRules.WagonArc(30f, 2, -1), 4);

        Assert.Equal(1, RailRules.PoseFacing(1, -1));
        Assert.Equal(-1, RailRules.PoseFacing(-1, 1));
        Assert.Equal(-1, RailRules.PoseFacing(0, -1)); // an older server sends no facing: its wagons followed the direction
        Assert.Equal(1, RailRules.PoseFacing(0, 1));
    }
}

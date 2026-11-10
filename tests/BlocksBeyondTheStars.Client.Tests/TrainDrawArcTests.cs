// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2741: the drawn arc of a monorail train between the server's updates bounces off an open line's ends like the server
/// (the old drawing ran on past the end and was pulled back), never jumps, takes the server's direction when a halted
/// train was turned round, and on a loop eases the short way across the seam.
/// </summary>
public sealed class TrainDrawArcTests
{
    private const float Total = 60f;
    private const float Speed = 8f;
    private static readonly float Span = RailRules.TrainSpan(2);

    [Fact]
    public void TheFirstFrame_TakesTheServersArc_RunOnByItsAge()
    {
        var (arc, dir) = TrainDrawArc.Step(false, 0f, 1, 30f, 1, 1, Speed, 0.25f, 1f / 60f, Total, Span, closed: false);
        Assert.Equal(32f, arc, 3);
        Assert.Equal(1, dir);

        // Run on past the end, it has bounced already.
        (arc, dir) = TrainDrawArc.Step(false, 0f, 1, 59f, 1, 1, Speed, 0.25f, 1f / 60f, Total, Span, closed: false);
        Assert.Equal(59f, arc, 3);
        Assert.Equal(-1, dir);
    }

    [Fact]
    public void TheDrawing_FollowsTheServer_ThroughReversals_WithoutAJump()
    {
        // The server runs its train at 30 ticks a second and sends its arc five times a second; the client draws at 60 FPS.
        float serverArc = 40f, msgArc = serverArc;
        int serverDir = 1, msgDir = 1;
        double msgAt = 0, nextMsg = 0.2, serverTime = 0;
        float draw = 0f;
        int drawDir = 1;
        bool has = false;
        int reversals = 0;
        const float frame = 1f / 60f;
        for (int i = 0; i < 60 * 30; i++)
        {
            double now = (i + 1) * frame;
            while (serverTime + (1.0 / 30.0) <= now + 1e-9)
            {
                serverTime += 1.0 / 30.0;
                (serverArc, serverDir) = RailRules.Advance(serverArc, serverDir, 1, Speed / 30f, Total, Span, closed: false);
                if (serverTime >= nextMsg - 1e-9)
                {
                    msgArc = serverArc;
                    msgDir = serverDir;
                    msgAt = serverTime;
                    nextMsg += 0.2;
                }
            }

            var (next, nextDir) = TrainDrawArc.Step(has, draw, drawDir, msgArc, msgDir, 1, Speed, (float)(now - msgAt), frame, Total, Span, closed: false);
            if (has)
            {
                Assert.True(Math.Abs(next - draw) <= (Speed * frame * 1.6f) + 0.02f, $"frame {i}: the drawing jumped from {draw} to {next}");
                Assert.True(Math.Abs(next - serverArc) < 0.6f, $"frame {i}: the drawing ({next}) lags the server ({serverArc})");
                reversals += nextDir != drawDir ? 1 : 0;
            }

            Assert.InRange(next, Span - 1e-3f, Total + 1e-3f);
            draw = next;
            drawDir = nextDir;
            has = true;
        }

        Assert.True(reversals >= 4, $"only {reversals} reversals drawn");
    }

    [Fact]
    public void ThePredictedSpeed_IsTheServersVelocity_AndAnOlderServersTrainRunsAtItsOldFixedSpeed()
    {
        // #3444: a current server sends the speed it runs at (signed like the direction); 0 only while it stands.
        Assert.Equal(12.5f, TrainDrawArc.PredictSpeed(12.5f, halted: false, autopilot: true, setting: 2));
        Assert.Equal(12.5f, TrainDrawArc.PredictSpeed(-12.5f, halted: false, autopilot: true, setting: 2));
        Assert.Equal(RailMotion.StartSpeed, TrainDrawArc.PredictSpeed(RailMotion.StartSpeed, halted: false, autopilot: false, setting: 3));
        Assert.Equal(0f, TrainDrawArc.PredictSpeed(9f, halted: true, autopilot: true, setting: 2));
        Assert.Equal(0f, TrainDrawArc.PredictSpeed(0f, halted: false, autopilot: false, setting: 2)); // a manual train nobody drives
        Assert.Equal(0f, TrainDrawArc.PredictSpeed(float.NaN, halted: false, autopilot: false, setting: 2));

        // An older server sends no velocity: its running autopilot train is drawn at the old table's speed for its setting.
        Assert.Equal(new[] { 4f, 8f, 12f }, TrainDrawArc.LegacySpeedTable);
        Assert.Equal(8f, TrainDrawArc.PredictSpeed(0f, halted: false, autopilot: true, setting: 2));
        Assert.Equal(4f, TrainDrawArc.PredictSpeed(0f, halted: false, autopilot: true, setting: 0));
        Assert.Equal(12f, TrainDrawArc.PredictSpeed(0f, halted: false, autopilot: true, setting: 7));
    }

    [Fact]
    public void TheDrawing_FollowsAnEasingAndBrakingServer_ByTheVelocityItSends_WithoutAJump()
    {
        // #3444: the server runs the speed profile at 15 ticks a second — easing in, cruising at 24, braking into the open
        // line's end, turning round from rest — and sends its arc and signed velocity five times a second.
        const float total = 200f, cap = 24f, frame = 1f / 60f, tick = 1f / 15f;
        float serverArc = 20f, msgArc = serverArc, msgVelocity = RailMotion.StartSpeed, v = 0f;
        int serverDir = 1, msgDir = 1, drawDir = 1, reversals = 0;
        double msgAt = 0, nextMsg = 0.2, serverTime = 0;
        float draw = 0f, fastest = 0f;
        bool has = false;
        for (int i = 0; i < 60 * 40; i++)
        {
            double now = (i + 1) * frame;
            while (serverTime + tick <= now + 1e-9)
            {
                serverTime += tick;
                var (lo, hi) = RailRules.CabRange(total, Span, 1);
                float ahead = serverDir > 0 ? hi - serverArc : serverArc - lo;
                v = RailMotion.NextSpeed(v, RailMotion.Limit(cap, float.PositiveInfinity, ahead, RailMotion.MaxSpeed), tick);
                var (arc, dir) = RailRules.Advance(serverArc, serverDir, 1, v * tick, total, Span, closed: false);
                v = dir != serverDir ? 0f : v; // braked into the end: it sets off the other way from rest
                (serverArc, serverDir) = (arc, dir);
                fastest = Math.Max(fastest, v);
                if (serverTime >= nextMsg - 1e-9)
                {
                    (msgArc, msgDir, msgAt) = (serverArc, serverDir, serverTime);
                    msgVelocity = serverDir * Math.Max(v, RailMotion.StartSpeed);
                    nextMsg += 0.2;
                }
            }

            float speed = TrainDrawArc.PredictSpeed(msgVelocity, halted: false, autopilot: true, setting: 3);
            var (next, nextDir) = TrainDrawArc.Step(has, draw, drawDir, msgArc, msgDir, 1, speed, (float)(now - msgAt), frame, total, Span, closed: false);
            if (has)
            {
                // Against where the server's train is between its ticks (a tick at 24 blocks/s is 1.6 blocks long).
                float truth = serverArc + (serverDir * v * (float)(now - serverTime));
                Assert.True(Math.Abs(next - draw) <= (cap * frame * 1.6f) + 0.02f, $"frame {i}: the drawing jumped from {draw} to {next}");
                Assert.True(Math.Abs(next - truth) < 0.6f, $"frame {i}: the drawing ({next}) lags the server ({truth})");
                reversals += nextDir != drawDir ? 1 : 0;
            }

            draw = next;
            drawDir = nextDir;
            has = true;
        }

        Assert.True(fastest > cap - 0.5f, $"the server never reached its top speed ({fastest})");
        Assert.True(reversals >= 2, $"only {reversals} reversals drawn");
    }

    [Fact]
    public void AHaltedTrainTheServerTurnedRound_TakesTheServersDirection()
    {
        var (arc, dir) = TrainDrawArc.Step(true, 20.3f, 1, 20f, -1, 1, 0f, 0.1f, 1f / 60f, Total, Span, closed: false);
        Assert.Equal(-1, dir);
        Assert.True(arc < 20.3f && arc >= 20f, $"eases toward the halted arc ({arc})");
    }

    [Fact]
    public void ALaggingDrawing_CatchesUpSmoothly()
    {
        float draw = 25f;
        int dir = 1;
        for (int i = 0; i < 120; i++)
        {
            var (next, nextDir) = TrainDrawArc.Step(true, draw, dir, 30f, 1, 1, 0f, 0f, 1f / 60f, Total, Span, closed: false);
            Assert.True(next - draw <= 5f * 4f / 60f + 1e-3f, "one frame closes at most the ease rate's share of the gap");
            Assert.True(next >= draw);
            draw = next;
            dir = nextDir;
        }

        Assert.True(Math.Abs(draw - 30f) < 0.01f, $"caught up ({draw})");
    }

    [Fact]
    public void OnALoop_TheDrawingEasesTheShortWayAcrossTheSeam()
    {
        var (arc, dir) = TrainDrawArc.Step(true, 99f, 1, 1f, 1, 1, 0f, 0f, 1f / 30f, 100f, Span, closed: true);
        Assert.True(arc > 99f || arc < 1f, $"the drawing went the long way round ({arc})");
        Assert.Equal(1, dir);
    }

    [Fact]
    public void TheTrainView_PosesByTheFacing_AndStepsThroughThisHelper()
    {
        // The Unity half, guarded as source text (the Unity scripts cannot load headless).
        string view = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "TrainView.cs"));
        Assert.Contains("RailRules.PoseFacing(t.Facing, t.Direction)", view);
        Assert.Contains("TrainDrawArc.Step(", view);
        Assert.Contains("TrainDrawArc.PredictSpeed(t.Velocity, t.Halted, t.Autopilot, t.Speed)", view); // #3444
        Assert.DoesNotContain("SpeedTable", view);
        Assert.Contains("RailRules.WagonArc(view.DrawArc, i, facing)", view);
        Assert.Contains("spline.PoseAt(arc, facing)", view);
        Assert.DoesNotContain("PoseAt(arc, t.Direction)", view);
    }

    [Fact]
    public void BadInput_NeverPoisonsTheDrawing()
    {
        var (arc, _) = TrainDrawArc.Step(true, float.NaN, 1, 30f, 1, 1, Speed, float.NaN, float.PositiveInfinity, Total, Span, closed: false);
        Assert.Equal(30f, arc, 3);
    }
}

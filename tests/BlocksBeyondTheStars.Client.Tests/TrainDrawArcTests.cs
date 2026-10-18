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

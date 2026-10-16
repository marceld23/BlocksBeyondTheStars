// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2664/#2666/#2670: the planet-entity snapshot interpolator. The old creature smoothing drew a steady walk at 0.17×–2×
/// its real speed twice a second and a stopped animal up to 0.8 blocks past where it stood; the old NPC lerp pulsed
/// 0.6×–1.6× at 5 Hz. These tests pin the replacement: an even drawn speed, no overshoot at a stop, braking starvation,
/// teleports, the world seam and server-side snaps that ease instead of bulging.
/// </summary>
public sealed class MotionInterpolatorTests
{
    private const int Circ = 1000;
    private const double Frame = 1.0 / 60.0;
    private const double Motion = 2.0 / 15.0;   // the creature motion stream: every second 15 Hz tick
    private const double FullList = 8.0 / 15.0; // the 2 Hz creature list: every eighth tick

    [Fact]
    public void NoSnapshots_ReturnsFalse()
    {
        var interp = new MotionInterpolator();
        Assert.False(interp.Sample(1.0, Circ, out _));
    }

    [Fact]
    public void SteadyWalk_OnTheMotionStream_IsDrawnAtAnEvenSpeed()
    {
        const float v = 3f;
        var interp = new MotionInterpolator(Motion);
        var speeds = new List<double>();
        double nextSnap = 0;
        float prevX = float.NaN;
        for (double now = 0; now < 4; now += Frame)
        {
            while (nextSnap <= now)
            {
                interp.Push(nextSnap, new Vector3f(10 + v * (float)nextSnap, 64, 20), new Vector3f(v, 0, 0), true, 0f, 0, Circ);
                nextSnap += Motion;
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            if (now > 1 && !float.IsNaN(prevX))
            {
                speeds.Add((s.Position.X - prevX) / Frame);
            }

            prevX = s.Position.X;
        }

        AssertEven(speeds, v, 0.03);
    }

    [Fact]
    public void SteadyWalk_OnTheTwoHertzListWithoutVelocity_IsDrawnAtAnEvenSpeedOnceTheDelayAdapted()
    {
        // An older server: no velocity on the wire, only the slow full list. The tangents come from the positions and the
        // delay grows to cover the long spacing — the walk must still be even, not stop-and-go.
        const float v = 2f;
        var interp = new MotionInterpolator(0.15);
        var speeds = new List<double>();
        double nextSnap = 0;
        float prevX = float.NaN;
        for (double now = 0; now < 16; now += Frame)
        {
            while (nextSnap <= now)
            {
                interp.Push(nextSnap, new Vector3f(v * (float)nextSnap, 64, 0), Vector3f.Zero, false, 0f, 0, Circ);
                nextSnap += FullList;
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            if (now > 12 && !float.IsNaN(prevX))
            {
                speeds.Add((s.Position.X - prevX) / Frame);
            }

            prevX = s.Position.X;
        }

        Assert.InRange(interp.Delay, 0.6, 0.8);
        AssertEven(speeds, v, 0.05);
    }

    [Fact]
    public void Stop_BetweenSnapshots_NeverOvershootsAndSettlesOnTheRealSpot()
    {
        const float v = 3f;
        const double stopAt = 2.05; // mid-interval, the worst case for the old dead reckoning
        var interp = new MotionInterpolator(Motion);
        double nextSnap = 0;
        float maxX = float.MinValue;
        var last = default(MotionSample);
        for (double now = 0; now < 5; now += Frame)
        {
            while (nextSnap <= now)
            {
                bool moving = nextSnap < stopAt;
                float x = v * (float)Math.Min(nextSnap, stopAt);
                interp.Push(nextSnap, new Vector3f(x, 64, 0), new Vector3f(moving ? v : 0, 0, 0), true, 0f, 0, Circ);
                nextSnap += Motion;
            }

            Assert.True(interp.Sample(now, Circ, out last));
            maxX = Math.Max(maxX, last.Position.X);
        }

        float stopX = v * (float)stopAt;
        Assert.True(maxX <= stopX + 0.01f, $"drawn {maxX:0.000} past the real stop {stopX:0.000}");
        Assert.Equal(stopX, last.Position.X, 3);
        Assert.Equal(0f, last.Velocity.X, 3);
    }

    [Fact]
    public void Starvation_BrakesToAStop_AndRecoversWithoutASnap()
    {
        const float v = 3f;
        var interp = new MotionInterpolator(Motion);
        double nextSnap = 0;
        float prevX = float.NaN;
        float newestX = 0;
        double maxStep = 0;
        for (double now = 0; now < 4; now += Frame)
        {
            // The stream drops out between 1.5 s and 2.1 s; the animal kept walking on the server meanwhile.
            while (nextSnap <= now)
            {
                if (nextSnap < 1.5 || nextSnap > 2.1)
                {
                    newestX = v * (float)nextSnap;
                    interp.Push(nextSnap, new Vector3f(newestX, 64, 0), new Vector3f(v, 0, 0), true, 0f, 0, Circ);
                }

                nextSnap += Motion;
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            if (s.Extrapolating)
            {
                // The coast is short and braking: never more than half the coast window's distance past the newest word.
                Assert.True(s.Position.X <= newestX + v * MotionInterpolator.MaxExtrapolation * 0.5 + 0.01,
                    $"coasted to {s.Position.X:0.000}, newest {newestX:0.000}");
            }

            if (!float.IsNaN(prevX))
            {
                maxStep = Math.Max(maxStep, Math.Abs(s.Position.X - prevX));
            }

            prevX = s.Position.X;
        }

        // Catching up after the gap fades in — no frame jumps more than a few frames' worth of walking.
        Assert.True(maxStep < v * Frame * 4, $"largest per-frame step {maxStep:0.000}");
    }

    [Fact]
    public void ALateSnapshot_DuringStarvation_FadesIn_NoPop()
    {
        // A latency spike longer than the delay: the next snapshot arrives already behind the render time. The body was
        // coasting; the new coast starts somewhere else — that jump must fade, not snap.
        const float v = 3f;
        var interp = new MotionInterpolator(Motion);
        double nextSnap = 0;
        float prevX = float.NaN;
        double maxStep = 0;
        for (double now = 0; now < 3; now += Frame)
        {
            while (nextSnap <= now && nextSnap < 1.0)
            {
                interp.Push(nextSnap, new Vector3f(v * (float)nextSnap, 64, 0), new Vector3f(v, 0, 0), true, 0f, 0, Circ);
                nextSnap += Motion;
            }

            if (Math.Abs(now - 1.8) < Frame / 2)
            {
                // Arrives at 1.8 s, stamped 1.5 s — already older than the render time (~1.58 s): still starved, but the
                // coast now starts from a different place.
                interp.Push(1.5, new Vector3f(v * 1.5f, 64, 0), new Vector3f(v, 0, 0), true, 0f, 0, Circ);
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            if (!float.IsNaN(prevX))
            {
                maxStep = Math.Max(maxStep, Math.Abs(s.Position.X - prevX));
            }

            prevX = s.Position.X;
        }

        Assert.True(maxStep < v * Frame * 6, $"largest per-frame step {maxStep:0.000}");
    }

    [Fact]
    public void Teleport_ResetsTheBuffer_InsteadOfSweepingAcross()
    {
        var interp = new MotionInterpolator(Motion);
        interp.Push(0.0, new Vector3f(100, 64, 100), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(Motion, new Vector3f(100, 64, 100), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(2 * Motion, new Vector3f(130, 64, 100), Vector3f.Zero, true, 0f, 0, Circ); // a leash teleport

        Assert.Equal(1, interp.Count);
        Assert.True(interp.Sample(0.5, Circ, out var s));
        Assert.Equal(130f, s.Position.X, 3);
    }

    [Fact]
    public void CrossingTheSeam_TakesTheShortWay()
    {
        const float v = 3f;
        var interp = new MotionInterpolator(Motion);
        double nextSnap = 0;
        for (double now = 0; now < 2; now += Frame)
        {
            while (nextSnap <= now)
            {
                double x = WorldConstants.WrapX(Circ - 2 + v * nextSnap, Circ);
                interp.Push(nextSnap, new Vector3f((float)x, 64, 0), new Vector3f(v, 0, 0), true, 0f, 0, Circ);
                nextSnap += Motion;
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            if (now > 0.5)
            {
                double expected = Circ - 2 + v * (now - interp.Delay);
                double err = WorldConstants.WrapDeltaX(s.Position.X - expected, Circ);
                Assert.True(Math.Abs(err) < 0.05, $"at {now:0.00}s drawn {s.Position.X:0.000}, expected ~{WorldConstants.WrapX(expected, Circ):0.000}");
            }
        }
    }

    [Fact]
    public void ServerSideStepUp_EasesMonotonically_NoBulge()
    {
        // An NPC steps up one block within a single server tick. The server's horizontal velocity is sent, the vertical
        // tangent is zero — the drawn Y must climb smoothly and never pass the step.
        var interp = new MotionInterpolator(0.2);
        double nextSnap = 0;
        float prevY = 64f;
        float last = 64f;
        for (double now = 0; now < 3; now += Frame)
        {
            while (nextSnap <= now)
            {
                float y = nextSnap < 1.0 ? 64f : 65f;
                interp.Push(nextSnap, new Vector3f(0.7f * (float)nextSnap, y, 0), new Vector3f(0.7f, 0, 0), true, 0f, 0, Circ);
                nextSnap += 0.2;
            }

            Assert.True(interp.Sample(now, Circ, out var s));
            Assert.InRange(s.Position.Y, 64f - 1e-4f, 65f + 1e-4f);
            Assert.True(s.Position.Y >= prevY - 1e-4f, "the climb went back down");
            prevY = s.Position.Y;
            last = s.Position.Y;
        }

        Assert.Equal(65f, last, 3);
    }

    [Fact]
    public void ServerVelocitySpike_IsHeldToTheSegment_NoBulge()
    {
        // A creature snapped up a block within one tick: the per-tick velocity it reports is 15 blocks/s upward. The tangent
        // limit keeps the following, level segment from bulging above the ledge.
        var interp = new MotionInterpolator(Motion);
        interp.Push(0, new Vector3f(0, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(Motion, new Vector3f(0.1f, 65, 0), new Vector3f(0.75f, 15f, 0), true, 0f, 0, Circ);
        interp.Push(2 * Motion, new Vector3f(0.2f, 65, 0), new Vector3f(0.75f, 0, 0), true, 0f, 0, Circ);
        interp.Push(3 * Motion, new Vector3f(0.3f, 65, 0), new Vector3f(0.75f, 0, 0), true, 0f, 0, Circ);
        for (double now = 0; now < 1; now += Frame)
        {
            Assert.True(interp.Sample(now, Circ, out var s));
            Assert.True(s.Position.Y <= 65.0f + 0.03f, $"bulged to {s.Position.Y:0.000}");
        }
    }

    [Fact]
    public void SameStamp_ReplacesTheNewest()
    {
        var interp = new MotionInterpolator(Motion);
        interp.Push(0, new Vector3f(0, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(Motion, new Vector3f(0.4f, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(Motion, new Vector3f(1, 64, 0), Vector3f.Zero, true, 0f, 3, Circ); // the full list, same server tick
        Assert.Equal(2, interp.Count);
        Assert.True(interp.TryNewest(out var p));
        Assert.Equal(1f, p.X);
    }

    [Fact]
    public void LongSilenceThenMove_StartsPromptly_NotSmearedAcrossTheSilence()
    {
        var interp = new MotionInterpolator(Motion);
        interp.Push(0, new Vector3f(0, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(Motion, new Vector3f(0, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Sample(Motion, Circ, out _);

        // Three seconds of silence (out of range, a keep-alive gap), then it is walking a block further on.
        interp.Push(3.0, new Vector3f(1, 64, 0), new Vector3f(3, 0, 0), true, 0f, 0, Circ);
        Assert.True(interp.Sample(3.0 - Motion, Circ, out var before));
        Assert.Equal(0f, before.Position.X, 2); // still standing a beat before the move — not drifting since t=0.13
    }

    [Fact]
    public void Delay_AdaptsToTheStreamRate_AndNeverJumps()
    {
        var interp = new MotionInterpolator(Motion);
        double nextSnap = 0;
        double prevDelay = double.NaN;
        for (double now = 0; now < 14; now += Frame)
        {
            double spacing = now < 4 ? Motion : FullList; // walks out of the motion stream's radius at t=4
            while (nextSnap <= now)
            {
                interp.Push(nextSnap, new Vector3f((float)nextSnap, 64, 0), new Vector3f(1, 0, 0), true, 0f, 0, Circ);
                nextSnap += spacing;
            }

            Assert.True(interp.Sample(now, Circ, out _));
            if (!double.IsNaN(prevDelay))
            {
                Assert.True(Math.Abs(interp.Delay - prevDelay) <= (MotionInterpolator.DelaySlewUp * Frame) + 1e-9);
            }

            prevDelay = interp.Delay;
            if (Math.Abs(now - 3.9) < Frame / 2)
            {
                Assert.InRange(interp.Delay, 0.18, 0.24); // ~0.2 s on the 7.5 Hz stream
            }
        }

        Assert.InRange(interp.Delay, 0.6, 0.8); // ~0.7 s once only the 2 Hz list reaches it
    }

    [Fact]
    public void Yaw_TurnsTheShortWayRound()
    {
        var interp = new MotionInterpolator(0.1, minDelay: 0.1, maxDelay: 0.1);
        interp.Push(0, new Vector3f(0, 64, 0), Vector3f.Zero, true, 3.0f, 0, Circ);
        interp.Push(0.1, new Vector3f(0, 64, 0), Vector3f.Zero, true, -3.0f, 0, Circ);
        Assert.True(interp.Sample(0.15, Circ, out var s)); // halfway
        // 3.0 → −3.0 the short way passes π, not 0.
        Assert.True(Math.Abs(Math.Cos(s.Yaw) + 1) < 0.01, $"yaw {s.Yaw:0.000}");
    }

    [Fact]
    public void Flags_FollowTheSnapshotTheRenderTimeIsIn()
    {
        var interp = new MotionInterpolator(0.1, minDelay: 0.1, maxDelay: 0.1);
        interp.Push(0.0, new Vector3f(0, 64, 0), Vector3f.Zero, true, 0f, 0, Circ);
        interp.Push(0.1, new Vector3f(0, 64.5f, 0), new Vector3f(0, 4, 0), true, 0f, 1, Circ); // took off (airborne bit)
        interp.Push(0.2, new Vector3f(0, 64.8f, 0), new Vector3f(0, 2, 0), true, 0f, 1, Circ);

        Assert.True(interp.Sample(0.15, Circ, out var before)); // render 0.05: still in the grounded span
        Assert.Equal(0, before.Flags);
        Assert.True(interp.Sample(0.25, Circ, out var after)); // render 0.15: in the airborne span
        Assert.Equal(1, after.Flags);
    }

    private static void AssertEven(List<double> speeds, double expected, double tolerance)
    {
        Assert.NotEmpty(speeds);
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        foreach (double s in speeds)
        {
            min = Math.Min(min, s);
            max = Math.Max(max, s);
            sum += s;
        }

        double mean = sum / speeds.Count;
        Assert.True(Math.Abs(mean - expected) < expected * 0.05, $"mean drawn speed {mean:0.000}, expected {expected}");
        Assert.True(max / min < 1 + (2 * tolerance), $"drawn speed swings {min:0.000}..{max:0.000} (ratio {max / min:0.000})");
    }
}

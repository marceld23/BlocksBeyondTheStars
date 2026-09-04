// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #1530: presence arrives on change + a 0.5 s keep-alive instead of a fixed 10 Hz. The interpolator must treat a
/// repeated pose after a gap as a keep-alive (no new point — the avatar keeps standing) and a new pose after a gap
/// as "just started moving" (the motion begins within one nominal beat, not smeared across the silent stretch).
/// </summary>
public class RemoteEntityInterpolatorGapTests
{
    private const int Circ = 6000;

    [Fact]
    public void KeepAlive_RepeatingThePose_DoesNotBecomeANewSample()
    {
        var interp = new RemoteEntityInterpolator(0.15);
        var pos = new Vector3f(10, 64, 10);
        interp.Push(0.0, pos, 1f);
        interp.Push(0.5, pos, 1f); // keep-alive
        interp.Push(1.0, pos, 1f); // keep-alive

        Assert.True(interp.Sample(1.0 + 0.15, Circ, out var p, out var yaw));
        Assert.Equal(pos.X, p.X);
        Assert.Equal(pos.Z, p.Z);
        Assert.Equal(1f, yaw);
    }

    [Fact]
    public void MotionAfterAGap_StartsWithinOneBeat_NotAcrossTheWholeGap()
    {
        var interp = new RemoteEntityInterpolator(0.15);
        var start = new Vector3f(10, 64, 10);
        interp.Push(0.0, start, 0f);
        interp.Push(0.5, start, 0f);                       // keep-alive at 0.5 s
        interp.Push(0.95, new Vector3f(11, 64, 10), 0f);   // first packet of a walk, 0.45 s after the keep-alive

        // Rendering 0.15 s behind the newest packet: at t = 0.95 + 0.075 the render time (0.875) lies INSIDE the
        // synthetic bridge (0.85 → 0.95), so the avatar is already on its way — halfway, not still at the start.
        Assert.True(interp.Sample(0.95 + 0.075, Circ, out var mid, out _));
        Assert.InRange(mid.X, 10.2f, 10.8f);

        // ...and a moment before that (render time 0.80) it was still standing at the start.
        Assert.True(interp.Sample(0.95, Circ, out var before, out _));
        Assert.Equal(10f, before.X);
    }

    [Fact]
    public void NormalBeat_IsUntouched()
    {
        var interp = new RemoteEntityInterpolator(0.15);
        interp.Push(0.0, new Vector3f(0, 64, 0), 0f);
        interp.Push(0.1, new Vector3f(1, 64, 0), 0f);
        interp.Push(0.2, new Vector3f(2, 64, 0), 0f);

        Assert.True(interp.Sample(0.35, Circ, out var atRender, out _)); // render time 0.2 → the newest sample
        Assert.Equal(2f, atRender.X);
        Assert.True(interp.Sample(0.30, Circ, out var between, out _)); // render time 0.15 → halfway 1 → 2
        Assert.InRange(between.X, 1.4f, 1.6f);
    }
}

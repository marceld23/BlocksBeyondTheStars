// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless tests for the remote-entity snapshot interpolation buffer (B Tier1b). Pure logic — no Unity — so
/// the seam-aware interpolation, the starvation hold and the render delay can be asserted deterministically.
/// </summary>
public sealed class RemoteEntityInterpolatorTests
{
    private const int Circ = 1000;

    [Fact]
    public void NoSamples_ReturnsFalse()
    {
        var interp = new RemoteEntityInterpolator(0.1);
        Assert.False(interp.Sample(1.0, Circ, out _, out _));
    }

    [Fact]
    public void SingleSample_ReturnsThatPose()
    {
        var interp = new RemoteEntityInterpolator(0.1);
        interp.Push(0.0, new Vector3f(3, 64, 7), 42f);

        Assert.True(interp.Sample(5.0, Circ, out var pos, out var yaw));
        Assert.Equal(3f, pos.X, 3);
        Assert.Equal(7f, pos.Z, 3);
        Assert.Equal(42f, yaw, 3);
    }

    [Fact]
    public void TwoSamples_InterpolatesAtTheDelayedRenderTime()
    {
        var interp = new RemoteEntityInterpolator(0.1);
        interp.Push(0.0, new Vector3f(0, 64, 0), 0f);
        interp.Push(0.1, new Vector3f(10, 64, 0), 90f);

        // now - delay = 0.15 - 0.1 = 0.05 → halfway between the two snapshots.
        Assert.True(interp.Sample(0.15, Circ, out var pos, out var yaw));
        Assert.Equal(5f, pos.X, 2);
        Assert.Equal(45f, yaw, 2);
    }

    [Fact]
    public void RenderTimeAheadOfNewest_HoldsNewest_NoExtrapolation()
    {
        var interp = new RemoteEntityInterpolator(0.1);
        interp.Push(0.0, new Vector3f(0, 64, 0), 0f);
        interp.Push(0.1, new Vector3f(10, 64, 0), 0f);

        // now - delay = 0.5 - 0.1 = 0.4, well past the newest sample (0.1) → hold it, do not run past x=10.
        Assert.True(interp.Sample(0.5, Circ, out var pos, out _));
        Assert.Equal(10f, pos.X, 3);
    }

    [Fact]
    public void Interpolation_TakesShortestPathAcrossTheSeam()
    {
        var interp = new RemoteEntityInterpolator(0.1);
        // 995 → 5 on a circumference-1000 world is a +10 step across the seam, not a -990 sweep.
        interp.Push(0.0, new Vector3f(995, 64, 0), 0f);
        interp.Push(0.1, new Vector3f(5, 64, 0), 0f);

        // Halfway across the seam lands on x = 0 (≡ 1000), NOT x = 500 (the long way round).
        Assert.True(interp.Sample(0.15, Circ, out var pos, out _));
        float wrapped = pos.X % Circ;
        float distFromSeam = System.Math.Min(wrapped, Circ - wrapped); // distance to 0 either way
        Assert.True(distFromSeam < 1f, $"expected x≈0 (short path across seam), got {pos.X}");
    }
}

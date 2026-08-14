// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Render-side sight march for ranged attack effects (#1004): a scan-drone hovering outside a cave must
/// not visibly fire its laser at a player behind solid rock (the server's damage is sight-gated, so the
/// shot was always a lie), while a clear line — including one only broken by the endpoints' own cells —
/// keeps firing.
/// </summary>
public sealed class SightLineTests
{
    [Fact]
    public void Clear_OpenAir_IsClear()
    {
        static bool Nothing(int x, int y, int z) => false;
        Assert.True(SightLine.Clear(Nothing, 0f, 5f, 0f, 14f, 8f, 3f));
    }

    [Fact]
    public void Clear_WallBetween_IsBlocked()
    {
        // A one-block-thick wall at x == 6 spans the segment at every height: the cave scenario.
        static bool Wall(int x, int y, int z) => x == 6;
        Assert.False(SightLine.Clear(Wall, 0f, 5f, 0f, 14f, 5f, 0f));
    }

    [Fact]
    public void Clear_PointBlank_SkipsTheEndpointSamples()
    {
        // At point-blank range (one march step or less) the only samples are the endpoints themselves —
        // both skipped, since the shooter's and the target's own bodies aren't occluders.
        static bool Everything(int x, int y, int z) => true;
        Assert.True(SightLine.Clear(Everything, 0.9f, 2.5f, 0.5f, 1.1f, 2.5f, 0.5f));
    }

    [Fact]
    public void Clear_DiagonalThroughCeiling_IsBlocked()
    {
        // A hovering drone above a roofed player: a horizontal slab at y == 4 cuts the steep segment.
        static bool Slab(int x, int y, int z) => y == 4;
        Assert.False(SightLine.Clear(Slab, 2f, 9f, 2f, 3f, 1.5f, 4f));
    }

    [Fact]
    public void Clear_ZeroLengthSegment_IsClear()
    {
        // Degenerate but must not throw or scan anything: shooter and target in the same spot.
        static bool Everything(int x, int y, int z) => true;
        Assert.True(SightLine.Clear(Everything, 3f, 3f, 3f, 3f, 3f, 3f));
    }
}

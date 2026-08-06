// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Terrain probing for the staged prologue orbit (#777): a ship parked against a mountainside must not
/// stage the camera inside the slope, unloaded chunks must read as blocked (never as open space), and
/// the widest-arc search must handle wrap-around and fully-clear rings.
/// </summary>
public sealed class CinematicStageScanTests
{
    /// <summary>Flat ground: everything at y &lt; 0 is solid, the rest is loaded air.</summary>
    private static bool FlatGround(int x, int y, int z) => y >= 0;

    [Fact]
    public void PathClear_OpenAir_IsClear()
    {
        Assert.True(CinematicStageScan.PathClear(FlatGround, 0f, 2f, 0f, 16f, 9f, 16f));
    }

    [Fact]
    public void PathClear_ThroughAHill_IsBlocked()
    {
        // A wall occupying x in [5, 8] at all heights sits between the look target and the camera.
        bool Wall(int x, int y, int z) => y >= 0 && (x < 5 || x > 8);
        Assert.False(CinematicStageScan.PathClear(Wall, 0f, 2f, 0f, 16f, 9f, 0f));
    }

    [Fact]
    public void PathClear_BuriedLookTarget_IsBlocked()
    {
        // The start cell itself is solid — a look target inside terrain can never stage a shot.
        Assert.False(CinematicStageScan.PathClear(FlatGround, 0f, -2f, 0f, 0f, 9f, 16f));
    }

    [Fact]
    public void SpotClear_AllowsSolidGroundBelow_ButNotBeside()
    {
        // Hovering one block above flat ground is fine (the cell below may be solid) ...
        Assert.True(CinematicStageScan.SpotClear(FlatGround, 8.5f, 1.5f, 8.5f));

        // ... but a wall in a horizontal neighbour cell violates the near-plane clearance.
        bool WallAtX2(int x, int y, int z) => y >= 0 && x != 2;
        Assert.False(CinematicStageScan.SpotClear(WallAtX2, 1.5f, 1.5f, 0.5f));

        // A solid ceiling directly above blocks too.
        bool Ceiling(int x, int y, int z) => y >= 0 && y != 3;
        Assert.False(CinematicStageScan.SpotClear(Ceiling, 0.5f, 2.5f, 0.5f));
    }

    [Fact]
    public void UnloadedChunks_ReadAsBlocked()
    {
        // A sampler that knows nothing (nothing streamed yet) must stage nothing.
        static bool Unknown(int x, int y, int z) => false;
        Assert.False(CinematicStageScan.PathClear(Unknown, 0f, 2f, 0f, 4f, 2f, 0f));
        Assert.False(CinematicStageScan.SpotClear(Unknown, 0f, 2f, 0f));

        var ring = CinematicStageScan.ScanRing(Unknown, 0f, 0f, 0f, 2f, 16f, 7f, 72);
        Assert.False(CinematicStageScan.TryFindWidestClearArc(ring, 60f, out _, out _));
    }

    [Fact]
    public void ScanRing_FlatGround_IsFullyClear()
    {
        var ring = CinematicStageScan.ScanRing(FlatGround, 0.5f, 0.5f, 0.5f, 2f, 16f, 7f, 72);
        Assert.All(ring, r => Assert.True(r));

        Assert.True(CinematicStageScan.TryFindWidestClearArc(ring, 110f, out _, out float sweep));
        Assert.Equal(360f, sweep, 1);
    }

    [Fact]
    public void ScanRing_Mountainside_LeavesOnlyTheOpenArc()
    {
        // A mountain wall east of the ship: solid for x > 6 at every height. With the orbit convention
        // pos = center + (sin·r, h, cos·r), east (+x) is angle 90° — that side must scan blocked.
        bool Mountain(int x, int y, int z) => y >= 0 && x <= 6;

        var ring = CinematicStageScan.ScanRing(Mountain, 0.5f, 0.5f, 0.5f, 2f, 16f, 7f, 72);
        Assert.False(ring[18]); // 90°: camera inside the mountain
        Assert.True(ring[54]);  // 270°: open side

        Assert.True(CinematicStageScan.TryFindWidestClearArc(ring, 110f, out float center, out float sweep));
        Assert.True(sweep < 360f);
        // The open arc must face away from the mountain (west, around 270°).
        float delta = Math.Abs(((center - 270f) % 360f + 540f) % 360f - 180f);
        Assert.True(delta < 30f, $"arc center {center}° should face ~270°");
    }

    [Fact]
    public void TryFindWidestClearArc_HandlesWrapAround()
    {
        // Clear samples wrap 350°..360°..40°: one 60° run crossing zero, plus a narrow decoy at 180°.
        var ring = new bool[36];
        for (int i = 35; i < 36 + 4; i++)
        {
            ring[i % 36] = true;
        }

        ring[18] = true;

        Assert.True(CinematicStageScan.TryFindWidestClearArc(ring, 40f, out float center, out float sweep));
        Assert.Equal(50f, sweep, 1);
        // Run covers samples 35..39 (350°..30°) → its centre sits at 10°.
        Assert.Equal(10f, center, 1);
    }

    [Fact]
    public void TryFindWidestClearArc_BelowMinimumSweep_Fails()
    {
        var ring = new bool[36];
        ring[0] = ring[1] = ring[2] = true; // 30° of open sky

        Assert.False(CinematicStageScan.TryFindWidestClearArc(ring, 110f, out _, out float sweep));
        Assert.Equal(30f, sweep, 1); // the sweep is still reported for logging
    }

    [Fact]
    public void TryFindWidestClearArc_EmptyOrAllBlocked_Fails()
    {
        Assert.False(CinematicStageScan.TryFindWidestClearArc(Array.Empty<bool>(), 10f, out _, out _));
        Assert.False(CinematicStageScan.TryFindWidestClearArc(new bool[12], 10f, out _, out _));
    }
}

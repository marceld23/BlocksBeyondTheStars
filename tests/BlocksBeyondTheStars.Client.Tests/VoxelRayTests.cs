// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3058: the voxel walk that turns another player's shot (only from/to arrive) into a struck cell and face.</summary>
public sealed class VoxelRayTests
{
    private static bool Ground(int x, int y, int z) => y <= 0;

    private static bool WallAtFive(int x, int y, int z) => x == 5;

    [Fact]
    public void AShotStraightDown_HitsTheGroundsTopFace()
    {
        Assert.True(VoxelRay.Cast(Ground, 0.5f, 4.5f, 0.5f, 0.5f, -2f, 0.5f, out var hit));

        Assert.Equal((0, 0, 0), (hit.X, hit.Y, hit.Z));
        Assert.Equal(2, hit.Face); // entered through its +Y face
        Assert.Equal(3.5f, hit.Distance, 4);
    }

    [Fact]
    public void AShotAlongX_EntersTheWallThroughItsNearSide()
    {
        Assert.True(VoxelRay.Cast(WallAtFive, 1.25f, 2.5f, 0.5f, 9f, 2.5f, 0.5f, out var hit));

        Assert.Equal((5, 2, 0), (hit.X, hit.Y, hit.Z));
        Assert.Equal(1, hit.Face); // −X face, turned back toward the shooter
        Assert.Equal(3.75f, hit.Distance, 4);
    }

    [Fact]
    public void AShotTowardMinusX_EntersThroughThePlusXSide()
    {
        Assert.True(VoxelRay.Cast(WallAtFive, 8.5f, 2.5f, 0.5f, 0f, 2.5f, 0.5f, out var hit));

        Assert.Equal(5, hit.X);
        Assert.Equal(0, hit.Face);
        Assert.Equal(2.5f, hit.Distance, 4);
    }

    [Fact]
    public void ADiagonalShot_ReportsTheFaceItActuallyCrossed()
    {
        // Falling steeply onto flat ground while moving along +X: the last boundary crossed is the ground's top, not a side.
        Assert.True(VoxelRay.Cast(Ground, 0.2f, 3.5f, 0.5f, 3.2f, -1.5f, 0.5f, out var hit));

        Assert.Equal(2, hit.Face);
        Assert.Equal(0, hit.Y);
        // The entry point lies on the plane y = 1.
        float t = hit.Distance / (float)Math.Sqrt(3f * 3f + 5f * 5f);
        Assert.Equal(1f, 3.5f - 5f * t, 3);
    }

    [Fact]
    public void TheShootersOwnCell_IsNeverTheHit()
    {
        // The start cell is solid (a muzzle poking into a wall); the next solid cell along the ray is reported instead.
        static bool Cell(int x, int y, int z) => x == 0 || x == 3;

        Assert.True(VoxelRay.Cast(Cell, 0.5f, 0.5f, 0.5f, 6f, 0.5f, 0.5f, out var hit));
        Assert.Equal(3, hit.X);
    }

    [Fact]
    public void ASegmentThatStopsShortOfTheWall_HitsNothing()
    {
        Assert.False(VoxelRay.Cast(WallAtFive, 1f, 2.5f, 0.5f, 4.9f, 2.5f, 0.5f, out _));
        Assert.False(VoxelRay.Cast(WallAtFive, 1f, 2.5f, 0.5f, 1f, 2.5f, 0.5f, out _)); // zero length
    }
}

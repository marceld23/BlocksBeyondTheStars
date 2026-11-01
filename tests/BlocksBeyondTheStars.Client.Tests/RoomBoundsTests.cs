// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3057: the room scan behind the High preset's room reflections.</summary>
public sealed class RoomBoundsTests
{
    /// <summary>A world of walls with the given open cells; everything outside <paramref name="known"/> is unknown.</summary>
    private static System.Func<int, int, int, RoomCell> World(HashSet<(int, int, int)> open, int known = 100)
        => (x, y, z) =>
        {
            if (System.Math.Abs(x) > known || System.Math.Abs(y) > known || System.Math.Abs(z) > known)
            {
                return RoomCell.Unknown;
            }

            return open.Contains((x, y, z)) ? RoomCell.Open : RoomCell.Wall;
        };

    private static HashSet<(int, int, int)> Box(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        var cells = new HashSet<(int, int, int)>();
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    cells.Add((x, y, z));
                }
            }
        }

        return cells;
    }

    [Fact]
    public void ClosedRoom_YieldsItsBox()
    {
        var room = Box(0, 0, 0, 5, 2, 3);
        Assert.True(RoomBounds.TryFind(2, 1, 1, World(room), out var box));
        Assert.Equal(new RoomBox(0, 0, 0, 5, 2, 3), box);
        Assert.True(box.Contains(5, 2, 3));
        Assert.False(box.Contains(6, 2, 3));
        Assert.True(box.Contains(6, 2, 3, margin: 1));
    }

    [Fact]
    public void LShapedRoom_YieldsTheBoxAroundBothWings()
    {
        var room = Box(0, 0, 0, 5, 2, 1);
        room.UnionWith(Box(4, 0, 2, 5, 2, 6));
        Assert.True(RoomBounds.TryFind(0, 0, 0, World(room), out var box));
        Assert.Equal(new RoomBox(0, 0, 0, 5, 2, 6), box);
    }

    [Fact]
    public void AnOpenDoorToTheOutdoors_IsNoRoom()
    {
        var room = Box(0, 0, 0, 5, 2, 3);
        room.UnionWith(Box(-60, 0, 0, -1, 0, 0)); // a long open corridor out of the box: more than 40 blocks long
        Assert.False(RoomBounds.TryFind(2, 1, 1, World(room), out _));
    }

    [Fact]
    public void AHugeHall_OverflowsTheCellCap()
    {
        var hall = Box(0, 0, 0, 30, 6, 30); // 31 × 7 × 31 > 4096 cells, every side under 40
        Assert.False(RoomBounds.TryFind(1, 1, 1, World(hall), out _));
    }

    [Fact]
    public void ARoomWithAnOpenDoor_IsFoundNearby_WithinTheSearchCube()
    {
        var room = Box(0, 0, 0, 5, 2, 3);
        room.UnionWith(Box(-60, 0, 0, -1, 0, 0)); // the open doorway leads into a long corridor
        Assert.False(RoomBounds.TryFind(2, 1, 1, World(room), out _));
        Assert.True(RoomBounds.TryFindNear(2, 1, 1, 12, World(room), out var box));
        Assert.Equal(new RoomBox(-10, 0, 0, 5, 2, 3), box); // the corridor is cut at the cube's edge (2 − 12)
    }

    [Fact]
    public void UnknownCells_AndAWallStart_AreNoRoom()
    {
        var room = Box(0, 0, 0, 5, 2, 3);
        Assert.False(RoomBounds.TryFind(2, 1, 1, World(room, known: 3), out _));
        Assert.False(RoomBounds.TryFind(9, 9, 9, World(room), out _));
        Assert.False(RoomBounds.TryFind(0, 0, 0, null!, out _));
    }
}

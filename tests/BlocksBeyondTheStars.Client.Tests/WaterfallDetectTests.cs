// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's own reading of "falling water" (#1745). A builder lined the top of a wall with waterfall
/// blocks and saw one water cell under each; the sheet below was there on the server and invisible on the
/// client, because a column with water on both sides had only one open side and the old rule wanted two.
/// </summary>
public sealed class WaterfallDetectTests
{
    private static readonly BlockId Water = new(7);
    private static readonly BlockId Stone = new(1);

    private sealed class Grid
    {
        private readonly Dictionary<Vector3i, BlockId> _cells = new();

        public void Set(int x, int y, int z, BlockId id) => _cells[new Vector3i(x, y, z)] = id;

        public BlockId At(int x, int y, int z) => _cells.TryGetValue(new Vector3i(x, y, z), out var id) ? id : BlockId.Air;

        public bool Falling(int x, int y, int z) => WaterfallDetect.IsFalling(At, Water, x, y, z);
    }

    /// <summary>A wall along z = -1, a water column hanging in front of it at (x, 0..4, 0).</summary>
    private static Grid ColumnsAgainstAWall(int fromX, int toX)
    {
        var g = new Grid();
        for (int x = fromX - 2; x <= toX + 2; x++)
        {
            for (int y = -1; y <= 6; y++)
            {
                g.Set(x, y, -1, Stone);
            }
        }

        for (int x = fromX; x <= toX; x++)
        {
            for (int y = 0; y <= 4; y++)
            {
                g.Set(x, y, 0, Water);
            }
        }

        return g;
    }

    [Fact]
    public void ALoneColumnAgainstAWall_IsFalling()
    {
        var g = ColumnsAgainstAWall(0, 0);
        Assert.True(g.Falling(0, 2, 0));
    }

    [Fact]
    public void TheTopCellOfAColumn_IsNotFalling()
    {
        // Nothing above it is water: it is the surface of the column, and draws as such.
        var g = ColumnsAgainstAWall(0, 0);
        Assert.False(g.Falling(0, 4, 0));
    }

    [Fact]
    public void EveryColumnOfACurtain_IsFalling()
    {
        // Eight columns side by side: each has water left and right, the wall behind, air only in front.
        var g = ColumnsAgainstAWall(0, 7);
        for (int x = 0; x <= 7; x++)
        {
            Assert.True(g.Falling(x, 2, 0), $"column {x} must read as falling");
        }
    }

    [Fact]
    public void ACurtainAlongAZWall_IsFallingToo()
    {
        var g = new Grid();
        for (int z = -2; z <= 9; z++)
        {
            for (int y = -1; y <= 6; y++)
            {
                g.Set(-1, y, z, Stone); // wall along x = -1
            }
        }

        for (int z = 0; z <= 7; z++)
        {
            for (int y = 0; y <= 4; y++)
            {
                g.Set(0, y, z, Water);
            }
        }

        Assert.True(g.Falling(0, 1, 3));
        Assert.True(g.Falling(0, 1, 0));
        Assert.True(g.Falling(0, 1, 7));
    }

    [Fact]
    public void ASubmergedPoolCell_IsNotFalling()
    {
        var g = new Grid();
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
                for (int y = 0; y < 3; y++)
                {
                    g.Set(x, y, z, Water);
                }

        Assert.False(g.Falling(2, 1, 2), "the middle of a deep pool has water all round");
    }

    [Fact]
    public void ADeepChannelBetweenBanks_IsNotFalling()
    {
        var g = new Grid();
        for (int z = 0; z < 6; z++)
        {
            for (int y = 0; y < 3; y++)
            {
                g.Set(-1, y, z, Stone);
                g.Set(1, y, z, Stone);
                g.Set(0, y, z, Water);
            }
        }

        Assert.False(g.Falling(0, 1, 3), "no side of a channel cell is open to the air");
    }

    [Fact]
    public void APoolCellWithOneOpenSide_IsNotFalling_WhenTheShoreBesideItIsClosed()
    {
        // A deep pool whose bank has a one-cell gap below the surface: the cell at the gap has one open side,
        // but its neighbours along the shore are shut in by the bank — that is a pool with a hole, not a sheet.
        var g = new Grid();
        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                for (int y = 0; y < 3; y++)
                {
                    g.Set(x, y, z, Water);
                }

        for (int z = -1; z <= 4; z++)
        {
            for (int y = 0; y < 3; y++)
            {
                g.Set(4, y, z, Stone); // the east bank …
            }
        }

        g.Set(4, 1, 2, BlockId.Air); // … with one gap at depth
        Assert.False(g.Falling(3, 1, 2));
    }

    [Fact]
    public void ImpactDrop_CountsTheColumnAboveTheLandingCell()
    {
        var g = ColumnsAgainstAWall(0, 0);
        g.Set(0, -1, 0, Stone); // the floor the column lands on
        Assert.Equal(4, WaterfallDetect.ImpactDrop(g.At, Water, 0, -1, 0, 16));
    }
}

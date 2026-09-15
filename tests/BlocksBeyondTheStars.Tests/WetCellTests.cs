// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1902: a plant, a ladder or a building form standing in water deletes the water in its cell. The shared rule
/// decides when such a cell still counts as under water — for the oxygen drain, the underwater wash and the mesher.
/// </summary>
public sealed class WetCellTests
{
    private const ushort Water = 7;
    private const ushort Stone = 1;
    private const ushort Kelp = 9;

    private sealed class Grid
    {
        private readonly Dictionary<(int, int, int), ushort> _cells = new();

        public Grid Set(int x, int y, int z, ushort id)
        {
            _cells[(x, y, z)] = id;
            return this;
        }

        public ushort At(int x, int y, int z) => _cells.TryGetValue((x, y, z), out var id) ? id : (ushort)0;

        public bool NonFull(int x, int y, int z) => At(x, y, z) == Kelp;

        public bool IsWet(int x, int y, int z) => WetCell.IsWet(At, Water, NonFull, x, y, z);

        /// <summary>A 5×5×3 pool of water around the origin, stone underneath.</summary>
        public static Grid Pool()
        {
            var g = new Grid();
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    g.Set(x, -1, z, Stone);
                    for (int y = 0; y <= 2; y++)
                    {
                        g.Set(x, y, z, Water);
                    }
                }

            return g;
        }
    }

    [Fact]
    public void Water_IsWet_AirAndStone_AreNot()
    {
        var g = Grid.Pool();
        Assert.True(g.IsWet(0, 1, 0));
        Assert.False(g.IsWet(0, 5, 0));
        Assert.False(g.IsWet(0, -1, 0));
    }

    [Fact]
    public void APlantInsideThePool_IsWet_AllTheWayUpTheStalk()
    {
        var g = Grid.Pool().Set(0, 0, 0, Kelp).Set(0, 1, 0, Kelp).Set(0, 2, 0, Kelp);
        Assert.True(g.IsWet(0, 0, 0));
        Assert.True(g.IsWet(0, 1, 0));
        Assert.True(g.IsWet(0, 2, 0)); // the top segment: no water above, but water on all four sides
    }

    [Fact]
    public void APlantAtTheSurface_WithAirAbove_IsWet_WhenTheWaterSurroundsIt()
    {
        var g = Grid.Pool().Set(2, 2, 2, Kelp); // a corner cell: water on two sides (-X, -Z), air above
        Assert.True(g.IsWet(2, 2, 2));
    }

    [Fact]
    public void APlantOnTheBank_BesideOneWaterBlock_StaysDry()
    {
        var g = new Grid().Set(0, -1, 0, Stone).Set(0, 0, 0, Kelp).Set(1, 0, 0, Water);
        Assert.False(g.IsWet(0, 0, 0));
    }

    [Fact]
    public void AFullBlockInThePool_IsNeverWet()
    {
        var g = Grid.Pool().Set(0, 1, 0, Stone);
        Assert.False(g.IsWet(0, 1, 0));
    }

    [Fact]
    public void WithoutWater_NothingIsWet()
    {
        var g = Grid.Pool().Set(0, 1, 0, Kelp);
        Assert.False(WetCell.IsWet(g.At, 0, g.NonFull, 0, 1, 0));
    }

    [Theory]
    [InlineData("flora_kelp", true)]
    [InlineData("flora_succulent", true)]
    [InlineData("ladder", true)]
    [InlineData("torch", true)]
    [InlineData("stone", false)]
    [InlineData("water", false)]
    [InlineData(null, false)]
    public void NonFullKeys_ArePlantsAndSlimProps(string? key, bool expected)
    {
        Assert.Equal(expected, WetCell.IsNonFullKey(key));
    }
}

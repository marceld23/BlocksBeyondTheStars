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

    /// <summary>#4075 (report 2026-11-28, "another water surface under water"): a 3×3 block of kelp stalks two cells tall in
    /// the pool, water over it. The middle stalk's foot has kelp above and kelp on all four sides — it used to count as
    /// dry, and the mesher drew the water's faces toward it. The water runs down the stalk now.</summary>
    [Fact]
    public void TheInnerStalkOfADenseKelpPatch_IsWet_TheWaterRunsDownTheStalk()
    {
        var g = Grid.Pool();
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                g.Set(x, 0, z, Kelp).Set(x, 1, z, Kelp);
            }

        Assert.True(g.IsWet(0, 0, 0));  // kelp above, kelp on every side: wet only through the stalk rule
        Assert.True(g.IsWet(0, 1, 0));  // water above
        Assert.True(g.IsWet(-1, 0, -1)); // a corner foot: two water sides anyway
        Assert.False(WetCell.WaterSurrounds(g.At, Water, 0, 0, 0)); // the old one-cell test alone still says dry
    }

    [Fact]
    public void AStalkThatEndsInAir_DoesNotCarryWaterDown()
    {
        // A kelp column standing in a dry pit with a single water block beside its foot: nothing runs down from above.
        var g = new Grid().Set(0, -1, 0, Stone).Set(1, 0, 0, Water);
        for (int y = 0; y <= 3; y++)
        {
            g.Set(0, y, 0, Kelp);
        }

        Assert.False(g.IsWet(0, 0, 0));
        Assert.False(g.IsWet(0, 3, 0));
    }

    [Fact]
    public void AStalkUnderAFullBlock_DoesNotCarryWaterDown()
    {
        // Kelp walled in by stone on all sides with stone on top: the pool around does not reach it.
        var g = Grid.Pool().Set(0, 0, 0, Kelp).Set(0, 1, 0, Stone);
        g.Set(1, 0, 0, Stone).Set(-1, 0, 0, Stone).Set(0, 0, 1, Stone).Set(0, 0, -1, Stone);
        Assert.False(g.IsWet(0, 0, 0));
    }

    [Fact]
    public void TheStalkRule_StopsAfterMaxStalkCells()
    {
        // A tall shaft of kelp boxed in by stone, water only above its top: the foot further down than the cap stays dry.
        var g = new Grid();
        int top = WetCell.MaxStalkCells + 3;
        for (int y = 0; y <= top; y++)
        {
            g.Set(0, y, 0, Kelp).Set(1, y, 0, Stone).Set(-1, y, 0, Stone).Set(0, y, 1, Stone).Set(0, y, -1, Stone);
        }

        g.Set(0, top + 1, 0, Water);
        Assert.True(g.IsWet(0, top, 0));
        Assert.True(g.IsWet(0, top - WetCell.MaxStalkCells, 0));
        Assert.False(g.IsWet(0, top - WetCell.MaxStalkCells - 1, 0));
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

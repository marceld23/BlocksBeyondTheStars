// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The water-surface weights (#1749). A moat of varying width and a swamp full of reeds used to flip between
/// "river", "pond" and "open water" from cell to cell — a hard branch in the shader, so the surface read as a
/// mosaic of ripple directions and brightness tiles. The classification is continuous now, and a plant in the
/// water no longer counts as a shore.
/// </summary>
public sealed class WaterSurfaceTests
{
    private static readonly BlockId Water = new(7);
    private static readonly BlockId Stone = new(1);
    private static readonly BlockId Reed = new(9);

    private sealed class Grid
    {
        private readonly Dictionary<Vector3i, BlockId> _cells = new();

        public void Set(int x, int y, int z, BlockId id) => _cells[new Vector3i(x, y, z)] = id;

        public BlockId At(int x, int y, int z) => _cells.TryGetValue(new Vector3i(x, y, z), out var id) ? id : BlockId.Air;

        public bool IsReed(int x, int y, int z) => At(x, y, z).Value == Reed.Value;

        public WaterSurfaceData Classify(int x, int z, bool skipPlants = false)
            => WaterSurface.Classify(At, Water, x, 0, z, null, skipPlants ? IsReed : null);

        /// <summary>A water body filling [x0..x1] × [z0..z1] at y = 0 with stone all round.</summary>
        public static Grid Body(int x0, int z0, int x1, int z1)
        {
            var g = new Grid();
            for (int x = x0 - 1; x <= x1 + 1; x++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    bool inside = x >= x0 && x <= x1 && z >= z0 && z <= z1;
                    g.Set(x, 0, z, inside ? Water : Stone);
                }

            return g;
        }
    }

    [Fact]
    public void ANarrowChannel_IsABrookAlongItsLongAxis()
    {
        var g = Grid.Body(0, 0, 40, 2); // three wide along Z, long along X
        var d = g.Classify(20, 1);
        Assert.Equal(1f, d.FlowX, 3);
        Assert.Equal(0f, d.FlowZ, 3);
        Assert.Equal(0f, d.Open, 3);
    }

    [Fact]
    public void AChannelAlongZ_FlowsAlongZ()
    {
        var g = Grid.Body(0, 0, 2, 40);
        var d = g.Classify(1, 20);
        Assert.Equal(1f, d.FlowZ, 3);
        Assert.Equal(0f, d.FlowX, 3);
    }

    [Fact]
    public void AWideLake_IsOpenWater()
    {
        var g = Grid.Body(0, 0, 40, 40);
        var d = g.Classify(20, 20);
        Assert.Equal(1f, d.Open, 3);
        Assert.Equal(0f, d.Channel, 3);
        Assert.Equal(0f, d.Foam, 3);
    }

    [Fact]
    public void ASmallBasin_IsCalm()
    {
        var g = Grid.Body(0, 0, 9, 9); // ten wide: past the brook ramp, short of the open ramp
        var d = g.Classify(5, 5);
        Assert.Equal(0f, d.Open, 3);
        Assert.Equal(0f, d.Channel, 3);
    }

    [Fact]
    public void WideningAChannel_FadesTheBrookInsteadOfSwitchingItOff()
    {
        // Widths 5, 7 and 9 along Z: brook weight 1, ½, 0 — a moat that narrows at a corner blends.
        Assert.Equal(1f, Grid.Body(0, 0, 40, 4).Classify(20, 2).Channel, 3);
        Assert.Equal(0.5f, Grid.Body(0, 0, 40, 6).Classify(20, 3).Channel, 3);
        Assert.Equal(0f, Grid.Body(0, 0, 40, 8).Classify(20, 4).Channel, 3);
    }

    [Fact]
    public void ANearlySquareChannel_SharesTheFlowBetweenBothAxes()
    {
        // A 5×5 pool: a brook by width, but with no long axis to flow along — half and half, never a flip.
        var d = Grid.Body(0, 0, 4, 4).Classify(2, 2);
        Assert.Equal(1f, d.Channel, 3);
        Assert.Equal(0.5f, d.FlowX, 3);
        Assert.Equal(0.5f, d.FlowZ, 3);
    }

    [Fact]
    public void FoamSitsAtTheShoreAndFadesThreeBlocksOut()
    {
        var g = Grid.Body(0, 0, 40, 40);
        Assert.Equal(1f, g.Classify(0, 20).Foam, 3);
        Assert.Equal(0f, g.Classify(4, 20).Foam, 3);
        Assert.True(g.Classify(1, 20).Foam > g.Classify(2, 20).Foam);
    }

    [Fact]
    public void ReedsInALake_DoNotCutItIntoBrooks_WhenDeclaredPassable()
    {
        var g = Grid.Body(0, 0, 40, 40);
        for (int x = 2; x < 40; x += 3) // a line of reeds every third column
            for (int z = 0; z <= 40; z++)
            {
                g.Set(x, 0, z, Reed);
            }

        var cut = g.Classify(21, 21);
        Assert.True(cut.Channel > 0.9f, "without the passable test every reed is a shore and the lake reads as brooks");

        var whole = g.Classify(21, 21, skipPlants: true);
        Assert.Equal(1f, whole.Open, 3);
        Assert.Equal(0f, whole.Channel, 3);
        Assert.Equal(0f, whole.Foam, 3);
    }

    [Fact]
    public void TheStreamedEdge_IsNotAShore()
    {
        // A 4-wide strip whose far side is simply not loaded: the body is assumed to carry on.
        var g = Grid.Body(0, 0, 3, 40);
        var d = WaterSurface.Classify(g.At, Water, 1, 0, 20, loaded: (x, y, z) => x < 2);
        Assert.Equal(0f, d.Channel, 3);
    }
}

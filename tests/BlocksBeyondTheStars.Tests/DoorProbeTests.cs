// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1975: the one door rule. A door is authored as a single cell; the jambs beside it decide the wall axis and
/// the air run along the wall decides the width. The server has always hung doors this way — now the placement
/// ghost and the build editors run the same static over their own grids, so these tests pin the rule itself and
/// its agreement with the door-lane rule the furnisher uses.
/// </summary>
public sealed class DoorProbeTests
{
    private static Func<int, int, int, bool> Solid(params (int X, int Y, int Z)[] cells)
    {
        var set = new HashSet<(int, int, int)>(cells);
        return (x, y, z) => set.Contains((x, y, z));
    }

    [Fact]
    public void JambsOnXMeanTheWallRunsAlongX()
    {
        var fit = DoorProbe.Measure(Solid((-1, 1, 0), (1, 1, 0)), 0, 1, 0);
        Assert.True(fit.AxisX);
        Assert.True(fit.HasJamb);
        Assert.Equal(0, fit.Lo);
        Assert.Equal(0, fit.Hi);
        Assert.Equal(1f, fit.Width);
        Assert.Equal(0.5f, fit.CentreX(0));
        Assert.Equal(0.5f, fit.CentreZ(0));
    }

    [Fact]
    public void JambsOnZMeanTheWallRunsAlongZ()
    {
        var fit = DoorProbe.Measure(Solid((0, 1, -1), (0, 1, 1)), 0, 1, 0);
        Assert.False(fit.AxisX);
        Assert.True(fit.HasJamb);
        Assert.Equal(1f, fit.Width);
    }

    [Fact]
    public void JambsOnBothAxesFallBackToX()
    {
        var fit = DoorProbe.Measure(Solid((-1, 1, 0), (0, 1, 1)), 0, 1, 0);
        Assert.True(fit.AxisX);
        Assert.True(fit.HasJamb);
    }

    [Fact]
    public void NoJambRunsAlongZAndSaysSo()
    {
        // The historical default for a marker on open floor: a Z wall, the gap scanned to the reach on both sides.
        var fit = DoorProbe.Measure(Solid(), 0, 1, 0);
        Assert.False(fit.AxisX);
        Assert.False(fit.HasJamb);
        Assert.Equal(-DoorProbe.DefaultReach, fit.Lo);
        Assert.Equal(DoorProbe.DefaultReach, fit.Hi);
        Assert.Equal(7f, fit.Width);
    }

    [Fact]
    public void ATwoWideGapHasTheSameCentreFromEitherCell()
    {
        var solid = Solid((-1, 1, 0), (2, 1, 0));
        var left = DoorProbe.Measure(solid, 0, 1, 0);
        var right = DoorProbe.Measure(solid, 1, 1, 0);
        Assert.Equal(2f, left.Width);
        Assert.Equal(2f, right.Width);
        Assert.Equal(0, left.Lo);
        Assert.Equal(1, left.Hi);
        Assert.Equal(-1, right.Lo);
        Assert.Equal(0, right.Hi);
        Assert.Equal(1.0f, left.CentreX(0));
        Assert.Equal(1.0f, right.CentreX(1));
    }

    [Fact]
    public void AThreeWideGapProbedBesideItsJambCentresOnTheMiddleCell()
    {
        // Jambs at x = -2 and x = 2, the marker on the gap cell next to the left jamb.
        var fit = DoorProbe.Measure(Solid((-2, 1, 0), (2, 1, 0)), -1, 1, 0);
        Assert.True(fit.AxisX);
        Assert.Equal(0, fit.Lo);
        Assert.Equal(2, fit.Hi);
        Assert.Equal(3f, fit.Width);
        Assert.Equal(0.5f, fit.CentreX(-1));
    }

    [Fact]
    public void AMarkerInTheMiddleOfAWideGapHasNoJambAndSaysSo()
    {
        // The B41a ambiguity: the ±1 probe sees air on both axes, so the server would hang this door along Z —
        // the editors colour such a marker red ("needs a wall beside it") instead of promising the X door.
        var fit = DoorProbe.Measure(Solid((-2, 1, 0), (2, 1, 0)), 0, 1, 0);
        Assert.False(fit.HasJamb);
        Assert.False(fit.AxisX);
    }

    [Fact]
    public void TheReachCapsTheScan()
    {
        // One jamb on the minus side, open floor on the plus side: the gap stops at the reach, not at infinity.
        var solid = Solid((-1, 1, 0));
        Assert.Equal(3, DoorProbe.Measure(solid, 0, 1, 0).Hi);
        Assert.Equal(4f, DoorProbe.Measure(solid, 0, 1, 0).Width);
        Assert.Equal(1, DoorProbe.Measure(solid, 0, 1, 0, reach: 1).Hi);
    }

    [Fact]
    public void AForcedAxisWinsOverTheJambs()
    {
        // The ship hatch: a wide gap in the front wall where the ±1 probe is ambiguous — the caller knows the axis.
        var solid = Solid((-1, 1, 0), (1, 1, 0), (0, 1, -2), (0, 1, 2));
        var fit = DoorProbe.Measure(solid, 0, 1, 0, forceAxisX: false);
        Assert.False(fit.AxisX);
        Assert.Equal(-1, fit.Lo);
        Assert.Equal(1, fit.Hi);
    }

    [Fact]
    public void APlacedDoorFollowsTheOnlyJambAxisWhateverThePlayerLooksAt()
    {
        Assert.True(DoorProbe.AxisForPlacedDoor(Solid((-1, 1, 0)), 0, 1, 0, yawDegrees: 90));
        Assert.False(DoorProbe.AxisForPlacedDoor(Solid((0, 1, 1)), 0, 1, 0, yawDegrees: 0));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(60, false)]
    [InlineData(90, false)]
    [InlineData(180, true)]
    [InlineData(270, false)]
    public void APlacedDoorWithoutADecidingJambFacesThePlayer(double yaw, bool axisX)
    {
        Assert.Equal(axisX, DoorProbe.AxisForPlacedDoor(Solid(), 0, 1, 0, yaw));
        // Jambs on BOTH axes are just as undecided as none.
        Assert.Equal(axisX, DoorProbe.AxisForPlacedDoor(Solid((-1, 1, 0), (0, 1, 1)), 0, 1, 0, yaw));
    }

    /// <summary>A little voxel grid: floor at y = 0, air above until set.</summary>
    private sealed class Grid
    {
        private readonly ushort[,,] _b;

        public Grid(int w, int h, int l)
        {
            W = w;
            H = h;
            L = l;
            _b = new ushort[w, h, l];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < l; z++)
                {
                    _b[x, 0, z] = 1;
                }
        }

        public int W { get; }

        public int H { get; }

        public int L { get; }

        public void Set(int x, int y, int z) => _b[x, y, z] = 1;

        public ushort Get(int x, int y, int z) => _b[x, y, z];

        public bool Solid(int x, int y, int z)
            => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L && _b[x, y, z] != 0;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AgreesWithTheDoorLaneRuleOnAxisAndGap(int gapWidth)
    {
        // A wall along X at z = 3, three tall, with a gap of gapWidth cells starting at x = 2.
        var g = new Grid(9, 4, 7);
        for (int x = 0; x < 9; x++)
            for (int y = 1; y <= 3; y++)
            {
                if (x < 2 || x >= 2 + gapWidth)
                {
                    g.Set(x, y, 3);
                }
            }

        var lane = RoomFurnisher.DoorLaneAt(g.Get, g.W, g.H, g.L, 2, 1, 3);
        Assert.NotNull(lane);
        var fit = DoorProbe.Measure(g.Solid, 2, 1, 3);

        Assert.Equal(lane!.WallAlongX, fit.AxisX);
        Assert.Equal(lane.GapMin, 2 + fit.Lo);
        Assert.Equal(lane.GapMax, 2 + fit.Hi);
        Assert.Equal((float)gapWidth, fit.Width);
    }
}

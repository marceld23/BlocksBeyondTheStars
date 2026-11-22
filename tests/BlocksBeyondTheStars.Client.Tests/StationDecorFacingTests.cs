// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using Xunit;
using static BlocksBeyondTheStars.Client.StationDecorFacing;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The station decor faces the person who uses it (<see cref="StationDecorFacing"/>) — the cockpit the pilot
/// behind it, a terminal the side someone can stand on, toward the ship's interior.
/// </summary>
public sealed class StationDecorFacingTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 0, -1)]
    [InlineData(3, -1, 0)]
    public void AQuarterTurnsTheFrontLikeUnitysYaw(int quarter, int x, int z)
    {
        // Quaternion.Euler(0, 90q, 0) * (0, 0, 1) = (sin 90q, 0, cos 90q).
        Assert.Equal((x, z), Side(quarter));
        Assert.Equal((x, z), Side(quarter + 4));
        Assert.Equal((x, z), Side(quarter - 4));
    }

    [Fact]
    public void FourQuarterTurnsComeBack()
    {
        Assert.Equal((2, -3), Turn(2, -3, 4));
        Assert.Equal(Turn(2, -3, 3), Turn(Turn(2, -3, 1).X, Turn(2, -3, 1).Z, 2));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(-1f, 0f)]
    [InlineData(0.3f, 0.95f)]
    [InlineData(-0.9f, -0.2f)]
    public void TheCockpitFacesThePilotBehindItForEveryHeading(float noseX, float noseZ)
    {
        var front = Side(CockpitFront(noseX, noseZ));
        // The front points against the nose along its main axis.
        Assert.True((front.X * noseX) + (front.Z * noseZ) < -0.7f, $"front {front} for nose ({noseX}, {noseZ})");
    }

    [Fact]
    public void AStockShipFacingPlusZTurnsTheConsoleRoundToMinusZ()
    {
        Assert.Equal(2, CockpitFront(0f, 1f));
        Assert.Equal(2, CockpitFront(0f, 0f)); // no nose = the stock ships' +Z
    }

    /// <summary>A little ship interior: a floor at y 0 everywhere, the marker at (0, 1, 0), and the walls given.</summary>
    private static Func<int, int, int, bool> Room(params (int X, int Y, int Z)[] walls)
    {
        var solid = new HashSet<(int, int, int)>(walls) { (0, 1, 0) };
        return (x, y, z) => y > 0 && !solid.Contains((x, y, z));
    }

    private static (int X, int Y, int Z)[] Wall(int x, int z) => new[] { (x, 1, z), (x, 2, z) };

    [Fact]
    public void ATerminalAgainstAWallFacesAwayFromIt()
    {
        // Wall on −X; the interior lies toward +X.
        Assert.Equal(1, TerminalFront(Room(Wall(-1, 0)), 0, 1, 0, 1f, 0.2f));

        // Wall on +Z; the interior straight away from it.
        Assert.Equal(2, TerminalFront(Room(Wall(0, 1)), 0, 1, 0, 0f, -1f));
    }

    [Fact]
    public void ATerminalInANicheFacesItsOnlyOpenSideWhereverTheInteriorIs()
    {
        var walls = new List<(int, int, int)>();
        walls.AddRange(Wall(-1, 0));
        walls.AddRange(Wall(1, 0));
        walls.AddRange(Wall(0, 1));
        Assert.Equal(2, TerminalFront(Room(walls.ToArray()), 0, 1, 0, 1f, 1f));
    }

    [Fact]
    public void AmongOpenSidesTheInteriorDecides()
    {
        // Open all round: the side nearest the interior.
        Assert.Equal(3, TerminalFront(Room(), 0, 1, 0, -0.9f, 0.3f));
        Assert.Equal(0, TerminalFront(Room(), 0, 1, 0, 0.2f, 0.8f));
    }

    [Fact]
    public void ASideWithoutAFloorLosesToOneWithAFloor()
    {
        // The interior points to +X, but there is a hole in the floor at (1, 0, 0): nobody stands there.
        var solid = new HashSet<(int, int, int)> { (0, 1, 0) };
        bool Passable(int x, int y, int z) => (y > 0 || (x == 1 && z == 0)) && !solid.Contains((x, y, z));
        Assert.NotEqual(1, TerminalFront(Passable, 0, 1, 0, 1f, 0f));
    }

    [Fact]
    public void NoOpenSideFallsBackToTheInterior()
    {
        var walls = new List<(int, int, int)>();
        walls.AddRange(Wall(-1, 0));
        walls.AddRange(Wall(1, 0));
        walls.AddRange(Wall(0, 1));
        walls.AddRange(Wall(0, -1));
        Assert.Equal(3, TerminalFront(Room(walls.ToArray()), 0, 1, 0, -1f, 0.1f));
        Assert.Equal(1, Choose(new bool[4], 0.7f, -0.5f));
    }

    [Fact]
    public void TheChoiceIsDeterministicAndBreaksExactTiesTheSameWay()
    {
        // Interior exactly between +X and +Z, both open: always the lower quarter.
        var open = new[] { true, true, false, false };
        Assert.Equal(0, Choose(open, 1f, 1f));
        Assert.Equal(0, Choose(open, 1f, 1f));

        // No interior known: the lowest open quarter.
        Assert.Equal(1, Choose(new[] { false, true, true, false }, 0f, 0f));
        Assert.Equal(TerminalFront(Room(Wall(-1, 0)), 0, 1, 0, 0.4f, -0.4f), TerminalFront(Room(Wall(-1, 0)), 0, 1, 0, 0.4f, -0.4f));
    }
}

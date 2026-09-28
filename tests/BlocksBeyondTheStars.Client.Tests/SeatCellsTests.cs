// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's quick look before sitting down (#2122): a sitter's reported feet count for the seat cell they are in —
/// across both wrap seams — and not for the cell beside or above it.
/// </summary>
public sealed class SeatCellsTests
{
    private const int Circ = 1024;

    [Fact]
    public void ASitterOnTheSeat_CoversIt_ANeighbourDoesNot()
    {
        Assert.True(SeatCells.Covers(10.5f, 64f, -3.5f, 10, 64, -4, Circ));
        Assert.False(SeatCells.Covers(11.5f, 64f, -3.5f, 10, 64, -4, Circ)); // standing beside the chair
        Assert.False(SeatCells.Covers(10.5f, 65f, -3.5f, 10, 64, -4, Circ)); // a floor up
    }

    [Fact]
    public void FeetAHairBelowTheFloor_StillCount()
        => Assert.True(SeatCells.Covers(10.5f, 63.999f, 2.5f, 10, 64, 2, Circ));

    [Fact]
    public void TheSeams_AreCrossedTheShortWay()
    {
        int zHalf = WorldConstants.LatitudePeriodFor(Circ) / 2;

        // A sitter reported at the canonical end of X, the aimed cell one world round further (unwrapped).
        Assert.True(SeatCells.Covers(Circ - 0.5f, 64f, 0.5f, -1, 64, 0, Circ));

        // The same north–south: canonical Z just below +half, the aimed cell expressed from the other side.
        Assert.True(SeatCells.Covers(0.5f, 64f, zHalf - 0.5f, 0, 64, -zHalf - 1, Circ));
    }
}

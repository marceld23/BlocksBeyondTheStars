// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Double doors (#1729): two hinged doors placed side by side in one wall must swing away from their shared
/// edge. The server records each placed door on its own, so the client infers the pair from positions — and
/// the only thing it has to get right is WHICH of the two hangs its leaf on the far jamb.
/// </summary>
public sealed class DoorPairsTests
{
    private static DoorPairs.Door Wood(float x, float y, float z, bool axisX) => new("wood", true, x, y, z, axisX);

    [Fact]
    public void ASingleDoor_KeepsTheDefaultSwing()
    {
        var alone = new[] { Wood(0.5f, 10f, 0.5f, axisX: true) };
        Assert.False(DoorPairs.MirrorsLeaf(alone[0], alone));
    }

    [Fact]
    public void APairInAnXWall_MirrorsTheRightHandDoorOnly()
    {
        // Two one-block doorways side by side along X: the one with the greater X is the right-hand half.
        var pair = new[] { Wood(0.5f, 10f, 0.5f, axisX: true), Wood(1.5f, 10f, 0.5f, axisX: true) };
        Assert.False(DoorPairs.MirrorsLeaf(pair[0], pair));
        Assert.True(DoorPairs.MirrorsLeaf(pair[1], pair));
    }

    [Fact]
    public void APairInAZWall_MirrorsTheDoorOnTheMinusZSide()
    {
        // The renderer turns a Z-wall door +90° about Y, which carries its local +X onto world −Z — so along a
        // Z wall the door with the SMALLER Z is the one on the right, and that is the one that mirrors.
        var pair = new[] { Wood(0.5f, 10f, 0.5f, axisX: false), Wood(0.5f, 10f, 1.5f, axisX: false) };
        Assert.True(DoorPairs.MirrorsLeaf(pair[0], pair));
        Assert.False(DoorPairs.MirrorsLeaf(pair[1], pair));
    }

    [Fact]
    public void ThreeInARow_OnlyTheEndDoorMirrors()
    {
        var row = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(1.5f, 10f, 0.5f, true), Wood(2.5f, 10f, 0.5f, true) };
        Assert.False(DoorPairs.MirrorsLeaf(row[0], row));
        Assert.False(DoorPairs.MirrorsLeaf(row[1], row)); // the middle leaf has a neighbour on both sides
        Assert.True(DoorPairs.MirrorsLeaf(row[2], row));
    }

    [Fact]
    public void DifferentKinds_DoNotPair()
    {
        var mixed = new[] { Wood(0.5f, 10f, 0.5f, true), new DoorPairs.Door("hinge", true, 1.5f, 10f, 0.5f, true) };
        Assert.False(DoorPairs.MirrorsLeaf(mixed[0], mixed));
        Assert.False(DoorPairs.MirrorsLeaf(mixed[1], mixed));
    }

    [Fact]
    public void SlideDoors_NeverPair()
    {
        var slides = new[] { new DoorPairs.Door("slide", false, 0.5f, 10f, 0.5f, true), new DoorPairs.Door("slide", false, 1.5f, 10f, 0.5f, true) };
        Assert.False(DoorPairs.MirrorsLeaf(slides[0], slides));
        Assert.False(DoorPairs.MirrorsLeaf(slides[1], slides));
    }

    [Fact]
    public void DoorsOneBehindTheOther_AcrossTheWall_DoNotPair()
    {
        // Same X, one block apart in Z, both in X walls: two parallel walls, not one doorway.
        var stacked = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(0.5f, 10f, 1.5f, true) };
        Assert.False(DoorPairs.MirrorsLeaf(stacked[0], stacked));
        Assert.False(DoorPairs.MirrorsLeaf(stacked[1], stacked));
    }

    [Fact]
    public void DoorsOnDifferentFloors_DoNotPair()
    {
        var floors = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(1.5f, 13f, 0.5f, true) };
        Assert.False(DoorPairs.MirrorsLeaf(floors[1], floors));
    }

    // ---------------- #1852: the shared jamb ----------------

    [Fact]
    public void PartnerSides_NamesTheSharedJamb_OfEachLeaf()
    {
        // An X wall: the left leaf's partner is on its local +X, the right leaf's on its local −X.
        var pair = new[] { Wood(0.5f, 10f, 0.5f, axisX: true), Wood(1.5f, 10f, 0.5f, axisX: true) };
        Assert.Equal(DoorPairs.Sides.Plus, DoorPairs.PartnerSides(pair[0], pair));
        Assert.Equal(DoorPairs.Sides.Minus, DoorPairs.PartnerSides(pair[1], pair));

        // A Z wall: local +X is world −Z, so the door with the greater Z has its partner on local −X.
        var zPair = new[] { Wood(0.5f, 10f, 0.5f, axisX: false), Wood(0.5f, 10f, 1.5f, axisX: false) };
        Assert.Equal(DoorPairs.Sides.Minus, DoorPairs.PartnerSides(zPair[0], zPair));
        Assert.Equal(DoorPairs.Sides.Plus, DoorPairs.PartnerSides(zPair[1], zPair));
    }

    [Fact]
    public void PartnerSides_IsNone_ForALoneDoor_ASlideDoor_OrAGap()
    {
        var alone = new[] { Wood(0.5f, 10f, 0.5f, true) };
        Assert.Equal(DoorPairs.Sides.None, DoorPairs.PartnerSides(alone[0], alone));

        var slides = new[] { new DoorPairs.Door("slide", false, 0.5f, 10f, 0.5f, true), new DoorPairs.Door("slide", false, 1.5f, 10f, 0.5f, true) };
        Assert.Equal(DoorPairs.Sides.None, DoorPairs.PartnerSides(slides[0], slides));

        var gap = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(2.5f, 10f, 0.5f, true) };
        Assert.Equal(DoorPairs.Sides.None, DoorPairs.PartnerSides(gap[0], gap));
        Assert.Equal(DoorPairs.Sides.None, DoorPairs.PartnerSides(gap[1], gap));

        var stacked = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(0.5f, 10f, 1.5f, true) };
        Assert.Equal(DoorPairs.Sides.None, DoorPairs.PartnerSides(stacked[0], stacked));
    }

    [Fact]
    public void ThreeInARow_TheMiddleLeafSharesBothJambs_AndKeepsTheDefaultSwing()
    {
        var row = new[] { Wood(0.5f, 10f, 0.5f, true), Wood(1.5f, 10f, 0.5f, true), Wood(2.5f, 10f, 0.5f, true) };
        Assert.Equal(DoorPairs.Sides.Plus, DoorPairs.PartnerSides(row[0], row));
        Assert.Equal(DoorPairs.Sides.Minus | DoorPairs.Sides.Plus, DoorPairs.PartnerSides(row[1], row));
        Assert.Equal(DoorPairs.Sides.Minus, DoorPairs.PartnerSides(row[2], row));

        // The swing rule reads the same neighbourhood: only a sole −X partner mirrors the leaf.
        Assert.False(DoorPairs.MirrorsLeaf(DoorPairs.Sides.Plus));
        Assert.False(DoorPairs.MirrorsLeaf(DoorPairs.Sides.Minus | DoorPairs.Sides.Plus));
        Assert.True(DoorPairs.MirrorsLeaf(DoorPairs.Sides.Minus));
        Assert.False(DoorPairs.MirrorsLeaf(DoorPairs.Sides.None));
    }
}

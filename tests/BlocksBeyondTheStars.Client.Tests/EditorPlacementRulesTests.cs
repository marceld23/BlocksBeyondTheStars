// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #1975: a click in the build editors writes what the game would have stamped — default forms, the bed as a
/// pair, the ladder against its wall — and a door marker is only valid where the server would hang the door the
/// author means.
/// </summary>
public sealed class EditorPlacementRulesTests
{
    private const int Room = 16;

    private static bool InRoom(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Room && y < Room && z < Room;

    private static Func<int, int, int, bool> Cells(params (int X, int Y, int Z)[] cells)
    {
        var set = new HashSet<(int, int, int)>(cells);
        return (x, y, z) => set.Contains((x, y, z));
    }

    private static readonly Func<int, int, int, bool> Nothing = (_, _, _) => false;

    [Theory]
    [InlineData("stone", 0)]
    [InlineData("bed", (int)BlockShape.BedHead)]
    [InlineData("campfire", (int)BlockShape.Slab)]
    [InlineData("rug", (int)BlockShape.Sheet)]
    [InlineData("flower_pot", (int)BlockShape.Pot)]
    [InlineData("ladder", (int)BlockShape.Panel)]
    [InlineData("stairs", (int)BlockShape.Stairs)]
    [InlineData("stretcher", (int)BlockShape.Table)]
    public void AutomaticMeansTheBlocksOwnForm(string block, int form)
    {
        Assert.Equal(form, EditorPlacementRules.ResolveForm(block, EditorPlacementRules.AutoForm));
    }

    [Fact]
    public void AChosenFormWinsOverTheDefaultAndCubeIsAChoice()
    {
        Assert.Equal((int)BlockShape.Bench, EditorPlacementRules.ResolveForm("campfire", (int)BlockShape.Bench));
        Assert.Equal(0, EditorPlacementRules.ResolveForm("campfire", 0));
        Assert.Equal((int)BlockShape.Slab, EditorPlacementRules.ResolveForm("stone", (int)BlockShape.Slab));
    }

    [Fact]
    public void AnOrdinaryBlockWritesOneCellWithTheBrushYaw()
    {
        Assert.True(EditorPlacementRules.TryPlaceBlock("stone", (int)BlockShape.Ramp, 2, 3, 1, 4, -1, Nothing, InRoom, Nothing, out var writes, out var why));
        Assert.Null(why);
        var w = Assert.Single(writes);
        Assert.Equal((3, 1, 4), (w.X, w.Y, w.Z));
        Assert.Equal((int)BlockShape.Ramp, ShapeCode.ShapeOf(w.Shape));
        Assert.Equal(2, ShapeCode.OrientationOf(w.Shape));
        Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(w.Shape));
    }

    [Fact]
    public void ACubeWritesAZeroDescriptorWhateverTheYaw()
    {
        Assert.True(EditorPlacementRules.TryPlaceBlock("stone", EditorPlacementRules.AutoForm, 3, 0, 0, 0, -1, Nothing, InRoom, Nothing, out var writes, out _));
        Assert.Equal(0, Assert.Single(writes).Shape);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, -1, 0)]
    [InlineData(2, 0, -1)]
    [InlineData(3, 1, 0)]
    public void ABedWritesHeadAndFootWhereTheYawPoints(int yaw, int dx, int dz)
    {
        Assert.True(EditorPlacementRules.TryPlaceBlock("bed", EditorPlacementRules.AutoForm, yaw, 5, 1, 5, -1, Nothing, InRoom, Nothing, out var writes, out var why));
        Assert.Null(why);
        Assert.Equal(2, writes.Count);
        var head = writes[0];
        var foot = writes[1];
        Assert.Equal((5, 1, 5), (head.X, head.Y, head.Z));
        Assert.Equal((5 + dx, 1, 5 + dz), (foot.X, foot.Y, foot.Z));
        Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(head.Shape));
        Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(foot.Shape));
        Assert.Equal(yaw, ShapeCode.OrientationOf(foot.Shape));
        // The pair agrees with the server's own partner rule from either half.
        Assert.True(FurnitureShapes.TryBedPartnerOffset(foot.Shape, out int bx, out int bz));
        Assert.Equal((-dx, -dz), (bx, bz));
    }

    [Fact]
    public void ABedIsRefusedWhenItsFootCellIsTaken()
    {
        var taken = Cells((5, 1, 6));
        Assert.False(EditorPlacementRules.TryPlaceBlock("bed", EditorPlacementRules.AutoForm, 0, 5, 1, 5, -1, taken, InRoom, Nothing, out var writes, out var why));
        Assert.Equal(EditorPlacementRules.BedNeedsRoom, why);
        Assert.Empty(writes);
    }

    [Fact]
    public void ABedIsRefusedAtTheRoomsEdge()
    {
        // Yaw 0 puts the foot at +Z; at z = 15 that is outside a 16-room.
        Assert.False(EditorPlacementRules.TryPlaceBlock("bed", EditorPlacementRules.AutoForm, 0, 5, 1, Room - 1, -1, Nothing, InRoom, Nothing, out _, out var why));
        Assert.Equal(EditorPlacementRules.BedNeedsRoom, why);
    }

    [Fact]
    public void ABedForcedToACubeIsJustACube()
    {
        Assert.True(EditorPlacementRules.TryPlaceBlock("bed", 0, 0, 5, 1, 5, -1, Nothing, InRoom, Nothing, out var writes, out _));
        Assert.Equal(0, Assert.Single(writes).Shape);
    }

    [Fact]
    public void RemovingABedHalfTakesThePartnerAlong()
    {
        // Yaw 3 points the head's local +Z along +X, so the foot lies at x + 1.
        int head = ShapeCode.Pack((int)BlockShape.BedHead, 3, ShapeCode.UpPlusY);
        var partner = Assert.Single(EditorPlacementRules.PartnerCells(head, 5, 1, 5));
        Assert.Equal((6, 1, 5), partner);
        int foot = FurnitureShapes.BedPartnerDescriptor(head);
        Assert.Equal((5, 1, 5), Assert.Single(EditorPlacementRules.PartnerCells(foot, 6, 1, 5)));
        Assert.Empty(EditorPlacementRules.PartnerCells(ShapeCode.Pack((int)BlockShape.Slab, 0), 5, 1, 5));
        Assert.Empty(EditorPlacementRules.PartnerCells(0, 5, 1, 5));
    }

    [Fact]
    public void ALadderHugsTheWallItWasClickedAgainst()
    {
        // Walls on both the −X and the +Z side of the cell. The author clicked the +Z wall, so the hit normal
        // (the step from the wall block into the placed cell) points −Z — that is the "clicked face".
        var solid = Cells((4, 1, 5), (5, 1, 6));
        int clickedFace = ShapeCode.FaceFromDirection(0, 0, -1);
        Assert.True(EditorPlacementRules.TryPlaceBlock("ladder", EditorPlacementRules.AutoForm, 0, 5, 1, 5, clickedFace, Nothing, InRoom, solid, out var writes, out _));
        var w = Assert.Single(writes);
        Assert.Equal((int)BlockShape.Panel, ShapeCode.ShapeOf(w.Shape));
        // The plate's up-face points AWAY from its wall: the wall at +Z means up-face −Z.
        var dir = ShapeCode.FaceDirection(ShapeCode.UpFaceOf(w.Shape));
        Assert.Equal((0, 0, -1), dir);
    }

    [Fact]
    public void ALadderWithNoWallBecomesThePole()
    {
        Assert.True(EditorPlacementRules.TryPlaceBlock("ladder", EditorPlacementRules.AutoForm, 0, 5, 1, 5, -1, Nothing, InRoom, Nothing, out var writes, out _));
        var w = Assert.Single(writes);
        Assert.Equal(PropShapes.LadderFreeStanding, ShapeCode.ShapeOf(w.Shape));
        Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(w.Shape));
    }

    [Fact]
    public void ADoorMarkerNeedsAWallBesideIt()
    {
        var jambs = Cells((4, 1, 5), (6, 1, 5));
        Assert.True(EditorPlacementRules.DoorValid(jambs, jambs, InRoom, 5, 1, 5, out var fit, out var why));
        Assert.Null(why);
        Assert.True(fit.AxisX);
        Assert.Equal(1f, fit.Width);

        Assert.False(EditorPlacementRules.DoorValid(Nothing, Nothing, InRoom, 5, 1, 5, out _, out why));
        Assert.Equal(EditorPlacementRules.DoorNeedsWall, why);
    }

    [Fact]
    public void ADoorMarkerNeedsTwoFreeCellsAbove()
    {
        var jambs = Cells((4, 1, 5), (6, 1, 5));
        var lintelTooLow = Cells((4, 1, 5), (6, 1, 5), (5, 3, 5));
        Assert.False(EditorPlacementRules.DoorValid(jambs, lintelTooLow, InRoom, 5, 1, 5, out _, out var why));
        Assert.Equal(EditorPlacementRules.DoorNeedsWall, why);

        // A marker anything above it — even another marker — is in the way; and the top row of the room is too low.
        var markerAbove = Cells((5, 2, 5));
        Assert.False(EditorPlacementRules.DoorValid(jambs, markerAbove, InRoom, 5, 1, 5, out _, out _));
        Assert.False(EditorPlacementRules.DoorValid(Cells((4, Room - 1, 5)), Nothing, InRoom, 5, Room - 1, 5, out _, out _));
    }

    [Fact]
    public void AChangeNearADoorReFitsItAChangeFarAwayDoesNot()
    {
        Assert.True(EditorPlacementRules.AffectsDoor(9, 1, 5, 5, 1, 5));   // 4 along X: the far end of a 3-reach scan plus its jamb
        Assert.True(EditorPlacementRules.AffectsDoor(5, 1, 9, 5, 1, 5));   // the axis can flip, so Z counts too
        Assert.False(EditorPlacementRules.AffectsDoor(10, 1, 5, 5, 1, 5));
        Assert.False(EditorPlacementRules.AffectsDoor(5, 2, 5, 5, 1, 5));  // the probe reads the floor level only
    }
}

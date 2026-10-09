// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2438: the client's fall guard asks whether the cell at chest or eye height can really enclose the player. A carpet,
/// a plate or a slab laid into the feet cell must not count — that is what teleported Justus "to my last position" every
/// time he placed one — while a plain cube or a full-height form still does.
/// </summary>
public sealed class BlockShapeFactsTests
{
    [Theory]
    [InlineData(BlockShape.Cube, true)]
    [InlineData(BlockShape.Pyramid, true)]
    [InlineData(BlockShape.Dome, true)]
    [InlineData(BlockShape.Sphere, true)]
    [InlineData(BlockShape.Ramp, true)]
    [InlineData(BlockShape.Stairs, true)]
    [InlineData(BlockShape.Cone, true)]
    [InlineData(BlockShape.Cylinder, true)]
    [InlineData(BlockShape.Slab, false)]
    [InlineData(BlockShape.Panel, false)]
    [InlineData(BlockShape.Sheet, false)]
    [InlineData(BlockShape.LowRamp, false)]
    [InlineData(BlockShape.QuarterCube, false)]
    [InlineData(BlockShape.Post, false)]
    [InlineData(BlockShape.Beam, false)]
    [InlineData(BlockShape.Table, false)]
    [InlineData(BlockShape.Chair, false)]
    [InlineData(BlockShape.Fence, false)]
    [InlineData(BlockShape.Pot, false)]
    [InlineData(BlockShape.Bench, false)]
    [InlineData(BlockShape.BedHead, false)]
    [InlineData(BlockShape.BedFoot, false)]
    public void CanEnclosePlayer_OnlyForFormsThatFillTheCell(BlockShape shape, bool encloses)
        => Assert.Equal(encloses, BlockShapeFacts.CanEnclosePlayer((int)shape));

    [Fact]
    public void APlayerDesignedForm_CountsAsEnclosing_TheGuardCannotKnowItsCells()
    {
        int custom = ShapeCode.FirstCustom;
        Assert.True(ShapeCode.IsCustomShape(custom));
        Assert.True(BlockShapeFacts.CanEnclosePlayer(custom));
    }
}

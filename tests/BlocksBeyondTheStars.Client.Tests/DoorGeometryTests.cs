// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #1975: the boxes every door is drawn from — by the world renderer, the placement ghost and the build editors
/// alike. These pin the part counts and the sizes <c>DoorView</c> has always used, so extracting them changed
/// nothing and a preview can never promise a door the world then hangs differently.
/// </summary>
public sealed class DoorGeometryTests
{
    private static int Count(string kind, DoorGeometry.Part part, float width = 1f, DoorPairs.Sides partners = DoorPairs.Sides.None)
        => DoorGeometry.Closed(kind, width, partners: partners).Count(b => b.Part == part);

    [Theory]
    [InlineData("hinge")]
    [InlineData("wood")]
    public void AHandDoorIsOneLeafWithASeamAndTwoPosts(string kind)
    {
        var boxes = DoorGeometry.Closed(kind, 1f);
        Assert.Equal(4, boxes.Count);
        Assert.Equal(1, Count(kind, DoorGeometry.Part.Leaf));
        Assert.Equal(1, Count(kind, DoorGeometry.Part.Seam));
        Assert.Equal(1, Count(kind, DoorGeometry.Part.PostMinus));
        Assert.Equal(1, Count(kind, DoorGeometry.Part.PostPlus));
        Assert.Equal(0, Count(kind, DoorGeometry.Part.Field));
    }

    [Fact]
    public void ASlideDoorIsTwoPanelsWithSeamsAndTwoPosts()
    {
        var boxes = DoorGeometry.Closed("slide", 2f);
        Assert.Equal(6, boxes.Count);
        Assert.Equal(1, Count("slide", DoorGeometry.Part.PanelMinus, 2f));
        Assert.Equal(1, Count("slide", DoorGeometry.Part.PanelPlus, 2f));
        Assert.Equal(2, Count("slide", DoorGeometry.Part.Seam, 2f));
        Assert.Equal(0, Count("slide", DoorGeometry.Part.Leaf, 2f));
    }

    [Fact]
    public void AnEnergyDoorIsASlideDoorPlusItsField()
    {
        var boxes = DoorGeometry.Closed("energy", 2f);
        Assert.Equal(7, boxes.Count);
        var field = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.Field);
        Assert.Equal(2f * DoorGeometry.FieldWidthFactor, field.Size.X, 5);
        Assert.Equal(DoorGeometry.Height, field.Size.Y, 5);
        Assert.Equal(DoorGeometry.FieldThickness, field.Size.Z, 5);
    }

    [Fact]
    public void TheLeafSpansTheGapAndStandsOnTheFloor()
    {
        var leaf = Assert.Single(DoorGeometry.Closed("hinge", 1f), b => b.Part == DoorGeometry.Part.Leaf);
        Assert.Equal(1f * DoorGeometry.LeafWidthFactor, leaf.Size.X, 5);
        Assert.Equal(DoorGeometry.Height, leaf.Size.Y, 5);
        Assert.Equal(DoorGeometry.Thickness, leaf.Size.Z, 5);
        Assert.Equal(DoorGeometry.Height * 0.5f, leaf.Centre.Y, 5);
        Assert.Equal(0f, leaf.Centre.X, 5);
    }

    [Fact]
    public void ClosedSlidePanelsSitAQuarterGapEitherSideOfTheMiddle()
    {
        var boxes = DoorGeometry.Closed("slide", 2f);
        var minus = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.PanelMinus);
        var plus = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.PanelPlus);
        Assert.Equal(-0.5f, minus.Centre.X, 5);
        Assert.Equal(0.5f, plus.Centre.X, 5);
        Assert.Equal(1f * DoorGeometry.PanelWidthFactor, minus.Size.X, 5);
        Assert.Equal(minus.Size, plus.Size);
    }

    [Fact]
    public void ASeamFollowsItsPanelAndIsAFractionOfIt()
    {
        var boxes = DoorGeometry.Closed("slide", 2f);
        var plus = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.PanelPlus);
        var seam = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.Seam && b.Follows == DoorGeometry.Part.PanelPlus);
        Assert.Equal(plus.Centre, seam.Centre);
        Assert.Equal(plus.Size.X * DoorGeometry.SeamWidthFactor, seam.Size.X, 5);
        Assert.Equal(plus.Size.Y * DoorGeometry.SeamHeightFactor, seam.Size.Y, 5);
        Assert.Equal(plus.Size.Z * DoorGeometry.SeamDepthFactor, seam.Size.Z, 5);
        Assert.True(seam.IsTrim);
        Assert.False(plus.IsTrim);
    }

    [Fact]
    public void PostsStandOnTheJambsAndOvertopTheDoor()
    {
        var boxes = DoorGeometry.Closed("slide", 3f);
        var minus = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.PostMinus);
        var plus = Assert.Single(boxes, b => b.Part == DoorGeometry.Part.PostPlus);
        Assert.Equal(-1.5f, minus.Centre.X, 5);
        Assert.Equal(1.5f, plus.Centre.X, 5);
        Assert.Equal(DoorGeometry.PostWidth, plus.Size.X, 5);
        Assert.Equal(DoorGeometry.Height + DoorGeometry.PostExtraHeight, plus.Size.Y, 5);
        Assert.Equal(DoorGeometry.Thickness * DoorGeometry.PostDepthFactor, plus.Size.Z, 5);
        Assert.True(plus.IsTrim);
    }

    [Fact]
    public void ASharedJambGetsNoPost()
    {
        // The right-hand half of a double door (#1852): its −X jamb is the partner's, so no post there.
        Assert.Equal(0, Count("hinge", DoorGeometry.Part.PostMinus, partners: DoorPairs.Sides.Minus));
        Assert.Equal(1, Count("hinge", DoorGeometry.Part.PostPlus, partners: DoorPairs.Sides.Minus));
        Assert.Equal(0, Count("hinge", DoorGeometry.Part.PostMinus, partners: DoorPairs.Sides.Minus | DoorPairs.Sides.Plus));
        Assert.Equal(0, Count("hinge", DoorGeometry.Part.PostPlus, partners: DoorPairs.Sides.Minus | DoorPairs.Sides.Plus));
    }

    [Fact]
    public void TheMirroredLeafHangsOnTheFarJamb()
    {
        Assert.Equal(-0.5f, DoorGeometry.HingePivotX(1f, mirrored: false), 5);
        Assert.Equal(0.5f, DoorGeometry.HingePivotX(1f, mirrored: true), 5);
        Assert.Equal(-1f, DoorGeometry.HingePivotX(2f, mirrored: false), 5);
        // …and swings the other way round, so both halves open to the same side of the wall.
        Assert.Equal(-DoorGeometry.HingeSwingDegrees, DoorGeometry.HingeSwingDegreesFor(1f, mirrored: false), 5);
        Assert.Equal(DoorGeometry.HingeSwingDegrees, DoorGeometry.HingeSwingDegreesFor(1f, mirrored: true), 5);
        Assert.Equal(0f, DoorGeometry.HingeSwingDegreesFor(0f, mirrored: true), 5);
    }

    [Fact]
    public void SlidePanelsRetractIntoTheirJambs()
    {
        Assert.Equal(-0.5f, DoorGeometry.SlidePanelX(2f, plusSide: false, 0f), 5);
        Assert.Equal(0.5f, DoorGeometry.SlidePanelX(2f, plusSide: true, 0f), 5);
        Assert.Equal(-0.5f - 1f * DoorGeometry.SlideTravelFactor, DoorGeometry.SlidePanelX(2f, plusSide: false, 1f), 5);
        Assert.Equal(0.5f + 1f * DoorGeometry.SlideTravelFactor, DoorGeometry.SlidePanelX(2f, plusSide: true, 1f), 5);
    }

    [Fact]
    public void WidthIsNeverBelowOneBlock()
    {
        Assert.Equal(1f, DoorGeometry.ClampWidth(0f));
        Assert.Equal(1f, DoorGeometry.ClampWidth(0.4f));
        Assert.Equal(2f, DoorGeometry.ClampWidth(2f));
        var leaf = Assert.Single(DoorGeometry.Closed("wood", 0f), b => b.Part == DoorGeometry.Part.Leaf);
        Assert.Equal(DoorGeometry.LeafWidthFactor, leaf.Size.X, 5);
    }

    [Theory]
    [InlineData("hinge", 1f)]
    [InlineData("slide", 2f)]
    [InlineData("energy", 3f)]
    [InlineData("wood", 7f)]
    public void EveryBoxLiesInsideTheDoorFootprint(string kind, float width)
    {
        var half = DoorGeometry.HalfExtents(width);
        foreach (var b in DoorGeometry.Closed(kind, width))
        {
            Assert.True(b.Centre.X - b.Size.X * 0.5f >= -half.X - 1e-4f, $"{b.Part} pokes out on −X");
            Assert.True(b.Centre.X + b.Size.X * 0.5f <= half.X + 1e-4f, $"{b.Part} pokes out on +X");
            Assert.True(b.Centre.Y - b.Size.Y * 0.5f >= -DoorGeometry.PostExtraHeight * 0.5f - 1e-4f, $"{b.Part} sinks under the floor");
            Assert.True(b.Centre.Y + b.Size.Y * 0.5f <= half.Y * 2f + 1e-4f, $"{b.Part} rises over the posts");
            Assert.True(b.Size.Z * 0.5f <= half.Z + 1e-4f, $"{b.Part} is deeper than the posts");
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1873: a module turns as one piece — cells, shapes (yaw + up-face), markers and ports.</summary>
public sealed class TemplateTransformTests
{
    private static StructureTemplate Sample()
    {
        var t = new StructureTemplate { Key = "sample", Tier = "small", Kind = "station", Kit = "k", Function = "room", Width = 4, Height = 3, Length = 6 };
        t.Cells.Add(new TemplateCell { X = 3, Y = 1, Z = 2, Kind = "block", Id = "iron_wall", Port = "door" }); // on the +X face
        t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 1, Kind = "block", Id = "bed", Shape = ShapeCode.Pack(BlockShape.BedHead, 0) });
        t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 2, Kind = "block", Id = "bed", Shape = ShapeCode.Pack(BlockShape.BedFoot, 0) });
        t.Cells.Add(new TemplateCell { X = 0, Y = 1, Z = 3, Kind = "block", Id = "ladder", Shape = ShapeCode.Pack((int)BlockShape.Panel, 0, 2) }); // up-face +X
        t.Cells.Add(new TemplateCell { X = 2, Y = 1, Z = 4, Kind = "marker", Id = "npc" });
        return t;
    }

    private static string Signature(StructureTemplate t)
        => t.Width + "x" + t.Height + "x" + t.Length + "|" + string.Join(";", t.Cells
            .Select(c => $"{c.X},{c.Y},{c.Z},{c.Kind},{c.Id},{c.Shape},{c.Port}").OrderBy(s => s, System.StringComparer.Ordinal));

    [Fact]
    public void QuarterTurn_SwapsTheDimensions_AndMovesAPlusXPortToPlusZ()
    {
        var r = TemplateTransform.RotateY(Sample(), 1);
        Assert.Equal((6, 3, 4), (r.Width, r.Height, r.Length));
        var port = Assert.Single(StructurePorts.Collect(r));
        Assert.Equal(PortFace.PlusZ, port.Face);
        Assert.Equal("door", port.Tag);
        Assert.Contains(r.Cells, c => c.Kind == "marker" && c.Id == "npc" && c.X == 1 && c.Y == 1 && c.Z == 2);
    }

    [Fact]
    public void ABedPair_StaysAPair_AndALadderKeepsItsWall()
    {
        var r = TemplateTransform.RotateY(Sample(), 1);
        var head = r.Cells.Single(c => c.Id == "bed" && ShapeCode.ShapeOf(c.Shape) == (int)BlockShape.BedHead);
        var foot = r.Cells.Single(c => c.Id == "bed" && ShapeCode.ShapeOf(c.Shape) == (int)BlockShape.BedFoot);
        Assert.True(FurnitureShapes.TryBedPartnerOffset(head.Shape, out int dx, out int dz));
        Assert.Equal((foot.X, foot.Z), (head.X + dx, head.Z + dz));
        Assert.Equal(1, ShapeCode.OrientationOf(head.Shape));

        var ladder = r.Cells.Single(c => c.Id == "ladder");
        Assert.Equal(4, ShapeCode.UpFaceOf(ladder.Shape)); // +X → +Z, like the wall it hangs on
        Assert.Equal((2, 0), (ladder.X, ladder.Z)); // (0, 3) → (L − 1 − 3, 0): the −X wall turns onto the −Z wall
    }

    [Fact]
    public void FourTurns_AreTheIdentity_AndZeroReturnsTheSameInstance()
    {
        var t = Sample();
        Assert.Same(t, TemplateTransform.RotateY(t, 0));
        Assert.Same(t, TemplateTransform.RotateY(t, 4));
        var four = TemplateTransform.RotateY(TemplateTransform.RotateY(TemplateTransform.RotateY(TemplateTransform.RotateY(t, 1), 1), 1), 1);
        Assert.Equal(Signature(t), Signature(four));
        Assert.Equal(Signature(TemplateTransform.RotateY(t, 3)), Signature(TemplateTransform.RotateY(t, -1)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(4, 3)]
    [InlineData(3, 5)]
    [InlineData(5, 2)]
    public void UpFaces_TurnAroundTheVertical(int upFace, int expected)
    {
        Assert.Equal(expected, TemplateTransform.TurnUpFace(upFace));
    }
}

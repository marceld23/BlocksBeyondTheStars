// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1873: ports — the docking rectangles on a module's outer walls — and the airtight check.</summary>
public sealed class StructurePortsTests
{
    /// <summary>A hollow iron box <paramref name="w"/> × <paramref name="h"/> × <paramref name="l"/> with an npc marker
    /// in the middle; <paramref name="ports"/> tag wall cells.</summary>
    internal static StructureTemplate Box(int w, int h, int l, params (int X, int Y, int Z, string Port)[] ports)
    {
        var t = new StructureTemplate { Key = "box", Tier = "small", Kind = "station", Kit = "k", Function = "room", Width = w, Height = h, Length = l };
        var portAt = ports.ToDictionary(p => new Vector3i(p.X, p.Y, p.Z), p => p.Port);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    bool shell = x == 0 || y == 0 || z == 0 || x == w - 1 || y == h - 1 || z == l - 1;
                    if (shell)
                    {
                        portAt.TryGetValue(new Vector3i(x, y, z), out var port);
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = "iron_wall", Port = port ?? string.Empty });
                    }
                }

        t.Cells.Add(new TemplateCell { X = w / 2, Y = 1, Z = l / 2, Kind = "marker", Id = "npc" });
        return t;
    }

    [Fact]
    public void ARectangleOfTaggedWallCells_IsOnePort_WithItsFaceSizeAndDoor()
    {
        var t = Box(5, 4, 5, (4, 1, 2, "door:energy"), (4, 1, 3, "door:energy"), (4, 2, 2, "door:energy"), (4, 2, 3, "door:energy"));
        var errors = new List<string>();
        var port = Assert.Single(StructurePorts.Collect(t, errors));
        Assert.Empty(errors);
        Assert.Equal(PortFace.PlusX, port.Face);
        Assert.Equal(("door", "energy"), (port.Tag, port.Door));
        Assert.Equal((1, 2, 2, 2), (port.A0, port.B0, port.SizeA, port.SizeB)); // y from 1, z from 2, 2 × 2
        Assert.Equal(4, port.Depth);
        Assert.Equal(4, port.Cells.Count);
        Assert.Equal(new Vector3i(1, 0, 0), port.Outward);
    }

    [Fact]
    public void Errors_CornerCell_LShape_BlockedInside_MarkerPort_UnknownDoor()
    {
        var corner = Box(5, 4, 5, (0, 1, 0, "door"));
        Assert.Contains(StructurePorts.Validate(corner), e => e.Contains("exactly one outer face"));

        var lShape = Box(5, 4, 5, (4, 1, 1, "door"), (4, 1, 2, "door"), (4, 2, 1, "door"));
        Assert.Contains(StructurePorts.Validate(lShape), e => e.Contains("filled rectangle"));

        var blocked = Box(5, 4, 5, (4, 1, 2, "door"));
        blocked.Cells.Add(new TemplateCell { X = 3, Y = 1, Z = 2, Kind = "block", Id = "iron_wall" }); // right behind the port
        Assert.Contains(StructurePorts.Validate(blocked), e => e.Contains("must be air"));

        var markerPort = Box(5, 4, 5);
        markerPort.Cells.Add(new TemplateCell { X = 2, Y = 1, Z = 1, Kind = "marker", Id = "vendor", Port = "door" });
        Assert.Contains(StructurePorts.Validate(markerPort), e => e.Contains("not on a marker"));

        var unknownDoor = Box(5, 4, 5, (4, 1, 2, "door:swing"));
        Assert.Contains(StructurePorts.Validate(unknownDoor), e => e.Contains("unknown port"));

        Assert.Empty(StructurePorts.Validate(Box(5, 4, 5))); // no ports at all is fine
    }

    [Fact]
    public void Ports_DockOnlyWithEqualTagAndSize_OnOppositeFaces()
    {
        var a = StructurePorts.Collect(Box(5, 4, 5, (4, 1, 2, "door"), (4, 2, 2, "door"))).Single();  // +X, 2 tall × 1 wide
        var b = StructurePorts.Collect(Box(5, 4, 5, (0, 1, 2, "door"), (0, 2, 2, "door"))).Single();  // −X, same size
        var c = StructurePorts.Collect(Box(5, 4, 5, (0, 1, 2, "wide"), (0, 2, 2, "wide"))).Single();  // other tag
        var d = StructurePorts.Collect(Box(5, 4, 5, (0, 1, 2, "door"))).Single();                     // other size
        var e = StructurePorts.Collect(Box(5, 4, 5, (4, 1, 2, "door"), (4, 2, 2, "door"))).Single();  // same face
        Assert.True(a.Compatible(b));
        Assert.True(b.Compatible(a));
        Assert.False(a.Compatible(c));
        Assert.False(a.Compatible(d));
        Assert.False(a.Compatible(e));
    }

    [Theory]
    [InlineData("door", true, "door", "slide")]
    [InlineData("wide:open", true, "wide", "open")]
    [InlineData(" Ladder:Hinge ", true, "ladder", "hinge")]
    [InlineData("", false, "", "slide")]
    [InlineData("door:x", false, "door", "x")]
    [InlineData("a:slide:c", false, "a", "slide")]
    public void PortGrammar_IsTagWithAnOptionalDoor(string port, bool ok, string tag, string door)
    {
        Assert.Equal(ok, StructurePorts.TryParse(port, out string t, out string d));
        Assert.Equal(tag, t);
        Assert.Equal(door, d);
    }

    // ---------------- the seal ----------------

    [Fact]
    public void ASealedBoxWithAPort_IsAirtight_AndAMissingWallBlockIsALeak()
    {
        var sealedBox = Box(5, 4, 5, (4, 1, 2, "door"), (4, 2, 2, "door"));
        Assert.Empty(StructureSeal.FindLeaks(sealedBox));

        var holed = Box(5, 4, 5, (4, 1, 2, "door"), (4, 2, 2, "door"));
        holed.Cells.RemoveAll(c => c.Kind == "block" && c.X == 0 && c.Y == 1 && c.Z == 1); // a hole in the −X wall
        var leaks = StructureSeal.FindLeaks(holed);
        Assert.Contains(new Vector3i(0, 1, 1), leaks);
        Assert.All(leaks, p => Assert.True(p.X == 0 || p.Y == 0 || p.Z == 0 || p.X == 4 || p.Y == 3 || p.Z == 4));
    }

    [Fact]
    public void AirOutsideTheHull_IsNotALeak_AndNothingToSeal_IsFine()
    {
        // An antenna beside the hull widens the bounding box; the air around it is outside and never reached.
        var greebled = Box(5, 4, 5, (4, 1, 2, "door"));
        greebled.Width = 8;
        greebled.Cells.Add(new TemplateCell { X = 7, Y = 1, Z = 2, Kind = "block", Id = "iron_wall" });
        Assert.Empty(StructureSeal.FindLeaks(greebled));

        // No markers and no ports: no used air, nothing to report even though the box is open.
        var empty = new StructureTemplate { Width = 3, Height = 3, Length = 3 };
        Assert.Empty(StructureSeal.FindLeaks(empty));
    }
}

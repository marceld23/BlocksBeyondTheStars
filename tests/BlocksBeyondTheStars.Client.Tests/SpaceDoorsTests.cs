// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2607: doors seen from space — which doors a design lists, the cells they fill (the spacewalk aims at them), that a
/// closed energy door shows its field out here, and where the spacewalk's door preview may show.
/// </summary>
public sealed class SpaceDoorsTests
{
    [Fact]
    public void DoorsOf_ReadsTheParallelArrays_AndCutsAMalformedPayloadShort()
    {
        var design = new SpaceShipDesign
        {
            DoorX = new[] { 2, 4 },
            DoorY = new[] { -1, 0 },
            DoorZ = new[] { 0, 3 },
            DoorKind = new[] { "slide" }, // one kind short: the second door is an energy door, the server's default
            DoorAxisX = new[] { false, true },
        };

        var doors = SpaceDoors.DoorsOf(design);

        Assert.Equal(2, doors.Count);
        Assert.Equal((new Vector3i(2, -1, 0), "slide", false), (doors[0].Cell, doors[0].Kind, doors[0].AxisX));
        Assert.Equal((new Vector3i(4, 0, 3), "energy", true), (doors[1].Cell, doors[1].Kind, doors[1].AxisX));

        design.DoorAxisX = new[] { true }; // a shorter axis list cuts the rest
        Assert.Single(SpaceDoors.DoorsOf(design));
        Assert.Empty(SpaceDoors.DoorsOf(null));
        Assert.Empty(SpaceDoors.DoorsOf(new SpaceShipDesign()));
    }

    [Fact]
    public void DoorwayCells_AreThreeHighAboveEachDoor()
    {
        var doors = SpaceDoors.DoorsOf(new SpaceShipDesign
        {
            DoorX = new[] { 2 },
            DoorY = new[] { -1 },
            DoorZ = new[] { 0 },
            DoorKind = new[] { "energy" },
            DoorAxisX = new[] { false },
        });

        var cells = SpaceDoors.DoorwayCells(doors);

        Assert.Equal(new[] { new Vector3i(2, -1, 0), new Vector3i(2, 0, 0), new Vector3i(2, 1, 0) },
            cells.OrderBy(c => c.Y).ToArray());
    }

    [Fact]
    public void HatchOf_IsNullForAnOlderServer_AndAnEmptyListMeansNoHatch()
    {
        // An older server: no HatchKnown flag — the client guesses (null), whatever the arrays say.
        Assert.Null(SpaceDoors.HatchOf(new SpaceShipDesign()));
        Assert.Null(SpaceDoors.HatchOf(null));
        Assert.Null(SpaceDoors.HatchOf(new SpaceShipDesign { HatchX = new[] { 1 }, HatchY = new[] { 1 }, HatchZ = new[] { 0 } }));

        var hatch = SpaceDoors.HatchOf(new SpaceShipDesign
        {
            HatchKnown = true,
            HatchX = new[] { 1, 2 },
            HatchY = new[] { 1, 1 },
            HatchZ = new[] { 0, 0 },
        });
        Assert.Equal(new[] { (1, 1, 0), (2, 1, 0) }, hatch!.ToArray());

        // A current server that names no hatch: an empty list, not "guess".
        var none = SpaceDoors.HatchOf(new SpaceShipDesign { HatchKnown = true });
        Assert.NotNull(none);
        Assert.Empty(none!);
    }

    [Theory]
    [InlineData("energy", true)]
    [InlineData("slide", false)]
    [InlineData("hinge", false)]
    [InlineData("wood", false)]
    [InlineData(null, false)]
    public void OnlyAClosedEnergyDoor_ShowsItsFieldInSpace(string? kind, bool field)
        => Assert.Equal(field, SpaceDoors.ShowsFieldInSpace(kind));

    [Fact]
    public void TheDoorPreview_ShowsOnTheOwnersAndAnAllysStation_Only()
    {
        Assert.True(SpaceDoors.MayBuildOnStation("Screelit", "Screelit", null));
        Assert.True(SpaceDoors.MayBuildOnStation("Layex", "Screelit", new[] { "Bob", "Layex" }));
        Assert.False(SpaceDoors.MayBuildOnStation("Layex", "Screelit", new[] { "Bob" }));
        Assert.False(SpaceDoors.MayBuildOnStation(string.Empty, "Screelit", Array.Empty<string>())); // a generated hull
        Assert.False(SpaceDoors.MayBuildOnStation("Screelit", null, null));
    }
}

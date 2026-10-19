// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2817: when the hatch of the player's own ship closes behind them, the airlock hisses — the outside stays outside. The
/// client tells the hatch (and a side door built into the outer wall) from a door between two rooms with
/// <see cref="ShipHatch.OpensToOutside"/>: a line across the doorway leaves the hull on one side.
/// </summary>
public sealed class ShipHatchOutsideTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>A closed box shell <paramref name="width"/> × 5 × <paramref name="length"/> (walls at the box edges, floor y = 0,
    /// roof y = 4), with optional openings cut into it.</summary>
    private static HashSet<(int X, int Y, int Z)> Shell(int width, int length)
    {
        var cells = new HashSet<(int X, int Y, int Z)>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y <= 4; y++)
            {
                for (int z = 0; z < length; z++)
                {
                    if (x == 0 || x == width - 1 || y == 0 || y == 4 || z == 0 || z == length - 1)
                    {
                        cells.Add((x, y, z));
                    }
                }
            }
        }

        return cells;
    }

    /// <summary>Cuts a doorway (one column, y 1…3) into the hull.</summary>
    private static void Cut(HashSet<(int X, int Y, int Z)> cells, int x, int z)
    {
        for (int y = 1; y <= 3; y++)
        {
            cells.Remove((x, y, z));
        }
    }

    private static bool Outside(HashSet<(int X, int Y, int Z)> cells, int x, int y, int z, bool wallAlongX, int width, int length)
        => ShipHatch.OpensToOutside((cx, cy, cz) => cells.Contains((cx, cy, cz)), x, y, z, wallAlongX, width, length);

    [Fact]
    public void TheRearHatch_LeadsOutside()
    {
        var hull = Shell(5, 7);
        Cut(hull, 2, 0);

        // The door's floor cell is (2, 1, 0); the client looks across it one row up.
        Assert.True(Outside(hull, 2, 2, 0, wallAlongX: true, 5, 7));
    }

    [Fact]
    public void ADoorBetweenTwoRooms_DoesNotLeadOutside()
    {
        // Two rooms one behind the other: a bulkhead at z = 4 with a doorway in it, no hatch in line with it.
        var hull = Shell(5, 9);
        for (int x = 1; x <= 3; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                hull.Add((x, y, 4));
            }
        }

        Cut(hull, 2, 4);

        Assert.False(Outside(hull, 2, 2, 4, wallAlongX: true, 5, 9));
    }

    [Fact]
    public void ASideDoorInTheOuterWall_LeadsOutside()
    {
        // A one-wide door the owner built into the +X wall: the wall runs along Z, so the line runs along X.
        var hull = Shell(5, 7);
        Cut(hull, 4, 3);

        Assert.True(Outside(hull, 4, 2, 3, wallAlongX: false, 5, 7));
    }

    [Fact]
    public void ADoorwayOutsideTheBox_LeadsOutside()
        => Assert.True(Outside(Shell(5, 7), -1, 2, 3, wallAlongX: false, 5, 7));

    [Fact]
    public void EveryAuthoredLayout_ItsRearDoorLeadsOutside()
    {
        int ships = 0;
        foreach (var ship in Content.Ships.Values)
        {
            if (Content.GetShipLayout(ship.Layout) is not { } layout)
            {
                continue;
            }

            ships++;
            var hull = layout.Cells
                .Where(c => c.Kind == "station" || c.Id is not ("door_slide" or "door_hinge" or "door_energy" or "hatch"))
                .Select(c => (c.X, c.Y, c.Z))
                .ToHashSet();
            var rearDoor = layout.Cells.Single(c => c.Id.StartsWith("door_", System.StringComparison.Ordinal) && c.Z == 0);

            Assert.True(Outside(hull, rearDoor.X, rearDoor.Y + 1, rearDoor.Z, wallAlongX: true, layout.Width, layout.Length),
                $"{layout.Key}: the rear door does not lead outside");
        }

        Assert.True(ships > 0);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2587: "I built a few leaves onto my ship — at the helm, in the exterior view, the blue energy area of my door went
/// beyond the wall and out of my ship!" The flight view's rear-hatch panel is placed by <see cref="ShipHatch"/>: on the
/// server's hatch cells when it sends them, else by a guess that decorations around the hull can no longer grow.
/// </summary>
public sealed class ShipHatchTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    // The starter box as the server builds it (halfX 2, halfZ 3, height 4): a 5 × 5 × 7 shell, the rear hatch a 3 × 3
    // hole in the z = 0 wall (x 1…3, y 1…3), two nozzles behind the rear corners. Design box 5 × 5 × 7 (Height = 5).
    private const int StarterWidth = 5, StarterHeight = 5, StarterLength = 7;

    private static HashSet<(int X, int Y, int Z)> Starter()
    {
        var cells = new HashSet<(int X, int Y, int Z)>();
        for (int x = 0; x <= 4; x++)
        {
            for (int y = 0; y <= 4; y++)
            {
                for (int z = 0; z <= 6; z++)
                {
                    bool shell = x == 0 || x == 4 || y == 0 || y == 4 || z == 0 || z == 6;
                    bool hatch = z == 0 && x >= 1 && x <= 3 && y >= 1 && y <= 3;
                    if (shell && !hatch)
                    {
                        cells.Add((x, y, z));
                    }
                }
            }
        }

        cells.Add((0, 1, -1)); // the nozzles poke out behind the stern
        cells.Add((4, 1, -1));
        return cells;
    }

    private static ShipHatch.Plan Guess(IEnumerable<(int X, int Y, int Z)> cells, IEnumerable<(int X, int Y, int Z)>? ownerDoors = null)
        => ShipHatch.Find(cells, StarterWidth, StarterHeight, StarterLength, serverHatch: null, ownerDoors);

    private static void AssertStarterHatch(ShipHatch.Plan plan)
    {
        var panel = Assert.Single(plan.Panels);
        Assert.Equal((1, 3, 1, 3, 0, 0), (panel.MinX, panel.MaxX, panel.MinY, panel.MaxY, panel.MinZ, panel.MaxZ));
        Assert.True(panel.FacesZ);

        // The marker glows on the sill under the hatch, on the stern wall itself (its rear face is z = 0).
        Assert.True(plan.HasMarker);
        Assert.Equal(2.5f, plan.MarkerX, 3);
        Assert.Equal(0.5f, plan.MarkerY, 3);
        Assert.Equal(0f, plan.MarkerZ, 3);
    }

    [Fact]
    public void UntouchedStarter_TheHatchIsItsThreeByThreeOpening()
        => AssertStarterHatch(Guess(Starter()));

    [Fact]
    public void OneStrayLeaf_BesideTheStern_DoesNotGrowThePanel()
    {
        // The old guess took the stern plane's bounding box with the leaf in it: the empty column beside the hull became
        // "hatch" and the panel reached out of the ship.
        var cells = Starter();
        cells.Add((-1, 2, 0));

        AssertStarterHatch(Guess(cells));
    }

    [Fact]
    public void SideHedgesAndARoofOfLeaves_KeepThePanelOnTheHatch()
    {
        // Screelit's ship: leaves down both sides and a strip across the roof. The roof row sits INSIDE the design box
        // (y = Height), so the flood from the plane's edge has to be what keeps it from becoming an opening.
        var cells = Starter();
        for (int z = -1; z <= 6; z++)
        {
            for (int y = 0; y <= 4; y++)
            {
                cells.Add((-1, y, z));
                cells.Add((5, y, z));
            }

            for (int x = -1; x <= 5; x += 2)
            {
                cells.Add((x, 5, z));
            }
        }

        AssertStarterHatch(Guess(cells));
    }

    [Fact]
    public void ALeafLayerBehindTheStern_IsNoWall_AndTheMarkerStaysOnTheHull()
    {
        // A whole layer of leaves behind the stern (z = −1): outside the design box, so the stern is still the z = 0
        // wall — and the marker stays on it instead of floating on the global rear of the cells.
        var cells = Starter();
        for (int x = -1; x <= 5; x++)
        {
            for (int y = 0; y <= 5; y++)
            {
                cells.Add((x, y, -1));
            }
        }

        AssertStarterHatch(Guess(cells));
    }

    [Fact]
    public void ASealedStern_GetsNoPanel_ButKeepsItsMarker()
    {
        var cells = Starter();
        for (int x = 1; x <= 3; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                cells.Add((x, y, 0));
            }
        }

        var plan = Guess(cells);

        Assert.Empty(plan.Panels);
        Assert.True(plan.HasMarker);
        Assert.Equal(2.5f, plan.MarkerX, 3); // the wall's bottom row, centred
        Assert.Equal(0.5f, plan.MarkerY, 3);
        Assert.Equal(0f, plan.MarkerZ, 3);
    }

    [Fact]
    public void AnOwnerBuiltEnergyDoorInTheStern_GetsNoSecondField()
    {
        // #2607: the owner walled the hatch up and hung his own energy door in the middle column. His door shows its own
        // field in flight — the guess must not cap the same hole with the hatch panel on top of it.
        var cells = Starter();
        for (int x = 1; x <= 3; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                if (x != 2)
                {
                    cells.Add((x, y, 0));
                }
            }
        }

        var withoutDoorList = Guess(cells);
        Assert.Equal((2, 2, 1, 3), (withoutDoorList.Panels[0].MinX, withoutDoorList.Panels[0].MaxX,
            withoutDoorList.Panels[0].MinY, withoutDoorList.Panels[0].MaxY)); // what the door list prevents

        var plan = Guess(cells, ownerDoors: new[] { (2, 1, 0) });
        Assert.Empty(plan.Panels);
    }

    [Fact]
    public void WithTheServersHatchCells_ThePanelSitsExactlyOnThem()
    {
        // A current server names the hatch: no guess, whatever hangs around the hull.
        var cells = Starter();
        cells.Add((-1, 2, 0));
        for (int x = -1; x <= 5; x++)
        {
            cells.Add((x, 5, 0));
        }

        var hatch = new List<(int X, int Y, int Z)>();
        for (int x = 1; x <= 3; x++)
        {
            for (int y = 1; y <= 3; y++)
            {
                hatch.Add((x, y, 0));
            }
        }

        var plan = ShipHatch.Find(cells, StarterWidth, StarterHeight, StarterLength, hatch);

        Assert.True(plan.FromServer);
        AssertStarterHatch(plan);
    }

    [Fact]
    public void WhenTheServerNamesNoHatch_NothingIsCapped_ButTheMarkerStaysOnTheStern()
    {
        // A current server sends an empty hatch list for a ship whose doorways all lead into the hull (a room built
        // behind the stern): the guess would still take the stern's hole for a hatch — it must not.
        var plan = ShipHatch.Find(Starter(), StarterWidth, StarterHeight, StarterLength, new List<(int X, int Y, int Z)>());

        Assert.Empty(plan.Panels);
        Assert.True(plan.FromServer);
        Assert.True(plan.HasMarker);
        Assert.Equal(0f, plan.MarkerZ, 3);
    }

    [Fact]
    public void ServerCells_OneCellWideSideDoor_StandsAcrossX_AndTheSternComesFirst()
    {
        // A self-built ship: the stern door and a one-wide door in the +X side wall (hull above and below the opening
        // along Z — its jambs say the wall runs along Z).
        var cells = new HashSet<(int X, int Y, int Z)> { (4, 1, 1), (4, 1, 3), (1, 1, -1), (3, 1, -1) };
        var hatch = new List<(int X, int Y, int Z)> { (4, 1, 2), (4, 2, 2), (4, 3, 2), (2, 1, 0), (2, 2, 0), (2, 3, 0) };
        cells.Add((1, 1, 0));
        cells.Add((3, 1, 0));

        var plan = ShipHatch.Find(cells, 5, 4, 5, hatch);

        Assert.Equal(2, plan.Panels.Count);
        Assert.Equal(0, plan.Panels[0].MinZ); // the rearmost first: the marker goes under it
        Assert.True(plan.Panels[0].FacesZ);
        Assert.Equal(4, plan.Panels[1].MinX);
        Assert.False(plan.Panels[1].FacesZ);
    }

    [Fact]
    public void EveryAuthoredLayout_GuessesItsRearDoor_InsideTheHull()
    {
        // An older server: the guess must find each authored ship's rear doorway (the z = 0 door marker) and keep the
        // panel strictly inside the stern wall.
        int ships = 0;
        foreach (var ship in Content.Ships.Values)
        {
            if (Content.GetShipLayout(ship.Layout) is not { } layout)
            {
                continue;
            }

            ships++;
            var solid = layout.Cells
                .Where(c => c.Kind == "station" || c.Id is not ("door_slide" or "door_hinge" or "door_energy" or "hatch"))
                .Select(c => (c.X, c.Y, c.Z))
                .ToList();
            var rearDoor = layout.Cells.Single(c => c.Id.StartsWith("door_", System.StringComparison.Ordinal) && c.Z == 0);

            var plan = ShipHatch.Find(solid, layout.Width, layout.Height, layout.Length);

            var panel = Assert.Single(plan.Panels);
            Assert.True(panel.MinX <= rearDoor.X && rearDoor.X <= panel.MaxX && panel.MinY <= rearDoor.Y && rearDoor.Y <= panel.MaxY,
                $"{layout.Key}: the panel misses the rear door");
            Assert.Equal(0, panel.MinZ);
            Assert.True(panel.MinX > 0 && panel.MaxX < layout.Width - 1 && panel.MaxY < layout.Height,
                $"{layout.Key}: the panel reaches the hull's edge");
        }

        Assert.True(ships > 0);
    }

    [Fact]
    public void WithoutADesignBox_TheCellsBoundsStandIn()
    {
        // An older payload without a size: the cells' own bounds are the box, and the untouched starter still works.
        var plan = ShipHatch.Find(Starter(), 0, 0, 0);

        var panel = Assert.Single(plan.Panels);
        Assert.Equal((1, 3, 1, 3, 0), (panel.MinX, panel.MaxX, panel.MinY, panel.MaxY, panel.MinZ));
    }

    [Fact]
    public void NoCells_NoPlan()
    {
        var plan = ShipHatch.Find(new List<(int X, int Y, int Z)>(), 5, 5, 7);

        Assert.Empty(plan.Panels);
        Assert.False(plan.HasMarker);
    }
}

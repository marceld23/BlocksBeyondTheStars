// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The lively places — where a lived-in station's hull carries its position lights and its dish in the flight view: on
/// the outermost cells of its four sides and over its middle, the same on every client, nothing on a hull too small.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class StationLifePlanTests
{
    private static Dictionary<Vector3i, BlockId> Box(int w, int h, int l)
    {
        var cells = new Dictionary<Vector3i, BlockId>();
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    cells[new Vector3i(x, y, z)] = new BlockId(1);
                }
            }
        }

        return cells;
    }

    [Fact]
    public void AHull_CarriesALightOnEachSide_AndADishOverItsMiddle()
    {
        var cells = Box(12, 5, 20);
        cells[new Vector3i(6, 5, 10)] = new BlockId(1); // a dome over the middle
        cells[new Vector3i(6, 6, 10)] = new BlockId(1);

        var plan = StationLifePlan.Build(cells);
        Assert.Equal(4, plan.NavLights.Count);
        var port = plan.NavLights.Single(l => l.Side == StationLifePlan.Port).Cell;
        var starboard = plan.NavLights.Single(l => l.Side == StationLifePlan.Starboard).Cell;
        var fore = plan.NavLights.Single(l => l.Side == StationLifePlan.Fore).Cell;
        var aft = plan.NavLights.Single(l => l.Side == StationLifePlan.Aft).Cell;
        Assert.Equal((0, 4), (port.X, port.Y));           // the outermost slab, its top row …
        Assert.Equal((11, 4), (starboard.X, starboard.Y));
        Assert.Equal((0, 4), (fore.Z, fore.Y));
        Assert.Equal((19, 4), (aft.Z, aft.Y));
        Assert.InRange(port.Z, 9, 10);                    // … and the middle of the side
        Assert.InRange(fore.X, 5, 6);
        Assert.Equal(new Vector3i(6, 6, 10), plan.DishCell); // the top of the dome
    }

    [Fact]
    public void ThePlan_DoesNotDependOnTheOrderOfTheCells()
    {
        var cells = Box(10, 4, 14);
        var reversed = new Dictionary<Vector3i, BlockId>();
        foreach (var pair in cells.Reverse())
        {
            reversed[pair.Key] = pair.Value;
        }

        var a = StationLifePlan.Build(cells);
        var b = StationLifePlan.Build(reversed);
        Assert.Equal(a.NavLights, b.NavLights);
        Assert.Equal(a.DishCell, b.DishCell);
    }

    [Fact]
    public void ATinyHull_AndAnEmptyOne_CarryNothing()
    {
        var tiny = StationLifePlan.Build(Box(4, 3, 4));
        Assert.Empty(tiny.NavLights);
        Assert.Null(tiny.DishCell);

        var none = StationLifePlan.Build(new Dictionary<Vector3i, BlockId>());
        Assert.Empty(none.NavLights);
        Assert.Null(none.DishCell);
    }
}

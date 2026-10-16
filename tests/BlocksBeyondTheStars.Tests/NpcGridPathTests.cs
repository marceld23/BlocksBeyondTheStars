// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The NPC grid search (#1866) on synthetic voxel grids: a flat floor at y = −1 with walls, steps and doorways.
/// A node is a feet cell that is standable (solid under it, the cell and the one above free); moves are the four
/// horizontal neighbours — level, one up, one or two down — never diagonal; the goal counts as reached beside it.
/// </summary>
public sealed class NpcGridPathTests
{
    /// <summary>A tiny voxel grid: every cell below y = 0 is floor, the rest air unless marked solid.</summary>
    private sealed class Grid
    {
        public readonly HashSet<Vector3i> Solid = new();
        public readonly HashSet<Vector3i> Doors = new();

        public bool IsSolid(Vector3i c) => c.Y < 0 || Solid.Contains(c);

        public bool Free(Vector3i c) => !IsSolid(c);

        public bool Standable(Vector3i c) => IsSolid(new Vector3i(c.X, c.Y - 1, c.Z)) && Free(c) && Free(new Vector3i(c.X, c.Y + 1, c.Z));

        public void Wall(int x, int z, int height = 3)
        {
            for (int y = 0; y < height; y++)
            {
                Solid.Add(new Vector3i(x, y, z));
            }
        }
    }

    private static List<Vector3i>? Find(Grid g, Vector3i start, Vector3i goal, int budget = 3000)
        => NpcGridPath.Find(start, goal, g.Standable, g.Free, g.Doors.Contains, new NpcGridPath.Limits(24, 6, budget), out _);

    private static void AssertWalkable(Grid g, Vector3i start, List<Vector3i> path)
    {
        var prev = start;
        foreach (var c in path)
        {
            Assert.True(g.Standable(c), $"{c} is not standable");
            Assert.Equal(1, System.Math.Abs(c.X - prev.X) + System.Math.Abs(c.Z - prev.Z)); // one horizontal step, never diagonal
            Assert.InRange(c.Y - prev.Y, -2, 1);
            prev = c;
        }
    }

    [Fact]
    public void AroundAWall_TheRouteGoesAroundIt_NeverThroughIt()
    {
        var g = new Grid();
        for (int z = -4; z <= 4; z++)
        {
            g.Wall(2, z); // a wall across the straight line, open past z = ±5
        }

        var start = new Vector3i(0, 0, 0);
        var goal = new Vector3i(5, 0, 0);
        var path = Find(g, start, goal);

        Assert.NotNull(path);
        AssertWalkable(g, start, path!);
        Assert.DoesNotContain(path!, c => c.X == 2 && c.Z >= -4 && c.Z <= 4);
        var end = path!.Last();
        Assert.True(System.Math.Abs(end.X - goal.X) + System.Math.Abs(end.Z - goal.Z) <= 1, $"ended at {end}, not beside the goal");
    }

    [Fact]
    public void StepsUpOne_ButNotTwo_AndDropsTwo()
    {
        var g = new Grid();
        // A one-high ledge along x = 3: standable on top at y = 1.
        for (int z = -6; z <= 6; z++)
        {
            g.Solid.Add(new Vector3i(3, 0, z));
            g.Solid.Add(new Vector3i(4, 0, z));
        }

        var up = Find(g, new Vector3i(0, 0, 0), new Vector3i(4, 1, 0));
        Assert.NotNull(up);
        AssertWalkable(g, new Vector3i(0, 0, 0), up!);

        // A two-high cliff: no way up, the search gives up inside the box.
        var cliff = new Grid();
        for (int z = -30; z <= 30; z++)
        {
            for (int x = 3; x <= 30; x++)
            {
                cliff.Solid.Add(new Vector3i(x, 0, z));
                cliff.Solid.Add(new Vector3i(x, 1, z));
            }
        }

        Assert.Null(Find(cliff, new Vector3i(0, 0, 0), new Vector3i(6, 2, 0)));

        // …but down two is fine.
        var down = Find(cliff, new Vector3i(6, 2, 0), new Vector3i(0, 0, 0));
        Assert.NotNull(down);
        AssertWalkable(cliff, new Vector3i(6, 2, 0), down!);
    }

    [Fact]
    public void AClosedRoom_WithADoorway_IsLeftThroughTheDoorway()
    {
        var g = new Grid();
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                if (System.Math.Abs(x) == 3 || System.Math.Abs(z) == 3)
                {
                    g.Wall(x, z);
                }
            }

        // Cut a doorway in the east wall; a door stands in it (air to the grid).
        g.Solid.Remove(new Vector3i(3, 0, 0));
        g.Solid.Remove(new Vector3i(3, 1, 0));
        g.Doors.Add(new Vector3i(3, 0, 0));

        var start = new Vector3i(0, 0, 0);
        var path = Find(g, start, new Vector3i(8, 0, 0));
        Assert.NotNull(path);
        AssertWalkable(g, start, path!);
        Assert.Contains(new Vector3i(3, 0, 0), path!);

        // Wall the doorway up again: sealed, no route.
        g.Solid.Add(new Vector3i(3, 0, 0));
        g.Solid.Add(new Vector3i(3, 1, 0));
        Assert.Null(Find(g, start, new Vector3i(8, 0, 0)));
    }

    [Fact]
    public void TheBudget_BoundsTheSearch()
    {
        var g = new Grid();
        // An open field, the goal far outside the box: the search stops at its budget instead of running away.
        var path = NpcGridPath.Find(new Vector3i(0, 0, 0), new Vector3i(500, 0, 0), g.Standable, g.Free, null,
            new NpcGridPath.Limits(24, 4, 200), out int expansions);
        Assert.Null(path);
        Assert.True(expansions <= 201, $"{expansions} expansions past a budget of 200");
    }

    [Fact]
    public void AGoalRightBeside_IsAnEmptyRoute_AndSimplifyKeepsOnlyTheTurns()
    {
        var g = new Grid();
        Assert.Empty(Find(g, new Vector3i(0, 0, 0), new Vector3i(1, 0, 0))!);

        var straight = new List<Vector3i> { new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(3, 0, 1), new(3, 0, 2) };
        var simple = NpcGridPath.Simplify(straight, new Vector3i(0, 0, 0));
        Assert.Equal(new[] { new Vector3i(3, 0, 0), new Vector3i(3, 0, 2) }, simple);
    }

    // ---------------- #2669: string-pulling ----------------

    private static List<Vector3i> Smooth(Grid g, Vector3i start, List<Vector3i> cells)
        => NpcGridPath.Smooth(cells, start, g.Standable, g.Doors.Contains);

    /// <summary>Every straight segment of a smoothed route clears a body of the smoothing width: sampled finely along the
    /// centre line and both swath edges, every touched cell is standable at the segment's level.</summary>
    private static void AssertSegmentsWalkable(Grid g, Vector3i start, List<Vector3i> waypoints)
    {
        var prev = start;
        foreach (var w in waypoints)
        {
            if (w.Y == prev.Y)
            {
                double dx = w.X - prev.X, dz = w.Z - prev.Z, len = System.Math.Sqrt((dx * dx) + (dz * dz));
                double nx = len > 0 ? -dz / len * NpcGridPath.SmoothHalfWidth : 0, nz = len > 0 ? dx / len * NpcGridPath.SmoothHalfWidth : 0;
                for (int s = 0; s <= 200; s++)
                {
                    double f = s / 200.0;
                    foreach (int side in new[] { -1, 0, 1 })
                    {
                        double x = prev.X + 0.5 + (dx * f) + (side * nx * 0.99), z = prev.Z + 0.5 + (dz * f) + (side * nz * 0.99);
                        var c = new Vector3i((int)System.Math.Floor(x), prev.Y, (int)System.Math.Floor(z));
                        Assert.True(g.Standable(c), $"segment {prev}→{w} crosses {c}");
                    }
                }
            }
            else
            {
                Assert.Equal(1, System.Math.Abs(w.X - prev.X) + System.Math.Abs(w.Z - prev.Z)); // a step stays the search's move
            }

            prev = w;
        }
    }

    [Fact]
    public void OpenSquare_TheDiagonalWalkIsOneStraightLine_NotAStaircase()
    {
        var g = new Grid();
        var start = new Vector3i(0, 0, 0);
        var cells = Find(g, start, new Vector3i(9, 0, 7))!;
        Assert.True(NpcGridPath.Simplify(cells, start).Count > 2, "the grid route itself is a staircase");

        var smooth = Smooth(g, start, cells);
        Assert.Equal(2, smooth.Count); // the first cell (the walker may stand anywhere in its own), then straight on
        Assert.Equal(cells[0], smooth[0]);
        Assert.Equal(cells[^1], smooth[1]);
        AssertSegmentsWalkable(g, start, smooth);
    }

    [Fact]
    public void AroundAWall_TheSmoothedRouteStillNeverCrossesIt()
    {
        var g = new Grid();
        for (int z = -4; z <= 4; z++)
        {
            g.Wall(4, z);
        }

        var start = new Vector3i(0, 0, 0);
        var cells = Find(g, start, new Vector3i(8, 0, 1))!;
        var smooth = Smooth(g, start, cells);
        Assert.True(smooth.Count < NpcGridPath.Simplify(cells, start).Count, "smoothing should drop some corners");
        AssertSegmentsWalkable(g, start, smooth);
    }

    [Fact]
    public void AWallCorner_IsNotGrazed_TheLineKeepsTheBodyWidthAway()
    {
        // A short wall: the straight line from start to goal would scrape its end block.
        var g = new Grid();
        g.Wall(3, 3);
        g.Wall(3, 4);
        g.Wall(3, 5);
        var start = new Vector3i(0, 0, 0);
        var cells = Find(g, start, new Vector3i(6, 0, 6))!;
        AssertSegmentsWalkable(g, start, Smooth(g, start, cells));
    }

    [Fact]
    public void ADoorway_IsPassedStraightThrough_NeverDiagonally()
    {
        var g = new Grid();
        for (int z = -6; z <= 6; z++)
        {
            g.Wall(3, z);
        }

        g.Solid.Remove(new Vector3i(3, 0, 0));
        g.Solid.Remove(new Vector3i(3, 1, 0));
        g.Doors.Add(new Vector3i(3, 0, 0));
        var start = new Vector3i(0, 0, -3);
        var cells = Find(g, start, new Vector3i(7, 0, 3))!;
        var smooth = Smooth(g, start, cells);
        AssertSegmentsWalkable(g, start, smooth);

        // A segment that reaches into or across the door column does so along the door's own row.
        var prev = start;
        foreach (var w in smooth)
        {
            if (prev.X != w.X && System.Math.Min(prev.X, w.X) <= 3 && System.Math.Max(prev.X, w.X) >= 3)
            {
                Assert.True(prev.Z == 0 && w.Z == 0, $"segment {prev}→{w} takes the doorway at an angle");
            }

            prev = w;
        }
    }

    [Fact]
    public void Steps_StayWaypoints_TheLevelStretchesAroundThemArePulledStraight()
    {
        var g = new Grid();
        // A platform one block up for x ≥ 5: the step stays an orthogonal move from the cell beside it.
        for (int x = 5; x <= 12; x++)
            for (int z = -8; z <= 8; z++)
            {
                g.Solid.Add(new Vector3i(x, 0, z));
            }

        var start = new Vector3i(0, 0, -4);
        var cells = Find(g, start, new Vector3i(10, 1, 4))!;
        var smooth = Smooth(g, start, cells);
        AssertSegmentsWalkable(g, start, smooth);
        Assert.Contains(smooth, w => w.Y == 1);
        Assert.Equal(cells[^1], smooth[^1]);
    }
}

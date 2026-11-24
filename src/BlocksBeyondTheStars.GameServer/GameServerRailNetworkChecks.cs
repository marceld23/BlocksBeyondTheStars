// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A listed rail line rides the curve its pylons make (#4014; the network's main file is GameServerRailNetwork.cs): the
/// Catmull-Rom spline through the pylon tops (<see cref="RailSpline"/>), not the straight runs between them. So its turns
/// are rounded before the pylons are set (<see cref="RailTurnRounding"/>), the curve is checked against the obstacles before
/// the line is pinned — every half block, the wagon's middle and both its sides, outside the line's own halls; in a town the
/// wagon's floor over the town's — and its corridor is carved along that curve (<see cref="ListedSplineCorridor"/>), the
/// wagon's whole box swept along it, so nothing the curve swings into stays in the wagon's way.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The curve is checked every this many blocks of arc.</summary>
    private const float ListedRailCheckStep = 0.5f;

    /// <summary>The wagon's box is swept along the curve every this many blocks of arc when the corridor is carved: finer than
    /// any check, so no cell a wagon touches falls between two of the sweep's samples.</summary>
    private const float ListedRailCarveStep = 0.25f;

    /// <summary>How far to each side of the curve the check looks (blocks): just inside the wagon's half width, so a wagon in a
    /// street, its side flush with a district's edge, passes.</summary>
    private const float ListedRailCheckHalfWidth = 1.45f;

    /// <summary>The cell size of the check's obstacle buckets (blocks).</summary>
    private const int ListedRailBucket = 64;

    /// <summary>The floor row of the town whose column (x, z) is — a planned town's or metropolis's claimed column, a classic
    /// town's whole box — or null outside every town (#4014: a listed line's ground there).</summary>
    private int? TownFloorAt(int x, int z)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            int lx = WorldConstants.WrapDeltaX(x - s.Min.X, circ), lz = z - s.Min.Z;
            if (lx < 0 || lz < 0 || lx > s.Max.X - s.Min.X || lz > s.Max.Z - s.Min.Z)
            {
                continue;
            }

            int floor = s.Layout is { HasColumnMap: true } layout ? layout.FloorAt(lx, lz) : 0;
            if (floor >= 0)
            {
                return s.GroundY + floor;
            }
        }

        return null;
    }

    /// <summary>The first cell of a free pylon column of a line: on the town floor for a listed line in a town (#4014), else
    /// over the ground or the bed of the water it stands in.</summary>
    private int IntercityPlanFoot(IntercityPlan plan, int x, int z)
        => plan.Listed && TownFloorAt(x, z) is int floor ? floor + 1 : PylonFoot(_world.Planet, x, z);

    /// <summary>A listed leg's points (the lead, the street run's end beyond the city before, the corners, the waypoints, the
    /// street run's start beyond this city, the lead) with their turns rounded: a short reach where the leg leaves or joins a
    /// street, so the arc starts outside the city's box, a longer one everywhere else.</summary>
    private static List<(double X, double Z)> RoundListedRailTurns(List<(double X, double Z)> points)
        => RailTurnRounding.Round(points, i => i == 1 || i == points.Count - 2 ? ListedRailExitRadius : ListedRailTurnRadius);

    /// <summary>Moves a listed leg's free columns from <paramref name="from"/> on that would stand in lava (a volcano's crater —
    /// the legs are routed round structures, not volcanoes) along the line to the nearest dry column, at most
    /// <see cref="LavaNudgeShare"/> of the way to either neighbour, so the line stays where it runs.</summary>
    private void NudgeListedPylonsOffLava(IntercityColumns c, int from)
    {
        var planet = _world.Planet;
        bool Lava((double X, double Z) p)
            => _generator.IsSurfaceLava(planet, WorldConstants.WrapX((int)Math.Floor(p.X), _world.Circumference), (int)Math.Floor(p.Z));
        for (int j = Math.Max(1, from); j < c.Count; j++)
        {
            var p = c.Cols[j];
            if (c.IsFixed(j) || !Lava(p))
            {
                continue;
            }

            var toward = new List<(double X, double Z)> { c.Cols[j - 1] };
            if (j + 1 < c.Count)
            {
                toward.Add(c.Cols[j + 1]);
            }

            for (int d = 1; d <= 16; d++)
            {
                if (NudgedOffLava(p, toward, d, Lava) is { } dry)
                {
                    c.Cols[j] = dry;
                    break;
                }
            }
        }
    }

    /// <summary>How far toward a neighbour a pylon may move off lava (a share of the span).</summary>
    private const double LavaNudgeShare = 0.45;

    /// <summary>The first dry spot <paramref name="d"/> blocks from <paramref name="p"/> toward one of its neighbours, null when
    /// both are in lava or too near.</summary>
    private static (double X, double Z)? NudgedOffLava((double X, double Z) p, List<(double X, double Z)> toward, int d,
        Func<(double X, double Z), bool> lava)
    {
        foreach (var q in toward)
        {
            double dx = q.X - p.X, dz = q.Z - p.Z, len = Math.Sqrt((dx * dx) + (dz * dz));
            if (d <= LavaNudgeShare * len)
            {
                var spot = (Math.Round(p.X + (dx * d / len)), Math.Round(p.Z + (dz * d / len)));
                if (!lava(spot))
                {
                    return spot;
                }
            }
        }

        return null;
    }

    /// <summary>The curve a line's trains ride: the spline through its pylon tops, as the rail state builds it.</summary>
    private RailSpline IntercitySplineOf(IntercityPlan plan)
        => plan.Spline ??= RailSpline.Build(plan.Pylons.Select(PylonTop).ToList(), false, _world.Circumference);

    /// <summary>Why the curve of a listed line would hit something (the first spot, with what it is and where), or null when it
    /// keeps clear: the wagon's middle and sides over an obstacle outside the line's own halls, or its floor under a town's.</summary>
    private string? ListedRailCurveProblem(IntercityPlan plan, ListedRailObstacles obstacles)
    {
        var buckets = ListedRailBuckets(obstacles.Exact);
        foreach (var (p, right) in IntercityCurveSamples(IntercitySplineOf(plan), 0f, float.MaxValue, ListedRailCheckStep))
        {
            if (plan.Stops.Exists(st => InStationBox(st, p.X, p.Z)))
            {
                continue; // the line's own hall: its track is in the street, its platform on the square
            }

            for (int side = -1; side <= 1; side++)
            {
                double x = p.X + (right.X * side * ListedRailCheckHalfWidth), z = p.Z + (right.Z * side * ListedRailCheckHalfWidth);
                if (ObstacleAt(buckets, x, z) is { } hit)
                {
                    return $"its curve runs over {hit.What} at {Spot(x, z)}";
                }

                if (TownFloorAt((int)Math.Floor(x), (int)Math.Floor(z)) is int floor && p.Y + RailRules.HoverHeight < floor + 1)
                {
                    return $"its curve runs under the town floor at {Spot(x, z)}";
                }
            }
        }

        return null;
    }

    /// <summary>The curve's points every <paramref name="step"/> blocks of arc from <paramref name="from"/> to
    /// <paramref name="to"/> (unwrapped), each with the horizontal unit vector to its right.</summary>
    private static IEnumerable<(Vector3f Point, (float X, float Z) Right)> IntercityCurveSamples(RailSpline spline, float from, float to, float step)
    {
        float end = Math.Min(to, spline.TotalArc);
        for (float s = Math.Max(0f, from); s <= end + 1e-3f; s += step)
        {
            var a = spline.PointAtUnwrapped(Math.Max(0f, s - 0.25f));
            var b = spline.PointAtUnwrapped(Math.Min(spline.TotalArc, s + 0.25f));
            float tx = b.X - a.X, tz = b.Z - a.Z, tl = (float)Math.Sqrt((tx * tx) + (tz * tz));
            if (tl > 1e-4f)
            {
                yield return (spline.PointAtUnwrapped(s), (tz / tl, -tx / tl));
            }
        }
    }

    /// <summary>Whether a point (X unwrapped) lies in a station's footprint or the block round it.</summary>
    private bool InStationBox(IntercityStation st, double x, double z)
    {
        double lx = WorldConstants.WrapDeltaX(x - st.Origin.X, _world.Circumference), lz = z - st.Origin.Z;
        return lx >= -1 && lz >= -1 && lx < st.W + 1 && lz < st.L + 1;
    }

    /// <summary>The obstacles by bucket (canonical bucket column, bucket row), each in every bucket it overlaps.</summary>
    private Dictionary<(int, int), List<ListedRailObstacle>> ListedRailBuckets(IEnumerable<ListedRailObstacle> obstacles)
    {
        var buckets = new Dictionary<(int, int), List<ListedRailObstacle>>();
        foreach (var o in obstacles)
        {
            for (int bx = (int)Math.Floor(o.X0 / ListedRailBucket); bx <= (int)Math.Floor((o.X1 - 1e-6) / ListedRailBucket); bx++)
            {
                for (int bz = (int)Math.Floor(o.Z0 / ListedRailBucket); bz <= (int)Math.Floor((o.Z1 - 1e-6) / ListedRailBucket); bz++)
                {
                    var key = (BucketColumn(bx), bz);
                    if (!buckets.TryGetValue(key, out var list))
                    {
                        buckets[key] = list = new List<ListedRailObstacle>();
                    }

                    list.Add(o);
                }
            }
        }

        return buckets;
    }

    /// <summary>A bucket column wrapped round the world.</summary>
    private int BucketColumn(int bx)
    {
        int columns = Math.Max(1, _world.Circumference / ListedRailBucket);
        return ((bx % columns) + columns) % columns;
    }

    /// <summary>The obstacle a point (X any lap) lies in, or null.</summary>
    private ListedRailObstacle? ObstacleAt(Dictionary<(int, int), List<ListedRailObstacle>> buckets, double x, double z)
    {
        double cx = WorldConstants.WrapX(x, _world.Circumference);
        if (!buckets.TryGetValue((BucketColumn((int)Math.Floor(cx / ListedRailBucket)), (int)Math.Floor(z / ListedRailBucket)), out var list))
        {
            return null;
        }

        foreach (var o in list)
        {
            double lx = WorldConstants.WrapDeltaX(cx - o.X0, _world.Circumference);
            if (lx >= 0 && lx < o.X1 - o.X0 && z >= o.Z0 && z < o.Z1)
            {
                return o;
            }
        }

        return null;
    }

    /// <summary>A world spot as longitude and latitude, for the log.</summary>
    private string Spot(double x, double z)
    {
        int circ = _world.Circumference;
        double lon = WorldConstants.WrapX(x, circ) * 360.0 / circ;
        lon = lon >= 180 ? lon - 360 : lon;
        double lat = z * 180.0 / WorldConstants.LatitudePeriodFor(circ);
        return string.Format(CultureInfo.InvariantCulture, "{0:F2}°E {1:F2}°N", lon, lat);
    }

    /// <summary>A listed line's corridor from pylon <paramref name="first"/> to pylon <paramref name="last"/> (#4014): the cells
    /// the player's link rule checks on each span, then the wagon's whole box (3 wide, 3.2 high over its floor) swept along
    /// the curve the train rides, a quarter block at a time, reaching to the box's edges.</summary>
    private IEnumerable<Vector3i> ListedSplineCorridor(IntercityPlan plan, int first, int last)
    {
        for (int j = first; j < last; j++)
        {
            foreach (var cell in LinkClearCells(plan.Pylons[j], plan.Pylons[j + 1]))
            {
                yield return cell;
            }
        }

        var spline = IntercitySplineOf(plan);
        float[] sides = { -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f };
        float[] ups = { 0f, 1f, 2f, 3f, RailRules.WagonHeight + 0.1f };
        foreach (var (p, right) in IntercityCurveSamples(spline, spline.ArcAtControl(first), spline.ArcAtControl(last), ListedRailCarveStep))
        {
            float floor = p.Y + RailRules.HoverHeight;
            foreach (float side in sides)
            {
                foreach (float up in ups)
                {
                    yield return new Vector3i((int)Math.Floor(p.X + (right.X * side)), (int)Math.Floor(floor + up), (int)Math.Floor(p.Z + (right.Z * side)));
                }
            }
        }
    }
}

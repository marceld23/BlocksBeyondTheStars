// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// How a listed rail line finds its way (#4014; the network's main file is GameServerRailNetwork.cs). Every stop's hall
/// stands on a rail-hall square of its city with its track in the street beside it (<see cref="MetroRailHalls"/>); between
/// two cities a <b>leg</b> runs from where the one hall's street run leaves its city to where the next one's begins, through
/// the stop's waypoints, around everything that stands in its way (<see cref="RailLegRouter"/>): the districts of every city
/// of the list (not their streets, not the land they leave to nature), the landmarks, the villages and every other box of the
/// footprint registry but the lines' own. The halls of all stops are chosen together (<see cref="ListedRailHallChain"/>): a
/// hall's street runs plus each leg's length and the turns where a leg leaves and joins a street — a leg that cannot be
/// routed is never taken, so a line whose cities leave no clear way between them is not planned at all (and says why).
/// The rounded turns and the final check of the curve the train rides are in GameServerRailNetworkChecks.cs.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far a leg keeps from an obstacle (blocks): the wagon's half width (1.5), what a rounded turn cuts into a
    /// corner it goes round (at most 2.5 for the right angle a box's corner turns the way, <see cref="RailTurnRounding"/>) and
    /// one and a half to spare.</summary>
    private const double ListedRailClearance = 5.5;

    /// <summary>The arc reach of a turn where a leg leaves or joins a city's street (blocks): the arc starts before the turn
    /// point, which lies <see cref="MetroRailHalls.ExitMargin"/> beyond the city's box, so it starts outside the box.</summary>
    private const double ListedRailExitRadius = 6.0;

    /// <summary>The arc reach of a turn round an obstacle's corner or at a waypoint (blocks).</summary>
    private const double ListedRailTurnRadius = 6.0;

    /// <summary>A turn where a leg leaves or joins a street costs this many blocks per degree (a straight way out is better).</summary>
    private const double ListedRailTurnCost = 0.1;

    /// <summary>How far round a leg's ends the router looks for obstacles (blocks).</summary>
    private const double ListedRailObstacleReach = 400.0;

    /// <summary>One obstacle of a listed line, in the world (X canonical, continuous coordinates — a column x covers
    /// [x, x + 1)) — and what it is, for the log and the tests.</summary>
    private readonly record struct ListedRailObstacle(double X0, double Z0, double X1, double Z1, string What);

    /// <summary>What the listed lines of a body keep clear of: the router's boxes (a city's districts joined into as few as
    /// the grid allows) and the exact ones the final check reads (one per district).</summary>
    private sealed class ListedRailObstacles
    {
        public List<ListedRailObstacle> Route { get; } = new();
        public List<ListedRailObstacle> Exact { get; } = new();
    }

    /// <summary>Every obstacle of the body's listed lines: the districts of every metropolis of the list, the landmarks, the
    /// villages, and every footprint but a city's own boxes and the lines' reservations.</summary>
    private ListedRailObstacles ListedRailObstaclesOf(PlaceListDefinition list)
    {
        var obstacles = new ListedRailObstacles();
        var cities = list.OfKind(PlaceListKinds.City).Select(p => ListedRailCityOf(list, p.Key)).OfType<ListedRailCity>().ToList();
        foreach (var city in cities)
        {
            foreach (var r in MetroRailHalls.MergedDistrictRects(city.Spec))
            {
                obstacles.Route.Add(CityObstacle(city, r, city.Name));
            }

            foreach (var r in MetroRailHalls.DistrictRects(city.Spec))
            {
                obstacles.Exact.Add(CityObstacle(city, r, city.Name));
            }
        }

        bool InACity(double x, double z) => cities.Exists(c =>
        {
            var (lx, lz) = ListedRailLocal(c, x, z);
            return lx >= -MetroRailHalls.ExitMargin && lz >= -MetroRailHalls.ExitMargin
                   && lx < c.Spec.Footprint + MetroRailHalls.ExitMargin && lz < c.Spec.Footprint + MetroRailHalls.ExitMargin;
        });

        foreach (var lm in _landmarks)
        {
            var o = new ListedRailObstacle(lm.Origin.X, lm.Origin.Z, lm.Origin.X + lm.Structure.Width, lm.Origin.Z + lm.Structure.Length, "landmark " + lm.Key);
            obstacles.Exact.Add(o);
            if (!InACity((o.X0 + o.X1) * 0.5, (o.Z0 + o.Z1) * 0.5))
            {
                obstacles.Route.Add(o); // a city's landmarks stand on its landmark squares, inside its districts
            }
        }

        foreach (var f in Footprints.Entries)
        {
            if (f.Kind != StructureTypeRules.Intercity && !InACity(f.Cx + 0.5, f.Cz + 0.5))
            {
                var o = new ListedRailObstacle(f.MinX, f.MinZ, f.MaxX + 1, f.MaxZ + 1, f.Kind + " " + f.Owner);
                obstacles.Route.Add(o);
                obstacles.Exact.Add(o);
            }
        }

        return obstacles;
    }

    /// <summary>A box-local rectangle of a city as a world obstacle.</summary>
    private ListedRailObstacle CityObstacle(ListedRailCity city, MetroRailHalls.Rect r, string name)
        => new(WorldConstants.WrapX(city.Origin.X + r.X0, _world.Circumference), city.Origin.Z + r.Z0,
            WorldConstants.WrapX(city.Origin.X + r.X0, _world.Circumference) + (r.X1 - r.X0 + 1), city.Origin.Z + r.Z1 + 1, name);

    /// <summary>The obstacles near a leg in the leg's own frame: X unwrapped round <paramref name="refX"/>, only the ones within
    /// <see cref="ListedRailObstacleReach"/> of the box round <paramref name="points"/>.</summary>
    private List<RailLegRouter.Box> ListedRailBoxesNear(IReadOnlyList<ListedRailObstacle> obstacles, double refX, IReadOnlyList<(double X, double Z)> points)
    {
        double x0 = points.Min(p => p.X) - ListedRailObstacleReach, x1 = points.Max(p => p.X) + ListedRailObstacleReach;
        double z0 = points.Min(p => p.Z) - ListedRailObstacleReach, z1 = points.Max(p => p.Z) + ListedRailObstacleReach;
        var boxes = new List<RailLegRouter.Box>();
        foreach (var o in obstacles)
        {
            double cx = (o.X0 + o.X1) * 0.5;
            double shift = refX + WorldConstants.WrapDeltaX(cx - refX, _world.Circumference) - cx;
            if (o.X1 + shift >= x0 && o.X0 + shift <= x1 && o.Z1 >= z0 && o.Z0 <= z1)
            {
                boxes.Add(new RailLegRouter.Box(o.X0 + shift, o.Z0, o.X1 + shift, o.Z1));
            }
        }

        return boxes;
    }

    /// <summary>The halls of a listed line's stops, chosen together, and every leg's way (world X unwrapped round the leg's
    /// start, the leg's start and end included). Null with the reason when a city offers no hall or no chain of legs fits.</summary>
    private (List<MetroRailHalls.Hall> Halls, List<List<(double X, double Z)>> Legs)? ListedRailChain(PlaceDefinition place,
        List<ListedRailCity> cities, List<IntercityPlan> earlier, ListedRailObstacles obstacles, out string why)
    {
        int n = cities.Count;
        var candidates = new List<List<MetroRailHalls.Hall>>();
        for (int k = 0; k < n; k++)
        {
            bool through = k > 0 && k < n - 1;
            var taken = ListedRailTaken(cities[k], earlier);
            var halls = MetroRailHalls.Candidates(cities[k].Spec, through).Where(h => !MetroRailHalls.TouchesTaken(h, through, taken)).ToList();
            if (halls.Count == 0)
            {
                why = $"no rail-hall square of '{cities[k].Key}' has an open street for the line";
                return null;
            }

            candidates.Add(halls);
        }

        var legs = new Dictionary<(int K, int A, int B), List<(double X, double Z)>?>();
        List<(double X, double Z)>? Leg(int k, int a, int b)
        {
            if (!legs.TryGetValue((k, a, b), out var way))
            {
                way = ListedRailLeg(place.Stops[k + 1], cities, k, candidates[k][a], candidates[k + 1][b], obstacles);
                legs[(k, a, b)] = way;
            }

            return way;
        }

        double HallCost(int k, int j) => k > 0 && k < n - 1 ? candidates[k][j].ExitRun + candidates[k][j].EntryRun : candidates[k][j].ExitRun;
        double LegCost(int k, int a, int b)
            => Leg(k, a, b) is { } way ? RailLegRouter.Length(way) + ListedRailTurnsCost(way, candidates[k][a], candidates[k + 1][b], k + 1 == n - 1) : double.PositiveInfinity;

        var pick = ListedRailHallChain.Choose(candidates.Select(c => c.Count).ToList(), HallCost, LegCost);
        if (pick is null)
        {
            why = $"no way between its cities keeps clear of the cities, landmarks and villages ({string.Join(" – ", cities.Select(c => c.Key))})";
            return null;
        }

        why = string.Empty;
        return (pick.Select((j, k) => candidates[k][j]).ToList(), pick.Take(n - 1).Select((a, k) => Leg(k, a, pick[k + 1])!).ToList());
    }

    /// <summary>The way of the leg from city <paramref name="k"/>'s hall <paramref name="from"/> to the next city's hall
    /// <paramref name="to"/>: from the street run's end beyond the one city through the stop's waypoints to the street run's
    /// start beyond the other, each stretch routed round the obstacles near it. Null when a stretch cannot be routed.</summary>
    private List<(double X, double Z)>? ListedRailLeg(PlaceRailStop stop, List<ListedRailCity> cities, int k, MetroRailHalls.Hall from,
        MetroRailHalls.Hall to, ListedRailObstacles obstacles)
    {
        bool last = k + 1 == cities.Count - 1;
        var start = ListedRailWorld(cities[k], from.Exit);
        double refX = start.X;
        double U(double x) => refX + WorldConstants.WrapDeltaX(x - refX, _world.Circumference);
        var points = new List<(double X, double Z)> { (start.X, start.Z) };
        points.AddRange(ListedRailWaypoints(stop).Select(p => (U(p.X), (double)p.Z)));
        var end = ListedRailWorld(cities[k + 1], last ? to.Exit : to.Entry);
        points.Add((U(end.X), end.Z));
        var boxes = ListedRailBoxesNear(obstacles.Route, refX, points);
        var way = new List<(double X, double Z)> { points[0] };
        for (int i = 0; i + 1 < points.Count; i++)
        {
            if (RailLegRouter.Route(points[i], points[i + 1], boxes, ListedRailClearance) is not { } stretch)
            {
                return null;
            }

            way.AddRange(stretch.Skip(1));
        }

        return way;
    }

    /// <summary>The cost of the turns where a leg leaves its street (the hall's heading) and joins the next one (into the
    /// hall: its heading for a through station, against it for the terminus the line ends at).</summary>
    private static double ListedRailTurnsCost(List<(double X, double Z)> way, MetroRailHalls.Hall from, MetroRailHalls.Hall to, bool toIsLast)
    {
        var (fx, fz) = RailStationGenerator.HeadingVector(from.Heading);
        var (tx, tz) = RailStationGenerator.HeadingVector(to.Heading);
        int sign = toIsLast ? -1 : 1;
        var a = way[0];
        var b = way[way.Count - 1];
        double leave = RailTurnRounding.TurnDegrees((a.X - fx, a.Z - fz), a, way[1]);
        double join = RailTurnRounding.TurnDegrees(way[way.Count - 2], b, (b.X + (sign * tx), b.Z + (sign * tz)));
        return (leave + join) * ListedRailTurnCost;
    }
}

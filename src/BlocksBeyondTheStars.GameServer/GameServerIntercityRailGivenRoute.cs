// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The given-route planner (#3984; the main file is GameServerIntercityRail.cs): a line handed in as its stops by position
/// (two or more, in line order) and the waypoints between them, planned into an <see cref="IntercityPlan"/> the builder builds
/// like the rolled line. Each stop is a station centred on its column, its floor at its given row or the ground there,
/// heading toward the next stop (the last one back toward the one before) unless the stop names a heading. The first and the
/// last stop are termini like the rolled line's stations; a stop between them is a through station — the line enters its
/// hall from behind over the end pylon and leaves over the exit pylon, and its stop plate stands in the hall's middle (#3985).
/// Between two stops the route runs from the one's lead through the waypoints to the other's lead, a pylon every ≤
/// <see cref="IntercityPylonSpacing"/> blocks (over open water every <see cref="IntercityLimits.SeaPylonSpacing"/> when the
/// limits allow more); the heights, the pylon and cut checks are the shared core under the limits the caller hands in. The
/// route keeps clear of nothing on its own (its stops and waypoints are where the data puts them). A place list's rail lines
/// feed it (GameServerRailNetwork.cs, #3985).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One stop of a given route.</summary>
    internal sealed class IntercityGivenStop
    {
        public IntercityGivenStop(string name, int x, int z, int heading = -1, int? floorY = null)
        {
            Name = name;
            X = x;
            Z = z;
            Heading = heading;
            FloorY = floorY;
        }

        /// <summary>The place the station serves (its map name: "{0} Station").</summary>
        public string Name { get; }

        /// <summary>The station's centre column.</summary>
        public int X { get; }

        public int Z { get; }

        /// <summary>The station's heading (0 = +X, 1 = −X, 2 = +Z, 3 = −Z); −1 = toward the next stop (the last stop: back
        /// toward the one before), along the dominant axis.</summary>
        public int Heading { get; }

        /// <summary>The station's floor row; null = the ground at its centre.</summary>
        public int? FloorY { get; }

        /// <summary>The hall's platforms (#3985): both, or one side's (a city's hall beside its street).</summary>
        public RailHallPlatforms Platforms { get; init; } = RailHallPlatforms.Both;

        /// <summary>The waypoints (world X, Z) the route runs through on its way from the previous stop to this one.</summary>
        public List<(int X, int Z)> Via { get; } = new();
    }

    /// <summary>A given route: the line index it is pinned under and its stops in line order.</summary>
    internal sealed class IntercityGivenRoute
    {
        public IntercityGivenRoute(int lineIndex) => LineIndex = lineIndex;

        public int LineIndex { get; }

        public List<IntercityGivenStop> Stops { get; } = new();
    }

    /// <summary>Plans a given route on the active world under <paramref name="limits"/>. Null (with the reason) when it has
    /// fewer than two stops, a station would stand in lava or on a build, or the shared core refuses it.</summary>
    internal IntercityPlan? PlanGivenIntercityRoute(IntercityGivenRoute route, IntercityLimits limits, out string why)
    {
        var planet = _world.Planet;
        if (route.Stops.Count < 2)
        {
            why = "a line needs two stops";
            return null;
        }

        var plan = new IntercityPlan { LineIndex = route.LineIndex };
        for (int i = 0; i < route.Stops.Count; i++)
        {
            var st = GivenStation(planet, route, i);
            if (!IntercityStationSeats(planet, st, out why))
            {
                return null;
            }

            plan.Stops.Add(st);
        }

        var c = GivenColumns(route, plan, limits, out why);
        if (c is null || !IntercityLatitudeFits(c, out why))
        {
            return null;
        }

        return TryFinishIntercityPlan(planet, c, limits, plan, out why) ? plan : null;
    }

    /// <summary>The station of stop <paramref name="i"/>: centred on its column at its floor, turned to its heading, with its
    /// platforms; a stop between the first and the last is a through station (its stop plate in the hall's middle, #3985).</summary>
    private IntercityStation GivenStation(PlanetType planet, IntercityGivenRoute route, int i)
    {
        var stop = route.Stops[i];
        int heading = stop.Heading is >= 0 and <= 3 ? stop.Heading : GivenHeading(route, i);
        var (w, l) = RailStationGenerator.Footprint(heading, stop.Platforms);
        int floorY = stop.FloorY ?? _generator.SurfaceHeight(planet, stop.X, stop.Z);
        var shape = new IntercityHallShape(stop.Platforms, Through: i > 0 && i < route.Stops.Count - 1);
        return BuildStation(stop.Name, heading, new Vector3i(stop.X - (w / 2), floorY, stop.Z - (l / 2)), "slope", shape: shape);
    }

    /// <summary>A stop's heading along the dominant axis: toward the next point of the route (the next stop's first waypoint,
    /// else the next stop), for the last stop back toward the point before it.</summary>
    private int GivenHeading(IntercityGivenRoute route, int i)
    {
        var stop = route.Stops[i];
        bool last = i == route.Stops.Count - 1;
        (int X, int Z) toward;
        if (last)
        {
            toward = stop.Via.Count > 0 ? stop.Via[stop.Via.Count - 1] : (route.Stops[i - 1].X, route.Stops[i - 1].Z);
        }
        else
        {
            var next = route.Stops[i + 1];
            toward = next.Via.Count > 0 ? next.Via[0] : (next.X, next.Z);
        }

        double dx = WorldConstants.WrapDeltaX((double)(toward.X - stop.X), _world.Circumference), dz = toward.Z - stop.Z;
        return Math.Abs(dx) >= Math.Abs(dz) ? (dx >= 0 ? 0 : 1) : (dz >= 0 ? 2 : 3);
    }

    /// <summary>The given route's columns: the first stop's end + exit, then per leg its lead, the waypoints and the next
    /// stop's lead, subdivided, then that stop's own pylons (a terminus: exit + end; a through station: end + exit). X is
    /// unwrapped along the line. Null when the free route is longer than the limit.</summary>
    private IntercityColumns? GivenColumns(IntercityGivenRoute route, IntercityPlan plan, IntercityLimits limits, out string why)
    {
        int circ = _world.Circumference;
        var c = new IntercityColumns();
        double near = plan.Stops[0].EndPylon.X;
        double U(double x) => near = near + WorldConstants.WrapDeltaX(x - near, circ);
        var first = plan.Stops[0];
        c.Add(U(first.EndPylon.X), first.EndPylon.Z, 0, first.Origin.Y);
        c.Add(U(first.ExitPylon.X), first.ExitPylon.Z, 0, first.Origin.Y);
        var lead = GivenLead(first, leaving: true);
        double free = 0;
        for (int i = 1; i < plan.Stops.Count; i++)
        {
            var st = plan.Stops[i];
            bool last = i == plan.Stops.Count - 1;
            var points = new List<(double X, double Z)> { (U(lead.X), lead.Z) };
            foreach (var (vx, vz) in route.Stops[i].Via)
            {
                points.Add((U(vx), vz));
            }

            var entry = GivenLead(st, leaving: last);
            points.Add((U(entry.X), entry.Z));
            free += limits.SeaPylonSpacing > IntercityPylonSpacing ? AddGivenLegOverSea(c, points, limits.SeaPylonSpacing) : AddGivenLeg(c, points);
            var (a, b) = last ? (st.ExitPylon, st.EndPylon) : (st.EndPylon, st.ExitPylon);
            c.Add(U(a.X), a.Z, i, st.Origin.Y);
            c.Add(U(b.X), b.Z, i, st.Origin.Y);
            lead = GivenLead(st, leaving: true);
        }

        plan.RouteLength = (float)free;
        why = free > limits.MaxRoute ? $"the route is {free:F0} blocks long" : string.Empty;
        return why.Length == 0 ? c : null;
    }

    /// <summary>A station's lead: <see cref="IntercityLead"/> blocks out of its exit along its heading (<paramref name="leaving"/>),
    /// or behind its end pylon (a through station's way in).</summary>
    private static (double X, double Z) GivenLead(IntercityStation st, bool leaving)
    {
        var (hx, hz) = RailStationGenerator.HeadingVector(st.Heading);
        var from = leaving ? st.ExitPylon : st.EndPylon;
        int sign = leaving ? 1 : -1;
        return (from.X + (sign * hx * IntercityLead), from.Z + (sign * hz * IntercityLead));
    }

    /// <summary>Adds a leg's free columns (its polyline subdivided, a pylon every ≤ <see cref="IntercityPylonSpacing"/>
    /// blocks, no point twice) and returns its length.</summary>
    private static double AddGivenLeg(IntercityColumns c, List<(double X, double Z)> points)
    {
        double length = 0;
        for (int s = 0; s + 1 < points.Count; s++)
        {
            var (p, q) = (points[s], points[s + 1]);
            double dx = q.X - p.X, dz = q.Z - p.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            length += len;
            int n = Math.Max(1, (int)Math.Ceiling(len / IntercityPylonSpacing));
            for (int k = s == 0 ? 0 : 1; k <= n; k++)
            {
                double t = k / (double)n;
                c.Add(Math.Round(p.X + dx * t), Math.Round(p.Z + dz * t));
            }
        }

        return length;
    }

    /// <summary>Adds a leg's free columns like <see cref="AddGivenLeg"/>, but a span over open water may run
    /// <paramref name="seaSpacing"/> blocks (#3985: the oceans of a listed line — within the player's auto-link range, so
    /// every span is one a player could have linked): a pylon every <see cref="IntercityPylonSpacing"/> blocks while either
    /// end of the next stretch or anything between them is land. Returns the leg's length.</summary>
    private double AddGivenLegOverSea(IntercityColumns c, List<(double X, double Z)> points, int seaSpacing)
    {
        var planet = _world.Planet;
        double length = 0;
        for (int s = 0; s + 1 < points.Count; s++)
        {
            var (p, q) = (points[s], points[s + 1]);
            double dx = q.X - p.X, dz = q.Z - p.Z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            length += len;
            if (s == 0)
            {
                c.Add(Math.Round(p.X), Math.Round(p.Z));
            }

            double at = 0;
            while (at < len - 1e-6)
            {
                double step = OpenWaterAlong(planet, p, dx / Math.Max(len, 1e-9), dz / Math.Max(len, 1e-9), at, Math.Min(len, at + seaSpacing))
                    ? seaSpacing : IntercityPylonSpacing;
                at = len - at <= step ? len : at + step;
                c.Add(Math.Round(p.X + dx * at / len), Math.Round(p.Z + dz * at / len));
            }
        }

        return length;
    }

    /// <summary>Whether the stretch from <paramref name="from"/> to <paramref name="to"/> blocks along a direction is open
    /// water all the way (sampled every two blocks on the line and a wagon's half width to each side).</summary>
    private bool OpenWaterAlong(PlanetType planet, (double X, double Z) p, double ux, double uz, double from, double to)
    {
        for (double t = from; t <= to + 1e-6; t += 2.0)
        {
            for (int side = -1; side <= 1; side++)
            {
                int x = (int)Math.Floor(p.X + ux * t + 0.5 + uz * side * 1.5);
                int z = (int)Math.Floor(p.Z + uz * t + 0.5 - ux * side * 1.5);
                if (!_generator.TryGetWaterSurface(planet, WorldConstants.WrapX(x, _world.Circumference), z, out _, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Test seam (#3984): plans a given route, pins it and builds it on the active world as a load would (the stamp
    /// step, then the graph, the rail lines and the public train). Null (with the reason) when it does not plan.</summary>
    internal IntercityPlan? PlaceGivenIntercityRouteForTest(IntercityGivenRoute route, IntercityLimits limits, out string why)
    {
        var plan = PlanGivenIntercityRoute(route, limits, out why);
        if (plan is null)
        {
            return null;
        }

        PinIntercityPlan(plan);
        SavePlacementRecords();
        StampIntercityRail(rolled: true);
        bool generated = EnsureIntercityGraph();
        RebuildRailLines();
        if (EnsureIntercityTrain() || generated)
        {
            SaveRails();
        }

        return plan;
    }
}

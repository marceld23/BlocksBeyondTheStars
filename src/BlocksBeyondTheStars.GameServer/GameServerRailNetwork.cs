// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The rail network of a body's place list (#3985; Earth's Atlantic and Orient lines): every <c>rail</c> place is a line
/// through cities of the list, planned once by the given-route planner (GameServerIntercityRailGivenRoute.cs) and built by
/// the intercity builder like every other line — its own line index (<see cref="ListedRailLineIndexBase"/> + its position
/// among the list's lines), its records, its public train.
/// <list type="bullet">
/// <item><b>Its stations</b> stand in the metropolises (GameServerMetropolis.cs), each on a rail-hall square of the city's
/// pinned district map: <see cref="MetroRailHalls"/> picks the square, the street beside it and the direction the line leaves
/// along that street — a side-platform hall on the square's edge, the track in the street, the line straight out of the city
/// (a city's streets are open, its districts built to their edges). Two lines in one city take different squares.</item>
/// <item><b>Its route</b> runs from each city's street run through the place's waypoints to the next city's, under
/// <see cref="IntercityLimits.Listed"/>: no length cap, pylons on the sea bed however deep, a pylon every 32 blocks over
/// open water.</item>
/// <item><b>Its own step.</b> The lines plan after the cities (the stamp-chain step, whatever the settlement options say) on
/// every save generation (a map world's rule), from the cities' records alone, and are replayed from their own records on
/// every later load. A line that cannot be planned (a city that is not there) is left off that load and tried again on the
/// next; nothing is pinned for it.</item>
/// <item><b>Its voxels</b> are stamped section by section the first time a player comes near (GameServerRailSections.cs);
/// the graph, the line and its public train exist for the whole line from the first load.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The line index of a place list's first rail line; its stations are station records
    /// <c>(100 + i) × 64 + stop</c>. Far above the rolled line (0) and the given lines below it.</summary>
    internal const int ListedRailLineIndexBase = 100;

    /// <summary>Blocks between two pylons of a listed line over open water (#3985): the player's auto-link range.</summary>
    private const int IntercitySeaPylonSpacing = (int)RailRules.AutoLinkRange;

    /// <summary>A city a listed line stops at, from its metropolis record: its key, name, box corner (Y = the floor) and
    /// district map.</summary>
    private sealed record ListedRailCity(string Key, string Name, Vector3i Origin, CityLayoutSpec Spec)
    {
        public (double X, double Z) Centre => (Origin.X + (Spec.Footprint / 2.0), Origin.Z + (Spec.Footprint / 2.0));
    }

    /// <summary>The body's listed lines, in list order: replayed from their records, or planned and pinned now.</summary>
    private List<IntercityPlan> ListedRailLines(PlanetType planet)
    {
        var lines = new List<IntercityPlan>();
        if (_content.PlacesFor(planet.Key) is not { } list)
        {
            return lines;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int i = 0;
        foreach (var place in list.OfKind(PlaceListKinds.Rail))
        {
            if (ListedRailLine(list, place, ListedRailLineIndexBase + i++, lines) is { } plan)
            {
                plan.Listed = true;
                plan.NameKey = place.NameKey;
                lines.Add(plan);
            }
        }

        _listedRailMillis = clock.ElapsedMilliseconds;
        return lines;
    }

    /// <summary>How long the last load took to plan or replay the listed lines (milliseconds).</summary>
    private long _listedRailMillis;

    /// <summary>Test seam (#3985): <see cref="_listedRailMillis"/>.</summary>
    internal long ListedRailMillisForTest => _listedRailMillis;

    /// <summary>One listed line: replayed from its record, or — on a body that never planned it — planned and pinned. Null
    /// when it is not built this load.</summary>
    private IntercityPlan? ListedRailLine(PlaceListDefinition list, PlaceDefinition place, int index, List<IntercityPlan> earlier)
    {
        var rec = FindPlacementRecord(IntercityLineKind, index);
        if (rec is not null)
        {
            var replayed = rec.Placed ? ReplayIntercityLine(rec) : null;
            if (rec.Placed && replayed is null)
            {
                _log.Warn($"The rail line '{place.Key}' on '{_world.LocationId}' could not be replayed — it stays off this load.");
            }

            return replayed;
        }

        var route = ListedRailRoute(list, place, index, earlier, out string why);
        var plan = route is null ? null : PlanGivenIntercityRoute(route, IntercityLimits.Listed, out why);
        if (plan is null)
        {
            _log.Warn($"The rail line '{place.Key}' on '{_world.LocationId}' cannot be planned ({why}) — it stays off this load.");
            return null;
        }

        PinIntercityPlan(plan);
        SavePlacementRecords();
        return plan;
    }

    /// <summary>A listed line as a given route: each stop's hall in its city (<see cref="MetroRailHalls.Choose"/>, the stops
    /// in line order, each looking at the previous stop's way out and the next stop), and per leg the previous city's way out
    /// along its street, the place's waypoints and this city's way in.</summary>
    private IntercityGivenRoute? ListedRailRoute(PlaceListDefinition list, PlaceDefinition place, int index, List<IntercityPlan> earlier, out string why)
    {
        var cities = new List<ListedRailCity>();
        foreach (var stop in place.Stops)
        {
            if (ListedRailCityOf(list, stop.City) is not { } city)
            {
                why = $"the city '{stop.City}' is not on this body";
                return null;
            }

            cities.Add(city);
        }

        var halls = ListedRailHalls(place, cities, earlier, out why);
        if (halls is null)
        {
            return null;
        }

        var route = new IntercityGivenRoute(index);
        for (int k = 0; k < cities.Count; k++)
        {
            var (city, hall) = (cities[k], halls[k]);
            var (cx, cz) = hall.Centre;
            var stop = new IntercityGivenStop(city.Name, WorldConstants.WrapX(city.Origin.X + cx, _world.Circumference), city.Origin.Z + cz,
                hall.Heading, city.Origin.Y)
            { Platforms = hall.Platforms };
            if (k > 0)
            {
                stop.Via.Add(ListedRailWorld(cities[k - 1], halls[k - 1].Exit));
                stop.Via.AddRange(ListedRailWaypoints(place.Stops[k]));
                stop.Via.Add(ListedRailWorld(city, k == cities.Count - 1 ? hall.Exit : hall.Entry));
            }

            route.Stops.Add(stop);
        }

        return route;
    }

    /// <summary>Each stop's hall, in line order: a terminus looks toward its one neighbour, a through station toward both —
    /// the previous stop's way out (already chosen) and the next stop's first waypoint or its city's centre.</summary>
    private List<MetroRailHalls.Hall>? ListedRailHalls(PlaceDefinition place, List<ListedRailCity> cities, List<IntercityPlan> earlier, out string why)
    {
        var halls = new List<MetroRailHalls.Hall>();
        for (int k = 0; k < cities.Count; k++)
        {
            var city = cities[k];
            (double X, double Z)? prev = null, next = null;
            if (k > 0)
            {
                var via = ListedRailWaypoints(place.Stops[k]);
                var from = via.Count > 0 ? via[^1] : ListedRailWorld(cities[k - 1], halls[k - 1].Exit);
                prev = ListedRailLocal(city, from.X, from.Z);
            }

            if (k < cities.Count - 1)
            {
                var via = ListedRailWaypoints(place.Stops[k + 1]);
                var (tx, tz) = via.Count > 0 ? (via[0].X, (double)via[0].Z) : cities[k + 1].Centre;
                next = ListedRailLocal(city, tx, tz);
            }

            if (MetroRailHalls.Choose(city.Spec, prev, next, ListedRailTaken(city, earlier)) is not { } hall)
            {
                why = $"no rail-hall square of '{city.Key}' has an open street for the line";
                return null;
            }

            halls.Add(hall);
        }

        why = string.Empty;
        return halls;
    }

    /// <summary>A city place of the list as the line sees it: its metropolis record (settlement
    /// <see cref="CityPlaceIndexBase"/> + its position among the list's cities) with a metropolis layout; null when the body
    /// has none.</summary>
    private ListedRailCity? ListedRailCityOf(PlaceListDefinition list, string key)
    {
        int i = list.OfKind(PlaceListKinds.City).Select(p => p.Key).ToList().IndexOf(key);
        var rec = i < 0 ? null : FindPlacementRecord("settlement", CityPlaceIndexBase + i);
        return rec is { Placed: true } && CityLayoutSpec.TryParse(rec.KitLayout, out var spec) && spec.Metro
            ? new ListedRailCity(key, rec.Name, new Vector3i(rec.X, rec.GroundY, rec.Z), spec)
            : null;
    }

    /// <summary>What earlier lines took in a city (box-local): their halls there and the spans of their street runs.</summary>
    private List<MetroRailHalls.Rect> ListedRailTaken(ListedRailCity city, List<IntercityPlan> earlier)
    {
        var taken = new List<MetroRailHalls.Rect>();
        int box = city.Spec.Footprint;
        bool Inside((double X, double Z) p) => p.X >= -MetroRailHalls.ExitMargin && p.Z >= -MetroRailHalls.ExitMargin
                                               && p.X < box + MetroRailHalls.ExitMargin && p.Z < box + MetroRailHalls.ExitMargin;
        foreach (var plan in earlier)
        {
            foreach (var st in plan.Stops)
            {
                var (x, z) = ListedRailLocal(city, st.Origin.X, st.Origin.Z);
                if (Inside((x, z)))
                {
                    var footprint = new MetroRailHalls.Rect((int)x, (int)z, (int)x + st.W - 1, (int)z + st.L - 1);
                    taken.Add(new MetroRailHalls.Rect(footprint.X0 - 1, footprint.Z0 - 1, footprint.X1 + 1, footprint.Z1 + 1));
                    if (MetroRailHalls.SquareUnder(city.Spec, footprint) is { } square)
                    {
                        taken.Add(square); // one hall per square: Paris's second line takes its other square
                    }
                }
            }

            for (int j = 0; j + 1 < plan.Pylons.Count; j++)
            {
                var a = ListedRailLocal(city, plan.Pylons[j].X, plan.Pylons[j].Z);
                var b = ListedRailLocal(city, plan.Pylons[j + 1].X, plan.Pylons[j + 1].Z);
                if (Inside(a) || Inside(b))
                {
                    taken.Add(new MetroRailHalls.Rect((int)Math.Min(a.X, b.X) - 2, (int)Math.Min(a.Z, b.Z) - 2, (int)Math.Max(a.X, b.X) + 2, (int)Math.Max(a.Z, b.Z) + 2));
                }
            }
        }

        return taken;
    }

    /// <summary>A stop's waypoints as world columns.</summary>
    private List<(int X, int Z)> ListedRailWaypoints(PlaceRailStop stop)
        => stop.Via.Select(p => PlaceRules.BlockOf(p[0], p[1], _world.Circumference)).ToList();

    /// <summary>A box-local point of a city as a world column (X wrapped).</summary>
    private (int X, int Z) ListedRailWorld(ListedRailCity city, (int X, int Z) local)
        => (WorldConstants.WrapX(city.Origin.X + local.X, _world.Circumference), city.Origin.Z + local.Z);

    /// <summary>A world column in a city's box-local frame (X the short way round the seam).</summary>
    private (double X, double Z) ListedRailLocal(ListedRailCity city, double x, double z)
        => (WorldConstants.WrapDeltaX(x - city.Origin.X, _world.Circumference), z - city.Origin.Z);

    /// <summary>The name key of a generated line's place (empty for every line without one).</summary>
    private string IntercityNameKeyOf(RailLine line)
    {
        foreach (var state in Intercity.Lines)
        {
            if (state.Plan.NameKey.Length > 0 && state.Pylons.Count > 0 && line.Pylons.Contains(state.Pylons[0]))
            {
                return state.Plan.NameKey;
            }
        }

        return string.Empty;
    }

    /// <summary>Test seam (#3985): the line index of a place list's i-th rail line.</summary>
    internal static int ListedRailLineIndexForTest(int i) => ListedRailLineIndexBase + i;

    /// <summary>Test seam (#3985): the record index of stop <paramref name="stop"/> of line <paramref name="line"/>.</summary>
    internal static int IntercityStationRecordIndexForTest(int line, int stop) => IntercityStationRecordIndex(line, stop);
}

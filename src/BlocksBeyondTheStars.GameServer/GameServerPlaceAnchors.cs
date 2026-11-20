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

/// <summary>What a villager can go to and do something at (GameServerPlaceAnchors.cs).</summary>
internal enum PlaceAnchorKind : byte
{
    /// <summary>The meeting place: the plaza or the green.</summary>
    Plaza,

    /// <summary>In front of the market stall of the settlement's own vendor.</summary>
    Market,

    /// <summary>At the notice board.</summary>
    Board,

    /// <summary>Looking in at the craftsman's.</summary>
    Workshop,

    /// <summary>Looking in at the greenhouse.</summary>
    Greenhouse,

    /// <summary>At the edge of a field.</summary>
    Field,

    /// <summary>At the fence of a pen.</summary>
    Pen,

    /// <summary>At a well (a lively place's street furniture, like the four below).</summary>
    Well,
    Bench,
    Fire,
    Stall,

    /// <summary>Where a road leaves the settlement: a look out over the land.</summary>
    Exit,

    /// <summary>A street lantern — only the night watch walks these.</summary>
    Lamp,
}

/// <summary>One anchor of a settlement: its kind and the spot to stand on (feet).</summary>
internal readonly record struct PlaceAnchor(PlaceAnchorKind Kind, Vector3f Spot);

/// <summary>
/// The lively places, the anchors: per settlement the list of spots where somebody can go and do something — the plaza,
/// the market stall, the notice board, the workshop, the greenhouse, the edges of the fields and the pens, where a road
/// leaves the village, and in a lively place (terrain generation 46) its benches, well, stalls, fire place and lanterns.
/// The list is read from the markers the settlement has anyway and from its fields and pens, so it exists in every world;
/// nothing is stored. Every spot is one a person can stand on right now (<see cref="StandableSpot"/>): the list is built
/// when somebody first needs it and again every couple of minutes, because a player may have built on a spot — and because
/// a settlement whose chunks were not loaded yet has no spot to offer.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds a settlement's anchors are kept before they are read again.</summary>
    private const double PlaceAnchorLifetime = 120.0;

    /// <summary>Seconds after which an empty list is tried again (the chunks may have loaded since).</summary>
    private const double PlaceAnchorRetry = 10.0;

    /// <summary>The most anchors of one kind a settlement keeps (a city has dozens of lanterns).</summary>
    private const int PlaceAnchorsPerKind = 12;

    /// <summary>The settlement of the active world an NPC belongs to (by its name), or null.</summary>
    private SettlementInstance? SettlementOfNpc(ServerNpc npc)
    {
        if (npc.Settlement.Length == 0)
        {
            return null;
        }

        foreach (var s in _settlements)
        {
            if (string.Equals(s.Name, npc.Settlement, StringComparison.Ordinal))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>The anchors of a settlement (built on first use, refreshed every <see cref="PlaceAnchorLifetime"/>).</summary>
    private List<PlaceAnchor> PlaceAnchorsOf(SettlementInstance s)
    {
        if (s.Anchors is { } known && _uptime < s.AnchorsUntil)
        {
            return known;
        }

        var anchors = BuildPlaceAnchors(s);
        s.Anchors = anchors;
        s.AnchorsUntil = _uptime + (anchors.Count == 0 ? PlaceAnchorRetry : PlaceAnchorLifetime);
        return anchors;
    }

    /// <summary>The anchor a marker stands for, or null: its kind, how many columns around the marker the spot may lie,
    /// and how far it keeps off the marker itself. A post is somebody's place — the visitor stands a step or two away
    /// from it; a piece of street furniture is marked where one stands.</summary>
    private static (PlaceAnchorKind Kind, int Reach, float KeepOff)? MarkerAnchor(string marker) => marker switch
    {
        "vendor" => (PlaceAnchorKind.Market, 3, 1.3f),
        "mission_board" => (PlaceAnchorKind.Board, 3, 1.3f),
        SettlementGenerator.WorkshopMarker => (PlaceAnchorKind.Workshop, 3, 1.3f),
        "greenhouse" => (PlaceAnchorKind.Greenhouse, 3, 1.3f),
        SettlementPlanBaker.WellMarker => (PlaceAnchorKind.Well, 1, 0f),
        SettlementPlanBaker.BenchMarker => (PlaceAnchorKind.Bench, 1, 0f),
        SettlementPlanBaker.FireMarker => (PlaceAnchorKind.Fire, 2, 1.3f),
        SettlementPlanBaker.StallMarker => (PlaceAnchorKind.Stall, 1, 0f),
        SettlementPlanBaker.LampMarker => (PlaceAnchorKind.Lamp, 1, 0.8f),
        _ => null,
    };

    private List<PlaceAnchor> BuildPlaceAnchors(SettlementInstance s)
    {
        var anchors = new List<PlaceAnchor>();
        var counts = new int[(int)PlaceAnchorKind.Lamp + 1]; // Lamp is the last kind
        var taken = new HashSet<Vector3i>();
        void Add(PlaceAnchorKind kind, Vector3f? spot)
        {
            if (spot is { } at && counts[(int)kind] < PlaceAnchorsPerKind && taken.Add(at.ToBlock()))
            {
                anchors.Add(new PlaceAnchor(kind, at));
                counts[(int)kind]++;
            }
        }

        // The meeting place: four spots round its middle, so a handful of people stand in a loose group, not on one cell.
        var plaza = SettlementSquareCentre(s);
        foreach (var (dx, dz) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2) })
        {
            Add(PlaceAnchorKind.Plaza, AnchorSpotNear(new Vector3f(plaza.X + dx, plaza.Y, plaza.Z + dz), 1, 0f, plaza));
        }

        foreach (var (type, pos) in s.Markers)
        {
            if (MarkerAnchor(type) is { } anchor)
            {
                var feet = MarkerFeet(s, pos);
                Add(anchor.Kind, AnchorSpotNear(feet, anchor.Reach, anchor.KeepOff, feet));
            }
        }

        foreach (var (type, pos, data) in s.DataMarkers)
        {
            if (type == SettlementPlanBaker.RoadExitMarker)
            {
                Add(PlaceAnchorKind.Exit, AnchorSpotNear(MarkerFeet(s, pos), 1, 0f, plaza));
            }
            else if (type == SettlementPlanBaker.FieldMarker || type == SettlementPlanBaker.PenMarker)
            {
                var settings = MarkerSettings.Parse(data);
                Add(type == SettlementPlanBaker.FieldMarker ? PlaceAnchorKind.Field : PlaceAnchorKind.Pen,
                    FarmEdgeSpot(pos.ToBlock(), settings.GetInt("w", 1), settings.GetInt("l", 1), plaza));
            }
        }

        return anchors;
    }

    /// <summary>
    /// The standable spot nearest <paramref name="toward"/> among the cells within <paramref name="reach"/> columns of
    /// <paramref name="at"/> (one row up or down allowed) that lie at least <paramref name="keepOff"/> blocks from it, or
    /// null — a visitor's place in front of a counter, beside a well, at a bench.
    /// </summary>
    private Vector3f? AnchorSpotNear(Vector3f at, int reach, float keepOff, Vector3f toward)
    {
        int cx = (int)Math.Floor(at.X), cy = (int)Math.Floor(at.Y), cz = (int)Math.Floor(at.Z);
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                foreach (int dy in new[] { 0, -1, 1 })
                {
                    if (StandableSpot(cx + dx, cy + dy, cz + dz) is not { } spot)
                    {
                        continue;
                    }

                    if (keepOff > 0f && WrapDistSq(spot, at) < keepOff * keepOff)
                    {
                        break; // the post's own cell (and the cells on top of each other): somebody stands there
                    }

                    double d = WrapDistSq(spot, toward) + (dy == 0 ? 0 : 0.5); // the same row first
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = spot;
                    }

                    break; // one spot per column
                }
            }
        }

        return best;
    }

    /// <summary>The standable spot on the ring just outside a field or a pen (its corner <paramref name="corner"/>, its
    /// size) that lies nearest <paramref name="toward"/> — where somebody stands to look at the crops or the animals.</summary>
    private Vector3f? FarmEdgeSpot(Vector3i corner, int w, int l, Vector3f toward)
    {
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        for (int x = corner.X - 1; x <= corner.X + w; x++)
        {
            for (int z = corner.Z - 1; z <= corner.Z + l; z++)
            {
                bool ring = x == corner.X - 1 || x == corner.X + w || z == corner.Z - 1 || z == corner.Z + l;
                if (!ring)
                {
                    continue;
                }

                foreach (int dy in new[] { 0, -1, 1 })
                {
                    if (StandableSpot(x, corner.Y + dy, z) is { } spot)
                    {
                        double d = WrapDistSq(spot, toward);
                        if (d < bestSq)
                        {
                            bestSq = d;
                            best = spot;
                        }

                        break;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Test seam: gives a settlement registered with <c>RegisterTestSettlementForTest</c> a marker (with
    /// <paramref name="data"/> also a data marker), as a stamped settlement would carry it.</summary>
    internal void AddSettlementMarkerForTest(string settlementName, string type, Vector3f pos, string data = "")
    {
        foreach (var s in _settlements)
        {
            if (s.Name == settlementName)
            {
                s.Markers.Add((type, pos));
                if (data.Length > 0)
                {
                    s.DataMarkers.Add((type, pos, data));
                }

                s.Anchors = null;
            }
        }
    }

    /// <summary>Test seam: the anchors of the settlement of this name on the active world, freshly built.</summary>
    internal IReadOnlyList<(string Kind, Vector3f Spot)> PlaceAnchorsForTest(string settlementName)
    {
        var list = new List<(string, Vector3f)>();
        foreach (var s in _settlements)
        {
            if (s.Name == settlementName)
            {
                s.Anchors = null;
                foreach (var a in PlaceAnchorsOf(s))
                {
                    list.Add((a.Kind.ToString(), a.Spot));
                }
            }
        }

        return list;
    }
}

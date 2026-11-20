// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the server's report (docs/developer/LIVELY_PLACES.md): every inhabited settlement of the active world
/// as a <see cref="NetPlace"/> — where it begins and ends, its colour, the middle of its meeting place, what it offers, the
/// signs over its doors and the points the client dresses with small effects. The client greets the player with the name,
/// hangs the signs, plays the place's sound layer and shows its lights from afar; nothing in the list decides anything.
/// <para>Sent with the landmark list (on world entry, so an empty list clears what the client held from the world it left)
/// and again whenever a place's state changes (a market day begins, a festival gathers).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most signs and effect points one place sends — a metropolis must not grow the message without end.</summary>
    private const int PlaceSignCap = 48;
    private const int PlacePointCap = 64;

    /// <summary>What is going on at a settlement of the active world right now (<see cref="PlaceStates"/>), by place key.
    /// Runtime only: a market day and a gathering are re-derived from the clock.</summary>
    private readonly Dictionary<string, string> _placeStates = new(StringComparer.Ordinal);

    /// <summary>The state of a settlement of the active world (<see cref="PlaceStates.None"/> on an ordinary day).</summary>
    private string SettlementEventOf(SettlementInstance s)
        => _placeStates.TryGetValue(_world.LocationId + "|" + SettlementPlaceKey(s), out var state) ? state : PlaceStates.None;

    /// <summary>Sets what is going on at a settlement and tells everyone on the world when it changed.</summary>
    private void SetSettlementEvent(SettlementInstance s, string state)
    {
        string key = _world.LocationId + "|" + SettlementPlaceKey(s);
        string had = _placeStates.TryGetValue(key, out var old) ? old : PlaceStates.None;
        if (string.Equals(had, state, StringComparison.Ordinal))
        {
            return;
        }

        if (state.Length == 0)
        {
            _placeStates.Remove(key);
        }
        else
        {
            _placeStates[key] = state;
        }

        BroadcastPlaces();
    }

    /// <summary>The building function a post marker stands for (<see cref="StructureRoles"/>), or null for a marker that
    /// is no post.</summary>
    private static string? PlaceTradeOf(string marker) => marker switch
    {
        "vendor" => StructureRoles.Market,
        "mission_board" => StructureRoles.Board,
        SettlementGenerator.TavernMarker => StructureRoles.Tavern,
        SettlementGenerator.WorkshopMarker => StructureRoles.Workshop,
        "greenhouse" => StructureRoles.Greenhouse,
        _ => NpcProfessions.ByMarker(marker)?.Function,
    };

    /// <summary>The effect point a marker stands for (<see cref="PlacePointKinds"/>), or 0.</summary>
    private static byte PlacePointOf(string marker) => marker switch
    {
        SettlementPlanBaker.FireMarker => PlacePointKinds.Fire,
        SettlementPlanBaker.LampMarker => PlacePointKinds.Lamp,
        SettlementPlanBaker.WellMarker => PlacePointKinds.Well,
        SettlementPlanBaker.StallMarker => PlacePointKinds.Stall,
        SettlementPlanBaker.BenchMarker => PlacePointKinds.Bench,
        SettlementPlanBaker.ChimneyMarker => PlacePointKinds.Chimney,
        SettlementGenerator.WorkshopMarker => PlacePointKinds.Workbench,
        _ => 0,
    };

    /// <summary>What a settlement's data markers add to its place: the signs beside its doors (returned) and a garden
    /// point in the middle of every field (appended to <paramref name="points"/>).</summary>
    private static List<NetPlaceSign> SignsAndGardensOf(SettlementInstance s, List<NetPlacePoint> points)
    {
        var signs = new List<NetPlaceSign>();
        foreach (var (type, pos, data) in s.DataMarkers)
        {
            var cell = pos.ToBlock();
            if (type == SettlementPlanBaker.SignMarker && signs.Count < PlaceSignCap)
            {
                var settings = MarkerSettings.Parse(data);
                signs.Add(new NetPlaceSign
                {
                    X = cell.X,
                    Y = cell.Y,
                    Z = cell.Z,
                    Side = (byte)Math.Clamp(settings.GetInt("side", 0), 0, 3),
                    Fn = settings.Get("fn"),
                });
            }
            else if (type == SettlementPlanBaker.FieldMarker && points.Count < PlacePointCap)
            {
                var settings = MarkerSettings.Parse(data); // a field's corner and size: the point is its middle
                points.Add(new NetPlacePoint
                {
                    X = cell.X + (settings.GetInt("w", 1) / 2),
                    Y = cell.Y,
                    Z = cell.Z + (settings.GetInt("l", 1) / 2),
                    Kind = PlacePointKinds.Garden,
                });
            }
        }

        return signs;
    }

    /// <summary>One inhabited settlement as the client sees it.</summary>
    private NetPlace ToNetPlace(SettlementInstance s)
    {
        var plaza = SettlementSquareCentre(s);
        var trades = new List<string>();
        var points = new List<NetPlacePoint>();
        foreach (var (type, pos) in s.Markers)
        {
            if (PlaceTradeOf(type) is { Length: > 0 } trade && !trades.Contains(trade))
            {
                trades.Add(trade);
            }

            byte kind = PlacePointOf(type);
            if (kind != 0 && points.Count < PlacePointCap)
            {
                var cell = pos.ToBlock();
                points.Add(new NetPlacePoint { X = cell.X, Y = cell.Y, Z = cell.Z, Kind = kind });
            }
        }

        var signs = SignsAndGardensOf(s, points);
        return new NetPlace
        {
            Key = SettlementPlaceKey(s),
            Kind = PlaceKinds.Settlement,
            Name = s.Name,
            Tier = s.Tier,
            MinX = s.Min.X,
            MinZ = s.Min.Z,
            MaxX = s.Max.X,
            MaxZ = s.Max.Z,
            Y = s.GroundY,
            Color = s.Layout?.Colour ?? 0,
            PlazaX = (int)Math.Floor(plaza.X),
            PlazaY = (int)Math.Floor(plaza.Y),
            PlazaZ = (int)Math.Floor(plaza.Z),
            Trades = trades.ToArray(),
            Signs = signs.ToArray(),
            Points = points.ToArray(),
            State = SettlementEventOf(s),
        };
    }

    /// <summary>The inhabited places of the active world: every intact settlement, in the world's order — or, aboard a
    /// station, the station itself (GameServerStationPurpose.cs).</summary>
    private PlaceList PlaceListForActiveWorld()
    {
        var places = _settlements.Where(s => !s.Ruined).Select(ToNetPlace).ToList();
        if (StationPlaceOfActiveWorld() is { } station)
        {
            places.Add(station);
        }

        return new PlaceList { Places = places.ToArray() };
    }

    /// <summary>Sends the active world's places to a session. An empty list is sent too: it clears what the client held
    /// from the world it came from.</summary>
    private void SendPlaces(PlayerSession session) => Send(session, PlaceListForActiveWorld());

    /// <summary>Sends the active world's places to everyone on it (a place's state changed).</summary>
    private void BroadcastPlaces()
    {
        var list = PlaceListForActiveWorld();
        foreach (var session in JoinedInActiveWorld())
        {
            Send(session, list);
        }
    }

    /// <summary>Test seam: the places exactly as a session entering the active world is sent them.</summary>
    internal PlaceList PlaceListForTest() => PlaceListForActiveWorld();

    /// <summary>Test seam (public for the client suite): sends the places to everyone on the active world, as a change of a
    /// place's state does.</summary>
    public void BroadcastPlacesForTest() => BroadcastPlaces();
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    /// <summary>The most lines, trains, trips and guests one bump snapshot lists (nearest first).</summary>
    private const int BumpRailCap = 8;

    /// <summary>
    /// #3447: the monorail of the reporter's world for the bump snapshot — reports about trains, villagers riding them and
    /// guests coming by train could not be diagnosed from it. The lines nearest the reporter (pylons, length, closed, stops
    /// with the place each belongs to), the trains (line, public or the owner, setting, autopilot, speed, halt, wagons,
    /// players and NPCs aboard, the passenger switch), the villagers' trips, and the reporter's nearest base with its stops
    /// and its guests. Every list is capped (<see cref="BumpRailCap"/>).
    /// </summary>
    private object BumpRailSnapshot(PlayerSession session)
    {
        var p = session.State.Position;
        var rails = Rails;
        return new
        {
            lines = rails.Lines.Values.OrderBy(l => l.Spline.Nearest(p).Distance).Take(BumpRailCap).Select(BumpRailLine).ToList(),
            trains = rails.Trains.Where(t => rails.Lines.ContainsKey(t.LineId))
                .OrderBy(t => WrapDistSq(p, WagonPose(t, rails.Lines[t.LineId], 0).Pos)).Take(BumpRailCap).Select(BumpTrain).ToList(),
            trips = rails.Trips.Take(BumpRailCap).Select(BumpTrip).ToList(),
            nearestBase = BumpNearestBase(session),
        };
    }

    private object BumpRailLine(RailLine l) => new
    {
        id = l.Id,
        pylons = l.Pylons.Count,
        length = Math.Round(l.Spline.TotalArc, 1),
        l.Closed,
        intercity = ReferenceEquals(l, IntercityLine()),
        stops = l.Stops.Take(BumpRailCap * 2).Select(s =>
        {
            var places = RailPlacesAt(s.Cell); // #3457: a stop may belong to a town and a base at once
            return new
            {
                arc = Math.Round(s.Arc, 1),
                x = s.Cell.X,
                y = s.Cell.Y,
                z = s.Cell.Z,
                place = places.FirstOrDefault()?.Name,
                placeKind = places.Count == 0 ? null : places[0].Base is null ? "settlement" : "base",
                alsoBase = places.Count > 1 ? places[1].Name : null,
            };
        }).ToList(),
    };

    private object BumpTrain(ServerTrain t) => new
    {
        id = t.Id,
        line = t.LineId,
        owner = RailRules.IsPublic(t.OwnerId) ? "public" : t.OwnerId,
        setting = t.Speed,
        t.Autopilot,
        velocity = Math.Round(t.Velocity, 2),
        t.Direction,
        t.Facing,
        t.Halted,
        haltStop = t.HaltStop is { } h ? new[] { h.X, h.Y, h.Z } : null,
        haltRemaining = t.Halted && t.HaltUntil > 0 ? Math.Round(Math.Max(0.0, t.HaltUntil - _uptime), 1) : 0.0,
        wagons = t.Wagons.ToList(),
        riders = t.Riders.Keys.ToList(),
        npcPassengers = t.NpcPassengers.Take(BumpRailCap).Select(kv => new
        {
            npc = kv.Key,
            name = NpcWithId(kv.Key)?.Name ?? string.Empty,
            wagon = kv.Value.Wagon,
            seat = kv.Value.Seat,
        }).ToList(),
        takesPassengers = RailRules.IsPublic(t.OwnerId) || t.TakesPassengers,
    };

    private object BumpTrip(RailTrip trip) => new
    {
        npc = trip.NpcId,
        name = NpcWithId(trip.NpcId)?.Name ?? string.Empty,
        kind = trip.Kind.ToString(),
        phase = trip.Phase.ToString(),
        trip.Returning,
        home = trip.HomeName,
        away = trip.AwayName,
        line = trip.LineId,
        train = NpcWithId(trip.NpcId)?.TrainId ?? string.Empty,
        seconds = Math.Round(_uptime - trip.StartedAt, 1),
    };

    /// <summary>The reporter's own base nearest them on this body (else any base nearest them): its stops (the stops that
    /// belong to it) and its guests.</summary>
    private object? BumpNearestBase(PlayerSession session)
    {
        var p = session.State.Position;
        var here = _bases.Where(b => b.Planet == _world.LocationId).ToList();
        var b = here.Where(x => x.OwnerId == session.State.PlayerId).OrderBy(x => WrapDistSq(p, BaseCorePos(x))).FirstOrDefault()
                ?? here.OrderBy(x => WrapDistSq(p, BaseCorePos(x))).FirstOrDefault();
        if (b is null)
        {
            return null;
        }

        var stops = Rails.Lines.Values
            .SelectMany(l => l.Stops.Where(s => RailBaseAt(s.Cell)?.Id == b.Id).Select(s => new { line = l.Id, x = s.Cell.X, y = s.Cell.Y, z = s.Cell.Z }))
            .Take(BumpRailCap).ToList();
        var relays = BaseRelayCells(b);
        return new
        {
            id = b.Id,
            name = b.Name,
            owner = b.OwnerId,
            distance = Math.Round(Math.Sqrt(WrapDistSq(p, BaseCorePos(b))), 1),
            stops,
            relays = relays.Count, // #3457: the power-relay chain that widens the stop reach (empty while the owner is away)
            relayChainComplete = !_railRelayChains.TryGetValue(b.Id, out var chain) || chain.Complete,
            nearestStop = BumpNearestStopReach(b),
            trainGuestRoutes = TrainGuestRoutes(b).Take(BumpRailCap).Select(r => new { line = r.Line.Id, town = r.Town.Name }).ToList(),
            guests = _baseGuests.Where(g => g.BaseId == b.Id).Select(g => new
            {
                npc = g.NpcId,
                kind = g.Kind.ToString(),
                phase = g.Phase.ToString(),
                g.ByTrain,
                trainLeave = g.TrainLeave,
            }).ToList(),
        };
    }

    /// <summary>#3457: the stop nearest the base's core on any line — how far from the core and from the nearest chained relay,
    /// and whom it belongs to — so a report shows directly why a station is or is not the base's.</summary>
    private object? BumpNearestStopReach(ServerBase b)
    {
        var core = BaseCorePos(b);
        var nearest = Rails.Lines.Values.SelectMany(l => l.Stops).Select(s => s.Cell)
            .OrderBy(c => WrapDistSq(core, new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f))).Cast<Vector3i?>().FirstOrDefault();
        if (nearest is not { } stop)
        {
            return null;
        }

        var at = new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f);
        var relays = BaseRelayCells(b);
        return new
        {
            x = stop.X,
            y = stop.Y,
            z = stop.Z,
            coreDistance = Math.Round(Math.Sqrt(WrapDistSq(new Vector3f(at.X, core.Y, at.Z), core)), 1),
            relayDistance = relays.Count == 0 ? (double?)null
                : Math.Round(relays.Min(r => Math.Sqrt(WrapDistSq(at, new Vector3f(r.X + 0.5f, at.Y, r.Z + 0.5f)))), 1),
            places = RailPlacesAt(stop).Select(p => p.Name + (p.Base is null ? " (settlement)" : " (base)")).ToList(),
        };
    }

    /// <summary>Test seam (#3447): the rail part of a bump snapshot for a player, as JSON.</summary>
    public string BumpRailSnapshotJsonForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? JsonSerializer.Serialize(BumpRailSnapshot(s)) : string.Empty;
}

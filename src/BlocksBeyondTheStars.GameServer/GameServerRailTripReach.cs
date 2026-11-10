// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Rail trips far from the players (#3446/#3447 review). Two things a trip must not do on a long line to a far town:
/// <list type="bullet">
/// <item><b>Load terrain on the tick thread.</b> Out of every player's reach (<see cref="InNpcReach"/>) the trip reads no
/// chunk that is not loaded (<see cref="RailAreaLoaded"/>): a stop's platform spot is the last one found, else a guess from
/// the line (<see cref="GuessPlatformSpot"/>: the cell beside the stop away from the line, at its level); a passenger
/// steps off onto that spot without looking for the door; the town's square is the layout's middle
/// (<see cref="TripSquareSpot"/>). A guessed leg end is looked for properly once it lies in reach
/// (<see cref="ResolveGuessedLeg"/>), and a traveller put down out of reach is settled onto real ground when it comes into
/// reach (<see cref="SettleTraveller"/>) — at the edge of the players' area of interest, where nobody notices a step.</item>
/// <item><b>Give up too early.</b> The platform wait, the ride and a guest's boarding window scale with the line
/// (<see cref="RailLineTimeouts"/>): a round of the slowest train that carries villagers on it — its length at
/// <see cref="RailTripCruiseShare"/> of its setting's top speed, there and back on an open line, plus a halt at every stop
/// each way — times <see cref="RailTripRoundsAllowed"/>, never less than the short-line values (300 s, 900 s, 180 s). The
/// ride uses the round too: a passenger may board a train heading away from its stop (no direction is checked), which then
/// runs to the line's end and back.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A train for villagers covers a whole line at about this share of its setting's top speed (it eases into every
    /// stop and bend and leaves each from rest).</summary>
    private const double RailTripCruiseShare = 0.6;

    /// <summary>A platform wait, a ride or a guest's boarding window lasts at most this many rounds of the slowest train for
    /// villagers on the line (above the short-line floors).</summary>
    private const double RailTripRoundsAllowed = 1.25;

    // ---------------- How long ----------------

    /// <summary>How long a train takes on a line: one way from end to end and a whole round (there and back on an open line,
    /// a lap on a loop), a halt at every stop on the way included. Pure.</summary>
    internal static (double OneWay, double Round) RailTrainTimes(double length, bool closed, int stops, int setting, bool publicTrain)
    {
        var table = RailRules.SpeedTable;
        double cruise = RailTripCruiseShare * table[Math.Clamp(setting, 1, table.Length) - 1];
        double halt = publicTrain ? RailRules.PublicStopHaltSeconds : RailRules.StopHaltSeconds;
        double oneWay = (Math.Max(0.0, length) / cruise) + (Math.Max(0, stops) * halt);
        return (oneWay, closed ? oneWay : 2.0 * oneWay);
    }

    /// <summary>The trip timeouts for a train round of <paramref name="round"/> seconds: the platform wait, the ride and a
    /// guest's boarding window — each <see cref="RailTripRoundsAllowed"/> rounds, never below its short-line value. Pure.</summary>
    internal static (double Wait, double Ride, double GuestBoard) RailTripTimeouts(double round)
    {
        double rounds = RailTripRoundsAllowed * Math.Max(0.0, round);
        return (Math.Max(RailTripWaitSeconds, rounds), Math.Max(RailTripRideSeconds, rounds), Math.Max(TrainGuestBoardSeconds, rounds));
    }

    /// <summary>A round of one train on its line (0 when the line is gone).</summary>
    private double RailTrainRound(ServerTrain t)
        => Rails.Lines.TryGetValue(t.LineId, out var line)
            ? RailTrainTimes(line.Spline.TotalArc, line.Closed, line.Stops.Count, t.Speed, RailRules.IsPublic(t.OwnerId)).Round
            : 0.0;

    /// <summary>The trip timeouts of a line: by the slowest train that carries villagers on it (the short-line values when
    /// none runs).</summary>
    private (double Wait, double Ride, double GuestBoard) RailLineTimeouts(int lineId)
    {
        double round = 0.0;
        foreach (var t in Rails.Trains)
        {
            if (t.LineId == lineId && CarriesNpcs(t))
            {
                round = Math.Max(round, RailTrainRound(t));
            }
        }

        return RailTripTimeouts(round);
    }

    // ---------------- No terrain loads out of reach ----------------

    /// <summary>Whether every chunk of the box round <paramref name="at"/> (<paramref name="radius"/> across,
    /// <paramref name="up"/> above, <paramref name="down"/> below) is loaded — reading it then loads nothing.</summary>
    private bool RailAreaLoaded(Vector3i at, int radius, int up, int down)
    {
        for (int cx = WorldConstants.WorldToChunk(at.X - radius); cx <= WorldConstants.WorldToChunk(at.X + radius); cx++)
        {
            for (int cy = WorldConstants.WorldToChunk(at.Y - down); cy <= WorldConstants.WorldToChunk(at.Y + up); cy++)
            {
                for (int cz = WorldConstants.WorldToChunk(at.Z - radius); cz <= WorldConstants.WorldToChunk(at.Z + radius); cz++)
                {
                    if (!_world.IsChunkLoaded(new ChunkCoord(cx, cy, cz)))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Where a passenger probably waits at a stop, from the line alone (no block is read): the cell beside the stop
    /// on the side away from the line, at the stop's own level — a stop stands on the ground beside its line.</summary>
    private Vector3f GuessPlatformSpot(Vector3i stop)
    {
        int sx = 0, sz = 1;
        if (Rails.Lines.Values.FirstOrDefault(l => l.Stops.Any(s => s.Cell == stop)) is { } line)
        {
            var at = new Vector3f(stop.X + 0.5f, stop.Y + 0.5f, stop.Z + 0.5f);
            var p = line.Spline.PointAt(line.Spline.Nearest(at).Arc);
            double dx = WorldConstants.WrapDeltaX((double)at.X - p.X, _world.Circumference);
            double dz = WorldConstants.WrapDeltaZ((double)at.Z - p.Z, _world.Circumference);
            (sx, sz) = Math.Abs(dx) >= Math.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));
            if (sx == 0 && sz == 0)
            {
                sz = 1; // the stop sits on the line's middle: either side will do
            }
        }

        return new Vector3f(stop.X + sx + 0.5f, stop.Y, stop.Z + sz + 0.5f);
    }

    /// <summary>The spot at the other town's square a traveller walks to: in reach the festival square, a little to one side
    /// (it reads blocks); out of every player's reach the layout's middle, guessed (<c>Guessed</c>) and looked for properly
    /// once a player is near (<see cref="ResolveGuessedLeg"/>).</summary>
    private (Vector3f Spot, bool Guessed) TripSquareSpot(RailTrip trip, ServerNpc npc)
    {
        if (_settlements.FirstOrDefault(s => !s.Ruined && s.Name == trip.AwayName) is not { } town)
        {
            return (npc.Pos, false);
        }

        var centre = SettlementSquareCentre(town);
        if (!InNpcReach(centre))
        {
            return (centre, true);
        }

        var square = FestivalAnchorFor(town);
        var jitter = new Vector3f(square.X + (float)((_tripRng.NextDouble() * 6.0) - 3.0), square.Y, square.Z + (float)((_tripRng.NextDouble() * 6.0) - 3.0));
        return (NpcSpotNear(jitter, 3) ?? NpcSpotNear(square, 3) ?? npc.Pos, false);
    }

    /// <summary>A leg whose end was guessed out of reach now lies in reach: the platform spot or the square, looked for
    /// properly (it stays guessed while the stop is still out of reach).</summary>
    private void ResolveGuessedLeg(RailTrip trip, ServerNpc npc)
    {
        trip.LegGuessed = false;
        if (trip.Phase == RailTripPhase.ToStation)
        {
            if (RailStopPlatformSpot(trip.FromStop, out bool guessed) is { } platform)
            {
                trip.LegTarget = platform;
                trip.LegGuessed = guessed;
            }
        }
        else if (trip.Phase == RailTripPhase.ToSight)
        {
            (trip.LegTarget, trip.LegGuessed) = TripSquareSpot(trip, npc);
        }
    }

    /// <summary>A traveller put down out of reach has come into a player's area of interest: when it does not stand on real
    /// ground, onto the nearest spot that is (on the platform while it waits there). It happens at the area's edge, far from
    /// every player.</summary>
    private void SettleTraveller(RailTrip trip, ServerNpc npc)
    {
        trip.Unsettled = false;
        var cell = npc.Pos.ToBlock();
        if (StandableSpot(cell.X, cell.Y, cell.Z) is not null)
        {
            return;
        }

        var spot = trip.Phase == RailTripPhase.Waiting ? RailStopPlatformSpot(trip.FromStop) : NpcSpotNear(npc.Pos, 3);
        if (spot is not { } at)
        {
            return;
        }

        npc.Pos = at;
        npc.Home = at;
        npc.Path = null;
        npc.PathIndex = 0;
        BroadcastNpcs();
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3447 review): the trip timeouts of a train's line (the platform wait, the ride, a guest's
    /// boarding window).</summary>
    public (double Wait, double Ride, double GuestBoard) RailTripTimeoutsForTest(string trainId)
        => RailLineTimeouts(Rails.Trains.FirstOrDefault(t => t.Id == trainId)?.LineId ?? 0);
}

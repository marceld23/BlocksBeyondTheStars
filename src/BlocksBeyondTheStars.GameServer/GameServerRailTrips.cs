// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Villagers travel by train (#3446, Lyxette: "the rail line would get a real purpose — more than decoration"). Now and then,
/// by day, a plain villager walks to its town's stop, waits on the platform, boards the next train that halts there with a
/// free seat, rides to another town on the same line, steps off, walks to that town's square, stays a few minutes and comes
/// back the same way. The server does not simulate NPCs far from the players (#1530): only inside a player's area of
/// interest do they walk (<see cref="InNpcReach"/>) — so a leg out of reach runs on a timer (about a block a second) and
/// ends with the villager placed where it was going, never in front of anyone; a leg in reach is walked with the NPC route
/// search. Riding needs no such care: a passenger goes wherever its train goes (<see cref="CarryNpcPassengers"/>).
/// <list type="bullet">
/// <item><b>Places</b> (<see cref="RailPlaceAt"/>): an intercity station's stop belongs to its town; any other stop to the
/// settlement whose box holds it or lies within <see cref="RailStopSettlementMargin"/>, else to a player base whose core
/// is within <see cref="RailStopBaseReach"/>. A trip runs between two mapped stops of different places on the
/// <b>same line</b> (there are no switches).</item>
/// <item><b>Who</b> (<see cref="MayTravelByRail"/>): a plain villager — a settler of the town with no post (no vendor,
/// quartermaster, guard, innkeeper, gardener or craftsman), no story character, nobody who holds a player's wish, nobody
/// celebrating, talking or away; awake and standing. #3447: a base's residents without a post ride from their base's stop
/// to a connected town the same way.</item>
/// <item><b>When</b> (<see cref="PlanRailTrips"/>, 1 Hz): a line is due every <see cref="RailTripSpellDays"/> in-game days
/// (× 0.8–1.2) while a train runs on it that may carry villagers (<see cref="CarriesNpcs"/>); local day
/// <see cref="RailTripDayStart"/>–<see cref="RailTripDayEnd"/> at the home stop; at most
/// <see cref="MaxRailTripsPerWorld"/> travellers per world and one per place.</item>
/// <item><b>Phases</b>: to the station → waiting (boards the next train halting at the stop with a free seat, in reach at
/// its door; <see cref="RailTripWaitSeconds"/> at most, longer on a long line) → riding (until a halt at the other stop) →
/// to the square → a visit of 2–4 minutes → back the same way → home, where its routine thinks about the hour again.</item>
/// <item><b>Safety net</b>: every failure — no train for too long, the train stowed, the line changed, a walk that never
/// arrives, a world nobody ticked for <see cref="RailTripFrozenSeconds"/> — ends with the villager home: walking there in
/// reach (the route search's unseen fallback does the rest), placed there out of reach — at once when nobody watches its
/// home, else by the timed way home that comes in from out of sight. RAM only; a world load regenerates the villagers
/// anyway. Nobody is ever lost or doubled: the traveller is the villager itself, moved by its trip.</item>
/// <item><b>Out of reach no chunk is read</b> that is not loaded (<c>GameServerRailTripReach.cs</c>): the platform spot, the
/// door and the square are guessed from the stop and the town's layout, then looked for properly once a player comes
/// near.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most villagers on a train trip on one world at once.</summary>
    internal const int MaxRailTripsPerWorld = 3;

    /// <summary>A line's quiet spell between two trip starts, in in-game days (× 0.8–1.2) — about every five in-game hours.</summary>
    private const double RailTripSpellDays = 0.2;

    /// <summary>Trips start by day only: local day fraction from …</summary>
    private const double RailTripDayStart = 0.25;

    /// <summary>… to (early afternoon, so most travellers are home for the evening).</summary>
    private const double RailTripDayEnd = 0.55;

    /// <summary>A stop belongs to a settlement whose box holds it or lies within this many blocks of it.</summary>
    private const int RailStopSettlementMargin = 12;

    /// <summary>A stop belongs to a player base whose core lies within this many blocks of it (horizontally) …</summary>
    internal const float RailStopBaseReach = 48f;

    /// <summary>… and this many up or down.</summary>
    private const float RailStopBaseReachY = 24f;

    /// <summary>How fast a leg out of reach passes (blocks per second along the straight way).</summary>
    private const float RailTripVirtualSpeed = 1f;

    /// <summary>A leg out of reach takes at most this long (however far the way).</summary>
    private const double RailTripVirtualLegMaxSeconds = 300.0;

    /// <summary>How long a traveller waits on the platform for a train with a free seat before it goes home — at least; a
    /// long line waits longer (<see cref="RailLineTimeouts"/>).</summary>
    private const double RailTripWaitSeconds = 300.0;

    /// <summary>How long a traveller may ride before something is wrong (the train never reaches its stop) — at least; a
    /// long line rides longer (<see cref="RailLineTimeouts"/>).</summary>
    private const double RailTripRideSeconds = 900.0;

    /// <summary>The visit's length: from … to … seconds.</summary>
    private const double RailTripVisitMin = 120.0;
    private const double RailTripVisitMax = 240.0;

    /// <summary>A world that was not ticked for this long (nobody there) ends every trip — the travellers are simply home.</summary>
    private const double RailTripFrozenSeconds = 20.0;

    /// <summary>A walking leg counts as arrived this close to its end.</summary>
    private const float RailTripArriveRadius = 1.6f;

    /// <summary>An arrival a player would see happen walks in the last stretch from about this far.</summary>
    private const float RailTripApproachDistance = 28f;

    /// <summary>A waiting traveller in reach walks to its wagon's door; it hops in from farther (up to the players' boarding
    /// reach) when the halt is about to end.</summary>
    private const double RailTripBoardLastSeconds = 1.5;
    private const float RailTripBoardReach = RailRules.BoardRange + RailRules.WagonLength;
    private const float RailTripDoorReach = 1.6f;

    /// <summary>The nameplate activity on the way and aboard.</summary>
    internal const string RailTripActivityKey = "npc.activity.train_trip";

    /// <summary>The nameplate activity at the other place.</summary>
    private const string RailVisitActivityKey = "npc.activity.visiting";

    /// <summary>Who travels (#3446) — a villager between two towns, a base resident (#3447), a base guest (#3447).</summary>
    internal enum RailTripKind : byte
    {
        Villager,
        Resident,
        Guest,
    }

    /// <summary>Where a traveller is in its trip.</summary>
    internal enum RailTripPhase : byte
    {
        ToStation,
        Waiting,
        Riding,
        ToSight,
        Visiting,
        GoingHome,
    }

    /// <summary>A villager's trip by train (#3446). RAM only; lives in <see cref="RailWorldState.Trips"/>.</summary>
    internal sealed class RailTrip
    {
        public int NpcId;
        public RailTripKind Kind;
        public int LineId;
        public Vector3i HomeStop;
        public Vector3i AwayStop;
        public string HomeName = string.Empty;
        public string AwayName = string.Empty;

        /// <summary>#3447: the base a guest rides to (0 = none).</summary>
        public int AwayBaseId;
        public RailTripPhase Phase;
        public bool Returning;
        public double StartedAt;
        public double PhaseSince;

        /// <summary>Out of reach: when the leg is over (0 = not timed yet).</summary>
        public double VirtualUntil;
        public double StayUntil;
        public bool Walking;
        public bool Approached;
        public Vector3f LegTarget;

        /// <summary>The leg's end was guessed out of every player's reach without reading a chunk
        /// (<see cref="RailStopPlatformSpot(Vector3i, out bool)"/>, <see cref="TripSquareSpot"/>): it is looked for properly
        /// once it lies in reach.</summary>
        public bool LegGuessed;

        /// <summary>The traveller was put down out of every player's reach (a guessed spot, a seat it was set down from): it is
        /// settled onto real ground when it comes into reach (<see cref="SettleTraveller"/>).</summary>
        public bool Unsettled;

        public Vector3i FromStop => Returning ? AwayStop : HomeStop;
        public Vector3i ToStop => Returning ? HomeStop : AwayStop;
    }

    /// <summary>The place a stop belongs to (#3446): a settlement or a player base.</summary>
    internal sealed class RailStopPlace
    {
        public Vector3i Stop;
        public string Name = string.Empty;
        public SettlementInstance? Settlement;
        public ServerBase? Base;

        public bool SamePlace(RailStopPlace other)
            => (Settlement is not null && ReferenceEquals(Settlement, other.Settlement)) || (Base is not null && ReferenceEquals(Base, other.Base));
    }

    private readonly Random _tripRng = new(0x3446);

    /// <summary>Test seam: the planner starts no trips on its own (the tests start them).</summary>
    private bool _railTripPlannerPaused;

    // ---------------- Places ----------------

    /// <summary>The place a stop belongs to, or null (see the class summary).</summary>
    private RailStopPlace? RailPlaceAt(Vector3i stop)
    {
        var ic = Intercity;
        if (ic.Active && ic.Stations.FirstOrDefault(s => s.Stop == stop) is { } station
            && _settlements.FirstOrDefault(s => !s.Ruined && s.Name == station.SettlementName) is { } town)
        {
            return new RailStopPlace { Stop = stop, Name = town.Name, Settlement = town };
        }

        var at = new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f);
        SettlementInstance? best = null;
        double bestSq = double.MaxValue;
        foreach (var s in _settlements)
        {
            if (!s.Ruined && s.Name.Length > 0 && StopNearBox(stop, s) && WrapDistSq(at, SettlementCenter(s)) is var d && d < bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        if (best is not null)
        {
            return new RailStopPlace { Stop = stop, Name = best.Name, Settlement = best };
        }

        return RailBaseAt(stop) is { } b ? new RailStopPlace { Stop = stop, Name = b.Name, Base = b } : null;
    }

    private bool StopNearBox(Vector3i stop, SettlementInstance s)
    {
        int m = RailStopSettlementMargin;
        int lx = Shared.World.WorldConstants.WrapDeltaX(stop.X - s.Min.X, _world.Circumference);
        return lx >= -m && lx <= s.Max.X - s.Min.X + m && stop.Z >= s.Min.Z - m && stop.Z <= s.Max.Z + m
               && stop.Y >= s.Min.Y - m && stop.Y <= s.Max.Y + m;
    }

    /// <summary>The nearest player base on this body whose core is within reach of a stop, or null.</summary>
    private ServerBase? RailBaseAt(Vector3i stop)
    {
        ServerBase? best = null;
        double bestSq = (double)RailStopBaseReach * RailStopBaseReach;
        var at = new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f);
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId || Math.Abs(b.Cell.Y - stop.Y) > RailStopBaseReachY)
            {
                continue;
            }

            var core = BaseCorePos(b);
            double d = WrapDistSq(new Vector3f(at.X, core.Y, at.Z), core);
            if (d <= bestSq)
            {
                bestSq = d;
                best = b;
            }
        }

        return best;
    }

    /// <summary>A line's stops that belong to a place, in arc order.</summary>
    private List<RailStopPlace> RailStopPlaces(RailLine line)
    {
        var places = new List<RailStopPlace>();
        foreach (var (_, cell) in line.Stops)
        {
            if (RailPlaceAt(cell) is { } p)
            {
                places.Add(p);
            }
        }

        return places;
    }

    private bool StopOnLine(int lineId, Vector3i stop)
        => Rails.Lines.TryGetValue(lineId, out var line) && line.Stops.Any(s => s.Cell == stop);

    /// <summary>Whether a train runs on a line that may carry villagers now.</summary>
    private bool LineCarriesNpcs(int lineId) => Rails.Trains.Any(t => t.LineId == lineId && CarriesNpcs(t));

    // ---------------- Who ----------------

    /// <summary>A plain villager who may go on a trip now (see the class summary).</summary>
    private bool MayTravelByRail(ServerNpc n)
        => n.BaseId == 0 && n.Role == "settler" && n.Job.Length == 0 && n.CharacterId.Length == 0 && n.PersonKey.Length > 0
           && n.GuestKey.Length == 0 && n.LodgeKey.Length == 0 && n.Settlement.Length > 0 && MayLeaveForTrip(n);

    /// <summary>#3447: a base resident without a post who may ride to a connected town now.</summary>
    private bool MayTravelAsResident(ServerNpc n)
        => n.BaseId != 0 && n.Job.Length == 0 && n.CharacterId.Length == 0 && MayLeaveForTrip(n);

    /// <summary>The part both share: routine on, not away, not on a trip, awake and standing, not talking or celebrating,
    /// not in a player's dialogue, not holding any player's wish.</summary>
    private bool MayLeaveForTrip(ServerNpc n)
        => n.RoutineEnabled && !n.VisitingAway && n.Trip is null && n.TrainId.Length == 0 && n.Pose == 0
           && n.TalkWith is null && _uptime >= n.TalkHoldUntil && n.Phase != NpcPhase.Festival
           && !NpcInPlayerDialog(n) && !HoldsAWish(n);

    /// <summary>Whether a player on this world holds a wish of this villager — they would look for it at home.</summary>
    private bool HoldsAWish(ServerNpc n)
    {
        if (n.PersonKey.Length == 0)
        {
            return false;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            foreach (var ledger in s.State.Wishes.Values)
            {
                if (ledger.Lanes.Any(l => l.Giver == n.PersonKey))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---------------- Planning ----------------

    /// <summary>The planner (1 Hz per world): one due line at most starts one trip per pass.</summary>
    private void PlanRailTrips()
    {
        var rails = Rails;
        if (_uptime < rails.NextTripPlanAt || _railTripPlannerPaused)
        {
            return;
        }

        rails.NextTripPlanAt = _uptime + 1.0;
        if (rails.Trips.Count >= MaxRailTripsPerWorld || _npcs.Count == 0 || !JoinedInActiveWorld().Any())
        {
            return;
        }

        double spell = RailTripSpellDays * Math.Max(60.0, _dayLength);
        foreach (var line in rails.Lines.Values.OrderBy(l => l.Id))
        {
            if (!rails.NextTripAt.TryGetValue(line.Id, out double next))
            {
                rails.NextTripAt[line.Id] = _uptime + (spell * (0.3 + (_tripRng.NextDouble() * 0.3))); // first sight: armed
                continue;
            }

            if (_uptime < next || !LineCarriesNpcs(line.Id))
            {
                continue;
            }

            var places = RailStopPlaces(line);
            if (places.Count < 2)
            {
                continue;
            }

            rails.NextTripAt[line.Id] = _uptime + (spell * (0.8 + (_tripRng.NextDouble() * 0.4)));
            TryStartRailTrip(line, places, homePlace: null, admin: false);
            return;
        }
    }

    /// <summary>Starts a trip on a line between two of its places (random pairs; <paramref name="homePlace"/> narrows the
    /// home place by name). The admin's call skips the daytime. Returns the trip, or null when nobody may go.</summary>
    private RailTrip? TryStartRailTrip(RailLine line, List<RailStopPlace> places, string? homePlace, bool admin)
    {
        var pairs = new List<(RailStopPlace Home, RailStopPlace Away)>();
        foreach (var h in places)
        {
            foreach (var a in places)
            {
                if (a.Settlement is not null && !h.SamePlace(a))
                {
                    pairs.Add((h, a));
                }
            }
        }

        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = _tripRng.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        foreach (var (home, away) in pairs)
        {
            if ((homePlace is not null && !string.Equals(home.Name, homePlace, StringComparison.OrdinalIgnoreCase))
                || Rails.Trips.Any(t => t.HomeName == home.Name) || Rails.Trips.Count >= MaxRailTripsPerWorld
                || (!admin && !RailTripDaytime(home.Stop)))
            {
                continue;
            }

            if (PickRailTraveller(home) is { } npc
                && StartRailTrip(npc, home.Base is null ? RailTripKind.Villager : RailTripKind.Resident, line, home, away) is { } trip)
            {
                return trip;
            }
        }

        return null;
    }

    private bool RailTripDaytime(Vector3i at)
    {
        double day = LocalDayFraction(new Vector3f(at.X + 0.5f, at.Y, at.Z + 0.5f));
        return day >= RailTripDayStart && day <= RailTripDayEnd;
    }

    /// <summary>Somebody of the home place who may travel now: a villager of the town, or (#3447) a resident of the base.</summary>
    private ServerNpc? PickRailTraveller(RailStopPlace home)
    {
        var candidates = home.Base is { } b
            ? _npcs.Where(n => n.BaseId == b.Id && MayTravelAsResident(n)).ToList()
            : _npcs.Where(n => n.Settlement == home.Name && MayTravelByRail(n)).ToList();
        return candidates.Count == 0 ? null : candidates[_tripRng.Next(candidates.Count)];
    }

    /// <summary>The traveller sets off for its stop's platform. Null (nothing changes) when the stop has no platform spot.</summary>
    private RailTrip? StartRailTrip(ServerNpc npc, RailTripKind kind, RailLine line, RailStopPlace home, RailStopPlace away)
    {
        if (RailStopPlatformSpot(home.Stop, out bool guessed) is not { } platform)
        {
            return null;
        }

        var trip = new RailTrip
        {
            NpcId = npc.Id,
            Kind = kind,
            LineId = line.Id,
            HomeStop = home.Stop,
            AwayStop = away.Stop,
            HomeName = home.Name,
            AwayName = away.Name,
            AwayBaseId = away.Base?.Id ?? 0,
            StartedAt = _uptime,
        };
        npc.Trip = trip;
        npc.SeatWait = false;
        npc.AttendUntil = 0;
        npc.ActivityKey = RailTripActivityKey;
        BeginTripLeg(trip, npc, RailTripPhase.ToStation, platform, guessed);
        Rails.Trips.Add(trip);
        BroadcastNpcs();
        _log.Info($"Rail trips: '{npc.Name}' of {home.Name} takes the train to {away.Name} (line {line.Id}).");
        return trip;
    }

    // ---------------- The step ----------------

    /// <summary>Every tick (from <see cref="TickTrains"/>, after the trains moved): the planner, then every trip's step. A
    /// world that was not ticked for a while ends them all (the travellers are home).</summary>
    private void StepRailTrips()
    {
        var rails = Rails;
        PlanRailTrips();
        bool frozen = !double.IsNegativeInfinity(rails.TripsSteppedAt) && _uptime - rails.TripsSteppedAt > RailTripFrozenSeconds;
        rails.TripsSteppedAt = _uptime;
        for (int i = rails.Trips.Count - 1; i >= 0; i--)
        {
            if (i >= rails.Trips.Count)
            {
                continue; // a step ended more than one trip
            }

            var trip = rails.Trips[i];
            if (NpcWithId(trip.NpcId) is not { } npc || !ReferenceEquals(npc.Trip, trip))
            {
                rails.Trips.RemoveAt(i); // the traveller is gone (a resident whose bed went): nothing to bring home
                continue;
            }

            if (frozen)
            {
                EndRailTripAtHome(trip, npc);
                continue;
            }

            StepRailTrip(trip, npc);
        }
    }

    private void StepRailTrip(RailTrip trip, ServerNpc npc)
    {
        if (TrainGuestOverdue(trip))
        {
            TrainGuestWalksInstead(trip, npc); // #3447: no train (or no seat) in time — today's walk-in instead
            return;
        }

        if (trip.Unsettled && npc.TrainId.Length == 0 && InNpcReach(npc.Pos))
        {
            SettleTraveller(trip, npc); // put down out of reach: onto real ground now that it can be looked at
        }

        switch (trip.Phase)
        {
            case RailTripPhase.ToStation:
                if (StepTripWalk(trip, npc))
                {
                    BeginTripWaiting(trip, npc);
                }

                break;
            case RailTripPhase.Waiting:
                StepTripWaiting(trip, npc);
                break;
            case RailTripPhase.Riding:
                StepTripRiding(trip, npc);
                break;
            case RailTripPhase.ToSight:
                if (StepTripWalk(trip, npc))
                {
                    BeginTripVisit(trip, npc);
                }

                break;
            case RailTripPhase.Visiting:
                if (_uptime >= trip.StayUntil)
                {
                    BeginTripReturn(trip, npc);
                }

                break;
            default:
                if (StepTripWalk(trip, npc))
                {
                    FinishRailTrip(trip, npc);
                }

                break;
        }
    }

    private void BeginTripLeg(RailTrip trip, ServerNpc npc, RailTripPhase phase, Vector3f target, bool guessed = false)
    {
        trip.Phase = phase;
        trip.PhaseSince = _uptime;
        trip.VirtualUntil = 0;
        trip.Walking = false;
        trip.Approached = false;
        trip.LegTarget = target;
        trip.LegGuessed = guessed;
        npc.Goal = null;
        npc.Path = null;
    }

    /// <summary>
    /// One step of a walking leg; true when the traveller stands at its end. In reach it walks with the route search
    /// (whose unseen fallback helps a stuck walker); out of reach the leg takes a block a second and the traveller is then
    /// placed at its end — unless a player there could see it appear: then it is placed a short way off on the far side and
    /// walks the last stretch in view. A leg that never ends is a failure (<see cref="RailTripLegTimedOut"/>).
    /// </summary>
    private bool StepTripWalk(RailTrip trip, ServerNpc npc)
    {
        if (trip.LegGuessed && InNpcReach(trip.LegTarget))
        {
            ResolveGuessedLeg(trip, npc); // a leg end guessed out of reach is looked for properly now that a player is near it
        }

        var target = trip.LegTarget;
        if (WrapDistSq(npc.Pos, target) <= RailTripArriveRadius * RailTripArriveRadius && Math.Abs(npc.Pos.Y - target.Y) < 1.5f
            && npc.Goal is null)
        {
            return true;
        }

        if (InNpcReach(npc.Pos))
        {
            trip.VirtualUntil = 0;
            if (npc.Goal is not { } goal || !goal.Equals(target))
            {
                SetNpcGoal(npc, target, NpcArrival.None, 0.6f);
            }

            trip.Walking = true;
            return RailTripLegTimedOut(trip, npc);
        }

        if (trip.Walking)
        {
            npc.Goal = null; // out of everybody's reach now: the rest of the way is a timer
            npc.Path = null;
            trip.Walking = false;
        }

        if (trip.VirtualUntil <= 0)
        {
            double dist = Math.Sqrt(WrapDistSq(npc.Pos, target));
            trip.VirtualUntil = _uptime + Math.Clamp(dist / RailTripVirtualSpeed, 3.0, RailTripVirtualLegMaxSeconds);
        }

        return _uptime >= trip.VirtualUntil && PlaceAtLegEnd(trip, npc, target);
    }

    /// <summary>A walking leg in reach that has gone on too long: a trip's leg fails (the traveller goes home); the way home
    /// itself ends with the traveller placed at home once nobody sees it. Returns whether the leg is over (home).</summary>
    private bool RailTripLegTimedOut(RailTrip trip, ServerNpc npc)
    {
        double dist = Math.Sqrt(WrapDistSq(npc.Pos, trip.LegTarget));
        if (_uptime - trip.PhaseSince <= 120.0 + (dist / 0.4))
        {
            return false;
        }

        if (trip.Phase != RailTripPhase.GoingHome)
        {
            FailRailTrip(trip, npc);
            return false;
        }

        if (SpotWatched(npc.Pos) || SpotWatched(trip.LegTarget))
        {
            trip.PhaseSince = _uptime - 60.0; // still in view: another minute
            return false;
        }

        npc.Pos = trip.LegTarget;
        npc.Goal = null;
        npc.Path = null;
        return true;
    }

    /// <summary>The end of a leg out of reach: placed there — or, when a player there would see it appear, a short way off
    /// on the far side, from where it walks in (the leg then goes on in reach). Returns whether it stands at the end.</summary>
    private bool PlaceAtLegEnd(RailTrip trip, ServerNpc npc, Vector3f target)
    {
        if (SpotWatched(target))
        {
            if (!trip.Approached && TripApproachSpot(target, npc.Pos) is { } near)
            {
                npc.Pos = near;
                npc.Home = near;
                trip.Approached = true;
                trip.Unsettled = false; // real ground beside a watched spot
                BroadcastNpcs();
            }
            else
            {
                trip.VirtualUntil = _uptime + 5.0; // nowhere to come from unseen: a little later
            }

            return false;
        }

        npc.Pos = target;
        npc.Home = target;
        npc.Goal = null;
        npc.Path = null;
        npc.Loco.ModeTimer = 0f;
        trip.Unsettled = trip.LegGuessed; // a guessed end is checked once a player comes near
        BroadcastNpcs();
        return true;
    }

    /// <summary>A spot about <see cref="RailTripApproachDistance"/> from <paramref name="target"/> toward where the traveller
    /// comes from that no player sees, or null.</summary>
    private Vector3f? TripApproachSpot(Vector3f target, Vector3f from)
    {
        double bearing = BearingFrom(target, from);
        foreach (float d in new[] { RailTripApproachDistance, RailTripApproachDistance + 8f, RailTripApproachDistance - 8f })
        {
            foreach (double turn in new[] { 0.0, 0.6, -0.6 })
            {
                if (GuestSpotAlong(target, bearing + turn, d) is { } spot && !SpotWatched(spot))
                {
                    return spot;
                }
            }
        }

        return null;
    }

    /// <summary>Whether a player could see an NPC appear or vanish at a spot (#2680's rule: within the unseen range, with a
    /// line of sight to the body).</summary>
    private bool SpotWatched(Vector3f at)
    {
        foreach (var s in RailNpcTargets())
        {
            if (WrapDistSq(s.State.Position, at) <= NpcTeleportUnseenRange * NpcTeleportUnseenRange
                && PlayerSeesUnseenMove(s.State.Position, at, at))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Waiting and riding ----------------

    private void BeginTripWaiting(RailTrip trip, ServerNpc npc)
    {
        trip.Phase = RailTripPhase.Waiting;
        trip.PhaseSince = _uptime;
        trip.Walking = false;
        npc.Goal = null;
        npc.Path = null;
        npc.Leash = 0.6f;
        npc.ActivityKey = RailTripActivityKey;
    }

    /// <summary>On the platform: the next train halting at the stop that takes villagers and has a free seat — in reach it
    /// walks to its wagon's door first. Too long without one (<see cref="RailLineTimeouts"/>: longer on a long line), or the
    /// line changed: home.</summary>
    private void StepTripWaiting(RailTrip trip, ServerNpc npc)
    {
        if (!StopOnLine(trip.LineId, trip.FromStop) || !StopOnLine(trip.LineId, trip.ToStop))
        {
            FailRailTrip(trip, npc); // the line changed under the platform
            return;
        }

        var t = Rails.Trains.FirstOrDefault(x => x.LineId == trip.LineId && x.Halted && x.HaltStop == trip.FromStop
                                                 && CarriesNpcs(x) && FreeNpcSeat(x) is not null);
        if (t is not null && TryBoardAtHalt(npc, t, trip.FromStop, eager: false))
        {
            trip.Phase = RailTripPhase.Riding;
            trip.PhaseSince = _uptime;
            npc.ActivityKey = RailTripActivityKey;
            return;
        }

        if (_uptime - trip.PhaseSince > RailLineTimeouts(trip.LineId).Wait)
        {
            FailRailTrip(trip, npc);
        }
    }

    /// <summary>Boards a train halted at <paramref name="stop"/>: out of reach at once; in reach at its wagon's door (it walks
    /// there), or from up to the boarding reach when the halt is about to end — <paramref name="eager"/> (a guest's 1 Hz step)
    /// from up to that reach at once. Returns whether it is aboard.</summary>
    private bool TryBoardAtHalt(ServerNpc npc, ServerTrain t, Vector3i stop, bool eager)
    {
        if (!InNpcReach(npc.Pos) || !Rails.Lines.TryGetValue(t.LineId, out var line))
        {
            return TryBoardNpc(npc, t, stop);
        }

        int wagon = NearestPassengerWagon(t, line, npc.Pos);
        double toWagon = Math.Sqrt(WrapDistSq(npc.Pos, WagonPose(t, line, wagon).Pos));
        var door = NpcDoorSpot(t, line, wagon, npc.Pos);
        double left = t.HaltUntil > 0 ? t.HaltUntil - _uptime : double.MaxValue;
        bool atDoor = door is { } d && WrapDistSq(npc.Pos, d) <= RailTripDoorReach * RailTripDoorReach;
        if (atDoor || (toWagon <= RailTripBoardReach && (eager || door is null || left <= RailTripBoardLastSeconds)))
        {
            return TryBoardNpc(npc, t, stop);
        }

        if (door is { } goal && (npc.Goal is not { } g || !g.Equals(goal)))
        {
            SetNpcGoal(npc, goal, NpcArrival.None, 0.4f);
        }

        return false;
    }

    /// <summary>Aboard: until the train halts at the stop it rides to — then it steps off (a guest's at the base becomes the
    /// visit, #3447). The train gone, the stop gone from its line, or far too long a ride (<see cref="RailLineTimeouts"/>):
    /// home.</summary>
    private void StepTripRiding(RailTrip trip, ServerNpc npc)
    {
        var t = npc.TrainId.Length > 0 ? Rails.Trains.FirstOrDefault(x => x.Id == npc.TrainId) : null;
        if (t is null || !StopOnLine(t.LineId, trip.ToStop) || _uptime - trip.PhaseSince > RailLineTimeouts(t.LineId).Ride)
        {
            AbortRailTrip(trip, npc);
            return;
        }

        if (!t.Halted || t.HaltStop != trip.ToStop)
        {
            return;
        }

        if (trip.Kind == RailTripKind.Guest && !trip.Returning)
        {
            ArriveAsTrainGuest(trip, npc, t); // #3447: the guest steps off at the base's stop and its visit begins
            return;
        }

        if (!AlightNpc(npc, trip.ToStop))
        {
            FinishRailTrip(trip, npc); // nowhere to step off: it was sent home
            return;
        }

        trip.Unsettled = !InNpcReach(npc.Pos); // out of reach it stepped off onto the platform spot without a look round
        if (trip.Returning)
        {
            BeginTripHome(trip, npc);
        }
        else
        {
            BeginTripSight(trip, npc);
        }
    }

    // ---------------- The visit ----------------

    /// <summary>Off the train at the other place: to its square (a settlement's festival square, a little to one side;
    /// <see cref="TripSquareSpot"/>).</summary>
    private void BeginTripSight(RailTrip trip, ServerNpc npc)
    {
        var (spot, guessed) = TripSquareSpot(trip, npc);
        npc.ActivityKey = RailVisitActivityKey;
        BeginTripLeg(trip, npc, RailTripPhase.ToSight, spot, guessed);
    }

    private void BeginTripVisit(RailTrip trip, ServerNpc npc)
    {
        trip.Phase = RailTripPhase.Visiting;
        trip.PhaseSince = _uptime;
        trip.StayUntil = _uptime + RailTripVisitMin + (_tripRng.NextDouble() * (RailTripVisitMax - RailTripVisitMin));
        npc.Home = npc.Pos;
        npc.Leash = 2.5f; // it strolls round the square
        npc.ActivityKey = RailVisitActivityKey;
    }

    /// <summary>Time to go home: back to the stop it came in at.</summary>
    private void BeginTripReturn(RailTrip trip, ServerNpc npc)
    {
        trip.Returning = true;
        if (RailStopPlatformSpot(trip.FromStop, out bool guessed) is not { } platform)
        {
            AbortRailTrip(trip, npc);
            return;
        }

        npc.ActivityKey = RailTripActivityKey;
        BeginTripLeg(trip, npc, RailTripPhase.ToStation, platform, guessed);
    }

    // ---------------- Home ----------------

    /// <summary>The way home (the trip is over, or it failed): to its resting spot.</summary>
    private void BeginTripHome(RailTrip trip, ServerNpc npc)
    {
        npc.ActivityKey = string.Empty;
        BeginTripLeg(trip, npc, RailTripPhase.GoingHome, npc.Rest);
    }

    /// <summary>A failure: aboard, the passenger is set down (<see cref="OnNpcPassengerSetDown"/> turns the trip); out of
    /// reach with nobody watching its home the traveller is simply home; else the way home — walked in reach, a timed leg
    /// out of reach that never puts it down in front of a player (<see cref="PlaceAtLegEnd"/>: the approach spot).</summary>
    private void AbortRailTrip(RailTrip trip, ServerNpc npc)
    {
        if (npc.TrainId.Length > 0)
        {
            if (Rails.Trains.FirstOrDefault(x => x.Id == npc.TrainId) is { } t)
            {
                Rails.Lines.TryGetValue(t.LineId, out var line);
                SetDownNpcPassenger(npc, t, line); // calls back into OnNpcPassengerSetDown
                return;
            }

            ClearNpcRide(npc);
        }

        if (!InNpcReach(npc.Pos) && !SpotWatched(npc.Rest))
        {
            SendNpcHome(npc);
            FinishRailTrip(trip, npc);
            return;
        }

        BeginTripHome(trip, npc);
    }

    /// <summary>A trip that cannot go on before its traveller boarded: a guest still on its way to the base gives the train
    /// up and today's walk-in comes instead (<see cref="TrainGuestWalksInstead"/>, #3447); any other traveller goes home.</summary>
    private void FailRailTrip(RailTrip trip, ServerNpc npc)
    {
        if (trip.Kind == RailTripKind.Guest && !trip.Returning && npc.TrainId.Length == 0)
        {
            TrainGuestWalksInstead(trip, npc);
        }
        else
        {
            AbortRailTrip(trip, npc);
        }
    }

    /// <summary>#3445 seam: a passenger was set down off a train that could not carry it on (<paramref name="home"/>: sent
    /// straight home). Its trip ends — or it goes home from where it stands. A guest copy riding home never comes here
    /// (<see cref="SetDownTrainGuest"/>).</summary>
    private void OnNpcPassengerSetDown(ServerNpc npc, bool home)
    {
        if (npc.Trip is not { } trip)
        {
            return;
        }

        if (home)
        {
            FinishRailTrip(trip, npc);
            return;
        }

        trip.Unsettled = !InNpcReach(npc.Pos); // left where it rode, out of everybody's reach: its way home is a timed leg
        BeginTripHome(trip, npc);
    }

    /// <summary>A frozen world: off any train and home, the trip over — unless a player who just came back would see it
    /// pop out of the train or into its home: then the ordinary way home (<see cref="AbortRailTrip"/>).</summary>
    private void EndRailTripAtHome(RailTrip trip, ServerNpc npc)
    {
        if (SpotWatched(npc.Pos) || SpotWatched(npc.Rest))
        {
            AbortRailTrip(trip, npc);
            return;
        }

        DropNpcPassenger(npc);
        SendNpcHome(npc);
        FinishRailTrip(trip, npc);
    }

    /// <summary>The trip is over: the routine takes the villager back and thinks about the hour again.</summary>
    private void FinishRailTrip(RailTrip trip, ServerNpc npc)
    {
        Rails.Trips.Remove(trip);
        if (ReferenceEquals(npc.Trip, trip))
        {
            npc.Trip = null;
        }

        npc.ActivityKey = string.Empty;
        npc.Goal = null;
        npc.Path = null;
        npc.Home = npc.Pos;
        npc.Phase = NpcPhase.Unset;
        npc.PhaseCheckedAt = double.NegativeInfinity;
        BroadcastNpcs();
    }

    // ---------------- Greetings ----------------

    /// <summary>#3446: what a traveller says in passing — aboard "I'm on my way to {place}", at the other place "I'm
    /// visiting from {home} — I came by train!" (the #2713 idea: neighbours say where they live). Null otherwise.</summary>
    private string? RailTripHello(PlayerSession session, ServerNpc npc)
    {
        if (npc.Trip is not { } trip)
        {
            return null;
        }

        string name = session.State.Name ?? string.Empty;
        if (trip.Phase == RailTripPhase.Riding)
        {
            return Localize(session.Locale, "npc.hello.passenger").Replace("{0}", name).Replace("{1}", trip.Returning ? trip.HomeName : trip.AwayName);
        }

        bool away = trip.Phase is RailTripPhase.ToSight or RailTripPhase.Visiting
                    || (trip.Returning && trip.Phase is RailTripPhase.ToStation or RailTripPhase.Waiting);
        return away ? Localize(session.Locale, "npc.hello.visitor").Replace("{0}", name).Replace("{1}", trip.HomeName) : null;
    }
}

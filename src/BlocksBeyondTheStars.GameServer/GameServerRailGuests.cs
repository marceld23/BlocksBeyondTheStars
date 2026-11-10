// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Friendly visitors by train (#3447, Lyxette: "it would be charming if the settlers who visit me came by train instead of
/// appearing out of nowhere"). When a neighbour's visit is due (<see cref="TryScheduleBaseGuests"/>) and the base has a stop
/// (<see cref="RailBaseAt"/>) on a line that also stops at a town at least <see cref="TrainGuestMinDistance"/> blocks from
/// the core, and a train that takes villagers runs on it, the guest comes from that town by train — preferred over the
/// walk-in neighbour of the nearest village (a nearer town keeps the walk-in):
/// <list type="bullet">
/// <item><b>The way there</b> is a villager's train trip (<see cref="RailTripKind.Guest"/>, <c>GameServerRailTrips.cs</c>):
/// the real villager walks to its stop, boards, rides. Nothing in time — still not aboard <see cref="TrainGuestBoardSeconds"/>
/// (longer on a long line, <see cref="RailLineTimeouts"/>) after it reached the platform, or its way to the platform failed —
/// and today's walk-in comes instead (<see cref="TrainGuestWalksInstead"/>). A trip that could not start at all brings a
/// traveller (<see cref="TryScheduleBaseGuests"/>): a due visit is never spent on nobody.</item>
/// <item><b>At the base's stop</b> (<see cref="ArriveAsTrainGuest"/>): when the owner is home by day (within the stop's reach
/// of the core, <see cref="TrainGuestHost"/>) the villager steps off at the door and becomes the visit: the guest script's
/// copy (<see cref="SpawnBaseGuestAt"/> — the same person, name, face and friendship) stands where it stepped off and walks to
/// the owner, while the villager itself is home, hidden, as for every neighbour's visit. Nobody home: it stays aboard and
/// rides back.</item>
/// <item><b>Leaving</b> (<see cref="BeginGuestLeavingByTrain"/>, <see cref="StepGuestLeavingByTrain"/>): back to the
/// platform, the next train that halts there with a free seat, gone with it — the visit ends once the train has taken it out
/// of everybody's sight (<see cref="TrainGuestGoneRange"/>), at its town's halt, or as soon as nobody is within
/// <see cref="TrainGuestWatchRange"/> while it walks to the platform or waits there (NPCs only walk near a player — an
/// unwatched walk would never arrive). The walk taking longer than <see cref="TrainGuestToPlatformSeconds"/>, no train within
/// its boarding window, the line changed, or the guest set down from its train in view: it walks off on its bearing like any
/// guest (and the ordinary leaving step ends it unseen). Aboard longer than the line's ride timeout, it steps off at the next
/// standstill. A set-down copy nobody sees simply ends its visit (<see cref="SetDownTrainGuest"/>) — its resting spot is the
/// base's platform, so it is never sent "home".</item>
/// </list>
/// The base's own residents ride too: a resident trip (<see cref="MayTravelAsResident"/>) from the base's stop to a connected
/// town and back, within the same per-world cap.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A town at least this far from the base core sends its guest by train; a nearer one keeps the walk-in.</summary>
    internal const float TrainGuestMinDistance = 150f;

    /// <summary>A guest waiting on a platform this long without boarding gives up the train — the walk-in comes instead (or,
    /// leaving, it walks off). Counted from its arrival on the platform; a long line allows a round of its slowest train for
    /// villagers (<see cref="RailLineTimeouts"/>).</summary>
    private const double TrainGuestBoardSeconds = 180.0;

    /// <summary>A leaving guest's walk to the base's platform taking longer than this never arrives: it walks off on foot.</summary>
    private const double TrainGuestToPlatformSeconds = 120.0;

    /// <summary>A guest riding home is gone once no player is this close to it any more.</summary>
    private const float TrainGuestGoneRange = 48f;

    /// <summary>A leaving guest walks to the platform and waits there for the train only while a player is this close.</summary>
    private const float TrainGuestWatchRange = 64f;

    /// <summary>Test seam: a guest's train trip never starts (<see cref="BlockTrainGuestStartForTest"/>).</summary>
    private bool _trainGuestStartBlockedForTest;

    // ---------------- Coming ----------------

    /// <summary>Whether a guest is on the train to this base: its trip has neither turned back nor ended in the walk home of a
    /// failed trip (<see cref="RailTripPhase.GoingHome"/> — that villager is no longer coming).</summary>
    private bool TrainGuestUnderway(int baseId)
        => Rails.Trips.Any(t => t.Kind == RailTripKind.Guest && t.AwayBaseId == baseId && !t.Returning && t.Phase != RailTripPhase.GoingHome);

    /// <summary>The rail routes to a base: a line that carries villagers, the base's stop on it and a town's stop at least
    /// <see cref="TrainGuestMinDistance"/> from the core.</summary>
    private List<(RailLine Line, RailStopPlace Base, RailStopPlace Town)> TrainGuestRoutes(ServerBase b)
    {
        var routes = new List<(RailLine, RailStopPlace, RailStopPlace)>();
        var core = BaseCorePos(b);
        foreach (var line in Rails.Lines.Values.OrderBy(l => l.Id))
        {
            if (!LineCarriesNpcs(line.Id))
            {
                continue;
            }

            var places = RailStopPlaces(line);
            if (places.FirstOrDefault(p => p.Base is not null && p.Base.Id == b.Id) is not { } basePlace)
            {
                continue;
            }

            foreach (var town in places)
            {
                if (town.Settlement is { } s && town.Stop != basePlace.Stop // #3457: a dual stop is never its own route
                    && WrapDistSq(core, SettlementCenter(s)) >= TrainGuestMinDistance * TrainGuestMinDistance)
                {
                    routes.Add((line, basePlace, town));
                }
            }
        }

        return routes;
    }

    /// <summary>Whether a guest could come by train now (a route, a free town, somebody there who may travel, a platform at
    /// the town's stop — exactly what <see cref="TryStartTrainGuest"/> needs).</summary>
    private bool TrainGuestPossible(ServerBase b)
        => Rails.Trips.Count < MaxRailTripsPerWorld
           && TrainGuestRoutes(b).Any(r => !Rails.Trips.Any(t => t.HomeName == r.Town.Name)
                                            && _npcs.Any(n => n.Settlement == r.Town.Name && MayTravelByRail(n))
                                            && RailStopPlatformSpot(r.Town.Stop) is not null);

    /// <summary>Starts a guest's train trip to the base from a rail-connected town and tells the owner. Null when no route,
    /// no free town or nobody to come.</summary>
    private RailTrip? TryStartTrainGuest(ServerBase b, PlayerSession owner)
    {
        if (Rails.Trips.Count >= MaxRailTripsPerWorld || TrainGuestUnderway(b.Id) || _trainGuestStartBlockedForTest)
        {
            return null;
        }

        var routes = TrainGuestRoutes(b);
        for (int i = routes.Count - 1; i > 0; i--)
        {
            int j = _guestRng.Next(i + 1);
            (routes[i], routes[j]) = (routes[j], routes[i]);
        }

        foreach (var (line, basePlace, town) in routes)
        {
            if (Rails.Trips.Any(t => t.HomeName == town.Name))
            {
                continue;
            }

            var candidates = _npcs.Where(n => n.Settlement == town.Name && MayTravelByRail(n)).ToList();
            if (candidates.Count > 0 && StartRailTrip(candidates[_guestRng.Next(candidates.Count)], RailTripKind.Guest, line, town, basePlace) is { } trip)
            {
                var npc = NpcWithId(trip.NpcId)!;
                Send(owner, new ServerMessage
                {
                    Text = Localize(owner.Locale, "srv.guest.coming_by_train").Replace("{name}", npc.Name).Replace("{village}", town.Name),
                });
                _log.Info($"Base guests: '{npc.Name}' of {town.Name} takes the train to base #{b.Id}.");
                return trip;
            }
        }

        return null;
    }

    /// <summary>A guest's trip that did not get its traveller aboard in time: on the platform longer than its boarding window
    /// (counted from its arrival there), or still on its way to the platform after the longest leg out of reach and a
    /// boarding window more (a platform a player watches with nowhere to come in from unseen holds an arrival back).</summary>
    private bool TrainGuestOverdue(RailTrip trip)
    {
        if (trip.Kind != RailTripKind.Guest || trip.Returning)
        {
            return false;
        }

        double since = _uptime - trip.PhaseSince;
        return trip.Phase switch
        {
            RailTripPhase.Waiting => since > RailLineTimeouts(trip.LineId).GuestBoard,
            RailTripPhase.ToStation => since > RailTripVirtualLegMaxSeconds + TrainGuestBoardSeconds,
            _ => false,
        };
    }

    /// <summary>No train in time: the traveller goes home and today's walk-in neighbour comes instead — else a traveller, so
    /// the visit is not lost (when the owner is still home).</summary>
    private void TrainGuestWalksInstead(RailTrip trip, ServerNpc npc)
    {
        AbortRailTrip(trip, npc);
        if (_bases.FirstOrDefault(x => x.Id == trip.AwayBaseId && x.Planet == _world.LocationId) is { } b
            && Rules.FriendlyVisitors && !_baseGuests.Any(g => g.BaseId == b.Id) && GuestHost(b) is { } owner
            && SpawnBaseGuest(b, owner, BaseGuestKind.Neighbour) is null)
        {
            SpawnBaseGuest(b, owner, BaseGuestKind.Traveller);
        }
    }

    /// <summary>The owner a train guest may visit now: on foot on this body, within the stop's reach of the core, near a
    /// chained power relay or at one of the base's stops (#3457: they may wait on the platform —
    /// <see cref="OwnerNearBaseForTrainGuest"/>), by day until the evening, nothing hostile near, no scouts. Null otherwise.</summary>
    private PlayerSession? TrainGuestHost(ServerBase b)
    {
        if (!Rules.FriendlyVisitors || FindSessionByPlayerId(b.OwnerId) is not { } owner || !GuestOwnerOnFoot(owner))
        {
            return null;
        }

        double day = LocalDayFraction(BaseCorePos(b));
        if (!OwnerNearBaseForTrainGuest(b, owner.State.Position) || day < BaseGuestDayStart || day > RoutineEveningStart)
        {
            return null;
        }

        return GuestHostileNear(owner.State.Position) || ScoutsAlreadyAt(b.Id) ? null : owner;
    }

    /// <summary>
    /// A guest's train halted at the base's stop: with the owner home, the villager steps off and its visit begins right
    /// there — the guest script's copy at the door, the villager itself home (hidden while it visits). Nobody home (or a
    /// guest already there): it stays aboard and rides back.
    /// </summary>
    private void ArriveAsTrainGuest(RailTrip trip, ServerNpc npc, ServerTrain t)
    {
        _ = t;
        var b = _bases.FirstOrDefault(x => x.Id == trip.AwayBaseId && x.Planet == _world.LocationId);
        var owner = b is null || _baseGuests.Any(g => g.BaseId == b.Id) ? null : TrainGuestHost(b);
        if (b is null || owner is null)
        {
            trip.Returning = true; // nobody home: it rides back the way it came
            trip.PhaseSince = _uptime;
            return;
        }

        if (!AlightNpc(npc, trip.ToStop))
        {
            FinishRailTrip(trip, npc); // nowhere to step off: it was sent home
            return;
        }

        var at = npc.Pos;
        FinishRailTrip(trip, npc);
        SendNpcHome(npc); // the villager is home — hidden while its guest visits (SpawnBaseGuestAt)
        var village = _settlements.FirstOrDefault(s => !s.Ruined && s.Name == trip.HomeName);
        double bearing = village is not null ? BearingFrom(BaseCorePos(b), SettlementCenter(village)) : _guestRng.NextDouble() * Math.PI * 2.0;
        var guest = SpawnBaseGuestAt(b, owner, BaseGuestKind.Neighbour, npc, village, at, bearing);
        guest.ByTrain = true;
        guest.TrainLineId = trip.LineId;
        guest.TrainStop = trip.AwayStop;
        guest.HomeStop = trip.HomeStop;
    }

    // ---------------- Leaving ----------------

    /// <summary>A train guest's way home starts: to the base's platform, and the owner reads "{name} is taking the train home
    /// to {village}." False (the walk on its bearing instead) when the stop or its platform is gone.</summary>
    private bool BeginGuestLeavingByTrain(BaseGuest g, ServerNpc npc)
    {
        if (!StopOnLine(g.TrainLineId, g.TrainStop) || !StopOnLine(g.TrainLineId, g.HomeStop)
            || RailStopPlatformSpot(g.TrainStop) is not { } platform)
        {
            g.ByTrain = false;
            return false;
        }

        g.TrainLeave = 0;
        SetNpcGoal(npc, platform, NpcArrival.None, 0.6f);
        if (g.Place.Length > 0 && FindSessionByPlayerId(g.OwnerId) is { Spectating: false } owner && owner.CurrentLocationId == _world.LocationId)
        {
            Send(owner, new ServerMessage
            {
                Text = Localize(owner.Locale, "srv.guest.train_home").Replace("{name}", npc.Name).Replace("{village}", g.Place),
            });
        }

        return true;
    }

    /// <summary>
    /// One step of a train guest's way home (1 Hz). Aboard: <see cref="StepTrainGuestRiding"/>. On foot, walking to the base's
    /// platform (<c>TrainLeave</c> 0) or waiting there (1): nobody within <see cref="TrainGuestWatchRange"/> — the visit simply
    /// ends (NPCs walk only near a player: an unwatched walk would never arrive, #3447 review); the walk longer than
    /// <see cref="TrainGuestToPlatformSeconds"/>, no train in its boarding window or the line changed — the walk on its
    /// bearing. Returns false when the ordinary leaving step should take over.
    /// </summary>
    private bool StepGuestLeavingByTrain(BaseGuest g, ServerNpc npc)
    {
        if (npc.TrainId.Length > 0)
        {
            StepTrainGuestRiding(g, npc);
            return true;
        }

        if (!StopOnLine(g.TrainLineId, g.TrainStop) || !StopOnLine(g.TrainLineId, g.HomeStop))
        {
            GuestLeavesOnFoot(g, npc);
            return false;
        }

        if (!AnyPlayerWithin(npc.Pos, TrainGuestWatchRange))
        {
            EndBaseGuest(g, npc); // nobody near the way to the platform or the platform: it is simply gone, its villager home
            return true;
        }

        if (g.TrainLeave == 0)
        {
            if (npc.Goal is not null)
            {
                return _uptime - g.PhaseSince <= TrainGuestToPlatformSeconds || GuestGivesUpTheTrain(g, npc); // still walking there
            }

            g.TrainLeave = 1;
            g.TrainWaitSince = _uptime;
        }

        var t = Rails.Trains.FirstOrDefault(x => x.LineId == g.TrainLineId && x.Halted && x.HaltStop == g.TrainStop
                                                 && CarriesNpcs(x) && FreeNpcSeat(x) is not null);
        if (t is not null && TryBoardAtHalt(npc, t, g.TrainStop, eager: true))
        {
            g.TrainLeave = 2;
            g.TrainWaitSince = _uptime; // from now the ride's clock
            npc.ActivityKey = RailTripActivityKey;
            return true;
        }

        return _uptime - g.TrainWaitSince <= RailLineTimeouts(g.TrainLineId).GuestBoard || GuestGivesUpTheTrain(g, npc);
    }

    /// <summary>The walk to the platform or the wait there took too long: off on foot (always false — the ordinary leaving
    /// step takes over).</summary>
    private bool GuestGivesUpTheTrain(BaseGuest g, ServerNpc npc)
    {
        GuestLeavesOnFoot(g, npc);
        return false;
    }

    /// <summary>A train guest riding home: gone once out of everybody's sight, or at its town's halt — stepping off and
    /// walking into town when somebody is there to see it. A ride longer than the line's ride timeout (a train that stays
    /// in view and never gets there) steps off at the next standstill, a minute later wherever the train is.</summary>
    private void StepTrainGuestRiding(BaseGuest g, ServerNpc npc)
    {
        var t = Rails.Trains.FirstOrDefault(x => x.Id == npc.TrainId);
        if (t is null)
        {
            EndBaseGuest(g, npc);
            return;
        }

        bool home = t.Halted && t.HaltStop == g.HomeStop;
        if (!AnyPlayerWithin(npc.Pos, home ? BaseGuestDespawnUnseenRange : TrainGuestGoneRange))
        {
            EndBaseGuest(g, npc); // the train took it out of everybody's sight (or nobody waits at its town)
            return;
        }

        if (!home)
        {
            double over = _uptime - g.TrainWaitSince - RailTripTimeouts(RailTrainRound(t)).Ride;
            if (over > 0.0 && (t.Halted || t.Velocity < 1f || over > 60.0))
            {
                Rails.Lines.TryGetValue(t.LineId, out var line);
                SetDownNpcPassenger(npc, t, line); // → SetDownTrainGuest: off beside the wagon, or gone unseen
            }

            return;
        }

        if (!AlightNpc(npc, g.HomeStop))
        {
            EndBaseGuest(g, npc); // nowhere to step off at its town: the visit simply ends
            return;
        }

        g.ByTrain = false; // stepped off at its town: it walks in, and the ordinary leaving step ends the visit unseen
        npc.ActivityKey = "npc.activity.visiting";
        if (GuestSpotAlong(npc.Pos, g.LeaveBearing, 12f) is { } into)
        {
            SetNpcGoal(npc, into, NpcArrival.None, 0.6f);
        }
    }

    /// <summary>The train is no way home after all: the guest walks off on its bearing like any guest.</summary>
    private void GuestLeavesOnFoot(BaseGuest g, ServerNpc npc)
    {
        g.ByTrain = false;
        g.PhaseSince = _uptime;
        if (GuestLeaveSpot(g, npc) is { } spot)
        {
            SetNpcGoal(npc, spot, NpcArrival.None, 0.6f);
        }
        else
        {
            StopGuestWalking(npc);
        }
    }

    /// <summary>#3445 seam for guests (<see cref="SetDownNpcPassenger"/>): a guest riding home was set down off its train
    /// (stowed, its line changed, a ride that never got there). A guest copy's resting spot is where it stepped off at the
    /// base, so it is never sent "home" (#3447 review: it popped back onto the base's platform): set down beside the wagon
    /// in view (<paramref name="spot"/>), it walks off on its bearing like any leaving guest; nobody near, or nowhere to stand,
    /// the visit simply ends — its villager is home.</summary>
    private void SetDownTrainGuest(BaseGuest g, ServerNpc npc, Vector3f? spot)
    {
        if (spot is not { } at || !AnyPlayerWithin(at, BaseGuestDespawnUnseenRange))
        {
            EndBaseGuest(g, npc);
            return;
        }

        npc.Pos = at;
        npc.Home = at;
        npc.Loco.ModeTimer = 0f;
        g.Phase = BaseGuestPhase.Leaving;
        GuestLeavesOnFoot(g, npc);
        BroadcastNpcs();
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3447 review): a guest's train trip cannot start (as if its town's platform were gone between the
    /// roll and the start) — the visit must fall back to a traveller.</summary>
    public void BlockTrainGuestStartForTest(bool blocked = true) => _trainGuestStartBlockedForTest = blocked;

    /// <summary>Test seam (#3447 review): whether a guest could come to a base by train now.</summary>
    public bool TrainGuestPossibleForTest(int baseId) => _bases.FirstOrDefault(b => b.Id == baseId) is { } b && TrainGuestPossible(b);
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Walking around inside your own ship while it floats in space. This reuses the <b>existing</b> ship
/// interior — the very same <see cref="StampShip"/> layout you walk through when landed on a planet — by
/// loading it into a void world, the ship staying put in its space instance. From the flight view you step
/// inside; from inside, a helm console returns you to the flight view (no take-off — you never landed) and
/// an airlock starts an EVA (those interactions are wired in later stages). Server-authoritative; the client
/// renders blocks and sends intents only.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Void world type backing the in-space ship interior (space sky, life support, no terrain or
    /// weather). See data/planets.json.</summary>
    private const string ShipInteriorType = "ship_interior";

    // Players currently walking inside their ship in space → how to drop them back into the flight view at
    // the ship's parked position, so the ship "stays where it is" across the visit.
    private readonly Dictionary<string, ShipInteriorReturn> _inShipInterior = new();

    /// <summary>How to drop a pilot back into the flight view: the instance, the ship's pose there (position +
    /// heading, #2118) and the body world under the flight.</summary>
    private readonly record struct ShipInteriorReturn(string InstanceId, SpacePlayerPose Ship, string ReturnLoc, string ReturnType);

    /// <summary>True while the player is walking inside their ship in space (not piloting, not on a surface).</summary>
    public bool InShipInterior(string playerId) => _inShipInterior.ContainsKey(playerId);

    /// <summary>Step out of the pilot seat — or in from an EVA — into your ship's walkable interior while in
    /// space. Loads the ship interior as a void world and stamps your existing ship layout into it; the
    /// ship's flight-view position is remembered so taking the helm again puts it back exactly where it was.</summary>
    public void EnterShipInterior(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        // #2723: "back to my ship" while visiting a docked friend's ship — home into the own interior, whose return
        // record (the parked ship's pose) the visit kept.
        if (IsVisitingShip(playerId))
        {
            EndShipVisit(playerId, "@srv.visit.back_home", hostNotice: "@srv.visit.guest_left:" + session.State.Name);
            return;
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            Reject(session, "ship", "@srv.misc.interior_space_only");
            return;
        }

        ParkShipForWalkabout(session, instanceId, instance);

        Send(session, new SpaceClosed { Reason = "@srv.misc.stepped_inside", ShipDisabled = false });
        LoadShipInteriorFor(session);
        RaiseGameEvent(session, Shared.Story.GameEventKeys.InteriorEntered); // #2479: the pilot course's walk-inside lesson
        _log.Info($"Player '{session.State.Name}' stepped inside their ship (world 'shipint:{playerId}').");
    }

    /// <summary>The void world that holds a player's own ship interior.</summary>
    private static string ShipInteriorLoc(string playerId) => "shipint:" + playerId;

    /// <summary>Takes a pilot (at the helm or on a spacewalk) out of the flight instance while the ship stays where it
    /// floats: the way back is recorded in <see cref="_inShipInterior"/>, and the parked hull keeps showing to the other
    /// pilots and keeps the instance loaded (#2431). Shared by stepping inside the own ship and by the docking visit
    /// (#2723). The active world must be the body under the flight — it is the way back.</summary>
    private void ParkShipForWalkabout(PlayerSession session, string instanceId, SpaceInstance instance)
    {
        string playerId = session.State.PlayerId;

        // Remember how to drop back into the flight view (and the ship's parked spot, even if the now-empty
        // instance unloads while we're inside). #994: THIS pilot's spot, not whichever pilot moved last.
        // #2118: the SHIP's pose — boarding from an EVA, the pilot's pose is the suit's, not the ship's.
        var ship = instance.ShipPoses.TryGetValue(playerId, out var shipPose)
            ? shipPose
            : new SpacePlayerPose(PilotPositionIn(instance, playerId), 0f, false);
        _inShipInterior[playerId] = new ShipInteriorReturn(instanceId, ship, session.CurrentLocationId, _world.PlanetKey);

        instance.Players.Remove(playerId);
        instance.ShipPoses.Remove(playerId);
        _playerInstance.Remove(playerId);
        // #2431: the instance stays loaded while a parked ship floats in it (its hull structure lives there — the next
        // pilot to arrive gets it as a "ship_remote" design like any other ship out here). The tick skips an instance
        // without pilots, and the last pilot's leave tears it down once no parked ship is left (LeaveSpace).
        if (instance.Players.Count == 0 && !AnyParkedShipIn(instance))
        {
            // ShipPosition is saved above; restored on return. #2748: the floating salvage is stashed like on every
            // other teardown (this one used to drop it).
            TearDownSpaceInstance(instance);
        }
    }

    /// <summary>#2722: forgets a player's walk inside their floating ship when they quit (and, as a safety net, when they
    /// join again) — the ship-interior counterpart of <see cref="ForgetStationBoarding"/>. The record used to outlive the
    /// session: after a rejoin onto the planet, walking out of the landed ship took the hatch route into a spacewalk,
    /// the planet air did not count, and the forgotten "parked" hull floated in orbit again for the other pilots. With
    /// the record gone, a flight instance that only that parked hull kept loaded closes like the last pilot's leave.</summary>
    private void ForgetShipInterior(string playerId)
    {
        if (!_inShipInterior.Remove(playerId, out var walkabout))
        {
            return;
        }

        if (_spaceInstances.TryGetValue(walkabout.InstanceId, out var instance) && instance.Players.Count == 0 && !AnyParkedShipIn(instance))
        {
            TearDownSpaceInstance(instance); // #1475/#2354/#2748: salvage + carve ledger outlive the flight
        }
    }

    /// <summary>#2722/#2723: a join starts with no walkabout and no visit — whatever a previous session of this player
    /// left behind is dropped (the disconnect already does this; this catches anything that slipped past it). Only for
    /// a name nobody holds right now.</summary>
    private void ForgetWalkaboutOnJoin(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not null)
        {
            return;
        }

        _shipVisits.Remove(playerId);
        ForgetShipInterior(playerId);
    }

    /// <summary>Loads the pilot's ship interior as its own void world and puts them inside it, at the heal tank:
    /// the ship structure OBJECT is parked in it (ship-as-object: the same structure the flight view renders —
    /// design + persisted edits — so interior furnishing and EVA hull edits are one and the same grid everywhere).
    /// The caller has already recorded the way back in <see cref="_inShipInterior"/>.</summary>
    private void LoadShipInteriorFor(PlayerSession session)
    {
        string shipLoc = ShipInteriorLoc(session.State.PlayerId);
        LoadWorld(ShipInteriorType, shipLoc);
        SetCurrent(session);
        PlaceLandedShip();

        session.CurrentLocationId = shipLoc;
        session.State.Position = _shipPlaced ? _healTank : session.State.Position;
        session.State.AboardShip = true; // inside the hull → life support, oxygen safe
        session.State.InEva = false;     // entering from an EVA ends the spacewalk
        // #2723: coming home from a friend's ship the client still reports a step or two from THAT interior — a stale pose
        // outside this hull would read as a walk out of the hatch. Drop far reports until the client is at the heal tank,
        // and (both interiors share their coordinates) reports from outside the hull for a moment (StaleShipInteriorPose).
        session.AwaitingSpawnAdopt = true;
        session.ShipInteriorSettleUntil = _uptime + ShipInteriorSettleSeconds;
        session.Streaming.SentChunks.Clear();

        Send(session, new WorldReset { PlanetType = ShipInteriorType, PlanetName = string.Empty, SystemName = string.Empty, Hyperjump = false });
        SendLandedShips(session); // the ship object itself — the world is void apart from it — BEFORE the position (#1450)
        SendPlayerState(session);
        Send(session, new RespawnNotice { X = session.State.Position.X, Y = session.State.Position.Y, Z = session.State.Position.Z, Reason = "@srv.misc.stepped_inside" });
        SendEnvironment(session);
        SendInventory(session);
        SendShipStations(session);
        SendDoors(session);
        // The helm in here is the repair point too (R at the cockpit cell): the readout the client holds is from the
        // last landing, so a hull dented since then showed no panel and R at the helm did nothing (2026-10-09).
        SendShipRepairStatus(session);
        // #2723: a docked friend may already be visiting (the host comes back in from a spacewalk) — faces and body paint
        // both ways, as on any world change. Nobody else is here otherwise, and then this sends nothing.
        SyncAppearance(session);
    }

    /// <summary>#2723: how long after a switch into a ship interior a report from outside the hull counts as stale
    /// (<see cref="StaleShipInteriorPose"/>). Nobody walks from the heal tank out of the hatch this fast.</summary>
    private const double ShipInteriorSettleSeconds = 1.5;

    /// <summary>#2723: just after a switch into a ship interior (the own one, or a docked friend's) the client stands at
    /// the heal tank — a reported pose outside that hull is one the client sent from the interior it left, before the
    /// reset reached it. Every ship interior shares the same coordinates, so the spawn gate's distance cannot tell it from
    /// a step, and it would read as a walk out of the hatch (a spacewalk, or a guest sent home). A client that stamps its
    /// moves with the world id never gets here with such a pose (see HandleMove); this covers older clients.</summary>
    private bool StaleShipInteriorPose(PlayerSession session, Vector3f reported)
    {
        if (_uptime >= session.ShipInteriorSettleUntil
            || !session.CurrentLocationId.StartsWith("shipint:", System.StringComparison.Ordinal))
        {
            return false;
        }

        var hull = VisitedShipOf(session.State.PlayerId) ?? _worlds.Active.LandedFor(session.State.PlayerId);
        return hull.Placed && !WithinShipExtents(hull, reported, 0.4f);
    }

    /// <summary>When the pilot was last told that their ship can't fly (#2233) — the hatch route runs every tick
    /// while they stand outside the hull, so the reason is repeated at most every few seconds.</summary>
    private readonly Dictionary<string, double> _unflyableNoticeAt = new();

    private const double UnflyableNoticeSeconds = 4.0;

    /// <summary>The ship can't fly (#2233): the pilot stays inside, where it can be repaired. Through the helm that
    /// is just the reason; through the hatch (or a hole in the hull) they are put back on board — otherwise the
    /// next tick would try the airlock again, and there is no space outside to float in.</summary>
    private void KeepPilotInsideUnflyableShip(PlayerSession session, string problem, bool eva)
    {
        string playerId = session.State.PlayerId;
        if (!_unflyableNoticeAt.TryGetValue(playerId, out var last) || _uptime - last >= UnflyableNoticeSeconds)
        {
            _unflyableNoticeAt[playerId] = _uptime;
            RejectSpace(session, problem);
            Send(session, new ServerMessage { Text = "@srv.ship.stay_aboard_repair" });
        }

        if (eva && _shipPlaced)
        {
            session.State.Position = _healTank;
            session.State.InEva = false;
            SendPlayerState(session);
            Send(session, new RespawnNotice { X = _healTank.X, Y = _healTank.Y, Z = _healTank.Z, Reason = "@srv.ship.stay_aboard_repair" });
        }
    }

    /// <summary>Take the helm again: leave the ship interior straight back into the flight view, the ship
    /// restored to exactly where it was parked (no take-off animation — you never landed).</summary>
    public void ExitShipToFlight(string playerId) => ReturnToFlight(playerId, eva: false);

    /// <summary>Cycle out through the airlock: leave the ship interior into the flight view as a floating EVA
    /// suit next to the parked ship — the spacewalk begins (oxygen now drains).</summary>
    public void StartEvaFromShip(string playerId) => ReturnToFlight(playerId, eva: true);

    private void ReturnToFlight(string playerId, bool eva)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || !_inShipInterior.TryGetValue(playerId, out var ret))
        {
            return;
        }

        // #2233: every launch gate BEFORE the interior is left. This used to load the body's world and send the reset
        // first and only then let EnterSpace refuse a ship that lost its engine, door or airtightness — the pilot then
        // stood on the planet at the interior's coordinates, in no instance, with the ship gone from that world (so it
        // could not even be repaired there), and the hatch route had already flagged them as on an EVA there.
        Serve(session); // the interior world + THIS pilot's ship
        if (SpaceLaunchProblem(session, requireAboard: false) is { } problem)
        {
            KeepPilotInsideUnflyableShip(session, problem, eva);
            return;
        }

        // #2723: the host takes the helm — the ship is about to move, so a visiting guest goes home first. Walking out of
        // the airlock (eva) leaves the ship floating where it is: the guest may stay.
        if (!eva && ShipGuestOf(playerId) is { } guestId)
        {
            EndShipVisit(guestId, "@srv.visit.host_moving:" + session.State.Name);
        }

        bool wasVisiting = IsVisitingShip(playerId); // a guest at the helm (or launching) flies their OWN ship again
        _inShipInterior.Remove(playerId);

        // Restore the planet world under the flight view (so a later landing drops you there), like LeaveStation —
        // and TELL the client, like LeaveStation (#2117): the interior's WorldReset made the body "the world we just
        // left", so without a reset of its own every chunk of a later landing here was dropped — "DAS NICHTS".
        LoadWorld(ret.ReturnType, ret.ReturnLoc);
        SetCurrent(session);
        session.CurrentLocationId = ret.ReturnLoc;
        if (wasVisiting)
        {
            EndShipVisit(playerId, reason: null, sendHome: false, hostNotice: "@srv.visit.guest_left:" + session.State.Name);
        }

        session.State.AboardShip = true;
        session.State.InEva = false;
        session.Streaming.SentChunks.Clear();
        SendReturnWorldSnapshot(session, ret.ReturnType);

        // Back into the flight view, the ship exactly where it was parked and heading where it pointed (#2118) —
        // the flight view is told that pose. Skip the take-off sequence: you never landed, you just stepped out.
        // #2723: out through the airlock the ship stays parked — a visiting friend stays aboard (airlock: eva).
        EnterSpace(playerId, skipLaunch: true, resume: ret.Ship, airlock: eva);
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var inst))
        {
            // Safety net (#2233): EnterSpace refused for a reason the gate above does not know. Back aboard rather
            // than stranded on the body below; InEva is only ever set once the pilot really is in an instance.
            _log.Warn($"Player '{session.State.Name}' could not return to flight — put back inside the ship.");
            _inShipInterior[playerId] = ret;
            LoadShipInteriorFor(session);
            return;
        }

        if (eva)
        {
            inst.PlayerPoses[playerId] = ret.Ship with { Eva = true };
            session.State.InEva = true; // stepping out the airlock starts the spacewalk → oxygen drains
            SendPlayerState(session);   // tell the client it is now floating in EVA next to the ship
            RaiseGameEvent(session, Shared.Story.GameEventKeys.EvaStarted, Shared.Story.GameEventKeys.EvaHatch); // #2479: the spacewalk lesson
        }

        _log.Info($"Player '{session.State.Name}' {(eva ? "stepped out for an EVA" : "took the helm again")} (flight view).");
    }
}

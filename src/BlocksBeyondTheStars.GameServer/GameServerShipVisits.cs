// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The docking visit (#2723): two docked players float side by side in space, and the guest walks over into the host's
/// ship. Docking means exactly this visit — no shared cargo, no coupled flight.
/// <para>
/// Ship interiors are per-owner void worlds (<c>shipint:&lt;id&gt;</c>, <see cref="LoadShipInteriorFor"/>), and presence,
/// doors and block broadcasts are per world, so two sessions can share one interior. A visit parks the guest's own ship
/// exactly like stepping inside it (<see cref="ParkShipForWalkabout"/>: the hull keeps floating, the other pilots see it
/// parked, it keeps the flight instance loaded) and then loads the guest into the HOST's interior world, at the host's
/// heal tank.
/// </para>
/// <list type="bullet">
/// <item>Allowed only while the two are docked, in the same flight instance, within <see cref="DockRange"/>, and with
/// the host's ship floating parked — the host walks inside it or is out on a spacewalk. A host at the helm is told that
/// a friend wants to come over.</item>
/// <item>The guest goes home — into their own interior, whose return record the visit kept, so the helm resumes at the
/// parked pose — before the host's ship can move: the host takes the helm, launches, switches ships, dies, quits or
/// leaves the flight; either side undocks; and a 1 Hz check catches anything else. The guest is never carried along as
/// a passenger. "Back to my ship" and walking out of the host's hull go home too; the helm or a launch takes the guest
/// straight to their own helm.</item>
/// <item>Look only: no building, mining or painting in the host's ship (the void around it included), no ship stations
/// and no bio lab. Walking, looking, doors, sitting, chat and trading with the host stay.</item>
/// <item>Air: the guest breathes inside the host's hull like any visitor in a parked ship on a planet — the aboard flag
/// follows "inside a parked hull", and an unsealed extension holds no air.</item>
/// </list>
/// Visits live in memory only — no save change. A quit or a server restart ends them like the docking itself.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A guest's visit: whose ship, in which flight instance, and the host's active ship when it began (a ship
    /// switch sends the guest home).</summary>
    private sealed record ShipVisit(string HostId, string HostName, string InstanceId, string HostShipId);

    /// <summary>How often the open visits are checked against the host's ship (<see cref="CheckShipVisits"/>).</summary>
    private const double VisitCheckSeconds = 1.0;

    // Guest id → visit. Docking is one-to-one, so a host has at most one guest.
    private readonly Dictionary<string, ShipVisit> _shipVisits = new();

    // Guests whose feet left the host's hull during this tick's world loop — sent home after the loop (a world change).
    private List<string> _visitWalkOuts = new();
    private List<string> _visitWalkOutsSpare = new();

    private double _sinceVisitCheck;

    /// <summary>How often a host at the helm is told that a docked friend wants to come over (the guest is refused every time).</summary>
    private const double VisitAskNoticeSeconds = 10.0;

    // Guest id → uptime of the last "wants to come over" notice sent to their host.
    private readonly Dictionary<string, double> _visitAskedAt = new();

    /// <summary>True while the player walks in a docked friend's ship as a guest.</summary>
    public bool IsVisitingShip(string playerId) => _shipVisits.ContainsKey(playerId);

    /// <summary>The host whose ship the guest visits, or null.</summary>
    public string? VisitedHostOf(string guestId) => _shipVisits.TryGetValue(guestId, out var visit) ? visit.HostId : null;

    /// <summary>The guest visiting this host's ship, or null (docking is one-to-one, so there is at most one).</summary>
    public string? ShipGuestOf(string hostId)
    {
        foreach (var (guestId, visit) in _shipVisits)
        {
            if (visit.HostId == hostId)
            {
                return guestId;
            }
        }

        return null;
    }

    private void HandleVisitShip(PlayerSession session, VisitShipIntent intent)
        => VisitShip(session.State.PlayerId, intent.HostId);

    /// <summary>The guest asks to walk over into the docked host's ship (see the class summary for the rules).</summary>
    public void VisitShip(string guestId, string hostId)
    {
        var guest = FindSessionByPlayerId(guestId);
        if (guest is null)
        {
            return;
        }

        Serve(guest); // the body under the guest's flight is the way back to it
        var host = string.IsNullOrEmpty(hostId) || hostId == guestId ? null : FindSessionByPlayerId(hostId);
        if (host is null || !AreDocked(guestId, hostId))
        {
            RejectVisit(guest, "@srv.visit.not_docked");
            return;
        }

        if (_shipVisits.TryGetValue(guestId, out var current) && current.HostId == hostId)
        {
            return; // already aboard — a repeated press
        }

        if (_shipVisits.TryGetValue(hostId, out var hostAway) && hostAway.HostId == guestId)
        {
            // Both pressed at once: the host already came over, so the two are together in the guest's own ship.
            RejectVisit(guest, "@srv.visit.already_here:" + host.State.Name);
            return;
        }

        if (!SpacePositionOf(guest, out string guestInstance, out var guestPos)
            || !HostShipPose(host, out string hostInstance, out var hostShip, out bool hostFlying))
        {
            RejectVisit(guest, "@srv.visit.space_only");
            return;
        }

        if (guestInstance != hostInstance || guestPos.DistanceSquared(hostShip.Pos) > DockRange * DockRange)
        {
            RejectVisit(guest, "@srv.visit.too_far");
            return;
        }

        if (hostFlying)
        {
            RejectVisit(guest, "@srv.visit.host_flying");
            // The host hears "X wants to come over" at most every few seconds, however often the guest presses.
            if (!_visitAskedAt.TryGetValue(guestId, out double asked) || _uptime - asked >= VisitAskNoticeSeconds)
            {
                _visitAskedAt[guestId] = _uptime;
                Send(host, new ServerMessage { Text = "@srv.visit.host_flying_notice:" + guest.State.Name });
            }

            return;
        }

        BeginVisit(guest, host, hostInstance, hostOnEva: host.State.InEva);
    }

    /// <summary>Where a player is in space, for docking and for a visit: their ship (or suit, on a spacewalk) in a flight
    /// instance, or their own ship floating parked while they walk its interior. False anywhere else — a surface, a
    /// station, or a friend's ship they are visiting.</summary>
    private bool SpacePositionOf(PlayerSession player, out string instanceId, out Vector3f pos)
    {
        string id = player.State.PlayerId;
        if (_playerInstance.TryGetValue(id, out var flying) && _spaceInstances.TryGetValue(flying, out var instance))
        {
            instanceId = flying;
            pos = PilotPositionIn(instance, id);
            return true;
        }

        if (!_shipVisits.ContainsKey(id) && _inShipInterior.TryGetValue(id, out var walkabout) && player.CurrentLocationId == ShipInteriorLoc(id))
        {
            instanceId = walkabout.InstanceId;
            pos = walkabout.Ship.Pos;
            return true;
        }

        instanceId = string.Empty;
        pos = default;
        return false;
    }

    /// <summary>Where the host's ship floats: parked while the host walks its interior, or beside the host's suit on a
    /// spacewalk — or flown by the host right now (<paramref name="atHelm"/>). False when the host is not in space with
    /// their ship at all (a planet, a station, someone else's ship).</summary>
    private bool HostShipPose(PlayerSession host, out string instanceId, out SpacePlayerPose ship, out bool atHelm)
    {
        string id = host.State.PlayerId;
        atHelm = false;
        if (_playerInstance.TryGetValue(id, out var flying) && _spaceInstances.TryGetValue(flying, out var instance))
        {
            instanceId = flying;
            ship = instance.ShipPoses.TryGetValue(id, out var shipPose) ? shipPose : new SpacePlayerPose(PilotPositionIn(instance, id), 0f, false);
            atHelm = !host.State.InEva;
            return true;
        }

        if (!_shipVisits.ContainsKey(id) && _inShipInterior.TryGetValue(id, out var walkabout) && host.CurrentLocationId == ShipInteriorLoc(id))
        {
            instanceId = walkabout.InstanceId;
            ship = walkabout.Ship;
            return true;
        }

        instanceId = string.Empty;
        ship = default;
        return false;
    }

    /// <summary>Moves the guest into the host's ship: the own ship parks (unless it already floats parked because the guest
    /// walked its interior), the host's interior world gets the host's hull, and the guest arrives at its heal tank.</summary>
    private void BeginVisit(PlayerSession guest, PlayerSession host, string instanceId, bool hostOnEva)
    {
        string guestId = guest.State.PlayerId;
        string hostId = host.State.PlayerId;
        string arrived = "@srv.visit.arrived:" + host.State.Name;

        if (_playerInstance.TryGetValue(guestId, out var flying) && _spaceInstances.TryGetValue(flying, out var instance))
        {
            ParkShipForWalkabout(guest, flying, instance); // the active world is the body under the flight (Serve above)
            Send(guest, new SpaceClosed { Reason = arrived, ShipDisabled = false });
        }

        // The host's interior world holds the host's hull. Out on a spacewalk the host is not in it, so the hull may never
        // have been placed there — or may have been worked on from outside since: place it (again) first.
        string hostLoc = ShipInteriorLoc(hostId);
        LoadWorld(ShipInteriorType, hostLoc);
        if (hostOnEva || !_worlds.Active.LandedFor(hostId).Placed)
        {
            SetCurrent(host);
            PlaceLandedShip();
        }

        var hostShip = _worlds.Active.LandedFor(hostId);
        SetCurrent(guest);
        _shipVisits[guestId] = new ShipVisit(hostId, host.State.Name, instanceId, host.ActiveShipId);
        guest.CurrentLocationId = hostLoc;
        guest.State.Position = hostShip.Placed ? hostShip.HealTank : guest.State.Position;
        guest.State.AboardShip = true; // inside a parked hull — UpdateAboard keeps it honest from here on
        guest.State.InEva = false;     // coming over from a spacewalk ends it
        guest.AwaitingSpawnAdopt = true; // the client may still report a step from where it was — keep the arrival spot
        guest.ShipInteriorSettleUntil = _uptime + ShipInteriorSettleSeconds; // …even one from the own interior, at the same coordinates
        guest.Streaming.SentChunks.Clear();

        Send(guest, new WorldReset { PlanetType = ShipInteriorType, PlanetName = string.Empty, SystemName = string.Empty, Hyperjump = false, ShipOwnerId = hostId });
        SendLandedShips(guest); // the host's hull — the world is void apart from it — BEFORE the position (#1450)
        SendPlayerState(guest);
        Send(guest, new RespawnNotice { X = guest.State.Position.X, Y = guest.State.Position.Y, Z = guest.State.Position.Z, Reason = arrived });
        SendEnvironment(guest);
        SendInventory(guest);
        SendVisitedShipStations(guest, hostShip);
        SendDoors(guest);
        SyncAppearance(guest); // faces and body paint both ways, as on any world change
        Send(host, new ServerMessage { Text = "@srv.visit.guest_arrived:" + guest.State.Name });
        _log.Info($"Player '{guest.State.Name}' is visiting '{host.State.Name}''s ship (world '{hostLoc}').");
    }

    /// <summary>The host's ship stations, for the guest: the client draws the rooms' decor from this list (the helm's map,
    /// the medbay tank). Using them stays the owner's — <see cref="UseStation"/> refuses a guest.</summary>
    private void SendVisitedShipStations(PlayerSession guest, LandedShip hostShip)
    {
        var stations = new List<NetShipStation>();
        if (hostShip.Placed)
        {
            foreach (var (type, pos) in hostShip.Stations)
            {
                stations.Add(new NetShipStation { Type = type, X = pos.X, Y = pos.Y, Z = pos.Z });
            }
        }

        Send(guest, new ShipStations { Stations = stations.ToArray() });
    }

    /// <summary>Ends a guest's visit. <paramref name="sendHome"/> moves a guest still standing in the host's ship into their
    /// own interior (its return record stayed, so the helm resumes at the parked pose); false only forgets the visit,
    /// for a guest who is leaving anyway (a quit, a death, their own helm). <paramref name="reason"/> is told to the guest,
    /// <paramref name="hostNotice"/> to the host. Both cursors are restored, so any caller may end a visit mid-operation.</summary>
    private void EndShipVisit(string guestId, string? reason, bool sendHome = true, string? hostNotice = null)
    {
        if (!_shipVisits.Remove(guestId, out var visit))
        {
            return;
        }

        string? previousWorld = _worlds.Active?.LocationId;
        var previousCurrent = _current;
        var guest = FindSessionByPlayerId(guestId);
        var host = FindSessionByPlayerId(visit.HostId);
        string hostLoc = ShipInteriorLoc(visit.HostId);

        if (sendHome && guest is not null && guest.CurrentLocationId == hostLoc)
        {
            if (_inShipInterior.ContainsKey(guestId))
            {
                LoadShipInteriorFor(guest);
            }
            else
            {
                // Safety net: no parked hull to go back to (nothing should clear it while visiting) — the ship's body.
                _log.Warn($"Visiting player '{guest.State.Name}' had no parked ship to return to — recovered to the ship's body.");
                RecoverToShip(guest, reason ?? "@srv.visit.back_home", salvaged: false, died: false);
            }
        }

        // The host sees the guest go at once (not after the presence timeout).
        if (guest is not null && guest.CurrentLocationId != hostLoc && SetActiveWorld(hostLoc))
        {
            BroadcastToWorld(new PlayerLeft { PlayerId = guestId });
        }

        if (guest is not null && !string.IsNullOrEmpty(reason))
        {
            Send(guest, new ServerMessage { Text = reason });
        }

        if (host is not null && !string.IsNullOrEmpty(hostNotice))
        {
            Send(host, new ServerMessage { Text = hostNotice });
        }

        if (previousWorld is not null)
        {
            SetActiveWorld(previousWorld);
        }

        if (previousCurrent is not null)
        {
            SetCurrent(previousCurrent);
        }

        _log.Info($"Visit of '{guest?.State.Name ?? guestId}' in '{visit.HostName}''s ship ended ({(string.IsNullOrEmpty(reason) ? "no message" : reason)}).");
    }

    /// <summary>A player leaves the game or dies: as a guest the visit is forgotten (the quit or the respawn moves them —
    /// the visit must not), as a host the guest goes home first.</summary>
    private void EndShipVisitsOf(PlayerSession session)
    {
        string id = session.State.PlayerId;
        _visitAskedAt.Remove(id);
        EndShipVisit(id, reason: null, sendHome: false);
        if (ShipGuestOf(id) is { } guestId)
        {
            EndShipVisit(guestId, "@srv.visit.host_left:" + session.State.Name);
        }
    }

    /// <summary>The docking between <paramref name="by"/> and <paramref name="partner"/> ends: a visit across it ends first.
    /// The host undocking is the host's "send home"; the guest undocking goes home on their own.</summary>
    private void EndShipVisitsOnUndock(string by, string partner)
    {
        if (_shipVisits.TryGetValue(partner, out var hosted) && hosted.HostId == by)
        {
            string byName = FindSessionByPlayerId(by)?.State.Name ?? hosted.HostName;
            EndShipVisit(partner, "@srv.visit.sent_home:" + byName);
        }

        if (_shipVisits.TryGetValue(by, out var visiting) && visiting.HostId == partner)
        {
            EndShipVisit(by, "@srv.visit.undocked", hostNotice: "@srv.visit.guest_left:" + (FindSessionByPlayerId(by)?.State.Name ?? by));
        }
    }

    /// <summary>The host's ship is about to move (a launch, a ship switch, leaving the flight): its guest goes home first.</summary>
    private void SendShipGuestHome(PlayerSession host, string reasonKey)
    {
        if (ShipGuestOf(host.State.PlayerId) is { } guestId)
        {
            EndShipVisit(guestId, reasonKey + ":" + host.State.Name);
        }
    }

    /// <summary>The visited host's parked hull, in the active world — null when the player is no guest.</summary>
    private LandedShip? VisitedShipOf(string guestId)
        => _shipVisits.TryGetValue(guestId, out var visit) ? _worlds.Active.LandedFor(visit.HostId) : null;

    /// <summary>The guest's feet left the host's hull — through its hatch, off its edge, into the void around it. Same rule
    /// as the owner's hatch route (<see cref="SteppedOutOfShipHull"/>), measured on the host's ship.</summary>
    private bool GuestSteppedOutOfHostHull(string guestId, Vector3f pos)
        => VisitedShipOf(guestId) is { Placed: true } hostShip && !WithinShipExtents(hostShip, pos, 0.4f);

    /// <summary>From the world loop: the guest walked out of the host's hull — home after the loop.</summary>
    private void QueueVisitWalkOut(string guestId)
    {
        if (!_visitWalkOuts.Contains(guestId))
        {
            _visitWalkOuts.Add(guestId);
        }
    }

    /// <summary>Runs after the world loop: guests who walked out of the host's hull go home, and once a second every open
    /// visit is checked against the host's ship (<see cref="CheckShipVisits"/>).</summary>
    private void TickShipVisits(double dt)
    {
        if (_visitWalkOuts.Count > 0)
        {
            (_visitWalkOuts, _visitWalkOutsSpare) = (_visitWalkOutsSpare, _visitWalkOuts);
            foreach (string guestId in _visitWalkOutsSpare)
            {
                string name = FindSessionByPlayerId(guestId)?.State.Name ?? guestId;
                EndShipVisit(guestId, "@srv.visit.back_home", hostNotice: "@srv.visit.guest_left:" + name);
            }

            _visitWalkOutsSpare.Clear();
        }

        if (_shipVisits.Count == 0)
        {
            _sinceVisitCheck = 0;
            return;
        }

        _sinceVisitCheck += dt;
        if (_sinceVisitCheck < VisitCheckSeconds)
        {
            return;
        }

        _sinceVisitCheck = 0;
        CheckShipVisits();
    }

    /// <summary>The 1 Hz validity check: the host is still here, still docked, their ship still floats parked in the
    /// guest's flight instance and is still the same ship. Anything else sends the guest home with a line saying why.
    /// Whatever moved the guest somewhere else already just ends the visit.</summary>
    private void CheckShipVisits()
    {
        foreach (var (guestId, visit) in _shipVisits.ToList())
        {
            var guest = FindSessionByPlayerId(guestId);
            if (guest is null || guest.CurrentLocationId != ShipInteriorLoc(visit.HostId))
            {
                // Something moved the guest away already (an admin teleport, say). Nothing to send home; and unless they
                // stand in their own interior, the walkabout record of their parked hull is stale too (#2722).
                EndShipVisit(guestId, reason: null, sendHome: false);
                if (guest is not null && guest.CurrentLocationId != ShipInteriorLoc(guestId))
                {
                    ForgetShipInterior(guestId);
                }

                continue;
            }

            var host = FindSessionByPlayerId(visit.HostId);
            string? reason;
            if (host is null)
            {
                reason = "@srv.visit.host_left:" + visit.HostName;
            }
            else if (!AreDocked(guestId, visit.HostId))
            {
                reason = "@srv.visit.undocked";
            }
            else if (!HostShipPose(host, out var hostInstance, out _, out bool atHelm))
            {
                reason = "@srv.visit.host_left:" + host.State.Name;
            }
            else if (atHelm || hostInstance != visit.InstanceId || host.ActiveShipId != visit.HostShipId)
            {
                reason = "@srv.visit.host_moving:" + host.State.Name;
            }
            else
            {
                reason = null;
            }

            if (reason is not null)
            {
                EndShipVisit(guestId, reason);
            }
        }
    }

    /// <summary>Look only (#2723): what a guest in a friend's ship may not do — build, mine, paint, paste, burn a cell,
    /// open or fill the host's crates, or use a gadget that changes the world. Answered with
    /// <c>@srv.visit.look_only</c> and never handled; walking, doors, sitting, chat and trading pass. Null = allowed.</summary>
    private static string? GuestRefusalAction(object message) => message switch
    {
        MineBlockIntent => "mine",
        PlaceBlockIntent => "place",
        PaintBlockIntent => "paint",
        StructureEditIntent => "structure",
        PasteBuildIntent => "paste",
        ShootBlockIntent => "shoot",
        DeployStationCoreIntent => "station_core",
        LootContainerIntent or DepositContainerIntent or OpenContainerIntent or MoveContainerItemIntent or SetContainerFilterIntent => "container",
        UseGadgetIntent gadget when GadgetChangesTheWorld(gadget.GadgetKey) => "gadget",
        _ => null,
    };

    /// <summary>Gadgets that change the world at the aim point or put something into it — refused for a guest.</summary>
    private static bool GadgetChangesTheWorld(string gadgetKey) => gadgetKey switch
    {
        "terrain_blaster" or "fluid_pump" or "speeder" or "boat" => true,
        RailRules.LinkerItemKey or RailRules.CabItemKey => true,
        "wagon_seats" or "wagon_sleeper" or "wagon_bar" => true,
        _ => false,
    };

    /// <summary>Ways out of the world that do not pass through the guest's own ship (travel, teleports, a ship switch).
    /// Taken from a friend's ship they would leave the guest's parked hull behind with nobody to take its helm — the
    /// guest goes home first. Null = not one of them.</summary>
    private static string? GuestLeaveAction(object message) => message switch
    {
        TravelIntent or HyperjumpSystemIntent or WormholeTransitIntent or LeaveSpaceIntent => "travel",
        BoardStationIntent => "station",
        TeleportToShipIntent or TeleportToPlayerIntent or TeleportToPointIntent or BeamTeleportIntent => "teleport",
        SwitchShipIntent => "switch_ship",
        _ => null,
    };

    /// <summary>Dispatch gate: true when the message from a visiting guest is answered here — refused because look-only
    /// forbids it or because the guest has to go home first, or (the pause menu's "Back to my ship", #2286) taken as the
    /// way home itself.</summary>
    private bool RefusedForGuest(PlayerSession session, object message)
    {
        if (_shipVisits.Count == 0 || !_shipVisits.ContainsKey(session.State.PlayerId))
        {
            return false;
        }

        if (message is ReturnToShipIntent)
        {
            EnterShipInterior(session.State.PlayerId); // "back to my ship" while visiting goes home
            return true;
        }

        if (GuestRefusalAction(message) is { } action)
        {
            Reject(session, action, "@srv.visit.look_only");
            return true;
        }

        if (GuestLeaveAction(message) is { } leave)
        {
            Reject(session, leave, "@srv.visit.go_home_first");
            return true;
        }

        return false;
    }

    private void RejectVisit(PlayerSession session, string reason) => Reject(session, "visit", reason);
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Ship docking (technical requirements / `anf_space_flight.md` §13): two players may dock
/// their ships so they can move between them and get guest access to each other's ship.
/// Docking is gated by <see cref="GameRules.ShipDocking"/> and requires a built
/// <c>docking_module</c>. Dockings are transient (in-memory) and dissolved on undock or
/// disconnect — there is nothing to persist because a docking cannot outlive a session.
/// </summary>
public sealed partial class GameServer
{
    private const string DockingModule = "docking_module";

    /// <summary>How close two players must be for docking (#426 S17). Wider than <c>TradeRange</c> —
    /// the players stand in/near their parked ships, not face to face — but still "same landing site":
    /// docking grants guest access to the partner's ship, so it must not be establishable across the
    /// map or from another world.</summary>
    private const float DockRange = 48f;

    // Outstanding handshake requests: requester id -> target id (one pending request each).
    private readonly Dictionary<string, string> _pendingDock = new();

    // Symmetric active-docking map: _docked[a] == b and _docked[b] == a. A player can hold
    // at most one docking at a time (a single docking port).
    private readonly Dictionary<string, string> _docked = new();

    /// <summary>True while the two players are docked together.</summary>
    public bool AreDocked(string a, string b)
        => _docked.TryGetValue(a, out var partner) && partner == b;

    /// <summary>
    /// True while <paramref name="guestId"/> has guest access to <paramref name="hostId"/>'s
    /// ship — granted for the lifetime of an active docking.
    /// </summary>
    public bool HasGuestAccess(string guestId, string hostId) => AreDocked(guestId, hostId);

    /// <summary>
    /// A player asks to dock with <paramref name="toId"/>. Off rejects; Free docks immediately;
    /// RequestRequired/FriendsOnly record a pending request and notify the target, who must
    /// confirm with <see cref="RespondDock"/>.
    /// </summary>
    public void RequestDock(string fromId, string toId)
    {
        var fromSession = FindSessionByPlayerId(fromId);

        if (Rules.ShipDocking == DockingMode.Off)
        {
            RejectDock(fromSession, "@srv.dock.disabled");
            return;
        }

        if (string.IsNullOrEmpty(toId) || fromId == toId)
        {
            RejectDock(fromSession, "@srv.dock.bad_target");
            return;
        }

        var toSession = FindSessionByPlayerId(toId);
        if (toSession is null)
        {
            RejectDock(fromSession, "@srv.dock.offline");
            return;
        }

        if (!CanDockTogether(fromId, toId))
        {
            RejectDock(fromSession, "@srv.dock.too_far");
            return;
        }

        if (fromSession != null)
        {
            Serve(fromSession); // check the requester's own ship for the docking module
        }

        if (!_ship.HasModule(DockingModule))
        {
            RejectDock(fromSession, "@srv.dock.no_module");
            return;
        }

        if (AreDocked(fromId, toId))
        {
            RejectDock(fromSession, "@srv.dock.already");
            return;
        }

        if (_docked.ContainsKey(fromId))
        {
            RejectDock(fromSession, "@srv.dock.port_busy");
            return;
        }

        if (_docked.ContainsKey(toId))
        {
            RejectDock(fromSession, "@srv.dock.target_port_busy");
            return;
        }

        if (Rules.ShipDocking == DockingMode.Free)
        {
            EstablishDock(fromId, toId);
            return;
        }

        // RequestRequired / FriendsOnly: handshake. FriendsOnly currently behaves like
        // RequestRequired (the target confirms manually) until a friends system exists.
        _pendingDock[fromId] = toId;
        Send(toSession, new DockRequestNotice { Requester = fromId });
    }

    /// <summary>
    /// The request target (<paramref name="toId"/>) accepts or declines a pending request from
    /// <paramref name="fromId"/>. Accepting establishes the docking; declining notifies the requester.
    /// </summary>
    public void RespondDock(string toId, string fromId, bool accept)
    {
        if (!_pendingDock.TryGetValue(fromId, out var target) || target != toId)
        {
            return; // no matching pending request
        }

        _pendingDock.Remove(fromId);

        if (!accept)
        {
            var requester = FindSessionByPlayerId(fromId);
            if (requester is not null)
            {
                Send(requester, new DockStatus { Partner = toId, Docked = false, Reason = "@srv.dock.declined", CanVisit = true });
            }

            return;
        }

        // Re-check at accept time (#426 S17): the requester may have flown to another world (or across
        // the map) between request and accept — the request-time check alone must not be bankable.
        if (!CanDockTogether(fromId, toId))
        {
            var requester = FindSessionByPlayerId(fromId);
            if (requester is not null)
            {
                Send(requester, new DockStatus { Partner = toId, Docked = false, Reason = "@srv.dock.apart", CanVisit = true });
            }

            return;
        }

        EstablishDock(fromId, toId);
    }

    /// <summary>Both players together within <see cref="DockRange"/> (#426 S17, #2711) — see <see cref="PlayersTogether"/>.
    /// <para>#2723: for docking, a player who walks inside their own ship while it floats parked counts as out in that
    /// flight instance, at the parked ship's pose — "step inside, I'm coming over" is exactly when a friend docks to
    /// visit. So a pilot docks with a friend walking their parked ship, and two friends each walking their own floating
    /// ship dock too; one in space (flying, on a spacewalk or walking a parked ship) and one on a surface never do. The
    /// accept re-checks the same way. Trading keeps the plain rule: it needs both on foot in one world.</para></summary>
    private bool CanDockTogether(string a, string b)
    {
        var sa = FindSessionByPlayerId(a);
        var sb = FindSessionByPlayerId(b);
        if (sa is null || sb is null)
        {
            return false;
        }

        bool aInSpace = SpacePositionOf(sa, out string instanceA, out var posA);
        bool bInSpace = SpacePositionOf(sb, out string instanceB, out var posB);
        if (aInSpace || bInSpace)
        {
            return aInSpace && bInSpace && instanceA == instanceB && posA.DistanceSquared(posB) <= DockRange * DockRange;
        }

        return PlayersTogether(a, b, DockRange);
    }

    /// <summary>The one "are these two players together?" check behind docking and trading (#2720): both joined and
    /// within <paramref name="range"/> of each other in the SAME place.
    /// <list type="bullet">
    /// <item>On foot: the same world and their positions there — positions are world-local, so two players on different
    /// planets could otherwise "overlap" numerically and dock or trade across worlds (#426 S17).</item>
    /// <item>In flight: the same space instance and their poses there — the ship, or the suit on a spacewalk (#2711).
    /// <c>State.Position</c> keeps the stale surface spot where the player last stood, so two ships side by side were
    /// refused and two far apart were "together".</item>
    /// <item>One flying and one on foot are never together.</item>
    /// </list></summary>
    private bool PlayersTogether(string a, string b, float range)
    {
        var sa = FindSessionByPlayerId(a);
        var sb = FindSessionByPlayerId(b);
        if (sa is null || sb is null || !sa.Joined || !sb.Joined)
        {
            return false;
        }

        bool aFlying = _playerInstance.TryGetValue(a, out var instanceA);
        bool bFlying = _playerInstance.TryGetValue(b, out var instanceB);
        if (aFlying || bFlying)
        {
            return instanceA is not null
                && instanceA == instanceB
                && _spaceInstances.TryGetValue(instanceA, out var instance)
                && instance.PlayerPoses.TryGetValue(a, out var poseA)
                && instance.PlayerPoses.TryGetValue(b, out var poseB)
                && poseA.Pos.DistanceSquared(poseB.Pos) <= range * range;
        }

        return sa.CurrentLocationId == sb.CurrentLocationId
            && WrapDistSq(sa.State.Position, sb.State.Position) <= range * range;
    }

    /// <summary>Dissolves the player's active docking, if any, and notifies both sides.</summary>
    public void Undock(string id)
    {
        if (!_docked.TryGetValue(id, out var partner))
        {
            return;
        }

        EndShipVisitsOnUndock(id, partner); // #2723: a visit ends with its docking — the guest goes home first
        _docked.Remove(id);
        _docked.Remove(partner);

        NotifyDock(id, partner, docked: false, "@srv.dock.undocked");
        NotifyDock(partner, id, docked: false, "@srv.dock.undocked");
    }

    private void EstablishDock(string a, string b)
    {
        _docked[a] = b;
        _docked[b] = a;
        NotifyDock(a, b, docked: true, "@srv.dock.docked");
        NotifyDock(b, a, docked: true, "@srv.dock.docked");
    }

    /// <summary>Clears any docking and pending requests involving the player (on disconnect).</summary>
    private void ClearDocking(string id)
    {
        Undock(id);
        _pendingDock.Remove(id);

        // Drop pending requests that targeted this player.
        var stale = _pendingDock.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList();
        foreach (var requester in stale)
        {
            _pendingDock.Remove(requester);
        }
    }

    private void NotifyDock(string playerId, string partnerId, bool docked, string reason)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            // #2723: CanVisit tells the client this server lets a docked guest walk over into the host's ship.
            Send(session, new DockStatus { Partner = partnerId, Docked = docked, Reason = reason, CanVisit = true });
        }
    }

    private void RejectDock(PlayerSession? session, string reason)
    {
        if (session is not null)
        {
            Reject(session, "dock", reason);
        }
    }

    /// <summary>The session of a player id — JOINED sessions only (a connection still in its handshake is never
    /// returned), so callers need no <c>Joined</c> check of their own. Null for an empty id or nobody online.</summary>
    private PlayerSession? FindSessionByPlayerId(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return null;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.State.PlayerId == playerId)
            {
                return s;
            }
        }

        return null;
    }

    // ---------------- Intent handlers (dispatched from OnPayload) ----------------

    private void HandleDockRequest(PlayerSession session, DockRequestIntent intent)
        => RequestDock(session.State.PlayerId, intent.TargetPlayer);

    private void HandleDockResponse(PlayerSession session, DockResponseIntent intent)
        => RespondDock(session.State.PlayerId, intent.Requester, intent.Accept);

    private void HandleUndock(PlayerSession session)
        => Undock(session.State.PlayerId);
}

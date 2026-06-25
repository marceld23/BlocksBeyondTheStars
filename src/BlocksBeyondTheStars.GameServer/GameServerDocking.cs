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
            RejectDock(fromSession, "Ship docking is disabled on this server.");
            return;
        }

        if (string.IsNullOrEmpty(toId) || fromId == toId)
        {
            RejectDock(fromSession, "Invalid docking target.");
            return;
        }

        var toSession = FindSessionByPlayerId(toId);
        if (toSession is null)
        {
            RejectDock(fromSession, "Target player is not online.");
            return;
        }

        if (fromSession != null)
        {
            Serve(fromSession); // check the requester's own ship for the docking module
        }

        if (!_ship.HasModule(DockingModule))
        {
            RejectDock(fromSession, "Your ship has no docking module.");
            return;
        }

        if (AreDocked(fromId, toId))
        {
            RejectDock(fromSession, "Already docked with that player.");
            return;
        }

        if (_docked.ContainsKey(fromId))
        {
            RejectDock(fromSession, "Undock first: your docking port is in use.");
            return;
        }

        if (_docked.ContainsKey(toId))
        {
            RejectDock(fromSession, "The target's docking port is already in use.");
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
                Send(requester, new DockStatus { Partner = toId, Docked = false, Reason = "Docking request declined." });
            }

            return;
        }

        EstablishDock(fromId, toId);
    }

    /// <summary>Dissolves the player's active docking, if any, and notifies both sides.</summary>
    public void Undock(string id)
    {
        if (!_docked.TryGetValue(id, out var partner))
        {
            return;
        }

        _docked.Remove(id);
        _docked.Remove(partner);

        NotifyDock(id, partner, docked: false, "Undocked.");
        NotifyDock(partner, id, docked: false, "Undocked.");
    }

    private void EstablishDock(string a, string b)
    {
        _docked[a] = b;
        _docked[b] = a;
        NotifyDock(a, b, docked: true, "Docked.");
        NotifyDock(b, a, docked: true, "Docked.");
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
            Send(session, new DockStatus { Partner = partnerId, Docked = docked, Reason = reason });
        }
    }

    private void RejectDock(PlayerSession? session, string reason)
    {
        if (session is not null)
        {
            Reject(session, "dock", reason);
        }
    }

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

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The two ends of a session: loading the record a joining player plays with, and the teardown of a leaving one.
/// <para>
/// A join loads the record through <see cref="LoadJoiningPlayer"/> — the network join and the local one alike. The network
/// join loads it a second time after the reconnect eviction (#3186): the eviction saves the old session, and the new one
/// must start from that save, not from the record read before it.
/// </para>
/// <para>
/// A leave runs <see cref="LeaveTeardownSteps"/>, removes the session, then runs <see cref="LeaveCleanupSteps"/> (#3197).
/// Every step is guarded on its own: one that throws is logged with a crash report, and the others still run — the save
/// still tries to write what it can, and the session ALWAYS leaves <c>_sessions</c>. A throw used to leave a joined ghost
/// holding the player's name: the rejoin eviction ran the same teardown, threw again, and the player was locked out of
/// their own world until a restart.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One named step of a leaving player's teardown: the server, the leaving session and the body it was on.</summary>
    private readonly record struct LeaveStep(string Name, Action<GameServer, PlayerSession, string> Run);

    /// <summary>What a leaving player was doing ends, then they are saved — all while the session is still registered.</summary>
    private static readonly LeaveStep[] LeaveTeardownSteps =
    {
        // #2723: a quitting guest's visit ends here; a quitting host's guest goes home first.
        new("ship visits", static (g, s, _) => g.EndShipVisitsOf(s)),
        new("docking", static (g, s, _) => g.ClearDocking(s.State.PlayerId)),
        new("space", static (g, s, _) => g.LeaveSpace(s.State.PlayerId)),
        // #1925: NOT LeaveStation — that undocks to the planet (which then got saved) and relaunches the leaver into a space
        // instance LeaveSpace had just cleared. The save keeps them aboard; the rejoin re-boards.
        new("station boarding", static (g, s, _) => g.ForgetStationBoarding(s)),
        new("ship interior", static (g, s, _) => g.ForgetShipInterior(s.State.PlayerId)), // #2722: the walk inside ends too
        new("trades", static (g, s, _) => g.CancelTradesFor(s.State.PlayerId)),
        new("npc conversation", static (g, s, _) => g.EndNpcConversation(s, tellClient: false)), // #2616: back to its day
        new("fleet save", static (g, s, _) => g.SaveLeaverFleet(s)),
        new("player save", static (g, s, _) => g.SaveLeaver(s)),
    };

    /// <summary>After the session is gone: the world, the other players and the admins forget the leaver.</summary>
    private static readonly LeaveStep[] LeaveCleanupSteps =
    {
        new("pending requests", static (g, s, _) => g.ForgetLeaverRequests(s)),
        new("markers", static (g, s, loc) => g.OnMarkerOwnerLeftWorld(s, loc, disconnected: true)), // #1293
        new("world presence", static (g, s, loc) => g.RemoveLeaverFromWorld(s, loc)),
        new("admin rosters", static (g, _, _) => g.RefreshAdminRosters()),
        new("world unload", static (g, _, loc) => g.UnloadLeftWorld(loc)),
    };

    /// <summary>Runs one phase of a leave, each step in its own guard (see the class summary).</summary>
    private void RunLeaveSteps(LeaveStep[] steps, PlayerSession session, string loc)
    {
        foreach (var step in steps)
        {
            try
            {
                step.Run(this, session, loc);
            }
            catch (Exception ex)
            {
                _log.Error($"Disconnect of '{session.State.Name}' (connection {session.ConnectionId}): step '{step.Name}' threw — " +
                           $"the session is torn down anyway: {ex}");
                TryWriteCrashReport("disconnect-fault", step.Name, ex);
            }
        }
    }

    /// <summary>The whole fleet and the fleet index on the state, before the player record is written.</summary>
    private void SaveLeaverFleet(PlayerSession session)
    {
        SetCurrent(session);
        SaveFleet(session);
    }

    /// <summary>The leaver's record, stamped with their "last online" (#2595: the next join's welcome-back reads it).</summary>
    private void SaveLeaver(PlayerSession session)
    {
        session.State.LastSeenUtc = WallUtcNowIso();
        _repo.SavePlayer(session.State);
    }

    /// <summary>Drops the leaver's transient alliance requests and crew invites; online allies and mates see the change.</summary>
    private void ForgetLeaverRequests(PlayerSession session)
    {
        ClearAlliancePending(session.State.PlayerId);
        ClearCrewPending(session.State.PlayerId);
    }

    /// <summary>The leaver's parked ship object and half-built hull leave the world with them (both live on in the fleet
    /// save), their avatar goes, and their landing pad is free again on everyone's map (#1020).</summary>
    private void RemoveLeaverFromWorld(PlayerSession session, string loc)
    {
        SetActiveWorld(loc);
        RemoveLandedShip(session);
        RemoveConstructionSite(session);
        BroadcastToWorld(new PlayerLeft { PlayerId = session.State.PlayerId });
        BroadcastLandingPads();
    }

    /// <summary>The admins' player-mode roster (#1121) loses the leaver.</summary>
    private void RefreshAdminRosters()
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other.State.IsAdmin)
            {
                SendRules(other);
            }
        }
    }

    /// <summary>Drops the body the leaver was on from memory when nobody is left there (its edits are persisted).</summary>
    private void UnloadLeftWorld(string loc)
    {
        if (!string.IsNullOrEmpty(loc) && loc != _meta.ActiveLocationId && !OccupiedLocations().Contains(loc))
        {
            // Move the cursor off the world we're about to drop, back to the (always-resident) default body. Without this
            // the Unload would target the world the disconnect just made Active and silently no-op, leaking that world
            // (and leaving the empty server ticking the orphan).
            SetActiveWorld(_meta.ActiveLocationId);
            _worlds.Unload(loc);
        }
    }

    /// <summary>A joining player's record — or a new player — made ready for play: inventories clamped, the equipment slots
    /// set up, a train bond dropped. Shared by the network join and the local one; throws
    /// <see cref="InvalidDataException"/> for a corrupted record.</summary>
    private PlayerState LoadJoiningPlayer(string name)
    {
        var state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);
        ClampInventory(state.Inventory, $"player '{name}' inventory");
        EnsureEquipmentInitialised(state); // #2110: a pre-slot save's gear moves into the slots once
        state.InTrain = string.Empty;      // #2113: a train bond never survives a join — the player stands where they were
        state.TrainSeat = -1;
        ClampInventory(state.Equipment, $"player '{name}' equipment");
        ClampInventory(state.RationStore, $"player '{name}' ration store");
        ClampInventory(state.SampleCase, $"player '{name}' sample case");
        return state;
    }

    /// <summary>The network join's <see cref="LoadJoiningPlayer"/>: false after a corrupted record, which is kept untouched for
    /// manual recovery while the join is turned away with <c>@srv.join.data_corrupted</c>.</summary>
    private bool TryLoadJoiningPlayer(int connectionId, string name, [NotNullWhen(true)] out PlayerState? state)
    {
        try
        {
            state = LoadJoiningPlayer(name);
            return true;
        }
        catch (InvalidDataException ex)
        {
            _log.Error($"Failed to load player '{name}' (connection {connectionId}): persisted data is corrupted. " +
                       $"The database record was kept untouched for manual recovery. Error: {ex.Message} -> {ex.InnerException?.Message}\n{ex.StackTrace}");
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.data_corrupted" });
            state = null;
            return false;
        }
    }
}

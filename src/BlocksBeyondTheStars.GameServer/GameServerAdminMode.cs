// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Admin mode (#3503, epic #3502): the in-game admin view's way in and out.
/// <list type="bullet">
/// <item><b>Access</b>: a world admin (<c>IsAdmin</c>) or a fleet admin — without cheats (hosted worlds never have any).</item>
/// <item><b>The invisible mode</b> (<see cref="AdminModes.Invisible"/>) IS the observer mode (<see cref="ObserverSystem"/>): no
/// presence, no damage, free flight, no ship or pad, ignored by NPCs and the story. The entry point (body, position,
/// facing) is remembered, and leaving puts the admin back there — admin mode is not a free teleport.</item>
/// <item><b>Official hosted worlds</b> (a join-token instance) keep invisibility for the fleet admin: the world's owner gets
/// the visible variant (<see cref="AdminModes.Visible"/>) — the panel only, no invisibility, god mode, flight or jump.</item>
/// <item>A reconnect drops admin mode (session-only state), and a quit or an autosave in the invisible mode saves the admin
/// at the entry point, never where they hovered.</item>
/// <item>Mining while observing stays possible (removing an offensive build is the in-world moderation lever) but yields
/// nothing and is logged.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>An official hosted world: the instance checks a join token (#3502 decision 1).</summary>
    private bool OfficialHostedWorld => !string.IsNullOrEmpty(_config.JoinTokenSecret);

    /// <summary>Who may open the admin view at all: a world admin or a fleet admin.</summary>
    private static bool MayUseAdminView(PlayerSession session) => session.State.IsAdmin || session.IsFleetAdmin;

    /// <summary>The admin mode this session would get (<see cref="AdminModes"/>): none, the invisible one, or — for a world
    /// owner on an official hosted world who is no fleet admin — the visible panel-only variant.</summary>
    private byte AdminModeAvailableFor(PlayerSession session)
    {
        if (!MayUseAdminView(session))
        {
            return AdminModes.Off;
        }

        return session.IsFleetAdmin || !OfficialHostedWorld ? AdminModes.Invisible : AdminModes.Visible;
    }

    /// <summary>Whether this admin may see observers and the staff's own actions: everywhere except as the world owner of an
    /// official hosted world, who must not see that the fleet admin is watching.</summary>
    private bool SeesObservers(PlayerSession viewer) => viewer.IsFleetAdmin || !OfficialHostedWorld;

    /// <summary>Whether this admin's roster, log and report list carry positions: only for the invisible variant. The visible
    /// variant (an official hosted world's owner — any registered player) learns on which body a player is, never where on
    /// it (child safety, 2026-11-12).</summary>
    private bool SeesPositions(PlayerSession viewer) => AdminModeAvailableFor(viewer) == AdminModes.Invisible;

    /// <summary>How long after a full panel open (roster, the 500-event backlog and the report list at once) another open
    /// only resumes the stream (seconds, real time).</summary>
    private const double AdminPanelOpenCooldown = 2.0;

    private void HandleAdminMode(PlayerSession session, AdminModeIntent intent)
    {
        if (intent.On)
        {
            EnterAdminMode(session);
        }
        else
        {
            LeaveAdminMode(session);
        }
    }

    /// <summary>Enters the admin mode this session may have (see the class summary).</summary>
    private void EnterAdminMode(PlayerSession session)
    {
        byte mode = AdminModeAvailableFor(session);
        if (mode == AdminModes.Off)
        {
            Reject(session, "admin", "@srv.admin.mode_no_access");
            return;
        }

        if (session.Admin.Mode != AdminModes.Off)
        {
            SendPlayerState(session); // already on — a repeated key press only re-syncs the client
            return;
        }

        if (mode == AdminModes.Visible)
        {
            session.Admin.Mode = AdminModes.Visible;
            SendPlayerState(session);
            Send(session, new ServerMessage { Text = "@srv.admin.mode_visible_on" });
            CheatLog(session.State, "opened the admin view (visible)");
            return;
        }

        if (!session.Spectating && AdminModeEntryRefusal(session) is { } refusal)
        {
            Reject(session, "admin", refusal);
            return;
        }

        var p = session.State;
        session.Admin.EntryLocationId = session.CurrentLocationId;
        session.Admin.EntryPosition = p.Position;
        session.Admin.EntryYaw = p.Yaw;
        session.Admin.EntryShipLocationId = session.Ships.TryGetValue(session.ActiveShipId, out var ship) ? ship.CurrentLocationId : string.Empty;
        session.Admin.Mode = AdminModes.Invisible; // before the state goes out: the client reads ADMIN, not SPECTATOR
        if (session.Spectating)
        {
            // A fleet admin's /spectate is already observer mode: the admin mode takes over from here.
            SendPlayerState(session);
            Send(session, new ServerMessage { Text = "@srv.admin.mode_on" });
            CheatLog(p, "entered admin mode");
            return;
        }

        _observerSystem.EnterSpectate(session, "@srv.admin.mode_on", "entered admin mode");
        RefreshAdminRosters(); // #3504: the admins' player-mode roster drops the observer
    }

    /// <summary>Why the invisible admin mode cannot start here, or null. It starts on foot on a body: from the flight view,
    /// a ship, a station, a vehicle, a train or a spacewalk the way back would land somewhere else than it started, and a
    /// body waiting for its respawn choice is not standing anywhere. Nor while something else holds the admin — a duel, an
    /// open trade, a seat, a moving lift, a visit to a friend's ship or a guest aboard their own: becoming invisible and
    /// flying off would leave the other player or the ride in a broken state.</summary>
    private string? AdminModeEntryRefusal(PlayerSession session)
    {
        var p = session.State;
        bool away = InSpace(p.PlayerId) || _inShipInterior.ContainsKey(p.PlayerId) || InStation(p.PlayerId) || p.AboardShip
            || p.InSpeeder.Length > 0 || p.InTrain.Length > 0 || p.InEva;
        if (away || session.Respawn.ChoicePending || p.Health <= 0f)
        {
            return "@srv.admin.mode_on_foot";
        }

        string id = p.PlayerId;
        bool busy = _duelPartner.ContainsKey(id) || ActiveTrade(id) is not null || p.Seated
            || session.MovingFallGraceUntil > _uptime || IsVisitingShip(id) || ShipGuestOf(id) is not null;
        return busy ? "@srv.admin.mode_busy" : null;
    }

    /// <summary>Leaves admin mode: the visible variant just closes; the invisible one puts the admin back at the entry point
    /// (another body is travelled to with the admin bypass, silently) and hands them back to the world as a player. When the
    /// way back is refused, the admin stays in admin mode — with the entry point — and is told so.</summary>
    private void LeaveAdminMode(PlayerSession session)
    {
        byte mode = session.Admin.Mode;
        if (mode == AdminModes.Invisible && !ReturnToAdminEntry(session))
        {
            Send(session, new ServerMessage { Text = "@srv.admin.mode_return_failed" });
            SendPlayerState(session);
            return;
        }

        session.Admin.Mode = AdminModes.Off;
        session.Admin.PanelOpen = false;
        if (mode == AdminModes.Off)
        {
            SendPlayerState(session); // not in admin mode — only re-sync the client
            return;
        }

        if (mode == AdminModes.Visible)
        {
            SendPlayerState(session);
            Send(session, new ServerMessage { Text = "@srv.admin.mode_visible_off" });
            CheatLog(session.State, "closed the admin view");
            return;
        }

        if (session.Spectating)
        {
            _observerSystem.ExitSpectate(session, notice: string.Empty, logLine: "left admin mode"); // the snap said it already
            RefreshAdminRosters(); // #3504: and lists the admin again
        }
        else
        {
            SendPlayerState(session);
        }
    }

    /// <summary>Puts an invisible admin back where admin mode started — the body, the position (or the first free cell above
    /// it, should something have been built there since) and the facing — and the ship back on its body. False when the
    /// travel there was refused: the entry point is then kept, so the admin can try again.</summary>
    private bool ReturnToAdminEntry(PlayerSession session)
    {
        var a = session.Admin;
        string body = a.EntryLocationId;
        if (body.Length == 0)
        {
            return true;
        }

        if (body != session.CurrentLocationId && !_observerSystem.TravelForGoto(session, body))
        {
            _log.Warn($"Admin '{session.State.Name}' could not be taken back to '{body}' — admin mode stays on.");
            return false;
        }

        var spot = a.EntryPosition;
        if (SetActiveWorld(body) && FreeStandingY((int)Math.Floor(spot.X), (int)Math.Floor(spot.Y), (int)Math.Floor(spot.Z), 32) is { } y
            && y != (int)Math.Floor(spot.Y))
        {
            spot = new Vector3f(spot.X, y, spot.Z); // something was built where the admin stood
        }

        session.State.Yaw = a.EntryYaw;
        SnapPlayerTo(session, spot, "@srv.admin.mode_off");
        if (a.EntryShipLocationId.Length > 0 && session.Ships.TryGetValue(session.ActiveShipId, out var ship))
        {
            ship.CurrentLocationId = a.EntryShipLocationId; // an observer's travel moved the ship's body along
        }

        a.EntryLocationId = string.Empty;
        a.EntryShipLocationId = string.Empty;
        return true;
    }

    /// <summary>Runs <paramref name="save"/> with an invisible admin's ship on the body it stood on when admin mode started
    /// (see <see cref="PlayerSessionAdmin.EntryShipLocationId"/>), and puts the live value back afterwards.</summary>
    private static void WithAdminEntryShipBody(PlayerSession session, Action save)
    {
        var a = session.Admin;
        if (a.Mode != AdminModes.Invisible || a.EntryShipLocationId.Length == 0
            || !session.Ships.TryGetValue(session.ActiveShipId, out var ship))
        {
            save();
            return;
        }

        string live = ship.CurrentLocationId;
        ship.CurrentLocationId = a.EntryShipLocationId;
        try
        {
            save();
        }
        finally
        {
            ship.CurrentLocationId = live;
        }
    }

    /// <summary>Writes a player's record — an invisible admin with the entry point in place of where they hover, so neither a
    /// quit nor an autosave (nor a crash after one) turns admin mode into a free teleport.</summary>
    private void SavePlayerRecord(PlayerSession session)
    {
        var a = session.Admin;
        var p = session.State;
        if (a.Mode != AdminModes.Invisible || a.EntryLocationId.Length == 0)
        {
            _repo.SavePlayer(p);
            return;
        }

        string location = p.CurrentLocationId;
        var position = p.Position;
        p.CurrentLocationId = a.EntryLocationId;
        p.Position = a.EntryPosition;
        try
        {
            _repo.SavePlayer(p);
        }
        finally
        {
            p.CurrentLocationId = location;
            p.Position = position;
        }
    }

    /// <summary>The admin panel opened or closed (#3505/#3506): opening needs admin mode and sends the roster, the event
    /// backlog and the report list at once; from then on the server streams them until it closes. A full open at most every
    /// <see cref="AdminPanelOpenCooldown"/> seconds — another one inside it only resumes the stream; closing always passes.</summary>
    private void HandleAdminPanel(PlayerSession session, AdminPanelIntent intent)
    {
        if (!intent.Open)
        {
            session.Admin.PanelOpen = false;
            return;
        }

        if (!MayUseAdminView(session))
        {
            Reject(session, "admin", "@srv.admin.mode_no_access");
            return;
        }

        if (session.Admin.Mode == AdminModes.Off)
        {
            SendPlayerState(session); // out of admin mode: the client's view is stale — re-sync it, stream nothing
            return;
        }

        session.Admin.PanelOpen = true;
        double now = AdminClockSeconds;
        if (now - session.Admin.LastPanelOpenAt < AdminPanelOpenCooldown)
        {
            return; // a burst of opens: the client still holds what the last one sent, the stream carries on from here
        }

        session.Admin.LastPanelOpenAt = now;
        Send(session, BuildAdminRoster(session));
        SendAdminEventBacklog(session);
        SendAdminReportList(session);
    }

    /// <summary>#3503: why an observer may not break this block, or null. A crate that still holds something stays — its
    /// contents would be lost with the scratch pool (<see cref="ObserverBreak"/>), and they are another player's.</summary>
    private string? ObserverMineRefusal(Vector3i pos, BlockDefinition def)
    {
        bool filledCrate = FilledTransportCrateAt(def, pos) is not null
            || _containers.Any(c => c.Kind == "crate" && c.Position.Equals(pos) && c.Items.Any(s => !s.IsEmpty));
        return filledCrate ? "@srv.admin.mine_crate_full" : null;
    }

    /// <summary>#3503: an observer (the invisible admin mode or <c>/spectate</c>) breaks one block — through the normal path,
    /// so the world, the attribution and the broadcast are those of any break, but into a pool nobody holds: no items, no
    /// sample, no area burst, no mission or story progress. Every removal is logged with its place.</summary>
    private void ObserverBreak(PlayerSession session, Vector3i pos, BlockDefinition def)
    {
        var scratch = MaterialPool.Discarding(_content);
        BreakBlockAt(session, pos, def, scratch);
        scratch.TakeLeftovers(); // dropped on purpose: the moderation lever yields nothing

        var p = session.State;
        _log.Info($"[CHEAT] Admin {p.Name} removed '{def.Key}' at {pos.X},{pos.Y},{pos.Z} on {session.CurrentLocationId} (observer, no items).");
        var ev = AdminEventAbout(AdminEventCategories.Admin, "admin.ev.mined", session, null, p.Name, def.Key);
        ev.HasPosition = true;
        ev.X = pos.X + 0.5f;
        ev.Y = pos.Y;
        ev.Z = pos.Z + 0.5f;
        LogAdminEvent(ev, staffOnly: true, writeLog: false); // the [CHEAT] line above is the log line
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: enter (true) or leave admin mode as if the client sent <see cref="AdminModeIntent"/>.</summary>
    internal void SetAdminModeForTest(PlayerSession session, bool on) => HandleAdminMode(session, new AdminModeIntent { On = on });

    /// <summary>Test seam: open or close the admin panel as if the client sent <see cref="AdminPanelIntent"/>.</summary>
    internal void SetAdminPanelForTest(PlayerSession session, bool open) => HandleAdminPanel(session, new AdminPanelIntent { Open = open });

    /// <summary>Test seam: the admin mode a session would get (<see cref="AdminModes"/>).</summary>
    internal byte AdminModeAvailableForTest(PlayerSession session) => AdminModeAvailableFor(session);

    /// <summary>Test seam: writes this player's record the way the autosave does.</summary>
    internal void SavePlayerRecordForTest(PlayerSession session) => SavePlayerRecord(session);
}

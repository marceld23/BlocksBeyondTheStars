// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin event log (#3506): PvP defeats and duel ends (for the whole world, not only the body where they happened),
/// deaths with their cause, joins and leaves, reports, moderation (kick, silence, auto-mute, chat-filter hits) and the admins'
/// own actions (everything <see cref="CheatLog"/> records, admin mode, mining while observing, jumps). The last 500 stay in
/// <see cref="AdminEventLog"/>, each also writes one <c>server.log</c> line. An admin whose panel opens gets the backlog
/// (<see cref="AdminEventBacklog"/>), then every new one live (<see cref="AdminEvent"/>); nobody else ever receives them.
/// Two rows are filtered per viewer: the staff's own actions are hidden from a world owner who may not see observers
/// (<see cref="SeesObservers"/>), and a report about an admin is never shown to that admin. The visible variant of an official
/// hosted world's owner gets no positions (<see cref="SeesPositions"/>). Player-written arguments are literal
/// (<see cref="LiteralArg"/>), and a player's masked chat lines are logged at most every 30 seconds.
/// </summary>
public sealed partial class GameServer
{
    private readonly AdminEventLog _adminEvents = new();

    /// <summary>A new event about <paramref name="actor"/> (and <paramref name="target"/>): the ids and names, and the place of
    /// the target — or of the actor when there is no target. Every argument is literal text (<see cref="LiteralArg"/>); an
    /// argument that IS a locale key (a death cause) is set on <see cref="NetAdminEvent.Args"/> afterwards.</summary>
    private static NetAdminEvent AdminEventAbout(string category, string textKey, PlayerSession? actor, PlayerSession? target,
        params string[] args)
    {
        var ev = new NetAdminEvent { Category = category, TextKey = textKey, Args = args.Select(LiteralArg).ToArray() };
        if (actor is not null)
        {
            ev.ActorId = actor.State.PlayerId;
            ev.ActorName = actor.State.Name ?? string.Empty;
        }

        if (target is not null)
        {
            ev.TargetId = target.State.PlayerId;
            ev.TargetName = target.State.Name ?? string.Empty;
        }

        if ((target ?? actor) is { } place)
        {
            ev.LocationId = place.CurrentLocationId;
            ev.HasPosition = true;
            ev.X = place.State.Position.X;
            ev.Y = place.State.Position.Y;
            ev.Z = place.State.Position.Z;
        }

        return ev;
    }

    /// <summary>A player-written argument as literal text for the client (#3502): a leading <c>@</c> would make the log read
    /// it as a locale key, so it is doubled (<c>@@…</c> reads as <c>@…</c>). The client never fills a placeholder inside an
    /// argument either, so nobody can put a text of their choosing into an admin's log.</summary>
    private static string LiteralArg(string? text)
    {
        string t = text ?? string.Empty;
        return t.StartsWith("@", StringComparison.Ordinal) ? "@" + t : t;
    }

    /// <summary>Records one event: stamps the time, keeps it in the ring buffer, writes the <c>server.log</c> line (control
    /// characters stripped — the arguments may hold a player's text) and sends it live to every admin whose panel is open and
    /// who may see it.</summary>
    private void LogAdminEvent(NetAdminEvent ev, bool staffOnly = false, bool writeLog = true)
    {
        ev.UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entry = _adminEvents.Add(ev, staffOnly);
        if (writeLog)
        {
            _log.Info($"[EVENT] {ev.Category} {ev.TextKey}: {StripControlChars(string.Join(" | ", ev.Args))}" +
                      (ev.LocationId.Length > 0 ? $" @ {StripControlChars(ev.LocationId)}" : string.Empty));
        }

        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined && viewer.Admin.PanelOpen && AdminEventVisibleTo(entry, viewer))
            {
                Send(viewer, new AdminEvent { Event = EventFor(viewer, ev) });
            }
        }
    }

    /// <summary>Whether <paramref name="viewer"/> may see this event (see the class summary). Player ids compare
    /// case-insensitively, like names.</summary>
    private bool AdminEventVisibleTo(AdminEventLog.Entry entry, PlayerSession viewer)
    {
        if (!MayUseAdminView(viewer) || (entry.StaffOnly && !SeesObservers(viewer)))
        {
            return false;
        }

        return entry.Event.Category != AdminEventCategories.Report || !SamePlayer(entry.Event.TargetId, viewer.State.PlayerId);
    }

    /// <summary>The event as <paramref name="viewer"/> receives it: the visible variant of an official hosted world's owner
    /// gets the body but never a position (child safety, <see cref="SeesPositions"/>).</summary>
    private NetAdminEvent EventFor(PlayerSession viewer, NetAdminEvent ev)
    {
        if (SeesPositions(viewer) || !ev.HasPosition)
        {
            return ev;
        }

        return new NetAdminEvent
        {
            Id = ev.Id,
            UnixTime = ev.UnixTime,
            Category = ev.Category,
            ActorId = ev.ActorId,
            ActorName = ev.ActorName,
            TargetId = ev.TargetId,
            TargetName = ev.TargetName,
            LocationId = ev.LocationId,
            TextKey = ev.TextKey,
            Args = ev.Args,
        };
    }

    /// <summary>The backlog for an admin whose panel just opened: every held event this viewer may see, oldest first.</summary>
    private void SendAdminEventBacklog(PlayerSession viewer)
    {
        Send(viewer, new AdminEventBacklog
        {
            Events = _adminEvents.All.Where(e => AdminEventVisibleTo(e, viewer)).Select(e => EventFor(viewer, e.Event)).ToArray(),
        });
    }

    // ---------------- The events themselves ----------------

    /// <summary>A player joined (the network join and the local one).</summary>
    private void AdminEventJoin(PlayerSession session)
        => LogAdminEvent(AdminEventAbout(AdminEventCategories.Join, "admin.ev.join", session, null, session.State.Name ?? "?"));

    /// <summary>The last step of a local join (<c>AddLocalPlayer</c>): its own placement is over (#2465 — from here on, what
    /// happens is the player's doing) and the admin view logs the join, as the network join does.</summary>
    private void EndLocalJoinPlacement(PlayerSession session)
    {
        session.InJoinPlacement = false;
        AdminEventJoin(session);
    }

    /// <summary>A player left (a leave step) — staff-only for an observer, whom nobody else saw arrive either.</summary>
    private void AdminViewOnLeave(PlayerSession session, string location)
    {
        var ev = AdminEventAbout(AdminEventCategories.Join, "admin.ev.leave", session, null, session.State.Name ?? "?");
        ev.LocationId = location;
        LogAdminEvent(ev, staffOnly: session.Spectating);
    }

    /// <summary>A PvP defeat (#2972) — logged for the whole world, wherever it happened.</summary>
    private void AdminEventPvpDefeat(PlayerSession attacker, PlayerSession target)
        => LogAdminEvent(AdminEventAbout(AdminEventCategories.Pvp, "admin.ev.pvp_defeat", attacker, target,
            attacker.State.Name ?? "?", target.State.Name ?? "?"));

    /// <summary>A duel ended: won by <paramref name="winnerName"/>, or without a winner (given up, timed out, a player left).</summary>
    private void AdminEventDuelEnd(string a, string b, string winnerName)
    {
        var sa = FindSessionByPlayerId(a);
        var sb = FindSessionByPlayerId(b);
        string nameA = sa?.State.Name ?? a, nameB = sb?.State.Name ?? b;
        if (winnerName.Length == 0)
        {
            LogAdminEvent(AdminEventAbout(AdminEventCategories.Pvp, "admin.ev.duel_ended", sa, sb, nameA, nameB));
            return;
        }

        bool aWon = string.Equals(winnerName, nameA, StringComparison.Ordinal);
        LogAdminEvent(AdminEventAbout(AdminEventCategories.Pvp, "admin.ev.duel_won", aWon ? sa : sb, aWon ? sb : sa,
            winnerName, aWon ? nameB : nameA));
    }

    /// <summary>The death causes that have their own short label (<c>admin.cause.*</c>) — the suffix of the death line's key
    /// (<c>@srv.death.&lt;cause&gt;</c>). Anything else reads "other".</summary>
    private static readonly HashSet<string> AdminDeathCauses = new(StringComparer.Ordinal)
    {
        "bandit", "boat", "burned", "corrosive_air", "crash", "creature", "critical", "fall", "froze", "gas", "giant", "poison",
        "ressaw", "speeder", "tank_spider", "toxic_water", "wildlife",
    };

    /// <summary>The short cause label for a death line's token: <c>@srv.death.fall</c> → <c>@admin.cause.fall</c>.</summary>
    internal static string AdminDeathCause(string? reason)
    {
        const string prefix = "@srv.death.";
        string r = reason ?? string.Empty;
        int colon = r.IndexOf(':');
        if (colon >= 0)
        {
            r = r.Substring(0, colon);
        }

        string cause = r.StartsWith(prefix, StringComparison.Ordinal) ? r.Substring(prefix.Length) : string.Empty;
        return "@admin.cause." + (AdminDeathCauses.Contains(cause) ? cause : "other");
    }

    /// <summary>A player died (not a PvP defeat — that has its own event), with the cause — a locale key the server chose.</summary>
    private void AdminEventDeath(PlayerSession session, string reason)
    {
        var ev = AdminEventAbout(AdminEventCategories.Death, "admin.ev.death", session, null, session.State.Name ?? "?", string.Empty);
        ev.Args[1] = AdminDeathCause(reason);
        LogAdminEvent(ev, staffOnly: session.Spectating);
    }

    /// <summary>How often a player's masked chat lines reach the log: once per this many seconds (a chatty player whose
    /// every line has a masked word would flood it).</summary>
    private const double MaskedEventIntervalSeconds = 30.0;

    /// <summary>The chat filter masked a line of <paramref name="session"/> — logged at most once per
    /// <see cref="MaskedEventIntervalSeconds"/> per player.</summary>
    private void AdminEventFilterMasked(PlayerSession session)
    {
        double now = AdminClockSeconds;
        if (now - session.Admin.LastMaskedEventAt < MaskedEventIntervalSeconds)
        {
            return;
        }

        session.Admin.LastMaskedEventAt = now;
        AdminEventModeration("admin.ev.filter_masked", null, session);
    }

    /// <summary>A moderation event about a player (<paramref name="textKey"/> with the player's name first).</summary>
    private void AdminEventModeration(string textKey, PlayerSession? actor, PlayerSession target, params string[] more)
    {
        var args = new List<string> { target.State.Name ?? "?" };
        args.AddRange(more);
        LogAdminEvent(AdminEventAbout(AdminEventCategories.Moderation, textKey, actor, target, args.ToArray()));
    }

    /// <summary>What <see cref="CheatLog"/> records, as an admin event at the admin's place — staff-only for a fleet admin
    /// or an observer, whose actions a hosted world's owner does not see.</summary>
    private void AdminEventCheat(PlayerState admin, string message)
    {
        var session = FindSessionByPlayerId(admin.PlayerId);
        var ev = AdminEventAbout(AdminEventCategories.Admin, "admin.ev.cheat", session, null, admin.Name ?? "?", message);
        ev.ActorId = admin.PlayerId;
        ev.ActorName = admin.Name ?? string.Empty;
        bool staffOnly = session is null || session.IsFleetAdmin || session.Spectating;
        LogAdminEvent(ev, staffOnly, writeLog: false); // CheatLog wrote the line
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the held events, oldest first.</summary>
    internal IReadOnlyList<NetAdminEvent> AdminEventsForTest => _adminEvents.All.Select(e => e.Event).ToList();
}

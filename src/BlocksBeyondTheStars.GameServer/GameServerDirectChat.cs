// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The direct chat between an admin and a player (#3507). An admin in admin mode (either variant — never from outside it)
/// may write to any player at any time; a player may only
/// ANSWER an admin who wrote to them — for <see cref="DirectReplyWindowMs"/> after that admin's last line — and there are no
/// player-to-player whispers at all (child safety, #3502 decision 4). One path for both directions: the client's
/// <see cref="DirectChatIntent"/> (the panel, the Reply button) and the chat's <c>/r Text</c> (answers whoever wrote last,
/// intercepted in <c>HandleChat</c>) end in <see cref="SendDirectLine"/>.
/// <para>A player's line goes through everything a chat line does — the throttle, the server mute, the burst limit and the
/// content screening — and is kept as report evidence. An admin's line is throttled too; on an official hosted world the
/// world owner's lines are screened as well (the owner is just some signed-up player there). Every direct line is written
/// to <c>server.log</c>. The line reaches the addressee, the sender and the admins whose panel is open — except an admin either
/// of the two ever reported.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a player may answer an admin after that admin's last line: 30 minutes, on the real-time chat clock.</summary>
    internal const long DirectReplyWindowMs = 30L * 60L * 1000L;

    /// <summary>The longest direct line; longer ones are cut like a chat line.</summary>
    private const int MaxDirectLineLength = 200;

    private void HandleDirectChat(PlayerSession session, DirectChatIntent intent)
        => SendDirectLine(session, intent.ToPlayerId ?? string.Empty, intent.Text ?? string.Empty);

    /// <summary>The two chat commands of the admin view, intercepted in <c>HandleChat</c> before the radio gate: <c>/r Text</c>
    /// (answer in the direct chat) and <c>/admin Text</c> ("Call an admin"). True when <paramref name="text"/> was one.</summary>
    private bool TryHandleAdminChatCommand(PlayerSession session, string text)
    {
        if (ChatCommandArgument(text, "/r") is { } reply)
        {
            if (reply.Length == 0)
            {
                Send(session, new ServerMessage { Text = "@srv.dm.usage_reply" });
                return true;
            }

            HandleReplyCommand(session, reply);
            return true;
        }

        if (ChatCommandArgument(text, "/admin") is { } call)
        {
            HandleCallAdmin(session, call); // the slot clock and the report window are applied there
            return true;
        }

        return false;
    }

    /// <summary>The trimmed argument after <paramref name="verb"/> when <paramref name="text"/> is that command (the verb on
    /// its own or followed by a space, any letter case), else null.</summary>
    private static string? ChatCommandArgument(string text, string verb)
    {
        if (!text.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Length == verb.Length)
        {
            return string.Empty;
        }

        return text[verb.Length] == ' ' ? text.Substring(verb.Length + 1).Trim() : null;
    }

    /// <summary><c>/r Text</c> typed in chat: answers whoever wrote last (an admin, for a player; the player who answered
    /// last, for an admin).</summary>
    private void HandleReplyCommand(PlayerSession session, string text)
    {
        if (session.Admin.ReplyTo.Length == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.dm.nobody_to_answer" });
            return;
        }

        SendDirectLine(session, session.Admin.ReplyTo, text);
    }

    /// <summary>One direct line from <paramref name="from"/> to the player <paramref name="toPlayerId"/> (see the class
    /// summary for the rules).</summary>
    private void SendDirectLine(PlayerSession from, string toPlayerId, string rawText)
    {
        string text = StripControlChars(rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > MaxDirectLineLength)
        {
            text = text.Substring(0, MaxDirectLineLength);
        }

        var to = FindSessionByPlayerId(toPlayerId);
        // #3502: an admin writes as an admin only from the admin view (either variant). Outside it they are a player here —
        // they may answer an admin who wrote to them, but never start a conversation.
        bool fromAdmin = MayUseAdminView(from) && from.Admin.Mode != AdminModes.Off;
        // An invisible admin is "not online" to anybody who may not see observers — except to the player they wrote to.
        bool hidden = to is not null && to.Spectating && !(fromAdmin ? SeesObservers(from) : MayAnswer(from, to));
        if (to is null || !to.Joined || ReferenceEquals(to, from) || hidden)
        {
            Send(from, new ServerMessage { Text = "@srv.dm.not_online" });
            return;
        }

        if (!fromAdmin && !MayAnswer(from, to))
        {
            Send(from, new ServerMessage { Text = MayUseAdminView(from) ? "@srv.dm.admin_view_only" : "@srv.dm.no_conversation" });
            return;
        }

        if (!TakeChatSlot(from) || (!fromAdmin && (ChatMuted(from) || NoteChatLine(from))))
        {
            return; // rate limit, the server mute or the burst limit (each says so itself where it should)
        }

        if (!fromAdmin || (OfficialHostedWorld && !from.IsFleetAdmin))
        {
            if (ScreenChatLine(from, text) is not { } screened)
            {
                return; // blocked — the sender was told, the filter hit is counted and logged
            }

            text = screened;
        }

        DeliverDirectLine(from, to, text, fromAdmin);
    }

    /// <summary>Whether the player <paramref name="player"/> may answer <paramref name="admin"/>: only an admin who wrote to
    /// them within the reply window.</summary>
    private bool MayAnswer(PlayerSession player, PlayerSession admin)
        => MayUseAdminView(admin)
           && player.Admin.AdminLineAtMs.TryGetValue(admin.State.PlayerId, out long at)
           && ChatClockMs - at <= DirectReplyWindowMs;

    /// <summary>Hands a screened line out: the conversation clock and the reply targets, the evidence, the log line, and the
    /// message to the addressee, the sender and every admin whose panel is open (and who may see the two).</summary>
    private void DeliverDirectLine(PlayerSession from, PlayerSession to, string text, bool fromAdmin)
    {
        if (fromAdmin)
        {
            to.Admin.AdminLineAtMs[from.State.PlayerId] = ChatClockMs; // the player may answer for the next 30 minutes
        }
        else
        {
            NoteChatLineForEvidence(from, text); // #1222: a player's answers count as report evidence like chat lines
        }

        to.Admin.ReplyTo = from.State.PlayerId;
        from.Admin.ReplyTo = to.State.PlayerId;
        _log.Info($"Direct chat: '{from.State.Name}' -> '{to.State.Name}'{(fromAdmin ? " (admin)" : string.Empty)}: {text}");

        var line = new DirectChatMessage
        {
            FromId = from.State.PlayerId,
            FromName = from.State.Name ?? string.Empty,
            ToId = to.State.PlayerId,
            ToName = to.State.Name ?? string.Empty,
            Text = text,
            FromAdmin = fromAdmin,
            UnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        bool staff = from.IsFleetAdmin || to.IsFleetAdmin || from.Spectating || to.Spectating;
        foreach (var s in _sessions.Values)
        {
            bool party = ReferenceEquals(s, from) || ReferenceEquals(s, to);
            bool watcher = s.Joined && s.Admin.PanelOpen && MayUseAdminView(s) && (!staff || SeesObservers(s))
                           && !HasReported(from, s) && !HasReported(to, s);
            if (party || watcher)
            {
                Send(s, line);
            }
        }
    }

    /// <summary>Whether <paramref name="reporter"/> filed any report about <paramref name="admin"/>. Such an admin never reads
    /// along in that player's conversations — the other admin may be asking about exactly that report.</summary>
    private bool HasReported(PlayerSession reporter, PlayerSession admin)
    {
        string by = reporter.State.PlayerId, about = admin.State.PlayerId;
        foreach (var r in WorldReports)
        {
            if (SamePlayer(r.ReporterId, by) && SamePlayer(r.TargetId, about))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: a direct line as if the client sent <see cref="DirectChatIntent"/>.</summary>
    internal void DirectChatForTest(PlayerSession from, string toPlayerId, string text)
        => HandleDirectChat(from, new DirectChatIntent { ToPlayerId = toPlayerId, Text = text });
}

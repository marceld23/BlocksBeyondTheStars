// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection chat bookkeeping of a <see cref="PlayerSession"/> (RAM only, never persisted).</summary>
public sealed class PlayerSessionChat
{
    /// <summary>Environment.TickCount of the last accepted chat line (rate limiting).</summary>
    public int LastChatTick { get; set; }

    /// <summary>Whether this session was already told once that a chat line of theirs was masked (#1207) —
    /// the notice is sent on the first masked line only, not on every one.</summary>
    public bool ChatMaskNoticeSent { get; set; }

    // ---- Anti-spam (#1208). All of this lives in RAM only and is never persisted: a temporary auto-mute is a
    // cool-down, not a mark on the record. The sliding windows are per session (a reconnect starts them fresh);
    // the mute deadline itself is NOT on the session — it is keyed by player id on the server (#1294), so a
    // muted player who leaves and rejoins is still muted. Both lists are trimmed to their window on every line,
    // so they hold a handful of entries at most.

    /// <summary>Server uptime (seconds) of the recently accepted chat lines — the burst window.</summary>
    public List<double> RecentChatAt { get; } = new();

    /// <summary>Server uptime (seconds) of the recent content-filter hits (a blocked or masked line).</summary>
    public List<double> RecentFilterHitsAt { get; } = new();

    /// <summary>Whether the sender was already told about the mute currently in force — one notice per mute,
    /// not one per line they keep trying to send. Per session on purpose: a player who rejoins while still
    /// muted is told again on their first blocked line, because the earlier notice is gone with the old client.</summary>
    public bool ChatMuteNoticeSent { get; set; }

    // ---- Report evidence (#1222) ----
    // An arcade guest has no portal account, so their report cannot carry a link to a chat log the operator
    // can open — it has to bring the evidence with it. These lines are RAM only and die with the session;
    // the only copy that ever leaves the process is the excerpt inside a report a human actually filed.

    /// <summary>The most recent chat lines this session sent, oldest first, capped at
    /// <see cref="MaxRecentChatLines"/>. Screened text, i.e. exactly what the other players saw.</summary>
    public List<string> RecentChatLines { get; } = new();

    /// <summary>How many of a player's lines are kept as report evidence.</summary>
    public const int MaxRecentChatLines = 20;

    /// <summary>Server uptime (seconds) of this session's recent player reports — the abuse window for
    /// reporting itself.</summary>
    public List<double> RecentReportsAt { get; } = new();
}

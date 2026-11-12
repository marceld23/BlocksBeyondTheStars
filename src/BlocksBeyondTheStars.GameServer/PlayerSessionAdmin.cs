// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin view's per-session state (epic #3502): the admin mode and its entry point, whether the panel is open, the
/// direct-chat conversations and the roster's idle clock. RAM only and never persisted — a reconnect drops admin mode
/// like observer mode (the safe default), and a player's open conversations end with the session.
/// </summary>
public sealed class PlayerSessionAdmin
{
    /// <summary>The admin mode this session is in (<c>Networking.Messages.AdminModes</c>: 0 off, 1 invisible, 2 visible).</summary>
    public byte Mode { get; set; }

    /// <summary>The admin panel is open: the server streams the roster, the live events and the report list to it.</summary>
    public bool PanelOpen { get; set; }

    /// <summary>Where the invisible admin mode was entered — the body, the position and the facing; leaving puts the admin
    /// back here (#3503: admin mode is not a free teleport). Set only while <see cref="Mode"/> is invisible.</summary>
    public string EntryLocationId { get; set; } = string.Empty;

    public Vector3f EntryPosition { get; set; }

    public float EntryYaw { get; set; }

    /// <summary>The body the admin's ship stood on when the invisible mode started: an observer's travel moves the ship's
    /// body along, so the save and the way back restore this one (a respawn must not land on a body the admin only
    /// watched). Empty when there was no ship.</summary>
    public string EntryShipLocationId { get; set; } = string.Empty;

    /// <summary>#3502 rate limits, on the real-time admin clock (seconds): the last full panel open (roster, backlog and
    /// report list at once), the recent report actions, and the last chat-filter "masked" event of this player.</summary>
    public double LastPanelOpenAt { get; set; } = double.NegativeInfinity;

    public List<double> ReportActionsAt { get; } = new();

    public double LastMaskedEventAt { get; set; } = double.NegativeInfinity;

    /// <summary>#3507: per admin (player id) the chat clock (ms) of their last direct line to THIS player — the player may
    /// answer that admin for 30 minutes after it. Only ever filled on a player's session.</summary>
    public Dictionary<string, long> AdminLineAtMs { get; } = new(System.StringComparer.Ordinal);

    /// <summary>#3507: the player id <c>/r</c> answers — the admin who wrote last (on a player's session), or the player who
    /// answered last (on an admin's session). Empty until a direct line arrived.</summary>
    public string ReplyTo { get; set; } = string.Empty;

    /// <summary>#3505: the position the roster's idle clock last saw, and the real-time second the player last moved.</summary>
    public Vector3f IdleSeenAt { get; set; }

    public double LastMovedAt { get; set; } = -1;
}

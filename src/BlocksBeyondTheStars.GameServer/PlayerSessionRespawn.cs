// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection respawn-choice bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionRespawn
{
    // --- Deferred death respawn (choice between ship and home spawn, issue #462) ---

    /// <summary>Server uptime deadline for a pending respawn choice; 0 = no choice pending. While pending the
    /// player lies at the death spot at 0 HP: the environment tick skips them (no drains, no re-death) and
    /// move intents are ignored. On timeout the ship respawn runs as the safe default.</summary>
    public double RespawnChoiceDeadline { get; set; }

    /// <summary>Death context captured when the choice was offered (consumed on resolution).</summary>
    public bool PendingRespawnSalvaged { get; set; }
    public bool PendingRespawnSameWorld { get; set; }
    public string PendingRespawnReason { get; set; } = string.Empty;

    /// <summary>#2972: set only around the respawn of a player defeated by another player — that respawn keeps every item,
    /// whatever the world's death penalty says.</summary>
    public bool SparedByPvp { get; set; }
}

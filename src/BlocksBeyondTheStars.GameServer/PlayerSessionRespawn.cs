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

    /// <summary>Whether a respawn choice is pending (<see cref="RespawnChoiceDeadline"/> set): the player lies dead at the
    /// death spot, so nothing may kill them again (#3188) — the choice and the timeout both clear the deadline first.</summary>
    public bool ChoicePending => RespawnChoiceDeadline > 0;

    /// <summary>Death context captured when the choice was offered (consumed on resolution).</summary>
    public bool PendingRespawnSalvaged { get; set; }
    public bool PendingRespawnSameWorld { get; set; }
    public string PendingRespawnReason { get; set; } = string.Empty;

    /// <summary>#2972: set only around the respawn of a player defeated by another player — that respawn keeps every item,
    /// whatever the world's death penalty says.</summary>
    public bool SparedByPvp { get; set; }

    // --- The fallen (#3765, DeathPenalty.Revive — GameServerFallen.cs) ---

    /// <summary>The player died on a Hard Survival world and watches until revived. Connection-scoped.</summary>
    public bool Fallen { get; set; }

    /// <summary>Where the player fell — where a medkit brings them back, and where they then stand up.</summary>
    public BlocksBeyondTheStars.Shared.Geometry.Vector3f FallenAt { get; set; }

    /// <summary>The uptime of the fall (the alone-timer counts from it), and the death's context for a restart at the ship.</summary>
    public double FallenSince { get; set; }
    public string FallenReason { get; set; } = string.Empty;
    public bool FallenSalvaged { get; set; }

    /// <summary>The hunger the player fell with. The watching body is kept topped up like any invulnerable one, so the
    /// revive reads this instead — otherwise everybody would stand up full.</summary>
    public float FallenHunger { get; set; }

    /// <summary>Every player had fallen at some moment: this one may start again at the ship at once (it stays so).</summary>
    public bool FallenRestartNow { get; set; }

    /// <summary>Set around the restart at the ship of a player who had fallen: that wake-up is no second death (no
    /// death flash, no "you have died" gate — they have watched long enough).</summary>
    public bool FallenRestarting { get; set; }

    /// <summary>The <c>RestartInSeconds</c> the client was last told (NaN: nothing yet).</summary>
    public float FallenToldRestartIn { get; set; } = float.NaN;
}

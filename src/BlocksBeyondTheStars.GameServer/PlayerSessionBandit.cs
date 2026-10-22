// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection bandit hold-up bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionBandit
{
    // --- Bandit hold-up (a robber demands part of the inventory; comply or fight) ---

    /// <summary>Id of the pending bandit demand (0 = none). The client's answer must echo it, so a stale
    /// or spoofed response can never resolve a different hold-up.</summary>
    public int BanditDemandId { get; set; }

    /// <summary>Combat-entity id of the demanding bandit (ground) or bandit ship (space).</summary>
    public string BanditDemandBanditId { get; set; } = string.Empty;

    /// <summary>Server uptime deadline; silence past it counts as a refusal (respawn-choice pattern).</summary>
    public double BanditDemandDeadline { get; set; }

    /// <summary>The demanded items, captured when the demand was made (validated again on comply).</summary>
    public List<ItemAmount> BanditDemandItems { get; } = new();

    /// <summary>True when the pending demand came from a bandit ship in space (routes the response).</summary>
    public bool BanditDemandFromShip { get; set; }

    /// <summary>Uptime before which no lone bandit stalks this player again (long per-player cooldown, so
    /// hold-ups stay rare events rather than a farm).</summary>
    public double NextBanditAmbushAt { get; set; }

    /// <summary>Worlds VEGA already flagged as bandit country to this player this session (the pre-briefing
    /// fires once per world, BEFORE any bandit walks up).</summary>
    public HashSet<string> BanditBriefedWorlds { get; } = new();
}

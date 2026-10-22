// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection rate-limit bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionThrottles
{
    /// <summary>#2672: token bucket shared by every appearance edit that is saved and relayed (the face, each body-paint
    /// part, a tool look), refilled on the server's uptime clock — starts full, so the join burst (face + four parts)
    /// passes; <see cref="AppearanceRefilledAt"/> is the uptime of the last refill.</summary>
    public double AppearanceBudget { get; set; } = GameServer.AppearanceBurst;
    public double AppearanceRefilledAt { get; set; }

    /// <summary>Server uptime (seconds) before which the next voice frame is throttled.</summary>
    public double NextVoiceFrameAt { get; set; }

    /// <summary>#2158: token bucket for relayed cosmetic action effects (<c>FxIntent</c>), refilled on the server's
    /// uptime clock — starts full; <see cref="FxRefilledAt"/> is the uptime of the last refill.</summary>
    public double FxBudget { get; set; } = GameServer.FxRelayBurst;
    public double FxRefilledAt { get; set; }

    /// <summary>Server uptime (seconds) before which the next accepted block paint is throttled (#817) —
    /// same 2 s pacing as the face: each accepted paint can mean a disk save + world-wide rebroadcast.</summary>
    public double NextPaintAt { get; set; }
    public double NextCustomShapeAt { get; set; }

    /// <summary>Earliest uptime the next world texture is accepted (#1958) — the same 2 s pacing: a disk write
    /// and a broadcast to every client.</summary>
    public double NextWorldTextureAt { get; set; }

    /// <summary>The pages of the world texture list still to send to this client (#1958) — one per tick.</summary>
    public System.Collections.Generic.Queue<BlocksBeyondTheStars.Networking.Messages.WorldTextureList> WorldTexturePages { get; } = new();

    /// <summary>Earliest uptime the next blueprint paste is accepted (#1117) — a paste is up to 4096
    /// placements in one intent, so it gets a real cooldown.</summary>
    public double NextBlueprintPasteAt { get; set; }

    /// <summary>Server uptime (seconds) before which the "backpack full" toast is suppressed (#600). Area
    /// mining can overflow on every block of a burst; one warning per few seconds says it just as well.</summary>
    public double NextInventoryFullHintAt { get; set; }

    /// <summary>Loot banked from asteroids / salvage drops that has not been announced yet (#1317): item key +
    /// how much went to the backpack and to the cargo hold. Flushed as ONE "+n … → hold" toast per cooldown.</summary>
    public List<(string Item, int ToBackpack, int ToCargo)> PendingLootToast { get; } = new();

    /// <summary>Server uptime (seconds) before which the next loot toast is held back (#1317).</summary>
    public double NextLootToastAt { get; set; }

    /// <summary>Token bucket for the per-connection message-rate gate (anti-flood). Refilled by wall clock.</summary>
    public double MsgBudget { get; set; } = 60.0;
    public int LastMsgRefillTick { get; set; }
}

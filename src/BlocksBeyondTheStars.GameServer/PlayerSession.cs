// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection server-side player session bookkeeping.</summary>
public sealed class PlayerSession
{
    public int ConnectionId { get; }
    public PlayerState State { get; }

    /// <summary>True once the player has completed the join handshake.</summary>
    public bool Joined { get; set; }

    /// <summary>The player's UI language ("en"/"de") sent on join (item 15). Server-authored dynamic text — LLM
    /// NPC greetings — is generated in this language. Connection-scoped (not persisted); defaults to English.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>The celestial-body id of the world this player is currently in. With multi-world, each
    /// player can be on a different body; the server sets <c>_worlds.Active</c> to this before handling the
    /// player's messages / streaming their chunks. Empty until the join places them. Mirrors
    /// <see cref="State"/>.<c>CurrentLocationId</c> so it is persisted (restored to the last body on load).</summary>
    public string CurrentLocationId
    {
        get => State.CurrentLocationId;
        set => State.CurrentLocationId = value;
    }

    /// <summary>The fixed landing pad this player currently holds on their body (item 38), or -1 if none. Pads
    /// are communal + occupancy is live: a pad counts as taken only while its holder is on the body (not in
    /// space). Set when landing; superseded on the next landing; ignored once the player is in space/elsewhere.</summary>
    public int AssignedPadIndex { get; set; } = -1;

    /// <summary>Fleet admin: the operator of this hosting installation, as opposed to the owner of one world
    /// (issue #487). Granted on join from <see cref="Shared.Configuration.ServerConfig.FleetAdminPlayers"/> and
    /// deliberately <b>never persisted</b> — see that property for why a saved role would leak between worlds.
    /// The only gate for the invisible observer mode.</summary>
    public bool IsFleetAdmin { get; set; }

    /// <summary>Observer mode (issue #487): this player is invisible to everyone — no presence, no avatar, no
    /// nameplate — leaves no footprint in the world (no parked ship, no landing pad, no fauna) and is ignored by
    /// creatures. Session-scoped, never persisted: a crash or reconnect always drops back to normal play, which
    /// is the safe default. Only ever set for <see cref="IsFleetAdmin"/> sessions.</summary>
    public bool Spectating { get; set; }

    /// <summary>Environment.TickCount of the last accepted chat line (rate limiting).</summary>
    public int LastChatTick { get; set; }

    /// <summary>Server uptime (seconds) before which the next face change / voice frame is throttled.</summary>
    public double NextFaceChangeAt { get; set; }
    public double NextVoiceFrameAt { get; set; }

    /// <summary>Server uptime (seconds) before which the "backpack full" toast is suppressed (#600). Area
    /// mining can overflow on every block of a burst; one warning per few seconds says it just as well.</summary>
    public double NextInventoryFullHintAt { get; set; }

    /// <summary>Token bucket for the per-connection message-rate gate (anti-flood). Refilled by wall clock.</summary>
    public double MsgBudget { get; set; } = 60.0;
    public int LastMsgRefillTick { get; set; }

    /// <summary>Chunks already streamed to this client, to avoid resending.</summary>
    public HashSet<ChunkCoord> SentChunks { get; } = new();

    /// <summary>The client's requested render distance in chunks (from its JoinRequest), or 0 if it didn't say.
    /// When set, it drives this player's streaming radius (clamped server-side) instead of the host's config —
    /// so the in-game View Distance slider extends the actually-streamed terrain on dedicated servers too, not
    /// only the local fog. 0 = fall back to <see cref="ServerConfig.ViewDistanceChunks"/>.</summary>
    public int ViewDistance { get; set; }

    /// <summary>Short rolling history of the player's recent state (for /bump diagnostics).</summary>
    public List<BumpSample> History { get; } = new();
    public double SinceHistorySample;

    // --- Per-player ship fleet (P4: one ship per player, no crew) ---
    // Each player owns their own ships and one is active (flown + stamped into their world). The server
    // serves a player by pointing its ship cursor at this fleet. Empty until the join sets it up.

    /// <summary>This player's owned ships, keyed by ship id.</summary>
    public Dictionary<string, ShipState> Ships { get; } = new();

    /// <summary>The id of this player's active ship (the one flown + stamped).</summary>
    public string ActiveShipId { get; set; } = string.Empty;

    // Avatar colours (packed 0xRRGGBB) relayed to other players. Sensible defaults until set.
    public int SkinColor { get; set; } = 0xD9AE8C;
    public int TorsoColor { get; set; } = 0x3372CC;
    public int ArmColor { get; set; } = 0x3372CC;
    public int LegColor { get; set; } = 0x40404F;

    /// <summary>Ship hull colour (packed 0xRRGGBB), relayed so other players see this player's ship tinted
    /// (item 32). Default = the steel tint the ship hull used before hull colours existed.</summary>
    public int HullColor { get; set; } = 0xD1D6E0;

    // --- Ship AI companion "VEGA" session bookkeeping (persisted progress lives in State.Milestones) ---

    /// <summary>Blocks mined toward the onboarding "mine" stage (session-scoped; the target is tiny).</summary>
    public int VegaMineCount { get; set; }

    /// <summary>Accumulator for the 1 Hz advisor poll.</summary>
    public double VegaAdvisorAccum { get; set; }

    /// <summary>Uptime gates pacing memory-fragment redemption and the space callouts.</summary>
    public double VegaMemoryReadyAt { get; set; }
    public double VegaThreatReadyAt { get; set; }
    public double VegaEvadeReadyAt { get; set; }

    /// <summary>Uptime of the next LLM banter check (0 = not armed yet; armed on the first poll).</summary>
    public double VegaBanterNextAt { get; set; }

    // --- Deferred death respawn (choice between ship and home spawn, issue #462) ---

    /// <summary>Server uptime deadline for a pending respawn choice; 0 = no choice pending. While pending the
    /// player lies at the death spot at 0 HP: the environment tick skips them (no drains, no re-death) and
    /// move intents are ignored. On timeout the ship respawn runs as the safe default.</summary>
    public double RespawnChoiceDeadline { get; set; }

    /// <summary>Death context captured when the choice was offered (consumed on resolution).</summary>
    public bool PendingRespawnSalvaged { get; set; }
    public bool PendingRespawnSameWorld { get; set; }
    public string PendingRespawnReason { get; set; } = string.Empty;

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

    // --- Heal-tank regen field (base/station life support, issue #460) ---

    /// <summary>Countdown to the next heal-tank proximity rescan (the regen itself applies every tick).</summary>
    public double HealTankScanIn { get; set; }

    /// <summary>Cached result of the last heal-tank proximity scan.</summary>
    public bool NearHealTank { get; set; }

    // --- Periodic vitals sync (HUD bars froze between event-driven sends before) ---
    public double VitalsSyncTimer { get; set; }
    public float LastSentHealth = 100f;
    public float LastSentOxygen = 100f;
    public float LastSentEnergy = 100f;
    public float LastSentHunger = 100f;

    public PlayerSession(int connectionId, PlayerState state)
    {
        ConnectionId = connectionId;
        State = state;
    }
}

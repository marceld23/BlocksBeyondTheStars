// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection NPC bookkeeping of a <see cref="PlayerSession"/> (RAM only, never persisted).</summary>
public sealed class PlayerSessionNpc
{
    /// <summary>Uptime before which no NPC radio call is made to this player (#1119; global ≤1/10 min
    /// cadence, armed with a quiet period on join so a rejoin never opens with a ringing radio).</summary>
    public double NpcCallReadyAt { get; set; }

    /// <summary>Per call key (e.g. "camp:12:(«campKey»)"): uptime before which the same call never repeats.</summary>
    public Dictionary<string, double> NpcCallCooldownUntil { get; } = new();

    /// <summary>Uptime of the next NPC radio-call trigger scan for this player (#1119).</summary>
    public double NpcCallScanAt { get; set; }

    /// <summary>Camps this player was CALLED about (camp key → the caller), so clearing one pays the
    /// promised friendship bonus (#1158). Session-scoped — the courtesy fades with a rejoin.</summary>
    public Dictionary<string, (string NpcKey, string Name, string Place)> CalledCamps { get; } = new();

    /// <summary>Per "«dialogKey»|«npcKey»": uptime before which a repeatable dialogue pays no relationship
    /// reward again (#1162 — spam-talking must not farm standing).</summary>
    public Dictionary<string, double> DialogRewardCooldownUntil { get; } = new();

    /// <summary>Dialogue keys the player declined this session whose choices hand out a mission (#1212) — the
    /// favour is not asked again until the next visit, so "not today" lets the ordinary smalltalk through.</summary>
    public HashSet<string> DeclinedMissionDialogs { get; } = new(StringComparer.Ordinal);

    /// <summary>Villager person keys whose passing hello already paid its +1 standing this session (#2598).</summary>
    public HashSet<string> HelloStandingPaid { get; } = new(StringComparer.Ordinal);

    /// <summary>#2616: the NPC conversation this player is in — it exists only while the client shows reply choices, and
    /// ends on a leave, an end node, a new talk, walking away, a world change, the NPC leaving the world or a disconnect
    /// (the session goes with it). RAM only; see <c>GameServer.NpcConversationValid</c> for the one validity rule.</summary>
    internal GameServer.NpcConversation? Conversation { get; set; }

    // --- #2601: personal wishes. RAM only. ---

    /// <summary>Villagers (person keys) whose wish the player answered "maybe later" this session — the ordinary chat
    /// until the next visit; the "!" stays.</summary>
    public HashSet<string> DeclinedWishGivers { get; } = new(StringComparer.Ordinal);

    /// <summary>Held wish id → the progress the villager last reminded the player of ("3 of 5 — no hurry!"); the same
    /// progress again means an ordinary chat.</summary>
    public Dictionary<string, int> WishReminded { get; } = new(StringComparer.Ordinal);

    /// <summary>The held wishes that were ready to hand over at the last 1 Hz check (the "?" markers).</summary>
    public HashSet<string> WishReady { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The settlement (location key) the player stood in at the last wish check — entering another refreshes
    /// the markers and warms the offers' AI wording ("" = none).</summary>
    public string WishPlaceKey { get; set; } = string.Empty;

    /// <summary>Whether a festival was running in the settlement the player stood in at the last wish check — its start
    /// and end re-send the markers (no new "!" while everybody celebrates).</summary>
    public bool WishFestivalHere { get; set; }

    /// <summary>#2593: uptime before which no NPC greets this player in passing again (4 s, 8 s in a crowd).</summary>
    public double PassingGreetReadyAt { get; set; }

    /// <summary>The reporter who just asked this player for an interview (2026-09), 0 = none — an answer is accepted only
    /// for them, once.</summary>
    public int PendingInterviewNpcId { get; set; }

    /// <summary>The in-game days on which a streamer already asked this player for a photo, per streamer place (2026-09).</summary>
    public HashSet<string> StreamerAskedToday { get; } = new(StringComparer.Ordinal);
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Server → client, per receiver (#1118): the relationship stage of the live NPCs THIS player has
/// a standing with (parallel arrays, stage as a locale key like <c>npc.stage.known</c>). Deliberately not
/// part of the 0.2 s NPC position broadcast — standings change rarely (trade/mission/dialog), positions
/// constantly. NPCs absent here read as strangers.</summary>
public sealed class NpcStandingList
{
    public int[] NpcIds { get; set; } = System.Array.Empty<int>();
    public string[] StageKeys { get; set; } = System.Array.Empty<string>();
}

/// <summary>Client → server (#1118): the Character tab's "People you know" list wants a fresh roster.</summary>
public sealed class RequestKnownNpcsIntent
{
}

/// <summary>One remembered acquaintance for the roster (#1118), straight from the player's persisted NPC
/// memory: coined name, role + stage as locale keys, where they live, and the raw score (sort order).</summary>
public sealed class NetKnownNpc
{
    public string Name { get; set; } = string.Empty;
    public string RoleKey { get; set; } = string.Empty;
    public string StageKey { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public int Standing { get; set; }
}

/// <summary>Server → client (#1118): everyone this player has a standing with, friendliest first.</summary>
public sealed class KnownNpcList
{
    public NetKnownNpc[] People { get; set; } = System.Array.Empty<NetKnownNpc>();
}

/// <summary>Client → server (#1119): the player's NPC radio-call preference — 0 All · 1 MissionsOnly ·
/// 2 Off. Stored in the save (the SERVER initiates calls, so a client-only mute could not silence them).</summary>
public sealed class SetNpcCallsIntent
{
    public int Mode { get; set; }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The data of "the village lives" (epic #2589), from <c>data/village_life.json</c>: the presents a villager friend
/// hands over (#2598), the little conversations villagers have with each other and the village festival. Optional
/// content — a data folder without the file (or without a section) simply has none of that part; every section is
/// optional on its own.
/// </summary>
public sealed class VillageLifeDefinition
{
    /// <summary>Conversation topics villagers talk about among themselves (C1), in authored order.</summary>
    public List<VillageChatterTopic> Chatter { get; set; } = new();

    /// <summary>The pool a villager friend picks a present from (#2598, C3) — one row per present, small and
    /// kid-friendly (berries, a fruit, a lantern …). Empty → friends give nothing.</summary>
    public List<ItemAmount> FriendGifts { get; set; } = new();

    /// <summary>The village festival (C6): the quartermaster's present and the cheer lines.</summary>
    public VillageFestivalDefinition Festival { get; set; } = new();
}

/// <summary>One conversation topic two villagers may pick (C1): when it fits, what it needs, how likely it is and the
/// lines the two speakers alternate (locale keys).</summary>
public sealed class VillageChatterTopic
{
    public string Key { get; set; } = string.Empty;

    /// <summary>"day", "evening" or "any".</summary>
    public string When { get; set; } = "any";

    /// <summary>What must be true in the world for the topic (e.g. "weather_wet", "trader"); "" = always.</summary>
    public string Needs { get; set; } = string.Empty;

    public int Weight { get; set; } = 1;

    /// <summary>The lines, speaker A first, then alternating (locale keys).</summary>
    public List<string> Lines { get; set; } = new();
}

/// <summary>The village festival's data (C6).</summary>
public sealed class VillageFestivalDefinition
{
    /// <summary>What the quartermaster hands the hero when the festival starts.</summary>
    public List<ItemAmount> Gift { get; set; } = new();

    /// <summary>The cheer lines villagers call out during the festival (locale keys).</summary>
    public List<string> Cheers { get; set; } = new();
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One achievement: a counter to watch, how far it has to go, and what the player gets for it. Asked for by a
/// player in exactly these terms — <i>"Ich möchte, dass es Erfolge gibt wie 'Baue 5 Eisen ab' und dafür gibt's
/// eine Belohnung."</i>
/// <para>
/// Data-driven (<c>data/achievements.json</c>) so the list can grow without code, and deliberately built on
/// plain counters: the server bumps a named counter when something happens, and any achievement watching that
/// counter advances. Adding a new achievement over an existing counter needs no server change at all.
/// </para>
/// </summary>
public sealed class AchievementDefinition
{
    /// <summary>Stable id. Names/descriptions are looked up as <c>achv.&lt;key&gt;.name</c> and
    /// <c>achv.&lt;key&gt;.desc</c>, so text lives in the locale files like everything else.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The counter this watches — see <c>AchievementCounters</c> for the names the server bumps
    /// (e.g. <c>mine:iron_ore</c>, <c>mine:any</c>, <c>craft:any</c>, <c>build:any</c>, <c>visit:body</c>).</summary>
    public string Counter { get; set; } = string.Empty;

    /// <summary>How high the counter must get. Values below 1 are treated as 1.</summary>
    public int Target { get; set; } = 1;

    /// <summary>Grouping for the achievements panel (e.g. "mining", "building"); free-form, shown via
    /// <c>achv.category.&lt;category&gt;</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>What the player is handed on unlock. May be empty — some achievements are just a pat on the
    /// back. Rewards are ordinary items, never new mechanics.</summary>
    public List<ItemAmount> Rewards { get; set; } = new();
}

/// <summary>
/// The counter names the server bumps. Kept as constants so a typo in server code is a compile error, while the
/// data files stay free to reference any counter (an achievement watching an unknown counter simply never
/// advances, which is a harmless authoring mistake rather than a crash).
/// </summary>
public static class AchievementCounters
{
    /// <summary>Any block mined, whatever it was.</summary>
    public const string MineAny = "mine:any";

    /// <summary>A specific block mined — <c>mine:&lt;blockKey&gt;</c>, e.g. <c>mine:iron_ore</c>.</summary>
    public static string Mine(string blockKey) => "mine:" + blockKey;

    /// <summary>Any block placed.</summary>
    public const string BuildAny = "build:any";

    /// <summary>Any successful craft.</summary>
    public const string CraftAny = "craft:any";

    /// <summary>A specific recipe crafted — <c>craft:&lt;recipeKey&gt;</c>.</summary>
    public static string Craft(string recipeKey) => "craft:" + recipeKey;

    /// <summary>Arriving on a body the player had never been to before.</summary>
    public const string VisitBody = "visit:body";

    /// <summary>A creature or raider defeated.</summary>
    public const string Defeat = "defeat:any";

    // --- Late-game counters (#1102): the mid/late-game goals hang off these. Every one is bumped at an
    // existing gameplay event; none of them is a new mechanic.

    /// <summary>A first-time scan of anything (block, flora, creature, micro-fauna, monument, asteroid).</summary>
    public const string ScanAny = "scan:any";

    /// <summary>A first-time monument (rune) scan.</summary>
    public const string ScanMonument = "scan:monument";

    /// <summary>A blueprint researched at the cockpit.</summary>
    public const string ResearchAny = "research:any";

    /// <summary>A player-built space station commissioned.</summary>
    public const string StationCommissioned = "station:commissioned";

    /// <summary>A player-built ship commissioned (keel → flight-worthy).</summary>
    public const string ShipCommissioned = "ship:commissioned";

    /// <summary>A wild creature tamed into a companion.</summary>
    public const string TameAny = "tame:any";

    /// <summary>A world container (wreck salvage, ruin cache, drop capsule …) looted empty.</summary>
    public const string LootAny = "loot:any";

    /// <summary>A star system entered for the first time (landed in it or arrived by hyperjump).</summary>
    public const string VisitSystem = "visit:system";

    /// <summary>A board / bounty mission turned in.</summary>
    public const string MissionCompleted = "mission:completed";

    /// <summary>A hyperjump to another star system.</summary>
    public const string Hyperjump = "hyperjump:any";

    /// <summary>The Guardian finale won (every player who was aboard for it).</summary>
    public const string StoryFinale = "story:finale";

    /// <summary>A player station converted into an SPS relay (#1125) — counted for the completing contributor.</summary>
    public const string RelayCommissioned = "relay:commissioned";
}

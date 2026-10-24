// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>
/// One kind of personal wish a villager may have (#2601), from <c>data/wishes.json</c>: what they would like (an item to
/// bring, an item to show, an animal to scan), how many, who may wish it, when the world can actually grant it and the
/// small present that says thank you. The server coins a concrete wish per player from these templates
/// (<c>GameServerWishes</c>); the texts live in the locale tables as <c>wish.&lt;key&gt;.ask</c> and
/// <c>wish.&lt;key&gt;.thanks</c> (English and German mandatory). Optional content: no file → no wishes.
/// </summary>
public sealed class WishTemplate
{
    /// <summary>"Bring me" — the items are handed over (a <see cref="MissionObjectiveType.Deliver"/> objective).</summary>
    public const string KindDeliver = "deliver";

    /// <summary>"Show me" — the player only has to have the items and keeps them (<see cref="MissionObjectiveType.Collect"/>).</summary>
    public const string KindShow = "show";

    /// <summary>"Check on the animals" — scans with the scanner (<see cref="MissionObjectiveType.Scan"/>, a
    /// <see cref="ScanTargets"/> expression as <see cref="Item"/>).</summary>
    public const string KindScan = "scan";

    /// <summary>No world condition.</summary>
    public const string RequiresNothing = "";

    /// <summary>A plant growing on this world drops <see cref="Item"/> — and a poisonous species does not count when the
    /// item has a poisonous twin (toxic berries are no present).</summary>
    public const string RequiresFloraDrop = "flora_drop";

    /// <summary>The world grows trees (wood).</summary>
    public const string RequiresTrees = "trees";

    /// <summary>The world has mycelium ground, where the giant mushrooms grow.</summary>
    public const string RequiresMycelium = "mycelium";

    /// <summary>The world has animals to scan.</summary>
    public const string RequiresCreatures = "creatures";

    /// <summary>The highest <see cref="Tier"/> a template may have.</summary>
    public const int MaxTier = 3;

    /// <summary>The villager jobs that have wishes at all ("" = a plain settler).</summary>
    public static readonly string[] EligibleJobs = { string.Empty, "gardener", "craftsman", "innkeeper" };

    /// <summary>The world conditions a template may name in <see cref="Requires"/>.</summary>
    public static readonly string[] KnownRequirements = { RequiresNothing, RequiresFloraDrop, RequiresTrees, RequiresMycelium, RequiresCreatures };

    /// <summary>Unique key; the locale keys are <c>wish.&lt;key&gt;.ask</c> and <c>wish.&lt;key&gt;.thanks</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary><see cref="KindDeliver"/>, <see cref="KindShow"/> or <see cref="KindScan"/>.</summary>
    public string Kind { get; set; } = KindDeliver;

    /// <summary>The item key (deliver / show) or the scan target expression (scan, e.g. <c>creature:any</c>).</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>#2958: a deliver / show wish that takes any member of this item group (<c>logs</c>: firewood of any tree),
    /// <see cref="Item"/> being its representative. Empty for exactly <see cref="Item"/>.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>How many — the coined wish varies it by up to ±<see cref="Jitter"/> (never below 1).</summary>
    public int Count { get; set; } = 1;

    /// <summary>The most the count of a coined wish differs from <see cref="Count"/> (0 = always exactly Count).</summary>
    public int Jitter { get; set; }

    /// <summary>The villager jobs that may have this wish (see <see cref="EligibleJobs"/>); empty = every eligible villager.</summary>
    public List<string> Jobs { get; set; } = new();

    /// <summary>The world condition (one of <see cref="KnownRequirements"/>) — a wish the world cannot grant is never asked.</summary>
    public string Requires { get; set; } = RequiresNothing;

    /// <summary>1 = can be a first wish; a template of tier T is asked once the player has fulfilled at least T−1 wishes in
    /// that lane of the place (glass needs a furnace — not the first thing a newcomer has). Counted per lane so that one
    /// lane's progress never changes the other lane's open offer.</summary>
    public int Tier { get; set; } = 1;

    /// <summary>The thank-you present (may be empty when <see cref="Knowledge"/> is set).</summary>
    public List<ItemAmount> Gift { get; set; } = new();

    /// <summary>Knowledge points the thank-you brings besides the present (0 = none).</summary>
    public int Knowledge { get; set; }

    /// <summary>A few English words on why the villager wishes it ("wants to bake a berry pie") — the AI re-wording's
    /// topic hint. Never shown to the player.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The locale key of the villager's request.</summary>
    public string AskKey => "wish." + Key + ".ask";

    /// <summary>The locale key of the villager's thanks.</summary>
    public string ThanksKey => "wish." + Key + ".thanks";

    /// <summary>The mission objective a coined wish of this template carries.</summary>
    public MissionObjectiveType ObjectiveType => Kind switch
    {
        KindShow => MissionObjectiveType.Collect,
        KindScan => MissionObjectiveType.Scan,
        _ => MissionObjectiveType.Deliver,
    };
}

/// <summary>
/// A player's wishes in one place (#2601), persisted in <c>PlayerState.Wishes</c> under the place's location key
/// (<c>settle_&lt;hash&gt;</c>): one entry per lane (a hamlet has one, a larger village two) and how many wishes were
/// fulfilled there in all. The open wish of a lane is coined deterministically from the world seed, the place, the
/// player and the lane's counter, so only the counters and the givers are stored.
/// </summary>
public sealed class WishLedger
{
    /// <summary>The lanes, index = lane.</summary>
    public List<WishLane> Lanes { get; set; } = new();

    /// <summary>Wishes fulfilled in this place (all lanes) — a tally; the tiers go by each lane's own counter.</summary>
    public int Done { get; set; }
}

/// <summary>One lane of a <see cref="WishLedger"/>: a villager's wish at a time.</summary>
public sealed class WishLane
{
    /// <summary>How many wishes of this lane were fulfilled — the counter of the lane's current wish.</summary>
    public int N { get; set; }

    /// <summary>The person key of the villager whose wish the player accepted and still holds ("" = none held).</summary>
    public string Giver { get; set; } = string.Empty;

    /// <summary>The person key of the villager whose wish of this lane was fulfilled last — the next one moves on to
    /// somebody else.</summary>
    public string LastGiver { get; set; } = string.Empty;

    /// <summary>The template of the lane's last fulfilled wish — the next one asks for something else.</summary>
    public string LastTemplate { get; set; } = string.Empty;

    /// <summary>#2714: the name of the village the held wish is handed over in (the giver's home) — the Missions tab, the
    /// toasts and the map marker name it. "" = no wish held, or one held before #2714 that the server has not seen its
    /// village again for yet (it fills this in the next time the player is on that body).</summary>
    public string Village { get; set; } = string.Empty;

    /// <summary>#2714: the body (location id) <see cref="Village"/> lies on — the map marker shows only there.</summary>
    public string VillageLocationId { get; set; } = string.Empty;

    /// <summary>#2714: the centre of <see cref="Village"/> in world blocks, where its map marker sits.</summary>
    public float VillageX { get; set; }

    /// <summary>#2714: see <see cref="VillageX"/>.</summary>
    public float VillageY { get; set; }

    /// <summary>#2714: see <see cref="VillageX"/>.</summary>
    public float VillageZ { get; set; }
}

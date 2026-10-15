// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One profession of the wilderness folk (#2640/#2641, <c>data/lodges.json</c>): the people who live in a lodge — a
/// small hut out in the open land — and work around it. A row says what the resident carries, which market goods they
/// trade, how their work goes (<see cref="Work"/>), which yard their hut has (<see cref="Yard"/>) and how strongly a
/// planet calls for them (<see cref="Weights"/> over the planet traits in <see cref="LodgeRules.Traits"/>).
/// <para>A resident is a <c>vendor</c> with this <see cref="Key"/> as their job and <see cref="Theme"/> as their market
/// theme — the profession pattern of <see cref="NpcProfessions"/>, so the trade-or-talk prompt, the vendor screen and the
/// memory keys work unchanged. The locale keys follow the job: <c>npc.role.&lt;key&gt;</c>, <c>npc.greet.&lt;key&gt;</c>,
/// <c>npc.activity.&lt;activity&gt;</c>, <c>ui.vendor.theme.&lt;theme&gt;</c>.</para>
/// </summary>
public sealed class LodgeProfessionDefinition
{
    /// <summary>The job key ("gatherer", "miner", "woodcutter", "hunter").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>What the resident carries — a render hint for the client ("npc_basket", "npc_pickaxe", "npc_axe",
    /// "npc_bow").</summary>
    public string Held { get; set; } = string.Empty;

    /// <summary>The market theme they trade (<c>marketTheme</c> in recipes.json).</summary>
    public string Theme { get; set; } = string.Empty;

    /// <summary>The nameplate activity while working (<c>npc.activity.&lt;activity&gt;</c>).</summary>
    public string Activity { get; set; } = string.Empty;

    /// <summary>How the work goes: one of <see cref="LodgeRules.WorkKinds"/> (gather, mine, chop, hunt).</summary>
    public string Work { get; set; } = string.Empty;

    /// <summary>The yard the lodge of this profession gets: one of <see cref="LodgeRules.YardKinds"/>.</summary>
    public string Yard { get; set; } = string.Empty;

    /// <summary>The profession of a second resident (a lodge with two beds), "" = the same as the first. It never needs a
    /// yard of its own (never a <c>mine</c> profession): it works the wild land around the hut.</summary>
    public string Partner { get; set; } = string.Empty;

    /// <summary>How much this world calls for the profession: a weighted sum over the planet traits
    /// (<see cref="LodgeRules.Traits"/>, each 0..1) — huntable fauna, flora, tier-1 ores, trees.</summary>
    public Dictionary<string, double> Weights { get; set; } = new();

    /// <summary>The nameplate role key.</summary>
    public string NameKey => "npc.role." + Key;

    /// <summary>The nameplate activity key while at work.</summary>
    public string ActivityKey => "npc.activity." + Activity;
}

/// <summary>The vocabulary of <see cref="LodgeProfessionDefinition"/> and the numbers of the lodges (#2640).</summary>
public static class LodgeRules
{
    /// <summary>Picks wild and planted plants (<c>NpcHarvestFlora</c>).</summary>
    public const string WorkGather = "gather";

    /// <summary>Mines the lodge's quarry face (<c>NpcMineCell</c>, the cells fill up again).</summary>
    public const string WorkMine = "mine";

    /// <summary>Fells a tree and plants a sapling (<c>NpcFellTree</c>).</summary>
    public const string WorkChop = "chop";

    /// <summary>Hunts a common wild animal (<c>NpcShootCreature</c>).</summary>
    public const string WorkHunt = "hunt";

    /// <summary>Every work kind.</summary>
    public static readonly IReadOnlyList<string> WorkKinds = new[] { WorkGather, WorkMine, WorkChop, WorkHunt };

    /// <summary>A small fenced crop plot.</summary>
    public const string YardPlot = "plot";

    /// <summary>A rock face of the planet's own tier-1 ores in stone.</summary>
    public const string YardQuarry = "quarry";

    /// <summary>A woodpile beside two or three trees.</summary>
    public const string YardWoodpile = "woodpile";

    /// <summary>A drying frame.</summary>
    public const string YardDryingFrame = "drying_frame";

    /// <summary>Every yard kind.</summary>
    public static readonly IReadOnlyList<string> YardKinds = new[] { YardPlot, YardQuarry, YardWoodpile, YardDryingFrame };

    /// <summary>Planet trait: huntable fauna (common passive or skittish land animals).</summary>
    public const string TraitFauna = "fauna";

    /// <summary>Planet trait: surface flora.</summary>
    public const string TraitFlora = "flora";

    /// <summary>Planet trait: the planet's own tier-1 ores.</summary>
    public const string TraitOres = "ores";

    /// <summary>Planet trait: trees.</summary>
    public const string TraitTrees = "trees";

    /// <summary>Every planet trait a weight may name.</summary>
    public static readonly IReadOnlyList<string> Traits = new[] { TraitFauna, TraitFlora, TraitOres, TraitTrees };

    /// <summary>The most lodges one world carries.</summary>
    public const int HardCap = 6;

    /// <summary>Whether a work kind is known.</summary>
    public static bool IsWorkKind(string? work) => work != null && Contains(WorkKinds, work);

    /// <summary>Whether a yard kind is known.</summary>
    public static bool IsYardKind(string? yard) => yard != null && Contains(YardKinds, yard);

    /// <summary>Whether a trait is known.</summary>
    public static bool IsTrait(string? trait) => trait != null && Contains(Traits, trait);

    /// <summary>A profession's weight on a world with these traits (each 0..1): the weighted sum, never negative.</summary>
    public static double WeightFor(LodgeProfessionDefinition profession, IReadOnlyDictionary<string, double> traits)
    {
        double w = 0.0;
        foreach (var kv in profession.Weights)
        {
            if (traits.TryGetValue(kv.Key, out double t))
            {
                w += Math.Max(0.0, kv.Value) * Math.Clamp(t, 0.0, 1.0);
            }
        }

        return w;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        foreach (var s in list)
        {
            if (string.Equals(s, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

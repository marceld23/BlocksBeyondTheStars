// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A base's focus (#3036, Justus 2026-10-30: "a base can only have one upgrade, which can be changed — the upgrades make the
/// base different"): one specialisation per base core, switched for free at the core screen (Marcel). The server keeps it
/// with the base's yield store; nothing here touches the world.
/// <list type="bullet">
/// <item><see cref="None"/> — the base as before: its core gathers the body's common ores (#2999). No travelling merchants
/// come (#3036: "bases should not spawn traders right away").</item>
/// <item><see cref="Market"/> (#3039) — merchants visit, more often, sometimes two who trade with each other; trader ships
/// count the base as a reason to land.</item>
/// <item><see cref="Miner"/> (#3037) — the core gathers by how deep the players dug below it: the deeper, the more, and the
/// rarer ores join once their depth is reached (<see cref="MinerRateFactor"/>, <see cref="MinerReaches"/>).</item>
/// <item><see cref="Lumberjack"/> (#3038) — a resident fells trees around the base and the core gathers wood
/// (<see cref="LumberjackYield"/>).</item>
/// <item><see cref="Radio"/> (#3040) — the base hears trader signals across its system and now and then an old distress
/// call that leads to a wreck.</item>
/// </list>
/// </summary>
public static class BaseFocus
{
    public const string None = "";
    public const string Market = "market";
    public const string Miner = "miner";
    public const string Lumberjack = "lumberjack";
    public const string Radio = "radio";

    /// <summary>Every focus in the order the core screen offers them.</summary>
    public static readonly string[] All = { None, Market, Miner, Lumberjack, Radio };

    /// <summary>A known focus as itself; anything else (an older save, a typo from a modified client) as <see cref="None"/>.</summary>
    public static string Normalize(string? focus)
    {
        foreach (string f in All)
        {
            if (string.Equals(f, focus, System.StringComparison.Ordinal))
            {
                return f;
            }
        }

        return None;
    }

    /// <summary>The locale key of a focus's name (<c>ui.base.focus.none</c> …).</summary>
    public static string NameKey(string focus) => "ui.base.focus." + (string.IsNullOrEmpty(focus) ? "none" : focus);

    /// <summary>The locale key of a focus's one-line explanation on the core screen.</summary>
    public static string DescKey(string focus) => NameKey(focus) + "_desc";

    // ---------------- Miner (#3037) ----------------

    /// <summary>The deepest a Miner base looks below its core for the players' shaft.</summary>
    public const int MinerMaxDepth = 160;

    /// <summary>Every this many blocks of depth add the base rate once more …</summary>
    public const int MinerDepthPerStep = 32;

    /// <summary>… up to this multiple of the base rate.</summary>
    public const double MinerRateCap = 3.0;

    /// <summary>The Miner's rate multiple at <paramref name="depth"/> blocks below the core: 1 at the surface, +1 per
    /// <see cref="MinerDepthPerStep"/>, at most <see cref="MinerRateCap"/>.</summary>
    public static double MinerRateFactor(int depth)
        => System.Math.Min(MinerRateCap, 1.0 + (System.Math.Max(0, depth) / (double)MinerDepthPerStep));

    /// <summary>Whether a vein that starts <paramref name="veinMinDepth"/> below the surface is reached by a shaft
    /// <paramref name="depth"/> deep. The common veins (depth 0–4) are always reached.</summary>
    public static bool MinerReaches(int depth, int veinMinDepth) => veinMinDepth <= System.Math.Max(4, depth);

    // ---------------- Lumberjack (#3038) ----------------

    /// <summary>How far around the core (blocks) the base's woodcutter fells trees — outside every base zone.</summary>
    public const int LumberjackRadius = 32;

    /// <summary>What a Lumberjack base's core gathers on a body with trees: logs above all, some fibre, now and then a
    /// sapling to plant.</summary>
    public static readonly KeyValuePair<string, int>[] LumberjackYield =
    {
        new("wood_log", 8),
        new("plant_fiber", 2),
        new("sapling", 1),
    };

    // ---------------- Market (#3039) ----------------

    /// <summary>The guest roll's merchant weight at a Market base (the neighbour weighs 50, the traveller 20).</summary>
    public const int MarketMerchantWeight = 120;

    /// <summary>A Market base's quiet spell between two visits, as a share of the normal one.</summary>
    public const double MarketVisitSpellFactor = 0.5;

    /// <summary>How often a merchant visiting a Market base brings a second one along (a market day: they trade with each other).</summary>
    public const double MarketPairChance = 0.5;

    // ---------------- Radio centre (#3040) ----------------

    /// <summary>Seconds between two old distress calls a Radio base passes to its owner (while they are on its body).</summary>
    public const double RadioDistressGapSeconds = 1200.0;
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The one place that decides whether a held tool may break a block (#1686). Server and client both need
/// the rule — the server to accept or reject a swing, the client to say what a block wants BEFORE the swing
/// and to aim the fluid cursor — and it used to live as two hand-copied twins that could drift apart.
/// </summary>
public static class MiningRules
{
    /// <summary>
    /// Whether <paramref name="tool"/> may mine <paramref name="block"/>: the right KIND first (a drill is
    /// no machete), then the tier. A block with <see cref="ToolKind.None"/> takes any tool, bare hands
    /// included; tier 0 likewise. Mineability itself is the caller's check — a wall you may not break at all
    /// gets its own message.
    /// </summary>
    public static bool ToolCanMine(ToolProperties tool, BlockDefinition block)
    {
        if (block.RequiredTool != ToolKind.None && tool.Kind != block.RequiredTool)
        {
            return false;
        }

        return tool.Tier >= block.MinToolTier;
    }

    /// <summary>
    /// The cheapest tool in the content set that WOULD break <paramref name="block"/>, or null when nothing
    /// does. "Cheapest" = the lowest tier that clears the gate, then the lowest mining power, then the item
    /// key — so a tier-2 wall names the Titanium Drill rather than whichever tier-3 laser happens to sort
    /// first, and a dead heat resolves the same way on every machine instead of following dictionary order
    /// (this is player-facing text; it must not flip between runs). The hint names a concrete tool on
    /// purpose: "a drill of tier 2" tells a new player nothing they can act on.
    /// </summary>
    public static ItemDefinition? CheapestToolFor(GameContent content, BlockDefinition block)
    {
        ItemDefinition? best = null;
        foreach (var item in content.Items.Values)
        {
            if (item.Tool is not { } tool || !ToolCanMine(tool, block))
            {
                continue;
            }

            if (best is null || Cheaper(item, tool, best))
            {
                best = item;
            }
        }

        return best;

        static bool Cheaper(ItemDefinition item, ToolProperties tool, ItemDefinition best)
        {
            var incumbent = best.Tool!;
            if (tool.Tier != incumbent.Tier)
            {
                return tool.Tier < incumbent.Tier;
            }

            if (tool.MiningPower != incumbent.MiningPower)
            {
                return tool.MiningPower < incumbent.MiningPower;
            }

            return string.CompareOrdinal(item.Key, best.Key) < 0;
        }
    }
}

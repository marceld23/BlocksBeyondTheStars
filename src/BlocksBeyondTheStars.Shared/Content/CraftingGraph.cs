// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// The crafting-depth graph behind the craft-list ordering (#826): 0 = raw resource, otherwise 1 + the deepest input of
/// the shallowest producing recipe; market (barter) trades are excluded. Built once by <see cref="GameContent"/> from its
/// recipe table.
/// </summary>
public sealed class CraftingGraph
{
    private readonly Dictionary<string, int> _craftDepth = new(StringComparer.Ordinal);

    public CraftingGraph(IEnumerable<RecipeDefinition> recipes)
    {
        ComputeCraftDepths(recipes);
    }

    /// <summary>
    /// How many crafting steps deep an item sits: 0 = raw resource (nothing produces it), otherwise
    /// 1 + the deepest input of its shallowest producing recipe. Market (barter) trades are excluded —
    /// they are exchanges, not production, and could form cycles. Computed once at load so the client's
    /// craft-list ordering (#826) is deterministic across sessions.
    /// </summary>
    public int CraftDepth(string itemKey) => _craftDepth.TryGetValue(itemKey, out var d) ? d : 0;

    /// <summary>The deepest <see cref="CraftDepth"/> across a recipe/build cost (0 for an empty cost).</summary>
    public int MaxInputDepth(IEnumerable<ItemAmount> cost)
    {
        int max = 0;
        foreach (var c in cost)
        {
            max = Math.Max(max, CraftDepth(c.Item));
        }

        return max;
    }

    private void ComputeCraftDepths(IEnumerable<RecipeDefinition> recipes)
    {
        var byOutput = new Dictionary<string, List<RecipeDefinition>>(StringComparer.Ordinal);
        foreach (var r in recipes)
        {
            if (r.Station == CraftingStation.Market)
            {
                continue;
            }

            foreach (var o in r.Outputs)
            {
                if (!byOutput.TryGetValue(o.Item, out var list))
                {
                    byOutput[o.Item] = list = new List<RecipeDefinition>();
                }

                list.Add(r);
            }
        }

        // Depth-first with a visiting set as cycle guard: an input currently being resolved counts as
        // raw (depth 0), which keeps the result finite and deterministic even if recipes ever loop.
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        int Depth(string item)
        {
            if (_craftDepth.TryGetValue(item, out var known))
            {
                return known;
            }

            if (!byOutput.TryGetValue(item, out var producers) || !visiting.Add(item))
            {
                return 0;
            }

            int best = int.MaxValue;
            foreach (var r in producers)
            {
                int deepest = 0;
                foreach (var input in r.Inputs)
                {
                    deepest = Math.Max(deepest, Depth(input.Item));
                }

                best = Math.Min(best, 1 + deepest);
            }

            visiting.Remove(item);
            _craftDepth[item] = best;
            return best;
        }

        foreach (var item in byOutput.Keys)
        {
            Depth(item);
        }
    }
}

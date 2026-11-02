// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3112 "Needed for": what an item is used in — the reverse of a recipe's ingredient list. The recipes that take it as
/// an ingredient (a group ingredient counts every member: any log feeds "logs", #2958), the blueprints whose research
/// costs it, and the ship modules and ships that take it as build cost. Market barter deals are left out (they trade
/// almost anything and would bury the real uses). Read from the content, so new recipes show up by themselves.
/// </summary>
public sealed class ItemUses
{
    private ItemUses(IReadOnlyList<RecipeDefinition> recipes, IReadOnlyList<string> products, IReadOnlyList<string> blueprints,
        IReadOnlyList<string> modules, IReadOnlyList<string> ships)
    {
        Recipes = recipes;
        Products = products;
        Blueprints = blueprints;
        Modules = modules;
        Ships = ships;
    }

    /// <summary>The non-market recipes that take the item as an ingredient, in recipe order.</summary>
    public IReadOnlyList<RecipeDefinition> Recipes { get; }

    /// <summary>What those recipes make, each item once, in recipe order (the factory twin of a bench recipe adds nothing).</summary>
    public IReadOnlyList<string> Products { get; }

    /// <summary>Blueprint keys whose research cost names the item.</summary>
    public IReadOnlyList<string> Blueprints { get; }

    /// <summary>Ship module keys whose build cost names the item.</summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>Ship keys whose construction cost names the item.</summary>
    public IReadOnlyList<string> Ships { get; }

    /// <summary>True when the item is used in nothing.</summary>
    public bool IsEmpty => Recipes.Count == 0 && Blueprints.Count == 0 && Modules.Count == 0 && Ships.Count == 0;

    /// <summary>The uses of <paramref name="itemKey"/> (its base key — a tuned tool counts as its plain item).</summary>
    public static ItemUses For(GameContent content, string itemKey)
    {
        string item = ItemKey.Base(itemKey ?? string.Empty);
        string group = content.ItemGroupOf(item);
        bool Needs(IEnumerable<ItemAmount> cost) => cost.Any(c => c.Item == item
                                                                  || (!string.IsNullOrEmpty(group) && c.Group == group));

        var recipes = content.Recipes.Values
            .Where(r => r.Station != CraftingStation.Market && Needs(r.Inputs))
            .ToList();
        var products = recipes.SelectMany(r => r.Outputs).Select(o => o.Item).Where(o => o != item).Distinct().ToList();
        var blueprints = content.Blueprints.Values.Where(b => Needs(b.UnlockCost)).Select(b => b.Key).ToList();
        var modules = content.ShipModules.Values.Where(m => Needs(m.BuildCost)).Select(m => m.Key).ToList();
        var ships = content.Ships.Values.Where(s => Needs(s.CraftCost)).Select(s => s.Key).ToList();
        return new ItemUses(recipes, products, blueprints, modules, ships);
    }
}

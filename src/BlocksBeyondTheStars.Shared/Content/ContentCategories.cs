// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3102: which build categories (<c>data/categories.json</c>) an item, a blueprint, a ship module or a ship belongs to —
/// several at once where that helps finding it. A category takes its explicit lists plus its named rules:
/// <list type="bullet">
/// <item><c>weapon</c> — items whose tool kind is a weapon; <c>vehicle</c> — items that deploy a vehicle;
/// <c>suitGear</c> — what <see cref="SuitEquipment.IsSuitGear"/> calls suit gear; <c>consumable</c> — consumables;
/// <c>materials</c> — materials and components that are not suit gear;</item>
/// <item><c>fromBlueprints</c> — every item a (non-market) recipe makes behind one of the category's own blueprints (the
/// whole Crystal Net from its 18 nodes);</item>
/// <item><c>allModules</c> / <c>allShips</c> — every ship module / every ship.</item>
/// </list>
/// An item no list and no rule names lands in the categories whose <see cref="CategoryDefinition.FallbackFor"/> holds its
/// item category, and a blueprint no list names inherits the categories of what it unlocks — so content added later is
/// findable without touching the file. Every list is in the file's category order. Built once per content
/// (<see cref="GameContent.Categories"/>); presentation only, no server rule reads it.
/// </summary>
public sealed class ContentCategories
{
    private static readonly IReadOnlyList<string> None = System.Array.Empty<string>();

    /// <summary>The rule names <see cref="CategoryDefinition.Derive"/> may use.</summary>
    public static readonly IReadOnlyList<string> RuleNames = new[]
    {
        "weapon", "vehicle", "suitGear", "consumable", "materials", "fromBlueprints", "allModules", "allShips",
    };

    /// <summary>No categories (no <c>data/categories.json</c>): every list shows only "All".</summary>
    public static readonly ContentCategories Empty = new(new List<CategoryGroupDefinition>(), new List<CategoryDefinition>());

    private readonly Dictionary<string, CategoryDefinition> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _order = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _blueprints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _ships = new(StringComparer.Ordinal);

    private ContentCategories(List<CategoryGroupDefinition> groups, List<CategoryDefinition> categories)
    {
        Groups = groups;
        Categories = categories;
        for (int i = 0; i < categories.Count; i++)
        {
            _byKey[categories[i].Key] = categories[i];
            _order[categories[i].Key] = i;
        }
    }

    /// <summary>The sidebar headings, in display order.</summary>
    public IReadOnlyList<CategoryGroupDefinition> Groups { get; }

    /// <summary>The categories, in display order.</summary>
    public IReadOnlyList<CategoryDefinition> Categories { get; }

    /// <summary>True when there are no categories at all.</summary>
    public bool IsEmpty => Categories.Count == 0;

    /// <summary>The category with this key, or null.</summary>
    public CategoryDefinition? Find(string? key)
        => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key!, out var c) ? c : null;

    /// <summary>The display position of a category (int.MaxValue for an unknown key) — for ordering by category.</summary>
    public int OrderOf(string? key) => !string.IsNullOrEmpty(key) && _order.TryGetValue(key!, out int i) ? i : int.MaxValue;

    /// <summary>The categories of an item (its base key — a tuned tool or a shaped block counts as its plain item).</summary>
    public IReadOnlyList<string> OfItem(string? itemKey)
        => string.IsNullOrEmpty(itemKey) ? None : Lookup(_items, ItemKey.Base(itemKey!));

    public IReadOnlyList<string> OfBlueprint(string? key) => Lookup(_blueprints, key);

    public IReadOnlyList<string> OfModule(string? key) => Lookup(_modules, key);

    public IReadOnlyList<string> OfShip(string? key) => Lookup(_ships, key);

    private static IReadOnlyList<string> Lookup(Dictionary<string, List<string>> map, string? key)
        => !string.IsNullOrEmpty(key) && map.TryGetValue(key!, out var list) ? list : None;

    /// <summary>Resolves every membership of <paramref name="catalog"/> against <paramref name="content"/>. Unknown keys in
    /// the file are skipped (the content tests name them); a null catalog gives <see cref="Empty"/>.</summary>
    public static ContentCategories Build(CategoryCatalog? catalog, GameContent content)
    {
        if (catalog is null)
        {
            return Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var categories = (catalog.Categories ?? new List<CategoryDefinition>())
            .Where(c => c is not null && !string.IsNullOrEmpty(c.Key) && seen.Add(c.Key))
            .ToList();
        var groups = (catalog.Groups ?? new List<CategoryGroupDefinition>())
            .Where(g => g is not null && !string.IsNullOrEmpty(g.Key))
            .ToList();
        var result = new ContentCategories(groups, categories);
        result.ResolveExplicit(content);
        result.ResolveRules(content);
        result.ResolveFallbacks(content);
        result.Sort();
        return result;
    }

    private void ResolveExplicit(GameContent content)
    {
        foreach (var c in Categories)
        {
            AddAll(_items, c.Items, c.Key, k => content.GetItem(k) is not null);
            AddAll(_blueprints, c.Blueprints, c.Key, k => content.GetBlueprint(k) is not null);
            AddAll(_modules, c.Modules, c.Key, k => content.GetShipModule(k) is not null);
            AddAll(_ships, c.Ships, c.Key, k => content.GetShip(k) is not null);
        }
    }

    private void ResolveRules(GameContent content)
    {
        foreach (var c in Categories)
        {
            var rules = new HashSet<string>(c.Derive ?? new List<string>(), StringComparer.Ordinal);
            if (rules.Count == 0)
            {
                continue;
            }

            foreach (var item in content.Items.Values)
            {
                if (ItemRuleMatches(rules, item))
                {
                    Add(_items, item.Key, c.Key);
                }
            }

            if (rules.Contains("fromBlueprints"))
            {
                var own = new HashSet<string>(c.Blueprints ?? new List<string>(), StringComparer.Ordinal);
                foreach (var item in ItemsUnlockedBy(content, own))
                {
                    Add(_items, item, c.Key);
                }
            }

            if (rules.Contains("allModules"))
            {
                foreach (var m in content.ShipModules.Keys)
                {
                    Add(_modules, m, c.Key);
                }
            }

            if (rules.Contains("allShips"))
            {
                foreach (var s in content.Ships.Keys)
                {
                    Add(_ships, s, c.Key);
                }
            }
        }
    }

    private static bool ItemRuleMatches(HashSet<string> rules, ItemDefinition item)
    {
        bool suitGear = SuitEquipment.IsSuitGear(item);
        return (rules.Contains("weapon") && item.Tool?.Kind == ToolKind.Weapon)
               || (rules.Contains("vehicle") && item.Vehicle is not null)
               || (rules.Contains("suitGear") && suitGear)
               || (rules.Contains("consumable") && item.Category == ItemCategory.Consumable)
               || (rules.Contains("materials") && !suitGear
                   && item.Category is ItemCategory.Material or ItemCategory.Component);
    }

    /// <summary>The items made by the non-market recipes that require one of <paramref name="blueprints"/>.</summary>
    private static IEnumerable<string> ItemsUnlockedBy(GameContent content, ICollection<string> blueprints)
        => content.Recipes.Values
            .Where(r => r.Station != CraftingStation.Market && !string.IsNullOrEmpty(r.RequiredBlueprint)
                        && blueprints.Contains(r.RequiredBlueprint!))
            .SelectMany(r => r.Outputs)
            .Select(o => o.Item);

    private void ResolveFallbacks(GameContent content)
    {
        foreach (var item in content.Items.Values)
        {
            if (_items.ContainsKey(item.Key))
            {
                continue;
            }

            string kind = item.Category.ToString();
            foreach (var c in Categories)
            {
                if ((c.FallbackFor ?? new List<string>()).Any(f => string.Equals(f, kind, StringComparison.OrdinalIgnoreCase)))
                {
                    Add(_items, item.Key, c.Key);
                }
            }
        }

        // A blueprint no category lists takes the categories of what it unlocks: the items of its recipes, its modules and
        // its ships. Read after the items are resolved, so their rules and fallbacks count too.
        foreach (var bp in content.Blueprints.Values)
        {
            if (_blueprints.ContainsKey(bp.Key))
            {
                continue;
            }

            var unlocked = ItemsUnlockedBy(content, new[] { bp.Key }).SelectMany(OfItem)
                .Concat(content.ShipModules.Values.Where(m => m.RequiredBlueprint == bp.Key).SelectMany(m => OfModule(m.Key)))
                .Concat(content.Ships.Values.Where(s => s.RequiredBlueprint == bp.Key).SelectMany(s => OfShip(s.Key)));
            foreach (string category in unlocked.ToList())
            {
                Add(_blueprints, bp.Key, category);
            }
        }
    }

    private void Sort()
    {
        foreach (var map in new[] { _items, _blueprints, _modules, _ships })
        {
            foreach (var list in map.Values)
            {
                list.Sort((a, b) => OrderOf(a).CompareTo(OrderOf(b)));
            }
        }
    }

    private static void AddAll(Dictionary<string, List<string>> map, List<string>? keys, string category, Func<string, bool> exists)
    {
        foreach (string key in keys ?? new List<string>())
        {
            if (!string.IsNullOrEmpty(key) && exists(key))
            {
                Add(map, key, category);
            }
        }
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string category)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = new List<string>();
        }

        if (!list.Contains(category))
        {
            list.Add(category);
        }
    }

    /// <summary>What is wrong with <paramref name="catalog"/> against <paramref name="content"/>: unknown item, blueprint,
    /// module, ship, group, rule or item-category names (the build skips them silently, the content tests do not).</summary>
    public static IReadOnlyList<string> Problems(CategoryCatalog catalog, GameContent content)
    {
        var problems = new List<string>();
        var groups = new HashSet<string>((catalog.Groups ?? new List<CategoryGroupDefinition>()).Select(g => g.Key), StringComparer.Ordinal);
        var kinds = new HashSet<string>(Enum.GetNames(typeof(ItemCategory)), StringComparer.OrdinalIgnoreCase);
        foreach (var c in catalog.Categories ?? new List<CategoryDefinition>())
        {
            string at = "category '" + c.Key + "'";
            if (!groups.Contains(c.Group))
            {
                problems.Add($"{at}: unknown group '{c.Group}'");
            }

            if (!string.IsNullOrEmpty(c.IconItem) && content.GetItem(c.IconItem) is null)
            {
                problems.Add($"{at}: unknown icon item '{c.IconItem}'");
            }

            problems.AddRange(Unknown(at, "item", c.Items, k => content.GetItem(k) is not null));
            problems.AddRange(Unknown(at, "blueprint", c.Blueprints, k => content.GetBlueprint(k) is not null));
            problems.AddRange(Unknown(at, "module", c.Modules, k => content.GetShipModule(k) is not null));
            problems.AddRange(Unknown(at, "ship", c.Ships, k => content.GetShip(k) is not null));
            problems.AddRange(Unknown(at, "rule", c.Derive, RuleNames.Contains));
            problems.AddRange(Unknown(at, "item category", c.FallbackFor, kinds.Contains));
        }

        return problems;
    }

    private static IEnumerable<string> Unknown(string at, string what, List<string>? keys, Func<string, bool> exists)
        => (keys ?? new List<string>()).Where(k => !exists(k)).Select(k => $"{at}: unknown {what} '{k}'");
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3102: the build categories of <c>data/categories.json</c> — one shared set for the crafting list, the blueprints, the
/// ship modules and the inventories, in which a thing may sit in several categories (the jetpack is "Travel &amp;
/// Transport" and "Suit &amp; Survival"). Presentation only: no server rule reads it. The membership is resolved by
/// <see cref="Content.ContentCategories"/>.
/// </summary>
public sealed class CategoryCatalog
{
    /// <summary>The sidebar headings, in display order.</summary>
    public List<CategoryGroupDefinition> Groups { get; set; } = new();

    /// <summary>The categories, in display order.</summary>
    public List<CategoryDefinition> Categories { get; set; } = new();
}

/// <summary>A sidebar heading that gathers several categories ("Equipment", "Building &amp; Base", …).</summary>
public sealed class CategoryGroupDefinition
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
}

/// <summary>One build category and who belongs to it.</summary>
public sealed class CategoryDefinition
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;

    /// <summary>The <see cref="CategoryGroupDefinition.Key"/> it is listed under.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>A UI icon name (<c>Resources/icons</c>), the fallback when <see cref="IconItem"/> has no picture.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>An item whose icon stands for the category in the sidebar (the speeder for "Travel &amp; Transport").</summary>
    public string IconItem { get; set; } = string.Empty;

    /// <summary>Named membership rules on top of the lists below — see <see cref="Content.ContentCategories"/> for the
    /// rule names (<c>weapon</c>, <c>vehicle</c>, <c>suitGear</c>, <c>consumable</c>, <c>materials</c>,
    /// <c>fromBlueprints</c>, <c>allModules</c>, <c>allShips</c>).</summary>
    public List<string> Derive { get; set; } = new();

    /// <summary>Item categories (<see cref="ItemCategory"/> names) whose items land here when no list and no rule put them
    /// anywhere — so every item has at least one category, also an item added later without touching this file.</summary>
    public List<string> FallbackFor { get; set; } = new();

    public List<string> Items { get; set; } = new();
    public List<string> Blueprints { get; set; } = new();
    public List<string> Modules { get; set; } = new();
    public List<string> Ships { get; set; } = new();
}

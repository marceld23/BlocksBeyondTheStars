// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A tech-tree / blueprint node. Unlocking a blueprint is the first of the two-step
/// progression (unlock blueprint, then build), loaded from <c>data/blueprints.json</c>.
/// </summary>
public sealed class BlueprintDefinition
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>Free-form category for UI grouping, e.g. "ShipExpansion", "Tools", "Suit".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Blueprint keys that must be unlocked before this one becomes available.</summary>
    public List<string> Prerequisites { get; set; } = new();

    /// <summary>Resource/research cost to unlock this blueprint.</summary>
    public List<ItemAmount> UnlockCost { get; set; } = new();

    /// <summary>Knowledge points (earned by scanning new things) additionally required to unlock.</summary>
    public int KnowledgeCost { get; set; }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A quantity of an item referenced by its string key. Used for recipe inputs/outputs,
/// block drops, build costs and blueprint unlock costs.
/// </summary>
public sealed class ItemAmount
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; } = 1;

    /// <summary>#2958: an ingredient or a cost that takes any member of this item group (<c>logs</c>, <c>leaves</c>) —
    /// <see cref="Item"/> is then its representative (shown in lists, given back by a disassembly, used up first).
    /// Empty for a plain amount of exactly <see cref="Item"/>.</summary>
    public string Group { get; set; } = string.Empty;

    public ItemAmount() { }

    public ItemAmount(string item, int count)
    {
        Item = item;
        Count = count;
    }
}

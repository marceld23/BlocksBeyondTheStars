// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>A stack of identical items in an inventory slot.</summary>
public sealed class ItemStack
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }

    public ItemStack() { }

    public ItemStack(string item, int count)
    {
        Item = item;
        Count = count;
    }

    public bool IsEmpty => Count <= 0 || string.IsNullOrEmpty(Item);

    public ItemStack Clone() => new(Item, Count);
}

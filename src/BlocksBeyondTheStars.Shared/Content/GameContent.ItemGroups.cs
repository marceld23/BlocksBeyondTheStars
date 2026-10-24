// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #2958: item groups. A tree's pale bark or its second and third leaf kind drop items of their own (so a child builds
/// with the wood they cut), and a recipe that asks for wood takes any of them: an ingredient that names a group
/// (<see cref="ItemAmount.Group"/>) counts every member (<see cref="ItemDefinition.Group"/>). The ingredient's
/// <see cref="ItemAmount.Item"/> stays its representative — the name a list shows, what a disassembly gives back —
/// and is used up first, the other members after it in <c>data/items.json</c> order. The server's material pool and
/// the client's craft gate both read the members from here, so the craft button answers what the server does.
/// </summary>
public sealed partial class GameContent
{
    private readonly Dictionary<string, List<string>> _itemGroups = new(StringComparer.Ordinal);

    private void IndexItemGroups(IEnumerable<ItemDefinition> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Group))
            {
                continue;
            }

            if (!_itemGroups.TryGetValue(item.Group, out var members))
            {
                _itemGroups[item.Group] = members = new List<string>();
            }

            members.Add(item.Key);
        }
    }

    /// <summary>The members of an item group in <c>data/items.json</c> order; empty for an unknown or empty group.</summary>
    public IReadOnlyList<string> ItemGroupMembers(string? group)
        => !string.IsNullOrEmpty(group) && _itemGroups.TryGetValue(group!, out var members) ? members : Array.Empty<string>();

    /// <summary>The items an ingredient draws from besides its representative: the members of its group, or none for a
    /// plain ingredient. Callers count (and take) <see cref="ItemAmount.Item"/> first and then every member that is not
    /// it — allocation-free, so the crafting menu may ask every frame.</summary>
    public IReadOnlyList<string> GroupMembersOf(ItemAmount need)
        => string.IsNullOrEmpty(need.Group) ? Array.Empty<string>() : ItemGroupMembers(need.Group);

    /// <summary>True when a stack of <paramref name="itemKey"/> serves the ingredient: it is the item, or a member of the
    /// ingredient's group.</summary>
    public bool Accepts(ItemAmount need, string itemKey)
        => string.Equals(itemKey, need.Item, StringComparison.Ordinal) || Contains(GroupMembersOf(need), itemKey);

    /// <summary>The localization key of a group's name ("Logs (any kind)").</summary>
    public static string ItemGroupNameKey(string group) => "itemgroup." + group + ".name";

    /// <summary>The group an item belongs to, or empty.</summary>
    public string ItemGroupOf(string itemKey) => GetItem(itemKey)?.Group ?? string.Empty;

    /// <summary>Validation of the group references (#2958): an ingredient's group exists, and its representative is a
    /// member of it.</summary>
    private void ValidateItemGroup(List<string> problems, string ctx, ItemAmount amount)
    {
        if (string.IsNullOrEmpty(amount.Group))
        {
            return;
        }

        var members = ItemGroupMembers(amount.Group);
        if (members.Count == 0)
        {
            problems.Add($"{ctx} names unknown item group '{amount.Group}'.");
        }
        else if (!Contains(members, amount.Item))
        {
            problems.Add($"{ctx} represents item group '{amount.Group}' by '{amount.Item}', which is not a member of it.");
        }
    }

    private static bool Contains(IReadOnlyList<string> list, string key)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

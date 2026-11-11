// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3488: a named loot table (<c>data/loot_tables.json</c>) — what a chest an author set to "random" may hold. A table
/// is a list of ROLLS; each roll draws one of its weighted entries (or nothing, by its chance) and a count from the
/// entry's range. Every draw is a stateless hash of the caller's seed, the roll and the pick, so a chest's content
/// depends only on the chest and never on what was rolled before it.
/// </summary>
public sealed class LootTableDefinition
{
    public string Key { get; set; } = string.Empty;

    public List<LootRoll> Rolls { get; set; } = new();

    /// <summary>Rolls the table with <paramref name="seed"/>. Unknown items (per <paramref name="itemExists"/>) are skipped.</summary>
    public List<ItemStack> Roll(ulong seed, Func<string, bool> itemExists)
    {
        var items = new List<ItemStack>();
        for (int r = 0; r < Rolls.Count; r++)
        {
            var roll = Rolls[r];
            int picks = Math.Max(1, roll.Picks);
            for (int p = 0; p < picks; p++)
            {
                ulong lane = StableHashing.Mix64(seed ^ ((ulong)(uint)r * 0x9E37_79B9_7F4A_7C15UL) ^ ((ulong)(uint)p * 0xC2B2_AE3D_27D4_EB4FUL));
                if (roll.Chance < 1.0 && StableHashing.Unit(StableHashing.Mix64(lane ^ 0xA5A5UL)) >= roll.Chance)
                {
                    continue;
                }

                var entry = roll.Pick(StableHashing.Unit(StableHashing.Mix64(lane ^ 0x5A5AUL)));
                if (entry is null || !itemExists(entry.Item))
                {
                    continue;
                }

                int min = Math.Max(1, entry.Min);
                int max = Math.Max(min, entry.Max);
                int count = min + (int)(StableHashing.Unit(StableHashing.Mix64(lane ^ 0x3C3CUL)) * (max - min + 1));
                items.Add(new ItemStack(entry.Item, Math.Min(max, count)));
            }
        }

        return items;
    }
}

/// <summary>One roll of a <see cref="LootTableDefinition"/>: how often it draws, how likely each draw yields
/// something, and the weighted entries it draws from.</summary>
public sealed class LootRoll
{
    /// <summary>How many times this roll draws (at least 1).</summary>
    public int Picks { get; set; } = 1;

    /// <summary>Chance that a draw yields an item at all (1 = always).</summary>
    public double Chance { get; set; } = 1.0;

    public List<LootEntry> Entries { get; set; } = new();

    /// <summary>The entry a uniform draw in [0, 1) lands on, by weight — null when the roll has no entry.</summary>
    public LootEntry? Pick(double unit)
    {
        int total = 0;
        foreach (var e in Entries)
        {
            total += Math.Max(1, e.Weight);
        }

        if (total == 0)
        {
            return null;
        }

        int at = (int)(unit * total);
        foreach (var e in Entries)
        {
            at -= Math.Max(1, e.Weight);
            if (at < 0)
            {
                return e;
            }
        }

        return Entries[Entries.Count - 1];
    }
}

/// <summary>One weighted entry of a <see cref="LootRoll"/>.</summary>
public sealed class LootEntry
{
    public string Item { get; set; } = string.Empty;

    public int Min { get; set; } = 1;

    public int Max { get; set; } = 1;

    public int Weight { get; set; } = 1;
}

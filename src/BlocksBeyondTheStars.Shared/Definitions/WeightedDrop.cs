// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One row of a block's <see cref="BlockDefinition.RandomDrops"/> table (#1761): an item and count that come out
/// with probability <see cref="Weight"/> over the table's weight sum. An empty <see cref="Item"/> is the
/// "nothing" outcome, so a table can say "one in twenty scrap piles is empty" without a sentinel item.
/// </summary>
public sealed class WeightedDrop
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public int Weight { get; set; } = 1;

    /// <summary>Draws one row from <paramref name="table"/> for a mined cell: the cell coordinates and the world
    /// seed are folded into one FNV-style hash and reduced over the weight sum. Deterministic on purpose (see
    /// <see cref="BlockDefinition.RandomDrops"/>); null when the table is empty or the drawn row is "nothing".</summary>
    public static ItemAmount? Draw(System.Collections.Generic.IReadOnlyList<WeightedDrop> table, long worldSeed, int x, int y, int z)
    {
        int total = 0;
        foreach (var row in table)
        {
            total += System.Math.Max(0, row.Weight);
        }

        if (total <= 0)
        {
            return null;
        }

        ulong h = 14695981039346656037UL;
        foreach (long v in new[] { worldSeed, x, y, z, 0x5C2A9L })
        {
            h ^= (ulong)v;
            h *= 1099511628211UL;
            h ^= h >> 29;
        }

        int roll = (int)(h % (ulong)total);
        foreach (var row in table)
        {
            roll -= System.Math.Max(0, row.Weight);
            if (roll < 0)
            {
                return string.IsNullOrEmpty(row.Item) || row.Count <= 0 ? null : new ItemAmount(row.Item, row.Count);
            }
        }

        return null;
    }
}

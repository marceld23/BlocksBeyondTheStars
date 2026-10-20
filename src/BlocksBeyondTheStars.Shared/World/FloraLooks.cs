// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #2832: which LOOK a flora archetype wears on a world. An archetype (<see cref="BlockDefinition.Looks"/>) has its own
/// tile plus extra tiles of the same plant in other shapes; every world picks one of them, like it picks the species'
/// colour (<see cref="FloraTints"/>) — a pure function of (world seed, location key, block key), so every client agrees
/// without any traffic and the server never needs to know. Purely visual: the client repaints the block's atlas slot.
/// </summary>
public static class FloraLooks
{
    /// <summary>The suffix of a look's texture key: <c>&lt;block&gt;_look&lt;n&gt;</c> (n ≥ 2; look 1 is the block's own tile).</summary>
    public const string Suffix = "_look";

    /// <summary>The look a block wears on a world: 0 = its own tile, 1..n = <c>Looks[i - 1]</c>. Every look (the own tile
    /// included) is equally likely.</summary>
    public static int IndexFor(long worldSeed, string locationKey, string blockKey, int lookCount)
    {
        if (lookCount <= 0)
        {
            return 0;
        }

        ulong h = Mix(FloraTints.Hash($"{worldSeed}|{locationKey}|flora-look|{blockKey}"));
        return (int)(h % (ulong)(lookCount + 1));
    }

    /// <summary>The texture key a block wears on a world — its own key, or one of its looks.</summary>
    public static string TextureKeyFor(long worldSeed, string locationKey, BlockDefinition block)
    {
        var looks = block.Looks;
        int index = looks == null ? 0 : IndexFor(worldSeed, locationKey, block.Key, looks.Count);
        return index == 0 ? block.Key : looks![index - 1];
    }

    /// <summary>The textures a group of blocks that share one look pool wear on a world (#2851, the three leaf slots of
    /// <see cref="BlocksBeyondTheStars.Shared.Definitions.TreeFoliage.LeafSlotKeys"/>): each block picks as
    /// <see cref="TextureKeyFor"/> does, in the group's order, and a block whose pick an earlier block already wears steps on
    /// through its own list to the next free texture — so the slots of one world never wear the same look. The first block
    /// of the group keeps exactly its <see cref="TextureKeyFor"/> pick. Block key → texture key.</summary>
    public static System.Collections.Generic.Dictionary<string, string> TextureKeysForGroup(long worldSeed, string locationKey,
        System.Collections.Generic.IReadOnlyList<BlockDefinition> group)
    {
        var picks = new System.Collections.Generic.Dictionary<string, string>();
        var taken = new System.Collections.Generic.HashSet<string>();
        foreach (var block in group)
        {
            var looks = block.Looks;
            int count = looks?.Count ?? 0;
            int index = IndexFor(worldSeed, locationKey, block.Key, count);
            string pick = block.Key;
            for (int step = 0; step <= count; step++)
            {
                int i = (index + step) % (count + 1);
                string candidate = i == 0 ? block.Key : looks![i - 1];
                if (!taken.Contains(candidate))
                {
                    pick = candidate;
                    break;
                }
            }

            taken.Add(pick);
            picks[block.Key] = pick;
        }

        return picks;
    }

    /// <summary>The block a look tile belongs to (<c>tree_leaves_look3</c> → <c>tree_leaves</c>); any other key as it is.
    /// The texture rules (alpha mode, the editor's grouping) treat a look like its block.</summary>
    public static string BaseKeyOf(string key)
    {
        int at = key.LastIndexOf(Suffix, System.StringComparison.Ordinal);
        if (at <= 0 || at + Suffix.Length >= key.Length)
        {
            return key;
        }

        for (int i = at + Suffix.Length; i < key.Length; i++)
        {
            if (key[i] < '0' || key[i] > '9')
            {
                return key;
            }
        }

        return key.Substring(0, at);
    }

    // FNV-1a's low bits are weak for a small modulo; a splitmix finaliser spreads them first.
    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z ^= z >> 30;
            z *= 0xBF58476D1CE4E5B9UL;
            z ^= z >> 27;
            z *= 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return z;
        }
    }
}

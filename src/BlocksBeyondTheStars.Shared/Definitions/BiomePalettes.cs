// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3289: the colour palettes of the biomes. Every biome SURFACE block names one (<see cref="BlockDefinition.BiomePalette"/>
/// in <c>data/blocks.json</c> — the 22 surface blocks of the shipped planets share eight), and a block with
/// <see cref="BlockDefinition.BiomeLooks"/> (the rune stone, the ancient brick) wears the palette of the column it stands
/// in: one bundled tile per palette, <c>&lt;block&gt;_&lt;palette&gt;</c>. The client mesher picks the tile; the server
/// stamps a monument's rune glow in the palette's <see cref="GlowOf"/> colour so light and texture agree.
/// <para>The list is the art: every look block ships one tile per entry (a content test holds that), so a palette is
/// added together with its tiles. The order is the index the client caches — append, never reorder.</para>
/// </summary>
public static class BiomePalettes
{
    /// <summary>The palette names, by index.</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "verdant", "desert", "frost", "volcanic", "marsh", "rock", "crystal", "toxic",
    };

    /// <summary>Rune glow per palette (0xRRGGBB): the light a monument's runes cast in a column of that palette —
    /// the colour of the notes in the palette's rune tile, so the glow and the texture tell the same story.</summary>
    private static readonly int[] Glows =
    {
        0x6FE05A, // verdant: leaf green
        0xF0A03C, // desert: warm amber
        0x9FE4FF, // frost: ice blue
        0xFF6A2A, // volcanic: ember orange
        0x5FD0A8, // marsh: bog teal
        0xC9D4E8, // rock: silver
        0xB070F0, // crystal: violet
        0xB8F040, // toxic: acid lime
    };

    /// <summary>How many palettes there are.</summary>
    public static int Count => Names.Count;

    /// <summary>The index of a palette name, or -1 for null, empty or an unknown name.</summary>
    public static int IndexOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (int i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The rune glow (0xRRGGBB) of a palette, or 0 for an index out of range.</summary>
    public static int GlowOf(int palette) => palette >= 0 && palette < Glows.Length ? Glows[palette] : 0;

    /// <summary>The palette a column whose biome surface is <paramref name="surfaceKey"/> wears, or -1 (no surface, an
    /// unknown block, or a block without a palette).</summary>
    public static int OfSurface(GameContent content, string? surfaceKey)
        => string.IsNullOrEmpty(surfaceKey) ? -1 : IndexOf(content.GetBlock(surfaceKey!)?.BiomePalette);

    /// <summary>The texture key of a block's look in a palette: <c>rune_stone</c> + <c>desert</c> → <c>rune_stone_desert</c>.</summary>
    public static string LookKey(string blockKey, int palette) => blockKey + "_" + Names[palette];

    /// <summary>Every look texture key of a block (none unless it has <see cref="BlockDefinition.BiomeLooks"/>), in
    /// palette order.</summary>
    public static IEnumerable<string> LookKeysOf(BlockDefinition block)
    {
        if (!block.BiomeLooks || string.IsNullOrEmpty(block.Key))
        {
            yield break;
        }

        for (int p = 0; p < Names.Count; p++)
        {
            yield return LookKey(block.Key, p);
        }
    }

    /// <summary>Splits a look texture key into its block and palette (<c>ancient_brick_frost</c> → <c>ancient_brick</c>,
    /// frost) when the block wears biome looks; false for any other key.</summary>
    public static bool TryParseLookKey(GameContent content, string key, out BlockDefinition block, out int palette)
    {
        block = null!;
        palette = -1;
        int at = key.LastIndexOf('_');
        if (at <= 0 || at >= key.Length - 1)
        {
            return false;
        }

        int p = IndexOf(key.Substring(at + 1));
        if (p < 0 || content.GetBlock(key.Substring(0, at)) is not { BiomeLooks: true } def)
        {
            return false;
        }

        block = def;
        palette = p;
        return true;
    }
}

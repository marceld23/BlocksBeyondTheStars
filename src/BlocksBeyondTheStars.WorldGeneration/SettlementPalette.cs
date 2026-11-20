// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The lively places: a settlement's own colour (plan revision 2). Every lively settlement carries one of a handful of
/// colours on its roofs, trims and awnings — "the red village", "the blue town" — drawn from its seed, or fixed by its kit
/// (<c>StructureKit.Palette</c>: a hex colour, or <c>none</c>). The colour is pinned in the plan
/// (<see cref="SettlementPlan.Colour"/>); the baker applies it as an ordinary dye on the cells a module marks with the roof
/// and accent tokens, and the client hangs its door signs in it.
/// </summary>
public static class SettlementPalette
{
    /// <summary>The kit value that switches the colour off.</summary>
    public const string None = "none";

    /// <summary>The colours a settlement draws from: clear, friendly, told apart at a distance — never so dark that a dyed
    /// roof reads as black, never the grey of iron or stone.</summary>
    public static readonly int[] Colours =
    {
        0xB8442C, // brick red
        0x2F6F9F, // harbour blue
        0x3F8F5A, // meadow green
        0xD9A441, // straw yellow
        0x7A4FA3, // violet
        0xC9683E, // terracotta
        0x2E8C8C, // teal
        0xC84F78, // rose
        0x8C5A3C, // chestnut
        0x5B7FB8, // sky blue
    };

    /// <summary>The colour of the settlement of <paramref name="seed"/>: the kit's fixed one when it names a hex colour, none
    /// (0) when it says <see cref="None"/>, else one of <see cref="Colours"/> by the seed.</summary>
    public static int ColourFor(long seed, string? kitPalette)
    {
        string fixedColour = (kitPalette ?? string.Empty).Trim();
        if (string.Equals(fixedColour, None, System.StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fixedColour.Length > 0 && int.TryParse(fixedColour.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return rgb & 0xFFFFFF;
        }

        return Colours[StableHashing.Index("settlement:colour", seed, Colours.Length)];
    }
}

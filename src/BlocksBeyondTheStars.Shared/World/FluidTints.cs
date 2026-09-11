// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The colour of a world's water (school club wave 3, #1758) — the water counterpart of
/// <see cref="FloraTints.ForWorld"/>: one pure function of (seed, body, type) that the server ships in the
/// environment message and the client's water shader multiplies in, so the minimap, the orbit preview and the
/// sea agree. A type opts in through <see cref="PlanetType.WaterTint"/>:
/// <list type="bullet">
/// <item>empty → <see cref="Mode.Classic"/>: the shader's own blue, every existing world untouched;</item>
/// <item>"auto" → <see cref="Mode.Tint"/> with a seeded pick from a blue-dominant palette (teal, green, yellow,
/// violet, red as the rarer finds);</item>
/// <item>"rainbow" → <see cref="Mode.Rainbow"/>: static rainbow bands by position (Marcel: not animated);</item>
/// <item>"#rrggbb" → <see cref="Mode.Tint"/> with that fixed colour.</item>
/// </list>
/// </summary>
public static class FluidTints
{
    public enum Mode
    {
        Classic = 0,
        Tint = 1,
        Rainbow = 2,
    }

    /// <summary>The classic water blue of the block atlas (0.20, 0.42, 0.85), for callers that need a colour in
    /// classic mode (the minimap paints the sea with it).</summary>
    public const int ClassicWater = 0x336BD9;

    private static readonly (int Rgb, int Weight)[] AutoPalette =
    {
        (0x336BD9, 55), // the classic blue
        (0x2FB5B0, 15), // teal
        (0x3FA65A, 12), // green
        (0xC9B83A, 8),  // yellow
        (0x8E5BD8, 6),  // violet
        (0xC94A4A, 4),  // red
    };

    /// <summary>The water colour and mode of one world. Deterministic from the seed and the body, like every
    /// other per-world look; the same historical location hash as <see cref="FloraTints.ForWorld"/>.
    /// <paramref name="terrainGeneration"/> is the SAVE's generation: the colour is computed at runtime, not baked
    /// into the world, so without this gate an old save would change colour the day its type opted in. Below
    /// <see cref="WorldDescription.AuthoredContentGeneration"/> every world keeps the classic blue.</summary>
    public static (int Rgb, Mode Mode) ForWorld(long worldSeed, string? locationKey, PlanetType? planet, int terrainGeneration)
    {
        string spec = planet?.WaterTint?.Trim() ?? string.Empty;
        if (spec.Length == 0 || terrainGeneration < WorldDescription.AuthoredContentGeneration)
        {
            return (ClassicWater, Mode.Classic);
        }

        if (string.Equals(spec, "rainbow", System.StringComparison.OrdinalIgnoreCase))
        {
            return (ClassicWater, Mode.Rainbow);
        }

        if (spec[0] == '#' && spec.Length == 7 && int.TryParse(spec.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out int fixedRgb))
        {
            return (fixedRgb & 0xFFFFFF, Mode.Tint);
        }

        // "auto" (and any unknown word): the seeded palette pick.
        int h = 17;
        foreach (char c in locationKey ?? string.Empty)
        {
            h = unchecked(h * 31 + c);
        }

        uint mix = unchecked((uint)(h ^ (int)worldSeed ^ 0x7A7E12));
        int total = 0;
        foreach (var (_, w) in AutoPalette)
        {
            total += w;
        }

        int roll = (int)(mix % (uint)total);
        int i = 0;
        for (; i < AutoPalette.Length; i++)
        {
            roll -= AutoPalette[i].Weight;
            if (roll < 0)
            {
                break;
            }
        }

        if (i >= AutoPalette.Length)
        {
            i = AutoPalette.Length - 1;
        }

        int anchor = AutoPalette[i].Rgb;
        int r = (anchor >> 16) & 0xFF, g = (anchor >> 8) & 0xFF, b = anchor & 0xFF;
        int Jit(int shift) => (int)((mix >> shift) & 0x1F) - 16; // -16..+15
        int rgb = (Clamp8(r + Jit(3)) << 16) | (Clamp8(g + Jit(8)) << 8) | Clamp8(b + Jit(13));
        return (rgb, Mode.Tint);
    }

    private static int Clamp8(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
}

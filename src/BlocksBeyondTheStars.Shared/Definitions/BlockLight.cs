// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Which block types are real light sources (#2036, #2407): they flood a coloured light into their surroundings
/// (the client's block-light field), on top of their own <see cref="BlockDefinition.Emission"/> glow. Glow alone
/// lights nothing — ores and the machines keep their self-glow look without flooding the world. Since #2407 the
/// natural emitters — lava, crystals and the glowing flora — DO light, quietly: a small <see cref="BlockDefinition.LightRadius"/>
/// (the fixtures' full reach is <see cref="DefaultRadius"/>) and, for the massed ones, only from cells with an air
/// neighbour (<see cref="BlockDefinition.LightSurfaceOnly"/>). A placed cell can still become (or recolour) a light
/// through its glow/dye modifier; that priority lives with the callers. Shared so the client's light index and the
/// content tests read one rule.
/// <para>
/// The client carries a source as ONE packed int (<see cref="Pack"/>): bits 0–23 the colour, bits 24–27 the radius
/// (0 = default) and bit 28 the surface-only flag — so the light index, the chunk messages and the mesher's source
/// list needed no new shape.
/// </para>
/// </summary>
public static class BlockLight
{
    /// <summary>Glow an authored material needs to count as a fixture when it declares no
    /// <see cref="BlockDefinition.LightColor"/> (the Material Editor path).</summary>
    public const float AuthoredFixtureEmission = 0.85f;

    /// <summary>The fixtures' reach in blocks (the mesher's flood radius); the largest a block can declare.</summary>
    public const int DefaultRadius = 9;

    private const int ColorMask = 0xFFFFFF;
    private const int RadiusShift = 24;
    private const int RadiusMask = 0xF;
    private const int SurfaceOnlyBit = 1 << 28;

    /// <summary>
    /// The light colour (0xRRGGBB) a block type floods on its own, or 0 when it is no light source. An explicit
    /// <see cref="BlockDefinition.LightColor"/> wins (0 opts out); otherwise a material authored with its own
    /// <see cref="BlockDefinition.Color"/> and at least <see cref="AuthoredFixtureEmission"/> glow counts as a
    /// bright fixture — the old implicit rule, kept only for Material Editor materials. Every shipped light in
    /// <c>data/blocks.json</c> declares its colour, so a new fixture can't silently fall below the threshold the
    /// way the lantern and the campfire did.
    /// </summary>
    public static int NaturalColorOf(BlockDefinition? def)
    {
        if (def is null)
        {
            return 0;
        }

        if (def.LightColor is int light)
        {
            return light & ColorMask;
        }

        return def.Color is int c && (def.Emission ?? 0f) >= AuthoredFixtureEmission ? c & ColorMask : 0;
    }

    /// <summary>How far a block type's light reaches: its declared radius, clamped to 1..<see cref="DefaultRadius"/>.</summary>
    public static int RadiusOf(BlockDefinition? def)
    {
        int r = def?.LightRadius ?? DefaultRadius;
        return r < 1 ? 1 : r > DefaultRadius ? DefaultRadius : r;
    }

    /// <summary>The block type's light as the client's packed source value (0 = no light).</summary>
    public static int PackedLightOf(BlockDefinition? def)
    {
        int rgb = NaturalColorOf(def);
        return rgb == 0 ? 0 : Pack(rgb, RadiusOf(def), def?.LightSurfaceOnly ?? false);
    }

    /// <summary>Packs a colour with its reach and surface-only flag; radius <see cref="DefaultRadius"/> (or 0) is stored as 0.</summary>
    public static int Pack(int rgb, int radius, bool surfaceOnly)
    {
        int r = radius <= 0 || radius >= DefaultRadius ? 0 : radius & RadiusMask;
        return (rgb & ColorMask) | (r << RadiusShift) | (surfaceOnly ? SurfaceOnlyBit : 0);
    }

    /// <summary>The colour part of a packed source.</summary>
    public static int ColorFrom(int packed) => packed & ColorMask;

    /// <summary>The reach of a packed source in blocks (<see cref="DefaultRadius"/> when none was stored).</summary>
    public static int RadiusFrom(int packed)
    {
        int r = (packed >> RadiusShift) & RadiusMask;
        return r == 0 ? DefaultRadius : r;
    }

    /// <summary>Whether a packed source lights only from cells with an air neighbour.</summary>
    public static bool SurfaceOnlyFrom(int packed) => (packed & SurfaceOnlyBit) != 0;

    /// <summary>A dyed light keeps its reach: the dye replaces the colour bits only.</summary>
    public static int Recolor(int packed, int rgb) => (packed & ~ColorMask) | (rgb & ColorMask);
}

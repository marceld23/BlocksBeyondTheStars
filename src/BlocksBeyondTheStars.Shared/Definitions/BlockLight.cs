// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Which block types are real light sources (#2036): they flood a coloured light into their surroundings (the
/// client's block-light field), on top of their own <see cref="BlockDefinition.Emission"/> glow. Glow alone lights
/// nothing — lava, crystals, ores, most glowing flora and the machines keep their self-glow look without flooding
/// the world. A placed cell can still become (or recolour) a light through its glow/dye modifier; that priority
/// lives with the callers. Shared so the client's light index and the content tests read one rule.
/// </summary>
public static class BlockLight
{
    /// <summary>Glow an authored material needs to count as a fixture when it declares no
    /// <see cref="BlockDefinition.LightColor"/> (the Material Editor path).</summary>
    public const float AuthoredFixtureEmission = 0.85f;

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
            return light & 0xFFFFFF;
        }

        return def.Color is int c && (def.Emission ?? 0f) >= AuthoredFixtureEmission ? c & 0xFFFFFF : 0;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>How a wall block holds a climber (#2191). Ordered from no hold to the full hold.</summary>
public enum ClimbSurface : byte
{
    /// <summary>No hold: air, liquids, plants, tree crowns, doors, glass — and ice for a climber without claws.</summary>
    None = 0,

    /// <summary>Holds only a climber wearing climbing claws (ice).</summary>
    Icy = 1,

    /// <summary>Holds, but the grip tires twice as fast (sand, snow, ash — the loose granular blocks).</summary>
    Slippery = 2,

    /// <summary>A plain hold.</summary>
    Normal = 3,
}

/// <summary>
/// Which blocks a wall climber can hold on to (#2191) — one rule for the client's climbing probe and the content tests.
/// Data-driven: a block's <see cref="BlockDefinition.Climb"/> field overrides the default, which is "every solid,
/// non-liquid block holds; the granular ones are slippery". Whether the block also has a collider in the client mesh
/// (tree crowns and props have none) is the client's own second check.
/// </summary>
public static class ClimbSurfaces
{
    /// <summary>The <c>climb</c> value for a block with no hold at all.</summary>
    public const string NoneValue = "none";

    /// <summary>The <c>climb</c> value for a block that only holds a climber wearing claws.</summary>
    public const string IcyValue = "icy";

    /// <summary>The <c>climb</c> value for a block that holds but tires the grip faster.</summary>
    public const string SlipperyValue = "slippery";

    /// <summary>Whether a <c>climb</c> value from the data is one this rule knows (null = the default).</summary>
    public static bool IsKnownValue(string? value) => value is null or NoneValue or IcyValue or SlipperyValue;

    /// <summary>How the block holds a climber without any gear.</summary>
    public static ClimbSurface Of(BlockDefinition? def)
    {
        if (def is null || !def.Solid || def.Liquid
            || def.Key is "air" or "lava" or "water" or "ladder"
            || def.Category == "door"
            || def.Key.StartsWith("flora_", StringComparison.Ordinal)
            || TreeFoliage.IsKey(def.Key)) // #2184: a crown is walked through, so it gives no hold (the trunk does)
        {
            return ClimbSurface.None;
        }

        return def.Climb switch
        {
            NoneValue => ClimbSurface.None,
            IcyValue => ClimbSurface.Icy,
            SlipperyValue => ClimbSurface.Slippery,
            _ => def.Granular ? ClimbSurface.Slippery : ClimbSurface.Normal,
        };
    }

    /// <summary>How the block holds THIS climber: without claws an icy wall gives no hold; with them (#2192) icy and
    /// slippery walls hold like any other.</summary>
    public static ClimbSurface ForClimber(ClimbSurface surface, bool iceGrip)
    {
        if (iceGrip)
        {
            return surface == ClimbSurface.None ? ClimbSurface.None : ClimbSurface.Normal;
        }

        return surface == ClimbSurface.Icy ? ClimbSurface.None : surface;
    }
}

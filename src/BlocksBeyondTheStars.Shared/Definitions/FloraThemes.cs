// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The tree archetypes world generation can stamp. A biome's <see cref="FloraThemes.Theme"/>
/// lists which kinds grow there; groves pick one by noise so a wood is conifers OR palms, not a mix of
/// every shape. <see cref="None"/> means the theme grows no trees (e.g. fungal worlds get giant mushrooms
/// instead).</summary>
public enum TreeKind
{
    None = 0,
    Broadleaf, // rounded leafy crown (the classic deciduous tree)
    Conifer,   // tall, narrow, conical layered needle crown (boreal/alpine)
    Palm,      // bare trunk + a frond burst at the very top (tropical/oasis)
    Jungle,    // very tall, broad heavy canopy (rainforest)
    Dead,      // bare trunk + stub branches, no leaves (arid / scorched / blighted)
}

/// <summary>
/// Per-world flora "theme": the climate/style signature that biases which species a world grows and how.
/// A planet (and optionally each biome) names a theme; the theme prefers some <see cref="FloraTag"/>s
/// (so the same surface block grows different plants from world to world), scales overall lushness +
/// tree density, and lists the tree archetypes its woods are made of. Pure data — server, client preview
/// and every player resolve the identical theme, so generation stays deterministic.
/// </summary>
public static class FloraThemes
{
    public sealed record Theme(
        string Name,
        FloraTag Preferred,
        double DensityMul,
        double TreeMul,
        TreeKind[] Trees);

    private static readonly Theme Temperate = new("temperate", FloraTag.Lush, 1.0, 1.0,
        new[] { TreeKind.Broadleaf });

    /// <summary>The themes, keyed by name. Unknown / empty names fall back to <see cref="Temperate"/>.</summary>
    private static readonly Theme[] AllThemes =
    {
        Temperate,
        new("tropical", FloraTag.Tropical | FloraTag.Lush, 1.3, 1.3,
            new[] { TreeKind.Jungle, TreeKind.Palm, TreeKind.Broadleaf }),
        new("savanna", FloraTag.Dry | FloraTag.Lush, 0.9, 0.7,
            new[] { TreeKind.Broadleaf }),
        new("desert", FloraTag.Dry, 0.6, 0.5,
            new[] { TreeKind.Palm, TreeKind.Dead }),
        new("swamp", FloraTag.Wetland | FloraTag.Fungal, 1.2, 0.9,
            new[] { TreeKind.Broadleaf, TreeKind.Dead }),
        new("tundra", FloraTag.Cold, 0.85, 0.8,
            new[] { TreeKind.Conifer }),
        new("alpine", FloraTag.Cold | FloraTag.Rocky, 0.75, 0.9,
            new[] { TreeKind.Conifer }),
        new("fungal", FloraTag.Fungal | FloraTag.Alien, 1.2, 0.0,
            new[] { TreeKind.None }),
        new("alien", FloraTag.Alien, 1.15, 0.8,
            new[] { TreeKind.Broadleaf, TreeKind.Dead }),
        new("crystal", FloraTag.Rocky | FloraTag.Alien | FloraTag.Glow, 1.0, 0.0,
            new[] { TreeKind.None }),
        new("ashen", FloraTag.Dry | FloraTag.Glow, 0.8, 0.4,
            new[] { TreeKind.Dead }),
    };

    /// <summary>Resolves a theme by name (case-insensitive); empty/unknown → temperate.</summary>
    public static Theme Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var t in AllThemes)
            {
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }
        }

        return Temperate;
    }

    /// <summary>0..1 probability a species with these tags is activated on a world of this theme. Species
    /// matching a preferred tag are common; off-theme species stay an occasional find (coverage is enforced
    /// separately so no surface ever goes bare).</summary>
    public static double ActivationChance(Theme theme, FloraTag speciesTags)
        => (theme.Preferred & speciesTags) != 0 ? 0.85 : 0.40;

    /// <summary>Relative pick weight (≥1) for a species with these tags under this theme — themed species
    /// dominate a patch, off-theme ones still appear for variety.</summary>
    public static int PickWeight(Theme theme, FloraTag speciesTags)
        => (theme.Preferred & speciesTags) != 0 ? 4 : 1;
}

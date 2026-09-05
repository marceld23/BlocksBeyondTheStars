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

    // Generation-1 kinds (#1648): themes list them in TreesGen1 only, so classic worlds never roll them.
    Baobab,       // thick 2×2 trunk under a flat wide crown (savanna)
    Mangrove,     // stilt roots beside water (tropical / swamp coasts)
    Bamboo,       // a grove of thin 8–12 stems (tropical)
    Saguaro,      // a green column with up-turned arms (desert)
    Willow,       // a broad crown dripping leaf strands (temperate / swamp)
    MushroomTree, // a mushroom-stem trunk under a flat cap (alien)
    CrystalTree,  // a crystal shaft with a cross of arms (alien / crystal)
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
        TreeKind[] Trees,
        TreeKind[]? TreesGen1 = null)
    {
        /// <summary>The palette a generation-1 world draws from (#1648): the classic kinds plus the new ones;
        /// generation-0 worlds keep <see cref="Trees"/> so their woods never change.</summary>
        public TreeKind[] PaletteFor(int generation) => generation >= 1 && TreesGen1 is { } g1 ? g1 : Trees;
    }

    private static readonly Theme Temperate = new("temperate", FloraTag.Lush, 1.0, 1.0,
        new[] { TreeKind.Broadleaf },
        new[] { TreeKind.Broadleaf, TreeKind.Willow });

    /// <summary>The themes, keyed by name. Unknown / empty names fall back to <see cref="Temperate"/>.</summary>
    private static readonly Theme[] AllThemes =
    {
        Temperate,
        new("tropical", FloraTag.Tropical | FloraTag.Lush, 1.3, 1.3,
            new[] { TreeKind.Jungle, TreeKind.Palm, TreeKind.Broadleaf },
            new[] { TreeKind.Jungle, TreeKind.Palm, TreeKind.Broadleaf, TreeKind.Mangrove, TreeKind.Bamboo }),
        new("savanna", FloraTag.Dry | FloraTag.Lush, 0.9, 0.7,
            new[] { TreeKind.Broadleaf },
            new[] { TreeKind.Broadleaf, TreeKind.Baobab }),
        new("desert", FloraTag.Dry, 0.6, 0.5,
            new[] { TreeKind.Palm, TreeKind.Dead },
            new[] { TreeKind.Palm, TreeKind.Dead, TreeKind.Saguaro }),
        new("swamp", FloraTag.Wetland | FloraTag.Fungal, 1.2, 0.9,
            new[] { TreeKind.Broadleaf, TreeKind.Dead },
            new[] { TreeKind.Broadleaf, TreeKind.Dead, TreeKind.Willow, TreeKind.Mangrove }),
        new("tundra", FloraTag.Cold, 0.85, 0.8,
            new[] { TreeKind.Conifer }),
        new("alpine", FloraTag.Cold | FloraTag.Rocky, 0.75, 0.9,
            new[] { TreeKind.Conifer }),
        new("fungal", FloraTag.Fungal | FloraTag.Alien, 1.2, 0.0,
            new[] { TreeKind.None }),
        new("alien", FloraTag.Alien, 1.15, 0.8,
            new[] { TreeKind.Broadleaf, TreeKind.Dead },
            new[] { TreeKind.Broadleaf, TreeKind.Dead, TreeKind.MushroomTree, TreeKind.CrystalTree }),
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

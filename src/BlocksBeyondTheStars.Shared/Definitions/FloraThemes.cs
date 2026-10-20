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

    // Generation-29 kinds (#2853): themes list them in TreesGen29 only, so no older world ever rolls them.
    Birch,    // a thin pale trunk under a loose small crown (temperate / tundra / alpine)
    Oak,      // a thick trunk under a wide crown of several overlapping blobs (temperate / savanna)
    Column,   // a narrow tall green column like a cypress (temperate / alpine / desert edges)
    Umbrella, // a leaning trunk under a flat wide crown like an acacia (savanna)
    Blossom,  // a round crown full of blossoms (temperate / tropical)
    TreeFern, // a thin trunk under a crown of long drooping fronds (tropical / swamp)
    Bottle,   // a bulging thick trunk under a small crown (desert / savanna)
    Spiral,   // a trunk winding like a corkscrew (alien)
    Ball,     // a straight trunk under a perfectly round crown — the lollipop tree (rare)
    Lantern,  // a crown hung with glowing fruit — only on a luminous world (#2857)
}

/// <summary>One entry of a weighted tree palette (#2848, generation 29): the kind and how often it leads a grove relative
/// to the other entries of the same palette.</summary>
public readonly struct TreeWeight
{
    public TreeWeight(TreeKind kind, int weight)
    {
        Kind = kind;
        Weight = weight;
    }

    public TreeKind Kind { get; }

    public int Weight { get; }
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
        TreeKind[]? TreesGen1 = null,
        bool Strict = false,
        TreeWeight[]? TreesGen29 = null,
        double? TreeMulGen29 = null)
    {
        // TreesGen29 / TreeMulGen29 (#2848, #2849, #2853): from generation 29 a theme's woods are drawn from a WEIGHTED
        // palette (the classic equal slices starved the first and last kinds of a long palette), and the fungal and
        // crystal themes grow trees at all. Older generations read Trees / TreesGen1 / TreeMul exactly as before.
        // Strict (#1760, generation ≥ 5): a strict theme activates ONLY species carrying one of its preferred
        // tags — the flower planet grows flowers and nothing else. Every classic theme is non-strict, so the
        // 85 / 40 roll and with it every existing roster stay exactly as they were.
        /// <summary>The palette a generation-1 world draws from (#1648): the classic kinds plus the new ones;
        /// generation-0 worlds keep <see cref="Trees"/> so their woods never change.</summary>
        public TreeKind[] PaletteFor(int generation)
        {
            if (generation >= TreeVarietyGeneration && TreesGen29 is { } g29)
            {
                var kinds = new TreeKind[g29.Length];
                for (int i = 0; i < g29.Length; i++)
                {
                    kinds[i] = g29[i].Kind;
                }

                return kinds;
            }

            return generation >= 1 && TreesGen1 is { } g1 ? g1 : Trees;
        }

        /// <summary>The weighted palette of a generation-29 world (#2848): <see cref="TreesGen29"/>, or the generation's
        /// classic palette with weight 1 per kind for a theme that names none.</summary>
        public TreeWeight[] WeightedPaletteFor(int generation)
        {
            if (generation >= TreeVarietyGeneration && TreesGen29 is { } g29)
            {
                return g29;
            }

            var kinds = PaletteFor(generation);
            var weights = new TreeWeight[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                weights[i] = new TreeWeight(kinds[i], 1);
            }

            return weights;
        }

        /// <summary>The theme's tree density multiplier on a world of this generation (#2849: the fungal and crystal themes
        /// grow trees from generation 29).</summary>
        public double TreeMulFor(int generation) => generation >= TreeVarietyGeneration && TreeMulGen29 is { } m ? m : TreeMul;
    }

    /// <summary>The generation of the tree-variety package (the value of <c>WorldDescription.TreeVarietyGeneration</c>,
    /// repeated here because the theme table is read by both and a test holds the two equal).</summary>
    public const int TreeVarietyGeneration = 29;

    private static TreeWeight W(TreeKind kind, int weight) => new(kind, weight);

    private static readonly Theme Temperate = new("temperate", FloraTag.Lush, 1.0, 1.0,
        new[] { TreeKind.Broadleaf },
        new[] { TreeKind.Broadleaf, TreeKind.Willow },
        TreesGen29: new[]
        {
            W(TreeKind.Broadleaf, 3), W(TreeKind.Oak, 3), W(TreeKind.Birch, 2), W(TreeKind.Willow, 2),
            W(TreeKind.Blossom, 1), W(TreeKind.Column, 1), W(TreeKind.Ball, 1),
        });

    /// <summary>The themes, keyed by name. Unknown / empty names fall back to <see cref="Temperate"/>.</summary>
    private static readonly Theme[] AllThemes =
    {
        Temperate,
        new("tropical", FloraTag.Tropical | FloraTag.Lush, 1.3, 1.3,
            new[] { TreeKind.Jungle, TreeKind.Palm, TreeKind.Broadleaf },
            new[] { TreeKind.Jungle, TreeKind.Palm, TreeKind.Broadleaf, TreeKind.Mangrove, TreeKind.Bamboo },
            TreesGen29: new[]
            {
                W(TreeKind.Jungle, 3), W(TreeKind.Palm, 3), W(TreeKind.Mangrove, 2), W(TreeKind.Bamboo, 2),
                W(TreeKind.TreeFern, 2), W(TreeKind.Broadleaf, 1), W(TreeKind.Blossom, 1), W(TreeKind.Ball, 1),
            }),
        new("savanna", FloraTag.Dry | FloraTag.Lush, 0.9, 0.7,
            new[] { TreeKind.Broadleaf },
            new[] { TreeKind.Broadleaf, TreeKind.Baobab },
            TreesGen29: new[]
            {
                W(TreeKind.Baobab, 3), W(TreeKind.Umbrella, 3), W(TreeKind.Bottle, 1), W(TreeKind.Oak, 1),
                W(TreeKind.Broadleaf, 1), W(TreeKind.Ball, 1),
            }),
        new("desert", FloraTag.Dry, 0.6, 0.5,
            new[] { TreeKind.Palm, TreeKind.Dead },
            new[] { TreeKind.Palm, TreeKind.Dead, TreeKind.Saguaro },
            TreesGen29: new[]
            {
                W(TreeKind.Palm, 3), W(TreeKind.Saguaro, 3), W(TreeKind.Bottle, 2), W(TreeKind.Dead, 2), W(TreeKind.Column, 1),
            }),
        new("swamp", FloraTag.Wetland | FloraTag.Fungal, 1.2, 0.9,
            new[] { TreeKind.Broadleaf, TreeKind.Dead },
            new[] { TreeKind.Broadleaf, TreeKind.Dead, TreeKind.Willow, TreeKind.Mangrove },
            TreesGen29: new[]
            {
                W(TreeKind.Willow, 3), W(TreeKind.Mangrove, 2), W(TreeKind.TreeFern, 2), W(TreeKind.Dead, 1), W(TreeKind.Broadleaf, 1),
            }),
        new("tundra", FloraTag.Cold, 0.85, 0.8,
            new[] { TreeKind.Conifer },
            TreesGen29: new[] { W(TreeKind.Conifer, 4), W(TreeKind.Birch, 2), W(TreeKind.Dead, 1) }),
        new("alpine", FloraTag.Cold | FloraTag.Rocky, 0.75, 0.9,
            new[] { TreeKind.Conifer },
            TreesGen29: new[] { W(TreeKind.Conifer, 4), W(TreeKind.Birch, 1), W(TreeKind.Column, 1) }),
        new("fungal", FloraTag.Fungal | FloraTag.Alien, 1.2, 0.0,
            new[] { TreeKind.None },
            TreesGen29: new[] { W(TreeKind.MushroomTree, 1) },
            TreeMulGen29: 0.5),
        new("alien", FloraTag.Alien, 1.15, 0.8,
            new[] { TreeKind.Broadleaf, TreeKind.Dead },
            new[] { TreeKind.Broadleaf, TreeKind.Dead, TreeKind.MushroomTree, TreeKind.CrystalTree },
            TreesGen29: new[]
            {
                W(TreeKind.Spiral, 3), W(TreeKind.MushroomTree, 2), W(TreeKind.CrystalTree, 1), W(TreeKind.Dead, 1),
                W(TreeKind.Broadleaf, 1),
            }),
        new("crystal", FloraTag.Rocky | FloraTag.Alien | FloraTag.Glow, 1.0, 0.0,
            new[] { TreeKind.None },
            TreesGen29: new[] { W(TreeKind.CrystalTree, 1) },
            TreeMulGen29: 0.15),
        new("ashen", FloraTag.Dry | FloraTag.Glow, 0.8, 0.4,
            new[] { TreeKind.Dead }),
        // School club wave 3 (#1760): Damian's flower planet — fields of flowers in every colour, no trees, and no
        // other plant at all (Strict). Only a generation-5 type names it.
        new("floral", FloraTag.Floral, 1.6, 0.0,
            new[] { TreeKind.None },
            Strict: true),
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
        => ActivationChance(theme.Preferred, speciesTags);

    /// <summary>The same roll against a SET of preferred tags — the union of the planet theme and its biome
    /// themes from <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration"/>
    /// (#1715).</summary>
    public static double ActivationChance(FloraTag preferred, FloraTag speciesTags)
        => (preferred & speciesTags) != 0 ? 0.85 : 0.40;

    /// <summary>The roll with a strict theme (#1760): off-theme species never activate; on-theme species keep the
    /// 85 % so a strict world still varies from seed to seed.</summary>
    public static double ActivationChance(FloraTag preferred, FloraTag speciesTags, bool strict)
        => (preferred & speciesTags) != 0 ? 0.85 : (strict ? 0.0 : 0.40);

    /// <summary>Relative pick weight (≥1) for a species with these tags under this theme — themed species
    /// dominate a patch, off-theme ones still appear for variety.</summary>
    public static int PickWeight(Theme theme, FloraTag speciesTags)
        => (theme.Preferred & speciesTags) != 0 ? 4 : 1;
}

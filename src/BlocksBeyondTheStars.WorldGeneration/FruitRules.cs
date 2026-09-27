// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The fruit-tree rules (#2038, terrain generation 14) — pure functions shared by world generation (the fruit under a
/// stamped crown), the server (a sapling's tree, the regrowing fruit) and the tests. A world rolls, per TREE KIND,
/// which of its active fruit shapes the kind bears and one colour for all of that kind's fruit: every broadleaf of a
/// world carries the same fruit in the same colour, its conifers another shape in another colour, and the same kind on
/// another world differs. The colour rides in the fruit cell's colour modifier (the dye channel the mesher already
/// renders), so no client needs any rule from here.
/// </summary>
public static class FruitRules
{
    /// <summary>Share of the fruit-bearing trees that actually carry fruit (decision 2026-09-26: about a third).</summary>
    public const double FruitTreeShare = 0.35;

    /// <summary>Fewest fruit on a tree that bears any.</summary>
    public const int MinFruitPerTree = 2;

    /// <summary>Most fruit on one tree.</summary>
    public const int MaxFruitPerTree = 5;

    /// <summary>Seconds a harvested fruit takes to grow back on its leaf (small flora returns after 30 s).</summary>
    public const double RegrowSeconds = 120.0;

    private const long ShapeSalt = 0x5A9E7F;
    private const long TintSalt = 0x71A7C0;
    private const long TreeSalt = 0x7EE7F0;

    /// <summary>True for a tree kind that can bear fruit: the leafy crowns. Dead snags, bamboo, saguaros, mushroom
    /// and crystal trees bear none.</summary>
    public static bool BearsFruit(TreeKind kind) => kind is TreeKind.Broadleaf or TreeKind.Conifer or TreeKind.Palm
        or TreeKind.Jungle or TreeKind.Baobab or TreeKind.Mangrove or TreeKind.Willow;

    /// <summary>The fruit block key this tree kind bears on the world of <paramref name="rosterSeed"/>, or null when the
    /// kind bears none or the world activated no fruit species. <paramref name="activeFruitKeys"/> are the world's
    /// active fruit species in catalog order (the roster's <c>Active</c> fruit entries).</summary>
    public static string? ShapeFor(long rosterSeed, TreeKind kind, IReadOnlyList<string> activeFruitKeys)
    {
        if (!BearsFruit(kind) || activeFruitKeys.Count == 0)
        {
            return null;
        }

        ulong h = Mix(rosterSeed, (long)kind, ShapeSalt);
        return activeFruitKeys[(int)(h % (ulong)activeFruitKeys.Count)];
    }

    /// <summary>The fruit block key this tree kind bears on a world that is sure to grow <paramref name="guaranteedFruit"/>
    /// (#2084, <see cref="GuaranteedFruitFor"/>): the world's palm and jungle trees always bear it, every other kind keeps its
    /// roll. With no guarantee (null) — or while the guaranteed species is not active — this is <see cref="ShapeFor(long,
    /// TreeKind, IReadOnlyList{string})"/>.</summary>
    public static string? ShapeFor(long rosterSeed, TreeKind kind, IReadOnlyList<string> activeFruitKeys, string? guaranteedFruit)
    {
        if (guaranteedFruit != null && BearsGuaranteedFruit(kind))
        {
            foreach (var key in activeFruitKeys)
            {
                if (key == guaranteedFruit)
                {
                    return guaranteedFruit;
                }
            }
        }

        return ShapeFor(rosterSeed, kind, activeFruitKeys);
    }

    /// <summary>The tree kinds that always bear a world's guaranteed fruit (#2084): the palms and the jungle trees — the
    /// bananas' own trees, and a tropical world's signature.</summary>
    public static bool BearsGuaranteedFruit(TreeKind kind) => kind is TreeKind.Palm or TreeKind.Jungle;

    /// <summary>The fruit species a world of this type is sure to grow (#2084): the type's
    /// <see cref="PlanetType.GuaranteedFruit"/> on a generation-16 world, or null (below it, or when the type names none or
    /// names something that is not a fruit).</summary>
    public static string? GuaranteedFruitFor(PlanetType planet, int terrainGeneration)
        => terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BipedGeneration
           && !string.IsNullOrEmpty(planet.GuaranteedFruit) && FloraCatalog.IsFruit(planet.GuaranteedFruit)
            ? planet.GuaranteedFruit
            : null;

    /// <summary>The item a fruit block yields when picked ("flora_fruit_banana" → "fruit_banana", as <c>data/blocks.json</c>
    /// drops it).</summary>
    public static string ItemFor(string fruitBlockKey)
        => fruitBlockKey.StartsWith("flora_", System.StringComparison.Ordinal) ? fruitBlockKey.Substring(6) : fruitBlockKey;

    /// <summary>The clean fruit items a world's trees can bear (#2082) — what a begging species may love: every active fruit
    /// shape some tree kind of the type's palettes (planet theme + biome themes) bears, in catalog order. Empty on a world
    /// with no trees or no active fruit, and on a world whose tree species is toxic (its fruit is, too — nothing to love
    /// there before the detoxifier). <paramref name="rosterSeed"/> is the world's roster seed, the one worldgen and the
    /// server roll the fruit with.</summary>
    public static IReadOnlyList<string> CleanFruitItems(PlanetType planet, long rosterSeed, int terrainGeneration)
    {
        var items = new List<string>();
        if (terrainGeneration < BlocksBeyondTheStars.Shared.World.WorldDescription.FruitTreesGeneration
            || planet.TreeDensity is <= 0.0
            || TreeGenerator.Generate(planet, rosterSeed) is not { Toxic: false })
        {
            return items;
        }

        var active = new List<string>();
        foreach (var fs in FloraGenerator.GenerateRoster(planet, rosterSeed, terrainGeneration))
        {
            if (fs.Active && FloraCatalog.IsFruit(fs.BlockKey))
            {
                active.Add(fs.BlockKey);
            }
        }

        if (active.Count == 0)
        {
            return items;
        }

        var kinds = new HashSet<TreeKind>();
        void AddTheme(string themeName)
        {
            var theme = FloraThemes.Resolve(themeName);
            foreach (var k in theme.Trees)
            {
                kinds.Add(k);
            }

            foreach (var k in theme.PaletteFor(1))
            {
                kinds.Add(k);
            }
        }

        AddTheme(planet.FloraTheme);
        foreach (var biome in planet.Biomes)
        {
            AddTheme(string.IsNullOrEmpty(biome.FloraTheme) ? planet.FloraTheme : biome.FloraTheme);
        }

        string? guaranteed = GuaranteedFruitFor(planet, terrainGeneration);
        var borne = new HashSet<string>();
        foreach (var kind in kinds)
        {
            if (ShapeFor(rosterSeed, kind, active, guaranteed) is { } shape)
            {
                borne.Add(shape);
            }
        }

        foreach (var key in active) // catalog order, so the list (and the pick from it) is stable
        {
            if (borne.Contains(key))
            {
                items.Add(ItemFor(key));
            }
        }

        return items;
    }

    /// <summary>The colour (0xRRGGBB) of every fruit this tree kind bears on the world of <paramref name="rosterSeed"/>:
    /// any hue, in the flora band (saturation 0.45–0.85, value 0.85–1.0) so a fruit reads as ripe on any world and the
    /// dye recolour never blows out.</summary>
    public static int TintFor(long rosterSeed, TreeKind kind)
    {
        ulong h = Mix(rosterSeed, (long)kind, TintSalt);
        float hue = (h % 3600UL) / 3600f;
        float sat = 0.45f + ((h >> 12) % 1000UL) / 1000f * 0.40f;
        float val = 0.85f + ((h >> 24) % 1000UL) / 1000f * 0.15f;
        var (r, g, b) = HsvToRgb(hue, sat, val);
        return (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
    }

    /// <summary>The per-tree hash every fruit decision of one tree derives from (the trunk column, wrapped in X by the
    /// caller so a tree at the seam rolls the same from both sides).</summary>
    public static ulong TreeHash(long worldSeed, int wrappedX, int wz) => Mix(worldSeed ^ TreeSalt, wrappedX, wz);

    /// <summary>Whether a tree bears fruit at all (<see cref="FruitTreeShare"/> of the fruit-bearing kinds).</summary>
    public static bool TreeBearsFruit(ulong treeHash) => (treeHash >> 40) % 1000UL < (ulong)(FruitTreeShare * 1000);

    /// <summary>The cells a tree hangs its fruit in: under its LOWEST leaves. A leaf qualifies when nothing of the tree
    /// (no leaf, no log) sits directly beneath it; the fruit goes into that cell below. Picks 2–5 of the candidates,
    /// spread along the crown, from the tree hash — the same list from every chunk a straddling tree is built from,
    /// because the caller records the builder's whole cell sequence, not what its own chunk kept.</summary>
    /// <param name="leafCells">Every leaf cell the builder wrote or tried to write, in builder order.</param>
    /// <param name="treeCells">Every cell of the tree (leaves and logs).</param>
    public static List<(int X, int Y, int Z)> PickFruitCells(IReadOnlyList<(int X, int Y, int Z)> leafCells,
        HashSet<(int X, int Y, int Z)> treeCells, ulong treeHash)
    {
        var candidates = new List<(int X, int Y, int Z)>();
        var seen = new HashSet<(int X, int Y, int Z)>();
        foreach (var (x, y, z) in leafCells)
        {
            var below = (x, y - 1, z);
            if (!treeCells.Contains(below) && seen.Add(below))
            {
                candidates.Add(below);
            }
        }

        var picked = new List<(int X, int Y, int Z)>();
        if (candidates.Count == 0)
        {
            return picked;
        }

        int count = MinFruitPerTree + (int)((treeHash >> 8) % (ulong)(MaxFruitPerTree - MinFruitPerTree + 1));
        if (candidates.Count <= count)
        {
            picked.AddRange(candidates);
            return picked;
        }

        int start = (int)((treeHash >> 16) % (ulong)candidates.Count);
        int step = candidates.Count / count;
        for (int i = 0; i < count; i++)
        {
            picked.Add(candidates[(start + i * step) % candidates.Count]);
        }

        return picked;
    }

    private static ulong Mix(long a, long b, long c)
    {
        unchecked
        {
            ulong h = (ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)c * 0x165667B19E3779F9UL;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 29;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 32;
            return h;
        }
    }

    private static int Channel(float v) => v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f);

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h * 6f;
        float x = c * (1f - System.Math.Abs(hp % 2f - 1f));
        (float r, float g, float b) = ((int)hp % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return (r + m, g + m, b + m);
    }
}

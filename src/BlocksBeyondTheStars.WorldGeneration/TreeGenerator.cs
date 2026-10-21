// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministically derives a planet's tree identity (<see cref="TreeSpecies"/>) from the world seed +
/// planet — the arboreal counterpart to <see cref="FloraGenerator"/>. Up to generation 28 a single tree
/// block pair (<c>wood_log</c> + <c>tree_leaves</c>) is shared by every tree shape, so a world coins one
/// common tree species (the trunk and the leaves read as the same plant) plus the giant tree's. From
/// generation 29 (#2851) the leaf slots B and C, the blossom crown and the saguaro are their own blocks, and
/// each slot coins its own species (<see cref="GenerateSlot"/>). Same seed + planet → the same species, so
/// nothing needs storing. Surfaced when the player scans a trunk or leaf block.
/// </summary>
public static class TreeGenerator
{
    /// <summary>This world's tree species, or null on worlds that grow no trees (airless / barren).</summary>
    public static TreeSpecies? Generate(PlanetType planet, long worldSeed)
    {
        // Airless + barren worlds grow no flora, and trees gate on flora (see WorldGenerator.StampTrees),
        // so they have no tree identity either.
        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            return null;
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x77EEA1100B;
        var rng = new System.Random(unchecked((int)(planetSeed ^ (planetSeed >> 32))));
        return new TreeSpecies
        {
            Id = "tr0",
            Name = NameGenerator.Tree(rng),
            Toxic = rng.NextDouble() < 0.3, // most trees are benign; a notable minority is toxic (matches flora)
        };
    }

    /// <summary>This world's GIANT tree species (#1783, generation 6): the landmark trees grow on their own block
    /// pair (<c>giant_log</c> + <c>giant_leaves</c>), so they carry a second coined name and their own toxic roll.
    /// Own salt, so adding it changed nothing about the ordinary tree's name. Null where no tree can grow.</summary>
    public static TreeSpecies? GenerateGiant(PlanetType planet, long worldSeed)
    {
        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            return null;
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x61A4773EE5;
        var rng = new System.Random(unchecked((int)(planetSeed ^ (planetSeed >> 32))));
        return new TreeSpecies
        {
            Id = "tr1",
            Name = NameGenerator.Tree(rng),
            Toxic = rng.NextDouble() < 0.3,
        };
    }

    // ---------------- the tree slots (#2851, terrain generation 29) ----------------

    /// <summary>The common tree's slot: <c>wood_log</c>, <c>tree_leaves</c>, needles and fronds (<see cref="Generate"/>, "tr0").</summary>
    public const int CommonSlot = 0;

    /// <summary>The giant tree's slot (<see cref="GenerateGiant"/>, "tr1").</summary>
    public const int GiantSlot = 1;

    /// <summary>Leaf slot B and the pale bark (birch, bottle, willow, mangrove, umbrella trees — "tr2").</summary>
    public const int LeavesBSlot = 2;

    /// <summary>Leaf slot C (baobab, bamboo, column, tree fern, spiral and lantern trees — "tr3").</summary>
    public const int LeavesCSlot = 3;

    /// <summary>The blossom tree's crown ("tr4").</summary>
    public const int BlossomSlot = 4;

    /// <summary>The saguaro's body ("tr5").</summary>
    public const int SaguaroSlot = 5;

    /// <summary>How many slots there are (ids "tr0" … "tr5").</summary>
    public const int SlotCount = 6;

    // The salts of slots 2..5 — their own, so the common and the giant species keep their names.
    private static readonly long[] SlotSalts = { 0, 0, 0x2851_B0B0_B2, 0x2851_C0C0_C3, 0x2851_B105_5F, 0x2851_5A6A_0A };

    private static readonly string[][] SlotKeys =
    {
        new[] { TreeWood.LogKey, TreeFoliage.LeavesKey, TreeFoliage.NeedlesKey, TreeFoliage.FrondKey },
        new[] { TreeWood.GiantLogKey, TreeFoliage.GiantLeavesKey },
        new[] { TreeFoliage.LeavesBKey, TreeWood.PaleLogKey },
        new[] { TreeFoliage.LeavesCKey },
        new[] { TreeFoliage.BlossomKey },
        new[] { WorldGenerator.SaguaroKey },
    };

    /// <summary>The blocks a slot's species names when scanned (#2851): the server maps each of them to
    /// <see cref="GenerateSlot"/>'s species. Slot 0 is the classic common tree (trunk, leaves, needles, fronds), slot 1 the
    /// giant tree; slots 2–5 exist on a generation-29 world. The common trunk (<c>wood_log</c>) stays slot 0 even under a
    /// slot-B or slot-C crown — one log, one name.</summary>
    public static IReadOnlyList<string> SlotBlockKeys(int slot)
        => slot >= 0 && slot < SlotCount ? SlotKeys[slot] : throw new System.ArgumentOutOfRangeException(nameof(slot));

    /// <summary>This world's tree species of one slot (#2851): slot 0 and 1 are <see cref="Generate"/> and
    /// <see cref="GenerateGiant"/>; slots 2–5 coin their own name from their own salt, with the id <c>"tr" + slot</c>.
    /// A slot's name never repeats a lower slot's name on the same world (re-drawn from the slot's own stream), so the
    /// scanner tells a birch from an oak. Their toxicity is the common tree's: the fruit of every kind is toxic exactly when
    /// the common tree is (<c>FloraGenerator</c>), and the trunk of most kinds is the common log — leaves, trunk and fruit
    /// of one tree must never disagree on "edible". Same seed + planet → the same species; null where no tree can
    /// grow (airless / barren worlds).</summary>
    public static TreeSpecies? GenerateSlot(PlanetType planet, long worldSeed, int slot)
    {
        if (slot < 0 || slot >= SlotCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(slot));
        }

        if (slot == CommonSlot)
        {
            return Generate(planet, worldSeed);
        }

        if (slot == GiantSlot)
        {
            return GenerateGiant(planet, worldSeed);
        }

        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            return null;
        }

        var taken = new HashSet<string>(System.StringComparer.Ordinal);
        TreeSpecies? species = null;
        for (int s = 0; s <= slot; s++)
        {
            species = s == CommonSlot ? Generate(planet, worldSeed)
                : s == GiantSlot ? GenerateGiant(planet, worldSeed)
                : CoinSlot(planet, worldSeed, s, taken, Generate(planet, worldSeed)?.Toxic ?? false);
            if (species != null)
            {
                taken.Add(species.Name);
            }
        }

        return species;
    }

    private static TreeSpecies CoinSlot(PlanetType planet, long worldSeed, int slot, HashSet<string> taken, bool commonToxic)
    {
        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ SlotSalts[slot];
        var rng = new System.Random(unchecked((int)(planetSeed ^ (planetSeed >> 32))));
        string name = NameGenerator.Tree(rng);
        for (int tries = 0; tries < 8 && taken.Contains(name); tries++)
        {
            name = NameGenerator.Tree(rng);
        }

        return new TreeSpecies
        {
            Id = "tr" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = name,
            Toxic = commonToxic,
        };
    }

    /// <summary>The slots 2–5 whose blocks can grow on a world of this type (#2851): the slots of every kind in the
    /// generation-29 palettes of the planet's theme and its biome themes (the lantern tree only on a luminous world, the
    /// crystal tree only where it glows), and the saguaro slot wherever sand lies (the giant cactus of the generation-1
    /// giant flora grows on any sand). Empty below generation 29 or where no tree grows. The server names only these, so a
    /// scan list never offers a tree the world cannot have.</summary>
    public static IReadOnlyList<int> SlotsGrownOn(PlanetType planet, long rosterSeed, int terrainGeneration)
    {
        var slots = new SortedSet<int>();
        if (terrainGeneration < BlocksBeyondTheStars.Shared.World.WorldDescription.TreeVarietyGeneration
            || planet.IsAirless || planet.FloraDensity <= 0)
        {
            return new List<int>();
        }

        var glow = GlowRules.LevelFor(planet, rosterSeed, terrainGeneration);
        void AddTheme(string? themeName)
        {
            foreach (var e in WorldGenerator.EffectiveTreePalette(FloraThemes.Resolve(themeName), terrainGeneration, glow))
            {
                var (leaves, trunk) = WorldGenerator.TreeSlotKeysFor(e.Kind);
                for (int slot = LeavesBSlot; slot < SlotCount; slot++)
                {
                    foreach (var key in SlotKeys[slot])
                    {
                        if (key == leaves || key == trunk)
                        {
                            slots.Add(slot);
                        }
                    }
                }
            }
        }

        AddTheme(planet.FloraTheme);
        bool sand = planet.SurfaceBlock == "sand" || planet.BeachBlock == "sand";
        foreach (var biome in planet.Biomes)
        {
            AddTheme(string.IsNullOrEmpty(biome.FloraTheme) ? planet.FloraTheme : biome.FloraTheme);
            sand |= biome.SurfaceBlock == "sand";
        }

        if (sand)
        {
            slots.Add(SaguaroSlot);
        }

        return new List<int>(slots);
    }
}

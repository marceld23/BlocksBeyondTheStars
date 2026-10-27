// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>The form a planted sapling grows into (#1774, #2977–#2980).</summary>
public enum SaplingShape
{
    Broadleaf,  // the classic small tree: a trunk of 4–5 logs under a round crown
    Palm,       // a bare trunk under a burst of fronds (the banana sapling)
    Conifer,    // a trunk under a cone of needle rings
    Fifi,       // the Fifi plant's smaller shape: green stem, yellow crown, pink blossoms, berries
    YoungGiant, // a 2×2 trunk of giant logs under a wide crown (the mahogany sapling)
}

/// <summary>One plantable sapling: its block and item, the shape it grows, the blocks the tree is made of and how long it
/// takes. <see cref="Fruit"/> is the fruit block the tree always bears (a fruit-tree sapling, #2977); null for the
/// classic sapling, whose tree bears the world's own broadleaf fruit, and for the trees that bear none.</summary>
public sealed record SaplingKind(string BlockKey, string ItemKey, SaplingShape Shape, string Log, string Crown,
    string? Fruit = null, string? Blossom = null, string? Berries = null, double GrowSeconds = SaplingCatalog.DefaultGrowSeconds);

/// <summary>
/// The saplings of the game (#1774 the classic one; #2977 one per fruit; #2978 the conifer; #2979 the Fifi plant; #2980 the
/// mahogany). Every sapling is a flora block that roots in dirt, grass or mud, is picked up as itself, and turns into its
/// tree once there is room (<c>GameServerSaplings</c>). Where a sapling COMES FROM is data: leaf drops and hand recipes in
/// <c>data/blocks.json</c> and <c>data/recipes.json</c>.
/// </summary>
public static class SaplingCatalog
{
    /// <summary>Seconds a small sapling takes to become a tree (#1774).</summary>
    public const double DefaultGrowSeconds = 150.0;

    /// <summary>Seconds the mahogany sapling takes — a young giant grows more slowly (#2980).</summary>
    public const double GiantGrowSeconds = 300.0;

    /// <summary>Every sapling, the classic one first.</summary>
    public static readonly IReadOnlyList<SaplingKind> All = new[]
    {
        new SaplingKind("flora_sapling", "sapling", SaplingShape.Broadleaf, "wood_log", "tree_leaves"),
        new SaplingKind("flora_sapling_conifer", "sapling_conifer", SaplingShape.Conifer, "wood_log", "pine_needles"),
        new SaplingKind("flora_sapling_fruit_round", "sapling_fruit_round", SaplingShape.Broadleaf, "wood_log", "tree_leaves", Fruit: "flora_fruit_round"),
        new SaplingKind("flora_sapling_fruit_long", "sapling_fruit_long", SaplingShape.Broadleaf, "wood_log", "tree_leaves", Fruit: "flora_fruit_long"),
        new SaplingKind("flora_sapling_fruit_grape", "sapling_fruit_grape", SaplingShape.Broadleaf, "wood_log", "tree_leaves", Fruit: "flora_fruit_grape"),
        new SaplingKind("flora_sapling_fruit_banana", "sapling_fruit_banana", SaplingShape.Palm, "wood_log", "palm_frond", Fruit: "flora_fruit_banana"),
        new SaplingKind("flora_sapling_fifi", "sapling_fifi", SaplingShape.Fifi, FifiPlant.StemKey, FifiPlant.LeafKey,
            Blossom: FifiPlant.BlossomKey, Berries: FifiPlant.BerriesKey),
        new SaplingKind("flora_sapling_mahogany", "sapling_mahogany", SaplingShape.YoungGiant, "giant_log", "giant_leaves",
            GrowSeconds: GiantGrowSeconds),
    };

    /// <summary>The sapling planted as <paramref name="blockKey"/>, or null.</summary>
    public static SaplingKind? ForBlock(string blockKey)
    {
        foreach (var k in All)
        {
            if (k.BlockKey == blockKey)
            {
                return k;
            }
        }

        return null;
    }

    /// <summary>True for an item that plants a sapling.</summary>
    public static bool IsSaplingItem(string itemKey)
    {
        foreach (var k in All)
        {
            if (k.ItemKey == itemKey)
            {
                return true;
            }
        }

        return false;
    }
}

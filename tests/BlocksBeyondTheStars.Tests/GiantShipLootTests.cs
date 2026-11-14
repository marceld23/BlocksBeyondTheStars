// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3623: the loot tables of the giant ship wrecks (<c>data/loot_tables.json</c>) — one per ship (carrier, battleship,
/// liner, terraforming ark, crystal squid) and <c>giant_cache</c> for the hidden chests. They load and name real items,
/// every chest holds something but no hoard, and they stay inside the economy: no tool, vehicle, worn gear or story item
/// for free, no machine or component whose every recipe needs a blueprint (only the two scanner parts may be found), no
/// reactor fuel, and the rare materials stay rare.
/// </summary>
public sealed class GiantShipLootTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    public static readonly TheoryData<string> Tables = new()
    {
        "giant_carrier", "giant_battleship", "giant_liner", "giant_ark", "giant_squid", "giant_cache",
    };

    /// <summary>Components a wreck may hold although their own recipe needs a blueprint: the scanner parts. The scanner
    /// module that takes one still needs its blueprint, so a found part only saves the crafting.</summary>
    private static readonly HashSet<string> FoundComponents = new(StringComparer.Ordinal) { "quantum_sensor", "sensor_lens" };

    private static LootTableDefinition Table(string key)
    {
        var table = Content.Value.LootTable(key);
        Assert.True(table is not null, $"the loot table '{key}' is missing from data/loot_tables.json");
        return table!;
    }

    private static List<string> ItemsOf(string key)
        => Table(key).Rolls.SelectMany(r => r.Entries).Select(e => e.Item).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>How many of <paramref name="item"/> one chest of the table holds on average (2000 chests).</summary>
    private static double Mean(string key, string item)
    {
        var table = Table(key);
        long sum = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            sum += table.Roll(seed, i => Content.Value.GetItem(i) is not null).Where(s => s.Item == item).Sum(s => s.Count);
        }

        return sum / 2000.0;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryGiantTable_Loads_AndNamesRealItems(string key)
    {
        var table = Table(key);
        Assert.NotEmpty(table.Rolls);
        foreach (var e in table.Rolls.SelectMany(r => r.Entries))
        {
            Assert.True(Content.Value.GetItem(e.Item) is not null, $"{key}: unknown item '{e.Item}'");
            Assert.True(e.Min >= 1 && e.Max >= e.Min, $"{key}: {e.Item} has the range {e.Min}..{e.Max}");
        }
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryGiantTable_HandsOutNoToolGearMachineOrStoryItemForFree(string key)
    {
        var content = Content.Value;
        foreach (string item in ItemsOf(key))
        {
            var def = content.GetItem(item)!;
            Assert.True(def.Category != ItemCategory.Tool && def.Tool is null && def.Vehicle is null,
                $"{key}: {item} is a tool or a vehicle");
            var block = def.PlacesBlock is null ? null : content.GetBlock(def.PlacesBlock);
            if (block is { Category: "machine" } || def.Category == ItemCategory.Component)
            {
                // Worn gear and machines come from blueprints; story items (discs, logs, the robot core) have no recipe.
                bool freelyMade = content.Recipes.Values.Any(r => string.IsNullOrEmpty(r.RequiredBlueprint)
                                                                  && r.Outputs.Any(o => o.Item == item));
                Assert.True(freelyMade || FoundComponents.Contains(item),
                    $"{key}: {item} needs a blueprint or is a story item — a wreck must not hand it out");
            }

            Assert.True(item != "reactor_fuel", $"{key}: reactor fuel is the big builds' one-time cost, never loot");
        }
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryGiantChest_HoldsSomething_ButNoHoard(string key)
    {
        var table = Table(key);
        long stacks = 0;
        for (ulong seed = 0; seed < 1000; seed++)
        {
            var roll = table.Roll(seed, i => Content.Value.GetItem(i) is not null);
            Assert.InRange(roll.Count, 2, 8);
            stacks += roll.Count;
        }

        Assert.InRange(stacks / 1000.0, 3.0, 6.0);
    }

    [Fact]
    public void TheRareMaterials_StayRare()
    {
        // A squid wreck holds about 16 of its own chests and 3 caches: a few Ressaw crystals and now and then a quantum
        // sensor or a diamond per wreck — generous for a rare find, never a heap.
        Assert.Contains("ressaw_crystal", ItemsOf("giant_squid"));
        Assert.InRange(Mean("giant_squid", "ressaw_crystal"), 0.05, 0.4);
        Assert.InRange(Mean("giant_cache", "ressaw_crystal"), 0.05, 0.5);
        Assert.InRange(Mean("giant_squid", "quantum_sensor"), 0.0, 0.1);
        Assert.InRange(Mean("giant_squid", "diamond"), 0.0, 0.15);
        Assert.InRange(Mean("giant_cache", "access_code"), 0.0, 0.12);
        Assert.InRange(Mean("giant_battleship", "diamond"), 0.0, 0.1);
    }
}

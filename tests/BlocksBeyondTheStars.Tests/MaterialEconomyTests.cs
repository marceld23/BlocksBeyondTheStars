// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Horizon-2 material economy (#1106–#1108): reactor fuel is the one-time build cost of the big things,
/// every metal has at least three uses across at least two stations, and the interior-decor blocks worldgen
/// builds with are craftable. All three are content guards — the numbers are data, the shape is what's pinned.
/// </summary>
public sealed class MaterialEconomyTests
{
    private readonly GameContent _c = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The metals the roadmap's H2 target names — each ore family must matter past its first use.</summary>
    private static readonly string[] Metals =
    {
        "aluminium_ingot", "tin_ingot", "nickel_ingot", "cobalt_ingot", "platinum_ingot", "lead_ingot", "zinc_ingot",
        "tungsten_ingot", "lithium", "neodymium", "light_alloy", "biofuel", "magnet",
    };

    /// <summary>The worldgen-only interior blocks that became craftable (#1108). <c>data_cache</c> stays loot-only.</summary>
    private static readonly string[] Decor =
    {
        "light_white", "light_red", "light_green", "strip_light_cyan", "strip_light_warm", "force_field",
        "medbay_panel", "lab_panel", "cargo_floor", "engine_panel", "engine_nozzle", "factory_terminal",
        "factory_pipe", "machine_block",
    };

    /// <summary>Every place a material is consumed, tagged with the station that consumes it: recipe inputs
    /// (their crafting station), ship craft costs (the shipyard) and module build costs (the module bay).</summary>
    private Dictionary<string, List<(string Where, string Station)>> Uses()
    {
        var uses = new Dictionary<string, List<(string Where, string Station)>>();
        void Add(string item, string where, string station)
        {
            if (!uses.TryGetValue(item, out var list)) uses[item] = list = new List<(string Where, string Station)>();
            list.Add((where, station));
        }

        foreach (var r in _c.Recipes.Values)
        {
            foreach (var i in r.Inputs) Add(i.Item, r.Key, r.Station.ToString().ToLowerInvariant());
        }

        foreach (var s in _c.Ships.Values)
        {
            foreach (var i in s.CraftCost) Add(i.Item, "ship:" + s.Key, "shipyard");
        }

        foreach (var m in _c.ShipModules.Values)
        {
            foreach (var i in m.BuildCost) Add(i.Item, "module:" + m.Key, "module_bay");
        }

        return uses;
    }

    [Fact]
    public void EveryH2Metal_HasThreeUses_AcrossTwoStations()
    {
        var uses = Uses();
        var failures = new List<string>();
        foreach (var metal in Metals)
        {
            var list = uses.TryGetValue(metal, out var l) ? l : new List<(string Where, string Station)>();
            int stations = list.Select(u => u.Station).Distinct().Count();
            if (list.Count < 3 || stations < 2)
            {
                failures.Add($"{metal}: {list.Count} uses / {stations} stations ({string.Join(", ", list.Select(u => u.Where))})");
            }
        }

        Assert.True(failures.Count == 0, "H2 metals short of 3 uses across 2 stations:\n" + string.Join("\n", failures));
    }

    // ---------------------------------------------------------------- #1200: the generalised economy guard

    /// <summary>Everything an ore block drops (the raw resources a drill brings home).</summary>
    private HashSet<string> OreDrops()
        => _c.Blocks.Values.Where(b => b.Category == "ore").SelectMany(b => b.Drops).Select(d => d.Item).ToHashSet();

    /// <summary>Items a smelt produces: a recipe with ONE input that is an <c>*_ore</c> drop, at a real crafting
    /// station (the market barters ore away, the factory's raw-ore tier is the bulk shortcut — neither is a smelt),
    /// whose output is a material/component rather than a placeable or a consumable. That is the ingot/plate/wire
    /// set plus lithium, uranium, neodymium, sulfur and diamond — not carbon composite, seeds or a ladder.</summary>
    private HashSet<string> Smeltables()
    {
        var ores = OreDrops();
        return _c.Recipes.Values
            .Where(r => r.Station is not (CraftingStation.Market or CraftingStation.Factory)
                && r.Inputs.Count == 1 && r.Inputs[0].Item.EndsWith("_ore", System.StringComparison.Ordinal)
                && ores.Contains(r.Inputs[0].Item))
            .SelectMany(r => r.Outputs).Select(o => o.Item)
            .Where(o => !ores.Contains(o) && _c.GetItem(o) is { } item
                && item.Category is ItemCategory.Material or ItemCategory.Component
                && string.IsNullOrEmpty(item.PlacesBlock))
            .ToHashSet();
    }

    private static string Describe(List<(string Where, string Station)> list)
        => $"{list.Count} uses / {list.Select(u => u.Station).Distinct().Count()} stations ({string.Join(", ", list.Select(u => u.Where))})";

    /// <summary>No ore is a dead end: every raw drop feeds at least two recipes at two different stations, so a
    /// vein is never "mine it for the one smelt and forget it". Before #1200 eleven ores fed only their own smelt.</summary>
    [Fact]
    public void EveryOreDrop_HasTwoConsumers_AcrossTwoStations()
    {
        var uses = Uses();
        var failures = new List<string>();
        foreach (var ore in OreDrops().OrderBy(o => o))
        {
            var list = uses.TryGetValue(ore, out var l) ? l : new List<(string Where, string Station)>();
            if (list.Count < 2 || list.Select(u => u.Station).Distinct().Count() < 2)
            {
                failures.Add($"{ore}: {Describe(list)}");
            }
        }

        Assert.True(failures.Count == 0, "ore drops short of 2 consumers across 2 stations:\n" + string.Join("\n", failures));
    }

    /// <summary>Every smelted material matters past its first use: three consumers across two stations (the H2
    /// rule, now applied to the whole smelt set instead of a hand-picked list).</summary>
    [Fact]
    public void EverySmeltable_HasThreeUses_AcrossTwoStations()
    {
        var uses = Uses();
        var smeltables = Smeltables();
        Assert.True(smeltables.Count >= 15, "the smelt set looks wrong: " + string.Join(", ", smeltables.OrderBy(s => s)));

        var failures = new List<string>();
        foreach (var item in smeltables.OrderBy(s => s))
        {
            var list = uses.TryGetValue(item, out var l) ? l : new List<(string Where, string Station)>();
            if (list.Count < 3 || list.Select(u => u.Station).Distinct().Count() < 2)
            {
                failures.Add($"{item}: {Describe(list)}");
            }
        }

        Assert.True(failures.Count == 0, "smeltables short of 3 uses across 2 stations:\n" + string.Join("\n", failures));
    }

    /// <summary>A station with fewer than three recipes is scenery. The hand and the market are exempt (free
    /// crafting / barter have their own rules). The campfire, algae tank and detoxifier were the last one-recipe
    /// stations until #1203 filled them — no station is exempt any more.</summary>
    [Fact]
    public void EveryCraftingStation_HasThreeRecipes()
    {
        var counts = _c.Recipes.Values.GroupBy(r => r.Station).ToDictionary(g => g.Key, g => g.Count());
        var failures = new List<string>();
        foreach (var station in System.Enum.GetValues<CraftingStation>())
        {
            if (station is CraftingStation.Hand or CraftingStation.Market)
            {
                continue;
            }

            int count = counts.TryGetValue(station, out var c) ? c : 0;
            const int minimum = 3;
            if (count < minimum)
            {
                failures.Add($"{station}: {count} recipes (wants {minimum})");
            }
        }

        Assert.True(failures.Count == 0, "stations short of recipes:\n" + string.Join("\n", failures));
    }

    /// <summary>Every material is consumed somewhere, and the only components nobody consumes are the END products
    /// on this list (worn gear, the ship's own gadgets, keys). A new material or component that lands here without a
    /// sink is a design gap, not a whitelist candidate.</summary>
    [Fact]
    public void EveryMaterial_IsConsumed_AndOnlyEndProductComponentsAreNot()
    {
        var uses = Uses();
        foreach (var bp in _c.Blueprints.Values)
        {
            foreach (var i in bp.UnlockCost)
            {
                uses.TryAdd(i.Item, new List<(string Where, string Station)>());
            }
        }

        var deadMaterials = _c.Items.Values
            .Where(i => i.Category == ItemCategory.Material && !uses.ContainsKey(i.Key))
            .Select(i => i.Key).OrderBy(k => k).ToList();
        Assert.True(deadMaterials.Count == 0, "materials nobody consumes: " + string.Join(", ", deadMaterials));

        var endProducts = new[]
        {
            "ai_memory_fragment", "access_code", "suit_teleporter", "oxygen_extractor", "stealth_suit", "armor_chest",
            "armor_legs", "helmet", "oxygen_tank_3", "suit_liner_3", "suit_lamp", "jetpack", "radar_scanner", "galaxy_radio",
        };
        var unconsumedComponents = _c.Items.Values
            .Where(i => i.Category == ItemCategory.Component && !uses.ContainsKey(i.Key))
            .Select(i => i.Key).OrderBy(k => k).ToList();
        Assert.Equal(endProducts.OrderBy(k => k), unconsumedComponents);
    }

    [Fact]
    public void RefineryVariants_OutYieldTheWorkshop_AndTheWorkshopFallbackStays()
    {
        // (refinery recipe, output) — the same alloy at the workshop must yield strictly less per craft of the
        // same metal input, and the workshop recipe must still exist (nothing gets gated behind the refinery).
        foreach (var (key, item, metal) in new[]
                 {
                     ("refine_bronze", "bronze", "tin_ingot"), ("refine_brass", "brass", "zinc_ingot"),
                     ("refine_steel", "steel", "nickel_ingot"), ("magnet_sintered", "magnet", "neodymium"),
                     ("refine_light_alloy", "light_alloy", "aluminium_ingot"),
                 })
        {
            var refined = _c.Recipes[key];
            Assert.Equal(CraftingStation.Refinery, refined.Station);
            var workshop = _c.Recipes.Values.Single(r => r.Station == CraftingStation.Workshop
                && r.Outputs.Any(o => o.Item == item) && r.Inputs.Any(i => i.Item == metal));
            double refinedPerMetal = (double)refined.Outputs.First(o => o.Item == item).Count / refined.Inputs.First(i => i.Item == metal).Count;
            double workshopPerMetal = (double)workshop.Outputs.First(o => o.Item == item).Count / workshop.Inputs.First(i => i.Item == metal).Count;
            Assert.True(refinedPerMetal > workshopPerMetal, $"{key} must out-yield {workshop.Key} per {metal}");
        }
    }

    [Fact]
    public void ReactorFuel_IsAOneTimeBuildCost_OfAtLeastFourBigThings_AndNeverARecipeInput()
    {
        var uses = Uses();
        var sinks = uses.TryGetValue("reactor_fuel", out var l) ? l : new List<(string Where, string Station)>();
        // Capital ships and the jump generator / heavy cannon ignite their reactors once — build costs only.
        Assert.True(sinks.Count >= 4, "reactor_fuel sinks: " + string.Join(", ", sinks.Select(s => s.Where)));
        Assert.Contains(sinks, s => s.Where == "ship:thunderbolt");
        Assert.Contains(sinks, s => s.Where == "ship:hammerhead");
        Assert.Contains(sinks, s => s.Where == "ship:deathblock");
        Assert.Contains(sinks, s => s.Where == "module:jump_generator");
        // Decided (#1106, variant A): fuel is never a recurring consumable — no crafting recipe eats it.
        Assert.DoesNotContain(sinks, s => s.Station is not ("shipyard" or "module_bay"));
    }

    [Fact]
    public void DecorBlocks_HaveAnItem_ARecipe_AndDropThemselves()
    {
        foreach (var key in Decor)
        {
            var block = _c.GetBlock(key);
            Assert.NotNull(block);
            Assert.True(block!.Mineable, $"{key} must be mineable so a placed one can be taken back");
            Assert.Contains(block.Drops, d => d.Item == key);
            var item = _c.GetItem(key);
            Assert.True(item is not null && item.PlacesBlock == key, $"{key} needs an item that places it");
            Assert.Contains(_c.Recipes.Values, r => r.Outputs.Any(o => o.Item == key));
        }

        // The data cache is the deliberate exception: a loot block, never a build block.
        Assert.Null(_c.GetItem("data_cache"));
    }

    [Fact]
    public void DecorItems_AreNamedInBothLanguages()
    {
        var en = _c.CreateLocalizer(GameLocale.English);
        var de = _c.CreateLocalizer(GameLocale.German);
        foreach (var key in Decor)
        {
            Assert.True(en.Has($"item.{key}.name"), key);
            Assert.True(de.Has($"item.{key}.name"), key);
        }
    }
}

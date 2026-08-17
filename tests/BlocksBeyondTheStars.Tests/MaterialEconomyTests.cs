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

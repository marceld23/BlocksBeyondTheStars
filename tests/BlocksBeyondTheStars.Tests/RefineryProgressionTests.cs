// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Refinery is a mid-game station (its module is unlocked by the <c>refinery</c> blueprint, and
/// <c>titanium_plate</c> — gated there — is the de-facto doorway to the titanium age). It now owns the
/// smelting of the rare Tier-2 metals plus a few efficiency smelts. These tests pin the one thing that
/// must never break: nothing the player can build BEFORE the refinery may become gated behind it.
/// </summary>
public sealed class RefineryProgressionTests
{
    private readonly GameContent _c = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Metals whose ORE needs a Tier-2 drill (titanium_drill → titanium_plate → refinery), so they
    /// are already unreachable until the refinery exists — provably safe to smelt at the refinery.</summary>
    private static readonly string[] MovedToRefinery =
        { "cobalt_ingot", "platinum_ingot", "tungsten_ingot", "uranium", "neodymium", "carbide" };

    [Fact]
    public void Tier2MetalSmelting_LivesAtTheRefinery()
    {
        foreach (var key in MovedToRefinery)
        {
            var r = _c.Recipes[key];
            Assert.Equal(CraftingStation.Refinery, r.Station);
        }
    }

    /// <summary>The metals whose ore a basic drill CAN mine must keep a workshop smelt — they feed
    /// pre-refinery chains (gold/silver→circuit_board, nickel→steel, tin→bronze, zinc→brass, sulfur→polymer).</summary>
    [Fact]
    public void BasicDrillMetalSmelting_StaysAtTheWorkshop()
    {
        string[] mustStayWorkshop =
            { "iron_ingot", "gold_ingot", "silver_ingot", "nickel_ingot", "tin_ingot", "zinc_ingot", "lead_ingot",
              "aluminium_ingot", "lithium", "sulfur", "steel", "bronze", "brass", "circuit_board" };
        foreach (var key in mustStayWorkshop)
        {
            Assert.Contains(_c.Recipes.Values, r => r.Key == key && r.Station == CraftingStation.Workshop);
        }
    }

    [Fact]
    public void EfficiencySmelts_BeatTheirWorkshopYield_AndKeepTheWorkshopFallback()
    {
        // (refinery recipe key, base ore, item) — each must out-yield the workshop smelt per ore unit,
        // while the workshop recipe for the same item still exists (so nothing is gated).
        var cases = new[]
        {
            ("refine_iron", "iron_ore", "iron_ingot"),
            ("refine_copper", "copper_ore", "copper_wire"),
            ("refine_gold", "gold_ore", "gold_ingot"),
            ("refine_silver", "silver_ore", "silver_ingot"),
        };

        foreach (var (key, ore, item) in cases)
        {
            var refined = _c.Recipes[key];
            Assert.Equal(CraftingStation.Refinery, refined.Station);

            double refinedYield = PerOreYield(refined, ore, item);
            var workshop = _c.Recipes.Values.First(r => r.Station == CraftingStation.Workshop
                && r.Inputs.Any(i => i.Item == ore) && r.Outputs.Any(o => o.Item == item));
            double workshopYield = PerOreYield(workshop, ore, item);

            Assert.True(refinedYield > workshopYield,
                $"{key}: refinery yield {refinedYield} must beat workshop {workshopYield}");
            // The workshop fallback for this metal must remain.
            Assert.Contains(_c.Recipes.Values, r => r.Key == workshop.Key && r.Station == CraftingStation.Workshop);
        }
    }

    private static double PerOreYield(RecipeDefinition r, string ore, string item)
        => (double)r.Outputs.First(o => o.Item == item).Count / r.Inputs.First(i => i.Item == ore).Count;

    /// <summary>
    /// THE ordering guard. Everything craftable from basic-drill ore via Hand + Workshop alone (no refinery,
    /// market or transmuter) is the "pre-refinery" reachable set. Every item the player is meant to build
    /// before unlocking the refinery must stay in it — so moving a recipe to the refinery can never strand a
    /// pre-refinery item. This is what the generic obtainability tests do NOT check (they ignore stations).
    /// </summary>
    [Fact]
    public void NothingPreRefinery_IsGatedBehindTheRefinery()
    {
        var available = new HashSet<string> { "creature_meat", "toxic_gland", "basic_drill", "hand_scanner", "scrap_pistol" };

        // Base materials: drops of every block a basic drill (Tier <= 1) can already mine — this excludes the
        // Tier-2 ores (titanium, cobalt, uranium, platinum, tungsten, neodymium) by construction.
        foreach (var b in _c.Blocks.Values.Where(b => b.MinToolTier <= 1))
        {
            foreach (var d in b.Drops) available.Add(d.Item);
        }

        // Fixpoint over Hand + Workshop recipes only (the stations available before the refinery).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var r in _c.Recipes.Values.Where(r => r.Station is CraftingStation.Hand or CraftingStation.Workshop))
            {
                if (r.Inputs.All(i => available.Contains(i.Item)))
                {
                    foreach (var o in r.Outputs)
                    {
                        changed |= available.Add(o.Item);
                    }
                }
            }
        }

        // Items the player must be able to craft before the refinery (no titanium in their chain).
        string[] mustBePreRefinery =
        {
            // base industrial chain
            "iron_ingot", "iron_plate", "copper_wire", "cable", "carbon_composite", "glass", "energy_cell_1", "metal_panel",
            // alloys + feedstock for pre-refinery gear
            "steel", "bronze", "brass", "circuit_board", "bronze_gear", "brass_fitting",
            "gold_ingot", "silver_ingot", "nickel_ingot", "tin_ingot", "zinc_ingot",
            // gear / buildables reachable before the refinery
            "machete", "gauss_pistol", "armor_chest", "armor_legs", "helmet", "oxygen_tank_1", "suit_lamp",
            "comm_radio", "camera", "detoxifier", "workbench", "base_core", "station_core", "station_vendor",
        };

        var stranded = mustBePreRefinery.Where(k => !available.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(stranded.Count == 0,
            "pre-refinery items stranded behind the refinery (broken progression order): " + string.Join(", ", stranded));
    }
}

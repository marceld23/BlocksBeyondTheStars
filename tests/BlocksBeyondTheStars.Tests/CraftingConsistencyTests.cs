using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Task 5 crafting/materials integrity: the recipe/material/tech graph must have no broken references, no
/// unobtainable inputs and no dead-end intermediates, every planet must be able to reach the key `cable`
/// component, and the new metals must mine + smelt + get used.
/// </summary>
public sealed class CraftingConsistencyTests
{
    private readonly GameContent _c = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>Items a player can come by: block drops + recipe outputs + a few creature drops.</summary>
    private HashSet<string> Obtainable()
    {
        var set = new HashSet<string> { "creature_meat", "toxic_gland" }; // fauna drops (procedural species)
        foreach (var b in _c.Blocks.Values)
        {
            foreach (var d in b.Drops) set.Add(d.Item);
        }

        foreach (var r in _c.Recipes.Values)
        {
            foreach (var o in r.Outputs) set.Add(o.Item);
        }

        return set;
    }

    /// <summary>Items consumed somewhere: recipe inputs + blueprint/module/ship build costs.</summary>
    private HashSet<string> Consumed()
    {
        var set = new HashSet<string>();
        foreach (var r in _c.Recipes.Values)
        {
            foreach (var i in r.Inputs) set.Add(i.Item);
        }

        foreach (var bp in _c.Blueprints.Values)
        {
            foreach (var i in bp.UnlockCost) set.Add(i.Item);
        }

        foreach (var m in _c.ShipModules.Values)
        {
            foreach (var i in m.BuildCost) set.Add(i.Item);
        }

        foreach (var s in _c.Ships.Values)
        {
            foreach (var i in s.CraftCost) set.Add(i.Item);
        }

        return set;
    }

    [Fact]
    public void EveryRecipeReference_IsARealItem()
    {
        var bad = new List<string>();
        foreach (var r in _c.Recipes.Values)
        {
            foreach (var ia in r.Inputs.Concat(r.Outputs))
            {
                if (_c.GetItem(ia.Item) is null)
                {
                    bad.Add($"{r.Key}:{ia.Item}");
                }
            }
        }

        Assert.True(bad.Count == 0, "recipe references to non-existent items: " + string.Join(", ", bad));
    }

    [Fact]
    public void EveryRecipeInput_IsObtainable()
    {
        var obtainable = Obtainable();
        var bad = _c.Recipes.Values
            .SelectMany(r => r.Inputs.Select(i => (r.Key, i.Item)))
            .Where(x => !obtainable.Contains(x.Item))
            .Select(x => $"{x.Key}:{x.Item}")
            .Distinct().ToList();

        Assert.True(bad.Count == 0, "recipe inputs with no source (not mined / dropped / crafted): " + string.Join(", ", bad));
    }

    [Fact]
    public void BlockDrops_AndItemPlacesBlock_AllResolve()
    {
        foreach (var b in _c.Blocks.Values)
        {
            foreach (var d in b.Drops)
            {
                Assert.True(_c.GetItem(d.Item) is not null, $"block {b.Key} drops missing item {d.Item}");
            }
        }

        foreach (var it in _c.Items.Values.Where(i => !string.IsNullOrEmpty(i.PlacesBlock)))
        {
            Assert.True(_c.GetBlock(it.PlacesBlock!) is not null, $"item {it.Key} places missing block {it.PlacesBlock}");
        }
    }

    [Fact]
    public void EveryPlanetWithOres_CanReachCable()
    {
        // cable = copper_wire (from copper_ore) + silicate, so a world must mine both to progress.
        foreach (var p in _c.Planets.Values.Where(p => p.Ores.Count > 0))
        {
            var ores = p.Ores.Select(o => o.Block).ToHashSet();
            Assert.True(ores.Contains("copper_ore"), $"planet has ores but no copper_ore → can't reach cable");
            Assert.True(ores.Contains("silicate"), $"planet has ores but no silicate → can't reach cable");
        }
    }

    [Fact]
    public void NewMetals_Mine_Smelt_AndGetUsed()
    {
        string[] ores =
        {
            "gold_ore", "silver_ore", "aluminium_ore", "tin_ore", "nickel_ore", "cobalt_ore", "lithium_ore",
            "uranium_ore", "platinum_ore", "lead_ore", "zinc_ore", "tungsten_ore", "sulfur_ore", "neodymium_ore",
        };
        var obtainable = Obtainable();
        foreach (var ore in ores)
        {
            Assert.True(_c.GetBlock(ore) is not null, $"{ore} should be a mineable block");
            Assert.True(obtainable.Contains(ore), $"{ore} should drop from its block");
        }

        // Every new refined material + alloy/component must be consumed somewhere (no dead-ends).
        string[] intermediates =
        {
            "gold_ingot", "silver_ingot", "aluminium_ingot", "tin_ingot", "nickel_ingot", "cobalt_ingot",
            "platinum_ingot", "lead_ingot", "zinc_ingot", "tungsten_ingot", "lithium", "uranium", "neodymium",
            "sulfur", "steel", "bronze", "brass", "circuit_board", "power_cell", "reactor_fuel", "carbide",
            "magnet", "light_alloy",
            // New tiers / functional sinks (Materialvielfalt): each must be made AND consumed somewhere.
            "diamond", "polymer", "biofuel", "bronze_gear", "brass_fitting",
        };
        var consumed = Consumed();
        var deadEnds = intermediates.Where(m => !consumed.Contains(m)).ToList();
        Assert.True(deadEnds.Count == 0, "new materials never consumed (dead-ends): " + string.Join(", ", deadEnds));

        // And each is obtainable (has a recipe producing it).
        var unbuildable = intermediates.Where(m => !obtainable.Contains(m)).ToList();
        Assert.True(unbuildable.Count == 0, "new materials with no recipe to make them: " + string.Join(", ", unbuildable));
    }

    /// <summary>
    /// Guard against dead items: every defined item must be reachable in-game — mined/dropped, crafted, a
    /// fauna drop, granted in the starter kit, or dropped by structure loot. This is what would have caught
    /// the unobtainable plant_seed / crystal_seed (defined items with no source).
    /// </summary>
    [Fact]
    public void EveryItem_IsObtainable_OrIntentionallyGranted()
    {
        // Items handed out by code rather than a recipe/drop (so legitimately not in Obtainable()):
        var granted = new HashSet<string>
        {
            "basic_drill", "block_placer", "hand_scanner", "scrap_pistol", // starter hotbar kit
            "ai_memory_fragment",                          // VEGA data-terminal structure loot
            "toxic_berries",                               // runtime poison variant of a toxic flora's berries
        };

        var obtainable = Obtainable();
        var dead = _c.Items.Keys
            .Where(k => !obtainable.Contains(k) && !granted.Contains(k))
            .OrderBy(k => k)
            .ToList();

        Assert.True(dead.Count == 0, "items with no in-game source (dead items): " + string.Join(", ", dead));
    }

    [Fact]
    public void Seeds_AreCraftable()
    {
        // plant_seed / crystal_seed were dead (placeable but unobtainable); a hand recipe now sources them.
        foreach (var seed in new[] { "plant_seed", "crystal_seed" })
        {
            Assert.Contains(_c.Recipes.Values, r => r.Outputs.Any(o => o.Item == seed));
        }
    }

    [Fact]
    public void DeadStations_AreGone()
    {
        // Lab / MachineRoom were dead (no module, no recipe); ensure no recipe references them either.
        Assert.DoesNotContain(_c.Recipes.Values, r => r.Station.ToString() is "Lab" or "MachineRoom");
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The tech-tree chaining (#1202) and the station packs it gates (#1203): eight former leaf blueprints hang off
/// a parent now, three new research nodes exist, and the campfire / algae tank / detoxifier stopped being
/// one-recipe stations. The meals are real food behind the field kitchen, the orphans (toxic berries, obsidian,
/// ancient brick, rune stone, giant-mushroom parts) all have a sink.
/// </summary>
public sealed class StationPackTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationPackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stationpack_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pack"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "pack", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------------------------------------------------------- #1202: the chain

    [Fact]
    public void EightLeaves_HangOffTheirDecidedParent()
    {
        foreach (var (child, parent) in new[]
                 {
                     ("stasis_projector", "field_medkit"), ("creature_translator", "terrain_scanner"),
                     ("radio_beacon", "comm_radio"), ("beam_block", "door_energy"), ("radar_scanner", "radar_array"),
                     ("suit_teleporter", "jump_generator"), ("oxygen_extractor", "oxygen_tank_2"),
                     ("station_mission_board", "station_container"),
                 })
        {
            var bp = _content.GetBlueprint(child);
            Assert.NotNull(bp);
            Assert.Contains(parent, bp!.Prerequisites);
            Assert.True(bp.KnowledgeCost >= _content.GetBlueprint(parent)!.KnowledgeCost, $"{child} must not be cheaper than {parent}");
        }
    }

    [Fact]
    public void ThreeNewNodes_GateTheStationPacks()
    {
        var kitchen = _content.GetBlueprint("field_kitchen")!;
        Assert.Empty(kitchen.Prerequisites); // the first thing a camper researches — no parent, cheap
        Assert.True(kitchen.KnowledgeCost <= 12);

        var bio = _content.GetBlueprint("bio_refining")!;
        Assert.Contains("detoxifier", bio.Prerequisites);
        var arch = _content.GetBlueprint("archaeology")!;
        Assert.Contains("terrain_scanner", arch.Prerequisites);

        // Each node gates at least two recipes — a blueprint that unlocks one thing is what #1202 set out to fix.
        foreach (var key in new[] { "field_kitchen", "bio_refining", "archaeology" })
        {
            int gated = _content.Recipes.Values.Count(r => r.RequiredBlueprint == key);
            Assert.True(gated >= 2, $"{key} gates {gated} recipes");
        }
    }

    // ---------------------------------------------------------------- #1203: the packs

    [Theory]
    [InlineData(CraftingStation.Campfire, 7)]
    [InlineData(CraftingStation.AlgaeTank, 4)]
    [InlineData(CraftingStation.Detoxifier, 4)]
    public void FormerOneRecipeStations_HaveAPack(CraftingStation station, int atLeast)
    {
        int count = _content.Recipes.Values.Count(r => r.Station == station);
        Assert.True(count >= atLeast, $"{station} has {count} recipes");
    }

    [Fact]
    public void Meals_AreTheBestFood_AndCookedNotForaged()
    {
        var stew = _content.GetItem("hearty_stew")!;
        var soup = _content.GetItem("algae_soup")!;
        var skewer = _content.GetItem("mushroom_skewer")!;
        foreach (var meal in new[] { stew, soup, skewer })
        {
            Assert.Equal(ItemCategory.Consumable, meal.Category);
            Assert.True(meal.ConsumeHunger > 0 && meal.ConsumeHealth > 0);
            var recipe = _content.Recipes.Values.Single(r => r.Outputs.Any(o => o.Item == meal.Key));
            Assert.Equal(CraftingStation.Campfire, recipe.Station);
            Assert.Equal("field_kitchen", recipe.RequiredBlueprint);
        }

        // The stew tops the whole food table; each meal beats the raw thing it is made of.
        Assert.Equal(stew.ConsumeHunger, _content.Items.Values.Max(i => i.ConsumeHunger));
        Assert.True(stew.ConsumeHunger > _content.GetItem("cooked_meat")!.ConsumeHunger);
        Assert.True(soup.ConsumeHunger > _content.GetItem("algae_ration")!.ConsumeHunger);
        Assert.True(skewer.ConsumeHunger > _content.GetItem("grain")!.ConsumeHunger);

        // The #1204 promise: grain is what the stew wants cooked.
        Assert.Contains(_content.Recipes["hearty_stew"].Inputs, i => i.Item == "grain");
    }

    [Fact]
    public void Orphans_AllHaveASink()
    {
        var consumed = _content.Recipes.Values.SelectMany(r => r.Inputs.Select(i => i.Item))
            .Concat(_content.Blueprints.Values.SelectMany(b => b.UnlockCost.Select(c => c.Item)))
            .ToHashSet();
        foreach (var orphan in new[] { "toxic_berries", "obsidian", "ancient_brick", "rune_stone", "mushroom_stem", "mushroom_cap" })
        {
            Assert.Contains(orphan, consumed);
        }

        // toxic berries come back as safe berries — the detoxifier's whole point — never as anything better.
        var wash = _content.Recipes["wash_berries"];
        Assert.Equal(CraftingStation.Detoxifier, wash.Station);
        Assert.Equal("berries", wash.Outputs.Single().Item);
        Assert.True(wash.Outputs.Single().Count <= wash.Inputs.First(i => i.Item == "toxic_berries").Count);
    }

    [Fact]
    public void StationVariants_OutYieldTheHandRecipes()
    {
        // ice: hand 2 → 1 water, campfire 1 → 1
        Assert.True(_content.Recipes["melt_ice"].Inputs[0].Count < _content.Recipes["water_ice"].Inputs[0].Count);
        // torches per log: campfire 6 vs hand 4 (and no fibre)
        Assert.True(_content.Recipes["torch_campfire"].Outputs[0].Count > _content.Recipes["torch"].Outputs[0].Count);
        // biofuel: algae 3 per craft vs hand 2, and without burning berries
        Assert.True(_content.Recipes["biofuel_algae"].Outputs[0].Count > _content.Recipes["biofuel"].Outputs[0].Count);
        Assert.DoesNotContain(_content.Recipes["biofuel_algae"].Inputs, i => i.Item == "berries");
    }

    /// <summary>End to end: at a campfire the stew is refused without the field kitchen, cooks once it is
    /// researched, and eating it is the biggest single hunger fill in the game.</summary>
    [Fact]
    public void Stew_NeedsTheFieldKitchen_CooksAtTheFire_AndFeeds()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Cook");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("campfire")!.NumericId);
            foreach (var input in _content.Recipes["hearty_stew"].Inputs)
            {
                p.State.Inventory.Add(input.Item, input.Count, 64);
            }

            server.Craft(p.State.PlayerId, "hearty_stew");
            Assert.Equal(0, p.State.Inventory.CountOf("hearty_stew")); // no blueprint → nothing cooked

            p.State.UnlockedBlueprints.Add("field_kitchen");
            server.Craft(p.State.PlayerId, "hearty_stew");
            Assert.Equal(1, p.State.Inventory.CountOf("hearty_stew"));
            Assert.Equal(0, p.State.Inventory.CountOf("grain")); // the grain went into the pot

            p.State.Hunger = 10f;
            server.ConsumeItem(p.State.PlayerId, "hearty_stew");
            Assert.Equal(0, p.State.Inventory.CountOf("hearty_stew"));
            Assert.True(p.State.Hunger >= 80f, $"the stew should fill the player up (hunger {p.State.Hunger})");
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// #2536 ("I want a workbench I can carry"): the simple things are made by hand, anywhere — a wooden boat and a wooden
/// ladder next to their workshop/carbon recipes, and three wooden tools (pickaxe, club, spear) from wood, plant fibre
/// and stone. No carried workbench: the hand station already is one.
/// </summary>
public sealed class HandCraftedToolsTests : IDisposable
{
    private static readonly string[] WoodenTools = { "wood_pickaxe", "wood_club", "wood_spear" };
    private static readonly HashSet<string> SimpleMaterials = new() { "wood_log", "plant_fiber", "stone" };

    private readonly string _root;
    private readonly GameContent _content;

    public HandCraftedToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_handtools_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "handtools"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "handtools", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void WoodenTools_AreHandRecipes_FromWoodFibreAndStoneOnly()
    {
        foreach (var key in WoodenTools)
        {
            var item = _content.GetItem(key);
            Assert.NotNull(item);
            Assert.NotNull(item!.Tool);
            Assert.Null(item.PlacesBlock); // items only: no new block, so the block ids do not shift
            Assert.NotNull(item.HeldModel);
            Assert.InRange(item.HeldModel!.Count, 1, HeldModelPart.MaxParts);
            Assert.All(item.HeldModel!, part => Assert.True(part.IsValid(), key + " has a part the game cannot draw"));

            var recipe = _content.Recipes[key];
            Assert.Equal(CraftingStation.Hand, recipe.Station);
            Assert.True(string.IsNullOrEmpty(recipe.RequiredBlueprint), $"'{key}' must not be blueprint-gated");
            Assert.All(recipe.Inputs, i => Assert.Contains(i.Item, SimpleMaterials));
            Assert.Contains(recipe.Outputs, o => o.Item == key && o.Count == 1);
        }
    }

    [Fact]
    public void WoodenPickaxe_IsATierOneDrill_SlowerThanTheBasicDrill()
    {
        var pick = _content.GetItem("wood_pickaxe")!.Tool!;
        var drill = _content.GetItem("basic_drill")!.Tool!;

        Assert.Equal(ToolKind.Drill, pick.Kind);
        Assert.Equal(drill.Tier, pick.Tier); // mines what the basic drill mines …
        Assert.True(pick.MiningPower < drill.MiningPower, "the wooden pickaxe must be slower than the basic drill");
        Assert.True(pick.MiningPower > 0f);
        Assert.Equal(0f, pick.EnergyPerUse); // … and never needs suit energy
    }

    [Fact]
    public void WoodenWeapons_AreMelee_BetterThanFists_WeakerThanTheMachete()
    {
        var machete = _content.GetItem("machete")!.Tool!;
        var club = _content.GetItem("wood_club")!.Tool!;
        var spear = _content.GetItem("wood_spear")!.Tool!;
        float macheteRate = machete.Damage / machete.CooldownSeconds;
        const float FistRate = MeleeRules.FistDamage / MeleeRules.FistCooldownSeconds;

        foreach (var weapon in new[] { club, spear })
        {
            Assert.Equal(ToolKind.Weapon, weapon.Kind);
            Assert.Equal(1, weapon.Tier);
            Assert.Equal(0f, weapon.EnergyPerUse);
            Assert.Equal(FxStyles.Slash, weapon.Fx?.Style); // a swing at the hand, no projectile
            Assert.True(weapon.Damage < machete.Damage, "a wooden weapon hits softer than the machete");
            Assert.True(weapon.Damage > MeleeRules.FistDamage, "a wooden weapon hits harder than a fist");
            float rate = weapon.Damage / weapon.CooldownSeconds;
            Assert.True(rate > FistRate && rate < macheteRate, $"damage per second {rate} must sit between fist and machete");
        }

        // The spear trades a slower swing for the longest reach of the early melee weapons.
        Assert.True(spear.Range > machete.Range && spear.Range > club.Range);
        Assert.True(spear.CooldownSeconds > club.CooldownSeconds);
    }

    [Fact]
    public void WoodenLadder_AndWoodenBoat_HaveHandRecipes_NextToTheOldOnes()
    {
        Assert.Equal(CraftingStation.Hand, _content.Recipes["ladder_wood"].Station);
        Assert.Equal(CraftingStation.Hand, _content.Recipes["ladder"].Station); // the carbon ladder stays
        Assert.Equal(CraftingStation.Hand, _content.Recipes["boat_wood"].Station);
        Assert.Equal(CraftingStation.Workshop, _content.Recipes["boat"].Station); // the workshop boat stays
        Assert.All(_content.Recipes["boat_wood"].Inputs, i => Assert.Contains(i.Item, SimpleMaterials));
    }

    [Fact]
    public void Boat_IsCraftedByHand_AwayFromAnyStation()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sailor");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            int boats = p.State.Inventory.CountOf("boat");
            p.State.Inventory.Add("wood_log", 10, 1024);
            p.State.Inventory.Add("plant_fiber", 6, 1024);

            // No workbench, no ship: the workshop boat is out of reach …
            server.Craft(p.State.PlayerId, "boat");
            Assert.Equal(boats, p.State.Inventory.CountOf("boat"));

            // … but the wooden one is made by hand, right here.
            server.Craft(p.State.PlayerId, "boat_wood");
            Assert.Equal(boats + 1, p.State.Inventory.CountOf("boat"));
            Assert.Equal(0, p.State.Inventory.CountOf("wood_log"));
            Assert.Equal(0, p.State.Inventory.CountOf("plant_fiber"));
        }
    }

    [Fact]
    public void WoodenTools_AreCraftedByHand_AwayFromAnyStation()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Woodworker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("wood_log", 7, 1024);
            p.State.Inventory.Add("plant_fiber", 5, 1024);
            p.State.Inventory.Add("stone", 2, 1024);

            foreach (var key in WoodenTools)
            {
                server.Craft(p.State.PlayerId, key);
                Assert.Equal(1, p.State.Inventory.CountOf(key));
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

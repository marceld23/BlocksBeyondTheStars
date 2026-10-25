// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2958: a child cut a birch and a red-crowned tree and got the one plain log and the one plain leaves — "always the
/// standard kind of all trees". The tree variants now drop items of their own that place their own block, and a recipe
/// that asks for wood names the <c>logs</c> group, so a pale log crafts everything a log does.
/// </summary>
public sealed class TreeVariantItemTests : IDisposable
{
    private static readonly (string Key, string Group)[] Variants =
    {
        ("wood_log_b", "logs"), ("giant_log", "logs"), ("tree_leaves_b", "leaves"), ("tree_leaves_c", "leaves"), ("blossom_leaves", "leaves"),
    };

    private readonly string _root;
    private readonly GameContent _content;

    public TreeVariantItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_treevariants_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp dir is not worth failing a test run over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "treevariants"));
        var config = new ServerConfig { WorldName = "treevariants", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void EveryTreeVariant_DropsItsOwnItem_ThatPlacesItAgain_InItsGroup()
    {
        foreach (var (key, group) in Variants)
        {
            var drop = Assert.Single(_content.GetBlock(key)!.Drops);
            Assert.Equal(key, drop.Item);
            var item = _content.GetItem(key);
            Assert.NotNull(item);
            Assert.Equal(key, item!.PlacesBlock);
            Assert.Equal(group, item.Group);
            Assert.False(string.IsNullOrEmpty(item.NameKey));
        }

        // The common kind leads its group: it is what a list names and what is used up first.
        Assert.Equal(new[] { "wood_log", "wood_log_b", "giant_log" }, _content.ItemGroupMembers("logs"));
        Assert.Equal("tree_leaves", _content.ItemGroupMembers("leaves")[0]);
        Assert.Equal(2, Assert.Single(_content.GetBlock("giant_log")!.Drops).Count); // #2968: two mahogany logs, the old yield
    }

    [Fact]
    public void LeavesOfEveryKind_HaveANameOfTheirOwn_AndEveryGroupIsNamed()
    {
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var l = _content.CreateLocalizer(locale);
            var names = new[] { "tree_leaves", "tree_leaves_b", "tree_leaves_c", "blossom_leaves" }
                .Select(k => l.Get(_content.GetItem(k)!.NameKey)).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            foreach (var group in new[] { "logs", "leaves" })
            {
                string key = GameContent.ItemGroupNameKey(group);
                Assert.NotEqual(key, l.Get(key));
            }
        }
    }

    [Fact]
    public void EveryCostThatTakesWood_TakesAnyLog()
    {
        var woodInputs = _content.Recipes.Values.SelectMany(r => r.Inputs).Where(i => i.Item == "wood_log").ToList();
        Assert.True(woodInputs.Count >= 18, $"the wood recipes ({woodInputs.Count})");
        Assert.All(woodInputs, i => Assert.Equal("logs", i.Group));
        Assert.All(_content.Blueprints.Values.SelectMany(b => b.UnlockCost).Where(c => c.Item == "wood_log"), c => Assert.Equal("logs", c.Group));
        Assert.Equal("logs", _content.Wishes.Single(w => w.Key == "firewood").Group);
    }

    [Fact]
    public void Validation_RejectsAnUnknownGroup_AndARepresentativeOutsideItsGroup()
    {
        static GameContent Build(ItemAmount input) => new(
            blocks: Array.Empty<BlockDefinition>(),
            items: new[]
            {
                new ItemDefinition { Key = "log", Group = "logs" }, new ItemDefinition { Key = "pale_log", Group = "logs" },
                new ItemDefinition { Key = "stone" }, new ItemDefinition { Key = "plank" },
            },
            recipes: new[] { new RecipeDefinition { Key = "planks", Inputs = { input }, Outputs = { new ItemAmount("plank", 1) } } },
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>());

        Build(new ItemAmount("log", 2) { Group = "logs" }).Validate();

        var unknown = Assert.Throws<ContentValidationException>(() => Build(new ItemAmount("log", 2) { Group = "planks" }).Validate());
        Assert.Contains(unknown.Problems, p => p.Contains("planks") && p.Contains("unknown item group"));

        var outsider = Assert.Throws<ContentValidationException>(() => Build(new ItemAmount("stone", 2) { Group = "logs" }).Validate());
        Assert.Contains(outsider.Problems, p => p.Contains("'stone'") && p.Contains("not a member"));

        // A log and "any log" in one recipe would both count the same stack.
        var twice = Assert.Throws<ContentValidationException>(() => new GameContent(
            blocks: Array.Empty<BlockDefinition>(),
            items: new[] { new ItemDefinition { Key = "log", Group = "logs" }, new ItemDefinition { Key = "plank" } },
            recipes: new[]
            {
                new RecipeDefinition
                {
                    Key = "planks",
                    Inputs = { new ItemAmount("log", 1), new ItemAmount("log", 2) { Group = "logs" } },
                    Outputs = { new ItemAmount("plank", 1) },
                },
            },
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>()).Validate());
        Assert.Contains(twice.Problems, p => p.Contains("'log'") && p.Contains("two ingredients"));
    }

    /// <summary>The reported case: a pale log crafts what a log crafts — alone or mixed — and the common logs go first,
    /// so the pale ones a child cut to build with are the ones that stay.</summary>
    [Fact]
    public void PaleLogs_CraftWhatLogsCraft_AndTheCommonLogsAreUsedFirst()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Woodworker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            int doors = p.State.Inventory.CountOf("door_wood");
            Assert.Equal(4, _content.Recipes["door_wood"].Inputs.Single().Count);

            void DropAllLogs()
            {
                foreach (var log in new[] { "wood_log", "wood_log_b" })
                {
                    p.State.Inventory.Remove(log, p.State.Inventory.CountOf(log)); // the starter pack, a craft achievement's reward
                }
            }

            DropAllLogs();
            p.State.Inventory.Add("wood_log", 3, 1024);
            p.State.Inventory.Add("wood_log_b", 3, 1024);
            server.Craft(p.State.PlayerId, "door_wood");
            Assert.Equal(doors + 1, p.State.Inventory.CountOf("door_wood"));
            Assert.Equal(2, p.State.Inventory.CountOf("wood_log_b")); // three common logs, then one pale one

            DropAllLogs();
            p.State.Inventory.Add("wood_log_b", 4, 1024);
            server.Craft(p.State.PlayerId, "door_wood");
            Assert.Equal(doors + 2, p.State.Inventory.CountOf("door_wood"));
            Assert.Equal(0, p.State.Inventory.CountOf("wood_log_b"));
        }
    }

    /// <summary>The material pool behind crafting, research and villager wishes: the field kitchen's research and the
    /// firewood wish take pale logs, and a plain cost still takes only its own item.</summary>
    [Fact]
    public void ThePool_CountsAndTakesAGroupIngredient_FromAnyMember()
    {
        var player = new PlayerState { PlayerId = "p" };
        player.Inventory.Add("plant_fiber", 4, 1024);
        player.Inventory.Add("berries", 2, 1024);
        player.Inventory.Add("wood_log_b", 2, 1024);
        var pool = new MaterialPool(_content, player, new ShipState());

        var kitchen = _content.Blueprints["field_kitchen"].UnlockCost;
        Assert.True(pool.Has(kitchen), "the field kitchen researches with pale logs");
        Assert.False(pool.Has(new[] { new ItemAmount("wood_log", 2) }), "a plain cost of logs is still exactly logs");

        var firewood = new MissionObjective { Type = MissionObjectiveType.Deliver, Target = "wood_log", TargetGroup = "logs", Required = 6 };
        player.Inventory.Add("wood_log", 4, 1024);
        Assert.Equal(6, pool.Count(firewood.AsItemAmount()));

        Assert.True(pool.CanFitAfterRemoving(new[] { firewood.AsItemAmount() }, new[] { new ItemAmount("cooked_meat", 1) }));
        pool.Remove(new[] { firewood.AsItemAmount() });
        Assert.Equal(0, player.Inventory.CountOf("wood_log"));
        Assert.Equal(0, player.Inventory.CountOf("wood_log_b"));
    }

    /// <summary>The tutorial's "trees near you" sees a birch: its trunk drops the pale log, which is wood.</summary>
    [Fact]
    public void TheTutorialsTreeBlocks_IncludeThePaleTrunk()
    {
        var trees = new TutorialSystem(null!, _content).TreeBlocks;
        Assert.Contains(_content.GetBlock("wood_log_b")!.NumericId.Value, trees);
        Assert.Contains(_content.GetBlock("wood_log")!.NumericId.Value, trees);
        Assert.Contains(_content.GetBlock("giant_log")!.NumericId.Value, trees);
    }
}

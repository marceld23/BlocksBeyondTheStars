// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public class ContentTests
{
    private static GameContent Load() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void ShippedContent_LoadsAndValidates()
    {
        // Throws ContentValidationException if any cross-reference is broken.
        var content = Load();

        Assert.NotEmpty(content.Blocks);
        Assert.NotEmpty(content.Items);
        Assert.NotEmpty(content.Recipes);
        Assert.NotEmpty(content.Blueprints);
        Assert.NotEmpty(content.ShipModules);
    }

    [Fact]
    public void CraftTab_OnlyNamesTheMachinesTab_AndOnlyForPlaceableDevices()
    {
        // #1273: the Machines crafting tab is an explicit per-item field, not BlockDefinition.Category (which
        // doubles as the airtight/settler rule). Every tagged item must place a block that is a device.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var tagged = content.Items.Values.Where(i => i.CraftTab != null).ToList();
        Assert.NotEmpty(tagged);
        foreach (var item in tagged)
        {
            Assert.Equal("machines", item.CraftTab);
            Assert.False(string.IsNullOrEmpty(item.PlacesBlock), $"{item.Key}: a Machines-tab item must place a block");
            var block = content.GetBlock(item.PlacesBlock!);
            Assert.NotNull(block);
            Assert.Contains(block!.Category, new[] { "machine", "door", "light" });
        }

        // The everyday stations sit in the tab; the decorative factory housings (#1108/#1265) do not.
        foreach (var key in new[] { "workbench", "forge", "heal_tank", "base_core", "beam_block" })
        {
            Assert.Equal("machines", content.GetItem(key)!.CraftTab);
        }

        foreach (var key in new[] { "factory_terminal", "machine_block", "factory_pipe", "bed", "campfire", "stone" })
        {
            Assert.Null(content.GetItem(key)!.CraftTab);
        }
    }

    [Fact]
    public void ClearGlass_IsAnAirtightTintableBuildingBlock_BehindABlueprint()
    {
        // #1274: the deliberate exception to "glass is frosted" — a second, rarer block, not a shader toggle
        // on the old one. Same building rules (airtight, dyeable, not shapeable), gated by its own blueprint.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var block = content.GetBlock("glass_clear");
        Assert.NotNull(block);
        Assert.Equal("building", block!.Category);
        Assert.True(block.Airtight);
        Assert.True(block.Tintable);
        Assert.Equal("glass_clear", content.GetItem("glass_clear")!.PlacesBlock);
        var recipe = content.GetRecipe("glass_clear");
        Assert.NotNull(recipe);
        Assert.Equal("glass_clear", recipe!.RequiredBlueprint);
        Assert.NotNull(content.GetBlueprint("glass_clear"));
    }

    [Fact]
    public void BlockIds_AreDeterministicAndAirIsZero()
    {
        var a = Load();
        var b = Load();

        foreach (var key in a.Blocks.Keys)
        {
            Assert.Equal(a.GetBlock(key)!.NumericId, b.GetBlock(key)!.NumericId);
        }

        // No defined block accidentally collides with air (0).
        foreach (var block in a.Blocks.Values)
        {
            Assert.NotEqual(BlockId.AirValue, block.NumericId.Value);
        }
    }

    [Fact]
    public void BulkItems_StackToAFullThousand_WhileToolsStayUnstackable()
    {
        var content = Load();

        // Blocks, ores, materials and components are the bulk group — one slot holds a full 1024.
        Assert.Equal(ItemDefinition.DefaultMaxStack, content.MaxStackOf("stone"));
        Assert.Equal(ItemDefinition.DefaultMaxStack, content.MaxStackOf("iron_ore"));
        Assert.Equal(ItemDefinition.DefaultMaxStack, content.MaxStackOf("iron_plate"));

        // Tools/equipment stay one-per-slot, and the deliberately scarce goods keep their small caps.
        Assert.Equal(1, content.MaxStackOf("basic_drill"));
        Assert.Equal(20, content.MaxStackOf("medpack"));
    }

    [Fact]
    public void BlockById_RoundTrips()
    {
        var content = Load();
        var stone = content.GetBlock("stone")!;
        Assert.Equal("stone", content.BlockById(stone.NumericId)!.Key);
    }

    [Fact]
    public void Localization_ResolvesBothLanguages()
    {
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);

        Assert.Equal("Iron Ore", en.Get("item.iron_ore.name"));
        Assert.Equal("Eisenerz", de.Get("item.iron_ore.name"));
    }

    [Fact]
    public void Localization_FallsBackToEnglish_WhenKeyMissingInGerman()
    {
        // A key only present in English should fall back rather than show the raw key.
        var content = Load();
        var de = content.CreateLocalizer(GameLocale.German);

        // Every English name key should resolve to *something* (its own value or fallback),
        // never the unknown-key bracket form.
        var en = content.CreateLocalizer(GameLocale.English);
        foreach (var block in content.Blocks.Values)
        {
            Assert.False(de.Get(block.NameKey).StartsWith("["),
                $"German localizer returned unresolved key for {block.NameKey}");
            Assert.False(en.Get(block.NameKey).StartsWith("["),
                $"English localizer returned unresolved key for {block.NameKey}");
        }
    }

    [Fact]
    public void BlockCategories_HaveLocalizedSectionTitles()
    {
        // The editor palettes group blocks by BlockDefinition.Category and title each section via
        // ui.cat.<category> — a typo'd or new category without locale entries would show a raw slug.
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);

        foreach (var block in content.Blocks.Values)
        {
            if (block.Key == "air")
            {
                continue;
            }

            Assert.False(string.IsNullOrEmpty(block.Category), $"block '{block.Key}' has no category");
            string key = "ui.cat." + block.Category;
            Assert.True(en.Has(key), $"missing '{key}' in en locale (block '{block.Key}')");
            Assert.True(de.Has(key), $"missing '{key}' in de locale (block '{block.Key}')");
        }
    }

    [Fact]
    public void EditorOptionValues_HaveLocalizedLabels()
    {
        // The in-game material/item editors offer the enum members as pickers and label each one via
        // ui.opt.<set>.<value>. A new enum member without locale entries would show its raw identifier
        // (and the station list once drifted from the enum badly enough to break content loading, #508).
        var content = Load();
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);

        static string Camel(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

        void CheckSet(string set, IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                string key = $"ui.opt.{set}.{value}";
                Assert.True(en.Has(key), $"missing '{key}' in en locale");
                Assert.True(de.Has(key), $"missing '{key}' in de locale");
            }
        }

        CheckSet("category", Enum.GetNames<ItemCategory>().Select(Camel));
        CheckSet("tool", Enum.GetNames<ToolKind>().Select(Camel));
        CheckSet("station", Enum.GetNames<CraftingStation>().Select(Camel));
        CheckSet("blocktool", new[] { "none", "drill" });
        CheckSet("worldtype", new[] { "any", "airless", "atmosphere", "single_biome", "multi_biome" });
    }

    [Fact]
    public void EditorStationOptions_MatchWhatTheDataUses()
    {
        // Every station spelled in recipes.json must be a real enum member — the editor derives its picker
        // from the same enum, so this pins both ends of the round trip.
        var content = Load();

        foreach (var recipe in content.Recipes.Values)
        {
            Assert.True(Enum.IsDefined(typeof(CraftingStation), recipe.Station),
                $"recipe '{recipe.Key}' has an undefined station");
        }
    }

    [Fact]
    public void Validation_DetectsBrokenReference()
    {
        var ex = Assert.Throws<ContentValidationException>(() =>
        {
            var content = new GameContent(
                blocks: new[]
                {
                    new BlocksBeyondTheStars.Shared.Definitions.BlockDefinition
                    {
                        Key = "stone",
                        Drops = { new BlocksBeyondTheStars.Shared.Definitions.ItemAmount("nonexistent_item", 1) },
                    },
                },
                items: Array.Empty<BlocksBeyondTheStars.Shared.Definitions.ItemDefinition>(),
                recipes: Array.Empty<BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition>(),
                blueprints: Array.Empty<BlocksBeyondTheStars.Shared.Definitions.BlueprintDefinition>(),
                shipModules: Array.Empty<BlocksBeyondTheStars.Shared.Definitions.ShipModuleDefinition>(),
                locales: new Dictionary<GameLocale, Dictionary<string, string>>());
            content.Validate();
        });

        Assert.Contains(ex.Problems, p => p.Contains("nonexistent_item"));
    }

    /// <summary>#1048: recipe amounts below 1 are a data error that used to load silently (a free input, a
    /// negative output). Validation fails now, naming the recipe, the side and the item.</summary>
    [Fact]
    public void Validation_RejectsRecipeAmountsBelowOne()
    {
        BuildRecipes(new RecipeDefinition { Key = "smelt", Inputs = { new ItemAmount("iron_ore", 2) }, Outputs = { new ItemAmount("iron_ingot", 1) } }).Validate();

        var zeroInput = Assert.Throws<ContentValidationException>(() => BuildRecipes(
            new RecipeDefinition { Key = "free_lunch", Inputs = { new ItemAmount("iron_ore", 0) }, Outputs = { new ItemAmount("iron_ingot", 1) } }).Validate());
        Assert.Contains(zeroInput.Problems, p => p.Contains("free_lunch") && p.Contains("input") && p.Contains("iron_ore") && p.Contains("amount 0"));

        var negativeOutput = Assert.Throws<ContentValidationException>(() => BuildRecipes(
            new RecipeDefinition { Key = "sink", Inputs = { new ItemAmount("iron_ore", 1) }, Outputs = { new ItemAmount("iron_ingot", -1) } }).Validate());
        Assert.Contains(negativeOutput.Problems, p => p.Contains("sink") && p.Contains("output") && p.Contains("iron_ingot") && p.Contains("amount -1"));
    }

    /// <summary>#1048: a recipe that consumes what it produces (a free loop, or a no-op) is rejected.</summary>
    [Fact]
    public void Validation_RejectsSelfConsumingRecipe()
    {
        var ex = Assert.Throws<ContentValidationException>(() => BuildRecipes(
            new RecipeDefinition { Key = "loop", Inputs = { new ItemAmount("iron_ingot", 1) }, Outputs = { new ItemAmount("iron_ingot", 2) } }).Validate());
        Assert.Contains(ex.Problems, p => p.Contains("loop") && p.Contains("iron_ingot") && p.Contains("both input and output"));
    }

    /// <summary>The amount checks share the existing item-lookup loops, so an unknown recipe item is still
    /// reported exactly once, not once per check.</summary>
    [Fact]
    public void Validation_ReportsUnknownRecipeItemOnce()
    {
        var ex = Assert.Throws<ContentValidationException>(() => BuildRecipes(
            new RecipeDefinition { Key = "ghost", Inputs = { new ItemAmount("unobtainium", 1) }, Outputs = { new ItemAmount("iron_ingot", 1) } }).Validate());
        Assert.Single(ex.Problems, p => p.Contains("unobtainium"));
    }

    private static GameContent BuildRecipes(params RecipeDefinition[] recipes) => new(
        blocks: Array.Empty<BlockDefinition>(),
        items: new[] { new ItemDefinition { Key = "iron_ore" }, new ItemDefinition { Key = "iron_ingot" } },
        recipes: recipes,
        blueprints: Array.Empty<BlueprintDefinition>(),
        shipModules: Array.Empty<ShipModuleDefinition>(),
        locales: new Dictionary<GameLocale, Dictionary<string, string>>());

    /// <summary>#427: an unknown block id in an authored ship layout used to become hull silently at stamp
    /// time; the validator now fails the load, while the editor's special palette ids (hatch, doors, lights,
    /// engine, glass) and station markers stay legal.</summary>
    [Fact]
    public void Validation_DetectsUnknownShipLayoutCell()
    {
        static GameContent Build(params ShipLayoutCell[] cells) => new(
            blocks: new[] { new BlockDefinition { Key = "iron_wall" } },
            items: Array.Empty<ItemDefinition>(),
            recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(),
            shipLayouts: new[] { new ShipLayout { Key = "ship_test", Width = 3, Height = 3, Length = 3, Cells = cells.ToList() } });

        // Legal: a real block, every element id and a station marker.
        Build(
            new ShipLayoutCell { Id = "iron_wall" },
            new ShipLayoutCell { Id = "hatch" },
            new ShipLayoutCell { Id = "door_slide", Kind = "element" },
            new ShipLayoutCell { Id = "light_red" },
            new ShipLayoutCell { Id = "engine" },
            new ShipLayoutCell { Id = "cockpit", Kind = "station" }).Validate();

        var ex = Assert.Throws<ContentValidationException>(() =>
            Build(new ShipLayoutCell { X = 1, Y = 2, Z = 0, Id = "iron_wal" }).Validate());
        Assert.Contains(ex.Problems, p => p.Contains("ship_test") && p.Contains("iron_wal") && p.Contains("(1,2,0)"));
    }

    /// <summary>The browser client parses locale files it fetched over HTTP with this entry point, before
    /// its content cache exists, so the splash/intro screens can localize (#831). It must read a real
    /// shipped locale file exactly like the filesystem path does.</summary>
    [Fact]
    public void ParseLocaleTable_ReadsAShippedLocaleFile()
    {
        var path = Path.Combine(TestPaths.DataDir(), "locales", "en.json");
        var table = ContentLoader.ParseLocaleTable(File.ReadAllText(path));

        Assert.NotEmpty(table);
        Assert.Equal(Load().CreateLocalizer(GameLocale.English).Get("ui.splash.tagline"), table["ui.splash.tagline"]);
    }

    [Fact]
    public void ParseLocaleTable_EmptyObjectYieldsEmptyTable()
    {
        Assert.Empty(ContentLoader.ParseLocaleTable("{}"));
    }

    [Fact]
    public void Validation_RejectsInvalidItemMaxStack()
    {
        var ex = Assert.Throws<ContentValidationException>(() => new GameContent(
            blocks: Array.Empty<BlockDefinition>(),
            items: new[] { new ItemDefinition { Key = "broken_crate", MaxStack = 0 } },
            recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>()).Validate());

        Assert.Contains(ex.Problems, p => p.Contains("broken_crate") && p.Contains("MaxStack"));
    }

    [Fact]
    public void Validation_RejectsPlanetWithAirOrEmptySurfaceBlock()
    {
        var ex = Assert.Throws<ContentValidationException>(() => new GameContent(
            blocks: new[] { new BlockDefinition { Key = "air" }, new BlockDefinition { Key = "stone" } },
            items: Array.Empty<ItemDefinition>(),
            recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(),
            planets: new[] { new PlanetType { Key = "void_world", SurfaceBlock = "air" } }).Validate());

        Assert.Contains(ex.Problems, p => p.Contains("void_world") && p.Contains("surface block"));
    }

    [Fact]
    public void Validation_RejectsBlockCountExceedingAtlasLimit()
    {
        var blocks = Enumerable.Range(1, 260)
            .Select(i => new BlockDefinition { Key = $"block_{i}" })
            .ToList();

        var ex = Assert.Throws<ContentValidationException>(() => new GameContent(
            blocks: blocks,
            items: Array.Empty<ItemDefinition>(),
            recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(),
            shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>()).Validate());

        Assert.Contains(ex.Problems, p => p.Contains("256") || p.Contains("atlas"));
    }
}

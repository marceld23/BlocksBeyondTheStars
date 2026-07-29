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
}

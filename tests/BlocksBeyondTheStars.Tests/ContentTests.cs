// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
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

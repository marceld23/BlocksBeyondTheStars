// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The craft-depth metric behind the reachability ordering of the crafting and ship-module
/// lists (#826): raw resources are depth 0, each crafting step adds one, market barter never counts.
/// </summary>
public class CraftDepthTests
{
    private static GameContent Build(params RecipeDefinition[] recipes) => new(
        blocks: Array.Empty<BlockDefinition>(),
        items: new[]
        {
            new ItemDefinition { Key = "ore" },
            new ItemDefinition { Key = "plate" },
            new ItemDefinition { Key = "gadget" },
        },
        recipes: recipes,
        blueprints: Array.Empty<BlueprintDefinition>(),
        shipModules: Array.Empty<ShipModuleDefinition>(),
        locales: new Dictionary<GameLocale, Dictionary<string, string>>());

    private static RecipeDefinition Recipe(string key, string output, params string[] inputs) => new RecipeDefinition
    {
        Key = key,
        Outputs = { new ItemAmount(output, 1) },
    }.WithInputs(inputs);

    [Fact]
    public void RawResource_HasDepthZero_AndEachStepAddsOne()
    {
        var content = Build(
            Recipe("plate", "plate", "ore"),
            Recipe("gadget", "gadget", "plate"));

        Assert.Equal(0, content.CraftDepth("ore"));
        Assert.Equal(1, content.CraftDepth("plate"));
        Assert.Equal(2, content.CraftDepth("gadget"));
    }

    [Fact]
    public void SeveralProducers_TheShallowestRecipeWins()
    {
        // gadget can be crafted from raw ore directly OR from the deeper plate — depth is the cheap path.
        var content = Build(
            Recipe("plate", "plate", "ore"),
            Recipe("gadget_deep", "gadget", "plate"),
            Recipe("gadget_cheap", "gadget", "ore"));

        Assert.Equal(1, content.CraftDepth("gadget"));
    }

    [Fact]
    public void MarketBarter_DoesNotCountAsProduction()
    {
        var market = Recipe("barter", "plate", "ore");
        market.Station = CraftingStation.Market;
        var content = Build(market);

        Assert.Equal(0, content.CraftDepth("plate"));
    }

    [Fact]
    public void RecipeCycle_StaysFiniteAndDeterministic()
    {
        var content = Build(
            Recipe("a", "plate", "gadget"),
            Recipe("b", "gadget", "plate"));

        // A loop must not hang or overflow; both sides resolve to a small finite depth.
        Assert.InRange(content.CraftDepth("plate"), 1, 2);
        Assert.InRange(content.CraftDepth("gadget"), 1, 2);
    }

    [Fact]
    public void MaxInputDepth_TakesTheDeepestIngredient_AndZeroForEmptyCost()
    {
        var content = Build(
            Recipe("plate", "plate", "ore"),
            Recipe("gadget", "gadget", "plate"));

        Assert.Equal(2, content.MaxInputDepth(new[] { new ItemAmount("ore", 5), new ItemAmount("gadget", 1) }));
        Assert.Equal(0, content.MaxInputDepth(Array.Empty<ItemAmount>()));
    }

    [Fact]
    public void ShippedContent_EveryCraftedOutputHasDepthAtLeastOne()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

        foreach (var recipe in content.Recipes.Values.Where(r => r.Station != CraftingStation.Market))
        {
            foreach (var output in recipe.Outputs)
            {
                Assert.True(content.CraftDepth(output.Item) >= 1,
                    $"'{output.Item}' is produced by recipe '{recipe.Key}' but has craft depth 0");
            }
        }
    }
}

internal static class RecipeDefinitionTestExtensions
{
    public static RecipeDefinition WithInputs(this RecipeDefinition r, params string[] inputs)
    {
        foreach (var item in inputs)
        {
            r.Inputs.Add(new ItemAmount(item, 1));
        }

        return r;
    }
}

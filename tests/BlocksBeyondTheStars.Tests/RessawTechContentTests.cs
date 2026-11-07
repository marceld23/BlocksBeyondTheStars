// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Ressaw technology (#3337–#3340): one blueprint behind the titanium drill unlocks the four diving-suit pieces, the drill,
/// the lamp and the conduit, all made at the workbench; every new item has a source (the ore, the pumped liquid, a mined
/// block or prop, a recipe) and the raw materials have a use. The tier-3 ore needs a tier-3 drill, the Ressaw drill out-mines
/// the diamond drill, and the lamp takes the pumped liquid only as an ingredient.
/// </summary>
public sealed class RessawTechContentTests
{
    private const string Node = "ressaw_tech";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] SuitPieces = { "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots" };
    private static readonly string[] Crafted = SuitPieces.Concat(new[] { "ressaw_drill", "ressaw_lamp", "ressaw_conduit" }).ToArray();

    private static bool Uses(RecipeDefinition r, string item) => r.Inputs.Any(i => i.Item == item);

    [Fact]
    public void OneBlueprint_BehindTheTitaniumDrill_UnlocksEveryRessawPiece_AtTheWorkbench()
    {
        var node = Content.GetBlueprint(Node)!;
        Assert.Contains("titanium_drill", node.Prerequisites);
        Assert.True(node.KnowledgeCost >= Content.GetBlueprint("diamond_drill")!.KnowledgeCost, "a research step like the diamond chain");
        Assert.Contains(node.UnlockCost, c => c.Item == "ressaw_crystal"); // researched from what the ruins give

        foreach (string item in Crafted)
        {
            var recipe = Assert.Single(Content.Recipes.Values, r => r.Outputs.Any(o => o.Item == item));
            Assert.Equal(Node, recipe.RequiredBlueprint);
            Assert.Equal(CraftingStation.Workshop, recipe.Station);
            Assert.True(Uses(recipe, "ressaw_crystal"), $"{item} is made from Ressaw crystal");
        }
    }

    [Fact]
    public void TheRecipes_AreTheOnesThePlayerDescribed()
    {
        foreach (string piece in SuitPieces)
        {
            var recipe = Content.GetRecipe(piece)!;
            Assert.True(Uses(recipe, "cable") && Uses(recipe, "titanium_plate"), $"{piece}: crystals + cables + titanium plates");
        }

        var helmet = Content.GetRecipe("ressaw_helmet")!;
        Assert.True(Uses(helmet, "glass") || Uses(helmet, "polymer"), "the helmet has a visor");

        var drill = Content.GetRecipe("ressaw_drill")!;
        Assert.True(Uses(drill, "cable") && Uses(drill, "titanium_plate") && drill.Inputs.Count > 3, "crystals + cables + plates + more");

        var lamp = Content.GetRecipe("ressaw_lamp")!;
        foreach (string input in new[] { "ressaw", "ressaw_crystal", "glass", "cable" })
        {
            Assert.True(Uses(lamp, input), $"the lamp takes {input}");
        }

        var conduit = Content.GetRecipe("ressaw_conduit")!;
        Assert.True(Uses(conduit, "crystal") && Uses(conduit, "glass"), "the crystal conduit's own materials");
        Assert.True(conduit.Outputs.Single(o => o.Item == "ressaw_conduit").Count > 1, "a batch, like the crystal conduit");
    }

    [Fact]
    public void TheOre_NeedsATier3Drill_AndTheRessawDrill_OutMinesTheDiamondDrill()
    {
        var ore = Content.GetBlock("ressaw_crystal_ore")!;
        Assert.Equal(3, ore.MinToolTier);
        Assert.Contains(ore.Drops, d => d.Item == "ressaw_crystal");
        Assert.False(MiningRules.ToolCanMine(Content.GetItem("titanium_drill")!.Tool!, ore));
        Assert.True(MiningRules.ToolCanMine(Content.GetItem("diamond_drill")!.Tool!, ore));

        var ressaw = Content.GetItem("ressaw_drill")!.Tool!;
        var diamond = Content.GetItem("diamond_drill")!.Tool!;
        Assert.Equal(ToolKind.Drill, ressaw.Kind);
        Assert.Equal(3, ressaw.Tier);
        Assert.True(ressaw.MiningPower > diamond.MiningPower);
        Assert.True(MiningRules.ToolCanMine(ressaw, ore));
        Assert.Equal("diamond_drill", MiningRules.CheapestToolFor(Content, ore)?.Key); // the "you need …" hint names the cheaper drill
    }

    [Fact]
    public void EveryNewItem_HasASource_AndTheRawMaterials_HaveAUse()
    {
        // Sources: a block that drops it (the ore, the pumped liquid, a mined block or prop) or a recipe.
        var dropped = Content.Blocks.Values.SelectMany(b => b.Drops).Select(d => d.Item).ToHashSet();
        var made = Content.Recipes.Values.SelectMany(r => r.Outputs).Select(o => o.Item).ToHashSet();
        string[] all =
        {
            "ressaw", "ressaw_crystal", "ressaw_block", "ressaw_lamp", "ressaw_drill", "ressaw_conduit",
            "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots", "gds_skull", "gds_diving_helmet", "gds_guardian_head",
        };
        foreach (string item in all)
        {
            Assert.True(Content.GetItem(item) is not null, $"{item} exists");
            Assert.True(dropped.Contains(item) || made.Contains(item), $"{item} has no source");
        }

        // The liquid is pumped: a liquid block with a drop is what the fluid pump takes (RessawHazardTests pumps one).
        var liquid = Content.GetBlock("ressaw")!;
        Assert.True(liquid.Liquid);
        Assert.Contains(liquid.Drops, d => d.Item == "ressaw");

        // The raw materials are used: the crystal by every recipe of the node, the liquid by the lamp.
        var consumed = Content.Recipes.Values.SelectMany(r => r.Inputs).Select(i => i.Item).ToHashSet();
        Assert.Contains("ressaw_crystal", consumed);
        Assert.Contains("ressaw", consumed);

        // An ore drop feeds two stations (MaterialEconomyTests): the researchers buy the crystal too — and nothing sells it.
        var sell = Content.GetRecipe("market_sell_ressaw_crystal")!;
        Assert.Equal(CraftingStation.Market, sell.Station);
        Assert.Equal("researchers", sell.MarketTheme);
        Assert.DoesNotContain(Content.Recipes.Values, r => r.Station == CraftingStation.Market && r.Outputs.Any(o => o.Item == "ressaw_crystal"));
    }

    /// <summary>The keys of one locale file as written (no English fallback).</summary>
    private static Dictionary<string, string> LocaleFile(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheBlueprintAndTheNewLines_AreWritten_InEnglishAndGerman(string code)
    {
        var texts = LocaleFile(code);
        var node = Content.GetBlueprint(Node)!;
        foreach (string key in new[]
        {
            node.NameKey, node.DescriptionKey, "srv.death.ressaw", "vega.hint.ressaw", "ui.equip.effect.night_sight",
            "ui.suit.active_ressaw_lamp",
        })
        {
            Assert.True(texts.TryGetValue(key, out var text) && text.Length > 0, $"{code}.json lacks '{key}'");
        }

        Assert.Contains("{key}", texts["ui.suit.active_ressaw_lamp"]);
    }
}

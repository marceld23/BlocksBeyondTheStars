// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Justus' three new sets (v2026.11.7): the guardian set with its sword and laser drill (#3290), the robber disguise
/// (#3291) and the flower set (#3292) — their look bits (<see cref="GearLook"/>), what they do
/// (<see cref="SuitEquipment"/>), the one price rule of the trader discount (<see cref="MarketPricing"/>), where their
/// materials come from, and the market rows that may never form a profitable loop (#3293), not even at the full discount.
/// </summary>
public sealed class GearSetsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] NewItems =
    {
        "guardian_plate", "guardian_helmet", "guardian_chest", "guardian_legs", "guardian_sword", "guardian_drill",
        "robber_cloth", "robber_mask", "robber_jacket", "blue_blossom", "flower_wreath", "flower_bracelets",
    };

    private static Func<string, bool> Wearing(params string[] keys) => key => keys.Contains(key);

    private static IEnumerable<ItemDefinition> Items => Content.Items.Values;

    // ---------------- The look (GearLook) ----------------

    [Fact]
    public void TheStyleBits_AreThreePerSlot_AboveEveryOlderBit()
    {
        Assert.Equal(16, GearLook.HeadStyleShift);
        Assert.Equal(19, GearLook.ChestStyleShift);
        Assert.Equal(22, GearLook.LegsStyleShift);
        int head = GearLook.Style(GearLook.HeadStyleShift, GearLook.StyleBits);
        int chest = GearLook.Style(GearLook.ChestStyleShift, GearLook.StyleBits);
        int legs = GearLook.Style(GearLook.LegsStyleShift, GearLook.StyleBits);
        Assert.Equal(0, (head | chest | legs) & 0xFFFF); // the old bits, LampLit included, stay untouched
        Assert.Equal(0, head & chest);
        Assert.Equal(0, chest & legs);
        Assert.True(legs > 0); // still a positive int
    }

    [Theory]
    [InlineData("guardian_helmet", GearLook.Helmet, GearLook.HeadStyleShift, GearLook.StyleGuardian)]
    [InlineData("robber_mask", GearLook.Helmet, GearLook.HeadStyleShift, GearLook.StyleRobber)]
    [InlineData("flower_wreath", GearLook.Helmet, GearLook.HeadStyleShift, GearLook.StyleFlower)]
    [InlineData("guardian_chest", GearLook.Chest, GearLook.ChestStyleShift, GearLook.StyleGuardian)]
    [InlineData("robber_jacket", GearLook.Chest, GearLook.ChestStyleShift, GearLook.StyleRobber)]
    [InlineData("flower_bracelets", GearLook.Chest, GearLook.ChestStyleShift, GearLook.StyleFlower)]
    [InlineData("guardian_legs", GearLook.Legs, GearLook.LegsStyleShift, GearLook.StyleGuardian)]
    public void EachSetPiece_SetsItsSlotBit_AndItsStyle(string item, int slotBit, int shift, int style)
    {
        int mask = GearLook.Mask(Wearing(item));

        Assert.Equal(slotBit | GearLook.Style(shift, style), mask); // an older client still draws a plain piece there
        Assert.Equal(style, (mask >> shift) & GearLook.StyleBits);
    }

    [Fact]
    public void MixedSets_DecodeSlotBySlot_AndTheClassicPiecesKeepStyleZero()
    {
        int mixed = GearLook.Mask(Wearing("robber_mask", "guardian_chest", "guardian_legs", "boots", "suit_lamp")) | GearLook.LampLit;
        Assert.Equal(GearLook.StyleRobber, GearLook.HeadStyle(mixed));
        Assert.Equal(GearLook.StyleGuardian, GearLook.ChestStyle(mixed));
        Assert.Equal(GearLook.StyleGuardian, GearLook.LegsStyle(mixed));
        Assert.True(GearLook.Has(mixed, GearLook.Boots | GearLook.Lamp | GearLook.LampLit));

        int other = GearLook.Mask(Wearing("flower_wreath", "robber_jacket", "titan_legs"));
        Assert.Equal(GearLook.StyleFlower, GearLook.HeadStyle(other));
        Assert.Equal(GearLook.StyleRobber, GearLook.ChestStyle(other));
        Assert.Equal(GearLook.StyleClassic, GearLook.LegsStyle(other));
        Assert.True(GearLook.Has(other, GearLook.Legs | GearLook.TitanLegs));

        int classic = GearLook.Mask(Wearing("titan_helmet", "armor_chest", "armor_legs"));
        Assert.Equal(0, GearLook.HeadStyle(classic) | GearLook.ChestStyle(classic) | GearLook.LegsStyle(classic));
        Assert.Equal(0, GearLook.Mask(Wearing())); // the minimal presence stays at Gear = 0
    }

    [Fact]
    public void AStyledMask_SurvivesTheWire()
    {
        int mask = GearLook.Mask(Wearing("guardian_helmet", "flower_bracelets", "guardian_legs"));
        var back = Assert.IsType<PlayerPresence>(NetCodec.Decode(NetCodec.Encode(new PlayerPresence { PlayerId = "a", Gear = mask })));
        Assert.Equal(mask, back.Gear);
    }

    // ---------------- What the sets do (SuitEquipment) ----------------

    [Fact]
    public void TheTraderDiscount_AddsUpTheWornPieces_AndIsCapped()
    {
        Assert.Equal(0f, SuitEquipment.TraderDiscount(Items, Wearing()));
        Assert.Equal(0.10f, SuitEquipment.TraderDiscount(Items, Wearing("flower_wreath")), 3);
        Assert.Equal(0.15f, SuitEquipment.TraderDiscount(Items, Wearing("flower_bracelets")), 3);
        Assert.Equal(0.25f, SuitEquipment.TraderDiscount(Items, Wearing("flower_wreath", "flower_bracelets")), 3);
        Assert.Equal(25, MarketPricing.ToPercent(SuitEquipment.TraderDiscount(Items, Wearing("flower_wreath", "flower_bracelets"))));

        var generous = new[]
        {
            new ItemDefinition { Key = "a", TraderDiscount = 0.2f },
            new ItemDefinition { Key = "b", TraderDiscount = 0.2f },
        };
        Assert.Equal(SuitEquipment.MaxTraderDiscount, SuitEquipment.TraderDiscount(generous, Wearing("a", "b")));
    }

    [Fact]
    public void TheDisguise_NeedsTheMaskAndTheJacket()
    {
        Assert.True(SuitEquipment.BanditDisguised(Items, Wearing("robber_mask", "robber_jacket")));
        Assert.False(SuitEquipment.BanditDisguised(Items, Wearing("robber_mask")));
        Assert.False(SuitEquipment.BanditDisguised(Items, Wearing("robber_jacket")));
        Assert.False(SuitEquipment.BanditDisguised(Items, Wearing("robber_mask", "armor_chest")));
        Assert.False(SuitEquipment.BanditDisguised(Items, Wearing()));
    }

    [Fact]
    public void TheDisguise_ProtectsNothing_AndTheGuardianSet_IsASideGradeToTitanium()
    {
        foreach (var key in new[] { "robber_mask", "robber_jacket", "flower_wreath", "flower_bracelets" })
        {
            var def = Content.GetItem(key)!;
            Assert.Equal(0f, def.ArmorResistance + def.ThermalInsulation + def.CorrosionResistance);
            Assert.True(SuitEquipment.IsSuitGear(def), key);
        }

        float guardian = SuitEquipment.ArmorResistance(Items, Wearing("guardian_helmet", "guardian_chest", "guardian_legs"));
        float titan = SuitEquipment.ArmorResistance(Items, Wearing("titan_helmet", "titan_chest", "titan_legs"));
        Assert.Equal(titan, guardian, 3);
        Assert.True(SuitEquipment.ThermalInsulation(Items, Wearing("guardian_chest")) < SuitEquipment.ThermalInsulation(Items, Wearing("titan_chest")));
        Assert.True(SuitEquipment.CorrosionResistance(Items, Wearing("guardian_chest")) > 0f);
    }

    // ---------------- The price rule (MarketPricing) ----------------

    [Theory]
    [InlineData(5, 0f, 5)]
    [InlineData(1, 0.25f, 1)]   // a one-piece trade stays one piece
    [InlineData(2, 0.25f, 1)]   // half rounds up: 0.5 off → 1 off
    [InlineData(3, 0.25f, 2)]
    [InlineData(4, 0.25f, 3)]
    [InlineData(5, 0.25f, 4)]
    [InlineData(6, 0.25f, 4)]
    [InlineData(8, 0.25f, 6)]
    [InlineData(3, 0.10f, 3)]   // 0.3 off rounds to nothing
    [InlineData(5, 0.10f, 4)]
    [InlineData(10, 0.10f, 9)]
    [InlineData(4, 0.15f, 3)]
    [InlineData(6, 0.15f, 5)]
    [InlineData(0, 0.25f, 0)]
    public void Discounted_TakesTheRoundedShareOff_NeverBelowOne(int count, float discount, int expected)
        => Assert.Equal(expected, MarketPricing.Discounted(count, discount));

    [Fact]
    public void Inputs_DiscountOnlyMarketTrades_AndKeepTheGroups()
    {
        var bed = Content.GetRecipe("market_doctor_bed")!;
        var priced = MarketPricing.Inputs(bed, 0.25f);
        var logs = priced.Single(i => i.Group == "logs");
        Assert.Equal(3, logs.Count); // 4 logs (any log) → 3
        Assert.Equal(1, priced.Single(i => i.Item == "iron_ore").Count);
        Assert.Equal(4, bed.Inputs.Single(i => i.Group == "logs").Count); // the recipe itself is untouched

        var helmet = Content.GetRecipe("helmet")!;
        Assert.Same(helmet.Inputs, MarketPricing.Inputs(helmet, 0.25f)); // a workbench recipe never gets cheaper
        Assert.Same(bed.Inputs, MarketPricing.Inputs(bed, 0f));

        Assert.Equal(25, MarketPricing.ToPercent(0.10f + 0.15f));
        Assert.Equal(0.25f, MarketPricing.FromPercent(25));
        Assert.Equal(0f, MarketPricing.FromPercent(-3));
    }

    // ---------------- No profitable loop (#3293) ----------------

    /// <summary>The net of ONE trade at <paramref name="discount"/>: what it gives minus what it takes, per item (a group
    /// ingredient counts under its own item key).</summary>
    private static Dictionary<string, double> Net(RecipeDefinition r, float discount)
    {
        var net = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var input in MarketPricing.Inputs(r, discount))
        {
            net[input.Item] = net.GetValueOrDefault(input.Item) - input.Count;
        }

        foreach (var output in r.Outputs)
        {
            net[output.Item] = net.GetValueOrDefault(output.Item) + output.Count;
        }

        return net;
    }

    /// <summary>Whether running trade A once and trade B s times (any s &gt; 0) can leave the player with no item fewer
    /// and some item more — a loop that makes goods out of nothing.</summary>
    private static bool Profitable(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        double lo = 0, hi = double.PositiveInfinity;
        foreach (string item in a.Keys.Union(b.Keys))
        {
            double ai = a.GetValueOrDefault(item), bi = b.GetValueOrDefault(item);
            if (bi > 0)
            {
                lo = Math.Max(lo, -ai / bi);
            }
            else if (bi < 0)
            {
                hi = Math.Min(hi, ai / -bi);
            }
            else if (ai < 0)
            {
                return false; // A alone takes it and B never gives it back
            }
        }

        if (lo > hi + 1e-9 || hi <= 0)
        {
            return false;
        }

        double s = double.IsPositiveInfinity(hi) ? lo + 1 : (lo + hi) / 2;
        if (s <= 0)
        {
            return false;
        }

        bool gain = false;
        foreach (string item in a.Keys.Union(b.Keys))
        {
            double v = a.GetValueOrDefault(item) + (s * b.GetValueOrDefault(item));
            if (v < -1e-9)
            {
                return false;
            }

            gain |= v > 1e-9;
        }

        return gain;
    }

    [Fact]
    public void NoTwoMarketTrades_FormAProfitableLoop_NotEvenAtTheFullTraderDiscount()
    {
        var trades = Content.Recipes.Values.Where(MarketStock.IsTrade).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        Assert.True(trades.Count > 20);
        var loops = new List<string>();
        foreach (float discount in new[] { 0f, 0.10f, 0.15f, SuitEquipment.MaxTraderDiscount })
        {
            for (int i = 0; i < trades.Count; i++)
            {
                for (int j = i + 1; j < trades.Count; j++)
                {
                    if (Profitable(Net(trades[i], discount), Net(trades[j], discount)))
                    {
                        loops.Add($"{trades[i].Key} + {trades[j].Key} at {discount:0.00}");
                    }
                }
            }
        }

        Assert.True(loops.Count == 0, "market trades that make goods out of nothing: " + string.Join(", ", loops));
    }

    [Fact]
    public void TheLoopCheck_WouldHaveCaughtTheOldCopperRows()
    {
        var sell = new RecipeDefinition { Key = "a", Station = CraftingStation.Market };
        sell.Inputs.Add(new ItemAmount("copper_ore", 3));
        sell.Outputs.Add(new ItemAmount("silicate", 4));
        var buy = new RecipeDefinition { Key = "b", Station = CraftingStation.Market };
        buy.Inputs.Add(new ItemAmount("silicate", 3));
        buy.Outputs.Add(new ItemAmount("copper_ore", 4));

        Assert.True(Profitable(Net(sell, 0f), Net(buy, 0f)));
        Assert.False(Profitable(Net(Content.GetRecipe("market_copper_to_silicate")!, 0.25f), Net(Content.GetRecipe("market_buy_copper")!, 0.25f)));
    }

    // ---------------- The sword and the laser drill (#3290) ----------------

    [Fact]
    public void TheGuardianSword_IsAPlasmaSwordWithARedHologramBlade()
    {
        var sword = Content.GetItem("guardian_sword")!.Tool!;
        var plasma = Content.GetItem("plasma_sword")!.Tool!;
        Assert.Equal(ToolKind.Weapon, sword.Kind);
        Assert.Equal(3, sword.Tier);
        Assert.Equal(70f, sword.Damage);
        Assert.Equal(plasma.Fx!.Style, sword.Fx!.Style);
        Assert.Equal("#ff2a2a", sword.Fx.Color);
        Assert.All(Content.GetItem("guardian_sword")!.HeldModel!, part => Assert.True(part.IsValid()));
    }

    [Fact]
    public void TheGuardianDrill_IsAPreciseTier3Laser_AndTheDiamondDrillStaysTheCheapestTool()
    {
        var drill = Content.GetItem("guardian_drill")!.Tool!;
        Assert.Equal(ToolKind.Drill, drill.Kind);
        Assert.Equal(3, drill.Tier);
        Assert.Equal(0, drill.MiningRadius); // one cell at a time — the mining beam clears 3×3×3
        Assert.True(drill.MiningPower >= Content.GetItem("diamond_drill")!.Tool!.MiningPower);
        Assert.Equal("mining_beam", drill.Fx!.Style);
        Assert.Equal("#ff3030", drill.Fx.Color);
        Assert.All(Content.GetItem("guardian_drill")!.HeldModel!, part => Assert.True(part.IsValid()));

        // The hint "you need a …" names the cheapest tool that mines a block — never the new laser.
        foreach (var block in Content.Blocks.Values)
        {
            Assert.NotEqual("guardian_drill", MiningRules.CheapestToolFor(Content, block)?.Key);
        }

        var tier3 = Content.Blocks.Values.FirstOrDefault(b => b.MinToolTier == 3);
        if (tier3 != null)
        {
            Assert.Equal("diamond_drill", MiningRules.CheapestToolFor(Content, tier3)?.Key);
        }
    }

    // ---------------- Where it all comes from ----------------

    [Fact]
    public void TheNewMaterials_DropFromTheMachines_TheRobbers_AndTheBellflowers()
    {
        int Loot(string enemy, string tier, string item)
            => Content.Enemy(enemy).Tier(tier).Loot.Where(l => l.Item == item).Sum(l => l.Count);

        Assert.Equal(1, Loot(EnemyCatalog.ScanDrone, EnemyTiers.Normal, "guardian_plate"));
        Assert.Equal(2, Loot(EnemyCatalog.GuardianRobot, EnemyTiers.Normal, "guardian_plate"));
        Assert.Equal(2, Loot(EnemyCatalog.Hunter, EnemyTiers.Normal, "guardian_plate"));
        Assert.Equal(1, Loot(EnemyCatalog.TankSpider, EnemyTiers.Normal, "guardian_plate"));
        foreach (string robber in new[] { EnemyCatalog.Bandit, EnemyCatalog.BanditGunner })
        {
            Assert.Equal(1, Loot(robber, EnemyTiers.Normal, "robber_cloth"));
            Assert.Equal(2, Loot(robber, EnemyTiers.Camp, "robber_cloth"));
        }

        Assert.Contains(Content.GetBlock("flora_bellflower")!.Drops, d => d.Item == "blue_blossom");

        // After the finale only scan drones come — a trader sells robot metal now and then too.
        var row = Content.GetRecipe("market_buy_guardian_plate")!;
        Assert.Equal("traders", row.MarketTheme);
        Assert.True(row.MarketRotation > 1);
        Assert.Contains(row.Outputs, o => o.Item == "guardian_plate");
    }

    [Fact]
    public void TheSets_AreResearchedAndCrafted_AsPlanned()
    {
        Assert.Contains("titan_chest", Content.GetBlueprint("guardian_gear")!.Prerequisites);
        foreach (string piece in new[] { "guardian_helmet", "guardian_chest", "guardian_legs" })
        {
            Assert.Equal("guardian_gear", Content.GetRecipe(piece)!.RequiredBlueprint);
        }

        foreach (string piece in new[] { "robber_mask", "robber_jacket", "flower_wreath", "flower_bracelets" })
        {
            var recipe = Content.GetRecipe(piece)!;
            Assert.Equal(CraftingStation.Hand, recipe.Station);
            Assert.True(string.IsNullOrEmpty(recipe.RequiredBlueprint), piece);
        }

        Assert.Contains(Content.GetRecipe("guardian_sword")!.Inputs, i => i.Item == "vibro_knife" && i.Count == 1); // an upgrade
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
    public void EveryNewItemAndLine_IsWritten_InEnglishAndGerman(string code)
    {
        var texts = LocaleFile(code);
        var keys = NewItems.SelectMany(k => new[] { Content.GetItem(k)!.NameKey, Content.GetItem(k)!.DescriptionKey })
            .Concat(new[] { "guardian_gear", "guardian_sword", "guardian_drill" }.SelectMany(b => new[]
            {
                Content.GetBlueprint(b)!.NameKey, Content.GetBlueprint(b)!.DescriptionKey,
            }))
            .Concat(new[] { "srv.bandit.disguised", "ui.vendor.discount", "ui.vendor.discount_from" });
        foreach (string key in keys)
        {
            Assert.True(texts.TryGetValue(key, out var text) && text.Length > 0, $"{code}.json lacks '{key}'");
        }

        Assert.Contains("{name}", texts["srv.bandit.disguised"]);
        Assert.Contains("{0}", texts["ui.vendor.discount"]);
        Assert.Contains("{name}", texts["ui.vendor.discount_from"]);
    }
}

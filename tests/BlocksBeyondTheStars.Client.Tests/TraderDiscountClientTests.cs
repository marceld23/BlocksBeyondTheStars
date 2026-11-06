// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3292 on the client: the crafting gate prices a market trade with the discount the server pushed
/// (<see cref="TraderDiscountInfo"/>), through the one shared rule (<see cref="MarketPricing"/>), so the menu's "craftable"
/// and its counts agree with what the server's barter takes — and the push arrives through the real wire.
/// </summary>
public sealed class TraderDiscountClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private const string Ore = "iron_ore";

    private static NetItemStack[] Holding(int ore) => new[] { new NetItemStack { Slot = 0, Item = Ore, Count = ore } };

    private static CraftGateInput Input(int ore, float discount)
        => new(Content, false, new HashSet<string>(), Holding(ore), Array.Empty<NetItemStack>(), Array.Empty<NetItemStack>(),
            aboard: false, PlayerState.PersonalSlots, cargoSlots: 0, () => true, _ => true, _ => false, _ => true, discount);

    private static RecipeDefinition Recipe(CraftingStation station)
        => new()
        {
            Key = "test_trade",
            Station = station,
            Inputs = { new ItemAmount(Ore, 4) },
            Outputs = { new ItemAmount("titanium_ore", 1) },
        };

    [Fact]
    public void AMarketTrade_TheDiscountAffords_IsCraftable_AndCountsAtTheDiscountedPrice()
    {
        var trade = Recipe(CraftingStation.Market);

        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(trade, Input(3, 0f)));
        Assert.Equal(CraftBlock.None, CraftGate.Check(trade, Input(3, 0.25f))); // 4 at 25 % off = 3
        Assert.Equal(2, CraftGate.MaxCraftable(trade, Input(9, 0f)));
        Assert.Equal(3, CraftGate.MaxCraftable(trade, Input(9, 0.25f)));
        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(trade, Input(5, 0.25f), count: 2)); // a batch: 2 × 3
        Assert.Equal(3, Assert.Single(CraftGate.Cost(trade, Input(0, 0.25f))).Count);
    }

    [Fact]
    public void ABenchRecipe_NeverGetsCheaper()
    {
        var bench = Recipe(CraftingStation.Hand);

        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(bench, Input(3, 0.25f)));
        Assert.Same(bench.Inputs, CraftGate.Cost(bench, Input(0, 0.25f)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // encoded like on a socket: the new tag must survive MessagePack
    public void TheServersPush_ReachesTheClient_AndAJoinStartsWithoutADiscount(bool passObjects)
    {
        using var h = new ClientServerHarness(Content, passObjects: passObjects);
        TraderDiscountInfo? pushed = null;
        h.Client.TraderDiscountReceived += m => pushed = m;
        h.Join();
        Assert.Equal(0, h.Client.TraderDiscountPercent);

        var me = h.Server.Sessions.Values.Single(s => s.State.PlayerId == h.JoinAccepted!.PlayerId).State;
        me.Equipment.SetSlot((int)EquipSlot.Head, new ItemStack("flower_wreath", 1));
        me.Equipment.SetSlot((int)EquipSlot.Chest, new ItemStack("flower_bracelets", 1));

        Assert.True(h.PumpUntil(() => pushed != null, 40), "the discount must be pushed within the 1 Hz check");
        Assert.Equal(25, h.Client.TraderDiscountPercent);
        Assert.Equal(0.25f, h.Client.TraderDiscount);
        Assert.Equal(string.Empty, h.Client.TraderDiscountFrom);
    }
}

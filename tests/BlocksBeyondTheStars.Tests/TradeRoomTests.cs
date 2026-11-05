// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3191: a trade never destroys goods — both sides are dry-run before anything moves, and a side without room refuses the
/// swap (the trade stays open). #3201: the trade pool leaves the worn gear out, so a worn cloak is never trade goods.
/// </summary>
public sealed class TradeRoomTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TradeRoomTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_traderoom_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "traderoom"));
        var config = new ServerConfig { WorldName = "traderoom", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Alice and Bob on foot side by side (no cargo hold in the pool), both backpacks empty.</summary>
    private static (PlayerSession A, PlayerSession B) Traders(SvGameServer server)
    {
        var a = server.AddLocalPlayer("Alice");
        var b = server.AddLocalPlayer("Bob");
        foreach (var p in new[] { a, b })
        {
            p.State.AboardShip = false;
            for (int i = 0; i < p.State.Inventory.SlotCount; i++)
            {
                p.State.Inventory.SetSlot(i, null);
            }
        }

        a.State.Position = new Vector3f(0, 64, 0);
        b.State.Position = new Vector3f(1, 64, 0); // within trade range
        return (a, b);
    }

    /// <summary>Puts a full stack of <paramref name="item"/> into the first slot and a full stack of stone into every other.</summary>
    private void FullBackpack(Inventory inventory, string item)
    {
        inventory.SetSlot(0, new ItemStack(item, _content.MaxStackOf(item)));
        for (int i = 1; i < inventory.SlotCount; i++)
        {
            inventory.SetSlot(i, new ItemStack("stone", _content.MaxStackOf("stone")));
        }
    }

    private static int Total(PlayerSession a, PlayerSession b, string item)
        => a.State.Inventory.CountOf(item) + b.State.Inventory.CountOf(item);

    private static void OpenTrade(SvGameServer server)
    {
        server.RequestTrade("Alice", "Bob");
        server.RespondTrade("Bob", true);
    }

    [Fact]
    public void FullBackpacks_HalfAStackForAWholeOne_IsRefused_AndNothingIsLost()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var (a, b) = Traders(server);
            FullBackpack(a.State.Inventory, "iron_ore");
            FullBackpack(b.State.Inventory, "copper_ore");
            int iron = Total(a, b, "iron_ore");
            int copper = Total(a, b, "copper_ore");
            int stone = Total(a, b, "stone");
            int half = _content.MaxStackOf("iron_ore") / 2;
            OpenTrade(server);

            // Alice's iron slot stays occupied; Bob's copper slot empties. The old swap put the iron into Bob's freed slot,
            // found no room for the copper at Alice's, "handed it back" to Bob's now-full backpack — and lost it.
            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", half) });
            server.SetTradeOffer("Bob", new[] { new ItemAmount("copper_ore", _content.MaxStackOf("copper_ore")) });
            server.ConfirmTrade("Alice");
            server.ConfirmTrade("Bob");

            Assert.Equal(iron, Total(a, b, "iron_ore"));
            Assert.Equal(copper, Total(a, b, "copper_ore"));
            Assert.Equal(stone, Total(a, b, "stone"));
            Assert.Equal(0, a.State.Inventory.CountOf("copper_ore")); // nothing moved at all
            Assert.Contains(transport.To<ActionRejected>(a), r => r.Reason == "@srv.trade.no_room");
            Assert.Contains(transport.To<ActionRejected>(b), r => r.Reason == "@srv.trade.partner_no_room:Alice");
            Assert.DoesNotContain(transport.To<TradeClosed>(a), c => c.Completed);

            // The trade stays open with both ready flags cleared — the two can change their offers and confirm again.
            var open = server.ActiveTrade("Alice");
            Assert.NotNull(open);
            Assert.False(open!.ConfirmA);
            Assert.False(open.ConfirmB);
        }
    }

    [Fact]
    public void FullBackpacks_WholeStackForWholeStack_StillSwaps()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var (a, b) = Traders(server);
            FullBackpack(a.State.Inventory, "iron_ore");
            FullBackpack(b.State.Inventory, "copper_ore");
            int iron = _content.MaxStackOf("iron_ore");
            int copper = _content.MaxStackOf("copper_ore");
            OpenTrade(server);

            // Each side frees the slot the other's goods need: the dry run counts the freed slot, so this is no refusal.
            server.SetTradeOffer("Alice", new[] { new ItemAmount("iron_ore", iron) });
            server.SetTradeOffer("Bob", new[] { new ItemAmount("copper_ore", copper) });
            server.ConfirmTrade("Alice");
            server.ConfirmTrade("Bob");

            Assert.Null(server.ActiveTrade("Alice"));
            Assert.Equal(copper, a.State.Inventory.CountOf("copper_ore"));
            Assert.Equal(0, a.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(iron, b.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(0, b.State.Inventory.CountOf("copper_ore"));
            Assert.Contains(transport.To<TradeClosed>(a), c => c.Completed);
            Assert.Empty(server.DropPackets); // nothing had to go to the ground
        }
    }

    [Fact]
    public void AWornCloak_IsNotTradeGoods_OnlyTheBackpackCopy()
    {
        var server = Started(new RecordingTransport(), out var repo);
        using (repo)
        {
            var (a, b) = Traders(server);
            TestGear.Wear(a.State, "stealth_suit");
            a.State.Inventory.Add("stealth_suit", 1, 1);
            OpenTrade(server);

            server.SetTradeOffer("Alice", new[] { new ItemAmount("stealth_suit", 2) }); // the worn one does not count
            Assert.Empty(server.ActiveTrade("Alice")!.OfferA);

            server.SetTradeOffer("Alice", new[] { new ItemAmount("stealth_suit", 1) });
            server.ConfirmTrade("Alice");
            server.ConfirmTrade("Bob");

            Assert.Null(server.ActiveTrade("Alice"));
            Assert.Equal(1, a.State.Equipment.CountOf("stealth_suit")); // still worn
            Assert.Equal(0, a.State.Inventory.CountOf("stealth_suit"));
            Assert.Equal(1, b.State.Inventory.CountOf("stealth_suit"));
        }
    }

    [Fact]
    public void AWornCloak_DoesNotStandInForABackpackCopyThatWentMeanwhile()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var (a, b) = Traders(server);
            TestGear.Wear(a.State, "stealth_suit");
            a.State.Inventory.Add("stealth_suit", 1, 1);
            OpenTrade(server);
            server.SetTradeOffer("Alice", new[] { new ItemAmount("stealth_suit", 1) });
            server.ConfirmTrade("Alice");

            a.State.Inventory.Remove("stealth_suit", 1); // the backpack copy is gone before Bob confirms
            server.ConfirmTrade("Bob");

            Assert.Contains(transport.To<TradeClosed>(a), c => !c.Completed && c.Reason == "@srv.trade.items_gone");
            Assert.Equal(1, a.State.Equipment.CountOf("stealth_suit")); // the worn cloak was not handed over instead
            Assert.Equal(0, b.State.Inventory.CountOf("stealth_suit"));
        }
    }

    [Fact]
    public void CanExchange_CountsTheSlotTheGiveFrees_AndRefusesWithoutOne()
    {
        var player = new PlayerState { PlayerId = "Solo", AboardShip = false };
        FullBackpack(player.Inventory, "iron_ore");
        var pool = MaterialPool.WithoutEquipment(_content, player, new ShipState());
        int stack = _content.MaxStackOf("iron_ore");

        Assert.True(pool.CanExchange(new[] { new ItemAmount("iron_ore", stack) }, new[] { new ItemAmount("copper_ore", 5) }));
        Assert.False(pool.CanExchange(new[] { new ItemAmount("iron_ore", stack / 2) }, new[] { new ItemAmount("copper_ore", 5) }));
        Assert.Equal(stack, player.Inventory.CountOf("iron_ore")); // a dry run: nothing moved
        Assert.Equal(0, player.Inventory.CountOf("copper_ore"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheNoRoomLines_AreLocalized_AndNameThePartner(string language)
    {
        var table = TestLocales.Load(language);
        Assert.False(string.IsNullOrWhiteSpace(table["srv.trade.no_room"]));
        Assert.Contains("{name}", table["srv.trade.partner_no_room"]); // the client fills in the partner's name
    }
}

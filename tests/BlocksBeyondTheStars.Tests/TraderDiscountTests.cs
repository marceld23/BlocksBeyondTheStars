// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3292 (Justus: "flower armour that lowers the traders' prices for you and your allies"): the server prices every market
/// trade with the best of the player's own worn discount and that of an ally (alliance or crew) wearing flowers within
/// 32 m on the same world — never several added up — and tells the client the value whenever it changes.
/// </summary>
public sealed class TraderDiscountTests : IDisposable
{
    private const string Trade = "market_iron_to_titanium"; // a themeless trade at the ship's console: 5 iron → 1 titanium

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_discount_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string world, RecordingTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, ViewDistanceChunks = 1 };
        var server = new SvGameServer(config, _content, transport ?? new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    /// <summary>A player aboard (at the ship's trade console) or on foot, at <paramref name="x"/> along the ground.</summary>
    private static PlayerSession Player(SvGameServer server, string name, float x, bool aboard = true)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = aboard;
        p.State.Position = new Vector3f(x, 64f, 8f);
        return p;
    }

    private static void Wear(PlayerSession p, params string[] items)
    {
        foreach (string item in items)
        {
            var slot = item.EndsWith("wreath", StringComparison.Ordinal) ? EquipSlot.Head : EquipSlot.Chest;
            p.State.Equipment.SetSlot((int)slot, new ItemStack(item, 1));
        }
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAllianceForTest(a, b);
        server.RespondAllianceForTest(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    [Fact]
    public void TheWearer_PaysLess_SoATradeOnlyTheDiscountAffords_GoesThrough()
    {
        var server = Started("wearer");
        var flowers = Player(server, "Justus", 8f);
        var plain = Player(server, "Marcel", 9f);
        Wear(flowers, "flower_wreath", "flower_bracelets");
        flowers.State.Inventory.Add("iron_ore", 4, 1024);
        plain.State.Inventory.Add("iron_ore", 4, 1024);

        server.Craft("Justus", Trade); // 5 iron at 25 % off = 4
        server.Craft("Marcel", Trade);

        Assert.Equal(1, flowers.State.Inventory.CountOf("titanium_ore"));
        Assert.Equal(0, flowers.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(0, plain.State.Inventory.CountOf("titanium_ore")); // 4 is not enough at the full price
        Assert.Equal(4, plain.State.Inventory.CountOf("iron_ore"));
    }

    [Fact]
    public void ABatch_PaysTheDiscountedTradeEveryTime()
    {
        var server = Started("batch");
        var p = Player(server, "Justus", 8f);
        Wear(p, "flower_wreath", "flower_bracelets");
        p.State.Inventory.Add("iron_ore", 9, 1024);

        server.Craft("Justus", Trade, count: 2);

        Assert.Equal(2, p.State.Inventory.CountOf("titanium_ore"));
        Assert.Equal(1, p.State.Inventory.CountOf("iron_ore")); // 2 × 4, not 2 × 5 and not round(10 × 0.75)
    }

    [Fact]
    public void AnAllyNearby_SharesTheDiscount_TheBestOneCounts_NeverAddedUp()
    {
        var server = Started("ally");
        var buyer = Player(server, "Marcel", 8f);
        var wreath = Player(server, "Bob", 18f, aboard: false);
        var full = Player(server, "Justus", 28f, aboard: false);
        Ally(server, "Marcel", "Bob");
        Ally(server, "Marcel", "Justus");
        Wear(wreath, "flower_wreath");
        Wear(full, "flower_wreath", "flower_bracelets");

        Assert.Equal((25, "Justus"), server.TraderDiscountForTest("Marcel"));

        Wear(buyer, "flower_wreath"); // her own 10 % does not add to the ally's 25 %
        Assert.Equal((25, "Justus"), server.TraderDiscountForTest("Marcel"));

        buyer.State.Inventory.Add("iron_ore", 4, 1024);
        server.Craft("Marcel", Trade);
        Assert.Equal(1, buyer.State.Inventory.CountOf("titanium_ore"));

        Wear(buyer, "flower_wreath", "flower_bracelets"); // as good as the ally's — her own counts
        Assert.Equal((25, string.Empty), server.TraderDiscountForTest("Marcel"));
    }

    [Fact]
    public void AnAllyTooFarAway_OrSomebodyWhoIsNoAlly_GivesNothing()
    {
        var server = Started("far");
        Player(server, "Marcel", 8f);
        var far = Player(server, "Bob", 8f + SvGameServer.TraderDiscountAllyRange + 4f, aboard: false);
        var stranger = Player(server, "Stranger", 12f, aboard: false);
        Ally(server, "Marcel", "Bob");
        Wear(far, "flower_wreath", "flower_bracelets");
        Wear(stranger, "flower_wreath", "flower_bracelets");

        Assert.Equal((0, string.Empty), server.TraderDiscountForTest("Marcel"));

        far.State.Position = new Vector3f(8f + SvGameServer.TraderDiscountAllyRange - 2f, 64f, 8f); // walks up
        Assert.Equal((25, "Bob"), server.TraderDiscountForTest("Marcel"));
    }

    [Fact]
    public void TheDiscount_IsPushedWhenItChanges_AndOnlyThen()
    {
        var transport = new RecordingTransport();
        var server = Started("push", transport);
        var justus = Player(server, "Justus", 8f, aboard: false);
        var marcel = Player(server, "Marcel", 12f);
        Ally(server, "Justus", "Marcel");

        transport.Sent.Clear();
        server.TickTraderDiscountsForTest();
        Assert.Empty(transport.To<TraderDiscountInfo>(justus)); // nothing to tell while there never was a discount

        Wear(justus, "flower_wreath", "flower_bracelets");
        server.TickTraderDiscountsForTest();
        var own = Assert.Single(transport.To<TraderDiscountInfo>(justus));
        Assert.Equal(25, own.Percent);
        Assert.Equal(string.Empty, own.FromName);
        var shared = Assert.Single(transport.To<TraderDiscountInfo>(marcel));
        Assert.Equal(25, shared.Percent);
        Assert.Equal("Justus", shared.FromName);

        server.TickTraderDiscountsForTest(); // no change, no message
        Assert.Single(transport.To<TraderDiscountInfo>(justus));

        justus.State.Equipment.SetSlot((int)EquipSlot.Chest, null); // the bracelets come off: 10 %
        server.TickTraderDiscountsForTest();
        Assert.Equal(10, transport.To<TraderDiscountInfo>(marcel).Last().Percent);

        justus.State.Equipment.SetSlot((int)EquipSlot.Head, null);
        server.TickTraderDiscountsForTest();
        Assert.Equal(0, transport.To<TraderDiscountInfo>(justus).Last().Percent);
        Assert.Equal(0, transport.To<TraderDiscountInfo>(marcel).Last().Percent);
        Assert.Equal(string.Empty, transport.To<TraderDiscountInfo>(marcel).Last().FromName);
    }

    [Fact]
    public void ARecipeThatIsNoTrade_NeverGetsCheaper()
    {
        var server = Started("hand");
        var p = Player(server, "Justus", 8f);
        Wear(p, "flower_wreath", "flower_bracelets");
        p.State.Inventory.Add("carbon", 1, 1024); // a medpack takes 2 carbon — 25 % off would make it 1, but this is no trade
        p.State.Inventory.Add("silicate", 1, 1024);

        server.Craft("Justus", "medpack");

        Assert.Equal(0, p.State.Inventory.CountOf("medpack"));
        Assert.Equal(1, p.State.Inventory.CountOf("carbon"));
    }
}

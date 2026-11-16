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
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3731: samples change hands in the player trade — case to case, validated against the sample case and the species
/// register, all-or-nothing with the goods, without the story event of a sample taken, and without re-arming the
/// "first block of a deposit" rule. #3732: a seedling that arrives by trade or out of a crate brings its species entry.
/// </summary>
public sealed class SampleTradeTests : IDisposable
{
    private const string NoItems = "@srv.trade.missing_items";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SampleTradeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sampletrade_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sampletrade"));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = "sampletrade", Seed = 77, StartPlanet = "jungle", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Alice and Bob on foot side by side high up in the air, backpacks and sample cases empty.</summary>
    private static (PlayerSession A, PlayerSession B) Traders(SvGameServer server)
    {
        var a = server.AddLocalPlayer("Alice");
        var b = server.AddLocalPlayer("Bob");
        foreach (var p in new[] { a, b })
        {
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            for (int i = 0; i < p.State.Inventory.SlotCount; i++)
            {
                p.State.Inventory.SetSlot(i, null);
            }
        }

        a.State.Position = new Vector3f(0, 200, 0);
        b.State.Position = new Vector3f(1, 200, 0); // within trade range
        return (a, b);
    }

    private static void OpenTrade(SvGameServer server)
    {
        server.RequestTrade("Alice", "Bob");
        server.RespondTrade("Bob", true);
    }

    private static void ConfirmBoth(SvGameServer server)
    {
        server.ConfirmTrade("Alice");
        server.ConfirmTrade("Bob");
    }

    private static string Sample(uint seed, bool mineral = false) => ItemKey.WithSeed(mineral ? BioItems.MineralSample : BioItems.Sample, seed);

    private static int InCase(PlayerSession p, string key) => p.State.SampleCase.CountOf(key);

    private static ItemAmount[] Offer(params (string Item, int Count)[] lines) => lines.Select(l => new ItemAmount(l.Item, l.Count)).ToArray();

    private void Give(PlayerSession p, string item, int count) => Assert.Equal(0, p.State.Inventory.Add(item, count, _content.MaxStackOf(item)));

    /// <summary>Fills every slot of a sample case with one sample of a kind of its own (seeds no register knows).</summary>
    private static void FillCaseWithKinds(PlayerSession p)
    {
        for (int i = 0; i < p.State.SampleCase.SlotCount; i++)
        {
            p.State.SampleCase.SetSlot(i, new ItemStack(Sample(0x00C0FFEEu + (uint)i), 1));
        }
    }

    private static void AssertNothingTraded(SvGameServer server, RecordingTransport transport, PlayerSession a)
    {
        Assert.DoesNotContain(transport.To<TradeClosed>(a), c => c.Completed);
        var open = server.ActiveTrade("Alice");
        Assert.NotNull(open); // the trade stays open with both ready flags cleared, like the backpack refusal (#3191)
        Assert.False(open!.ConfirmA);
        Assert.False(open.ConfirmB);
    }

    // ---------------- Offer and move ----------------

    [Fact]
    public void ASampleOffer_IsAccepted_AndMovesFromCaseToCase_OnConfirm()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 5);
        string key = Sample(plant);
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((key, 3)));

        var line = Assert.Single(server.ActiveTrade("Alice")!.OfferA);
        Assert.Equal(key, line.Item);
        Assert.Equal(3, line.Count);
        Assert.Contains(transport.To<TradeUpdate>(b), u => u.TheirOffer.Any(i => i.Item == key && i.Count == 3)); // the same message as ever
        Assert.Equal(5, InCase(a, key)); // nothing moves before both confirm

        ConfirmBoth(server);

        Assert.Null(server.ActiveTrade("Alice"));
        Assert.Contains(transport.To<TradeClosed>(a), c => c.Completed);
        Assert.Equal(2, InCase(a, key));
        Assert.Equal(3, InCase(b, key));
        Assert.Equal(0, b.State.Inventory.CountOf(key)); // into the case, not the backpack

        // Both clients are sent their changed sample case.
        var toAlice = transport.To<InventoryUpdate>(a).Last();
        var toBob = transport.To<InventoryUpdate>(b).Last();
        Assert.False(toAlice.SamplesUnchanged);
        Assert.False(toBob.SamplesUnchanged);
        Assert.Equal(2, toAlice.Samples.Where(s => s.Item == key).Sum(s => s.Count));
        Assert.Equal(3, toBob.Samples.Where(s => s.Item == key).Sum(s => s.Count));
    }

    [Fact]
    public void AnOfferOfMoreThanTheCaseHolds_OrOfASeedTheRegisterDoesNotKnow_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, _) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 2);
        string unknown = Sample(plant ^ 0x5A5A5A5Au);
        a.State.SampleCase.Add(unknown, 4, BioRules.SampleStack); // in the case, but no species of this save
        a.State.SampleCase.Add(Sample(plant, mineral: true), 1, BioRules.SampleStack); // a known seed under the wrong kind
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((Sample(plant), 3))); // one more than the case holds
        Assert.Empty(server.ActiveTrade("Alice")!.OfferA);
        Assert.Single(transport.To<ActionRejected>(a), r => r.Reason == NoItems);

        server.SetTradeOffer("Alice", Offer((unknown, 1)));
        Assert.Empty(server.ActiveTrade("Alice")!.OfferA);
        Assert.Equal(2, transport.To<ActionRejected>(a).Count(r => r.Reason == NoItems));

        server.SetTradeOffer("Alice", Offer((Sample(plant, mineral: true), 1))); // the seed is a plant's, the key a deposit's
        Assert.Empty(server.ActiveTrade("Alice")!.OfferA);
        Assert.Equal(3, transport.To<ActionRejected>(a).Count(r => r.Reason == NoItems));
    }

    [Fact]
    public void ASampleKeyInTheBackpack_IsNoTradeGoods_AndDoesNotCountTowardsTheCase()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 2);
        string key = Sample(plant);
        Give(a, key, 3); // an admin /give oddity: three samples of the same species in the backpack
        Give(b, key, 1); // …and Bob's only one lies in the backpack
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((key, 5))); // 2 in the case + 3 in the backpack do not add up
        Assert.Empty(server.ActiveTrade("Alice")!.OfferA);
        Assert.Contains(transport.To<ActionRejected>(a), r => r.Reason == NoItems);

        server.SetTradeOffer("Bob", Offer((key, 1))); // only in the backpack: not tradeable
        Assert.Empty(server.ActiveTrade("Alice")!.OfferB);
        Assert.Contains(transport.To<ActionRejected>(b), r => r.Reason == NoItems);

        server.SetTradeOffer("Alice", Offer((key, 2))); // what the case holds is fine
        ConfirmBoth(server);

        Assert.Null(server.ActiveTrade("Alice"));
        Assert.Equal(0, InCase(a, key));
        Assert.Equal(3, a.State.Inventory.CountOf(key)); // the backpack copies never moved
        Assert.Equal(2, InCase(b, key));
        Assert.Equal(1, b.State.Inventory.CountOf(key));
    }

    // ---------------- Room ----------------

    [Fact]
    public void ACaseWithoutAFreeSlot_CancelsTheWholeTrade_TheGoodsToo()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 2);
        string key = Sample(plant);
        FillCaseWithKinds(b); // 24 kinds: no slot for a new one
        Give(b, "iron_ore", 3);
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((key, 1)));
        server.SetTradeOffer("Bob", Offer(("iron_ore", 3)));
        ConfirmBoth(server);

        Assert.Equal(2, InCase(a, key));
        Assert.Equal(0, InCase(b, key));
        Assert.Equal(0, a.State.Inventory.CountOf("iron_ore")); // the ore stayed too: all or nothing
        Assert.Equal(3, b.State.Inventory.CountOf("iron_ore"));
        Assert.Contains(transport.To<ActionRejected>(b), r => r.Reason == "@srv.trade.sample_case_full");
        Assert.Contains(transport.To<ActionRejected>(a), r => r.Reason == "@srv.trade.partner_sample_case_full:Bob");
        AssertNothingTraded(server, transport, a);
    }

    [Fact]
    public void AFullStackOfTheKind_OnTheOtherSide_CancelsTheWholeTrade_TheGoodsToo()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", BioRules.SampleStack); // Alice: 20 of 20
        server.GiveSampleForTest(b, plant, 5);
        string key = Sample(plant);
        Give(a, "iron_ore", 3);
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer(("iron_ore", 3)));
        server.SetTradeOffer("Bob", Offer((key, 1))); // a 21st: free slots do not help, a kind stacks to 20
        ConfirmBoth(server);

        Assert.Equal(BioRules.SampleStack, InCase(a, key));
        Assert.Equal(5, InCase(b, key));
        Assert.Equal(3, a.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(0, b.State.Inventory.CountOf("iron_ore"));
        Assert.Contains(transport.To<ActionRejected>(a), r => r.Reason == "@srv.trade.sample_case_full");
        Assert.Contains(transport.To<ActionRejected>(b), r => r.Reason == "@srv.trade.partner_sample_case_full:Alice");
        AssertNothingTraded(server, transport, a);
    }

    [Fact]
    public void TheRoomCheck_CountsWhatLeavesTheCaseInTheSameTrade()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", BioRules.SampleStack); // a full stack
        uint ore = server.GiveMineralSampleForTest(b, "tungsten_ore", "tungsten_ore", 2);
        FillCaseWithKinds(b);
        b.State.SampleCase.SetSlot(0, new ItemStack(Sample(ore, mineral: true), 2)); // 24 kinds, one of them the deposit
        OpenTrade(server);

        // Bob gives the whole deposit kind away, which frees the slot Alice's plant sample needs.
        server.SetTradeOffer("Alice", Offer((Sample(plant), 4)));
        server.SetTradeOffer("Bob", Offer((Sample(ore, mineral: true), 2)));
        ConfirmBoth(server);

        Assert.Null(server.ActiveTrade("Alice"));
        Assert.Contains(transport.To<TradeClosed>(a), c => c.Completed);
        Assert.Equal(BioRules.SampleStack - 4, InCase(a, Sample(plant)));
        Assert.Equal(2, InCase(a, Sample(ore, mineral: true)));
        Assert.Equal(4, InCase(b, Sample(plant)));
        Assert.Equal(0, InCase(b, Sample(ore, mineral: true)));
    }

    [Fact]
    public void AMixedTrade_ItemsAndSamplesBothWays_IsAllOrNothing()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 4);
        uint ore = server.GiveMineralSampleForTest(b, "tungsten_ore", "tungsten_ore", 3);
        Give(a, "iron_ore", 6);
        Give(b, "copper_ore", 5);
        OpenTrade(server);
        var aliceGives = Offer(("iron_ore", 6), (Sample(plant), 2));
        var bobGives = Offer(("copper_ore", 5), (Sample(ore, mineral: true), 1));

        // No room in Alice's backpack for the copper: the samples stay where they are as well.
        for (int i = 0; i < a.State.Inventory.SlotCount; i++)
        {
            if (a.State.Inventory.Slots[i] is null)
            {
                a.State.Inventory.SetSlot(i, new ItemStack("stone", _content.MaxStackOf("stone")));
            }
        }

        server.SetTradeOffer("Alice", Offer((Sample(plant), 2))); // keeps her ore slot occupied
        server.SetTradeOffer("Bob", bobGives);
        ConfirmBoth(server);

        Assert.Contains(transport.To<ActionRejected>(a), r => r.Reason == "@srv.trade.no_room");
        Assert.Equal(4, InCase(a, Sample(plant)));
        Assert.Equal(3, InCase(b, Sample(ore, mineral: true)));
        Assert.Equal(0, InCase(b, Sample(plant)));
        Assert.Equal(5, b.State.Inventory.CountOf("copper_ore"));
        AssertNothingTraded(server, transport, a);

        // With the ore in the offer its slot is freed for the copper — now everything goes, both ways.
        server.SetTradeOffer("Alice", aliceGives);
        ConfirmBoth(server);

        Assert.Null(server.ActiveTrade("Alice"));
        Assert.Contains(transport.To<TradeClosed>(a), c => c.Completed);
        Assert.Equal(0, a.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(6, b.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(5, a.State.Inventory.CountOf("copper_ore"));
        Assert.Equal(0, b.State.Inventory.CountOf("copper_ore"));
        Assert.Equal(2, InCase(a, Sample(plant)));
        Assert.Equal(2, InCase(b, Sample(plant)));
        Assert.Equal(1, InCase(a, Sample(ore, mineral: true)));
        Assert.Equal(2, InCase(b, Sample(ore, mineral: true)));
    }

    // ---------------- Naming, and what a handed-over sample is not ----------------

    [Fact]
    public void TheReceiver_IsSentTheSpecies_WithTheOffer_AndNoSampleWasTaken()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 3);
        server.World.SetBlock(new Vector3i(2, 200, 0), _content.GetBlock(BioItems.Lab)!.NumericId);
        server.BioLabForTest(a, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = plant });
        Assert.True(server.BioAnalysedForTest("Alice", plant));
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        Assert.DoesNotContain(transport.To<BioBook>(b), book => book.Species.Any(s => s.Seed == plant));
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((Sample(plant), 2)));

        // Bob's window can name the offer before anything is his: the entry a holder gets, not Alice's analysis.
        var sent = Assert.Single(transport.To<BioBook>(b).SelectMany(book => book.Species), s => s.Seed == plant);
        Assert.Equal(server.BioEntryForTest(plant)!.Name, sent.Name);
        Assert.False(sent.Analysed);
        Assert.DoesNotContain(transport.To<BioBook>(b), book => book.Full || book.Reactions.Length > 0 || book.Changes.Length > 0);

        ConfirmBoth(server);

        Assert.Equal(2, InCase(b, Sample(plant)));
        Assert.Single(transport.To<BioBook>(b).SelectMany(book => book.Species), s => s.Seed == plant); // not sent twice
        Assert.False(server.BioAnalysedForTest("Bob", plant)); // knowledge is not traded
        Assert.True(server.BioAnalysedForTest("Alice", plant));
        Assert.Contains(events.Of(GameEventKeys.TradePlayer), e => e.Player == "Bob"); // the listener hears the trade…
        Assert.Empty(events.Of(GameEventKeys.SampleTaken));                             // …but nobody took a sample
        Assert.DoesNotContain("vega:hint:first_sample", b.State.Milestones);
        Assert.DoesNotContain(transport.LineKeys(b), k => k.Contains("first_sample", StringComparison.Ordinal));
    }

    // ---------------- The deposit rule ----------------

    [Fact]
    public void TradingTheLastMineralSampleAway_DoesNotReArmTheFirstBlock_ButThrowingItAwayStillDoes()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        var (a, b) = Traders(server);
        Give(a, "basic_drill", 1);
        Give(b, "basic_drill", 1);
        var ore = _content.GetBlock("iron_ore")!.NumericId;
        void Mine(PlayerSession p, Vector3i cell)
        {
            p.State.SuitEnergy = 100f;
            server.World.SetBlock(cell, ore); // a cell the world made — a cell mined once is a player's edit and no deposit
            server.MineBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z);
            Assert.Equal(BlockId.Air, server.World.GetBlock(cell));
        }

        Mine(a, new Vector3i(2, 200, 0));
        var first = Assert.Single(a.State.SampleCase.Slots, s => s is { IsEmpty: false });
        string key = first!.Item; // the first block of the deposit always yields a sample
        Assert.Equal(BioItems.MineralSample, ItemKey.Base(key));
        Assert.Equal(1, first.Count);

        // Three fresh cells that are not "one in eight": only a first block yields a sample there.
        var quiet = (from x in new[] { 2, 3 } from y in new[] { 201, 202 } from z in new[] { -2, -1, 0, 1 } select new Vector3i(x, y, z))
            .Where(cell => !YieldsOneInEight(cell, ItemKey.Seed(key))).Take(3).ToList();
        Assert.Equal(3, quiet.Count);
        Mine(a, quiet[0]);
        Assert.Equal(1, InCase(a, key)); // the deposit is known (a sample in the case): nothing from a quiet cell

        OpenTrade(server);
        server.SetTradeOffer("Alice", Offer((key, 1)));
        ConfirmBoth(server);
        Assert.Equal(0, InCase(a, key));
        Assert.Equal(1, InCase(b, key));

        Mine(a, quiet[1]);
        Assert.Equal(0, InCase(a, key)); // traded away, not forgotten: no fresh "first block"
        Assert.Contains("SamplesTradedAway", repo.LoadNamedBlob("bio:research:Alice") ?? string.Empty, StringComparison.Ordinal);

        // Bob throws his away: for him the next block is a first block again — that rule is unchanged.
        server.DiscardSampleForTest(b, key);
        Assert.Equal(0, InCase(b, key));
        Mine(b, quiet[2]);
        Assert.Equal(1, InCase(b, key));
    }

    /// <summary>The server's cell hash for "one cell in eight yields a mineral sample" (<c>BioOnBlockBroken</c>).</summary>
    private static bool YieldsOneInEight(Vector3i pos, uint seed)
        => BioHash.Mix(unchecked(((ulong)(uint)pos.X * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)pos.Y << 21) ^ ((ulong)(uint)pos.Z << 42) ^ seed))
            % BioRules.MineralSampleEvery == 0;

    // ---------------- #3732: the species of a seedling that arrives ----------------

    [Fact]
    public void ASeedlingHandedOverByTrade_BringsItsSpeciesEntry_BeforeTheInventory()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, plant);
        Give(a, seedling, 2);
        OpenTrade(server);

        server.SetTradeOffer("Alice", Offer((seedling, 2)));
        ConfirmBoth(server);

        Assert.Equal(2, b.State.Inventory.CountOf(seedling));
        var toBob = transport.To(b);
        int entry = toBob.FindIndex(m => m is BioBook book && book.Species.Any(s => s.Seed == plant));
        int inventory = toBob.FindIndex(m => m is InventoryUpdate u && u.Personal.Any(s => s.Item == seedling));
        Assert.True(entry >= 0, "the species entry was not sent");
        Assert.True(entry < inventory, "the entry has to be there when the seedling shows up");

        server.SendInventoryForTest(b); // and once is enough
        Assert.Single(transport.To<BioBook>(b).SelectMany(book => book.Species), s => s.Seed == plant);
    }

    [Fact]
    public void ASeedlingTakenFromACrate_BringsItsSpeciesEntry()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out _);
        var (a, b) = Traders(server);
        uint plant = server.GiveFloraSampleForTest(a, "flora_bush", 1);
        string seedling = ItemKey.WithSeed(BioItems.Seedling, plant);
        server.AddContainerForTest(new StoredContainer
        {
            Id = "crate_seedlings",
            Planet = b.State.CurrentLocationId,
            Kind = "crate",
            Position = new Vector3i(2, 200, 0),
            Items = new List<ItemStack> { new(seedling, 1), new("iron_ore", 4) },
        });
        Assert.DoesNotContain(transport.To<BioBook>(b), book => book.Species.Any(s => s.Seed == plant));

        server.LootContainer("Bob", "crate_seedlings");

        Assert.Equal(1, b.State.Inventory.CountOf(seedling));
        Assert.Single(transport.To<BioBook>(b).SelectMany(book => book.Species), s => s.Seed == plant);
    }

    // ---------------- Texts ----------------

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheSampleCaseLines_AreLocalized_AndNameThePartner(string language)
    {
        var table = TestLocales.Load(language);
        Assert.False(string.IsNullOrWhiteSpace(table["srv.trade.sample_case_full"]));
        Assert.Contains("{name}", table["srv.trade.partner_sample_case_full"]); // the client fills in the partner's name
        Assert.False(string.IsNullOrWhiteSpace(table["ui.trade.samples"]));
    }
}

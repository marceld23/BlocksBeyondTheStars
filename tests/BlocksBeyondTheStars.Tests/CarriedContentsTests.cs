// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
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
/// Items that carry items (#3098, #3099): the transport crate keeps what it holds when it is mined and gives it back when it
/// is placed again; the storage bag holds up to 1000 items and is filled and emptied from the backpack. Both keep their
/// contents in a save-wide record the item's key points at — and every way an item can travel (the ground, a death, a trade,
/// the Sandbox catalog, the bin) leaves exactly one item pointing at it.
/// </summary>
public sealed class CarriedContentsTests : IDisposable
{
    private const string Crate = CarriedContents.TransportCrate;
    private const string Bag = CarriedContents.Bag;

    private readonly string _root;
    private readonly GameContent _content;

    public CarriedContentsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_carried_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, GameRules? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "carried"));
        var config = new ServerConfig
        {
            WorldName = "carried",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceLodges = false,
            Rules = rules ?? new GameRules(),
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player up in the air (every cell around is free), off the ship so the hold takes nothing.</summary>
    private static PlayerState Player(SvGameServer server, string name = "Justus")
    {
        var p = server.AddLocalPlayer(name).State;
        p.AboardShip = false;
        p.Position = new Vector3f(0.5f, 200f, 0.5f);
        return p;
    }

    /// <summary>Places a transport crate at (1,200,0) and fills it with 10 iron ore and 40 stone.</summary>
    private static StoredContainer FilledCrate(SvGameServer server, PlayerState p)
    {
        p.Inventory.Add(Crate, 1, 64);
        p.Inventory.Add("iron_ore", 10, 1024);
        p.Inventory.Add("stone", 40, 1024);
        server.PlaceBlock(p.PlayerId, 1, 200, 0, Crate);
        var crate = server.Containers.Single(c => c.Kind == "crate" && c.Position == new Vector3i(1, 200, 0));
        server.DepositToContainer(p.PlayerId, crate.Id);
        Assert.Equal(0, p.Inventory.CountOf("iron_ore"));
        return crate;
    }

    private static string PackedKey(PlayerState p)
        => p.Inventory.Slots.Single(s => s is { IsEmpty: false } && CarriedContents.HasRecord(s.Item))!.Item;

    private static int SlotOf(PlayerState p, string item)
    {
        for (int i = 0; i < p.Inventory.SlotCount; i++)
        {
            if (p.Inventory.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------------- The key ----------------

    [Fact]
    public void ARecordKey_ParsesBack_NeverStacks_AndLeavesTheOtherModifiersAlone()
    {
        string key = CarriedContents.WithRecord(Bag, "0123456789abcdef");

        Assert.Equal("storage_bag#h0123456789abcdef", key);
        Assert.Equal("0123456789abcdef", CarriedContents.RecordId(key));
        Assert.True(CarriedContents.HasRecord(key));
        Assert.Equal(Bag, CarriedContents.WithoutRecord(key));
        Assert.False(CarriedContents.HasRecord(Bag));
        Assert.False(CarriedContents.HasRecord("storage_bag#h12")); // a malformed id is none
        Assert.Equal(1, _content.MaxStackOf(key));
        Assert.Equal(1, _content.MaxStackOf(CarriedContents.WithRecord(Crate, "00000000000000aa")));
        Assert.Equal(64, _content.MaxStackOf(Crate)); // an empty crate stacks as before
        Assert.Equal(0, ItemKey.Tint(key));
        Assert.Equal(0, ItemKey.Shape(key));
        Assert.Equal(0, ItemKey.Design(key));
        Assert.Equal(Bag, ItemKey.Base(key));
        Assert.Same(_content.GetItem(Bag), _content.GetItem(key));
    }

    [Fact]
    public void AFilledBag_IsNamedAsFilled_InBothLanguages()
    {
        string key = CarriedContents.WithRecord(Bag, "0123456789abcdef");
        foreach (string language in new[] { "en", "de" })
        {
            var loc = new BlocksBeyondTheStars.Shared.Localization.Localizer(
                BlocksBeyondTheStars.Shared.Localization.GameLocale.English, TestLocales.Load(language), TestLocales.Load("en"));
            string plain = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(loc, Bag);
            string filled = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(loc, key);

            Assert.StartsWith(plain, filled);
            Assert.EndsWith(loc.Get("ui.carried.filled"), filled);
            Assert.DoesNotContain("#", filled);
            Assert.DoesNotContain("[", filled);
        }
    }

    // ---------------- #3098: the transport crate ----------------

    [Fact]
    public void AMinedTransportCrate_KeepsItsContents_AndPlacingItGivesThemBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            var crate = FilledCrate(server, p);

            server.MineBlock(p.PlayerId, 1, 200, 0);

            Assert.DoesNotContain(server.Containers, c => c.Id == crate.Id);
            Assert.Equal(0, p.Inventory.CountOf("iron_ore")); // NOT handed to the miner
            Assert.Equal(0, p.Inventory.CountOf("stone"));
            string packed = PackedKey(p);
            Assert.Equal(Crate, ItemKey.Base(packed));
            Assert.Equal(1, p.Inventory.CountOf(packed));
            Assert.Contains(server.CarriedContentsForTest(packed), s => s.Item == "iron_ore" && s.Count == 10);
            Assert.Equal(1, server.CarriedRecordCountForTest);

            server.PlaceBlock(p.PlayerId, 0, 200, 2, packed);

            var placed = server.Containers.Single(c => c.Kind == "crate" && c.Position == new Vector3i(0, 200, 2));
            Assert.Contains(placed.Items, s => s.Item == "iron_ore" && s.Count == 10);
            Assert.Contains(placed.Items, s => s.Item == "stone" && s.Count == 40);
            Assert.Equal(0, p.Inventory.CountOf(packed)); // the packed item is used up …
            Assert.Equal(0, server.CarriedRecordCountForTest); // … and so is its record
            Assert.Empty(server.CarriedContentsForTest(packed));
        }
    }

    [Fact]
    public void AnEmptyTransportCrate_DropsPlain_AndStacksWithTheOthers()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Crate, 2, 64);
            server.PlaceBlock(p.PlayerId, 1, 200, 0, Crate);
            server.PlaceBlock(p.PlayerId, -1, 200, 0, Crate);

            server.MineBlock(p.PlayerId, 1, 200, 0);
            server.MineBlock(p.PlayerId, -1, 200, 0);

            Assert.Equal(2, p.Inventory.CountOf(Crate));
            Assert.Equal(0, server.CarriedRecordCountForTest);
        }
    }

    [Fact]
    public void APackedCrate_SurvivesASaveAndARestart()
    {
        string packed;
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            FilledCrate(server, p);
            server.MineBlock(p.PlayerId, 1, 200, 0);
            packed = PackedKey(p);
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Started(out var repo2);
        using (repo2)
        {
            Assert.Single(repo2.ListContainers("carried")); // save-wide, under its own pseudo-location
            var p = Player(again); // the join may set the player down somewhere else — back up into the free air
            Assert.Equal(1, p.Inventory.CountOf(packed));

            again.PlaceBlock(p.PlayerId, 0, 200, 2, packed);

            var placed = again.Containers.Single(c => c.Kind == "crate" && c.Position == new Vector3i(0, 200, 2));
            Assert.Contains(placed.Items, s => s.Item == "iron_ore" && s.Count == 10);
            Assert.Empty(repo2.ListContainers("carried"));
        }
    }

    [Fact]
    public void APackedCrate_OnTheGround_IsPickedUpOnce_AndStillCarriesItsContents()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            FilledCrate(server, p);
            int max = _content.MaxStackOf("dirt");
            for (int i = 0; i < p.Inventory.SlotCount; i++)
            {
                if (p.Inventory.Slots[i] is null)
                {
                    p.Inventory.SetSlot(i, new ItemStack("dirt", max)); // no room: the packed crate spills to the ground
                }
            }

            server.MineBlock(p.PlayerId, 1, 200, 0);
            var packet = Assert.Single(server.DropPackets);
            string packed = Assert.Single(packet.Items, s => CarriedContents.HasRecord(s.Item)).Item;
            Assert.Equal(1, packet.Items.Single(s => s.Item == packed).Count);

            p.Inventory.SetSlot(SlotOf(p, "dirt"), null);
            p.Position = new Vector3f(packet.Position.X + 0.5f, packet.Position.Y + 0.5f, packet.Position.Z + 0.5f);
            server.SweepDropPacketsForTest();
            server.SweepDropPacketsForTest();

            Assert.Equal(1, p.Inventory.CountOf(packed));
            Assert.Empty(server.DropPackets);
            Assert.Contains(server.CarriedContentsForTest(packed), s => s.Item == "stone" && s.Count == 40);
        }
    }

    [Fact]
    public void APackedCrate_IsUsedUpEvenWhenPlacingIsFree()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            FilledCrate(server, p);
            server.MineBlock(p.PlayerId, 1, 200, 0);
            string packed = PackedKey(p);
            p.ModeOverride = PlayerModeOverride.Creative; // Sandbox places for free …

            server.PlaceBlock(p.PlayerId, 0, 200, 2, packed);

            Assert.Equal(0, p.Inventory.CountOf(packed)); // … but not what it unpacks: no dead copy stays behind
            Assert.Single(server.Containers, c => c.Kind == "crate" && c.Position == new Vector3i(0, 200, 2));
        }
    }

    // ---------------- #3099: the storage bag ----------------

    [Fact]
    public void TheBag_HoldsAThousandItems_AndNoMore()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Bag, 1, 1);
            p.Inventory.Add("stone", 1200, 1024);
            int bagSlot = SlotOf(p, Bag);

            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, "stone", toBag: true, all: true));

            string filled = p.Inventory.Slots[bagSlot]!.Item;
            Assert.True(CarriedContents.HasRecord(filled));
            Assert.Equal(200, p.Inventory.CountOf("stone")); // only what fits went in
            Assert.Equal(CarriedContents.BagCapacity, server.CarriedContentsForTest(filled).Sum(s => s.Count));

            p.Inventory.Add("iron_ore", 5, 1024);
            Assert.False(server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: true));
            Assert.Equal(5, p.Inventory.CountOf("iron_ore"));
            Assert.Equal(CarriedContents.BagCapacity, server.CarriedContentsForTest(p.Inventory.Slots[bagSlot]!.Item).Sum(s => s.Count));
        }
    }

    [Fact]
    public void TheBag_TakesManyKinds_GivesThemBack_AndIsPlainOnceEmpty()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Bag, 1, 1);
            p.Inventory.Add("iron_ore", 30, 1024);
            p.Inventory.Add("berries", 12, 1024);
            int berries = p.Inventory.CountOf("berries"); // (the starter kit may carry a few already — one stack all the same)
            int bagSlot = SlotOf(p, Bag);

            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: true));
            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, "berries", toBag: true));
            string filled = p.Inventory.Slots[bagSlot]!.Item;
            Assert.Equal(30 + berries, server.CarriedContentsForTest(filled).Sum(s => s.Count));
            Assert.Equal(0, p.Inventory.CountOf("iron_ore"));

            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: false));
            Assert.Equal(30, p.Inventory.CountOf("iron_ore"));
            Assert.Equal(filled, p.Inventory.Slots[bagSlot]!.Item); // still the same bag while something is in it

            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, string.Empty, toBag: false, all: true)); // take all
            Assert.Equal(berries, p.Inventory.CountOf("berries"));
            Assert.Equal(Bag, p.Inventory.Slots[bagSlot]!.Item); // empty → plain again
            Assert.Equal(0, server.CarriedRecordCountForTest);
        }
    }

    [Fact]
    public void TheBag_RefusesBags_AndPackedCrates()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            FilledCrate(server, p);
            server.MineBlock(p.PlayerId, 1, 200, 0);
            string packed = PackedKey(p);
            p.Inventory.Add(Bag, 2, 1);
            int bagSlot = SlotOf(p, Bag);
            int otherBag = Enumerable.Range(0, p.Inventory.SlotCount).First(i => i != bagSlot && p.Inventory.Slots[i]?.Item == Bag);

            Assert.False(server.MoveBagItemForTest(p.PlayerId, bagSlot, Bag, toBag: true, fromSlot: otherBag));
            Assert.False(server.MoveBagItemForTest(p.PlayerId, bagSlot, packed, toBag: true));

            Assert.Equal(2, p.Inventory.CountOf(Bag));
            Assert.Equal(1, p.Inventory.CountOf(packed));
        }
    }

    [Fact]
    public void AFilledBag_IsSavedWithTheBackpack_AndSurvivesARestart()
    {
        string filled;
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Bag, 1, 1);
            p.Inventory.Add("iron_ore", 25, 1024);
            int bagSlot = SlotOf(p, Bag);
            Assert.True(server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: true));
            filled = p.Inventory.Slots[bagSlot]!.Item;

            // The move wrote the backpack together with the record — no autosave needed for either.
            var saved = repo.LoadPlayer(p.PlayerId)!;
            Assert.Equal(1, saved.Inventory.CountOf(filled));
            Assert.Equal(0, saved.Inventory.CountOf("iron_ore"));
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Started(out var repo2);
        using (repo2)
        {
            var p = again.AddLocalPlayer("Justus").State;
            int bagSlot = SlotOf(p, filled);
            Assert.True(bagSlot >= 0);
            Assert.Contains(again.CarriedContentsForTest(filled), s => s.Item == "iron_ore" && s.Count == 25);

            Assert.True(again.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: false));
            Assert.Equal(25, p.Inventory.CountOf("iron_ore"));
        }
    }

    // ---------------- Duplication safety ----------------

    [Fact]
    public void AFilledBag_CannotBeThrownAway_ButAnEmptyOneCan()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Bag, 1, 1);
            p.Inventory.Add("iron_ore", 5, 1024);
            int bagSlot = SlotOf(p, Bag);
            server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: true);
            string filled = p.Inventory.Slots[bagSlot]!.Item;

            server.DiscardItemForTest(p.PlayerId, bagSlot);
            Assert.Equal(1, p.Inventory.CountOf(filled)); // refused: the ore would be gone with it
            Assert.Equal(1, server.CarriedRecordCountForTest);

            server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: false);
            server.DiscardItemForTest(p.PlayerId, bagSlot);
            Assert.Equal(0, p.Inventory.CountOf(Bag));
            Assert.Equal(5, p.Inventory.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void AFilledBag_LostAtDeath_IsLootedOnce_WithItsContents()
    {
        var server = Started(out var repo, new GameRules { DeathPenalty = DeathPenalty.Normal, KeepInventoryOnDeath = false });
        using (repo)
        {
            var p = Player(server);
            p.Inventory.Add(Bag, 1, 1);
            p.Inventory.Add("iron_ore", 8, 1024);
            int bagSlot = SlotOf(p, Bag);
            server.MoveBagItemForTest(p.PlayerId, bagSlot, "iron_ore", toBag: true);
            string filled = p.Inventory.Slots[bagSlot]!.Item;
            var deathSpot = p.Position;

            server.KillPlayerForTest(server.Sessions.Values.Single(s => s.State == p), "test");

            Assert.Equal(0, p.Inventory.CountOf(filled));
            var capsule = Assert.Single(server.Containers, c => c.Kind == "salvage_capsule");
            Assert.Equal(1, capsule.Items.Single(s => s.Item == filled).Count);

            var finder = Player(server, "Layex");
            finder.Position = deathSpot;
            server.LootContainer(finder.PlayerId, capsule.Id);

            Assert.Equal(1, finder.Inventory.CountOf(filled));
            Assert.Equal(0, p.Inventory.CountOf(filled));
            Assert.Contains(server.CarriedContentsForTest(filled), s => s.Item == "iron_ore" && s.Count == 8);
        }
    }

    [Fact]
    public void AFilledBag_InATrade_ChangesHandsOnce()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = Player(server, "Alice");
            var b = Player(server, "Bob");
            b.Position = new Vector3f(1.5f, 200f, 0.5f);
            a.Inventory.Add(Bag, 1, 1);
            a.Inventory.Add("iron_ore", 3, 1024);
            int bagSlot = SlotOf(a, Bag);
            server.MoveBagItemForTest(a.PlayerId, bagSlot, "iron_ore", toBag: true);
            string filled = a.Inventory.Slots[bagSlot]!.Item;

            server.RequestTrade("Alice", "Bob");
            server.RespondTrade("Bob", true);
            server.SetTradeOffer("Alice", new[] { new ItemAmount(filled, 5) }); // more than one of a kind cannot be offered
            Assert.True(server.ActiveTrade("Alice") is { } open && open.OfferOf("Alice").Count == 0);
            server.SetTradeOffer("Alice", new[] { new ItemAmount(filled, 1) });
            server.ConfirmTrade("Alice");
            server.ConfirmTrade("Bob");

            Assert.Equal(0, a.Inventory.CountOf(filled));
            Assert.Equal(1, b.Inventory.CountOf(filled));
            Assert.Contains(server.CarriedContentsForTest(filled), s => s.Item == "iron_ore" && s.Count == 3);
        }
    }

    [Fact]
    public void ARecordKey_AskedFromTheSandboxCatalog_ComesOutEmpty()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var owner = Player(server);
            owner.Inventory.Add(Bag, 1, 1);
            owner.Inventory.Add("iron_ore", 4, 1024);
            server.MoveBagItemForTest(owner.PlayerId, SlotOf(owner, Bag), "iron_ore", toBag: true);
            string filled = PackedKey(owner);

            var cheat = Player(server, "Copycat");
            cheat.ModeOverride = PlayerModeOverride.Creative;
            server.CreativeTakeItemForTest(cheat.PlayerId, filled, 1);

            Assert.Equal(0, cheat.Inventory.CountOf(filled)); // no second key to somebody's bag
            Assert.Equal(1, cheat.Inventory.CountOf(Bag));    // an empty one instead
        }
    }

    // ---------------- The wire ----------------

    [Fact]
    public void TheBagMessages_AreRegistered_AndDispatched()
    {
        var back = Assert.IsType<MoveBagItemIntent>(NetCodec.Decode(NetCodec.Encode(new MoveBagItemIntent { BagSlot = 4, Item = "stone", ToBag = true, FromSlot = 9 })));
        Assert.Equal(4, back.BagSlot);
        Assert.Equal(9, back.FromSlot);
        var contents = Assert.IsType<BagContents>(NetCodec.Decode(NetCodec.Encode(new BagContents { Slot = 2, Total = 7, Capacity = 1000 })));
        Assert.Equal(7, contents.Total);
        Assert.IsType<OpenBagIntent>(NetCodec.Decode(NetCodec.Encode(new OpenBagIntent { Slot = 1 })));

        Assert.Contains(typeof(OpenBagIntent), SvGameServer.HandledIntentTypesForTest);
        Assert.Contains(typeof(MoveBagItemIntent), SvGameServer.HandledIntentTypesForTest);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Feed &amp; bond (#1225): the bond number goes up when you feed the animal, down slowly when you do not
/// (never below where taming starts), and the three tiers hang off it. The clock is pinned so a "day apart"
/// is a number, not a wait.
/// </summary>
public sealed class CompanionBondTests : IDisposable
{
    private const long Now = 1_700_000_000_000L;
    private const long Day = 24L * 60 * 60 * 1000;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public CompanionBondTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bond_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        SvGameServer.UnixMsOverrideForTest = Now;
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 3,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    /// <summary>A player with a companion at the given bond, fed (or tamed) right now.</summary>
    private static (PlayerSession Keeper, TamedCreature Pet) KeeperWithPet(SvGameServer server, int bond, long lastFed = Now)
    {
        var keeper = server.AddLocalPlayer("Keeper");
        keeper.State.AboardShip = false;
        var sp = server.SpeciesRoster.First();
        var pet = new TamedCreature
        {
            Id = "c1",
            HomeBodyId = server.ActiveLocationId,
            Name = "Flöckchen",
            SpeciesId = sp.Id,
            Species = sp,
            SizeScale = 1f,
            Bond = bond,
            TamedAtUtc = Now - (30 * Day),
            LastFedUtc = lastFed,
        };
        keeper.State.TamedCreatures.Add(pet);
        return (keeper, pet);
    }

    private static IEnumerable<string> MessagesTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text);

    private static IEnumerable<string> RejectionsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason);

    private static NetCompanion LastRoster(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<CompanionList>().Last().Companions.Single(c => c.Id == "c1");

    [Fact]
    public void Feeding_SpendsOneBait_AddsFiveBond_AndSaysWhatItBought()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_feed", transport, out var repo);
        var (keeper, pet) = KeeperWithPet(server, bond: 45, lastFed: Now - 120_000);
        keeper.State.Inventory.Add("forage_bait", 2, 16);
        transport.Sent.Clear();

        server.FeedCompanionForTest("Keeper", "c1");

        Assert.Equal(50, pet.Bond);
        Assert.Equal(1, keeper.State.Inventory.CountOf("forage_bait"));
        Assert.Equal(Now, pet.LastFedUtc);
        Assert.Contains("@srv.companion.fed:Flöckchen", MessagesTo(transport, keeper));
        Assert.Contains("@srv.companion.tier_fetch:Flöckchen", MessagesTo(transport, keeper)); // 45 → 50 crossed a tier

        // …and it is saved straight away, not only on the next autosave.
        var reloaded = repo.LoadPlayer("Keeper")!.TamedCreatures.Single();
        Assert.Equal(50, reloaded.Bond);
        Assert.Equal(Now, reloaded.LastFedUtc);
    }

    [Fact]
    public void AnyBait_Works_NotJustTheOneThePetPreferredWhenWild()
    {
        // Taming hides a per-animal preference; refusing the food a child actually brought would be a bad
        // second lesson, so every bait feeds every companion.
        var transport = new RecordingTransport();
        var server = NewServer("bond_anybait", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 40, lastFed: Now - 120_000);

        keeper.State.Inventory.Add("nectar_lure", 1, 16);
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(45, pet.Bond);

        SvGameServer.UnixMsOverrideForTest = Now + 61_000; // past the minute
        keeper.State.Inventory.Add("meat_bait", 1, 16);
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(50, pet.Bond);
    }

    [Fact]
    public void NoBait_NoChange_AndThePlayerIsToldWhatToBring()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_nobait", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 45, lastFed: Now - 120_000);
        transport.Sent.Clear();

        server.FeedCompanionForTest("Keeper", "c1");

        Assert.Equal(45, pet.Bond);
        Assert.Contains("@srv.companion.no_food", RejectionsTo(transport, keeper));
    }

    [Fact]
    public void AMinuteBetweenMeals_SoADoubleClickCostsOneBait()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_cooldown", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 40, lastFed: Now - 120_000);
        keeper.State.Inventory.Add("forage_bait", 3, 16);

        server.FeedCompanionForTest("Keeper", "c1");
        server.FeedCompanionForTest("Keeper", "c1"); // straight away again

        Assert.Equal(45, pet.Bond);
        Assert.Equal(2, keeper.State.Inventory.CountOf("forage_bait"));
        Assert.Contains("@srv.companion.fed_recently", RejectionsTo(transport, keeper));

        SvGameServer.UnixMsOverrideForTest = Now + 61_000;
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(50, pet.Bond);
    }

    [Fact]
    public void TheBond_StopsAtAHundred()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_full", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 98, lastFed: Now - 120_000);
        keeper.State.Inventory.Add("forage_bait", 2, 16);

        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(100, pet.Bond);

        SvGameServer.UnixMsOverrideForTest = Now + 61_000;
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(100, pet.Bond);
        Assert.Equal(1, keeper.State.Inventory.CountOf("forage_bait")); // the second bait is NOT spent
        Assert.Contains("@srv.companion.bond_full", RejectionsTo(transport, keeper));
    }

    [Fact]
    public void Decay_IsOnePointPerWholeDay_AndNeverGoesBelowTheFloor()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_decay", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 60, lastFed: Now - (5 * Day) - (Day / 2)); // 5½ days ago

        // A feeding attempt charges the time apart first — even when it then fails for want of bait.
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(55, pet.Bond); // five whole days; the half day keeps ticking
        Assert.Equal(Now - (Day / 2), pet.LastFedUtc);

        // A long holiday costs the perks, never the friendship: the floor is where taming starts.
        pet.Bond = 43;
        pet.LastFedUtc = Now - (40 * Day);
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(TamedCreature.BondFloor, pet.Bond);

        // And below the floor nothing is ever taken, however long it has been.
        pet.Bond = 40;
        pet.LastFedUtc = Now - (400 * Day);
        server.FeedCompanionForTest("Keeper", "c1");
        Assert.Equal(40, pet.Bond);
    }

    [Fact]
    public void TheFetchReach_GrowsAtFifty()
    {
        var transport = new RecordingTransport();
        var server = NewServer("bond_fetch", transport, out _);
        var (_, pet) = KeeperWithPet(server, bond: 49);

        float before = server.CompanionFetchRadiusForTest("Keeper", "c1");
        pet.Bond = 50;
        float after = server.CompanionFetchRadiusForTest("Keeper", "c1");

        Assert.Equal(before * 1.5f, after, 0.001f);
    }

    [Fact]
    public void TheRoster_SaysWhetherFeedingWouldDoAnything()
    {
        // The Feed button is dimmed rather than failing silently, so the flag must track all three reasons.
        var transport = new RecordingTransport();
        var server = NewServer("bond_canfeed", transport, out _);
        var (keeper, pet) = KeeperWithPet(server, bond: 45, lastFed: Now - 120_000);
        keeper.State.Inventory.Add("forage_bait", 2, 16);

        // The real wire path — the intent through the codec and the dispatch, not the seam.
        server.HandlePayloadForTest(keeper.ConnectionId, NetCodec.Encode(new FeedCompanionIntent { CompanionId = "c1" }));
        Assert.Equal(50, pet.Bond);
        Assert.False(LastRoster(transport, keeper).CanFeed, "just fed: the minute has not passed");

        // A minute later, bait still in the pack: the button is live again. A rename re-sends the roster
        // without touching the bond, which is the cleanest way to read the flag on its own.
        SvGameServer.UnixMsOverrideForTest = Now + 61_000;
        server.RenameCompanionForTest("Keeper", "c1", "Flöckchen");
        Assert.True(LastRoster(transport, keeper).CanFeed);

        // No bait left: grey.
        keeper.State.Inventory.Remove("forage_bait", 1);
        server.RenameCompanionForTest("Keeper", "c1", "Flöckchen");
        Assert.False(LastRoster(transport, keeper).CanFeed);

        // Bond full: grey, whatever is in the pack.
        keeper.State.Inventory.Add("forage_bait", 1, 16);
        pet.Bond = 100;
        server.RenameCompanionForTest("Keeper", "c1", "Flöckchen");
        Assert.False(LastRoster(transport, keeper).CanFeed);
    }

    public void Dispose()
    {
        SvGameServer.UnixMsOverrideForTest = null;
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

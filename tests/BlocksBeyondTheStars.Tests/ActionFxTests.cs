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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The VFX overhaul's server half: a player's cosmetic <see cref="FxIntent"/> is relayed as <see cref="ActionFx"/> to the
/// others nearby in the same world — never back to the sender, never across worlds, rate-limited and plausibility-
/// checked (#2158); a successful gadget use is confirmed to the user AND the bystanders (#2158); a killed creature is
/// announced with <see cref="CreatureDefeated"/>, a despawned one is not (#2154). Everything runs through the real
/// receive path (<c>HandlePayloadForTest</c>) where an intent is involved, and no test ticks more than once.
/// </summary>
public sealed class ActionFxTests : IDisposable
{
    private const float Sky = 300f; // open sky: nothing blocks a sightline and nothing ground-snaps without ticks

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ActionFxTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_actionfx_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every per-connection server send so a test can assert who received what.</summary>
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

    private SvGameServer NewServer(string name, RecordingTransport transport, string planet = "rocky", Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static PlayerSession OnFoot(SvGameServer server, string name, float x, float z)
    {
        var s = server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(x, Sky, z);
        return s;
    }

    private static void SendFx(SvGameServer server, PlayerSession from, FxIntent intent)
        => server.HandlePayloadForTest(from.ConnectionId, NetCodec.Encode(intent));

    private static FxIntent Shot(Vector3f from, Vector3f to, string item = "laser_pistol", byte kind = FxActionKinds.Shot)
        => new()
        {
            Kind = kind,
            ItemKey = item,
            FromX = from.X,
            FromY = from.Y,
            FromZ = from.Z,
            ToX = to.X,
            ToY = to.Y,
            ToZ = to.Z,
            Hit = true,
        };

    private static List<ActionFx> FxTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is ActionFx).Select(x => (ActionFx)x.Msg).ToList();

    private static List<CreatureDefeated> DefeatsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId && x.Msg is CreatureDefeated).Select(x => (CreatureDefeated)x.Msg).ToList();

    // ---------------- Relay (#2158) ----------------

    [Fact]
    public void Relay_ReachesANearbyPlayerOfTheSameWorld_NotTheSender_NotTheFar_NotAnotherWorld()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_relay", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 110f, 104f);       // 10 blocks away
        var carol = OnFoot(server, "Carol", 250f, 100f);   // 150 blocks away — beyond the 96-block audience
        var dave = OnFoot(server, "Dave", 110f, 104f);     // Bob's spot, but on another body
        dave.CurrentLocationId = "elsewhere";

        t.Sent.Clear();
        SendFx(server, alice, Shot(new Vector3f(101f, Sky + 1.5f, 100.5f), new Vector3f(120f, Sky + 1f, 100.5f)));

        var got = Assert.Single(FxTo(t, bob));
        Assert.Equal("Alice", got.PlayerId);
        Assert.Equal(FxActionKinds.Shot, got.Kind);
        Assert.Equal("laser_pistol", got.ItemKey);
        Assert.Equal(101f, got.FromX, 3);
        Assert.Equal(Sky + 1.5f, got.FromY, 3);
        Assert.Equal(120f, got.ToX, 3);
        Assert.True(got.Hit);
        Assert.False(got.Outcome); // a relayed action, not a server-confirmed outcome

        Assert.Empty(FxTo(t, alice)); // never echoed to the sender
        Assert.Empty(FxTo(t, carol));
        Assert.Empty(FxTo(t, dave));
    }

    [Fact]
    public void Relay_IsSeamAware_AndHandsTheReceiverOneStraightSegment()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_seam", t);
        int circ = server.World.Circumference;
        var alice = OnFoot(server, "Alice", circ - 2f, 50f); // just west of the longitude seam
        var bob = OnFoot(server, "Bob", 3f, 50f);            // just east of it — 5 blocks away, not a world away

        t.Sent.Clear();
        // The sender's client runs unwrapped: it aims east across the seam at x = circ + 10.
        SendFx(server, alice, Shot(new Vector3f(circ - 1.5f, Sky + 1f, 50f), new Vector3f(circ + 10f, Sky + 1f, 50f)));

        var got = Assert.Single(FxTo(t, bob));
        Assert.Equal(circ - 1.5f, got.FromX, 2);           // canonical start, like a presence position
        Assert.Equal(11.5f, got.ToX - got.FromX, 2);       // the end keeps the short way round
    }

    [Fact]
    public void Relay_DropsEverythingBeyondTwelvePerSecond_AndRefillsWithTime()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_rate", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104f, 100f);
        var shot = Shot(new Vector3f(101f, Sky + 1f, 100.5f), new Vector3f(115f, Sky + 1f, 100.5f));

        t.Sent.Clear();
        for (int i = 0; i < 20; i++)
        {
            SendFx(server, alice, shot);
        }

        Assert.Equal(12, FxTo(t, bob).Count); // the burst; the 13th and later are dropped

        server.AdvanceUptimeForTest(1.0); // one second later the bucket is full again
        for (int i = 0; i < 20; i++)
        {
            SendFx(server, alice, shot);
        }

        Assert.Equal(24, FxTo(t, bob).Count);
    }

    [Fact]
    public void Relay_DropsAStartFarFromTheSender_AndAnEndFarFromTheStart()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_range", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104f, 100f);

        t.Sent.Clear();
        SendFx(server, alice, Shot(new Vector3f(110.5f, Sky + 1f, 100.5f), new Vector3f(120f, Sky + 1f, 100.5f))); // starts 10 blocks off
        SendFx(server, alice, Shot(new Vector3f(101f, Sky + 1f, 100.5f), new Vector3f(171f, Sky + 1f, 100.5f)));   // 70 blocks long
        Assert.Empty(FxTo(t, bob));

        SendFx(server, alice, Shot(new Vector3f(101f, Sky + 1f, 100.5f), new Vector3f(160f, Sky + 1f, 100.5f)));   // 59 long: fine
        Assert.Single(FxTo(t, bob));
    }

    [Fact]
    public void Relay_DropsInvalidKindsKeysAndNumbers()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_invalid", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104f, 100f);
        var from = new Vector3f(101f, Sky + 1f, 100.5f);
        var to = new Vector3f(110f, Sky + 1f, 100.5f);

        t.Sent.Clear();
        SendFx(server, alice, Shot(from, to, kind: 0));
        SendFx(server, alice, Shot(from, to, kind: 7));
        SendFx(server, alice, Shot(from, to, item: new string('x', 49)));
        SendFx(server, alice, Shot(from, to, item: "not_a_real_item"));
        SendFx(server, alice, Shot(new Vector3f(float.NaN, Sky, 100f), to));
        SendFx(server, alice, Shot(from, new Vector3f(110f, float.PositiveInfinity, 100f)));
        Assert.Empty(FxTo(t, bob));

        SendFx(server, alice, Shot(from, to, item: "", kind: FxActionKinds.Melee));         // bare hands are fine
        SendFx(server, alice, Shot(from, to, item: "ship_laser_basic", kind: FxActionKinds.Shot)); // so is a ship module
        Assert.Equal(2, FxTo(t, bob).Count);
    }

    [Fact]
    public void Relay_FromSpace_StaysInTheFlightInstance()
    {
        // A pilot's positions are the flight instance's frame, not the body's surface: the shot reaches the other
        // pilot of the instance, and never the player standing on the body below at the same numbers.
        var t = new RecordingTransport();
        var server = NewServer("fx_space", t, planet: "jungle", configure: c => c.Rules.FreeSpaceFlight = true);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        var carol = OnFoot(server, "Carol", 21f, 1f);
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");
        Assert.True(server.InSpace("Alice") && server.InSpace("Bob"));
        Assert.Equal(server.SpaceInstanceIdForTest("Alice"), server.SpaceInstanceIdForTest("Bob"));
        server.ShipMove("Alice", 0f, 0f, 0f);
        server.ShipMove("Bob", 20f, 0f, 0f);
        carol.State.Position = new Vector3f(20f, 0f, 0f);

        t.Sent.Clear();
        SendFx(server, alice, Shot(new Vector3f(3f, 0f, 0f), new Vector3f(60f, 0f, 0f), item: "ship_laser_basic"));

        var got = Assert.Single(FxTo(t, bob));
        Assert.Equal("ship_laser_basic", got.ItemKey);
        Assert.Equal(60f, got.ToX, 3); // the instance frame is passed through untouched
        Assert.Empty(FxTo(t, carol));
        Assert.Empty(FxTo(t, alice));
    }

    // ---------------- Gadget outcomes (#2158) ----------------

    [Fact]
    public void GadgetSuccess_IsConfirmedToTheUser_AndToANearbyPlayer()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_gadget", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104f, 100f);
        var carol = OnFoot(server, "Carol", 250f, 100f);
        alice.State.Inventory.Add("field_medkit", 1, 1);
        alice.State.Health = 40f;

        t.Sent.Clear();
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new UseGadgetIntent { GadgetKey = "field_medkit", X = 100.5f, Y = Sky, Z = 100.5f }));

        Assert.True(alice.State.Health > 40f, "the medkit worked");
        foreach (var who in new[] { alice, bob })
        {
            var got = Assert.Single(FxTo(t, who));
            Assert.True(got.Outcome);
            Assert.True(got.Hit);
            Assert.Equal(FxActionKinds.Gadget, got.Kind);
            Assert.Equal("field_medkit", got.ItemKey);
            Assert.Equal("Alice", got.PlayerId);
            Assert.Equal(100.5f, got.FromX, 3);
        }

        Assert.Empty(FxTo(t, carol)); // out of sight
    }

    [Fact]
    public void RefusedGadget_BroadcastsNothing()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_gadget_refused", t);
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 104f, 100f);
        alice.State.Inventory.Add("field_medkit", 1, 1);
        alice.State.Health = 40f;
        alice.State.SuitEnergy = 0f; // the medkit needs 18

        t.Sent.Clear();
        server.HandlePayloadForTest(alice.ConnectionId, NetCodec.Encode(new UseGadgetIntent { GadgetKey = "field_medkit", X = 100.5f, Y = Sky, Z = 100.5f }));

        Assert.Equal(40f, alice.State.Health);
        Assert.Empty(FxTo(t, alice));
        Assert.Empty(FxTo(t, bob));
    }

    // ---------------- Creature defeat (#2154) ----------------

    [Fact]
    public void KillingACreature_AnnouncesItsDefeat_BeforeTheCreatureListDropsIt()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_defeat", t, planet: "jungle");
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);
        var bob = OnFoot(server, "Bob", 106f, 100f);
        var dave = OnFoot(server, "Dave", 106f, 100f);
        dave.CurrentLocationId = "elsewhere";

        string id = server.SpawnCreatureAtForTest(new Vector3f(102.5f, Sky, 100.5f));
        var creature = server.Creatures.First(c => c.Id == id);
        creature.Hull = 1f; // one bare-handed hit is enough

        t.Sent.Clear();
        server.AttackEntity("Alice", id);
        Assert.DoesNotContain(server.Creatures, c => c.Id == id);

        foreach (var who in new[] { alice, bob })
        {
            var defeat = Assert.Single(DefeatsTo(t, who));
            Assert.Equal(id, defeat.Id);
            Assert.Equal(102.5f, defeat.X, 3);
            Assert.Equal(Sky, defeat.Y, 3);
        }

        Assert.Empty(DefeatsTo(t, dave));

        // The end-of-tick creature list (which no longer has it) comes AFTER the defeat on the same reliable channel.
        dave.CurrentLocationId = alice.CurrentLocationId; // no tick for a made-up body
        server.TickForTest(0.05);
        var toAlice = t.Sent.Where(x => x.Conn == alice.ConnectionId).Select(x => x.Msg).ToList();
        int defeatAt = toAlice.FindIndex(m => m is CreatureDefeated);
        int listAt = toAlice.FindIndex(m => m is CreatureList);
        Assert.True(listAt > defeatAt, "the creature list that drops the animal follows its defeat");
        Assert.DoesNotContain(((CreatureList)toAlice[listAt]).Creatures, c => c.Id == id);
    }

    [Fact]
    public void ADespawnedCreature_JustDisappears()
    {
        var t = new RecordingTransport();
        var server = NewServer("fx_despawn", t, planet: "jungle");
        var alice = OnFoot(server, "Alice", 100.5f, 100.5f);

        // Far beyond every player's view: the next creature tick prunes it — a despawn, not a defeat.
        string id = server.SpawnCreatureAtForTest(new Vector3f(600.5f, Sky, 100.5f));

        t.Sent.Clear();
        server.TickForTest(0.05);

        Assert.DoesNotContain(server.Creatures, c => c.Id == id);
        Assert.Empty(DefeatsTo(t, alice));
        Assert.DoesNotContain(t.Sent, x => x.Msg is CreatureDefeated);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

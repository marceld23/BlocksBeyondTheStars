// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Tiered radio reach for comms — the same recipient-selection drives BOTH text chat and live voice relay.
/// A basic <c>comm_radio</c> reaches the same world; a <c>system_radio</c> upgrade reaches the whole star
/// system; a <c>galaxy_radio</c> reaches everyone. Voice is an opt-in, opaque relay over the same audience,
/// sent to everyone in reach EXCEPT the speaker. These headless tests lock in who-hears-whom per tier.
/// </summary>
public sealed class RadioTierTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public RadioTierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_radio_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every per-connection server send so a test can assert who received which message.</summary>
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

    private SvGameServer NewServer(string name, RecordingTransport transport, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            // Guarantee several star systems so the system/galaxy reach tiers are distinguishable.
            World = new WorldDescription { StarSystemCount = 4 },
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    // ---------------- Text chat reach ----------------

    [Fact]
    public void CommRadio_ReachesSameWorldOnly()
    {
        var transport = new RecordingTransport();
        var server = NewServer("radio_local", transport);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");        // same world as Alice
        var carol = server.AddLocalPlayer("Carol");
        carol.CurrentLocationId = OtherBodySameSystem(server, alice.CurrentLocationId);

        alice.State.Inventory.Add("comm_radio", 1, 1);

        transport.Sent.Clear();
        server.Chat("Alice", "hello on this rock");

        Assert.True(GotChat(transport, bob, "hello on this rock"), "Bob on the same world must hear a comm_radio.");
        Assert.False(GotChat(transport, carol, "hello on this rock"), "Carol on another body must NOT hear a comm_radio.");
    }

    [Fact]
    public void SystemRadio_ReachesWholeSystem_NotOtherSystems()
    {
        var transport = new RecordingTransport();
        var server = NewServer("radio_system", transport);

        var alice = server.AddLocalPlayer("Alice");
        var carol = server.AddLocalPlayer("Carol");
        var dave = server.AddLocalPlayer("Dave");
        carol.CurrentLocationId = OtherBodySameSystem(server, alice.CurrentLocationId);
        dave.CurrentLocationId = BodyInOtherSystem(server, alice.CurrentLocationId);

        alice.State.Inventory.Add("system_radio", 1, 1);

        transport.Sent.Clear();
        server.Chat("Alice", "system net check");

        Assert.True(GotChat(transport, carol, "system net check"), "Carol in the same system must hear a system_radio.");
        Assert.False(GotChat(transport, dave, "system net check"), "Dave in another system must NOT hear a system_radio.");
    }

    [Fact]
    public void GalaxyRadio_ReachesEveryone()
    {
        var transport = new RecordingTransport();
        var server = NewServer("radio_galaxy", transport);

        var alice = server.AddLocalPlayer("Alice");
        var dave = server.AddLocalPlayer("Dave");
        dave.CurrentLocationId = BodyInOtherSystem(server, alice.CurrentLocationId);

        alice.State.Inventory.Add("galaxy_radio", 1, 1);

        transport.Sent.Clear();
        server.Chat("Alice", "galaxy broadcast");

        Assert.True(GotChat(transport, dave, "galaxy broadcast"), "Dave anywhere must hear a galaxy_radio.");
    }

    [Fact]
    public void NoRadio_ChatIsRejected()
    {
        var transport = new RecordingTransport();
        var server = NewServer("radio_none", transport);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");

        transport.Sent.Clear();
        server.Chat("Alice", "anyone there?");

        Assert.False(transport.Sent.Any(x => x.Msg is ChatMessage), "No radio → no chat is relayed.");
        Assert.True(transport.Sent.Any(x => x.Conn == alice.ConnectionId && x.Msg is ActionRejected r && r.Action == "chat"),
            "No radio → the sender gets a rejection.");
        _ = bob;
    }

    // ---------------- Voice relay ----------------

    [Fact]
    public void Voice_Disabled_DropsFrame()
    {
        var transport = new RecordingTransport();
        var server = NewServer("voice_off", transport); // VoiceChatEnabled defaults to false

        var alice = server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        alice.State.Inventory.Add("comm_radio", 1, 1);

        transport.Sent.Clear();
        server.SendVoice("Alice", new byte[] { 1, 2, 3, 4 }, sequence: 1);

        Assert.False(transport.Sent.Any(x => x.Msg is VoiceFrame), "Voice disabled → no frame is relayed.");
    }

    [Fact]
    public void Voice_Enabled_RelaysToTierAudience_StampsSender_NotSelf()
    {
        var transport = new RecordingTransport();
        var server = NewServer("voice_on", transport, c => c.VoiceChatEnabled = true);

        var alice = server.AddLocalPlayer("Alice");
        var carol = server.AddLocalPlayer("Carol");
        var dave = server.AddLocalPlayer("Dave");
        carol.CurrentLocationId = OtherBodySameSystem(server, alice.CurrentLocationId);
        dave.CurrentLocationId = BodyInOtherSystem(server, alice.CurrentLocationId);

        alice.State.Inventory.Add("system_radio", 1, 1);

        transport.Sent.Clear();
        server.SendVoice("Alice", new byte[] { 9, 8, 7, 6 }, sequence: 42);

        var toCarol = transport.Sent
            .Where(x => x.Conn == carol.ConnectionId && x.Msg is VoiceFrame)
            .Select(x => (VoiceFrame)x.Msg)
            .FirstOrDefault();
        Assert.NotNull(toCarol);
        Assert.Equal("Alice", toCarol!.FromPlayerId);             // server stamps the authoritative sender id
        Assert.Equal(42, toCarol.Sequence);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, toCarol.Opus);    // payload relayed opaquely, unchanged

        Assert.False(transport.Sent.Any(x => x.Conn == dave.ConnectionId && x.Msg is VoiceFrame),
            "Dave in another system must NOT receive a system_radio voice frame.");
        Assert.False(transport.Sent.Any(x => x.Conn == alice.ConnectionId && x.Msg is VoiceFrame),
            "The speaker must not hear their own relayed voice.");
    }

    [Fact]
    public void Voice_NoRadio_DropsFrame()
    {
        var transport = new RecordingTransport();
        var server = NewServer("voice_noradio", transport, c => c.VoiceChatEnabled = true);

        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");

        transport.Sent.Clear();
        server.SendVoice("Alice", new byte[] { 1, 2, 3, 4 }, sequence: 1);

        Assert.False(transport.Sent.Any(x => x.Msg is VoiceFrame), "No radio → no voice is relayed.");
    }

    [Fact]
    public void Voice_OversizedFrame_IsDropped()
    {
        var transport = new RecordingTransport();
        var server = NewServer("voice_big", transport, c => c.VoiceChatEnabled = true);

        var alice = server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        alice.State.Inventory.Add("galaxy_radio", 1, 1);

        transport.Sent.Clear();
        server.SendVoice("Alice", new byte[8000], sequence: 1); // over the 4 KB per-frame ceiling

        Assert.False(transport.Sent.Any(x => x.Msg is VoiceFrame), "An oversized voice frame must be dropped.");
    }

    // ---------------- Helpers ----------------

    private static bool GotChat(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession to, string text)
        => t.Sent.Any(x => x.Conn == to.ConnectionId && x.Msg is ChatMessage m && m.Text == text);

    private static string OtherBodySameSystem(SvGameServer server, string fromBodyId)
    {
        string sysId = server.Galaxy.FindBody(fromBodyId)!.SystemId;
        var body = server.Galaxy.AllBodies().FirstOrDefault(b => b.Id != fromBodyId && b.SystemId == sysId);
        Assert.NotNull(body); // the test world must have ≥2 bodies in the start system
        return body!.Id;
    }

    private static string BodyInOtherSystem(SvGameServer server, string fromBodyId)
    {
        string sysId = server.Galaxy.FindBody(fromBodyId)!.SystemId;
        var body = server.Galaxy.AllBodies().FirstOrDefault(b => !string.IsNullOrEmpty(b.SystemId) && b.SystemId != sysId);
        Assert.NotNull(body); // the test world must have ≥2 star systems
        return body!.Id;
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

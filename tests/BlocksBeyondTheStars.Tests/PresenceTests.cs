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
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Multiplayer presence: the server broadcasts each player to the others (M24).</summary>
public sealed class PresenceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PresenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_presence_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>A transport that records every server send so we can assert who received what.</summary>
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

    [Fact]
    public void PresenceWithAppearance_IsBroadcastToOtherPlayers()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "presence"));
        var transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = "presence", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        bob.State.Position = new Vector3f(5, 64, 7);
        server.SetAppearance("Bob", 0x112233, 0x445566, 0x778899, 0xAABBCC);

        transport.Sent.Clear();
        server.Tick(0.2); // > presence interval -> broadcast

        var toAlice = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .FirstOrDefault();

        Assert.NotNull(toAlice);
        Assert.Equal("Bob", toAlice!.Name);
        Assert.Equal(0x112233, toAlice.Skin);
        Assert.Equal(5f, toAlice.X);
    }

    [Fact]
    public void OrbitingPlayer_IsStealthMarked_SoNoGhostAvatarStaysOnThePad()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "orbit"));
        var transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = "orbit", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        var alice = server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        server.EnterSpace("Bob"); // Bob launches — his surface avatar must vanish for Alice

        transport.Sent.Clear();
        server.Tick(0.2);

        var toAlice = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .FirstOrDefault();

        Assert.NotNull(toAlice);
        Assert.True(toAlice!.Stealthed, "a player flying in space must not keep standing at the pad as a frozen ghost");
    }

    [Fact]
    public void NoPresence_WhenAlone()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "alone"));
        var transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = "alone", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        server.AddLocalPlayer("Solo");

        transport.Sent.Clear();
        server.Tick(0.2);

        Assert.DoesNotContain(transport.Sent, x => x.Msg is PlayerPresence);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

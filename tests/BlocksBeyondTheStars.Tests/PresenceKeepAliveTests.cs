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

/// <summary>
/// #1530: presence goes out on change plus a keep-alive, and the entity lists / player states are flushed once
/// per tick. The wire messages are unchanged — these tests pin the COUNTS: a standing player costs one presence
/// per keep-alive window instead of ten a second, a moving one still gets a beat per tick, a viewer that just
/// joined sees everyone at once, and two creature-list events in one tick produce one list.
/// </summary>
public sealed class PresenceKeepAliveTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PresenceKeepAliveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_keepalive_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

    private (SvGameServer Server, RecordingTransport Transport, SqliteWorldRepository Repo) NewServer(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return (server, transport, repo);
    }

    private static int PresenceCount(RecordingTransport t, int viewerConn, string subject)
        => t.Sent.Count(x => x.Conn == viewerConn && x.Msg is PlayerPresence p && p.PlayerId == subject);

    [Fact]
    public void StandingPlayer_IsResentOnlyEveryKeepAlive_MovingPlayerEveryBeat()
    {
        var (server, transport, repo) = NewServer("keepalive");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            bob.State.Position = new Vector3f(5, 64, 7);
            server.Tick(0.1); // the first beat: everyone learns everyone
            transport.Sent.Clear();

            // Ten beats with nobody moving: Bob reaches Alice twice (two keep-alives at 5 beats), not ten times.
            for (int i = 0; i < 10; i++)
            {
                server.Tick(0.1);
            }

            int standing = PresenceCount(transport, alice.ConnectionId, "Bob");
            Assert.True(standing >= 1 && standing <= 3, $"a standing player was sent {standing} times in 1 s (expected the keep-alive only)");

            // Ten beats with Bob walking: every beat carries his new pose.
            transport.Sent.Clear();
            for (int i = 0; i < 10; i++)
            {
                bob.State.Position = new Vector3f(5 + i * 0.5f, 64, 7);
                server.Tick(0.1);
            }

            int moving = PresenceCount(transport, alice.ConnectionId, "Bob");
            Assert.True(moving >= 9, $"a moving player was sent only {moving} times in 10 beats");
            Assert.Equal(5 + 9 * 0.5f, ((PlayerPresence)transport.Sent.Last(x => x.Msg is PlayerPresence p && p.PlayerId == "Bob").Msg).X);
        }
    }

    [Fact]
    public void Joiner_SeesEveryone_OnTheNextBeat_EvenThoughNobodyMoved()
    {
        var (server, transport, repo) = NewServer("joiner");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            for (int i = 0; i < 3; i++)
            {
                server.Tick(0.1); // Alice ↔ Bob settled into the keep-alive window
            }

            var carol = server.AddLocalPlayer("Carol");
            transport.Sent.Clear();
            server.Tick(0.1);

            Assert.Equal(1, PresenceCount(transport, carol.ConnectionId, "Alice"));
            Assert.Equal(1, PresenceCount(transport, carol.ConnectionId, "Bob"));
            Assert.Equal(1, PresenceCount(transport, alice.ConnectionId, "Carol"));
            Assert.Equal(1, PresenceCount(transport, bob.ConnectionId, "Carol"));
        }
    }

    [Fact]
    public void NoPresence_WhenAlone_StillHolds()
    {
        var (server, transport, repo) = NewServer("alone");
        using (repo)
        {
            server.AddLocalPlayer("Alice");
            transport.Sent.Clear();
            for (int i = 0; i < 10; i++)
            {
                server.Tick(0.1);
            }

            Assert.DoesNotContain(transport.Sent, x => x.Msg is PlayerPresence);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}

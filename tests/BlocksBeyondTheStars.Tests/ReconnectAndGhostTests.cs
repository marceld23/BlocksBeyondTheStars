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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Getting back in after a client dies badly (#964), and the ghost-block amplifier (#965). Both come from the
/// 2026-08-12 LAN playtest, where a player whose PC crashed could not rejoin: his session kept looking alive
/// (the transport peer answered pings from its own thread long after the game was gone), so it held his name
/// and his slot — with no heartbeat and no way to evict it.
/// </summary>
public sealed class ReconnectAndGhostTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public ReconnectAndGhostTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_reconn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records what each connection was sent, and which ones the server hung up on.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();
        public readonly List<int> Disconnected = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void DisconnectClient(int connectionId) => Disconnected.Add(connectionId);
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private static void Join(SvGameServer server, int connectionId, string name, string token)
        => server.HandlePayloadForTest(connectionId, NetCodec.Encode(new JoinRequest { PlayerName = name, Token = token }));

    // ---------------- #964: reconnect ----------------

    [Fact]
    public void Rejoin_WithTheMatchingToken_EvictsTheStaleSession()
    {
        // The playtest case: the first session is still "joined" (its transport peer never noticed the crash).
        // Presenting the same name AND the token that claimed it proves ownership, so the old session goes.
        var transport = new RecordingTransport();
        var server = NewServer("reconn_evict", transport);

        Join(server, 1, "Pilot", "token-a");
        Assert.Equal(1, server.JoinedPlayerCountForTest);

        transport.Sent.Clear();
        Join(server, 2, "Pilot", "token-a"); // same player coming back on a fresh connection

        Assert.Contains(1, transport.Disconnected);        // the ghost session was hung up on
        Assert.Equal(1, server.JoinedPlayerCountForTest);  // …and replaced, not duplicated
        Assert.Contains(transport.Sent, x => x.Conn == 2 && x.Msg is JoinAccepted);
        Assert.DoesNotContain(transport.Sent, x => x.Conn == 2 && x.Msg is JoinRejected);
    }

    [Fact]
    public void Rejoin_WithAWrongToken_IsStillRefused()
    {
        // Eviction must not become a name-stealing tool: without the right token the live session stands.
        var transport = new RecordingTransport();
        var server = NewServer("reconn_wrongtoken", transport);

        Join(server, 1, "Pilot", "token-a");
        transport.Sent.Clear();
        Join(server, 2, "Pilot", "token-b");

        Assert.DoesNotContain(1, transport.Disconnected);
        Assert.Contains(transport.Sent, x => x.Conn == 2 && x.Msg is JoinRejected);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
    }

    [Fact]
    public void ASilentSession_IsDroppedByTheHeartbeat()
    {
        // A frozen/dead client keeps its transport peer alive, so only the absence of INTENTS reveals it.
        var transport = new RecordingTransport();
        var server = NewServer("reconn_heartbeat", transport);

        Join(server, 1, "Pilot", "token-a");
        server.HandlePayloadForTest(1, NetCodec.Encode(new MoveIntent())); // stamps the heartbeat
        Assert.Equal(1, server.JoinedPlayerCountForTest);

        server.Tick(30.0);
        Assert.Equal(1, server.JoinedPlayerCountForTest); // still within the grace period

        server.Tick(70.0); // now past SessionHeartbeatTimeout with no payloads at all
        Assert.Equal(0, server.JoinedPlayerCountForTest);
        Assert.Contains(1, transport.Disconnected);
    }

    [Fact]
    public void AnActivePlayer_IsNeverDroppedByTheHeartbeat()
    {
        var transport = new RecordingTransport();
        var server = NewServer("reconn_active", transport);

        Join(server, 1, "Pilot", "token-a");
        for (int i = 0; i < 10; i++)
        {
            server.HandlePayloadForTest(1, NetCodec.Encode(new MoveIntent()));
            server.Tick(30.0); // long ticks, but the client keeps talking
        }

        Assert.Equal(1, server.JoinedPlayerCountForTest);
        Assert.DoesNotContain(1, transport.Disconnected);
    }

    // ---------------- #965: the ghost-block amplifier ----------------

    [Fact]
    public void RepeatedGhostsInOneChunk_CostOneReStream()
    {
        // Each ghost used to drop the whole chunk from SentChunks, so the server re-sent it in full — with a
        // client that double-sent its mine intents that happened once per mined block. The corrective
        // BlockChanged still goes out every time; only the full re-stream is rate-limited.
        var transport = new RecordingTransport();
        var server = NewServer("ghost_ratelimit", transport);
        var session = server.AddLocalPlayer("Pilot");

        var air = server.FindAirCellForTest("Pilot");
        Assert.True(air.HasValue, "expected an air cell above the player");

        transport.Sent.Clear();
        for (int i = 0; i < 5; i++)
        {
            // Straight through the real receive path: the MineBlock helper loops "until the block breaks"
            // and would never even fire at a cell that is already air — which is exactly the case here.
            server.HandlePayloadForTest(session.ConnectionId,
                NetCodec.Encode(new MineBlockIntent { X = air.Value.X, Y = air.Value.Y, Z = air.Value.Z }));
        }

        int corrections = transport.Sent.Count(x => x.Conn == session.ConnectionId && x.Msg is BlockChanged);
        Assert.Equal(5, corrections);            // the client is corrected on every ghost…
        Assert.Equal(1, server.GhostReStreamsForTest("Pilot")); // …but the chunk is re-streamed once
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — a straggling handle on Windows must not fail the suite
        }
    }
}

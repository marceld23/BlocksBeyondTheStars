// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
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
/// #3194: a transport connection that never joins must not keep its slot. The server closes every open connection without
/// a joined session once it has waited <see cref="SvGameServer.PreJoinDeadlineSeconds"/> — silent ones, ones that sent
/// something else and ones whose join was rejected — and a joined session is never touched by it. The transport side is
/// pinned too: <c>DisconnectClient</c> really closes the connection and frees the slot on LiteNetLib and on the WebSocket
/// gateway, so the server's hang-up is not a no-op where it matters.
/// </summary>
public sealed class ConnectionGateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_conngate_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SvGameServer NewServer(string name, IServerTransport transport, CapturingLogger? log = null, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo, log);
        server.Start();
        return server;
    }

    private static JoinRequest Join(string name, string? password = null)
        => new() { ContentFingerprint = TestJoin.Fingerprint, PlayerName = name, Password = password, ViewDistanceChunks = 1 };

    /// <summary>One loopback "client" of a composite transport: its own link, what it received, whether it was hung up on.</summary>
    private sealed class Peer : IDisposable
    {
        public readonly LoopbackLink Link = new();
        public readonly LoopbackClientTransport Client;
        public readonly List<object> Received = new();
        public bool HungUp;

        public Peer()
        {
            Client = new LoopbackClientTransport(Link);
            Client.PayloadReceived += p =>
            {
                if (NetCodec.Decode(p) is { } m)
                {
                    Received.Add(m);
                }
            };
            Client.Disconnected += () => HungUp = true;
        }

        public void Send(object message) => Client.Send(NetCodec.Encode(message), DeliveryMode.ReliableOrdered);

        public void Dispose() => Client.Dispose();
    }

    private static void Advance(SvGameServer server, IEnumerable<Peer> peers, double seconds, double step = 0.5)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            server.Tick(step);
            foreach (var peer in peers)
            {
                peer.Client.Poll();
            }
        }
    }

    [Fact]
    public void ConnectionsThatNeverJoin_AreClosedAtTheDeadline_AndAJoinedOneStays()
    {
        using var silent = new Peer();
        using var rejected = new Peer();
        using var player = new Peer();
        var peers = new[] { silent, rejected, player };
        var transport = new CompositeServerTransport(
            new LoopbackServerTransport(silent.Link), new LoopbackServerTransport(rejected.Link), new LoopbackServerTransport(player.Link));
        var server = NewServer("gate", transport);

        foreach (var peer in peers)
        {
            peer.Client.Connect("loopback", 0);
        }

        rejected.Send(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Old", ProtocolVersion = Protocol.Version + 99 });
        player.Send(Join("Pilot"));

        Advance(server, peers, 25.0);
        Assert.Contains(rejected.Received, m => m is JoinRejected);
        Assert.Contains(player.Received, m => m is JoinAccepted);
        Assert.False(silent.HungUp, "25 s is inside the deadline — nobody is closed yet");
        Assert.False(rejected.HungUp);
        Assert.Equal(2, server.PreJoinConnectionCountForTest);

        Advance(server, peers, SvGameServer.PreJoinDeadlineSeconds - 25.0 + 1.5);

        Assert.True(silent.HungUp, "a connection that never sent anything must be closed at the deadline");
        Assert.True(rejected.HungUp, "a connection whose join was rejected must be closed at the deadline");
        Assert.False(player.HungUp, "a joined session is the heartbeat's business, never the pre-join deadline's");
        Assert.Equal(0, server.PreJoinConnectionCountForTest);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
    }

    /// <summary>A password retry on the SAME connection (no shipped client does it — it dials anew — but the join gate allows
    /// it) gets in if it comes within the deadline, and is then never closed by the gate.</summary>
    [Fact]
    public void ARetryOnTheSameConnection_WithinTheDeadline_Joins_AndStays()
    {
        using var peer = new Peer();
        var server = NewServer("gate_retry", new LoopbackServerTransport(peer.Link), configure: c => c.ServerPassword = "sesame");

        peer.Client.Connect("loopback", 0);
        peer.Send(Join("Pilot", password: "wrong"));
        Advance(server, new[] { peer }, 10.0);
        Assert.Contains(peer.Received, m => m is JoinRejected);

        peer.Send(Join("Pilot", password: "sesame"));
        Advance(server, new[] { peer }, SvGameServer.PreJoinDeadlineSeconds + 5.0);

        Assert.Contains(peer.Received, m => m is JoinAccepted);
        Assert.False(peer.HungUp);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
    }

    /// <summary>A transport may hand a closed connection's id to the next one (LiteNetLib reuses peer ids): the new
    /// connection starts its wait at zero instead of inheriting the old one's.</summary>
    [Fact]
    public void AReusedConnectionId_StartsItsWaitAfresh()
    {
        var transport = new DrivenTransport();
        var log = new CapturingLogger();
        var server = NewServer("gate_reuse", transport, log);
        string closedLine = $"WARN Connection 7 did not join within {SvGameServer.PreJoinDeadlineSeconds:0}s — closing it.";

        transport.Connect(7);
        for (int i = 0; i < 40; i++)
        {
            server.Tick(0.5); // 20 s on the first connection
        }

        transport.Disconnect(7);
        transport.Connect(7); // the id comes straight back for a new connection
        for (int i = 0; i < 40; i++)
        {
            server.Tick(0.5); // 20 s on the second — 40 s on the id, but the new connection is still inside its deadline
        }

        Assert.Equal(0, log.Count(closedLine));
        Assert.Equal(1, server.PreJoinConnectionCountForTest);

        for (int i = 0; i < 24; i++)
        {
            server.Tick(0.5);
        }

        Assert.Equal(1, log.Count(closedLine));
        Assert.Equal(0, server.PreJoinConnectionCountForTest);
    }

    /// <summary>The time BEFORE a connection was first seen is never charged to it: a very long tick (a world load for
    /// someone else) right before a connection shows up must not cut that connection off.</summary>
    [Fact]
    public void ALongTickBeforeTheConnectionArrived_IsNotChargedToIt()
    {
        var transport = new DrivenTransport();
        var log = new CapturingLogger();
        var server = NewServer("gate_longtick", transport, log);

        server.Tick(0.1);
        transport.Connect(3);
        server.Tick(45.0); // the tick that first sees the connection measured 45 s — none of it was this connection's
        server.Tick(0.1);

        Assert.Equal(1, server.PreJoinConnectionCountForTest);
        transport.Receive(3, Join("Late"));
        server.Tick(0.1);
        Assert.Equal(1, server.JoinedPlayerCountForTest);
        Assert.Equal(0, server.PreJoinConnectionCountForTest);
    }

    /// <summary>LiteNetLib: the server's hang-up really drops the peer and frees its transport slot for the next client.</summary>
    [Fact]
    public void LiteNetLib_DisconnectClient_FreesTheSlot()
    {
        int port = FreeUdpPort();
        using var server = new LiteNetLibServerTransport(maxConnections: 1);
        using var first = new LiteNetLibClientTransport();
        using var second = new LiteNetLibClientTransport();
        var connected = new List<int>();
        var disconnected = new List<int>();
        int rejected = 0;
        server.ClientConnected += connected.Add;
        server.ClientDisconnected += disconnected.Add;
        server.ConnectionRejected += () => rejected++;
        server.Start(port);

        first.Connect("127.0.0.1", port);
        Assert.True(PollUntil(() => connected.Count == 1, server, first), "the first client should connect");
        second.Connect("127.0.0.1", port);
        Assert.True(PollUntil(() => rejected > 0, server, first, second), "the only slot is taken");

        server.DisconnectClient(connected[0]);
        Assert.True(PollUntil(() => disconnected.Count == 1, server, first), "the hang-up must close the peer");

        using var third = new LiteNetLibClientTransport();
        third.Connect("127.0.0.1", port);
        Assert.True(PollUntil(() => connected.Count == 2, server, third), "the freed slot must take the next client");
    }

    /// <summary>The issue's own scenario over the real WebSocket gateway: a connection that only sends a wrong-version
    /// join is closed at the deadline and its slot (a gateway capped at one connection) takes the next browser.</summary>
    [Fact]
    [Trait("Category", "Slow")] // real sockets: the WebSocket neighbours are Slow for the same continuation-queueing reason
    public async Task WebSocket_AConnectionThatOnlySendsAWrongVersionJoin_IsClosed_AndItsSlotFreedAsync()
    {
        int port = FreeTcpPort();
        using var transport = new WebSocketServerTransport("127.0.0.1", maxConnections: 1, handshakeTimeout: TimeSpan.FromMinutes(10));
        var server = NewServer("gate_ws", transport, configure: c => c.GameplayPort = port);
        try
        {
            using var squatter = new ClientWebSocket();
            await squatter.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
            var wrongVersion = new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Old", ProtocolVersion = Protocol.Version + 99 };
            await squatter.SendAsync(NetCodec.EncodeJson(wrongVersion), WebSocketMessageType.Binary, true, CancellationToken.None);
            var received = new System.Collections.Concurrent.ConcurrentQueue<object>();
            var closed = ReceiveUntilClosedAsync(squatter, received);

            for (int i = 0; i < 100 && !received.Any(m => m is JoinRejected); i++)
            {
                server.Tick(0.1);
                await Task.Delay(25);
            }

            Assert.Contains(received, m => m is JoinRejected);
            for (int i = 0; i < (int)SvGameServer.PreJoinDeadlineSeconds + 2; i++)
            {
                server.Tick(1.0);
            }

            Assert.True(await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(10))) == closed, "the gateway must close the squatter");

            bool accepted = false;
            for (int i = 0; i < 40 && !accepted; i++)
            {
                server.Tick(0.1); // the transport's close reaches the server; the slot is free once the receive loop ended
                try
                {
                    using var attempt = new ClientWebSocket();
                    await attempt.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
                    accepted = true;
                    attempt.Abort();
                }
                catch (WebSocketException)
                {
                    await Task.Delay(50);
                }
            }

            Assert.True(accepted, "the freed slot must take the next browser");
        }
        finally
        {
            server.Stop();
        }
    }

    private static async Task ReceiveUntilClosedAsync(ClientWebSocket ws, System.Collections.Concurrent.ConcurrentQueue<object> received)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    await ms.WriteAsync(buffer.AsMemory(0, result.Count));
                }
                while (!result.EndOfMessage);

                if (NetCodec.Decode(ms.ToArray()) is { } message)
                {
                    received.Enqueue(message);
                }
            }
        }
        catch (WebSocketException)
        {
            // an abortive close counts — the connection is gone either way
        }
    }

    private static bool PollUntil(Func<bool> condition, LiteNetLibServerTransport server, params LiteNetLibClientTransport[] clients)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 5000)
        {
            server.Poll();
            foreach (var client in clients)
            {
                client.Poll();
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

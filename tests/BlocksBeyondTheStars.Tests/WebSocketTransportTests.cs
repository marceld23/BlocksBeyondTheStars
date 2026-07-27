// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
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

public sealed class WebSocketTransportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WebSocketTransportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ws_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WebSocketTransport_JoinsAndStreamsChunksAsync()
    {
        int port = FreeTcpPort();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "browser"));
        using var transport = new WebSocketServerTransport("127.0.0.1");
        var config = new ServerConfig
        {
            WorldName = "browser",
            GameplayPort = port,
            Seed = 11,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1,
            ChunkStreamPerTick = 8,
        };

        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        using var receiveCts = new CancellationTokenSource();
        var received = new ConcurrentQueue<byte[]>();
        var receiveTask = ReceiveLoopAsync(ws, received, receiveCts.Token);

        try
        {
            await ws.SendAsync(NetCodec.EncodeJson(new JoinRequest { PlayerName = "BrowserPilot", ViewDistanceChunks = 1 }),
                WebSocketMessageType.Binary, true, CancellationToken.None);

            bool joined = false;
            bool receivedChunk = false;
            for (int i = 0; i < 160 && (!joined || !receivedChunk); i++)
            {
                server.Tick(0.1);
                while (received.TryDequeue(out var payload))
                {
                    switch (NetCodec.Decode(payload))
                    {
                        case JoinAccepted:
                            joined = true;
                            break;
                        case ChunkDataMessage:
                            receivedChunk = true;
                            break;
                    }
                }

                await Task.Delay(25);
            }

            Assert.True(joined, "Browser WebSocket clients should complete the join handshake.");
            Assert.True(receivedChunk, "Browser WebSocket clients should receive authoritative world chunks.");
        }
        finally
        {
            await receiveCts.CancelAsync();
            ws.Abort();
            await Task.WhenAny(receiveTask, Task.Delay(500));
            server.Stop();
        }
    }

    [Fact]
    public void NetCodec_TryConvertToJsonPayload_DropsMalformedPayloads()
    {
        Assert.False(NetCodec.TryConvertToJsonPayload(new byte[] { 254, 1, 2, 3 }, out var converted));
        Assert.Empty(converted);
    }

    [Fact]
    public void NetCodec_Decode_DropsOversizedJsonPayloads()
    {
        var payload = new byte[NetCodec.MaxJsonPayloadBytes + 1];
        payload[0] = 255;

        Assert.Null(NetCodec.Decode(payload));
    }

    [Fact]
    [Trait("Category", "Slow")] // 183 ms in isolation (measured on Linux in a container), but 178.8 s and
                                // 208.9 s on two consecutive loaded PR runners on 2026-07-27 — both AFTER
                                // #536's clean-close fix, which made this rarer without removing it. What
                                // stretches is the runner's scheduling, not the code under test, so the
                                // fast-tier budget cannot hold it. Same symptom and same reasoning as
                                // Gateway_DropsAConnectionThatNeverSendsAsync below; full runs on main and
                                // release still cover it. Real cause tracked in #536 (reopened).
    public async Task Gateway_AcceptLoop_SurvivesAFaultingRequestAsync()
    {
        int port = FreeTcpPort();
        using var transport = new WebSocketServerTransport("127.0.0.1");
        // A throwing status provider faults request handling exactly where a client resetting the
        // connection mid-response write would — one bad /status poll must never end the accept loop (#417).
        transport.StatusJsonProvider = () => throw new InvalidOperationException("status snapshot failed (simulated)");
        transport.Start(port);

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // No keep-alive: on Linux (managed HttpListener) an idle connection is only reaped after the
        // listener's 2-minute idle timeout, and Dispose() below waits for that drain — the test body
        // takes milliseconds, but the CI runs measured it at ~126 s and it blew the fast-tier duration
        // budget on every PR. Windows (http.sys) never showed it. Closing each connection keeps the
        // shutdown immediate without changing what this test covers.
        http.DefaultRequestHeaders.ConnectionClose = true;

        // The faulted request must come back as a real 500, not a connection reset. That is the fix for
        // #536: the accept loop used to Abort() the response, which left the connection for the listener's
        // 2-minute idle sweep and dragged this test to 180-212 s. A clean close frees it immediately, so
        // asserting the status code here is also what pins the teardown behaviour.
        using (var faulted = await http.GetAsync($"http://127.0.0.1:{port}/status"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, faulted.StatusCode);
        }

        // The accept loop must still be alive: the next client gets a normal answer.
        using var ok = await http.GetAsync($"http://127.0.0.1:{port}/healthz");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Gateway_RejectsConnectionsBeyondTheCapAsync()
    {
        int port = FreeTcpPort();

        // The long handshakeTimeout is load-proofing, not a detail: ws1/ws2 never send, so with the default
        // 15 s window the transport reaps them as slow-loris idlers — and on a loaded 2-core CI runner the
        // suite once stretched the gap before ws3's connect past that window (failed 2026-07-27 after
        // 2 m 56 s: both slots free again, ws3 accepted, "no exception was thrown"). This test is about the
        // connection CAP; the handshake window has its own test right below.
        using var transport = new WebSocketServerTransport("127.0.0.1", maxConnections: 2,
            handshakeTimeout: TimeSpan.FromMinutes(10));
        transport.Start(port);

        using var ws1 = new ClientWebSocket();
        using var ws2 = new ClientWebSocket();
        await ws1.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await ws2.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        // #424 S9: connection 3 must be refused BEFORE the upgrade — an idler flood would otherwise hold
        // a socket + receive task + buffers each without ever counting against MaxPlayers.
        using var ws3 = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(
            () => ws3.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None));

        ws1.Abort();
        ws2.Abort();
    }

    [Fact]
    [Trait("Category", "Slow")] // real-socket close handshake under a loaded parallel suite: 3 s locally,
                                // but 190 s+ on a busy 2-core PR runner — full runs on main still cover it
    public async Task Gateway_DropsAConnectionThatNeverSendsAsync()
    {
        int port = FreeTcpPort();
        using var transport = new WebSocketServerTransport("127.0.0.1", handshakeTimeout: TimeSpan.FromMilliseconds(500));
        transport.Start(port);

        // A slow-loris connection: upgrade completes but no message ever follows. The server must close
        // it once the handshake window expires (#424 S9) instead of parking a receive task forever.
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var buffer = new byte[256];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool closedByServer;
        try
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            closedByServer = result.MessageType == WebSocketMessageType.Close;
        }
        catch (WebSocketException)
        {
            closedByServer = true; // an abortive teardown counts too — the connection is gone either way
        }

        Assert.True(closedByServer, "an idle pre-join connection must be dropped after the handshake window");
    }

    private static async Task ReceiveLoopAsync(ClientWebSocket ws, ConcurrentQueue<byte[]> received, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    await ms.WriteAsync(buffer.AsMemory(0, result.Count), token);
                }
                while (!result.EndOfMessage);

                received.Enqueue(ms.ToArray());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

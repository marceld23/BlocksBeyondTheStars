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

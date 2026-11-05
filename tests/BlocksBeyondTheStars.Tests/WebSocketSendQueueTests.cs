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
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3195: the WebSocket gateway's send queue is bounded per connection. A client that stops reading used to make every
/// later message wait in server memory (+159 MB for 20 000 × 8 KB, connection still open). Now it is dropped as a slow
/// consumer — at the byte cap, or when one socket write stays blocked past the stall timeout — and what waited for it is
/// released; a client that reads gets everything and is never dropped. Real sockets on 127.0.0.1, so these are Slow like
/// the gateway's other socket tests (continuation queueing under a loaded parallel suite, not the gateway, sets their
/// wall time); each wait is bounded.
/// </summary>
public sealed class WebSocketSendQueueTests
{
    private const int MessageChars = 8000;

    private static byte[] BigMessage() => NetCodec.Encode(new ChatMessage { Sender = "server", Text = new string('x', MessageChars) });

    /// <summary>Starts a gateway, connects one browser-like client, sends one message (past the handshake window) and
    /// returns the connection id the server sees.</summary>
    private static async Task<(WebSocketServerTransport Transport, ClientWebSocket Client, int Id, List<int> Disconnected)> ConnectAsync(
        Action<WebSocketServerTransport> configure)
    {
        int port = FreeTcpPort();
        var transport = new WebSocketServerTransport("127.0.0.1", handshakeTimeout: TimeSpan.FromMinutes(10));
        configure(transport);
        var connected = new List<int>();
        var disconnected = new List<int>();
        transport.ClientConnected += connected.Add;
        transport.ClientDisconnected += disconnected.Add;
        transport.Start(port);

        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await client.SendAsync(NetCodec.EncodeJson(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Reader" }), WebSocketMessageType.Binary, true, CancellationToken.None);
        Assert.True(PollUntil(transport, () => connected.Count == 1), "the gateway should report the connection");
        return (transport, client, connected[0], disconnected);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task AClientThatStopsReading_IsDroppedAtTheByteCap_AndItsQueueReleasedAsync()
    {
        var (transport, client, id, disconnected) = await ConnectAsync(t => t.MaxPendingSendBytes = 1024 * 1024);
        using (transport)
        using (client)
        {
            // The client never reads again: the socket buffers fill, writes block, and the outbox grows until the cap.
            byte[] payload = BigMessage();
            long sentBytes = 0;
            while (transport.SlowConsumerDrops == 0 && sentBytes < 256L * 1024 * 1024)
            {
                transport.Send(id, payload, DeliveryMode.ReliableOrdered);
                sentBytes += payload.Length;
            }

            Assert.Equal(1, transport.SlowConsumerDrops);
            Assert.True(PollUntil(transport, () => disconnected.Contains(id)), "the slow consumer must be disconnected");
            Assert.Equal(0, transport.PendingSendBytesForTest); // nothing waits for it any more

            transport.Send(id, payload, DeliveryMode.ReliableOrdered); // a send after the drop goes nowhere
            Assert.Equal(0, transport.PendingSendBytesForTest);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task AClientWhoseSocketWriteStalls_IsDroppedAfterTheStallTimeoutAsync()
    {
        // A cap far above anything sent here, so only the stall rule can fire.
        var (transport, client, id, disconnected) = await ConnectAsync(t =>
        {
            t.MaxPendingSendBytes = long.MaxValue;
            t.SendStallTimeout = TimeSpan.FromSeconds(1);
        });
        using (transport)
        using (client)
        {
            byte[] payload = BigMessage();
            for (int i = 0; i < 6000; i++) // ~48 MB: more than the loopback socket buffers take, so a write blocks
            {
                transport.Send(id, payload, DeliveryMode.ReliableOrdered);
            }

            // The drop is decided inside Poll (the watchdog) — then the aborted receive loop raises the disconnect.
            Assert.True(PollUntil(transport, () => transport.SlowConsumerDrops == 1),
                $"a client that takes no frame for the stall timeout must be dropped (pending {transport.PendingSendBytesForTest:N0} bytes)");
            Assert.True(PollUntil(transport, () => disconnected.Contains(id)), "the dropped client must be disconnected");
            Assert.Equal(0, transport.PendingSendBytesForTest);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task AReadingClient_GetsEverything_AndIsNeverDroppedAsync()
    {
        var (transport, client, id, disconnected) = await ConnectAsync(_ => { });
        using (transport)
        using (client)
        {
            const int count = 1000; // ~8 MB, as much as a whole view-16 join
            var reader = CountFramesAsync(client, count);
            byte[] payload = BigMessage();
            for (int i = 0; i < count; i++)
            {
                transport.Send(id, payload, DeliveryMode.ReliableOrdered);
            }

            Assert.True(await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(30))) == reader, "every frame should arrive");
            Assert.Equal(count, await reader);
            transport.Poll();
            Assert.Equal(0, transport.SlowConsumerDrops);
            Assert.Empty(disconnected);
        }
    }

    private static async Task<int> CountFramesAsync(ClientWebSocket ws, int expected)
    {
        var buffer = new byte[16 * 1024];
        int frames = 0;
        while (frames < expected)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (result.EndOfMessage)
            {
                frames++;
            }
        }

        return frames;
    }

    /// <summary>Polls the gateway until <paramref name="condition"/> holds (30 s budget). Synchronous on purpose: an
    /// awaited delay re-queues behind the suite's CPU-bound test bodies on xUnit's few workers, so under a loaded run the
    /// gateway was polled only a handful of times within the budget — the transport's own work runs on the thread pool
    /// and needs nothing from this thread but the polls.</summary>
    private static bool PollUntil(WebSocketServerTransport transport, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            transport.Poll();
            Thread.Sleep(20);
        }

        transport.Poll();
        return condition();
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

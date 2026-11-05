// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace BlocksBeyondTheStars.Networking.Transport;

/// <summary>
/// #3195: the bounded per-connection send queue. Every send used to start its own fire-and-forget <c>SendAsync</c> behind
/// a per-client <c>SemaphoreSlim</c>, with no bound on how many waited and no deadline on the socket write. A client that
/// stopped reading (the TCP window fills, the write blocks) made every later message wait in server memory — measured
/// +159 MB for 20 000 messages of 8 KB, the connection still open — until a container hit its memory limit and every
/// player on the world lost their progress since the last autosave.
/// <para>
/// Now each connection has an outbox drained in order by one pump, and the bytes waiting in it are counted. A connection
/// is dropped as a slow consumer when the waiting bytes pass <see cref="MaxPendingSendBytes"/>, or when a single socket
/// write has been blocked for <see cref="SendStallTimeout"/> (the client takes nothing at all). The drop aborts the socket
/// — the receive loop then raises the disconnect like any other close — and releases the queued payloads at once.
/// </para>
/// </summary>
public sealed partial class WebSocketServerTransport
{
    /// <summary>
    /// #3195: the most a browser connection may have waiting to be sent (payload bytes + <see cref="QueuedSendOverheadBytes"/>
    /// per message) before it is dropped. A legitimate client loading its view never comes close: measured on the shipped
    /// content, a WHOLE fresh join — every message, every chunk of the view — is about 1.2 MB of JSON at the browser
    /// default view of 4 chunks, 3 MB at 8 and 9.3 MB at the maximum of 16, and no single message is above ~11 KB. So
    /// even a client that took nothing at all while its largest possible view loaded stays at about a third of the cap,
    /// and one that reads keeps its queue far shorter. Only a client that has stopped reading while the server keeps
    /// producing (a player who keeps moving is streamed new chunks every tick) reaches it, holding at most this much.
    /// </summary>
    public const long DefaultMaxPendingSendBytes = 32L * 1024 * 1024;

    /// <summary>
    /// #3195: how long one socket write may stay blocked before the client counts as gone. A client that reads at all
    /// finishes every write in well under a second (messages are a few KB; the kernel's socket buffers take megabytes
    /// before a write blocks at all), and the server's own heartbeat gives a client that SENDS nothing 90 s; one that has
    /// taken no frame for a whole minute is not loading anything. Catches the client that stops reading while little is
    /// sent to it (its queue grows too slowly to reach <see cref="DefaultMaxPendingSendBytes"/> soon).
    /// </summary>
    public static readonly TimeSpan DefaultSendStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>#3195: bookkeeping charged per queued message on top of its payload (queue slot, array header) — so a
    /// flood of tiny messages is bounded too, not only big ones.</summary>
    private const int QueuedSendOverheadBytes = 64;

    /// <summary>#3195: the slow-consumer byte cap in force (<see cref="DefaultMaxPendingSendBytes"/>; tests lower it).</summary>
    internal long MaxPendingSendBytes { get; set; } = DefaultMaxPendingSendBytes;

    /// <summary>#3195: the blocked-write deadline in force (<see cref="DefaultSendStallTimeout"/>; tests lower it).</summary>
    internal TimeSpan SendStallTimeout { get; set; } = DefaultSendStallTimeout;

    private int _slowConsumerDrops;

    /// <summary>#3195: how many connections were dropped as slow consumers (diagnostics / tests).</summary>
    public int SlowConsumerDrops => Volatile.Read(ref _slowConsumerDrops);

    /// <summary>#3195 test seam: the bytes waiting in the send queues of every open connection.</summary>
    internal long PendingSendBytesForTest
    {
        get
        {
            long total = 0;
            foreach (var entry in _clients)
            {
                total += Interlocked.Read(ref entry.Value.PendingBytes);
            }

            return total;
        }
    }

    private static long QueuedCost(byte[] payload) => payload.Length + QueuedSendOverheadBytes;

    /// <summary>Queues one payload for <paramref name="client"/> and makes sure a pump drains the outbox. Runs on the tick
    /// thread (Send / Broadcast), as does every drop decision.</summary>
    private void Enqueue(Client client, byte[] payload)
    {
        if (Volatile.Read(ref client.Closed) != 0)
        {
            return; // being torn down — nothing more is delivered to it
        }

        client.Outbox.Enqueue(payload);
        long pending = Interlocked.Add(ref client.PendingBytes, QueuedCost(payload));
        if (pending > MaxPendingSendBytes)
        {
            DropSlowConsumer(client, $"{pending / 1024} KB waiting to be sent");
            return;
        }

        if (Interlocked.CompareExchange(ref client.Pumping, 1, 0) == 0)
        {
            _ = PumpAsync(client);
        }
    }

    /// <summary>Drains the outbox in order, one socket write at a time (a WebSocket allows one send in flight). Stops when
    /// the outbox is empty; a payload queued in the gap between the last dequeue and the flag reset is picked up by the
    /// re-check. A failed write ends the pump for good (Pumping stays 1), releases what waits and closes the socket.</summary>
    private static async Task PumpAsync(Client client)
    {
        try
        {
            do
            {
                while (client.Outbox.TryDequeue(out var payload))
                {
                    try
                    {
                        if (Volatile.Read(ref client.Closed) == 0 && client.Socket.State == WebSocketState.Open)
                        {
                            Volatile.Write(ref client.SendStartedAt, Stopwatch.GetTimestamp());
                            await client.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        Volatile.Write(ref client.SendStartedAt, 0L);
                        Interlocked.Add(ref client.PendingBytes, -QueuedCost(payload));
                    }
                }

                Volatile.Write(ref client.Pumping, 0);
            }
            while (!client.Outbox.IsEmpty && Interlocked.CompareExchange(ref client.Pumping, 1, 0) == 0);
        }
        catch
        {
            // The socket failed or was aborted: nothing more can be delivered. Abort it (a no-op when it is already gone)
            // so the receive loop ends and the server sees the connection leave, instead of a session that gets nothing.
            Volatile.Write(ref client.Closed, 1);
            DrainOutbox(client);
            try { client.Socket.Abort(); } catch { }
        }
    }

    /// <summary>Releases every queued payload of <paramref name="client"/> (and its share of the pending count).</summary>
    private static void DrainOutbox(Client client)
    {
        while (client.Outbox.TryDequeue(out var payload))
        {
            Interlocked.Add(ref client.PendingBytes, -QueuedCost(payload));
        }
    }

    /// <summary>Drops a client that does not take what the server sends: no more sends, the queue released, the socket
    /// aborted — the receive loop's finally then raises the disconnect, so the game server saves and ends the session.</summary>
    private void DropSlowConsumer(Client client, string why)
    {
        if (Interlocked.Exchange(ref client.Closed, 1) != 0)
        {
            return;
        }

        Interlocked.Increment(ref _slowConsumerDrops);
        LogWarning($"Dropped browser WebSocket connection {client.Id}: it is not reading what the server sends ({why}).");
        DrainOutbox(client);
        try { client.Socket.Abort(); } catch { }
    }

    /// <summary>Per poll: drops every client whose current socket write has been blocked for longer than
    /// <see cref="SendStallTimeout"/>.</summary>
    private void DropStalledSenders()
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long limit = (long)(SendStallTimeout.TotalSeconds * Stopwatch.Frequency);
        foreach (var entry in _clients)
        {
            long started = Volatile.Read(ref entry.Value.SendStartedAt);
            if (started != 0 && now - started > limit)
            {
                DropSlowConsumer(entry.Value, $"one send blocked for more than {SendStallTimeout.TotalSeconds:0} s");
            }
        }
    }
}

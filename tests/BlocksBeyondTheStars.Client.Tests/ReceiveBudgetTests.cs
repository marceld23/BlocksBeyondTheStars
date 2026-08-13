// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Configuration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client's per-frame receive budget (#963). A backgrounded client keeps its transport thread queueing
/// while its player loop is stopped; without a budget the next Poll dispatched everything at once — a
/// multi-second freeze followed by minutes of stutter as the mesh backlog drained.
/// </summary>
public sealed class ReceiveBudgetTests
{
    /// <summary>Transport stub that hands the client a pre-loaded batch of payloads on Poll, the way a real
    /// transport drains its whole event queue in one call.</summary>
    private sealed class BatchTransport : IClientTransport
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<byte[]>? PayloadReceived;

        public readonly Queue<byte[]> Pending = new();

        public void Connect(string host, int port) { _ = Connected; _ = Disconnected; }
        public void Send(byte[] payload, DeliveryMode mode) { }
        public void Disconnect() { }
        public void Dispose() { }

        public void Poll()
        {
            while (Pending.Count > 0)
            {
                PayloadReceived?.Invoke(Pending.Dequeue());
            }
        }
    }

    private static byte[] Chunk(int cx) => NetCodec.Encode(new ChunkDataMessage { Cx = cx, Cy = 4, Cz = 0 });

    private static byte[] Message(string text) => NetCodec.Encode(new ServerMessage { Text = text });

    [Fact]
    public void ChunkBacklog_IsPacedAcrossFrames_AndNothingIsLost()
    {
        var transport = new BatchTransport();
        using var client = new NetworkClient(transport);

        int received = 0;
        client.ChunkReceived += _ => received++;

        int queued = (client.MaxChunksPerPoll * 3) + 5;
        for (int i = 0; i < queued; i++)
        {
            transport.Pending.Enqueue(Chunk(i));
        }

        client.Poll();
        Assert.Equal(client.MaxChunksPerPoll, received);           // one frame's worth, not all 40
        Assert.Equal(queued - client.MaxChunksPerPoll, client.PendingPayloads);

        // Draining takes several frames, and every chunk arrives exactly once, in order.
        for (int frame = 0; frame < 20 && client.PendingPayloads > 0; frame++)
        {
            client.Poll();
        }

        Assert.Equal(queued, received);
        Assert.Equal(0, client.PendingPayloads);
    }

    [Fact]
    public void CheapMessages_AreNotBlockedByTheChunkCap()
    {
        // The chunk cap must not starve everything else: with only light messages queued they all go through
        // in one frame (up to the overall dispatch budget).
        var transport = new BatchTransport();
        using var client = new NetworkClient(transport);

        int messages = 0;
        client.ServerMessageReceived += _ => messages++;
        for (int i = 0; i < 20; i++)
        {
            transport.Pending.Enqueue(Message("hello " + i));
        }

        client.Poll();
        Assert.Equal(20, messages);
        Assert.Equal(0, client.PendingPayloads);
    }

    [Fact]
    public void StreamOrderIsPreserved_WhenTheChunkCapStopsTheFrame()
    {
        // A block edit must never overtake the chunk it patches, so hitting the chunk cap ends the frame
        // instead of skipping ahead to cheaper payloads.
        var transport = new BatchTransport();
        using var client = new NetworkClient(transport) { MaxChunksPerPoll = 2 };

        var order = new List<string>();
        client.ChunkReceived += m => order.Add("chunk" + m.Cx);
        client.BlockChanged += _ => order.Add("block");

        transport.Pending.Enqueue(Chunk(0));
        transport.Pending.Enqueue(Chunk(1));
        transport.Pending.Enqueue(Chunk(2));                                  // over the cap → next frame
        transport.Pending.Enqueue(NetCodec.Encode(new BlockChanged { X = 1 })); // must stay behind it

        client.Poll();
        Assert.Equal(new[] { "chunk0", "chunk1" }, order);

        client.Poll();
        Assert.Equal(new[] { "chunk0", "chunk1", "chunk2", "block" }, order);
    }

    [Fact]
    public void TheChunkCap_ExceedsWhatTheServerSendsPerTick()
    {
        // Guard rail for the budget's core invariant: a cap at or below the server's per-tick chunk budget
        // would not pace a backlog, it would manufacture one — the client could never catch up while
        // terrain kept streaming, and the gap would grow without bound.
        using var client = new NetworkClient(new BatchTransport());
        Assert.True(client.MaxChunksPerPoll > new ServerConfig().ChunkStreamPerTick,
            "the per-frame chunk cap must stay above ServerConfig.ChunkStreamPerTick");
        Assert.True(client.MaxDispatchPerPoll > client.MaxChunksPerPoll);
    }

    [Fact]
    public void TheChunkCap_HoldsForEveryConfiguredStreamRate()
    {
        // #999: ChunkStreamPerTick is operator-configurable (config file + BBS_CHUNK_STREAM_PER_TICK), so
        // the invariant above only guarded the DEFAULT. The setter clamps to a shared ceiling now — an
        // operator pushing the rate above the client's per-frame cap would put slow clients into exactly
        // the unbounded backlog #963 removed.
        var cfg = new ServerConfig { ChunkStreamPerTick = 999 };
        Assert.Equal(ServerConfig.ChunkStreamPerTickCeiling, cfg.ChunkStreamPerTick);

        cfg.ChunkStreamPerTick = 0; // nonsense low values clamp up to a working minimum
        Assert.Equal(1, cfg.ChunkStreamPerTick);

        using var client = new NetworkClient(new BatchTransport());
        Assert.True(client.MaxChunksPerPoll > ServerConfig.ChunkStreamPerTickCeiling,
            "the per-frame chunk cap must stay above the config ceiling, not just the default");
    }

    [Fact]
    public void IsMessageType_RecognisesChunks_WithoutDecodingTheBody()
    {
        Assert.True(NetCodec.IsMessageType<ChunkDataMessage>(Chunk(3)));
        Assert.False(NetCodec.IsMessageType<ChunkDataMessage>(Message("no")));
        Assert.False(NetCodec.IsMessageType<ChunkDataMessage>(Array.Empty<byte>()));
    }

    [Fact]
    public void IsMessageType_WorksForTheBrowserJsonEnvelope()
    {
        // Browser clients receive tagged JSON envelopes; the budget must classify those too.
        byte[] json = NetCodec.EncodeJson(new ChunkDataMessage { Cx = 7 });
        Assert.True(NetCodec.IsMessageType<ChunkDataMessage>(json));
        Assert.False(NetCodec.IsMessageType<ChunkDataMessage>(NetCodec.EncodeJson(new ServerMessage { Text = "x" })));
    }
}

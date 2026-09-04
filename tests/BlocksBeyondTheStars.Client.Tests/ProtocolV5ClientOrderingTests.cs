// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1534 (protocol v5): chunks and block changes arrive on their own transport channel, so the client
/// orders them against JoinAccepted / WorldReset by WorldId — held before the join, dropped for the world just
/// left, parked for a world not announced yet.</summary>
public sealed class ProtocolV5ClientOrderingTests
{
    /// <summary>An in-memory client transport: the test hands it server payloads in any order it likes.</summary>
    private sealed class ScriptedClientTransport : IClientTransport
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<byte[]>? PayloadReceived;

        public void Connect(string host, int port) => Connected?.Invoke();

        public void Send(byte[] payload, DeliveryMode mode)
        {
        }

        public void Poll()
        {
        }

        public void Disconnect() => Disconnected?.Invoke();

        public void Dispose()
        {
        }

        public void Deliver(object message) => PayloadReceived?.Invoke(NetCodec.Encode(message));
    }

    private static ChunkDataMessage Chunk(int worldId, int cx)
        => new() { Cx = cx, Cy = 0, Cz = 0, WorldId = worldId, Blocks = new ushort[WorldConstants.BlocksPerChunk] };

    [Fact]
    public void WorldStream_ArrivingBeforeJoinAccepted_IsReplayedAfterIt()
    {
        var transport = new ScriptedClientTransport();
        var client = new NetworkClient(transport);
        var order = new List<string>();
        client.JoinAccepted += _ => order.Add("join");
        client.ChunkReceived += c => order.Add("chunk" + c.Cx);
        client.BlockChanged += b => order.Add("block" + b.X);

        transport.Deliver(Chunk(worldId: 1, cx: 7));
        transport.Deliver(new BlockChanged { X = 3, WorldId = 1 });
        client.Poll();
        Assert.Empty(order); // held: nothing dispatched before the join

        transport.Deliver(new JoinAccepted { WorldId = 1 });
        client.Poll();

        Assert.Equal(new[] { "join", "chunk7", "block3" }, order);
        Assert.Equal(1, client.CurrentWorldId);
    }

    [Fact]
    public void WorldStream_OfTheWorldJustLeft_IsDropped()
    {
        var transport = new ScriptedClientTransport();
        var client = new NetworkClient(transport);
        var chunks = new List<int>();
        client.ChunkReceived += c => chunks.Add(c.Cx);

        transport.Deliver(new JoinAccepted { WorldId = 1 });
        transport.Deliver(Chunk(1, 1));
        transport.Deliver(new WorldReset { WorldId = 2 });
        transport.Deliver(Chunk(1, 99)); // a straggler from world 1, still draining on its channel
        transport.Deliver(Chunk(2, 2));
        client.Poll();

        Assert.Equal(new[] { 1, 2 }, chunks);
        Assert.Equal(2, client.CurrentWorldId);
        Assert.Equal(0, client.PendingFutureWorldMessages);
    }

    [Fact]
    public void WorldStream_OfAnUnannouncedWorld_WaitsForItsWorldReset()
    {
        var transport = new ScriptedClientTransport();
        var client = new NetworkClient(transport);
        var order = new List<string>();
        client.ChunkReceived += c => order.Add("chunk" + c.Cx);
        client.BlockChanged += b => order.Add("block" + b.X);
        client.WorldResetReceived += _ => order.Add("reset");

        transport.Deliver(new JoinAccepted { WorldId = 1 });
        transport.Deliver(Chunk(2, 5));                       // the new world's stream overtook its WorldReset
        transport.Deliver(new BlockChanged { X = 9, WorldId = 2 });
        client.Poll();
        Assert.Empty(order);
        Assert.Equal(2, client.PendingFutureWorldMessages);

        transport.Deliver(new WorldReset { WorldId = 2 });
        client.Poll();

        Assert.Equal(new[] { "reset", "chunk5", "block9" }, order);
        Assert.Equal(0, client.PendingFutureWorldMessages);
    }

    [Fact]
    public void WorldStream_WithoutAWorldId_IsAcceptedAsBefore()
    {
        var transport = new ScriptedClientTransport();
        var client = new NetworkClient(transport);
        var chunks = new List<int>();
        client.ChunkReceived += c => chunks.Add(c.Cx);

        transport.Deliver(new JoinAccepted { WorldId = 3 });
        transport.Deliver(Chunk(worldId: 0, cx: 4)); // e.g. a path that does not number worlds
        client.Poll();

        Assert.Equal(new[] { 4 }, chunks);
    }

    [Fact]
    public void Disconnect_ResetsTheOrderingState()
    {
        var transport = new ScriptedClientTransport();
        var client = new NetworkClient(transport);
        var chunks = new List<int>();
        client.ChunkReceived += c => chunks.Add(c.Cx);

        transport.Connect("x", 1);
        transport.Deliver(new JoinAccepted { WorldId = 1 });
        client.Poll();
        transport.Disconnect();
        Assert.Equal(0, client.CurrentWorldId);

        transport.Deliver(Chunk(1, 8)); // before the next join: held again, not dispatched
        client.Poll();
        Assert.Empty(chunks);
    }
}

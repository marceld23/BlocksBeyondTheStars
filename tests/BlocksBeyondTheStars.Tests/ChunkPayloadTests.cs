// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1532 / #1531: the chunk send path and the codec keep their bytes while doing less work — the RLE runs from
/// the chunk's backing array, the MessagePack encode writes tag + body into one buffer, a chunk's payload is
/// encoded once per version and format, and the WebSocket transport converts a shared payload once.
/// </summary>
public sealed class ChunkPayloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_chunkpayload_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly MessagePackSerializerOptions SameAsCodec =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

    [Fact]
    public void Rle_SpanOverload_MatchesTheArrayOverload_Exactly()
    {
        var rng = new Random(1532);
        for (int trial = 0; trial < 50; trial++)
        {
            var dense = new ushort[WorldConstants.BlocksPerChunk];
            int mode = trial % 4;
            for (int i = 0; i < dense.Length; i++)
            {
                dense[i] = mode switch
                {
                    0 => (ushort)(i < 2000 ? 3 : i < 2100 ? 7 : 0),          // terrain-like
                    1 => (ushort)(i & 1),                                     // checkerboard (worst case)
                    2 => (ushort)rng.Next(0, 3),                              // noisy
                    _ => (ushort)(rng.Next(0, 40) == 0 ? rng.Next(1, 200) : 5), // mostly one value
                };
            }

            var viaArray = ChunkBlocksRle.Encode(dense);
            var viaSpan = ChunkBlocksRle.Encode((ReadOnlySpan<ushort>)dense);
            Assert.Equal(viaArray, viaSpan);
            Assert.Equal(dense, ChunkBlocksRle.Decode(viaSpan, dense.Length));
        }
    }

    [Fact]
    public void Encode_WritesExactlyTagPlusMessagePackBody()
    {
        NetCodec.UseJsonEncoding = false;
        var messages = new object[]
        {
            new ChunkDataMessage { Cx = 3, Cy = -2, Cz = 9, BlocksRle = new ushort[] { 0, 100, 4, 3996 } },
            new PlayerPresence { PlayerId = "p1", Name = "Alice", X = 1.5f, Y = 64f, Z = -3f, Yaw = 0.25f, Gear = 5, Held = "torch" },
            new PlayerStateUpdate { PlayerId = "p1", X = 1, Y = 2, Z = 3, Health = 90f },
        };
        foreach (var msg in messages)
        {
            var body = MessagePackSerializer.Serialize(msg.GetType(), msg, SameAsCodec);
            var encoded = NetCodec.Encode(msg);
            Assert.Equal(body.Length + 1, encoded.Length);
            Assert.Equal(body, encoded.AsSpan(1).ToArray());
            Assert.IsType(msg.GetType(), NetCodec.Decode(encoded));
        }

        // back to back: the reusable buffer must not leak the previous message's tail
        var small = NetCodec.Encode(new PlayerStateUpdate { PlayerId = "p1" });
        var big = NetCodec.Encode(new ChunkDataMessage { Cx = 1, Blocks = new ushort[WorldConstants.BlocksPerChunk] });
        var smallAgain = NetCodec.Encode(new PlayerStateUpdate { PlayerId = "p1" });
        Assert.Equal(small, smallAgain);
        Assert.True(big.Length > small.Length);
    }

    [Fact]
    public void ChunkData_Version_ChangesOnEveryMutation_AndNeverRepeats()
    {
        var a = new ChunkData(new ChunkCoord(0, 0, 0));
        var b = new ChunkData(new ChunkCoord(0, 0, 0));
        Assert.NotEqual(a.Version, b.Version);

        long v0 = a.Version;
        a.Set(1, 2, 3, new BlockId(5));
        long v1 = a.Version;
        a.SetModifier(1, 2, 3, 0x112233, 0);
        long v2 = a.Version;
        a.SetShape(1, 2, 3, 7);
        long v3 = a.Version;
        Assert.True(v0 < v1 && v1 < v2 && v2 < v3);
    }

    private sealed class CountingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly Dictionary<int, int> ChunkSends = new();
        public readonly HashSet<byte[]> DistinctChunkPayloads = new(ReferenceEqualityComparer.Instance);

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.IsMessageType<ChunkDataMessage>(payload))
            {
                ChunkSends[connectionId] = ChunkSends.GetValueOrDefault(connectionId) + 1;
                DistinctChunkPayloads.Add(payload);
            }
        }
        public void Broadcast(byte[] payload, DeliveryMode mode) { }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    [Fact]
    public void CoLocatedPlayers_ShareOneEncodedPayloadPerChunk()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shared"));
        var transport = new CountingTransport();
        var config = new ServerConfig
        {
            WorldName = "shared",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 2,
            ChunkStreamPerTick = 64,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var a = server.AddLocalPlayer("Alice");
        var b = server.AddLocalPlayer("Bob");
        b.State.Position = a.State.Position;
        for (int i = 0; i < 12; i++)
        {
            server.TickForTest(0.1);
        }

        int sendsA = transport.ChunkSends.GetValueOrDefault(a.ConnectionId);
        int sendsB = transport.ChunkSends.GetValueOrDefault(b.ConnectionId);
        Assert.True(sendsA > 20 && sendsB > 20, $"both players should have streamed a view ({sendsA} / {sendsB})");
        Assert.True(server.ChunkPayloadCacheHits > 0, "the second player's chunks should come from the payload cache");
        Assert.True(server.ChunkPayloadEncodes < sendsA + sendsB, $"encodes {server.ChunkPayloadEncodes} vs sends {sendsA + sendsB}");
        Assert.Equal(server.ChunkPayloadEncodes, transport.DistinctChunkPayloads.Count);
    }

    [Fact]
    public void WebSocketTransport_ConvertsASharedPayloadOnce()
    {
        NetCodec.UseJsonEncoding = false;
        using var transport = new WebSocketServerTransport();
        var payload = NetCodec.Encode(new PlayerPresence { PlayerId = "p1", Name = "Alice" });
        transport.Broadcast(payload, DeliveryMode.ReliableOrdered);
        transport.Broadcast(payload, DeliveryMode.ReliableOrdered);
        transport.Broadcast(payload, DeliveryMode.ReliableOrdered);
        Assert.Equal(1, transport.JsonConversions);

        transport.Broadcast(NetCodec.Encode(new PlayerPresence { PlayerId = "p2", Name = "Bob" }), DeliveryMode.ReliableOrdered);
        Assert.Equal(2, transport.JsonConversions);
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

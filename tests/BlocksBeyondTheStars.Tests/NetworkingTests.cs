// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public class NetworkingTests
{
    [Fact]
    public void Codec_RoundTrips_IntentMessage()
    {
        var msg = new PlaceBlockIntent { X = 3, Y = -7, Z = 12, ItemKey = "iron_wall" };
        var decoded = NetCodec.Decode(NetCodec.Encode(msg));

        var typed = Assert.IsType<PlaceBlockIntent>(decoded);
        Assert.Equal(3, typed.X);
        Assert.Equal(-7, typed.Y);
        Assert.Equal(12, typed.Z);
        Assert.Equal("iron_wall", typed.ItemKey);
    }

    [Fact]
    public void Codec_RoundTrips_ChunkData()
    {
        var blocks = new ushort[] { 0, 1, 2, 3, 65535 };
        var msg = new ChunkDataMessage { Cx = 1, Cy = 2, Cz = 3, Blocks = blocks };
        var typed = Assert.IsType<ChunkDataMessage>(NetCodec.Decode(NetCodec.Encode(msg)));

        Assert.Equal(blocks, typed.Blocks);
        Assert.Equal(2, typed.Cy);
    }

    [Fact]
    public void ChunkRle_RoundTrips_TerrainLikeAndAwkwardPayloads()
    {
        // Terrain-like: big air span, a stone body, a thin surface strip — the shape RLE is built for.
        var terrain = new ushort[4096];
        for (int i = 0; i < 1800; i++) terrain[i] = 1;      // stone
        for (int i = 1800; i < 1860; i++) terrain[i] = 4;   // surface layer
        // cells 1860..4095 stay 0 (air)
        var rle = ChunkBlocksRle.Encode(terrain);
        Assert.True(rle.Length < 32, $"terrain chunk should collapse to a handful of runs, got {rle.Length}");
        Assert.Equal(terrain, ChunkBlocksRle.Decode(rle, terrain.Length));

        // Awkward: single cell, all-same, and a trailing lone value must all survive the round trip.
        var single = new ushort[] { 7 };
        Assert.Equal(single, ChunkBlocksRle.Decode(ChunkBlocksRle.Encode(single), 1));
        var allSame = new ushort[4096];
        for (int i = 0; i < allSame.Length; i++) allSame[i] = 9;
        Assert.Equal(allSame, ChunkBlocksRle.Decode(ChunkBlocksRle.Encode(allSame), allSame.Length));
        var tail = new ushort[] { 5, 5, 5, 8 };
        Assert.Equal(tail, ChunkBlocksRle.Decode(ChunkBlocksRle.Encode(tail), 4));
    }

    [Fact]
    public void ChunkRle_WorstCase_IsLargerThanDense_SoTheServerShipsDense()
    {
        // A checkerboard has no runs: RLE doubles the size, so the server's "smaller wins" pick keeps dense.
        var checker = new ushort[4096];
        for (int i = 0; i < checker.Length; i++) checker[i] = (ushort)(i & 1);
        var rle = ChunkBlocksRle.Encode(checker);
        Assert.True(rle.Length > checker.Length);
        Assert.Equal(checker, ChunkBlocksRle.Decode(rle, checker.Length)); // still decodes correctly
    }

    [Fact]
    public void ChunkRle_Decode_RejectsMalformedStreams()
    {
        Assert.Null(ChunkBlocksRle.Decode(Array.Empty<ushort>(), 4096));          // empty
        Assert.Null(ChunkBlocksRle.Decode(new ushort[] { 1, 2, 3 }, 4096));       // odd pair count
        Assert.Null(ChunkBlocksRle.Decode(new ushort[] { 1, 0 }, 4096));          // zero-length run
        Assert.Null(ChunkBlocksRle.Decode(new ushort[] { 1, 5000 }, 4096));       // overflows the chunk
        Assert.Null(ChunkBlocksRle.Decode(new ushort[] { 1, 10 }, 4096));         // underfills the chunk
    }

    [Fact]
    public void ChunkDataMessage_RleShrinksTheWirePayload_AndRoundTripsTheCodec()
    {
        var terrain = new ushort[4096];
        for (int i = 0; i < 2000; i++) terrain[i] = 1;

        var denseMsg = new ChunkDataMessage { Cx = 1, Cy = 2, Cz = 3, Blocks = terrain };
        var rleMsg = new ChunkDataMessage { Cx = 1, Cy = 2, Cz = 3, BlocksRle = ChunkBlocksRle.Encode(terrain) };

        // The RLE form must round-trip the binary codec and decode back to the identical dense cells.
        var typed = Assert.IsType<ChunkDataMessage>(NetCodec.Decode(NetCodec.Encode(rleMsg)));
        Assert.Equal(terrain, typed.DecodeBlocks(terrain.Length));

        // Wire-size assertions: dominant on the JSON path (browser websocket / WebGL loopback), and the
        // MessagePack payload shrinks too.
        int jsonDense = System.Text.Json.JsonSerializer.Serialize(denseMsg).Length;
        int jsonRle = System.Text.Json.JsonSerializer.Serialize(rleMsg).Length;
        Assert.True(jsonRle < jsonDense / 20, $"JSON: RLE {jsonRle} B should be far under dense {jsonDense} B");
        Assert.True(NetCodec.Encode(rleMsg).Length < NetCodec.Encode(denseMsg).Length);
    }

    [Fact]
    public void Codec_RoundTrips_FloraRegrowStarted()
    {
        var msg = new FloraRegrowStarted { X = 102, Y = 100, Z = -5, Block = 77, Seconds = 30f };
        var typed = Assert.IsType<FloraRegrowStarted>(NetCodec.Decode(NetCodec.Encode(msg)));

        Assert.Equal(102, typed.X);
        Assert.Equal(100, typed.Y);
        Assert.Equal(-5, typed.Z);
        Assert.Equal(77, typed.Block);
        Assert.Equal(30f, typed.Seconds);
    }

    [Fact]
    public void Codec_ReturnsNull_ForUnknownTag()
    {
        Assert.Null(NetCodec.Decode(new byte[] { 200, 1, 2, 3 }));
        Assert.Null(NetCodec.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void Codec_ReturnsNull_ForMalformedBody_DoesNotThrow()
    {
        // A registered tag byte followed by a corrupt MessagePack body must NOT throw (it would otherwise
        // crash the single-threaded server tick — a one-packet DoS); Decode swallows it and returns null.
        var good = NetCodec.Encode(new PlaceBlockIntent { X = 1, Y = 2, Z = 3, ItemKey = "iron_wall" });
        var corrupt = new byte[] { good[0], 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Null(NetCodec.Decode(corrupt));

        // A truncated body for a real tag is also dropped, not thrown.
        var truncated = new byte[] { good[0], good.Length > 2 ? good[1] : (byte)0x90 };
        Assert.Null(NetCodec.Decode(truncated));
    }

    [Fact]
    public void Loopback_DeliversBothDirections_AndSignalsConnection()
    {
        var link = new LoopbackLink();
        using var server = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        int connectedId = -1;
        object? serverGot = null;
        object? clientGot = null;

        server.ClientConnected += id => connectedId = id;
        server.PayloadReceived += (_, payload) => serverGot = NetCodec.Decode(payload);
        client.PayloadReceived += payload => clientGot = NetCodec.Decode(payload);

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);

        // Server processes the connect + message.
        server.Poll();
        Assert.Equal(1, connectedId);
        var join = Assert.IsType<JoinRequest>(serverGot);
        Assert.Equal("Pilot", join.PlayerName);

        // Server replies; client processes it.
        server.Send(connectedId, NetCodec.Encode(new JoinAccepted { PlayerId = "p1", WorldSeed = 99 }),
            DeliveryMode.ReliableOrdered);
        client.Poll();
        var accepted = Assert.IsType<JoinAccepted>(clientGot);
        Assert.Equal("p1", accepted.PlayerId);
        Assert.Equal(99, accepted.WorldSeed);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3190: MessagePack allocates the uncompressed size an LZ4 body DECLARES before decompressing it (up to 64 MB for
/// untrusted data), so a 14-byte packet used to cost the receiver 64 MB — before any login, on the server and in the
/// native client alike. <see cref="NetCodec.Decode"/> now checks the declared sizes against what LZ4 can produce from
/// the bytes received (≤ 255 per compressed byte) and drops a forged body unallocated, while every real message —
/// however compressible — still round-trips.
/// </summary>
public sealed class Lz4BombTests
{
    /// <summary>Allocation budget for decoding one forged packet — the guard itself allocates nothing; this leaves room
    /// for runtime noise and stays far below the 64 MB the packet used to cost.</summary>
    private const long AllocationBudgetBytes = 1024 * 1024;

    private const byte JoinRequestTag = 1;

    /// <summary>The packet from the issue: tag, fixarray(2), ext8 type 98 holding one Int32 (the declared length), and a
    /// bin8 block of two bytes — 14 bytes in all.</summary>
    private static byte[] BlockArrayBomb(int declared) => new byte[]
    {
        JoinRequestTag,
        0x92,                    // fixarray, 2 elements: the length header + one block
        0xC7, 0x05, 0x62,        // ext8, 5 bytes, type 98 (Lz4BlockArray)
        0xD2, (byte)(declared >> 24), (byte)(declared >> 16), (byte)(declared >> 8), (byte)declared, // Int32, big-endian
        0xC4, 0x02, 0x00, 0x00,  // bin8, 2 bytes of "LZ4"
    };

    /// <summary>The legacy single-block form MessagePack also decompresses: ext 99 holding the Int32 length + the block.</summary>
    private static byte[] SingleBlockBomb(int declared) => new byte[]
    {
        JoinRequestTag,
        0xC7, 0x07, 0x63,        // ext8, 7 bytes, type 99 (Lz4Block)
        0xD2, (byte)(declared >> 24), (byte)(declared >> 16), (byte)(declared >> 8), (byte)declared,
        0x00, 0x00,
    };

    /// <summary>Three blocks each declaring 20 MB — every one alone under the library's 64 MB, together 60 MB.</summary>
    private static byte[] ThreeBlockBomb()
    {
        int declared = 20 * 1024 * 1024;
        var bytes = new List<byte> { JoinRequestTag, 0x94, 0xC7, 0x0F, 0x62 }; // fixarray(4), ext8 15 bytes type 98
        for (int i = 0; i < 3; i++)
        {
            bytes.AddRange(new byte[] { 0xD2, (byte)(declared >> 24), (byte)(declared >> 16), (byte)(declared >> 8), (byte)declared });
        }

        for (int i = 0; i < 3; i++)
        {
            bytes.AddRange(new byte[] { 0xC4, 0x02, 0x00, 0x00 });
        }

        return bytes.ToArray();
    }

    private static long AllocatedWhileDecoding(byte[] payload, out object? decoded)
    {
        // Warm the codec (static registry, resolver caches) so only the decode itself is measured.
        Assert.NotNull(NetCodec.Decode(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "warm-up" })));
        long before = GC.GetAllocatedBytesForCurrentThread();
        decoded = NetCodec.Decode(payload);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void TheIssuesFourteenBytePacket_DecodesToNull_WithoutAllocatingTheDeclared64Mb()
    {
        byte[] bomb = BlockArrayBomb(64 * 1024 * 1024);
        Assert.Equal(14, bomb.Length);

        long allocated = AllocatedWhileDecoding(bomb, out object? decoded);

        Assert.Null(decoded);
        Assert.True(allocated < AllocationBudgetBytes, $"decoding a 14-byte packet allocated {allocated:N0} bytes");
    }

    [Theory]
    [InlineData(4 * 1024)]
    [InlineData(1024 * 1024)]
    [InlineData(16 * 1024 * 1024)]
    [InlineData(int.MaxValue)]
    public void AForgedDeclaration_OfAnySize_IsDroppedUnallocated(int declared)
    {
        foreach (byte[] bomb in new[] { BlockArrayBomb(declared), SingleBlockBomb(declared) })
        {
            long allocated = AllocatedWhileDecoding(bomb, out object? decoded);

            Assert.Null(decoded);
            Assert.True(allocated < AllocationBudgetBytes, $"declared {declared:N0} bytes: decoding allocated {allocated:N0}");
        }
    }

    [Fact]
    public void ManyBlocksThatAreEachSmall_AreJudgedByTheirTotal()
    {
        long allocated = AllocatedWhileDecoding(ThreeBlockBomb(), out object? decoded);

        Assert.Null(decoded);
        Assert.True(allocated < AllocationBudgetBytes, $"three 20 MB declarations allocated {allocated:N0} bytes");
    }

    /// <summary>A negative declared length is nonsense — dropped, never handed to the decompressor.</summary>
    [Fact]
    public void ANegativeDeclaredLength_IsDropped()
    {
        Assert.False(NetCodec.Lz4DeclarationIsPlausible(BlockArrayBomb(-1).AsMemory(1)));
        Assert.Null(NetCodec.Decode(BlockArrayBomb(-1)));
    }

    /// <summary>The bound must never reject real LZ4: a message made almost entirely of zeros compresses close to LZ4's
    /// limit, and it still round-trips — the shape of the largest, most repetitive server→client messages.</summary>
    [Fact]
    public void AHighlyCompressibleRealMessage_StillRoundTrips()
    {
        const int cells = 300_000;
        var ship = new LandedShipState
        {
            PlayerId = "Pilot",
            StructureId = "ship-1",
            X = new int[cells],
            Y = new int[cells],
            Z = new int[cells],
            Block = new ushort[cells],
        };

        byte[] payload = NetCodec.Encode(ship);
        byte[] plain = MessagePackSerializer.Serialize(typeof(LandedShipState), ship,
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance));
        Assert.True(plain.Length > 100 * payload.Length,
            $"the message should compress close to LZ4's limit ({plain.Length:N0} → {payload.Length:N0} bytes)");

        Assert.True(NetCodec.Lz4DeclarationIsPlausible(payload.AsMemory(1)));
        var decoded = Assert.IsType<LandedShipState>(NetCodec.Decode(payload));
        Assert.Equal(cells, decoded.X.Length);
        Assert.Equal(cells, decoded.Block.Length);
        Assert.Equal("ship-1", decoded.StructureId);
    }

    /// <summary>…and neither does a large message that hardly compresses (many blocks, ratio near 1).</summary>
    [Fact]
    public void ALargeRandomRealMessage_StillRoundTrips()
    {
        var random = new Random(3190);
        const int cells = 40_000; // ~720 KB of nearly incompressible MessagePack — under the 1 MB packet cap
        var ship = new LandedShipState { PlayerId = "Pilot", StructureId = "ship-2", X = new int[cells], Y = new int[cells], Z = new int[cells], Block = new ushort[cells] };
        for (int i = 0; i < cells; i++)
        {
            ship.X[i] = random.Next();
            ship.Y[i] = random.Next();
            ship.Z[i] = random.Next();
            ship.Block[i] = (ushort)random.Next(ushort.MaxValue);
        }

        byte[] payload = NetCodec.Encode(ship);
        Assert.True(payload.Length < NetCodec.MaxPacketBytes);

        var decoded = Assert.IsType<LandedShipState>(NetCodec.Decode(payload));
        Assert.Equal(ship.X, decoded.X);
        Assert.Equal(ship.Block, decoded.Block);
    }

    /// <summary>Bodies that are not LZ4 pass the check untouched: a short (uncompressed) message, and the JSON envelope.</summary>
    [Fact]
    public void UncompressedAndJsonMessages_AreNotAffected()
    {
        byte[] small = NetCodec.Encode(new ChatMessage { Sender = "Pilot", Text = "hi" });
        Assert.True(small.Length < NetCodec.CompressionMinLengthBytes); // below the LZ4 threshold — a plain body
        Assert.True(NetCodec.Lz4DeclarationIsPlausible(small.AsMemory(1)));
        Assert.Equal("hi", Assert.IsType<ChatMessage>(NetCodec.Decode(small)).Text);
        Assert.Equal("Pilot", Assert.IsType<JoinRequest>(NetCodec.Decode(NetCodec.EncodeJson(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" }))).PlayerName);
    }
}

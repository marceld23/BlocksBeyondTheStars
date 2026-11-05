// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using MessagePack;

namespace BlocksBeyondTheStars.Networking;

/// <summary>
/// #3190: the LZ4 size check in front of the MessagePack decoder. MessagePack reads the uncompressed length an LZ4 body
/// DECLARES and allocates a buffer of that size before it decompresses a single byte — up to
/// <c>MessagePackSecurity.MaximumDecompressedSize</c>, 64 MB for untrusted data. A 14-byte packet (tag, array header,
/// ext 98 declaring 64 MB, a 2-byte block) therefore cost the receiver 64 MB, before any login and on both transports;
/// about 90 of them per second stalled a world's tick in garbage collection. The same <see cref="Decode"/> runs in the
/// native client, so a hostile server could do the same to a player.
/// <para>
/// LZ4 cannot produce more than about 255 bytes from one compressed byte (a match-length extension byte adds at most
/// 255 to a match), so a body whose declared lengths exceed that ratio over the bytes actually received is forged and
/// is dropped here — without allocating. Every legitimate body passes: the encoder's real LZ4 output always fits the
/// bound, however compressible the message was, so large server→client messages keep working.
/// </para>
/// </summary>
public static partial class NetCodec
{
    /// <summary>#3190: the most bytes LZ4 can produce from one compressed byte. The format's own limit: each further
    /// match-length byte extends a match by at most 255 bytes, and every sequence spends at least three bytes (token +
    /// offset) on at most 19 bytes of output — so the output never exceeds 255 × the compressed input.</summary>
    internal const int MaxLz4ExpansionRatio = 255;

    /// <summary>#3190: slack on top of the ratio bound, so a tiny legitimate block can never be judged by rounding.</summary>
    internal const int Lz4DeclarationSlackBytes = 1024;

    /// <summary>MessagePack's own extension type codes for its LZ4 bodies (<c>ThisLibraryExtensionTypeCodes</c>, internal
    /// in the library): the block array the codec writes, and the legacy single block the reader still accepts.</summary>
    private const sbyte Lz4BlockArrayExtType = 98;
    private const sbyte Lz4BlockExtType = 99;

    /// <summary>
    /// #3190: false when <paramref name="body"/> is an LZ4 body (an <c>Lz4BlockArray</c> array or a single
    /// <c>Lz4Block</c> extension — MessagePack decompresses either, whatever the configured compression) whose declared
    /// uncompressed lengths add up to more than the received bytes can produce, or whose LZ4 header is malformed. Any
    /// other body is not compressed and passes untouched. Reads the header only; allocates nothing.
    /// </summary>
    internal static bool Lz4DeclarationIsPlausible(ReadOnlyMemory<byte> body)
    {
        try
        {
            var reader = new MessagePackReader(body);
            if (reader.End)
            {
                return true;
            }

            return reader.NextMessagePackType switch
            {
                MessagePackType.Extension => SingleBlockIsPlausible(ref reader, body.Length),
                MessagePackType.Array => BlockArrayIsPlausible(ref reader, body.Length),
                _ => true,
            };
        }
        catch (Exception)
        {
            // A header the reader cannot even parse would fail the deserializer as well — drop it here, unallocated.
            return false;
        }
    }

    /// <summary>The legacy single-block form: ext 99 holding a MessagePack Int32 (the declared length) and the LZ4 bytes.</summary>
    private static bool SingleBlockIsPlausible(ref MessagePackReader reader, int bodyLength)
    {
        ExtensionHeader header = reader.ReadExtensionFormatHeader();
        if (header.TypeCode != Lz4BlockExtType)
        {
            return true;
        }

        long declared = reader.ReadInt32();
        return declared >= 0 && declared <= MaxLz4Output(bodyLength - reader.Consumed);
    }

    /// <summary>The block-array form the codec writes: <c>[ext 98 (Int32 length per block), bin, bin, …]</c>.</summary>
    private static bool BlockArrayIsPlausible(ref MessagePackReader reader, int bodyLength)
    {
        int count = reader.ReadArrayHeader();
        if (count == 0 || reader.NextMessagePackType != MessagePackType.Extension)
        {
            return true;
        }

        ExtensionHeader header = reader.ReadExtensionFormatHeader();
        if (header.TypeCode != Lz4BlockArrayExtType)
        {
            return true;
        }

        // MessagePack reads one Int32 per block straight after the ext header (count - 1 of them) — sum them the same way.
        long declaredTotal = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int declared = reader.ReadInt32();
            if (declared < 0)
            {
                return false;
            }

            declaredTotal += declared;
        }

        // Everything after the length list is the compressed blocks (their bin headers included — a few bytes of
        // generosity in the bound, never too little).
        return declaredTotal <= MaxLz4Output(bodyLength - reader.Consumed);
    }

    private static long MaxLz4Output(long compressedBytes)
        => (System.Math.Max(0L, compressedBytes) * MaxLz4ExpansionRatio) + Lz4DeclarationSlackBytes;
}

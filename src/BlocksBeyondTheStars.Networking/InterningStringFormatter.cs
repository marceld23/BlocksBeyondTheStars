// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using MessagePack;
using MessagePack.Formatters;

namespace BlocksBeyondTheStars.Networking;

/// <summary>
/// The codec's string formatter (#1555): short strings are interned per decoding thread, so the same NPC name,
/// role key, biome key or item id that arrives in every entity list several times a second is decoded to
/// ONE string instance instead of a fresh one per message — the client saw ~280 string allocations per second
/// from the entity lists alone. Long strings (chat, descriptions) are decoded as before. Encoding is untouched,
/// so the wire format and every existing round-trip test stay the same; only the identity of equal short
/// strings changes, which no caller depends on.
/// </summary>
public sealed class InterningStringFormatter : IMessagePackFormatter<string?>
{
    public static readonly InterningStringFormatter Instance = new();

    /// <summary>Strings longer than this many UTF-8 bytes are not worth a cache lookup (and are rarely repeated).</summary>
    public const int MaxInternedBytes = 64;

    /// <summary>Per-thread cache ceiling; a full cache is simply dropped and refilled (a bounded, allocation-free reset).</summary>
    public const int MaxEntries = 4096;

    [ThreadStatic]
    private static Dictionary<long, string>? _cache;

    private InterningStringFormatter()
    {
    }

    public void Serialize(ref MessagePackWriter writer, string? value, MessagePackSerializerOptions options)
        => writer.Write(value);

    public string? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var sequence = reader.ReadStringSequence();
        if (sequence is null)
        {
            return null;
        }

        var seq = sequence.Value;
        if (seq.Length == 0)
        {
            return string.Empty;
        }

        if (seq.Length > MaxInternedBytes)
        {
            return DecodeSlow(seq);
        }

        Span<byte> bytes = stackalloc byte[MaxInternedBytes];
        seq.CopyTo(bytes);
        bytes = bytes.Slice(0, (int)seq.Length);

        // 64-bit FNV-1a over the UTF-8 bytes; a collision only costs a re-decode (the entry is verified).
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < bytes.Length; i++)
        {
            h = (h ^ bytes[i]) * 1099511628211UL;
        }

        long key = unchecked((long)h);
        var cache = _cache ??= new Dictionary<long, string>();
        if (cache.TryGetValue(key, out var hit) && SameBytes(hit, bytes))
        {
            return hit;
        }

        string s = Encoding.UTF8.GetString(bytes);
        if (cache.Count >= MaxEntries)
        {
            cache.Clear();
        }

        cache[key] = s;
        return s;
    }

    private static bool SameBytes(string candidate, ReadOnlySpan<byte> bytes)
    {
        if (candidate.Length > bytes.Length)
        {
            return false; // a UTF-8 string never has more chars than bytes
        }

        Span<byte> encoded = stackalloc byte[MaxInternedBytes];
        int n = Encoding.UTF8.GetBytes(candidate.AsSpan(), encoded);
        return n == bytes.Length && encoded.Slice(0, n).SequenceEqual(bytes);
    }

    private static string DecodeSlow(in ReadOnlySequence<byte> seq)
    {
        if (seq.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(seq.First.Span);
        }

        var rented = ArrayPool<byte>.Shared.Rent((int)seq.Length);
        try
        {
            seq.CopyTo(rented);
            return Encoding.UTF8.GetString(rented, 0, (int)seq.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>One cell of a copied build region: the block KEY (portable across saves — numeric ids are not),
/// its packed shape descriptor (design bits stripped: paint-design ids are save-local) and its dye/glow.</summary>
public struct BlueprintCell
{
    /// <summary>Block key; null/empty = air.</summary>
    public string? Key;
    public int Shape;
    public int Tint;
    public int Glow;
}

/// <summary>
/// Whole-build share codes (#1117): a region of up to 16×16×16 placed blocks serialised to a
/// <c>BBTS1-B-…</c> code a player can paste into chat, a forum post or a message — the structures'
/// counterpart to the form/paint codes (#846). The payload is deliberately compact (palette + RLE cells +
/// per-cell modifiers, Deflate-compressed) and, like every share code, NOT a security boundary: the server
/// re-validates every cell on paste with the same rules as a hand-placed block.
/// </summary>
public static class BlueprintCode
{
    /// <summary>Kind marker in the <see cref="ShareCode"/> envelope.</summary>
    public const string Kind = "B";

    /// <summary>Maximum region edge in blocks.</summary>
    public const int MaxEdge = 16;

    /// <summary>Hard cap on the compressed payload bytes a decode will accept — a hostile code can then
    /// never balloon memory (the legitimate worst case, a dense mixed 16³ region, stays well under this).</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    private const byte Version = 1;

    /// <summary>Encodes a copied region to a share code. Cells run x → y → z (x outermost).</summary>
    public static string Encode(int sx, int sy, int sz, string author, string name, IReadOnlyList<BlueprintCell> cells)
    {
        if (sx is < 1 or > MaxEdge || sy is < 1 or > MaxEdge || sz is < 1 or > MaxEdge || cells.Count != sx * sy * sz)
        {
            return string.Empty;
        }

        // Palette of distinct block keys (index 0 is reserved for air).
        var palette = new List<string>();
        var paletteIndex = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var c in cells)
        {
            if (!string.IsNullOrEmpty(c.Key) && !paletteIndex.ContainsKey(c.Key!))
            {
                if (palette.Count >= 255)
                {
                    return string.Empty; // more distinct block KINDS than a byte index carries — the game has ~200, so this is a format guard, not a real limit
                }

                paletteIndex[c.Key!] = (byte)(palette.Count + 1);
                palette.Add(c.Key!);
            }
        }

        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write((byte)sx);
            w.Write((byte)sy);
            w.Write((byte)sz);
            WriteShortString(w, author);
            w.Write((byte)palette.Count);
            foreach (var key in palette)
            {
                WriteShortString(w, key);
            }

            // Cell palette indices, run-length encoded (air-heavy regions collapse to almost nothing).
            int i = 0;
            while (i < cells.Count)
            {
                byte index = string.IsNullOrEmpty(cells[i].Key) ? (byte)0 : paletteIndex[cells[i].Key!];
                int run = 1;
                while (i + run < cells.Count && run < 255)
                {
                    byte next = string.IsNullOrEmpty(cells[i + run].Key) ? (byte)0 : paletteIndex[cells[i + run].Key!];
                    if (next != index)
                    {
                        break;
                    }

                    run++;
                }

                w.Write((byte)run);
                w.Write(index);
                i += run;
            }

            // Modifiers for the non-air cells, in cell order. Shape descriptors without design bits fit a
            // ushort (yaw 2 + shape 6 + upFace 3 bits); tint/glow are 24-bit RGB ints. Deflate flattens the
            // usual all-zero runs.
            foreach (var c in cells)
            {
                if (string.IsNullOrEmpty(c.Key))
                {
                    continue;
                }

                w.Write((ushort)(ShapeCode.Pack(ShapeCode.ShapeOf(c.Shape), ShapeCode.OrientationOf(c.Shape), ShapeCode.UpFaceOf(c.Shape)) & 0xFFFF));
                w.Write(c.Tint);
                w.Write(c.Glow);
            }
        }

        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        return ShareCode.Encode(Kind, Convert.ToBase64String(packed.ToArray()), name);
    }

    /// <summary>Decodes a build share code. False for anything malformed or over the caps — a hostile or
    /// mistyped code must simply not decode, never throw and never allocate unboundedly.</summary>
    public static bool TryDecode(string? code, out int sx, out int sy, out int sz, out string author, out string name, out BlueprintCell[] cells)
    {
        sx = sy = sz = 0;
        author = string.Empty;
        cells = Array.Empty<BlueprintCell>();
        if (!ShareCode.TryDecode(code, Kind, out string payload, out name))
        {
            return false;
        }

        try
        {
            var packed = Convert.FromBase64String(payload);
            if (packed.Length > MaxPayloadBytes)
            {
                return false;
            }

            using var inflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            CopyBounded(inflate, raw, MaxPayloadBytes * 4); // a zip bomb stops here, not at OOM
            raw.Position = 0;
            using var r = new BinaryReader(raw, Encoding.UTF8);

            if (r.ReadByte() != Version)
            {
                return false;
            }

            sx = r.ReadByte();
            sy = r.ReadByte();
            sz = r.ReadByte();
            if (sx is < 1 or > MaxEdge || sy is < 1 or > MaxEdge || sz is < 1 or > MaxEdge)
            {
                return false;
            }

            author = ReadShortString(r);
            int paletteCount = r.ReadByte();
            var palette = new string[paletteCount];
            for (int p = 0; p < paletteCount; p++)
            {
                palette[p] = ReadShortString(r);
                if (palette[p].Length == 0)
                {
                    return false;
                }
            }

            int total = sx * sy * sz;
            var indices = new byte[total];
            int filled = 0;
            while (filled < total)
            {
                int run = r.ReadByte();
                byte index = r.ReadByte();
                if (run < 1 || filled + run > total || index > paletteCount)
                {
                    return false;
                }

                for (int k = 0; k < run; k++)
                {
                    indices[filled++] = index;
                }
            }

            cells = new BlueprintCell[total];
            for (int c = 0; c < total; c++)
            {
                if (indices[c] == 0)
                {
                    continue;
                }

                cells[c] = new BlueprintCell
                {
                    Key = palette[indices[c] - 1],
                    Shape = r.ReadUInt16(),
                    Tint = r.ReadInt32(),
                    Glow = r.ReadInt32(),
                };
            }

            return true;
        }
        catch (Exception)
        {
            sx = sy = sz = 0;
            author = string.Empty;
            cells = Array.Empty<BlueprintCell>();
            return false; // truncated, not base64, not deflate — same answer either way
        }
    }

    /// <summary>Cell index for local coordinates in the fixed x → y → z visit order.</summary>
    public static int CellIndex(int x, int y, int z, int sy, int sz) => (x * sy + y) * sz + z;

    private static void WriteShortString(BinaryWriter w, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        int len = Math.Min(bytes.Length, 64);
        w.Write((byte)len);
        w.Write(bytes, 0, len);
    }

    private static string ReadShortString(BinaryReader r)
    {
        int len = r.ReadByte();
        return len == 0 ? string.Empty : Encoding.UTF8.GetString(r.ReadBytes(len));
    }

    private static void CopyBounded(Stream from, Stream to, int maxBytes)
    {
        var buffer = new byte[8192];
        int total = 0;
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("payload exceeds the decode cap");
            }

            to.Write(buffer, 0, read);
        }
    }
}

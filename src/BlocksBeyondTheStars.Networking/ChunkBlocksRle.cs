// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking;

/// <summary>
/// Run-length codec for the chunk block payload: flat (value, runLength) ushort pairs. Terrain chunks are
/// highly runnable (large air/stone spans in cell-index order), so this typically shrinks the 4096-cell
/// array by an order of magnitude — decisive on the browser JSON path, where every cell is otherwise a
/// plain JSON number, and a welcome cut to native MessagePack payloads and VPS egress too. The server
/// only sends the RLE form when it is actually smaller, so a degenerate (unrunnable) chunk ships dense.
/// </summary>
public static class ChunkBlocksRle
{
    /// <summary>Encodes a dense cell array as flat (value, runLength) pairs. A run is capped at
    /// ushort.MaxValue, comfortably above the 4096 cells a chunk holds.</summary>
    public static ushort[] Encode(ushort[] dense) => Encode((System.ReadOnlySpan<ushort>)dense);

    // #1532: the run buffer is a reusable thread-static (worst case 2·n entries — a checkerboard) trimmed to an
    // exact-size result; the old path grew a List and copied it once more with ToArray().
    [System.ThreadStatic] private static ushort[]? _runScratch;

    /// <summary>The same encoding straight from a chunk's backing array (<c>ChunkData.RawBlocks</c>) — the
    /// server no longer clones 4096 cells per streamed chunk just to run-length them (#1532).</summary>
    public static ushort[] Encode(System.ReadOnlySpan<ushort> dense)
    {
        if (dense.Length == 0)
        {
            return System.Array.Empty<ushort>();
        }

        var runs = _runScratch;
        if (runs == null || runs.Length < dense.Length * 2)
        {
            runs = _runScratch = new ushort[System.Math.Max(dense.Length * 2, 256)];
        }

        int w = 0;
        ushort value = dense[0];
        int count = 1;
        for (int i = 1; i < dense.Length; i++)
        {
            if (dense[i] == value && count < ushort.MaxValue)
            {
                count++;
                continue;
            }

            runs[w++] = value;
            runs[w++] = (ushort)count;
            value = dense[i];
            count = 1;
        }

        runs[w++] = value;
        runs[w++] = (ushort)count;
        var result = new ushort[w];
        System.Array.Copy(runs, result, w);
        return result;
    }

    /// <summary>Decodes flat (value, runLength) pairs into a dense array of exactly
    /// <paramref name="expectedLength"/> cells. Null when the stream is malformed (odd pair count,
    /// zero-length run, or a total that does not match) — callers drop the chunk, mirroring how a
    /// wrong-length dense payload is handled.</summary>
    public static ushort[]? Decode(ushort[] rle, int expectedLength)
    {
        var dense = new ushort[expectedLength];
        return DecodeInto(rle, dense, expectedLength) ? dense : null;
    }

    /// <summary>Decodes into a caller-owned array (#1555: the client rents it for the chunk's lifetime; a pooled
    /// array is not zeroed, so the first <paramref name="expectedLength"/> cells are cleared here). Same
    /// validation and result as <see cref="Decode"/>; false when the stream is malformed.</summary>
    public static bool DecodeInto(ushort[] rle, ushort[] dense, int expectedLength)
    {
        if (rle.Length == 0 || (rle.Length & 1) != 0 || dense.Length < expectedLength)
        {
            return false;
        }

        Array.Clear(dense, 0, expectedLength);
        int pos = 0;
        for (int i = 0; i < rle.Length; i += 2)
        {
            ushort value = rle[i];
            int count = rle[i + 1];
            if (count == 0 || pos + count > expectedLength)
            {
                return false;
            }

            if (value == 0)
            {
                pos += count; // the array is zero-initialised — air runs are free
            }
            else
            {
                for (int k = 0; k < count; k++)
                {
                    dense[pos++] = value;
                }
            }
        }

        return pos == expectedLength;
    }
}

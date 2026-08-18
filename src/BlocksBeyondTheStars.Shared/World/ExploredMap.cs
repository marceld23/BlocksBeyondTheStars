// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The coarse per-body explored-map grid (#1113): one bit per <see cref="CellChunks"/>×<see cref="CellChunks"/>
/// block of chunk columns, row-major west→east / north→south over the body's canonical chunk domain. Server
/// and client MUST derive the grid from the same circumference through these helpers — the bitmap itself
/// carries no dimensions on disk. Worst-case body (circumference 16000) is 125×63 cells ≈ 985 bytes, so a
/// whole galaxy of fully-mapped worlds stays well under a megabyte inside the player's save blob.
/// </summary>
public static class ExploredMap
{
    /// <summary>Cell edge in chunk columns (8 chunks = 128 blocks) — coarse on purpose: the planet map's
    /// remembered fog only needs "was here", not terrain.</summary>
    public const int CellChunks = 8;

    /// <summary>Hard ceiling for one body's bitmap — anything larger is refused outright (a tampered save
    /// can then never balloon the player blob).</summary>
    public const int MaxBytesPerBody = 4096;

    /// <summary>Grid dimensions for a body of the given circumference.</summary>
    public static (int Cols, int Rows) GridFor(int circumference)
    {
        int chunksAround = System.Math.Max(1, circumference / WorldConstants.ChunkSize);
        int latChunks = System.Math.Max(1, WorldConstants.LatitudePeriodFor(circumference) / WorldConstants.ChunkSize);
        return (
            (chunksAround + CellChunks - 1) / CellChunks,
            (latChunks + CellChunks - 1) / CellChunks);
    }

    /// <summary>Bitmap byte length for a grid.</summary>
    public static int ByteSize(int cols, int rows) => (cols * rows + 7) / 8;

    /// <summary>Cell bit index for a CANONICAL chunk column — X in [0, chunksAround) (see
    /// <see cref="WorldConstants.CanonicalChunkX"/>), Z in the centred latitude band (see
    /// <see cref="WorldConstants.CanonicalChunkZ"/>) — or -1 when the column lies outside the grid.</summary>
    public static int CellIndex(int chunkX, int chunkZ, int circumference)
    {
        int chunksAround = System.Math.Max(1, circumference / WorldConstants.ChunkSize);
        int latChunks = System.Math.Max(1, WorldConstants.LatitudePeriodFor(circumference) / WorldConstants.ChunkSize);
        int shiftedZ = chunkZ + latChunks / 2; // the centred band moved to [0, latChunks)
        if (chunkX < 0 || chunkX >= chunksAround || shiftedZ < 0 || shiftedZ >= latChunks)
        {
            return -1; // bounds-check at CHUNK level — the last cell must not swallow out-of-domain columns
        }

        var (cols, _) = GridFor(circumference);
        return (shiftedZ / CellChunks) * cols + chunkX / CellChunks;
    }

    /// <summary>True when the cell bit is set (out-of-range indices read as unexplored).</summary>
    public static bool GetBit(byte[]? cells, int index)
        => cells != null && index >= 0 && index / 8 < cells.Length && (cells[index / 8] & (1 << (index % 8))) != 0;

    /// <summary>Sets a cell bit; returns true when the bit was newly set.</summary>
    public static bool SetBit(byte[] cells, int index)
    {
        if (index < 0 || index / 8 >= cells.Length)
        {
            return false;
        }

        int mask = 1 << (index % 8);
        if ((cells[index / 8] & mask) != 0)
        {
            return false;
        }

        cells[index / 8] |= (byte)mask;
        return true;
    }
}

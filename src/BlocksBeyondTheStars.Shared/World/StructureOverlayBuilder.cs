// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Collects the cells of a <see cref="StructureOverlay"/> the way a stamp writes them: cell by cell, a later write of the
/// same cell replacing the earlier one, and a read of a cell already written answering what was written last. Positions are
/// CANONICAL world cells (<see cref="WorldConstants.CanonicalBlock"/>) — the caller wraps the seams. One dense block array and
/// a written-bit set per touched chunk, the dye, glow and shape only for the cells that carry one.
/// </summary>
public sealed class StructureOverlayBuilder
{
    private const int WordsPerChunk = WorldConstants.BlocksPerChunk / 64;

    private sealed class ChunkCells
    {
        public readonly ushort[] Blocks = new ushort[WorldConstants.BlocksPerChunk];
        public readonly ulong[] Written = new ulong[WordsPerChunk];
        public Dictionary<int, (int Tint, int Glow, int Shape)>? Extras;
        public int Count;
    }

    private readonly Dictionary<ChunkCoord, ChunkCells> _chunks = new();

    // A stamp writes column after column: most writes land in the chunk of the write before.
    private ChunkCoord _lastCoord;
    private ChunkCells? _lastChunk;

    /// <summary>The distinct cells written so far.</summary>
    public int Count { get; private set; }

    /// <summary>Writes one canonical cell: the block (air carves), its dye and glow (0xRRGGBB, 0 = none — only the low 24
    /// bits are kept, as a chunk keeps them) and its packed shape (0 = a plain cube).</summary>
    public void Set(Vector3i cell, ushort block, int tint, int glow, int shape)
    {
        var coord = WorldConstants.WorldToChunk(cell);
        ChunkCells? chunk = _lastChunk is not null && _lastCoord.Equals(coord) ? _lastChunk : null;
        if (chunk is null && !_chunks.TryGetValue(coord, out chunk))
        {
            chunk = new ChunkCells();
            _chunks[coord] = chunk;
        }

        (_lastCoord, _lastChunk) = (coord, chunk);
        var l = WorldConstants.WorldToLocal(cell);
        int local = WorldConstants.LocalIndex(l.X, l.Y, l.Z);
        ulong bit = 1UL << (local & 63);
        if ((chunk.Written[local >> 6] & bit) == 0)
        {
            chunk.Written[local >> 6] |= bit;
            chunk.Count++;
            Count++;
        }

        chunk.Blocks[local] = block;
        tint &= 0xFFFFFF;
        glow &= 0xFFFFFF;
        if (tint != 0 || glow != 0 || shape != 0)
        {
            (chunk.Extras ??= new Dictionary<int, (int, int, int)>())[local] = (tint, glow, shape);
        }
        else
        {
            chunk.Extras?.Remove(local);
        }
    }

    /// <summary>The block last written to a canonical cell — false when the cell was never written.</summary>
    public bool TryGet(Vector3i cell, out ushort block)
    {
        block = 0;
        if (!_chunks.TryGetValue(WorldConstants.WorldToChunk(cell), out var chunk))
        {
            return false;
        }

        var l = WorldConstants.WorldToLocal(cell);
        int local = WorldConstants.LocalIndex(l.X, l.Y, l.Z);
        if ((chunk.Written[local >> 6] & (1UL << (local & 63))) == 0)
        {
            return false;
        }

        block = chunk.Blocks[local];
        return true;
    }

    /// <summary>The overlay: the touched chunks in ascending (X, Y, Z) order, each chunk's cells in ascending local index.</summary>
    public StructureOverlay Build()
    {
        var coords = _chunks.Keys.ToList();
        coords.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z));
        var chunks = new List<StructureOverlayChunk>(coords.Count);
        foreach (var coord in coords)
        {
            chunks.Add(BuildChunk(coord, _chunks[coord]));
        }

        return new StructureOverlay(chunks);
    }

    /// <summary>One chunk's runs: consecutive written cells with one value merge.</summary>
    private static StructureOverlayChunk BuildChunk(ChunkCoord coord, ChunkCells cells)
    {
        var starts = new List<ushort>();
        var lengths = new List<ushort>();
        var blocks = new List<ushort>();
        var extras = new List<(int Tint, int Glow, int Shape)>();
        int end = -1;
        for (int local = 0; local < WorldConstants.BlocksPerChunk; local++)
        {
            if ((cells.Written[local >> 6] & (1UL << (local & 63))) == 0)
            {
                continue;
            }

            ushort block = cells.Blocks[local];
            var extra = cells.Extras is not null && cells.Extras.TryGetValue(local, out var e) ? e : (0, 0, 0);
            int last = starts.Count - 1;
            if (local == end && blocks[last] == block && extras[last] == extra)
            {
                lengths[last]++;
            }
            else
            {
                starts.Add((ushort)local);
                lengths.Add(1);
                blocks.Add(block);
                extras.Add(extra);
            }

            end = local + 1;
        }

        return new StructureOverlayChunk(coord, starts.ToArray(), lengths.ToArray(), blocks.ToArray(),
            Column(extras, e => e.Tint), Column(extras, e => e.Glow), Column(extras, e => e.Shape));
    }

    /// <summary>One value of every run, or null when it is 0 throughout.</summary>
    private static int[]? Column(List<(int Tint, int Glow, int Shape)> extras, Func<(int Tint, int Glow, int Shape), int> pick)
    {
        int[]? values = null;
        for (int r = 0; r < extras.Count; r++)
        {
            int v = pick(extras[r]);
            if (v != 0)
            {
                (values ??= new int[extras.Count])[r] = v;
            }
        }

        return values;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Dense block storage for a single chunk: a flat array of block ids indexed via
/// <see cref="WorldConstants.LocalIndex"/>. This is the in-memory representation used
/// by both client and server; persistence stores only the deltas against the
/// procedurally generated baseline.
/// </summary>
public sealed class ChunkData
{
    public ChunkCoord Coord { get; }

    private readonly ushort[] _blocks;

    // #1532: a process-wide monotonic stamp, taken at construction and on every mutation, so an encoded wire
    // payload can be cached per chunk and reused until the chunk changes (a regenerated chunk gets a fresh
    // stamp, so a stale cache entry can never match it).
    private static long s_versionClock;

    /// <summary>Changes whenever a cell, modifier or shape of this chunk is written (#1532).</summary>
    public long Version { get; private set; } = System.Threading.Interlocked.Increment(ref s_versionClock);

    private void Touch() => Version = System.Threading.Interlocked.Increment(ref s_versionClock);

    /// <summary>
    /// Sparse per-voxel colour modifiers, keyed by local index: a surface tint (0xRRGGBB) and/or a
    /// light colour (0xRRGGBB) stamped on a placed dyed/glowing block. Lazily allocated — the vast
    /// majority of cells carry none, so this stays tiny (only edited colour cells appear). A value
    /// of 0 means "none" for that channel.
    /// </summary>
    private Dictionary<int, (int Tint, int Glow)>? _mods;

    /// <summary>
    /// Sparse per-voxel SHAPE descriptors, keyed by local index: the packed (shape index, orientation) of a
    /// placed non-cube building block (see <see cref="ShapeCode"/>). Independent of the colour modifier so
    /// dye + shape compose freely. Lazily allocated and tiny — only shaped cells appear; 0 means "plain cube".
    /// </summary>
    private Dictionary<int, int>? _shapes;

    /// <summary>
    /// #3118: the EDITED non-air cells — cells that hold a stored delta against the generated baseline: a player's build,
    /// a settlement stamp, weather snow. The server knows them from its edit rows and streams them with the chunk, so the
    /// client can tell built ground from natural ground (the ground details stay off built cells). A bitset over the local
    /// indices, allocated with the first edit: a fixed 512 bytes however many cells a village stamps. A cell set to air
    /// drops out, like its modifier and shape.
    /// </summary>
    private ulong[]? _edited;
    private int _editedCount;

    public ChunkData(ChunkCoord coord)
    {
        Coord = coord;
        _blocks = new ushort[WorldConstants.BlocksPerChunk];
    }

    private ChunkData(ChunkCoord coord, ushort[] blocks)
    {
        Coord = coord;
        _blocks = blocks;
    }

    /// <summary>Wraps an existing raw block array (used when loading from storage).</summary>
    public static ChunkData FromRaw(ChunkCoord coord, ushort[] blocks)
    {
        if (blocks.Length != WorldConstants.BlocksPerChunk)
        {
            throw new ArgumentException(
                $"Expected {WorldConstants.BlocksPerChunk} blocks, got {blocks.Length}.", nameof(blocks));
        }

        return new ChunkData(coord, blocks);
    }

    public BlockId Get(int x, int y, int z) => new(_blocks[WorldConstants.LocalIndex(x, y, z)]);

    public void Set(int x, int y, int z, BlockId block)
    {
        int idx = WorldConstants.LocalIndex(x, y, z);
        _blocks[idx] = block.Value;
        Touch();

        // A cleared (air) cell can carry no colour — drop any stale modifier so mining a dyed block
        // and re-placing a plain one there never inherits the old colour.
        if (block.Value == BlockId.AirValue)
        {
            _mods?.Remove(idx);
            _shapes?.Remove(idx);
            if (IsEditedLocal(idx))
            {
                _edited![idx >> 6] &= ~(1UL << (idx & 63));
                _editedCount--;
            }
        }
    }

    /// <summary>Read-only view of the raw backing array for serialization.</summary>
    public ReadOnlySpan<ushort> RawBlocks => _blocks;

    public ushort[] ToArray() => (ushort[])_blocks.Clone();

    // --- Per-voxel colour modifiers (dyed surface tint + glow light colour) ---

    /// <summary>The colour modifier at a cell (Tint/Glow as 0xRRGGBB, 0 = none).</summary>
    public (int Tint, int Glow) GetModifier(int x, int y, int z) => GetModifierLocal(WorldConstants.LocalIndex(x, y, z));

    /// <summary>The colour modifier at a flat local index.</summary>
    public (int Tint, int Glow) GetModifierLocal(int localIndex)
        => _mods is not null && _mods.TryGetValue(localIndex, out var m) ? m : (0, 0);

    /// <summary>Stamps (or clears, when both colours are 0) the colour modifier at a cell.</summary>
    public void SetModifier(int x, int y, int z, int tint, int glow)
        => SetModifierLocal(WorldConstants.LocalIndex(x, y, z), tint, glow);

    /// <summary>Stamps (or clears) the colour modifier at a flat local index.</summary>
    public void SetModifierLocal(int localIndex, int tint, int glow)
    {
        Touch();
        tint &= 0xFFFFFF;
        glow &= 0xFFFFFF;
        if (tint == 0 && glow == 0)
        {
            _mods?.Remove(localIndex);
            return;
        }

        (_mods ??= new Dictionary<int, (int, int)>())[localIndex] = (tint, glow);
    }

    /// <summary>True if any cell in this chunk carries a colour modifier.</summary>
    public bool HasModifiers => _mods is { Count: > 0 };

    /// <summary>Read-only view of the sparse modifiers (local index → tint/glow) for serialization.</summary>
    public IReadOnlyDictionary<int, (int Tint, int Glow)>? Modifiers => _mods;

    // --- Per-voxel shape descriptors (non-cube building forms; see ShapeCode) ---

    /// <summary>The packed shape descriptor at a cell (0 = plain cube).</summary>
    public int GetShape(int x, int y, int z) => GetShapeLocal(WorldConstants.LocalIndex(x, y, z));

    /// <summary>The packed shape descriptor at a flat local index (0 = plain cube).</summary>
    public int GetShapeLocal(int localIndex)
        => _shapes is not null && _shapes.TryGetValue(localIndex, out var s) ? s : 0;

    /// <summary>Stamps (or clears, when 0) the packed shape descriptor at a cell.</summary>
    public void SetShape(int x, int y, int z, int shape)
        => SetShapeLocal(WorldConstants.LocalIndex(x, y, z), shape);

    /// <summary>Stamps (or clears) the packed shape descriptor at a flat local index.</summary>
    public void SetShapeLocal(int localIndex, int shape)
    {
        Touch();
        if (shape == 0)
        {
            _shapes?.Remove(localIndex);
            return;
        }

        (_shapes ??= new Dictionary<int, int>())[localIndex] = shape;
    }

    /// <summary>True if any cell in this chunk carries a non-cube shape.</summary>
    public bool HasShapes => _shapes is { Count: > 0 };

    /// <summary>Read-only view of the sparse shape descriptors (local index → packed shape) for serialization.</summary>
    public IReadOnlyDictionary<int, int>? Shapes => _shapes;

    // --- Edited cells (#3118: built vs natural ground) ---

    /// <summary>Whether the cell at a flat local index holds a stored edit (a placed or stamped block, not air).</summary>
    public bool IsEditedLocal(int localIndex) => _edited is not null && (_edited[localIndex >> 6] & (1UL << (localIndex & 63))) != 0;

    /// <summary>Marks the cell at a flat local index as edited. An air cell is never marked (mining leaves natural
    /// ground below, which keeps its look).</summary>
    public void MarkEditedLocal(int localIndex)
    {
        if (_blocks[localIndex] == BlockId.AirValue || IsEditedLocal(localIndex))
        {
            return;
        }

        (_edited ??= new ulong[WorldConstants.BlocksPerChunk / 64])[localIndex >> 6] |= 1UL << (localIndex & 63);
        _editedCount++;
        Touch();
    }

    /// <summary>True if any cell in this chunk is marked as edited.</summary>
    public bool HasEdits => _editedCount > 0;

    /// <summary>Takes over another chunk's edited cells (the client's mesh-worker copy of a chunk).</summary>
    public void CopyEditsFrom(ChunkData source)
    {
        _edited = source._edited is null ? null : (ulong[])source._edited.Clone();
        _editedCount = source._editedCount;
        Touch();
    }

    /// <summary>The edited cells as flat (start, length) runs over the ascending local indices — the wire form: a
    /// settlement chunk with thousands of stamped cells packs into a few hundred numbers. Empty when none.</summary>
    public ushort[] EditedRuns()
    {
        if (_edited is null || _editedCount == 0)
        {
            return Array.Empty<ushort>();
        }

        var runs = new List<ushort>();
        int start = -1;
        for (int i = 0; i <= WorldConstants.BlocksPerChunk; i++)
        {
            bool on = i < WorldConstants.BlocksPerChunk && IsEditedLocal(i);
            if (on && start < 0)
            {
                start = i;
            }
            else if (!on && start >= 0)
            {
                runs.Add((ushort)start);
                runs.Add((ushort)(i - start));
                start = -1;
            }
        }

        return runs.ToArray();
    }

    /// <summary>Marks the cells of (start, length) runs as edited (<see cref="EditedRuns"/>' form). Odd trailing or
    /// out-of-range entries are ignored, so a malformed payload can only under-mark, never throw.</summary>
    public void MarkEditedRuns(ushort[]? runs)
    {
        if (runs is null)
        {
            return;
        }

        for (int r = 0; r + 1 < runs.Length; r += 2)
        {
            int end = Math.Min(runs[r] + runs[r + 1], WorldConstants.BlocksPerChunk);
            for (int i = runs[r]; i < end; i++)
            {
                MarkEditedLocal(i);
            }
        }
    }
}

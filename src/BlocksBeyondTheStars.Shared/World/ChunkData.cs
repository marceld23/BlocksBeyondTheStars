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

        // A cleared (air) cell can carry no colour — drop any stale modifier so mining a dyed block
        // and re-placing a plain one there never inherits the old colour.
        if (block.Value == BlockId.AirValue)
        {
            _mods?.Remove(idx);
            _shapes?.Remove(idx);
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
}

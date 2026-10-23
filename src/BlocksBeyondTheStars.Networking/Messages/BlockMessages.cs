// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

public sealed class MineBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class PlaceBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>Optional label carried when placing a labelled block (a radio beacon); ignored otherwise.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Explicit shape up-face override (0..5) from the client's rotate key; -1 = none, so the server
    /// auto-orients the shape from the surface it was built against. Only used for shapeable building blocks.</summary>
    public int UpFace { get; set; } = -1;

    /// <summary>
    /// Explicit shape YAW override (0..3, quarter turns about the up-face); -1 = none, so the server derives it
    /// from where the player is looking. Asked for by a player who wanted stairs placeable at different angles:
    /// facing-derived yaw means you have to stand in a particular direction to get the turn you want, which does
    /// not work when you are building into a corner. Together with <see cref="UpFace"/> this gives the player
    /// direct control of all 24 orientations the shape descriptor already stores.
    /// </summary>
    public int Yaw { get; set; } = -1;

    /// <summary>#2267 (additive): the direction a directional Crystal Net device points to — 0..3 the quarter turns
    /// (0 = +Z, 1 = +X), 4 = up, 5 = down; -1 = none, the server derives the horizontal direction as before. The client
    /// sends it from its rotate cycle or from looking steeply up / down while placing.</summary>
    public int DeviceDir { get; set; } = -1;
}

public sealed class ChunkDataMessage
{
    public int Cx { get; set; }
    public int Cy { get; set; }
    public int Cz { get; set; }

    /// <summary>The server's id of the world this chunk belongs to (#1534, v5): the world stream travels on its own
    /// channel, so the client matches it against the WorldId of the last JoinAccepted / WorldReset. 0 = unknown.</summary>
    public int WorldId { get; set; }

    /// <summary>Dense per-cell block ids. Empty when <see cref="BlocksRle"/> carries the payload instead —
    /// the server sends whichever representation is smaller (a rare unrunnable chunk ships dense).</summary>
    public ushort[] Blocks { get; set; } = System.Array.Empty<ushort>();

    /// <summary>Run-length-encoded block payload as flat (value, runLength) pairs — terrain chunks are
    /// highly runnable, and on the browser JSON path this shrinks ~15-25 KB of numbers per chunk to
    /// usually a few hundred bytes (join time + VPS egress). Empty when <see cref="Blocks"/> is set.</summary>
    public ushort[] BlocksRle { get; set; } = System.Array.Empty<ushort>();

    /// <summary>The dense cell array, whichever representation this message carries. Null when an RLE
    /// payload is malformed — callers drop the chunk (same handling as a wrong-length dense array).</summary>
    public ushort[]? DecodeBlocks(int expectedLength)
        => BlocksRle.Length > 0 ? ChunkBlocksRle.Decode(BlocksRle, expectedLength) : Blocks;

    /// <summary>Decodes into a caller-owned array of at least <paramref name="expectedLength"/> cells (#1555:
    /// the client keeps chunk arrays in a pool). False when the payload is malformed or the dense array has
    /// the wrong length — the same cases in which <see cref="DecodeBlocks"/> yields null or a wrong length.</summary>
    public bool DecodeBlocksInto(ushort[] into, int expectedLength)
    {
        if (BlocksRle.Length > 0)
        {
            return ChunkBlocksRle.DecodeInto(BlocksRle, into, expectedLength);
        }

        if (Blocks == null || Blocks.Length != expectedLength || into.Length < expectedLength)
        {
            return false;
        }

        System.Array.Copy(Blocks, into, expectedLength);
        return true;
    }

    // Sparse per-voxel colour modifiers (dyed surface tint / glow light colour), parallel arrays
    // keyed by local cell index. Empty for the overwhelming majority of chunks (no colour edits).
    public int[] ModIndex { get; set; } = System.Array.Empty<int>();
    public int[] ModTint { get; set; } = System.Array.Empty<int>();
    public int[] ModGlow { get; set; } = System.Array.Empty<int>();

    // Sparse per-voxel SHAPE descriptors (packed shape index + orientation; see ShapeCode), parallel array
    // keyed by local cell index. Independent of the colour modifiers above — empty for the vast majority of
    // chunks. (Its own index array, since a cell can be shaped without being coloured and vice versa.)
    public int[] ShapeIndex { get; set; } = System.Array.Empty<int>();
    public int[] ShapeData { get; set; } = System.Array.Empty<int>();
}

public sealed class BlockChanged
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }

    /// <summary>Surface tint of the placed block as 0xRRGGBB (0 = none).</summary>
    public int Tint { get; set; }

    /// <summary>Light colour of the placed block as 0xRRGGBB (0 = no glow).</summary>
    public int Glow { get; set; }

    /// <summary>Packed shape descriptor of the placed block (shape index + orientation; see ShapeCode); 0 = plain cube.</summary>
    public int Shape { get; set; }

    /// <summary>The world this change belongs to (#1534, v5) — see <see cref="ChunkDataMessage.WorldId"/>.</summary>
    public int WorldId { get; set; }
}

/// <summary>A harvested surface plant has begun regrowing on its (now bare) cell: the server broadcasts this
/// once, the moment the regrow is scheduled, so the client can show the spawn source as a small sprout that
/// grows in over <see cref="Seconds"/> until the real plant returns (a normal BlockChanged). <see cref="Block"/>
/// is the flora block id coming back, so the marker can wear its tile. Purely cosmetic — gameplay is unchanged
/// whether or not a client renders it; a client may ignore it and just see the plant pop back at the end.</summary>
public sealed class FloraRegrowStarted
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>The flora block id that will return on this cell when the regrow completes.</summary>
    public ushort Block { get; set; }

    /// <summary>Seconds until the plant returns (the regrow delay), so the client can pace the grow-in.</summary>
    public float Seconds { get; set; }
}

/// <summary>Mining progress on a block (a harder block needs several drill hits): Fraction 0..1 of the
/// way to breaking. The client shows a crack overlay; the block stays until it breaks (BlockChanged).</summary>
public sealed class MiningProgress
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float Fraction { get; set; }
}

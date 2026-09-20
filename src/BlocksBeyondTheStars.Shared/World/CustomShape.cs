// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// A player-designed block FORM (#842): a micro-voxel grid inside one cell, registered once per save and
/// referenced from blocks + items by an ordinary shape index (see <see cref="ShapeCode"/>), exactly like a
/// built-in form such as <see cref="BlockShape.Slab"/>. The bitmap is the geometry sibling of a paint design:
/// one lowercase hex char per micro cell, row-major, so it validates and travels like the 32×32 paint bitmap.
///
/// Two grid sizes are allowed and the string LENGTH says which one it is (4³ = 64 chars, 8³ = 512 chars) —
/// no side-channel field, no version byte. <c>'0'</c> is empty and any other hex digit is filled; values
/// 2..f are deliberately accepted-but-equivalent so a later per-micro-cell tint can use them without a
/// format change (old clients would then render such a form in one colour rather than misreading it).
///
/// <para><b>Forms over several blocks (#1961).</b> A third payload describes a form with a footprint of up to
/// 3×3×3 blocks and at most <see cref="MaxCells"/> cells: <c>"m1:" W H L ":"</c> followed by one
/// <see cref="GridLarge"/> bitmap per cell (x fastest, then z, then y — the order of <see cref="IndexOf"/>). It
/// rides the same string the two legacy lengths use, so no message and no table column changed; a peer that
/// does not know the format finds neither legacy length, treats the form as unknown and draws a plain cube —
/// the fallback every unknown form already had. ONE registry slot holds the whole form; the placed blocks all
/// carry its shape index and say which cell they are in descriptor bits 27–30 (<see cref="ShapeCode.CellOf"/>).
/// Such a form turns (yaw) but never tips, like the bed.</para>
/// </summary>
public static class CustomShape
{
    /// <summary>The coarse grid: 4×4×4 micro cells (each 0.25 of a block — a Panel's thickness).</summary>
    public const int GridSmall = 4;

    /// <summary>The fine grid: 8×8×8 micro cells (each 0.125 of a block).</summary>
    public const int GridLarge = 8;

    /// <summary>Hex chars for a <see cref="GridSmall"/> form.</summary>
    public const int SmallChars = GridSmall * GridSmall * GridSmall;

    /// <summary>Hex chars for a <see cref="GridLarge"/> form.</summary>
    public const int LargeChars = GridLarge * GridLarge * GridLarge;

    /// <summary>
    /// Cap on the boxes a form may need after <see cref="Merge"/>. The chunk mesher feeds shaped geometry into
    /// the collider stream as well, and the synchronous MeshCollider cook is the most expensive thing a remesh
    /// does — so the SERVER refuses an over-budget form at registration and no client ever meshes one. A cube
    /// is 1 box / 6 faces; 48 boxes is the point where a wall of custom forms still stays affordable.
    /// </summary>
    public const int MaxBoxes = 48;

    /// <summary>Marker + version of the multi-cell payload.</summary>
    public const string MultiPrefix = "m1:";

    /// <summary>Largest footprint side of a multi-cell form, in blocks.</summary>
    public const int MaxFootprint = 3;

    /// <summary>Most cells a form may span — what fits the descriptor's cell field with room to spare, and as
    /// much as one hand-placed object should claim of the world.</summary>
    public const int MaxCells = 8;

    /// <summary>Length of the multi-cell header: prefix, three footprint digits, a colon.</summary>
    private const int MultiHeaderChars = 7;

    /// <summary>One axis-aligned box of a merged form, in micro cells: <c>[X0,X1)</c> × <c>[Y0,Y1)</c> ×
    /// <c>[Z0,Z1)</c> of a <paramref name="Grid"/>-sized cube. Divide by <c>Grid</c> for unit-cell coords.</summary>
    public readonly record struct Box(int X0, int Y0, int Z0, int X1, int Y1, int Z1, int Grid);

    /// <summary>The grid side length a bitmap of this length describes, or 0 when the length fits neither.</summary>
    public static int GridOf(string? voxels) => voxels?.Length switch
    {
        SmallChars => GridSmall,
        LargeChars => GridLarge,
        _ => 0,
    };

    /// <summary>
    /// Validates a form bitmap the same way <c>IsValidPaint</c> validates a design: exact length, hex charset,
    /// and — because a form is geometry, not decoration — at least one filled micro cell and at least one
    /// empty one. An all-empty grid is nothing and an all-filled grid is a cube; both already exist and must
    /// not burn a registry slot.
    /// </summary>
    public static bool IsValidVoxels(string? voxels)
    {
        if (IsMulti(voxels))
        {
            return IsValidMulti(voxels!);
        }

        if (voxels is null || GridOf(voxels) == 0)
        {
            return false;
        }

        bool anyFilled = false, anyEmpty = false;
        foreach (char c in voxels)
        {
            bool hex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!hex)
            {
                return false; // lowercase hex only — the wire form is normalized before it gets here
            }

            if (c == '0')
            {
                anyEmpty = true;
            }
            else
            {
                anyFilled = true;
            }
        }

        return anyFilled && anyEmpty;
    }

    /// <summary>Index of a micro cell in the bitmap (row-major: x fastest, then z, then y — y up).</summary>
    public static int IndexOf(int x, int y, int z, int grid) => ((y * grid) + z) * grid + x;

    /// <summary>True when the micro cell is filled. Out-of-range reads as empty, so callers can probe freely.</summary>
    public static bool IsFilled(string voxels, int x, int y, int z, int grid)
    {
        if (x < 0 || y < 0 || z < 0 || x >= grid || y >= grid || z >= grid)
        {
            return false;
        }

        return voxels[IndexOf(x, y, z, grid)] != '0';
    }

    /// <summary>
    /// Greedy-merges the filled micro cells into as few axis-aligned boxes as this simple pass can manage:
    /// from each unclaimed cell, grow along +X while the whole run is free, then extend that run along +Z,
    /// then extend the resulting slab along +Y. Deterministic and integer-only — the result is part of what
    /// the server validates and what the client meshes, so the two must agree bit for bit on every platform
    /// (no trig, no floats).
    /// </summary>
    public static List<Box> Merge(string voxels)
    {
        var boxes = new List<Box>();
        int grid = GridOf(voxels);
        if (grid == 0)
        {
            return boxes;
        }

        var claimed = new bool[grid * grid * grid];
        for (int y = 0; y < grid; y++)
        {
            for (int z = 0; z < grid; z++)
            {
                for (int x = 0; x < grid; x++)
                {
                    if (claimed[IndexOf(x, y, z, grid)] || !IsFilled(voxels, x, y, z, grid))
                    {
                        continue;
                    }

                    // 1) grow the run along +X
                    int x1 = x + 1;
                    while (x1 < grid && Free(voxels, claimed, x1, y, z, grid))
                    {
                        x1++;
                    }

                    // 2) extend the run along +Z while every cell of the next row is free
                    int z1 = z + 1;
                    while (z1 < grid && RowFree(voxels, claimed, x, x1, y, z1, grid))
                    {
                        z1++;
                    }

                    // 3) extend the slab along +Y while every cell of the next layer is free
                    int y1 = y + 1;
                    while (y1 < grid && SlabFree(voxels, claimed, x, x1, y1, z, z1, grid))
                    {
                        y1++;
                    }

                    for (int cy = y; cy < y1; cy++)
                    {
                        for (int cz = z; cz < z1; cz++)
                        {
                            for (int cx = x; cx < x1; cx++)
                            {
                                claimed[IndexOf(cx, cy, cz, grid)] = true;
                            }
                        }
                    }

                    boxes.Add(new Box(x, y, z, x1, y1, z1, grid));
                }
            }
        }

        return boxes;
    }

    /// <summary>True when the merged form stays inside the render/collider budget (<see cref="MaxBoxes"/>) —
    /// per cell for a multi-cell form, because each cell is meshed and collided as a block of its own.</summary>
    public static bool FitsBudget(string voxels)
    {
        if (!IsMulti(voxels))
        {
            return Merge(voxels).Count <= MaxBoxes;
        }

        int cells = CellCount(voxels);
        for (int cell = 0; cell < cells; cell++)
        {
            if (Merge(CellVoxels(voxels, cell)).Count > MaxBoxes)
            {
                return false;
            }
        }

        return cells > 0;
    }

    // ---------------------------------------------------------------- forms over several blocks (#1961)

    /// <summary>True when the payload claims to be a multi-cell form (it may still be invalid).</summary>
    public static bool IsMulti(string? voxels) => voxels != null && voxels.StartsWith(MultiPrefix, System.StringComparison.Ordinal);

    /// <summary>The footprint in blocks: width (X), height (Y), length (Z). A single-cell form is 1×1×1.
    /// False for a payload that is neither.</summary>
    public static bool TryFootprint(string? voxels, out int width, out int height, out int length)
    {
        width = height = length = 1;
        if (!IsMulti(voxels))
        {
            return GridOf(voxels) != 0;
        }

        string v = voxels!;
        if (v.Length < MultiHeaderChars || v[MultiHeaderChars - 1] != ':')
        {
            return false;
        }

        width = v[3] - '0';
        height = v[4] - '0';
        length = v[5] - '0';
        bool ok = width >= 1 && width <= MaxFootprint && height >= 1 && height <= MaxFootprint
            && length >= 1 && length <= MaxFootprint;
        int cells = width * height * length;
        if (!ok || cells < 2 || cells > MaxCells || v.Length != MultiHeaderChars + (cells * LargeChars))
        {
            width = height = length = 1;
            return false;
        }

        return true;
    }

    /// <summary>Number of cells (blocks) the form occupies: 1 for a single-cell form, 0 for garbage.</summary>
    public static int CellCount(string? voxels)
        => TryFootprint(voxels, out int w, out int h, out int l) ? w * h * l : 0;

    /// <summary>The bitmap of ONE cell — for a single-cell form the payload itself. Empty for a bad index.</summary>
    public static string CellVoxels(string voxels, int cell)
    {
        if (!IsMulti(voxels))
        {
            return cell == 0 ? voxels : string.Empty;
        }

        int cells = CellCount(voxels);
        return cell >= 0 && cell < cells
            ? voxels.Substring(MultiHeaderChars + (cell * LargeChars), LargeChars)
            : string.Empty;
    }

    /// <summary>Index of the cell at footprint position (cx, cy, cz) — x fastest, then z, then y, like
    /// <see cref="IndexOf"/>. Cell 0 is the ANCHOR: the block the player places.</summary>
    public static int CellIndex(int cx, int cy, int cz, int width, int length) => (((cy * length) + cz) * width) + cx;

    /// <summary>Footprint position of a cell, in the form's own (unturned) frame.</summary>
    public static (int X, int Y, int Z) CellPosition(int cell, int width, int length)
    {
        int perLayer = width * length;
        int cy = cell / perLayer;
        int rest = cell - (cy * perLayer);
        return (rest % width, cy, rest / width);
    }

    /// <summary>
    /// Where a cell lies in the WORLD relative to the anchor, after <paramref name="yaw"/> quarter turns — the
    /// same rotation the client mesher applies to the geometry inside a cell (one turn maps (x, z) to (−z, x),
    /// see <see cref="ShapeCode.YawDirection"/>). Server placement, sibling removal, the client's ghost and the
    /// menu editor's preview all go through here, so the cells a form claims and the cells it is drawn in
    /// cannot drift apart.
    /// </summary>
    public static (int X, int Y, int Z) CellOffset(string voxels, int cell, int yaw)
    {
        if (!TryFootprint(voxels, out int w, out _, out int l))
        {
            return (0, 0, 0);
        }

        var (x, y, z) = CellPosition(cell, w, l);
        for (int i = 0; i < (yaw & 3); i++)
        {
            (x, z) = (-z, x);
        }

        return (x, y, z);
    }

    /// <summary>Builds a multi-cell payload from its cells (each a <see cref="GridLarge"/> bitmap, in
    /// <see cref="CellIndex"/> order). Empty when the parts do not make a valid form.</summary>
    public static string ComposeMulti(int width, int height, int length, IReadOnlyList<string> cells)
    {
        if (cells == null || cells.Count != width * height * length)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(MultiHeaderChars + (cells.Count * LargeChars));
        sb.Append(MultiPrefix).Append((char)('0' + width)).Append((char)('0' + height)).Append((char)('0' + length)).Append(':');
        foreach (string cell in cells)
        {
            sb.Append(cell);
        }

        string voxels = sb.ToString();
        return IsValidMulti(voxels) ? voxels : string.Empty;
    }

    /// <summary>A multi-cell payload is valid when the header fits the length, every char is lowercase hex,
    /// EVERY cell holds something (an empty cell would be an invisible block that still claims its place) and
    /// the form as a whole is not just solid cubes.</summary>
    private static bool IsValidMulti(string voxels)
    {
        if (!TryFootprint(voxels, out int w, out int h, out int l))
        {
            return false;
        }

        int cells = w * h * l;
        bool anyEmpty = false;
        for (int cell = 0; cell < cells; cell++)
        {
            bool anyFilled = false;
            int start = MultiHeaderChars + (cell * LargeChars);
            for (int i = start; i < start + LargeChars; i++)
            {
                char c = voxels[i];
                if (!(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    return false;
                }

                if (c == '0')
                {
                    anyEmpty = true;
                }
                else
                {
                    anyFilled = true;
                }
            }

            if (!anyFilled)
            {
                return false;
            }
        }

        return anyEmpty;
    }

    /// <summary>The front silhouette of the WHOLE form (looking along +Z), one bool per micro column of the
    /// footprint: <paramref name="columns"/> × <paramref name="rows"/>. A single-cell form yields its grid.</summary>
    public static bool[] SilhouetteOfForm(string voxels, out int columns, out int rows)
    {
        columns = rows = 0;
        if (!IsMulti(voxels))
        {
            var single = Silhouette(voxels, out int grid);
            columns = rows = grid;
            return single;
        }

        if (!TryFootprint(voxels, out int w, out int h, out int l))
        {
            return System.Array.Empty<bool>();
        }

        columns = w * GridLarge;
        rows = h * GridLarge;
        var mask = new bool[columns * rows];
        for (int cell = 0; cell < w * h * l; cell++)
        {
            var (cx, cy, _) = CellPosition(cell, w, l);
            var part = Silhouette(CellVoxels(voxels, cell), out _);
            for (int y = 0; y < GridLarge; y++)
            {
                for (int x = 0; x < GridLarge; x++)
                {
                    if (part[(y * GridLarge) + x])
                    {
                        mask[(((cy * GridLarge) + y) * columns) + (cx * GridLarge) + x] = true;
                    }
                }
            }
        }

        return mask;
    }

    /// <summary>The 2-D front silhouette (looking along +Z) of a form — one bool per (x, y) column. Used for
    /// the inventory icon, so a self-made form reads as its own silhouette like the built-in ones do.</summary>
    public static bool[] Silhouette(string voxels, out int grid)
    {
        grid = GridOf(voxels);
        if (grid == 0)
        {
            return System.Array.Empty<bool>();
        }

        var mask = new bool[grid * grid];
        for (int y = 0; y < grid; y++)
        {
            for (int x = 0; x < grid; x++)
            {
                for (int z = 0; z < grid; z++)
                {
                    if (IsFilled(voxels, x, y, z, grid))
                    {
                        mask[y * grid + x] = true;
                        break;
                    }
                }
            }
        }

        return mask;
    }

    private static bool Free(string voxels, bool[] claimed, int x, int y, int z, int grid)
        => IsFilled(voxels, x, y, z, grid) && !claimed[IndexOf(x, y, z, grid)];

    private static bool RowFree(string voxels, bool[] claimed, int x0, int x1, int y, int z, int grid)
    {
        for (int x = x0; x < x1; x++)
        {
            if (!Free(voxels, claimed, x, y, z, grid))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SlabFree(string voxels, bool[] claimed, int x0, int x1, int y, int z0, int z1, int grid)
    {
        for (int z = z0; z < z1; z++)
        {
            if (!RowFree(voxels, claimed, x0, x1, y, z, grid))
            {
                return false;
            }
        }

        return true;
    }
}

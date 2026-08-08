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

    /// <summary>True when the merged form stays inside the render/collider budget (<see cref="MaxBoxes"/>).</summary>
    public static bool FitsBudget(string voxels) => Merge(voxels).Count <= MaxBoxes;

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

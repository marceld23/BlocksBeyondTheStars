// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3486: the working voxel grid of a landmark while it is built and ruined — the dense block array of a
/// <see cref="SettlementStructure"/> (same index, <c>(x * H + y) * L + z</c>) with its sparse modifiers, plus two bit
/// sets a settlement does not have: the cells the ruin pass must KEEP and the CAVITY cells an underground part carves.
/// <para>#3515: every other generator's structure (a settlement, a station, a wreck, a monument canvas) uses the same
/// index and the same sparse dictionaries, so the ruin pass runs over it as well: <see cref="Over"/> works right in the
/// caller's arrays, <see cref="CopyOf"/> works on a copy that <see cref="CopyTo"/> writes back.</para>
/// </summary>
internal sealed class VoxelGrid
{
    public VoxelGrid(int w, int h, int l)
        : this(w, h, l, new ushort[w * h * l], new Dictionary<int, (int Tint, int Glow)>(), new Dictionary<int, int>())
    {
    }

    private VoxelGrid(int w, int h, int l, ushort[] blocks, Dictionary<int, (int Tint, int Glow)> mods, Dictionary<int, int> shapes)
    {
        if (blocks.Length != w * h * l)
        {
            throw new ArgumentException($"a {w} x {h} x {l} grid needs {w * h * l} cells, the array has {blocks.Length}", nameof(blocks));
        }

        W = w;
        H = h;
        L = l;
        Blocks = blocks;
        Mods = mods;
        Shapes = shapes;
        Keep = new BitArray(w * h * l);
        Cavity = new BitArray(w * h * l);
    }

    /// <summary>A grid that works right in the caller's block array and modifier dictionaries (index
    /// <c>(x * H + y) * L + z</c>): whatever the ruin pass does lands there. Null dictionaries start empty (read them
    /// back from <see cref="Mods"/> / <see cref="Shapes"/>). Keep and cavity start clear.</summary>
    public static VoxelGrid Over(int w, int h, int l, ushort[] blocks, Dictionary<int, (int Tint, int Glow)>? mods = null,
        Dictionary<int, int>? shapes = null)
        => new(w, h, l, blocks, mods ?? new Dictionary<int, (int Tint, int Glow)>(), shapes ?? new Dictionary<int, int>());

    /// <summary>A grid on a copy of the caller's cells — the arrays passed in stay as they are (write the result back
    /// with <see cref="CopyTo"/>). Keep and cavity start clear.</summary>
    public static VoxelGrid CopyOf(int w, int h, int l, ushort[] blocks, IReadOnlyDictionary<int, (int Tint, int Glow)>? mods = null,
        IReadOnlyDictionary<int, int>? shapes = null)
    {
        var copyMods = new Dictionary<int, (int Tint, int Glow)>();
        foreach (var kv in mods ?? new Dictionary<int, (int Tint, int Glow)>())
        {
            copyMods[kv.Key] = kv.Value;
        }

        var copyShapes = new Dictionary<int, int>();
        foreach (var kv in shapes ?? new Dictionary<int, int>())
        {
            copyShapes[kv.Key] = kv.Value;
        }

        return new VoxelGrid(w, h, l, (ushort[])blocks.Clone(), copyMods, copyShapes);
    }

    public int W { get; }

    public int H { get; }

    public int L { get; }

    public ushort[] Blocks { get; }

    public Dictionary<int, (int Tint, int Glow)> Mods { get; }

    public Dictionary<int, int> Shapes { get; }

    /// <summary>Cells the ruin pass never takes (the author's keep brush, the floor under a marker).</summary>
    public BitArray Keep { get; }

    /// <summary>Cells that are explicitly air — the tunnels and caverns of an underground part (#3490).</summary>
    public BitArray Cavity { get; }

    public int Index(int x, int y, int z) => (((x * H) + y) * L) + z;

    public bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < W && y < H && z < L;

    public ushort Get(int x, int y, int z) => In(x, y, z) ? Blocks[Index(x, y, z)] : (ushort)0;

    public bool Solid(int x, int y, int z) => In(x, y, z) && Blocks[Index(x, y, z)] != 0;

    public int ShapeAt(int x, int y, int z) => In(x, y, z) && Shapes.TryGetValue(Index(x, y, z), out int s) ? s : 0;

    /// <summary>Writes a cell with its modifiers; block 0 clears them.</summary>
    public void Set(int x, int y, int z, ushort block, int tint = 0, int glow = 0, int shape = 0)
    {
        if (!In(x, y, z))
        {
            return;
        }

        int i = Index(x, y, z);
        Blocks[i] = block;
        if (block != 0 && (tint != 0 || glow != 0))
        {
            Mods[i] = (tint, glow);
        }
        else
        {
            Mods.Remove(i);
        }

        if (block != 0 && shape != 0)
        {
            Shapes[i] = shape;
        }
        else
        {
            Shapes.Remove(i);
        }
    }

    /// <summary>Removes the block of a cell (it becomes plain air, not a cavity).</summary>
    public void Clear(int x, int y, int z) => Set(x, y, z, 0);

    /// <summary>Marks every cell the predicate (x, y, z, block) picks as kept (#3515) — an entrance's clearance, the
    /// cells around a relic, the floor of a gate.</summary>
    public void KeepWhere(Func<int, int, int, ushort, bool> keep)
    {
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++)
            {
                for (int z = 0; z < L; z++)
                {
                    int i = Index(x, y, z);
                    if (keep(x, y, z, Blocks[i]))
                    {
                        Keep[i] = true;
                    }
                }
            }
        }
    }

    /// <summary>Marks the cells of a box (inclusive, clipped to the grid) as kept (#3515).</summary>
    public void KeepBox(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        for (int x = Math.Max(0, x0); x <= Math.Min(W - 1, x1); x++)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++)
            {
                for (int z = Math.Max(0, z0); z <= Math.Min(L - 1, z1); z++)
                {
                    Keep[Index(x, y, z)] = true;
                }
            }
        }
    }

    /// <summary>Writes the cells back into the caller's arrays (#3515): the blocks into <paramref name="blocks"/> (same
    /// size), the modifiers into the dictionaries given (cleared first; null = not wanted).</summary>
    public void CopyTo(ushort[] blocks, Dictionary<int, (int Tint, int Glow)>? mods = null, Dictionary<int, int>? shapes = null)
    {
        Array.Copy(Blocks, blocks, Blocks.Length);
        if (mods is not null && !ReferenceEquals(mods, Mods))
        {
            mods.Clear();
            foreach (var kv in Mods)
            {
                mods[kv.Key] = kv.Value;
            }
        }

        if (shapes is not null && !ReferenceEquals(shapes, Shapes))
        {
            shapes.Clear();
            foreach (var kv in Shapes)
            {
                shapes[kv.Key] = kv.Value;
            }
        }
    }
}

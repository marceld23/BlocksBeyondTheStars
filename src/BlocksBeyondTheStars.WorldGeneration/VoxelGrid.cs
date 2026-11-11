// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3486: the working voxel grid of a landmark while it is built and ruined — the dense block array of a
/// <see cref="SettlementStructure"/> (same index, <c>(x * H + y) * L + z</c>) with its sparse modifiers, plus two bit
/// sets a settlement does not have: the cells the ruin pass must KEEP and the CAVITY cells an underground part carves.
/// </summary>
internal sealed class VoxelGrid
{
    public VoxelGrid(int w, int h, int l)
    {
        W = w;
        H = h;
        L = l;
        Blocks = new ushort[w * h * l];
        Keep = new BitArray(w * h * l);
        Cavity = new BitArray(w * h * l);
    }

    public int W { get; }

    public int H { get; }

    public int L { get; }

    public ushort[] Blocks { get; }

    public Dictionary<int, (int Tint, int Glow)> Mods { get; } = new();

    public Dictionary<int, int> Shapes { get; } = new();

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
}

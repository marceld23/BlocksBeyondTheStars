// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3626 / #3627: the torches and lanterns of a landmark carry the wall / floor / ceiling mount of #2683 (the landmark
/// toolkit stamps it, tools/landmarks/lightmount.py), so each one hangs on exactly one block. The ruin pass never erodes a
/// shaped cell, and its settle keeps a cell while ANY neighbour stands — a wall torch whose wall fell would stay, leaning
/// on its plate out of thin air. After the pass, such a light falls with the block that held it: the reading of the
/// server's support rule (#2532), which pops a mounted light when its own block goes.
/// <para>Only mounted lights are touched. A template without them (every landmark before the mount pass, the framework's
/// samples, every generator's lights) is ruined byte for byte as before.</para>
/// </summary>
internal static class LandmarkLights
{
    /// <summary>Removes every mounted torch or lantern of the ruined grid whose holding cell no longer holds a light: not a
    /// block that <see cref="PropShapes.HoldsLight"/>, and not the planet's rock — a cell below
    /// <paramref name="groundRow"/> the template leaves empty (no cavity), which the stamp keeps as it is. Returns how many
    /// fell.</summary>
    public static int DropUnheld(VoxelGrid grid, int groundRow, GameContent content)
    {
        int layer = grid.H * grid.L, fell = 0;
        var lantern = new Dictionary<ushort, bool?>(); // per block id: null = no light mount, else whether it may hang
        foreach (int i in grid.Shapes.Keys.OrderBy(i => i).ToList())
        {
            ushort b = grid.Blocks[i];
            if (b == 0)
            {
                continue;
            }

            if (!lantern.TryGetValue(b, out bool? isLantern))
            {
                string? key = content.BlockById(new BlockId(b))?.Key;
                isLantern = PropShapes.IsLightMountKey(key) ? PropShapes.IsLanternKey(key) : null;
                lantern[b] = isLantern;
            }

            int mount = isLantern is { } l ? PropShapes.LightMountOf(grid.Shapes[i], l) : -1;
            if (mount < 0)
            {
                continue;
            }

            int x = i / layer, y = (i % layer) / grid.L, z = i % grid.L;
            var (dx, dy, dz) = ShapeCode.FaceDirection(mount);
            if (!Holds(grid, x - dx, y - dy, z - dz, groundRow, content))
            {
                grid.Clear(x, y, z);
                fell++;
            }
        }

        return fell;
    }

    private static bool Holds(VoxelGrid grid, int x, int y, int z, int groundRow, GameContent content)
    {
        ushort b = grid.Get(x, y, z);
        if (b != 0)
        {
            return PropShapes.HoldsLight(content.BlockById(new BlockId(b)));
        }

        return y < groundRow && !(grid.In(x, y, z) && grid.Cavity[grid.Index(x, y, z)]);
    }
}

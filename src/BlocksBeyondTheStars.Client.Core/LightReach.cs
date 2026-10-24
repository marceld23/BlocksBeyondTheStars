// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2951: which chunk meshes a change of the block light can touch. The mesher floods a lamp's light up to
    /// reach − 1 face steps and bakes it into the faces looking into a lit cell, sampling the cell in front and its
    /// corner neighbours — one cell further out. Re-meshing only the edited chunk (plus a face neighbour when the cell
    /// sat on its edge) left the old light standing in the chunk next door: a lamp six cells from a border lit its own
    /// chunk, and the pool stopped dead along the border plane.
    /// </summary>
    public static class LightReach
    {
        /// <summary>
        /// Every chunk holding a cell within <paramref name="stepsLeft"/> + 1 blocks (box) of (wx, wy, wz): the cells
        /// whose light a change there can alter (<see cref="ClientWorld.LightReachThrough"/> steps around it) plus the
        /// one-cell sampling margin. Nothing when <paramref name="stepsLeft"/> is negative (no light reaches the cell).
        /// Coordinates come back canonical for a world of <paramref name="circumference"/>; duplicates (tiny worlds
        /// wrap onto themselves) are the caller's to fold.
        /// </summary>
        public static void ChunksTouched(int wx, int wy, int wz, int stepsLeft, int circumference, ICollection<ChunkCoord> into)
        {
            if (stepsLeft < 0)
            {
                return;
            }

            int half = stepsLeft + 1;
            int x0 = WorldConstants.WorldToChunk(wx - half), x1 = WorldConstants.WorldToChunk(wx + half);
            int y0 = WorldConstants.WorldToChunk(wy - half), y1 = WorldConstants.WorldToChunk(wy + half);
            int z0 = WorldConstants.WorldToChunk(wz - half), z1 = WorldConstants.WorldToChunk(wz + half);
            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        into.Add(WorldConstants.CanonicalChunk(new ChunkCoord(cx, cy, cz), circumference));
                    }
                }
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The unit cube a creature part is drawn with (#2831): the same shape as Unity's cube primitive, but its UVs repeat
    /// the 64-texel hide once per <see cref="TileMetres"/> of the part's real size on every face, so a toe and a 50-block
    /// colossus show the same texel density (the primitive stretched one tile over every face, whatever its size).
    /// Repeats are whole numbers, clamped 1..<see cref="MaxRepeats"/>, so the builder can share one mesh per repeat triple.
    /// <para>The orientation is fixed, so the coat masks know where they land: on the four faces around the part's Z axis
    /// (±X, ±Y) u runs along Z — a band across u rings the part — and v runs up (±X) or across (±Y); on the end caps (±Z)
    /// u runs along X and v up. A PINNED mesh maps v once over the part's height on the sides and end caps and keeps the
    /// top and bottom faces to the tile's top and bottom rows, so a band drawn at a height (the eye mask) sits there on
    /// the sides and never crosses the top.</para>
    /// <para>A coarse hide repeats every <see cref="CreatureHides.CoarseTileMetres"/> instead (#2860), and every animal
    /// wears one of <see cref="CreatureCoat.PlacementVariants"/> PLACEMENTS (#2865): its UVs shift by a fraction of a tile
    /// and may mirror in u, so the same coat lands differently on each animal of a herd while the texture and the material
    /// stay shared. A pinned mesh never shifts in v (the eye band and the countershading stay at their height).</para>
    /// </summary>
    public static class TiledCube
    {
        /// <summary>World units per tile repeat (blocks are 1 unit).</summary>
        public const float TileMetres = 0.6f;

        /// <summary>The most repeats along one face edge.</summary>
        public const int MaxRepeats = 8;

        /// <summary>The v range of a pinned mesh's top face (its bottom face mirrors it at the tile's bottom).</summary>
        public const float PinnedCapRows = 0.2f;

        /// <summary>Tile repeats along an edge of <paramref name="size"/> world units, one per <paramref name="tileMetres"/>
        /// (default <see cref="TileMetres"/>).</summary>
        public static int Repeats(float size, float tileMetres = TileMetres)
        {
            if (!(size > 0f) || float.IsInfinity(size) || !(tileMetres > 0f))
            {
                return 1;
            }

            int r = (int)Math.Round(size / tileMetres, MidpointRounding.AwayFromZero);
            return r < 1 ? 1 : r > MaxRepeats ? MaxRepeats : r;
        }

        /// <summary>A cache key for one mesh variant (repeats, pinned, placement).</summary>
        public static int Key(int rx, int ry, int rz, bool pinV, int placement = 0)
            => Clamp(rx) | (Clamp(ry) << 4) | (Clamp(rz) << 8) | (pinV ? 1 << 12 : 0)
               | ((placement & (CreatureCoat.PlacementVariants - 1)) << 13);

        // The u and v shifts of the placements (fractions of a tile; 4 shifts × mirrored or not = 8 placements).
        private static readonly float[] ShiftU = { 0f, 0.37f, 0.61f, 0.83f };
        private static readonly float[] ShiftV = { 0f, 0.44f, 0.19f, 0.71f };

        /// <summary>The u and v shift and the u mirror of a placement.</summary>
        public static void Placement(int placement, out float du, out float dv, out bool mirror)
        {
            int p = placement & (CreatureCoat.PlacementVariants - 1);
            du = ShiftU[p & 3];
            dv = ShiftV[p & 3];
            mirror = (p & 4) != 0;
        }

        private static int Clamp(int r) => r < 1 ? 1 : r > MaxRepeats ? MaxRepeats : r;

        /// <summary>Plain mesh arrays (Unity-free): 24 vertices (4 per face), 12 triangles.</summary>
        public sealed class Data
        {
            public Data(float[] positions, float[] normals, float[] uvs, int[] triangles)
            {
                Positions = positions;
                Normals = normals;
                Uvs = uvs;
                Triangles = triangles;
            }

            /// <summary>x, y, z per vertex, the cube spanning −0.5..0.5.</summary>
            public float[] Positions { get; }

            /// <summary>x, y, z per vertex (the face's outward normal).</summary>
            public float[] Normals { get; }

            /// <summary>u, v per vertex.</summary>
            public float[] Uvs { get; }

            /// <summary>Vertex indices, three per triangle, front faces wound the way Unity culls (outward normal =
            /// (b − a) × (c − a)).</summary>
            public int[] Triangles { get; }
        }

        // Per face: outward normal, the u axis, the v axis; then which part axis (0 = X, 1 = Y, 2 = Z) the u and v
        // repeats follow, and whether the face is a side (pinned v 0..1), the top (pinned to the top rows) or the bottom.
        private static readonly int[][] Faces =
        {
            //          n           u           v       ru rv  kind: 0 side, 1 top, 2 bottom
            new[] { 1, 0, 0,    0, 0, 1,    0, 1, 0,    2, 1, 0 },   // +X
            new[] { -1, 0, 0,   0, 0, -1,   0, 1, 0,    2, 1, 0 },   // −X
            new[] { 0, 1, 0,    0, 0, 1,    -1, 0, 0,   2, 0, 1 },   // +Y (top)
            new[] { 0, -1, 0,   0, 0, 1,    1, 0, 0,    2, 0, 2 },   // −Y (bottom)
            new[] { 0, 0, 1,    -1, 0, 0,   0, 1, 0,    0, 1, 0 },   // +Z (front cap)
            new[] { 0, 0, -1,   1, 0, 0,    0, 1, 0,    0, 1, 0 },   // −Z (back cap)
        };

        /// <summary>Builds the cube with <paramref name="rx"/>/<paramref name="ry"/>/<paramref name="rz"/> tile repeats along
        /// the part's X/Y/Z extent (each clamped 1..<see cref="MaxRepeats"/>); <paramref name="pinV"/> maps v once over the
        /// part's height instead; <paramref name="placement"/> shifts (and may mirror) the coat (#2865).</summary>
        public static Data Build(int rx, int ry, int rz, bool pinV, int placement = 0)
        {
            Placement(placement, out float du, out float dv, out bool mirror);
            if (pinV)
            {
                dv = 0f;
            }

            int[] rep = { Clamp(rx), Clamp(ry), Clamp(rz) };
            var pos = new float[24 * 3];
            var nrm = new float[24 * 3];
            var uv = new float[24 * 2];
            var tri = new int[36];
            for (int f = 0; f < Faces.Length; f++)
            {
                int[] d = Faces[f];
                float ru = rep[d[9]];
                float v0 = 0f, v1 = rep[d[10]];
                if (pinV)
                {
                    switch (d[11])
                    {
                        case 1: v0 = 1f - PinnedCapRows; v1 = 1f; break;
                        case 2: v0 = 0f; v1 = PinnedCapRows; break;
                        default: v0 = 0f; v1 = 1f; break;
                    }
                }

                for (int corner = 0; corner < 4; corner++)
                {
                    int a = corner & 1, b = corner >> 1; // (0,0) (1,0) (0,1) (1,1)
                    int vi = f * 4 + corner;
                    for (int k = 0; k < 3; k++)
                    {
                        pos[vi * 3 + k] = d[k] * 0.5f + d[3 + k] * (a - 0.5f) + d[6 + k] * (b - 0.5f);
                        nrm[vi * 3 + k] = d[k];
                    }

                    uv[vi * 2] = (mirror ? (1 - a) * ru : a * ru) + du;
                    uv[vi * 2 + 1] = v0 + b * (v1 - v0) + dv;
                }

                // Wind so (b − a) × (c − a) points along the normal: with u × v = n the order 00-10-11 does it,
                // with u × v = −n the order 00-01-11.
                int cx = d[4] * d[8] - d[5] * d[7];
                int cy = d[5] * d[6] - d[3] * d[8];
                int cz = d[3] * d[7] - d[4] * d[6];
                bool uvIsN = cx * d[0] + cy * d[1] + cz * d[2] > 0;
                int v00 = f * 4, v10 = f * 4 + 1, v01 = f * 4 + 2, v11 = f * 4 + 3;
                int t = f * 6;
                if (uvIsN)
                {
                    tri[t] = v00; tri[t + 1] = v10; tri[t + 2] = v11;
                    tri[t + 3] = v00; tri[t + 4] = v11; tri[t + 5] = v01;
                }
                else
                {
                    tri[t] = v00; tri[t + 1] = v01; tri[t + 2] = v11;
                    tri[t + 3] = v00; tri[t + 4] = v11; tri[t + 5] = v10;
                }
            }

            return new Data(pos, nrm, uv, tri);
        }
    }
}

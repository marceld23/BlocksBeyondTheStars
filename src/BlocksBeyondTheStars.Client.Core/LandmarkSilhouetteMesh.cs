// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The mesh of a landmark's far silhouette (#3497) — Unity-free so the headless tests can build it. The server
    /// sends a coarse occupancy model (<see cref="NetLandmark"/>: one cell per <c>Step</c>³ blocks, a palette index per
    /// cell); this turns it into quads: one per face of a filled cell that looks at an empty one (or out of the model).
    /// Faces between two filled cells are never emitted, so a solid pyramid costs only its skin.
    /// <para>Positions are in blocks relative to the model's (0, 0, 0) corner. Every vertex carries the 1-based palette
    /// index of its cell; the view turns it into a colour from the block atlas, like the far terrain does.</para>
    /// </summary>
    public sealed class LandmarkSilhouetteMesh
    {
        private LandmarkSilhouetteMesh(float[] positions, float[] normals, byte[] palette, int[] triangles)
        {
            Positions = positions;
            Normals = normals;
            Palette = palette;
            Triangles = triangles;
        }

        /// <summary>x, y, z per vertex, in blocks from the model's corner.</summary>
        public float[] Positions { get; }

        /// <summary>x, y, z per vertex.</summary>
        public float[] Normals { get; }

        /// <summary>The 1-based palette index of each vertex's cell.</summary>
        public byte[] Palette { get; }

        public int[] Triangles { get; }

        public int VertexCount => Palette.Length;

        public int QuadCount => Palette.Length / 4;

        // The six faces: the normal, then the four corners (cell-local 0/1) in an order that winds outward.
        private static readonly int[][] Faces =
        {
            new[] { 1, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1 },
            new[] { -1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0 },
            new[] { 0, 1, 0, 0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0 },
            new[] { 0, -1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 1 },
            new[] { 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1 },
            new[] { 0, 0, -1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0 },
        };

        /// <summary>Builds the mesh of <paramref name="model"/>; an empty or malformed model gives an empty mesh.</summary>
        public static LandmarkSilhouetteMesh Build(NetLandmark model)
        {
            int w = model.Width, h = model.Height, l = model.Length, step = Math.Max(1, model.Step);
            var cells = model.Cells ?? Array.Empty<byte>();
            var positions = new List<float>();
            var normals = new List<float>();
            var palette = new List<byte>();
            var triangles = new List<int>();
            if (w <= 0 || h <= 0 || l <= 0 || cells.Length != w * h * l)
            {
                return new LandmarkSilhouetteMesh(positions.ToArray(), normals.ToArray(), palette.ToArray(), triangles.ToArray());
            }

            byte At(int x, int y, int z)
                => x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l ? (byte)0 : cells[(((x * h) + y) * l) + z];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int z = 0; z < l; z++)
                    {
                        byte cell = At(x, y, z);
                        if (cell == 0)
                        {
                            continue;
                        }

                        foreach (var f in Faces)
                        {
                            if (At(x + f[0], y + f[1], z + f[2]) != 0)
                            {
                                continue; // the neighbour hides this face
                            }

                            int first = palette.Count;
                            for (int c = 0; c < 4; c++)
                            {
                                positions.Add((x + f[3 + (c * 3)]) * step);
                                positions.Add((y + f[4 + (c * 3)]) * step);
                                positions.Add((z + f[5 + (c * 3)]) * step);
                                normals.Add(f[0]);
                                normals.Add(f[1]);
                                normals.Add(f[2]);
                                palette.Add(cell);
                            }

                            triangles.Add(first);
                            triangles.Add(first + 1);
                            triangles.Add(first + 2);
                            triangles.Add(first);
                            triangles.Add(first + 2);
                            triangles.Add(first + 3);
                        }
                    }
                }
            }

            return new LandmarkSilhouetteMesh(positions.ToArray(), normals.ToArray(), palette.ToArray(), triangles.ToArray());
        }

        /// <summary>
        /// How near (blocks, 3-D from the player) the silhouette is cut away because the real blocks are drawn there: one
        /// chunk inside the nearer of the client's chunk draw distance and the sphere the server lifts a landmark's
        /// columns to (the sent-set prune radius minus one chunk, #3489). Never negative.
        /// </summary>
        public static float NearCut(float chunkDrawDistanceBlocks, int viewDistanceChunks, int chunkSize = 16)
        {
            float lift = (Math.Min(viewDistanceChunks + 4, 20) - 1) * chunkSize;
            return Math.Max(0f, Math.Min(chunkDrawDistanceBlocks, lift) - chunkSize);
        }
    }
}

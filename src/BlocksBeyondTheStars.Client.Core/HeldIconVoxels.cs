// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The extruded icon an item without a 3D model is held as (#2534, "for all of them something is missing in the
    /// hand"): the item's <c>item_*.png</c> icon is cut down to a small pixel grid and every opaque pixel becomes a thin
    /// voxel, Minecraft-style — front and back faces carry the pixel's colour, and side walls close the silhouette.
    /// Unity-free so the headless suite pins the maths; <c>HeldMiniatures</c> feeds it the read-back icon pixels and
    /// uploads the result as a vertex-coloured mesh. The grid lies in the XY plane (x right, y up, row 0 at the bottom
    /// like Unity's <c>GetPixels32</c>), centred on the origin, with the thickness along Z.
    /// </summary>
    public static class HeldIconVoxels
    {
        /// <summary>Pixels per side of the held grid. 24 keeps the generated icons readable at hand size while a
        /// full grid stays a few thousand vertices (the 128² icons map 5–6 source pixels onto one voxel).</summary>
        public const int DefaultResolution = 24;

        /// <summary>A source pixel counts toward the crop box from this alpha up (faint glows and shadows don't).</summary>
        public const byte CropAlpha = 128;

        /// <summary>Baked face shades (the vertex-colour shader has no light direction of its own): the front reads
        /// brightest, the top catches the light, the underside sits in shadow.</summary>
        public const float FrontShade = 1f, BackShade = 0.82f, TopShade = 0.92f, BottomShade = 0.6f, SideShade = 0.74f;

        /// <summary>An sRGB colour with alpha, 0..255 per channel.</summary>
        public readonly struct Rgba : IEquatable<Rgba>
        {
            public Rgba(byte r, byte g, byte b, byte a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }

            public byte R { get; }

            public byte G { get; }

            public byte B { get; }

            public byte A { get; }

            public bool Equals(Rgba other) => R == other.R && G == other.G && B == other.B && A == other.A;

            public override bool Equals(object? obj) => obj is Rgba other && Equals(other);

            public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;

            /// <summary>This colour with its RGB scaled by <paramref name="shade"/> (alpha kept).</summary>
            public Rgba Shaded(float shade)
                => new Rgba(Scale(R, shade), Scale(G, shade), Scale(B, shade), A);

            private static byte Scale(byte v, float s) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * s)));
        }

        /// <summary>A square grid of cells, row 0 at the bottom; a cell is opaque when its alpha is 255.</summary>
        public sealed class Grid
        {
            public Grid(int size)
            {
                Size = size;
                Cells = new Rgba[size * size];
            }

            public int Size { get; }

            public Rgba[] Cells { get; }

            public Rgba this[int x, int y] => Cells[(y * Size) + x];

            public bool IsOpaque(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && Cells[(y * Size) + x].A == 255;

            public int OpaqueCount
            {
                get
                {
                    int n = 0;
                    foreach (var c in Cells)
                    {
                        if (c.A == 255)
                        {
                            n++;
                        }
                    }

                    return n;
                }
            }
        }

        /// <summary>
        /// Cuts an icon down to a held grid. <paramref name="rgba"/> holds 4 bytes per pixel (R, G, B, A), rows bottom-up.
        /// The icon is first cropped to the square around its opaque pixels (so padding in the art never shrinks the held
        /// item), then every target cell averages the source pixels it covers — colour weighted by alpha, so the
        /// transparent surround never darkens an edge — and is opaque when at least half of it was covered. A crop smaller
        /// than <paramref name="size"/> keeps its own resolution (no empty cells between stretched pixels). Null when the
        /// icon has no opaque pixel or the buffer does not match the size.
        /// </summary>
        public static Grid? Downsample(byte[] rgba, int width, int height, int size = DefaultResolution)
        {
            if (rgba == null || width <= 0 || height <= 0 || size <= 0 || rgba.Length < width * height * 4)
            {
                return null;
            }

            // The crop box: the bounding box of the clearly opaque pixels, grown to a square around its centre.
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (rgba[(((y * width) + x) * 4) + 3] >= CropAlpha)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < 0)
            {
                return null;
            }

            int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
            int x0 = minX - ((side - (maxX - minX + 1)) / 2);
            int y0 = minY - ((side - (maxY - minY + 1)) / 2);
            int n = Math.Min(size, side);

            var sumA = new long[n * n];
            var sumR = new long[n * n];
            var sumG = new long[n * n];
            var sumB = new long[n * n];
            var count = new int[n * n];
            for (int sy = 0; sy < side; sy++)
            {
                int ty = (int)((long)sy * n / side);
                int py = y0 + sy;
                for (int sx = 0; sx < side; sx++)
                {
                    int cell = (ty * n) + (int)((long)sx * n / side);
                    count[cell]++;
                    int px = x0 + sx;
                    if (px < 0 || py < 0 || px >= width || py >= height)
                    {
                        continue; // outside the icon: transparent
                    }

                    int i = ((py * width) + px) * 4;
                    int a = rgba[i + 3];
                    sumA[cell] += a;
                    sumR[cell] += rgba[i] * a;
                    sumG[cell] += rgba[i + 1] * a;
                    sumB[cell] += rgba[i + 2] * a;
                }
            }

            var grid = new Grid(n);
            for (int c = 0; c < n * n; c++)
            {
                if (count[c] == 0 || sumA[c] * 2 < 255L * count[c])
                {
                    continue; // less than half covered: a hole
                }

                grid.Cells[c] = new Rgba(
                    (byte)(sumR[c] / sumA[c]), (byte)(sumG[c] / sumA[c]), (byte)(sumB[c] / sumA[c]), 255);
            }

            return grid.OpaqueCount > 0 ? grid : null;
        }

        /// <summary>A built mesh as plain lists (Unity uploads it as-is; triangles wind clockwise seen from the
        /// face's outside, Unity's front-face rule).</summary>
        public sealed class MeshData
        {
            public List<Vector3> Positions { get; } = new List<Vector3>();

            public List<Vector3> Normals { get; } = new List<Vector3>();

            public List<Rgba> Colors { get; } = new List<Rgba>();

            public List<int> Triangles { get; } = new List<int>();

            public int QuadCount => Positions.Count / 4;
        }

        /// <summary>
        /// Extrudes <paramref name="grid"/> into voxels of <paramref name="cell"/> × <paramref name="cell"/> ×
        /// <paramref name="thickness"/>, centred on the origin. The front (−Z, toward a viewer looking along +Z) and back
        /// faces merge runs of equal colour along a row; a side wall is emitted only where an opaque pixel borders a
        /// transparent one (or the grid edge), so the inside of the slab costs nothing.
        /// </summary>
        public static MeshData BuildMesh(Grid grid, float cell, float thickness)
        {
            var data = new MeshData();
            if (grid == null || grid.Size == 0 || cell <= 0f || thickness <= 0f)
            {
                return data;
            }

            int n = grid.Size;
            float half = n * cell * 0.5f;
            float zf = -thickness * 0.5f, zb = thickness * 0.5f;

            for (int y = 0; y < n; y++)
            {
                float y0 = (y * cell) - half, y1 = y0 + cell;
                int x = 0;
                while (x < n)
                {
                    if (!grid.IsOpaque(x, y))
                    {
                        x++;
                        continue;
                    }

                    // One run of equal colour → one front and one back quad.
                    var color = grid[x, y];
                    int end = x + 1;
                    while (end < n && grid.IsOpaque(end, y) && grid[end, y].Equals(color))
                    {
                        end++;
                    }

                    float x0 = (x * cell) - half, x1 = (end * cell) - half;
                    AddQuad(data, new Vector3(x0, y0, zf), new Vector3(x0, y1, zf), new Vector3(x1, y1, zf), new Vector3(x1, y0, zf),
                        -Vector3.UnitZ, color.Shaded(FrontShade));
                    AddQuad(data, new Vector3(x0, y0, zb), new Vector3(x1, y0, zb), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb),
                        Vector3.UnitZ, color.Shaded(BackShade));
                    x = end;
                }
            }

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (!grid.IsOpaque(x, y))
                    {
                        continue;
                    }

                    var c = grid[x, y];
                    float x0 = (x * cell) - half, x1 = x0 + cell, y0 = (y * cell) - half, y1 = y0 + cell;
                    if (!grid.IsOpaque(x, y + 1))
                    {
                        AddQuad(data, new Vector3(x0, y1, zf), new Vector3(x0, y1, zb), new Vector3(x1, y1, zb), new Vector3(x1, y1, zf),
                            Vector3.UnitY, c.Shaded(TopShade));
                    }

                    if (!grid.IsOpaque(x, y - 1))
                    {
                        AddQuad(data, new Vector3(x0, y0, zf), new Vector3(x1, y0, zf), new Vector3(x1, y0, zb), new Vector3(x0, y0, zb),
                            -Vector3.UnitY, c.Shaded(BottomShade));
                    }

                    if (!grid.IsOpaque(x - 1, y))
                    {
                        AddQuad(data, new Vector3(x0, y0, zf), new Vector3(x0, y0, zb), new Vector3(x0, y1, zb), new Vector3(x0, y1, zf),
                            -Vector3.UnitX, c.Shaded(SideShade));
                    }

                    if (!grid.IsOpaque(x + 1, y))
                    {
                        AddQuad(data, new Vector3(x1, y0, zf), new Vector3(x1, y1, zf), new Vector3(x1, y1, zb), new Vector3(x1, y0, zb),
                            Vector3.UnitX, c.Shaded(SideShade));
                    }
                }
            }

            return data;
        }

        /// <summary>Adds one quad (corners in cyclic order, either direction) facing <paramref name="normal"/>: the two
        /// triangles are wound so their geometric normal agrees with it.</summary>
        private static void AddQuad(MeshData data, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Rgba color)
        {
            int i = data.Positions.Count;
            data.Positions.Add(a);
            data.Positions.Add(b);
            data.Positions.Add(c);
            data.Positions.Add(d);
            for (int k = 0; k < 4; k++)
            {
                data.Normals.Add(normal);
                data.Colors.Add(color);
            }

            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f)
            {
                data.Triangles.Add(i);
                data.Triangles.Add(i + 1);
                data.Triangles.Add(i + 2);
                data.Triangles.Add(i);
                data.Triangles.Add(i + 2);
                data.Triangles.Add(i + 3);
            }
            else
            {
                data.Triangles.Add(i);
                data.Triangles.Add(i + 2);
                data.Triangles.Add(i + 1);
                data.Triangles.Add(i);
                data.Triangles.Add(i + 3);
                data.Triangles.Add(i + 2);
            }
        }
    }
}

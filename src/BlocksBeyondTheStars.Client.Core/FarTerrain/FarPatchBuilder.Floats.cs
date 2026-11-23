// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>
    /// #3958/#3959: what floats over the far terrain. Every sampled point the patch owns (the half-open grid
    /// [0, n) × [0, n), so a point shared with the neighbour patch is drawn once) turns each of its floating shapes into
    /// geometry appended after the height field: a solid band becomes a block slab one far cell wide — top face, bottom
    /// face, and side faces only over the height spans the neighbouring point does not cover — and a waterfall a slim
    /// curtain of four water faces. The blocky slabs match the game's look and leave the sky open under every island.
    /// </summary>
    public sealed partial class FarPatchBuilder
    {
        /// <summary>A waterfall curtain is this share of a far cell wide: a falling strand, not a wall of water.</summary>
        private const float WaterfallWidthShare = 0.5f;

        /// <summary>The growing vertex and index lists of the floating shapes (vertex indices already absolute).</summary>
        private sealed class FloatMesh
        {
            public readonly List<float> Positions = new List<float>();
            public readonly List<float> Normals = new List<float>();
            public readonly List<uint> Colors = new List<uint>();
            public readonly List<int> Indices = new List<int>();
            public readonly int Base;
            public float MinY = float.MaxValue;
            public float MaxY = float.MinValue;

            public FloatMesh(int baseVertex) => Base = baseVertex;

            public void Quad((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c,
                (float X, float Y, float Z) d, (float X, float Y, float Z) normal, uint color)
            {
                int v = Base + Colors.Count;
                Vertex(a, normal, color);
                Vertex(b, normal, color);
                Vertex(c, normal, color);
                Vertex(d, normal, color);
                Indices.Add(v); Indices.Add(v + 1); Indices.Add(v + 2);
                Indices.Add(v); Indices.Add(v + 2); Indices.Add(v + 3);
            }

            private void Vertex((float X, float Y, float Z) p, (float X, float Y, float Z) n, uint color)
            {
                Positions.Add(p.X); Positions.Add(p.Y); Positions.Add(p.Z);
                Normals.Add(n.X); Normals.Add(n.Y); Normals.Add(n.Z);
                Colors.Add(color);
                MinY = Math.Min(MinY, p.Y);
                MaxY = Math.Max(MaxY, p.Y);
            }
        }

        /// <summary>Appends the slabs and waterfalls of every owned point to <paramref name="geo"/>.</summary>
        private void AppendFloats(FarPatchGeometry geo, Func<FarSample, bool, FarEditTop, uint> colorOf)
        {
            FloatMesh? mesh = null;
            for (int j = 0; j < _n; j++)
                for (int i = 0; i < _n; i++)
                {
                    int s = (j + 1) * _side + (i + 1);
                    for (int k = 0; k < _floatCount[s]; k++)
                    {
                        mesh ??= new FloatMesh(geo.VertexCount);
                        var f = EffectiveFloat(s, k, out bool built);
                        if (f.Kind == FarFloatKind.Waterfall)
                        {
                            AddWaterfall(mesh, f, i * _cell, j * _cell, colorOf);
                        }
                        else
                        {
                            uint top = built ? colorOf(f.Surface, true, _edits[s]) : colorOf(f.Surface, false, default);
                            AddSlab(mesh, s, f, i * _cell, j * _cell, top, colorOf(new FarSample(f.Top, FarSurface.Ground, f.SideBlock), false, default));
                        }
                    }
                }

            if (mesh != null)
            {
                Merge(geo, mesh);
            }
        }

        /// <summary>A point's k-th float, raised to the build standing on it when there is one (#3960).</summary>
        private FarFloat EffectiveFloat(int s, int k, out bool built)
        {
            var f = _floats[s * FarTerrainSource.MaxFloats + k];
            built = _builtFloat[s] == k && _edits[s].Top > f.Top;
            return built ? f.WithTop(_edits[s].Top, f.Surface) : f;
        }

        /// <summary>One block slab centred on a point: top, bottom, and the exposed parts of its four sides.</summary>
        private void AddSlab(FloatMesh mesh, int s, in FarFloat f, float cx, float cz, uint topColor, uint sideColor)
        {
            float h = _cell * 0.5f;
            float offset = FarTerrainLayout.HeightOffset(Key.Level);
            float y0 = f.Bottom + offset, y1 = f.Top + offset;
            float x0 = cx - h, x1 = cx + h, z0 = cz - h, z1 = cz + h;
            mesh.Quad((x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0), (0f, 1f, 0f), topColor);
            mesh.Quad((x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1), (0f, -1f, 0f), sideColor);

            Span<int> lo = stackalloc int[FarTerrainSource.MaxFloats + 1];
            Span<int> hi = stackalloc int[FarTerrainSource.MaxFloats + 1];
            for (int side = 0; side < 4; side++)
            {
                int neighbour = side switch { 0 => s - 1, 1 => s + 1, 2 => s - _side, _ => s + _side };
                int segments = ExposedSpans(f, neighbour, lo, hi);
                for (int g = 0; g < segments; g++)
                {
                    float a = lo[g] + offset, b = hi[g] + offset;
                    switch (side)
                    {
                        case 0: mesh.Quad((x0, a, z0), (x0, a, z1), (x0, b, z1), (x0, b, z0), (-1f, 0f, 0f), sideColor); break;
                        case 1: mesh.Quad((x1, a, z0), (x1, b, z0), (x1, b, z1), (x1, a, z1), (1f, 0f, 0f), sideColor); break;
                        case 2: mesh.Quad((x0, a, z0), (x0, b, z0), (x1, b, z0), (x1, a, z0), (0f, 0f, -1f), sideColor); break;
                        default: mesh.Quad((x0, a, z1), (x1, a, z1), (x1, b, z1), (x0, b, z1), (0f, 0f, 1f), sideColor); break;
                    }
                }
            }
        }

        /// <summary>The height spans of <paramref name="f"/> that the neighbouring point's own solid slabs do not cover —
        /// the parts of this side that face open air. Returns the span count written into lo/hi.</summary>
        private int ExposedSpans(in FarFloat f, int neighbour, Span<int> lo, Span<int> hi)
        {
            int count = 1;
            lo[0] = f.Bottom;
            hi[0] = f.Top;
            for (int k = 0; k < _floatCount[neighbour] && count > 0; k++)
            {
                var other = EffectiveFloat(neighbour, k, out _);
                if (other.Kind != FarFloatKind.Solid)
                {
                    continue;
                }

                count = Subtract(lo, hi, count, other.Bottom, other.Top);
            }

            return count;
        }

        /// <summary>Removes [cutLo, cutHi) from every span; a span cut in its middle splits in two.</summary>
        private static int Subtract(Span<int> lo, Span<int> hi, int count, int cutLo, int cutHi)
        {
            Span<int> nlo = stackalloc int[lo.Length];
            Span<int> nhi = stackalloc int[hi.Length];
            int n = 0;
            for (int g = 0; g < count; g++)
            {
                if (cutHi <= lo[g] || cutLo >= hi[g])
                {
                    nlo[n] = lo[g]; nhi[n++] = hi[g]; // untouched
                    continue;
                }

                if (cutLo > lo[g] && n < nlo.Length)
                {
                    nlo[n] = lo[g]; nhi[n++] = cutLo;
                }

                if (cutHi < hi[g] && n < nlo.Length)
                {
                    nlo[n] = cutHi; nhi[n++] = hi[g];
                }
            }

            nlo.Slice(0, n).CopyTo(lo);
            nhi.Slice(0, n).CopyTo(hi);
            return n;
        }

        /// <summary>A waterfall (#3959): four water faces around the point, from the waterfall's foot to the island's
        /// underside. The water colour carries the far view's sea flag, so the shader tints it like the far sea.</summary>
        private void AddWaterfall(FloatMesh mesh, in FarFloat f, float cx, float cz, Func<FarSample, bool, FarEditTop, uint> colorOf)
        {
            float h = Math.Max(1f, _cell * WaterfallWidthShare * 0.5f);
            float offset = FarTerrainLayout.HeightOffset(Key.Level);
            float a = f.Bottom + offset, b = f.Top + offset;
            float x0 = cx - h, x1 = cx + h, z0 = cz - h, z1 = cz + h;
            uint water = colorOf(f.Surface, false, default);
            mesh.Quad((x0, a, z0), (x0, a, z1), (x0, b, z1), (x0, b, z0), (-1f, 0f, 0f), water);
            mesh.Quad((x1, a, z0), (x1, b, z0), (x1, b, z1), (x1, a, z1), (1f, 0f, 0f), water);
            mesh.Quad((x0, a, z0), (x0, b, z0), (x1, b, z0), (x1, a, z0), (0f, 0f, -1f), water);
            mesh.Quad((x0, a, z1), (x1, a, z1), (x1, b, z1), (x0, b, z1), (0f, 0f, 1f), water);
        }

        /// <summary>Appends the floating shapes' arrays to the height field's.</summary>
        private static void Merge(FarPatchGeometry geo, FloatMesh mesh)
        {
            int extra = mesh.Colors.Count;
            int vertices = geo.VertexCount + extra;
            var positions = new float[vertices * 3];
            var normals = new float[vertices * 3];
            var colors = new uint[vertices];
            var indices = new int[geo.Indices.Length + mesh.Indices.Count];
            Array.Copy(geo.Positions, positions, geo.VertexCount * 3);
            Array.Copy(geo.Normals, normals, geo.VertexCount * 3);
            Array.Copy(geo.Colors, colors, geo.VertexCount);
            Array.Copy(geo.Indices, indices, geo.Indices.Length);
            mesh.Positions.CopyTo(positions, geo.VertexCount * 3);
            mesh.Normals.CopyTo(normals, geo.VertexCount * 3);
            mesh.Colors.CopyTo(colors, geo.VertexCount);
            mesh.Indices.CopyTo(indices, geo.Indices.Length);
            geo.Positions = positions;
            geo.Normals = normals;
            geo.Colors = colors;
            geo.Indices = indices;
            geo.VertexCount = vertices;
            geo.MinY = Math.Min(geo.MinY, mesh.MinY);
            geo.MaxY = Math.Max(geo.MaxY, mesh.MaxY);
        }
    }
}

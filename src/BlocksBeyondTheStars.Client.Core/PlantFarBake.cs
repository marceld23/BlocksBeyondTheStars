// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Plant far pictures: bakes the crossed far picture of a small plant from its generated body, so the
    /// picture beyond the bodies' range has the body's outline instead of the old painted tile's. A CPU rasteriser over
    /// the body's triangles: two orthographic side views 90° apart (along +z and along +x), merged — a texel the first
    /// view covers keeps its colour, one only the second covers takes the second's — into a 64 × 64 RGBA tile at 2 × 2
    /// samples per texel. Alpha is the samples' coverage (the block shader alpha-tests it like every plant tile), the
    /// colour the mean of the covered samples; uncovered texels are black and clear, the dark ground the block atlas'
    /// cutout tiles have too. The one-texel border stays clear, so nothing bleeds into a neighbouring cell.
    /// <para>
    /// Shape and brightness only: the colour is the body's recipe (PlantBody.shader, <c>BbtsBodyAlbedo</c>) over the
    /// species' OWN tile colours (<see cref="Colours"/>), never the world's tint — the block shader re-tints a wild
    /// plant's far picture by its luminance as it re-tints the old tile, and an accent texel (bright, saturated, not
    /// green) still reads as a blossom there. Pure and deterministic: the same body gives the same bytes.
    /// </para>
    /// </summary>
    public static class PlantFarBake
    {
        /// <summary>The picture's side in texels (a block tile's).</summary>
        public const int Size = 64;

        /// <summary>Samples per texel side.</summary>
        public const int Super = 2;

        private const int Samples = Size * Super;

        /// <summary>The light the baked shading comes from, in view space (x right, y up, z into the view): from the upper
        /// left, a little toward the eye.</summary>
        private static readonly Vector3 Light = Vector3.Normalize(new Vector3(-0.35f, 0.8f, -0.5f));

        /// <summary>What the picture shows, in the body's own units: u 0..1 runs over x (or z) −HalfWidth..+HalfWidth about
        /// the root, v 0..1 over y 0..Height (v = 0 at the root, the tile's own convention).</summary>
        public readonly struct Frame
        {
            public Frame(float halfWidth, float height)
            {
                HalfWidth = Math.Max(0.01f, halfWidth);
                Height = Math.Max(0.01f, height);
            }

            public float HalfWidth { get; }

            public float Height { get; }
        }

        /// <summary>The four colour slots of a body (linear, or the shader's space in a gamma project): the main colour,
        /// the second (young leaves), the accent (blossoms, berries, tips) and the dark one (stems).</summary>
        public readonly struct Colours
        {
            public Colours(Vector3 main, Vector3 second, Vector3 accent, Vector3 dark)
            {
                Main = main;
                Second = second;
                Accent = accent;
                Dark = dark;
            }

            public Vector3 Main { get; }

            public Vector3 Second { get; }

            public Vector3 Accent { get; }

            public Vector3 Dark { get; }
        }

        /// <summary>A baked picture and its numbers.</summary>
        public sealed class Result
        {
            internal Result(byte[] rgba, int covered, int minX, int minY, int maxX, int maxY)
            {
                Rgba = rgba;
                Covered = covered;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            /// <summary><see cref="Size"/> × <see cref="Size"/> texels, RGBA, row 0 at the root (v = 0).</summary>
            public byte[] Rgba { get; }

            /// <summary>Texels that pass the cutout (alpha at least one half).</summary>
            public int Covered { get; }

            /// <summary>The share of the picture that passes the cutout.</summary>
            public float Coverage => Covered / (float)(Size * Size);

            /// <summary>The bounds of the covered texels (inclusive); −1 when nothing is covered.</summary>
            public int MinX { get; }

            public int MinY { get; }

            public int MaxX { get; }

            public int MaxY { get; }
        }

        /// <summary>
        /// Bakes the picture. <paramref name="weights"/> per vertex: x = brightness (0 dark … 1 light), y = second-colour
        /// weight, z = accent weight, w = dark weight — the body's vertex colour and part data. <paramref name="linearSpace"/>
        /// encodes the bytes as sRGB (the far atlas is an sRGB texture); in a gamma project they are the colour itself.
        /// </summary>
        public static Result Bake(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector4> weights, IReadOnlyList<int> triangles,
            Frame frame, Colours colours, bool linearSpace)
        {
            // The two views' sample buffers (2 × 192 KB) are kept per thread: a world bakes some sixty species in a row.
            var front = (_front ??= new View()).Reset(colours, frame);
            var side = (_side ??= new View()).Reset(colours, frame);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= positions.Count || b >= positions.Count || c >= positions.Count)
                {
                    continue;
                }

                var w = (weights[a] + weights[b] + weights[c]) / 3f;
                front.Draw(positions[a], positions[b], positions[c], w, turned: false);
                side.Draw(positions[a], positions[b], positions[c], w, turned: true);
            }

            return Resolve(front, side, linearSpace);
        }

        /// <summary>The projected bounds of the triangles' vertices in texels (front and side view merged, clamped to the
        /// picture): what the covered texels' bounds should come to, give or take a texel at a thin tip.</summary>
        public static (float MinX, float MinY, float MaxX, float MaxY) ProjectedBounds(IReadOnlyList<Vector3> positions,
            IReadOnlyList<int> triangles, Frame frame)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (int i in triangles)
            {
                var p = positions[i];
                foreach (float across in new[] { p.X, p.Z })
                {
                    float x = ((across / (2f * frame.HalfWidth)) + 0.5f) * Size;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }

                float y = p.Y / frame.Height * Size;
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            return (Clamp(minX, 1f, Size - 1f), Clamp(minY, 1f, Size - 1f), Clamp(maxX, 1f, Size - 1f), Clamp(maxY, 1f, Size - 1f));
        }

        private static Result Resolve(View front, View side, bool linearSpace)
        {
            var rgba = new byte[Size * Size * 4];
            int covered = 0, minX = -1, minY = -1, maxX = -1, maxY = -1;
            for (int y = 1; y < Size - 1; y++)
            {
                for (int x = 1; x < Size - 1; x++)
                {
                    var sum = Vector3.Zero;
                    int hits = 0;
                    for (int k = 0; k < Super * Super; k++)
                    {
                        int s = (((y * Super) + (k / Super)) * Samples) + (x * Super) + (k % Super);
                        var view = front.Depth[s] < float.MaxValue ? front : side.Depth[s] < float.MaxValue ? side : null;
                        if (view != null)
                        {
                            sum += view.Colour[s];
                            hits++;
                        }
                    }

                    if (hits == 0)
                    {
                        continue;
                    }

                    var c = sum / hits;
                    int o = ((y * Size) + x) * 4;
                    rgba[o] = Encode(c.X, linearSpace);
                    rgba[o + 1] = Encode(c.Y, linearSpace);
                    rgba[o + 2] = Encode(c.Z, linearSpace);
                    rgba[o + 3] = (byte)((hits * 255 + ((Super * Super) / 2)) / (Super * Super));
                    if (hits * 2 >= Super * Super)
                    {
                        covered++;
                        minX = minX < 0 ? x : Math.Min(minX, x);
                        minY = minY < 0 ? y : Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            return new Result(rgba, covered, minX, minY, maxX, maxY);
        }

        [ThreadStatic]
        private static View? _front;

        [ThreadStatic]
        private static View? _side;

        /// <summary>One side view's samples: the nearest triangle's depth and its colour.</summary>
        private sealed class View
        {
            public readonly float[] Depth = new float[Samples * Samples];
            public readonly Vector3[] Colour = new Vector3[Samples * Samples];
            private Colours _colours;
            private Frame _frame;

            /// <summary>Empties the samples for a new bake.</summary>
            public View Reset(Colours colours, Frame frame)
            {
                _colours = colours;
                _frame = frame;
                for (int i = 0; i < Depth.Length; i++)
                {
                    Depth[i] = float.MaxValue;
                }

                return this;
            }

            /// <summary>Rasterises one triangle: the front view looks along +z (x right), the turned one along +x (z right,
            /// so its picture is the body turned a quarter about its axis). Both windings count — a leaf has two sides.</summary>
            public void Draw(Vector3 a, Vector3 b, Vector3 c, Vector4 weights, bool turned)
            {
                var pa = Project(a, turned);
                var pb = Project(b, turned);
                var pc = Project(c, turned);
                float area = Edge(pa, pb, pc);
                if (Math.Abs(area) < 1e-9f)
                {
                    return; // seen edge-on: no sample centre lies inside
                }

                var viewNormal = Vector3.Cross(ToView(b, turned) - ToView(a, turned), ToView(c, turned) - ToView(a, turned));
                float shade = 0.72f + (0.38f * Math.Abs(Vector3.Dot(SafeNormalize(viewNormal), Light)));
                var colour = Albedo(weights) * shade;
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(pa.X, Math.Min(pb.X, pc.X))));
                int x1 = Math.Min(Samples - 1, (int)Math.Ceiling(Math.Max(pa.X, Math.Max(pb.X, pc.X))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(pa.Y, Math.Min(pb.Y, pc.Y))));
                int y1 = Math.Min(Samples - 1, (int)Math.Ceiling(Math.Max(pa.Y, Math.Max(pb.Y, pc.Y))));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector3(x + 0.5f, y + 0.5f, 0f);
                        float wa = Edge(pb, pc, p) / area, wb = Edge(pc, pa, p) / area, wc = Edge(pa, pb, p) / area;
                        if (wa < 0f || wb < 0f || wc < 0f)
                        {
                            continue;
                        }

                        float depth = (wa * pa.Z) + (wb * pb.Z) + (wc * pc.Z);
                        int s = (y * Samples) + x;
                        if (depth < Depth[s])
                        {
                            Depth[s] = depth;
                            Colour[s] = colour;
                        }
                    }
                }
            }

            /// <summary>The body's recipe (PlantBody.shader): the slots mixed by the weights, times the brightness ramp.</summary>
            private Vector3 Albedo(Vector4 w)
            {
                var albedo = Vector3.Lerp(_colours.Main, _colours.Second, Clamp(w.Y, 0f, 1f));
                albedo = Vector3.Lerp(albedo, _colours.Accent, Clamp(w.Z, 0f, 1f));
                albedo = Vector3.Lerp(albedo, _colours.Dark, Clamp(w.W, 0f, 1f));
                return albedo * Vector3.Lerp(new Vector3(0.50f, 0.57f, 0.68f), new Vector3(1.12f, 1.06f, 0.93f), Clamp(w.X, 0f, 1f));
            }

            // View space: x right, y up, z into the view (depth).
            private static Vector3 ToView(Vector3 p, bool turned) => turned ? new Vector3(p.Z, p.Y, p.X) : new Vector3(p.X, p.Y, p.Z);

            // Sample space: x, y in samples, z = depth.
            private Vector3 Project(Vector3 p, bool turned)
            {
                var v = ToView(p, turned);
                return new Vector3(((v.X / (2f * _frame.HalfWidth)) + 0.5f) * Samples, v.Y / _frame.Height * Samples, v.Z);
            }
        }

        private static float Edge(Vector3 a, Vector3 b, Vector3 p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

        private static Vector3 SafeNormalize(Vector3 v)
        {
            float length = v.Length();
            return length > 1e-12f ? v / length : Vector3.UnitY;
        }

        private static byte Encode(float value, bool linearSpace)
        {
            float v = Clamp(value, 0f, 1f);
            if (linearSpace)
            {
                v = v <= 0.0031308f ? v * 12.92f : (1.055f * (float)Math.Pow(v, 1.0 / 2.4)) - 0.055f;
            }

            return (byte)Math.Round(Clamp(v, 0f, 1f) * 255f);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}

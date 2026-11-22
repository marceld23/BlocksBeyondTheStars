// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meshes of the chamfered and the cone parts of a held tool (<see cref="HeldPartForms"/>).
    /// Like <see cref="HeldMaterialRules.BuildCube"/> each is a UNIT mesh (−0.5..0.5 on every axis) the part's transform
    /// scales to its size, so the hand's measuring and the muzzle stay as they are; but it is built for the part's own
    /// proportions, because a 45° cut in metres is not 45° in the scaled unit space. The normals are the unit mesh's own
    /// (Unity turns them by the inverse of the scale, which gives the true normal in metres). The UVs run in tiles of
    /// <see cref="HeldMaterialRules.TileMetres"/> like the cube's: flat faces planar on their own two axes, the cone's
    /// mantle a whole number of tiles around (the material tiles wrap, so there is no seam) and along its axis.
    /// </summary>
    public static class HeldFormMeshes
    {
        /// <summary>The cut of a regular octagon, as a share of the cross side (1 − 1/√2).</summary>
        public const float RegularCut = 0.2929f;

        /// <summary>The cone's tip radius as a share of its base radius.</summary>
        public const float ConeTip = 0.3f;

        /// <summary>The cone's segments around.</summary>
        public const int ConeSegments = 12;

        /// <summary>
        /// A box <paramref name="size"/> metres big with its four edges along <paramref name="axis"/> cut flat at 45° (in
        /// metres): the cut is <paramref name="cut"/> (clamped to 0.01..0.45) of the smaller of the two cross sides, so
        /// <see cref="RegularCut"/> makes a square cross-section a regular octagon. Eight side faces and two octagonal end
        /// caps, each flat: 48 vertices, 28 triangles.
        /// </summary>
        public static HeldMaterialRules.CubeMesh BuildChamfer(Vector3 size, int axis, float cut)
        {
            var frame = new Frame(size, axis);
            float c = Math.Max(0.01f, Math.Min(0.45f, cut)) * Math.Min(frame.Su, frame.Sv);
            float hu = frame.Su * 0.5f, hv = frame.Sv * 0.5f;

            // The octagon in metres (u, v), counter-clockwise about the axis (u × v = axis).
            var ring = new[]
            {
                new Vector2(hu, -hv + c), new Vector2(hu, hv - c), new Vector2(hu - c, hv), new Vector2(-hu + c, hv),
                new Vector2(-hu, hv - c), new Vector2(-hu, -hv + c), new Vector2(-hu + c, -hv), new Vector2(hu - c, -hv),
            };

            var mesh = new Builder(frame);
            for (int k = 0; k < ring.Length; k++)
            {
                mesh.Side(ring[k], ring[(k + 1) % ring.Length]);
            }

            mesh.Cap(ring, 1);
            mesh.Cap(ring, -1);
            return mesh.Finish();
        }

        /// <summary>
        /// A cone along <paramref name="axis"/> for a part <paramref name="size"/> metres big, narrowing toward the
        /// <paramref name="tip"/> side (+1 / −1): a frustum whose base is the ellipse inscribed in the part's cross-section
        /// and whose tip is <see cref="ConeTip"/> of it, <see cref="ConeSegments"/> segments around, smooth normals on the
        /// mantle and flat end caps: 50 vertices, 44 triangles.
        /// </summary>
        public static HeldMaterialRules.CubeMesh BuildCone(Vector3 size, int axis, int tip)
        {
            var frame = new Frame(size, axis);
            float s = tip < 0 ? -1f : 1f;
            float hu = frame.Su * 0.5f, hv = frame.Sv * 0.5f;
            var baseRing = new Vector2[ConeSegments];
            var tipRing = new Vector2[ConeSegments];
            for (int k = 0; k < ConeSegments; k++)
            {
                double a = 2.0 * Math.PI * k / ConeSegments;
                var round = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                baseRing[k] = new Vector2(round.X * hu, round.Y * hv);
                tipRing[k] = baseRing[k] * ConeTip;
            }

            var mesh = new Builder(frame);
            mesh.Mantle(baseRing, tipRing, s, TilesAround(hu, hv));
            mesh.Cap(baseRing, -s);
            mesh.Cap(tipRing, s);
            return mesh.Finish();
        }

        /// <summary>How many tiles run around a cone whose base half-axes are <paramref name="hu"/> and <paramref name="hv"/>
        /// metres: its mean circumference (Ramanujan's ellipse perimeter, averaged over base and tip) in tiles, rounded,
        /// at least one.</summary>
        public static int TilesAround(float hu, float hv)
        {
            double a = hu, b = hv;
            double perimeter = Math.PI * ((3.0 * (a + b)) - Math.Sqrt(((3.0 * a) + b) * (a + (3.0 * b))));
            double mean = perimeter * (1.0 + ConeTip) * 0.5;
            return Math.Max(1, (int)Math.Round(mean / HeldMaterialRules.TileMetres, MidpointRounding.AwayFromZero));
        }

        /// <summary>The part's axis and its two cross axes (u, v with u × v = axis) and their sides in metres.</summary>
        private readonly struct Frame
        {
            public Frame(Vector3 size, int axis)
            {
                A = axis is >= 0 and <= 2 ? axis : 2;
                U = (A + 1) % 3;
                V = (A + 2) % 3;
                var abs = new Vector3(Math.Abs(size.X), Math.Abs(size.Y), Math.Abs(size.Z));
                Sa = Math.Max(1e-4f, HeldPartForms.Side(abs, A));
                Su = Math.Max(1e-4f, HeldPartForms.Side(abs, U));
                Sv = Math.Max(1e-4f, HeldPartForms.Side(abs, V));
            }

            public int A { get; }

            public int U { get; }

            public int V { get; }

            public float Sa { get; }

            public float Su { get; }

            public float Sv { get; }

            /// <summary>The unit-space point of a point given in metres along the axis (<paramref name="a"/>) and across.</summary>
            public Vector3 Unit(float a, Vector2 across) => Compose(a / Sa, across.X / Su, across.Y / Sv);

            /// <summary>A unit-space direction from its axis, u and v components.</summary>
            public Vector3 Compose(float a, float u, float v)
            {
                var r = new float[3];
                r[A] = a;
                r[U] = u;
                r[V] = v;
                return new Vector3(r[0], r[1], r[2]);
            }
        }

        /// <summary>Collects faces; every triangle is wound so cross(b − a, c − a) points along its normals.</summary>
        private sealed class Builder
        {
            private readonly Frame _f;
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public Builder(Frame frame) => _f = frame;

            /// <summary>The flat side face over the cross-section edge <paramref name="from"/> → <paramref name="to"/>
            /// (metres): u runs along the edge, v along the axis.</summary>
            public void Side(Vector2 from, Vector2 to)
            {
                float ha = _f.Sa * 0.5f, length = Vector2.Distance(from, to) / HeldMaterialRules.TileMetres;
                float along = _f.Sa / HeldMaterialRules.TileMetres;
                Vector3 p0 = _f.Unit(-ha, from), p1 = _f.Unit(-ha, to), p2 = _f.Unit(ha, to), p3 = _f.Unit(ha, from);
                var edge = _f.Unit(0f, to) - _f.Unit(0f, from); // in unit space, perpendicular to the axis
                var outward = Vector3.Normalize(Vector3.Cross(edge, _f.Compose(1f, 0f, 0f)));
                int i = Add(p0, outward, new Vector2(0f, 0f));
                Add(p1, outward, new Vector2(length, 0f));
                Add(p2, outward, new Vector2(length, along));
                Add(p3, outward, new Vector2(0f, along));
                Quad(i, i + 1, i + 2, i + 3);
            }

            /// <summary>A flat end cap over <paramref name="ring"/> (metres, convex) at the <paramref name="side"/> end of the
            /// axis, its UVs planar on u and v.</summary>
            public void Cap(Vector2[] ring, float side)
            {
                float ha = _f.Sa * 0.5f * side, hu = _f.Su * 0.5f, hv = _f.Sv * 0.5f;
                var normal = _f.Compose(side, 0f, 0f);
                int first = _positions.Count;
                foreach (var q in ring)
                {
                    float u = side > 0f ? q.X + hu : hu - q.X; // seen from outside the cap, u × v points out
                    Add(_f.Unit(ha, q), normal, new Vector2(u / HeldMaterialRules.TileMetres, (q.Y + hv) / HeldMaterialRules.TileMetres));
                }

                for (int k = 1; k + 1 < ring.Length; k++)
                {
                    Triangle(first, first + k, first + k + 1);
                }
            }

            /// <summary>The cone's mantle from <paramref name="baseRing"/> (at the −<paramref name="s"/> end) to
            /// <paramref name="tipRing"/>; one extra column at the seam carries u = <paramref name="tiles"/>.</summary>
            public void Mantle(Vector2[] baseRing, Vector2[] tipRing, float s, int tiles)
            {
                float ha = _f.Sa * 0.5f, length = _f.Sa / HeldMaterialRules.TileMetres;
                float slope = 0.5f * (1f - ConeTip); // the unit cone's radius loses this over its unit length
                int n = baseRing.Length, first = _positions.Count;
                for (int k = 0; k <= n; k++)
                {
                    var b = baseRing[k % n];
                    var t = tipRing[k % n];
                    double angle = 2.0 * Math.PI * (k % n) / n;
                    var normal = Vector3.Normalize(_f.Compose(slope * s, (float)Math.Cos(angle), (float)Math.Sin(angle)));
                    float u = tiles * k / (float)n;
                    Add(_f.Unit(-ha * s, b), normal, new Vector2(u, s > 0f ? 0f : length));
                    Add(_f.Unit(ha * s, t), normal, new Vector2(u, s > 0f ? length : 0f));
                }

                for (int k = 0; k < n; k++)
                {
                    int b0 = first + (2 * k), t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    Quad(b0, b1, t1, t0);
                }
            }

            public HeldMaterialRules.CubeMesh Finish()
                => new HeldMaterialRules.CubeMesh(_positions.ToArray(), _normals.ToArray(), _uvs.ToArray(), _triangles.ToArray());

            private int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                _positions.Add(position);
                _normals.Add(normal);
                _uvs.Add(uv);
                return _positions.Count - 1;
            }

            private void Quad(int a, int b, int c, int d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            // Adds a triangle wound outward: if cross(b − a, c − a) points against the vertices' normals, b and c swap.
            private void Triangle(int a, int b, int c)
            {
                var cross = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                bool inward = Vector3.Dot(cross, _normals[a] + _normals[b] + _normals[c]) < 0f;
                _triangles.Add(a);
                _triangles.Add(inward ? c : b);
                _triangles.Add(inward ? b : c);
            }
        }
    }
}

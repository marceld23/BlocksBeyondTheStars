// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Crown far forms: the far stage of a needle or a palm crown cell. Near the camera
    /// such a cell is a crown form out of small boxes (<c>BlockShapeGeometry.CrownForm</c>: a bough that slopes down and
    /// outward, the spire, a frond, the palm's heart); beyond the clusters' range it used to be the leaf block's shell, so
    /// a conifer turned from a cone into a stack of blocks where the range ended. This is the same form as a handful of
    /// quads that follow the near form's ENVELOPE, in the near form's frame — the cell's 0..1 box, +X pointing away from
    /// the tree's axis, turned about the cell's middle by the mesher — so the outline agrees at the hand-over:
    /// <list type="bullet">
    /// <item><b>bough</b>: one closed wedge from the trunk-side edge (crest 1.0 high, 0.80 wide) to the hanging tip (0.30
    /// high, 0.28 wide) at the near bough's reach — 6 quads;</item>
    /// <item><b>spire</b>: a closed square frustum from the near spire's base (0.72 wide) to its point (1.32 high) — 6 quads;</item>
    /// <item><b>frond</b>: a flat leaf card over the rib and the leaflets, both sides — 2 quads, or 8 for the drooping end
    /// (four segments that sink and narrow like the near pieces);</item>
    /// <item><b>heart</b>: the bud as a closed box and six spokes as two-sided cards that rise as they spread — 18 quads.</item>
    /// </list>
    /// Every quad's corners give its outward normal as (B − A) × (D − A), the rule the chunk mesher shades and culls by;
    /// a card is drawn once per side. Texture coordinates are the quad's own planar projection, fitted into 0.02..0.98
    /// of the tile, so nothing samples a neighbouring atlas tile. Pure and cached: the same faces for every client.
    /// </summary>
    public static class CrownFarForms
    {
        /// <summary>The crown forms (the values of <c>BlockShapeGeometry.CrownForm*</c>).</summary>
        public const int Leaf = 0, NeedleBough = 1, NeedleTip = 2, Frond = 3, FrondRosette = 4;

        /// <summary>One quad of a far form.</summary>
        public readonly struct Quad
        {
            public Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int part)
            {
                A = a;
                B = b;
                C = c;
                D = d;
                UvA = ua;
                UvB = ub;
                UvC = uc;
                UvD = ud;
                Part = part;
            }

            public Vector3 A { get; }

            public Vector3 B { get; }

            public Vector3 C { get; }

            public Vector3 D { get; }

            public Vector2 UvA { get; }

            public Vector2 UvB { get; }

            public Vector2 UvC { get; }

            public Vector2 UvD { get; }

            /// <summary>The shade class 0..5 (the near boxes' <c>ShapePart</c>: tone and shade in the crown shader).</summary>
            public int Part { get; }

            /// <summary>The outward normal, not normalised.</summary>
            public Vector3 Normal => Vector3.Cross(B - A, D - A);

            public Vector3 Centre => (A + B + C + D) * 0.25f;
        }

        private static readonly ConcurrentDictionary<int, IReadOnlyList<Quad>> Cache = new ConcurrentDictionary<int, IReadOnlyList<Quad>>();

        /// <summary>The near bough's reach (cells from the trunk-side edge of its cell) for a variant.</summary>
        public static float BoughReach(int variant) => variant <= 0 ? 0.5f : variant == 1 ? 0.85f : 1.2f;

        /// <summary>True for the forms that have a far form (everything but the leaf cluster).</summary>
        public static bool Has(int form) => form >= NeedleBough && form <= FrondRosette;

        /// <summary>The quads of a far form (read-only, cached); empty for <see cref="Leaf"/> and unknown forms.</summary>
        public static IReadOnlyList<Quad> Of(int form, int variant)
            => Cache.GetOrAdd((form << 8) | (variant & 0xFF), _ => Build(form, variant));

        private static IReadOnlyList<Quad> Build(int form, int variant)
        {
            var q = new List<Quad>();
            switch (form)
            {
                case NeedleBough:
                    float reach = BoughReach(variant);
                    // The near bough: crest at the trunk −0.03..1.0 high, steps sinking 0.20 a step, the tip box at
                    // reach − 0.03 … reach + 0.16, 0.08 … 0.30 high, 0.36 … 0.64 wide; underneath down to 0.41 at the root.
                    Wedge(q, -0.03f, 0.50f, 1.00f, 0.40f, reach + 0.16f, 0.08f, 0.30f, 0.14f);
                    break;
                case NeedleTip:
                    Frustum(q, 0.36f, 0.04f, 1.32f);
                    break;
                case Frond:
                    if (variant == 0)
                    {
                        Card(q, new[] { -0.04f, 1.07f }, new[] { 0.58f, 0.58f }, new[] { 0.405f, 0.405f }, 0);
                    }
                    else
                    {
                        // The drooping end: the near rib's four pieces at 0.60, 0.54, 0.40, 0.22 over 1.36, the leaflets
                        // 0.34 shortening by 0.06 a piece (plus half the rib's 0.09).
                        Card(q, new[] { -0.04f, 0.34f, 0.68f, 1.02f, 1.39f }, new[] { 0.60f, 0.57f, 0.47f, 0.31f, 0.20f },
                            new[] { 0.385f, 0.355f, 0.295f, 0.235f, 0.10f }, 0);
                    }

                    break;
                case FrondRosette:
                    Heart(q);
                    break;
            }

            return q;
        }

        /// <summary>The bough: a closed wedge along +X, symmetric about z = 0.5 — at x0 from y0b to y0t and ±h0 wide, at x1
        /// from y1b to y1t and ±h1 wide.</summary>
        private static void Wedge(List<Quad> q, float x0, float y0b, float y0t, float h0, float x1, float y1b, float y1t, float h1)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool outer = (i & 1) != 0, top = (i & 2) != 0, far = (i & 4) != 0;
                float h = outer ? h1 : h0;
                c[i] = new Vector3(outer ? x1 : x0, outer ? (top ? y1t : y1b) : (top ? y0t : y0b), 0.5f + (far ? h : -h));
            }

            Hexahedron(q, c, 0);
        }

        /// <summary>The spire: a closed square frustum about the cell's vertical middle, half-width hb at y 0, ht at y top.</summary>
        private static void Frustum(List<Quad> q, float hb, float ht, float top)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool px = (i & 1) != 0, up = (i & 2) != 0, pz = (i & 4) != 0;
                float h = up ? ht : hb;
                c[i] = new Vector3(0.5f + (px ? h : -h), up ? top : 0f, 0.5f + (pz ? h : -h));
            }

            Hexahedron(q, c, 1);
        }

        /// <summary>The palm's heart: the bud (0.38 … 0.62, up to 0.42) and six spokes from the bud to 0.78 from the middle,
        /// rising from 0.41 to 0.53, 0.285 wide at the bud and 0.07 at the tip, every 60° from 30°.</summary>
        private static void Heart(List<Quad> q)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new Vector3((i & 1) != 0 ? 0.62f : 0.38f, (i & 2) != 0 ? 0.42f : 0f, (i & 4) != 0 ? 0.62f : 0.38f);
            }

            Hexahedron(q, c, 1);
            for (int s = 0; s < 6; s++)
            {
                double angle = (30.0 + (60.0 * s)) * Math.PI / 180.0;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                Vector3 P(float r, float y, float w) => new Vector3(0.5f + (r * cos) - (w * sin), y, 0.5f + (r * sin) + (w * cos));
                AddTwoSided(q, P(0.05f, 0.41f, -0.285f), P(0.78f, 0.53f, -0.035f), P(0.78f, 0.53f, 0.035f), P(0.05f, 0.41f, 0.285f), (s % 3) + 2);
            }
        }

        /// <summary>A two-sided leaf card along +X through the given stations: at x[i] it lies at y[i], ±half[i] about z =
        /// 0.5 — one quad per segment and side.</summary>
        private static void Card(List<Quad> q, float[] x, float[] y, float[] half, int part)
        {
            for (int i = 0; i + 1 < x.Length; i++)
            {
                AddTwoSided(q, new Vector3(x[i], y[i], 0.5f - half[i]), new Vector3(x[i + 1], y[i + 1], 0.5f - half[i + 1]),
                    new Vector3(x[i + 1], y[i + 1], 0.5f + half[i + 1]), new Vector3(x[i], y[i], 0.5f + half[i]), (part + i) % 6);
            }
        }

        /// <summary>A closed hexahedron from its 8 corners (index bits: 1 = +x side, 2 = top, 4 = +z side), every face
        /// wound outward from the solid's middle.</summary>
        private static void Hexahedron(List<Quad> q, Vector3[] c, int part)
        {
            var middle = Vector3.Zero;
            foreach (var p in c)
            {
                middle += p / 8f;
            }

            int[][] faces =
            {
                new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, // −x, +x
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, // bottom, top
                new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, // −z, +z
            };
            for (int f = 0; f < faces.Length; f++)
            {
                var k = faces[f];
                AddOutward(q, c[k[0]], c[k[1]], c[k[2]], c[k[3]], middle, (part + f) % 6);
            }
        }

        private static void AddOutward(List<Quad> q, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 middle, int part)
        {
            var quad = Make(a, b, c, d, part);
            q.Add(Vector3.Dot(quad.Normal, quad.Centre - middle) >= 0f ? quad : Make(a, d, c, b, part));
        }

        private static void AddTwoSided(List<Quad> q, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int part)
        {
            q.Add(Make(a, b, c, d, part));
            q.Add(Make(a, d, c, b, part));
        }

        /// <summary>A quad with its planar texture coordinates: projected along the normal's dominant axis, then fitted into
        /// 0.02..0.98 of the tile.</summary>
        private static Quad Make(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int part)
        {
            var n = Vector3.Cross(b - a, d - a);
            float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
            int axis = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
            Vector2 P(Vector3 p) => axis == 1 ? new Vector2(p.X, p.Z) : axis == 0 ? new Vector2(p.Z, p.Y) : new Vector2(p.X, p.Y);
            Vector2 pa = P(a), pb = P(b), pc = P(c), pd = P(d);
            var min = Vector2.Min(Vector2.Min(pa, pb), Vector2.Min(pc, pd));
            var max = Vector2.Max(Vector2.Max(pa, pb), Vector2.Max(pc, pd));
            var size = Vector2.Max(max - min, new Vector2(1e-4f));
            Vector2 Fit(Vector2 p) => new Vector2(0.02f, 0.02f) + ((p - min) / size * 0.96f);
            return new Quad(a, b, c, d, Fit(pa), Fit(pb), Fit(pc), Fit(pd), part);
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One outward polygon of a player form in the unit cell: a quad (A..D) or a triangle (A..C), wound like the
    /// mesher's faces (CCW from outside, normal = cross(B − A, D − A) or cross(B − A, C − A)). The texture coordinates are
    /// FRACTIONS of the block's tile (the planar projection of the corner along the polygon's main axis), and
    /// <see cref="Slot"/> is the colour slot of the micro cell the polygon belongs to.</summary>
    public readonly struct ShapeFacet
    {
        public ShapeFacet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool isQuad, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD, int slot)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            IsQuad = isQuad;
            UvA = uvA;
            UvB = uvB;
            UvC = uvC;
            UvD = uvD;
            Slot = slot;
        }

        public Vector3 A { get; }

        public Vector3 B { get; }

        public Vector3 C { get; }

        public Vector3 D { get; }

        public bool IsQuad { get; }

        public Vector2 UvA { get; }

        public Vector2 UvB { get; }

        public Vector2 UvC { get; }

        public Vector2 UvD { get; }

        public int Slot { get; }

        public Vector3 Normal => Vector3.Cross(B - A, (IsQuad ? D : C) - A);
    }

    /// <summary>
    /// Soft edges on a player form: a narrow 45° chamfer on the convex OUTER edges of the union of its
    /// boxes, the cube bevel of the chunk mesher (<c>ChunkMesher.EmitBevel</c>) applied at the scale of the micro cells.
    /// <para>
    /// <b>Rule.</b> The boxes are laid back onto their micro grid. A micro cell's face is open when the neighbouring micro
    /// cell is empty; it may be chamfered when it is open AND not on the border of the block cell — there the neighbouring
    /// block may continue the surface (a form over several blocks, a row of forms, a wall) and the cube bevel likewise keeps
    /// an edge against a solid neighbour flush; the block shader's soft border already rounds that edge. An edge is cut when
    /// both its faces may be chamfered (a strip), a corner when all three may (a triangle). A face is pulled in by the width
    /// along each cut edge. Where a strip runs into a micro cell of the same form that does not continue the edge (the inner
    /// corner of an L), the strip's end is closed by a small triangle on that cell's face. Faces, strips and caps are merged
    /// along runs, so a long edge is one strip.
    /// </para>
    /// <para>
    /// <b>Closed.</b> The result is a closed surface (every edge segment is shared by two polygons, T-junctions where a
    /// flush face meets an inset one, as in the cube bevel). Faces between two micro cells of the form are left out. Every
    /// point stays inside the cell. Width 0 returns exactly the plain faces of the boxes (<see cref="Plain"/>, the six faces
    /// per box of <c>BlockShapeGeometry.MicroBox</c>, buried ones included, in the same order).
    /// </para>
    /// </summary>
    public static class ShapeChamfer
    {
        /// <summary>The default: the chamfer as a share of a micro cell. 0.48 of a 1/8-block cell is 6 cm, the cube
        /// bevel's width (<c>ChunkMesher.BevelAmount</c> 0.06).</summary>
        public const float DefaultShare = 0.48f;

        /// <summary>Widest chamfer in micro cells: below one half, so the two cuts of a one-cell-thin part never meet.</summary>
        public const float MaxShare = 0.49f;

        /// <summary>The most polygons a chamfered cell may have: twice the plain faces of the largest form the server allows
        /// (2 × 6 × <see cref="CustomShape.MaxBoxes"/> = 576). A form over it keeps hard edges.</summary>
        public const int MaxFacets = 2 * 6 * CustomShape.MaxBoxes;

        /// <summary>Face flag: the face lies on the block cell's border (see <see cref="EmitBorderFace"/>).</summary>
        private const int BorderFace = 16;

        private static readonly int[] Dx = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] Dy = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] Dz = { 0, 0, 0, 0, 1, -1 };

        /// <summary>The chamfer width in block units for a share of a micro cell, never wider than <paramref name="cap"/>
        /// (the cube bevel's width, so a coarse 4³ form is not cut twice as deep as a cube).</summary>
        public static float WidthFor(float share, int grid, float cap)
            => share <= 0f || grid <= 0 ? 0f : Math.Min(Math.Min(share, MaxShare) / grid, cap);

        /// <summary>The six faces of every box (no chamfer): today's faces of a player form.</summary>
        public static List<ShapeFacet> Plain(IReadOnlyList<SlotBox> boxes)
        {
            var result = new List<ShapeFacet>(boxes.Count * 6);
            foreach (var sb in boxes)
            {
                var b = sb.Box;
                float g = b.Grid;
                for (int dir = 0; dir < 6; dir++)
                {
                    result.Add(BoxFace(dir, b.X0 / g, b.Y0 / g, b.Z0 / g, b.X1 / g, b.Y1 / g, b.Z1 / g, sb.Slot));
                }
            }

            return result;
        }

        /// <summary>The chamfered faces of the union of <paramref name="boxes"/> (all on one grid), the chamfer
        /// <paramref name="width"/> wide in block units (clamped below half a micro cell). Width 0 → <see cref="Plain"/>.</summary>
        public static List<ShapeFacet> Build(IReadOnlyList<SlotBox> boxes, float width)
        {
            if (width <= 0f || boxes.Count == 0)
            {
                return Plain(boxes);
            }

            var solid = new Solid(boxes);
            float w = Math.Min(width, MaxShare / solid.G);
            var result = new List<ShapeFacet>();
            AddFaces(solid, w, result);
            AddStrips(solid, w, result);
            AddCorners(solid, w, result);
            return result;
        }

        /// <summary><see cref="Build"/> when the result stays within <paramref name="maxFacets"/>, otherwise
        /// <see cref="Plain"/> — a form too fine to chamfer affordably keeps hard edges.</summary>
        public static List<ShapeFacet> BuildCapped(IReadOnlyList<SlotBox> boxes, float width, int maxFacets = MaxFacets)
        {
            var built = Build(boxes, width);
            return built.Count <= maxFacets ? built : Plain(boxes);
        }

        /// <summary>The six-direction index (0 +Y, 1 −Y, 2 +X, 3 −X, 4 +Z, 5 −Z — the mesher's order) of a signed axis.</summary>
        private static int DirOf(int axis, int sign) => axis == 1 ? (sign > 0 ? 0 : 1) : axis == 0 ? (sign > 0 ? 2 : 3) : (sign > 0 ? 4 : 5);

        private static int AxisOf(int dir) => dir < 2 ? 1 : dir < 4 ? 0 : 2;

        private static Vector3 Vec(int a0, float v0, int a1, float v1, int a2, float v2)
        {
            var p = new float[3];
            p[a0] = v0;
            p[a1] = v1;
            p[a2] = v2;
            return new Vector3(p[0], p[1], p[2]);
        }

        /// <summary>The tile fraction of a point projected along <paramref name="axis"/>: top/bottom by (x, z), the ±X sides
        /// by (z, y), the ±Z sides by (x, y) — the micro-box convention.</summary>
        private static Vector2 Uv(Vector3 p, int axis)
            => axis == 1 ? new Vector2(p.X, p.Z) : axis == 0 ? new Vector2(p.Z, p.Y) : new Vector2(p.X, p.Y);

        /// <summary>One face of the box x0..x1 × y0..y1 × z0..z1 in direction <paramref name="dir"/>, corners as
        /// <c>BlockShapeGeometry.MicroBox</c> lays them out.</summary>
        private static ShapeFacet BoxFace(int dir, float x0, float y0, float z0, float x1, float y1, float z1, int slot)
        {
            Vector3 a, b, c, d;
            switch (dir)
            {
                case 0: a = new Vector3(x0, y1, z0); b = new Vector3(x0, y1, z1); c = new Vector3(x1, y1, z1); d = new Vector3(x1, y1, z0); break;
                case 1: a = new Vector3(x0, y0, z1); b = new Vector3(x0, y0, z0); c = new Vector3(x1, y0, z0); d = new Vector3(x1, y0, z1); break;
                case 2: a = new Vector3(x1, y0, z0); b = new Vector3(x1, y1, z0); c = new Vector3(x1, y1, z1); d = new Vector3(x1, y0, z1); break;
                case 3: a = new Vector3(x0, y0, z1); b = new Vector3(x0, y1, z1); c = new Vector3(x0, y1, z0); d = new Vector3(x0, y0, z0); break;
                case 4: a = new Vector3(x1, y0, z1); b = new Vector3(x1, y1, z1); c = new Vector3(x0, y1, z1); d = new Vector3(x0, y0, z1); break;
                default: a = new Vector3(x0, y0, z0); b = new Vector3(x0, y1, z0); c = new Vector3(x1, y1, z0); d = new Vector3(x1, y0, z0); break;
            }

            int axis = AxisOf(dir);
            return new ShapeFacet(a, b, c, d, true, Uv(a, axis), Uv(b, axis), Uv(c, axis), Uv(d, axis), slot);
        }

        /// <summary>A strip, corner or cap: wound so its normal points along <paramref name="outward"/>, mapped along the
        /// outward's main axis (ties go to X, then Y — the cube bevel's rule).</summary>
        private static ShapeFacet Poly(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool isQuad, Vector3 outward, int slot)
        {
            Vector3 far = isQuad ? d : c;
            if (Vector3.Dot(Vector3.Cross(b - a, far - a), outward) < 0f)
            {
                if (isQuad)
                {
                    (b, d) = (d, b);
                }
                else
                {
                    (b, c) = (c, b);
                }
            }

            float ax = Math.Abs(outward.X), ay = Math.Abs(outward.Y), az = Math.Abs(outward.Z);
            int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
            return new ShapeFacet(a, b, c, d, isQuad, Uv(a, axis), Uv(b, axis), Uv(c, axis), isQuad ? Uv(d, axis) : Vector2.Zero, slot);
        }

        // ---------------------------------------------------------------- faces

        private static void AddFaces(Solid s, float w, List<ShapeFacet> result)
        {
            int g = s.G;
            for (int dir = 0; dir < 6; dir++)
            {
                int n = AxisOf(dir);
                int ua = n == 1 ? 0 : n == 0 ? 2 : 0, va = n == 1 ? 2 : 1;
                for (int layer = 0; layer < g; layer++)
                {
                    var used = new bool[g * g];
                    for (int v = 0; v < g; v++)
                    {
                        for (int u = 0; u < g; u++)
                        {
                            int flags = used[(v * g) + u] ? -1 : s.FaceFlags(dir, n, ua, va, layer, u, v);
                            if (flags >= 0)
                            {
                                MergeFace(s, w, result, used, dir, n, ua, va, layer, u, v, flags);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Grows a face rectangle from (u, v) along u while the side flags along v agree, then along v while each
        /// new row agrees at both ends, and emits it pulled in along its cut sides.</summary>
        private static void MergeFace(Solid s, float w, List<ShapeFacet> result, bool[] used, int dir, int n, int ua, int va,
            int layer, int u0, int v0, int flags)
        {
            int g = s.G, slot = s.SlotAt(Cell(n, layer, ua, u0, va, v0));
            int Flags(int u, int v)
            {
                int f = used[(v * g) + u] ? -1 : s.FaceFlags(dir, n, ua, va, layer, u, v);
                return f >= 0 && s.SlotAt(Cell(n, layer, ua, u, va, v)) == slot ? f : -1;
            }

            int u1 = u0 + 1;
            while (u1 < g)
            {
                int f = Flags(u1, v0);
                if (f < 0 || (f & 12) != (flags & 12))
                {
                    break;
                }

                u1++;
            }

            int uMinus = flags & 1, uPlus = Flags(u1 - 1, v0) & 2, vPlus = flags & 8;
            int v1 = v0 + 1;
            while (v1 < g && RowMatches(Flags, u0, u1, v1, uMinus, uPlus, out int rowVPlus))
            {
                vPlus = rowVPlus;
                v1++;
            }

            for (int v = v0; v < v1; v++)
            {
                for (int u = u0; u < u1; u++)
                {
                    used[(v * g) + u] = true;
                }
            }

            float gf = g;
            float plane = (dir & 1) == 0 ? (layer + 1) / gf : layer / gf;
            if ((flags & BorderFace) != 0)
            {
                EmitBorderFace(result, dir, n, ua, va, plane, new[] { u0 / gf, u1 / gf, v0 / gf, v1 / gf }, w,
                    uMinus != 0, uPlus != 0, (flags & 4) != 0, vPlus != 0, slot);
                return;
            }

            float uLo = (u0 / gf) + (uMinus != 0 ? w : 0f), uHi = (u1 / gf) - (uPlus != 0 ? w : 0f);
            float vLo = (v0 / gf) + ((flags & 4) != 0 ? w : 0f), vHi = (v1 / gf) - (vPlus != 0 ? w : 0f);
            var lo = Vec(n, plane, ua, uLo, va, vLo);
            var hi = Vec(n, plane, ua, uHi, va, vHi);
            result.Add(BoxFace(dir, lo.X, lo.Y, lo.Z, hi.X, hi.Y, hi.Z, slot));
        }

        /// <summary>
        /// A face on the block cell's border: never pulled in (the neighbouring block may continue it), but where an edge
        /// that stands on the border is cut — both in-plane sides of a corner may be chamfered — the strip runs flush into
        /// the border plane and the face loses that corner. Drawn as up to three quads: a trapezoid along each cut side
        /// (v−, v+) and the band between. <paramref name="r"/> = u0, u1, v0, v1.
        /// </summary>
        private static void EmitBorderFace(List<ShapeFacet> result, int dir, int n, int ua, int va, float plane, float[] r, float w,
            bool uMinus, bool uPlus, bool vMinus, bool vPlus, int slot)
        {
            bool c00 = uMinus && vMinus, c10 = uPlus && vMinus, c01 = uMinus && vPlus, c11 = uPlus && vPlus;
            if (!c00 && !c10 && !c01 && !c11)
            {
                var lo = Vec(n, plane, ua, r[0], va, r[2]);
                var hi = Vec(n, plane, ua, r[1], va, r[3]);
                result.Add(BoxFace(dir, lo.X, lo.Y, lo.Z, hi.X, hi.Y, hi.Z, slot));
                return;
            }

            var outward = Vec(n, (dir & 1) == 0 ? 1f : -1f, ua, 0f, va, 0f);
            Vector3 P(float u, float v) => Vec(n, plane, ua, u, va, v);
            float vb = c00 || c10 ? r[2] + w : r[2], vt = c01 || c11 ? r[3] - w : r[3];
            if (c00 || c10)
            {
                result.Add(Poly(P(r[0] + (c00 ? w : 0f), r[2]), P(r[1] - (c10 ? w : 0f), r[2]), P(r[1], vb), P(r[0], vb), true, outward, slot));
            }

            result.Add(Poly(P(r[0], vb), P(r[1], vb), P(r[1], vt), P(r[0], vt), true, outward, slot));
            if (c01 || c11)
            {
                result.Add(Poly(P(r[0], vt), P(r[1], vt), P(r[1] - (c11 ? w : 0f), r[3]), P(r[0] + (c01 ? w : 0f), r[3]), true, outward, slot));
            }
        }

        private static bool RowMatches(Func<int, int, int> flags, int u0, int u1, int v, int uMinus, int uPlus, out int vPlus)
        {
            vPlus = 0;
            for (int u = u0; u < u1; u++)
            {
                int f = flags(u, v);
                if (f < 0)
                {
                    return false;
                }

                if (u == u0)
                {
                    vPlus = f & 8;
                    if ((f & 1) != uMinus)
                    {
                        return false;
                    }
                }
                else if ((f & 8) != vPlus)
                {
                    return false;
                }

                if (u == u1 - 1 && (f & 2) != uPlus)
                {
                    return false;
                }
            }

            return true;
        }

        private static (int X, int Y, int Z) Cell(int n, int layer, int ua, int u, int va, int v)
        {
            var c = new int[3];
            c[n] = layer;
            c[ua] = u;
            c[va] = v;
            return (c[0], c[1], c[2]);
        }

        // ---------------------------------------------------------------- strips and their end caps

        private static void AddStrips(Solid s, float w, List<ShapeFacet> result)
        {
            for (int axisA = 0; axisA < 3; axisA++)
            {
                for (int axisB = axisA + 1; axisB < 3; axisB++)
                {
                    for (int sa = -1; sa <= 1; sa += 2)
                    {
                        for (int sb = -1; sb <= 1; sb += 2)
                        {
                            AddStripsOfEdge(s, w, result, new Edge(axisA, sa, axisB, sb));
                        }
                    }
                }
            }
        }

        private static void AddStripsOfEdge(Solid s, float w, List<ShapeFacet> result, Edge e)
        {
            int g = s.G;
            for (int ia = 0; ia < g; ia++)
            {
                for (int ib = 0; ib < g; ib++)
                {
                    int c = 0;
                    while (c < g)
                    {
                        var cell = e.Cell(ia, ib, c);
                        if (!s.HasEdge(cell, e))
                        {
                            c++;
                            continue;
                        }

                        int slot = s.SlotAt(cell), c1 = c + 1;
                        while (c1 < g && s.HasEdge(e.Cell(ia, ib, c1), e) && s.SlotAt(e.Cell(ia, ib, c1)) == slot)
                        {
                            c1++;
                        }

                        EmitStrip(s, w, result, e, ia, ib, c, c1, slot);
                        c = c1;
                    }
                }
            }
        }

        private static void EmitStrip(Solid s, float w, List<ShapeFacet> result, Edge e, int ia, int ib, int c0, int c1, int slot)
        {
            float g = s.G;
            float aMain = (ia + (e.Sa > 0 ? 1 : 0)) / g, aInset = aMain - (e.Sa * w);
            float bMain = (ib + (e.Sb > 0 ? 1 : 0)) / g, bInset = bMain - (e.Sb * w);
            var first = e.Cell(ia, ib, c0);
            var last = e.Cell(ia, ib, c1 - 1);
            bool lowCut = s.MayCut(first, DirOf(e.C, -1)), highCut = s.MayCut(last, DirOf(e.C, 1));
            float tLo = (c0 / g) + (lowCut ? w : 0f), tHi = (c1 / g) - (highCut ? w : 0f);

            var pAlo = Vec(e.A, aMain, e.B, bInset, e.C, tLo);
            var pAhi = Vec(e.A, aMain, e.B, bInset, e.C, tHi);
            var pBhi = Vec(e.B, bMain, e.A, aInset, e.C, tHi);
            var pBlo = Vec(e.B, bMain, e.A, aInset, e.C, tLo);
            result.Add(Poly(pAlo, pAhi, pBhi, pBlo, true, Vec(e.A, e.Sa, e.B, e.Sb, e.C, 0f), slot));

            // A strip that stops flush against a micro cell of the form which does not carry the edge on: that cell's face
            // shows through the end of the cut — close it.
            if (!lowCut && c0 > 0 && s.Filled(e.Cell(ia, ib, c0 - 1)) && !s.HasEdge(e.Cell(ia, ib, c0 - 1), e))
            {
                AddCap(result, e, aMain, aInset, bMain, bInset, c0 / g, 1f, s.SlotAt(e.Cell(ia, ib, c0 - 1)));
            }

            if (!highCut && c1 < s.G && s.Filled(e.Cell(ia, ib, c1)) && !s.HasEdge(e.Cell(ia, ib, c1), e))
            {
                AddCap(result, e, aMain, aInset, bMain, bInset, c1 / g, -1f, s.SlotAt(e.Cell(ia, ib, c1)));
            }
        }

        private static void AddCap(List<ShapeFacet> result, Edge e, float aMain, float aInset, float bMain, float bInset,
            float t, float sign, int slot)
        {
            var p0 = Vec(e.A, aMain, e.B, bInset, e.C, t);
            var p1 = Vec(e.A, aInset, e.B, bMain, e.C, t);
            var p2 = Vec(e.A, aMain, e.B, bMain, e.C, t);
            result.Add(Poly(p0, p1, p2, Vector3.Zero, false, Vec(e.C, sign, e.A, 0f, e.B, 0f), slot));
        }

        // ---------------------------------------------------------------- corners

        private static void AddCorners(Solid s, float w, List<ShapeFacet> result)
        {
            int g = s.G;
            float gf = g;
            for (int y = 0; y < g; y++)
            {
                for (int z = 0; z < g; z++)
                {
                    for (int x = 0; x < g; x++)
                    {
                        var cell = (x, y, z);
                        if (!s.Filled(cell))
                        {
                            continue;
                        }

                        for (int corner = 0; corner < 8; corner++)
                        {
                            int sx = (corner & 1) != 0 ? 1 : -1, sy = (corner & 2) != 0 ? 1 : -1, sz = (corner & 4) != 0 ? 1 : -1;
                            if (!s.MayCut(cell, DirOf(0, sx)) || !s.MayCut(cell, DirOf(1, sy)) || !s.MayCut(cell, DirOf(2, sz)))
                            {
                                continue;
                            }

                            float xm = (x + (sx > 0 ? 1 : 0)) / gf, ym = (y + (sy > 0 ? 1 : 0)) / gf, zm = (z + (sz > 0 ? 1 : 0)) / gf;
                            float xi = xm - (sx * w), yi = ym - (sy * w), zi = zm - (sz * w);
                            result.Add(Poly(new Vector3(xm, yi, zi), new Vector3(xi, ym, zi), new Vector3(xi, yi, zm), Vector3.Zero, false,
                                new Vector3(sx, sy, sz), s.SlotAt(cell)));
                        }
                    }
                }
            }
        }

        /// <summary>A convex edge kind: the edge between the faces toward <see cref="A"/>·<see cref="Sa"/> and
        /// <see cref="B"/>·<see cref="Sb"/>, running along axis <see cref="C"/>.</summary>
        private readonly struct Edge
        {
            public Edge(int a, int sa, int b, int sb)
            {
                A = a;
                Sa = sa;
                B = b;
                Sb = sb;
                C = 3 - a - b;
            }

            public int A { get; }

            public int Sa { get; }

            public int B { get; }

            public int Sb { get; }

            public int C { get; }

            public (int X, int Y, int Z) Cell(int ia, int ib, int ic)
            {
                var c = new int[3];
                c[A] = ia;
                c[B] = ib;
                c[C] = ic;
                return (c[0], c[1], c[2]);
            }
        }

        /// <summary>The micro grid of the union, with the slot of each filled cell.</summary>
        private sealed class Solid
        {
            private readonly int[] _slot;

            public Solid(IReadOnlyList<SlotBox> boxes)
            {
                G = Math.Max(1, boxes[0].Box.Grid);
                _slot = new int[G * G * G];
                foreach (var sb in boxes)
                {
                    var b = sb.Box;
                    for (int y = Math.Max(0, b.Y0); y < Math.Min(G, b.Y1); y++)
                    {
                        for (int z = Math.Max(0, b.Z0); z < Math.Min(G, b.Z1); z++)
                        {
                            for (int x = Math.Max(0, b.X0); x < Math.Min(G, b.X1); x++)
                            {
                                _slot[CustomShape.IndexOf(x, y, z, G)] = Math.Max(1, sb.Slot);
                            }
                        }
                    }
                }
            }

            public int G { get; }

            private bool Inside(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < G && y < G && z < G;

            public bool Filled((int X, int Y, int Z) c) => Inside(c.X, c.Y, c.Z) && _slot[CustomShape.IndexOf(c.X, c.Y, c.Z, G)] != 0;

            public int SlotAt((int X, int Y, int Z) c) => Filled(c) ? _slot[CustomShape.IndexOf(c.X, c.Y, c.Z, G)] : 0;

            /// <summary>The face toward <paramref name="dir"/> shows: the neighbouring micro cell is empty or outside.</summary>
            public bool Open((int X, int Y, int Z) c, int dir) => !Filled((c.X + Dx[dir], c.Y + Dy[dir], c.Z + Dz[dir]));

            /// <summary>The face may be chamfered: it shows and does not lie on the block cell's border.</summary>
            public bool MayCut((int X, int Y, int Z) c, int dir)
            {
                int nx = c.X + Dx[dir], ny = c.Y + Dy[dir], nz = c.Z + Dz[dir];
                return Inside(nx, ny, nz) && _slot[CustomShape.IndexOf(nx, ny, nz, G)] == 0;
            }

            public bool HasEdge((int X, int Y, int Z) c, Edge e)
                => Filled(c) && MayCut(c, DirOf(e.A, e.Sa)) && MayCut(c, DirOf(e.B, e.Sb));

            /// <summary>−1 when the cell shows no face toward <paramref name="dir"/>; else bits for the in-plane sides that may
            /// be chamfered: 1 = u−, 2 = u+, 4 = v−, 8 = v+ — on an inner face the sides it is pulled in on, on a face on the
            /// cell's border (<see cref="BorderFace"/> set) the sides whose corners a standing strip cuts off.</summary>
            public int FaceFlags(int dir, int n, int ua, int va, int layer, int u, int v)
            {
                var c = Cell(n, layer, ua, u, va, v);
                if (!Filled(c) || !Open(c, dir))
                {
                    return -1;
                }

                int flags = MayCut(c, dir) ? 0 : BorderFace;
                flags |= MayCut(c, DirOf(ua, -1)) ? 1 : 0;
                flags |= MayCut(c, DirOf(ua, 1)) ? 2 : 0;
                flags |= MayCut(c, DirOf(va, -1)) ? 4 : 0;
                flags |= MayCut(c, DirOf(va, 1)) ? 8 : 0;
                return flags;
            }
        }
    }
}

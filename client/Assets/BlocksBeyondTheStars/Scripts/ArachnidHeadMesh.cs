// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pyramid heads of the arachnid plan (#2009), built as flat-shaded meshes from the tiers in
    /// <see cref="ArachnidRules.HeadTiers"/>: one square frustum per tier, stacked, apex up. The mesh fills the unit
    /// box (x, z in −0.5..0.5, y in 0..1); the builder scales it to the head. One mesh per shape, cached for the
    /// process — a handful of triangles, never freed on purpose.
    /// </summary>
    internal static class ArachnidHeadMesh
    {
        private static readonly Dictionary<CreatureHeadShape, Mesh> Cache = new Dictionary<CreatureHeadShape, Mesh>();

        /// <summary>The mesh of a pyramid shape; null for the box (which is a primitive cube).</summary>
        public static Mesh For(CreatureHeadShape shape)
        {
            if (!ArachnidRules.IsPyramid(shape))
            {
                return null;
            }

            if (Cache.TryGetValue(shape, out var cached) && cached != null)
            {
                return cached;
            }

            var mesh = Build(shape);
            Cache[shape] = mesh;
            return mesh;
        }

        private static Mesh Build(CreatureHeadShape shape)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var tiers = ArachnidRules.HeadTiers(shape);
            for (int i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                Tier(verts, tris, t.Y0, t.Y1, t.Bottom * 0.5f, t.Top * 0.5f, cap: i == tiers.Count - 1 && t.Top > 0f);
            }

            // The base, facing down.
            float b = tiers.Count > 0 ? tiers[0].Bottom * 0.5f : 0.5f;
            Quad(verts, tris, new Vector3(-b, 0f, b), new Vector3(-b, 0f, -b), new Vector3(b, 0f, -b), new Vector3(b, 0f, b));

            var mesh = new Mesh { name = "ArachnidHead_" + shape };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // every face has its own vertices, so the normals come out flat
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One square frustum from <paramref name="y0"/> to <paramref name="y1"/> with half-widths
        /// <paramref name="r0"/> (bottom) and <paramref name="r1"/> (top); a top of 0 is an apex, otherwise the flat top is
        /// capped when <paramref name="cap"/> is set (a tier that carries another tier needs no cap).</summary>
        private static void Tier(List<Vector3> verts, List<int> tris, float y0, float y1, float r0, float r1, bool cap)
        {
            // Corners counter-clockwise seen from above: +x+z, −x+z, −x−z, +x−z.
            Vector3[] lo = { new Vector3(r0, y0, r0), new Vector3(-r0, y0, r0), new Vector3(-r0, y0, -r0), new Vector3(r0, y0, -r0) };
            Vector3[] hi = { new Vector3(r1, y1, r1), new Vector3(-r1, y1, r1), new Vector3(-r1, y1, -r1), new Vector3(r1, y1, -r1) };
            for (int s = 0; s < 4; s++)
            {
                int n = (s + 1) % 4;
                if (r1 <= 0f)
                {
                    Tri(verts, tris, lo[s], new Vector3(0f, y1, 0f), lo[n]);
                }
                else
                {
                    Quad(verts, tris, lo[s], hi[s], hi[n], lo[n]);
                }
            }

            if (cap && r1 > 0f)
            {
                Quad(verts, tris, hi[0], hi[1], hi[2], hi[3]);
            }
        }

        private static void Tri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        private static void Quad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
    }
}

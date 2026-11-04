// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using BodyPaint = BlocksBeyondTheStars.Shared.State.BodyPaint;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The painted segment meshes of the body paint (#874): a painted part's cubes swap onto these unit-cube meshes, whose
    /// UVs project the part's atlas (<see cref="BodyPaintKit"/>) planar per axis over the row's rest-pose box — every side
    /// face its own chunk, read as painted (#2750), the helmet frame's lips continuing their strips (#1776).
    /// <para>
    /// #3178: the editor has no top or bottom faces, and the torso's and limbs' tops and bottoms used to sit on the atlas's
    /// solid pad — the plain base tint, blue on a default suit, however completely the part was painted: the shoulder tops,
    /// a strip under the chest, a ring at the wrist (the hand is wider than the forearm), a wedge at a bent elbow. Now such a
    /// cap is four triangles, one per side edge, each continuing that side face's edge row inward — the colour of every
    /// pixel along the edge runs straight on round the corner, so the cap wears the painting and meets all four sides.
    /// </para>
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>A painted mesh under construction: the box it is projected over and its vertex streams.</summary>
        private sealed class PaintMeshBuild
        {
            public Vector3 Center, Size, BbMin, BbMax, Ext;
            public int Part;
            public int[] Chunks;
            public readonly List<Vector3> Verts = new List<Vector3>(40);
            public readonly List<Vector3> Normals = new List<Vector3>(40);
            public readonly List<Vector2> Uvs = new List<Vector2>(40);
            public readonly List<int> Tris = new List<int>(48);

            /// <summary>A unit-cube point's rest-pose position in the part frame.</summary>
            public Vector3 At(Vector3 unit) => Center + Vector3.Scale(unit, Size);
        }

        /// <summary>Which atlas chunk each cube-face direction samples (index: +x, −x, +y, −y, +z, −z; −1 = no chunk: a
        /// top or bottom of the torso or a limb continues its side faces' edge rows (#3178), the helmet's open front is its
        /// lip or the solid pad). Chunk indices are canvas-global (right limb = row 1 → +4). The figure faces +z, so its
        /// own left is −x.</summary>
        private static int[] FaceChunks(int part, int row)
        {
            if (part == BodyPaint.Helmet)
            {
                // Strip: left | back | right | chin | top — walking round the head as you see it (#2750). No front —
                // the helmet is open there.
                return new[] { 2, 0, 4, 3, -1, 1 };
            }

            int b = row * 4; // arms/legs: right limb chunks live in canvas row 1
            if (part == BodyPaint.Torso || row == 0)
            {
                // Front | Left(−x) | Back | Right(+x): standing in front of the figure, the strip's right edge runs on
                // round its left side, then the back, then its right side (#2750) — edge-continuous as you look at it.
                // The left limb's "outer" side is −x, matching.
                return new[] { b + 3, b + 1, -1, -1, b + 0, b + 2 };
            }

            // Right limb: outer is +x, inner −x — the same labels on the mirrored sides.
            return new[] { b + 1, b + 3, -1, -1, b + 0, b + 2 };
        }

        // Per cube face (order +x, −x, +y, −y, +z, −z): outward normal, the direction canvas-u grows in
        // (r), and the direction canvas-v grows in (w). Every face reads as someone outside sees it (#2750): u to the
        // observer's RIGHT, v up — the top as seen from in front of the figure (its far edge up), the bottom from
        // below with the front up. In Unity's left-handed frame that is r × w = −normal (right × up = the view
        // direction). The painting used to use r × w = +normal, the mirror view: canvas-left landed on the observer's
        // right and text read backwards. The winding below is the natural BL → TL → TR one for these corners.
        private static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] FaceR = { Vector3.forward, Vector3.back, Vector3.left, Vector3.left, Vector3.left, Vector3.right };
        private static readonly Vector3[] FaceW = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        /// <summary>#3178: the side faces round a cap, one triangle each (+x, −x, +z, −z).</summary>
        private static readonly int[] CapSides = { 0, 1, 4, 5 };

        /// <summary>The painted unit-cube mesh of one body-paint segment (<see cref="BuildPaintedMesh"/>, no helmet lip):
        /// public so the EditMode tests can pin that every painted face reads unmirrored (#2750) and that the caps continue
        /// their sides (#3178). The caller owns the mesh.</summary>
        public static Mesh PaintedSegmentMesh(int part, int row, Vector3 center, Vector3 size, Vector3 bbMin, Vector3 bbMax)
            => BuildPaintedMesh(center, size, bbMin, bbMax, part, row);

        /// <summary>Builds a unit-cube mesh for one segment whose UVs project the part's atlas planar per axis: painted faces
        /// map the corner's position within the row's bounding box into their chunk's UV rect; a top or bottom without a
        /// chunk is a cap continuing its sides (#3178); the helmet's open front is its lip (#1776) or the solid pad. Faces
        /// are laid out in face order — a side is one quad (4 vertices), a cap four triangles (12 vertices).</summary>
        private static Mesh BuildPaintedMesh(Vector3 center, Vector3 size, Vector3 bbMin, Vector3 bbMax, int part, int row,
            int frontChunk = -1, int lip = LipNone)
        {
            var m = new PaintMeshBuild
            {
                Center = center, Size = size, BbMin = bbMin, BbMax = bbMax, Ext = bbMax - bbMin, Part = part,
                Chunks = FaceChunks(part, row),
            };

            for (int f = 0; f < 6; f++)
            {
                int chunk = m.Chunks[f];
                if (chunk < 0 && (f == 2 || f == 3))
                {
                    AddPaintedCap(m, f);
                    continue;
                }

                bool lipFace = f == 4 && chunk < 0 && frontChunk >= 0 && lip != LipNone; // #1776: a helmet bar's front lip
                AddPaintedQuad(m, f, lipFace ? frontChunk : chunk, lipFace ? lip : LipNone);
            }

            var mesh = new Mesh
            {
                vertices = m.Verts.ToArray(),
                normals = m.Normals.ToArray(),
                uv = m.Uvs.ToArray(),
                triangles = m.Tris.ToArray(),
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One face as a quad: its chunk projected planar, the lip band past its strip's edge, or the solid pad.</summary>
        private static void AddPaintedQuad(PaintMeshBuild m, int f, int chunk, int lip)
        {
            Vector3 n = FaceN[f], r = FaceR[f], w = FaceW[f];
            Vector3 c = n * 0.5f;
            // BL, TL, TR, BR in the face's canvas orientation (u right, v up).
            var corners = new[]
            {
                c - r * 0.5f - w * 0.5f,
                c - r * 0.5f + w * 0.5f,
                c + r * 0.5f + w * 0.5f,
                c + r * 0.5f - w * 0.5f,
            };

            Rect rect = chunk >= 0 ? BodyPaintKit.ChunkRect(m.Part, chunk) : BodyPaintKit.SolidRect(m.Part);
            int b0 = m.Verts.Count;
            for (int i = 0; i < 4; i++)
            {
                m.Verts.Add(corners[i]);
                m.Normals.Add(n);
                m.Uvs.Add(chunk < 0
                    ? new Vector2(i <= 1 ? rect.xMin : rect.xMax, (i == 0 || i == 3) ? rect.yMin : rect.yMax) // the pad: non-degenerate
                    : QuadUv(m, f, rect, m.At(corners[i]), lip));
            }

            // Winding: BL→TL→TR / BL→TR→BR — clockwise as the observer sees the face, Unity's front face. (The
            // mirrored basis before #2750 needed the reversed order, which was the tell that it was mirrored.)
            m.Tris.Add(b0);
            m.Tris.Add(b0 + 1);
            m.Tris.Add(b0 + 2);
            m.Tris.Add(b0);
            m.Tris.Add(b0 + 2);
            m.Tris.Add(b0 + 3);
        }

        /// <summary>The atlas UV of a painted quad's corner at <paramref name="p"/> (rest pose, part frame).</summary>
        private static Vector2 QuadUv(PaintMeshBuild m, int f, Rect rect, Vector3 p, int lip)
        {
            float u, vv;
            if (lip != LipNone)
            {
                // The lip re-samples a band past its strip's front edge: the outer corner sits ON the edge,
                // the inner edge (against the face) LipBand further in — so the painting wraps round.
                float xf = m.Size.x > 0f ? (p.x - (m.Center.x - m.Size.x * 0.5f)) / m.Size.x : 0.5f; // 0 = −x side
                float yf = m.Size.y > 0f ? (p.y - (m.Center.y - m.Size.y * 0.5f)) / m.Size.y : 0.5f; // 0 = bottom
                switch (lip)
                {
                    case LipOuterRight: u = 1f - (1f - xf) * LipBand; vv = YFrac(p, m.BbMin, m.Ext); break;   // right bar: outer = +x
                    case LipOuterLeft: u = xf * LipBand; vv = YFrac(p, m.BbMin, m.Ext); break;                // left bar: outer = −x
                    case LipOuterTop: u = 1f - XFrac(p, m.BbMin, m.Ext); vv = 1f - yf * LipBand; break;       // chin: outer = bottom
                    default: u = 1f - XFrac(p, m.BbMin, m.Ext); vv = (1f - yf) * LipBand; break;              // top: outer = top
                }
            }
            else
            {
                // Planar projection: the corner's rest-pose position in the part frame, normalised within the row's
                // bounding box along the two axes this face spreads over.
                u = FaceU(m, f, p);
                vv = FaceV(m, f, p);
            }

            return new Vector2(rect.xMin + Mathf.Clamp01(u) * rect.width, rect.yMin + Mathf.Clamp01(vv) * rect.height);
        }

        /// <summary>Canvas u of face <paramref name="f"/> at <paramref name="p"/>: along FaceR, to the observer's right (#2750).</summary>
        private static float FaceU(PaintMeshBuild m, int f, Vector3 p) => f switch
        {
            0 => Mathf.InverseLerp(m.BbMin.z, m.BbMax.z, p.z), // +x: u → +z
            1 => Mathf.InverseLerp(m.BbMax.z, m.BbMin.z, p.z), // −x: u → −z
            5 => XFrac(p, m.BbMin, m.Ext),                     // −z: u → +x
            _ => 1f - XFrac(p, m.BbMin, m.Ext),                // ±y and +z: u → −x
        };

        /// <summary>Canvas v of face <paramref name="f"/> at <paramref name="p"/>: along FaceW, up as the observer sees it.</summary>
        private static float FaceV(PaintMeshBuild m, int f, Vector3 p) => f switch
        {
            2 => Mathf.InverseLerp(m.BbMax.z, m.BbMin.z, p.z), // +y: v → −z (the far edge up, seen from in front)
            3 => Mathf.InverseLerp(m.BbMin.z, m.BbMax.z, p.z), // −y: v → +z (the front up, seen from below)
            _ => YFrac(p, m.BbMin, m.Ext),
        };

        /// <summary>
        /// #3178: a top (<paramref name="f"/> = 2) or bottom (3) without a chunk — four triangles from the cap's centre,
        /// one to each side edge, sampling that side's chunk along the very texel row the side shows right at this edge
        /// (<see cref="BodyPaintEdges.EdgeTexel"/>), with the side's own u. So each pixel of the edge runs straight on
        /// inward: the shoulder top continues the shoulder's sides, the wrist ring the hand's top row, the elbow wedge the
        /// row at the elbow.
        /// </summary>
        private static void AddPaintedCap(PaintMeshBuild m, int f)
        {
            Vector3 n = FaceN[f];
            float y = n.y * 0.5f;
            var centre = new Vector3(0f, y, 0f);
            float edgeV = YFrac(m.At(centre), m.BbMin, m.Ext);
            int texel = BodyPaintEdges.EdgeTexel(edgeV, f == 2, BodyPaintKit.Face); // a top shows the row below its edge
            float rowV = BodyPaintEdges.TexelCentre(texel, BodyPaintKit.Face);
            float centreV = rowV + 0.25f / BodyPaintKit.Face; // still that row — keeps the triangle non-degenerate in UV

            foreach (int side in CapSides)
            {
                Vector3 mid = FaceN[side] * 0.5f + centre, along = FaceR[side] * 0.5f;
                Vector3 a = mid - along, b = mid + along;
                if (Vector3.Dot(Vector3.Cross(b - a, centre - a), n) < 0f)
                {
                    (a, b) = (b, a); // clockwise as the observer outside sees it, Unity's front face
                }

                int chunk = m.Chunks[side];
                Rect rect = chunk >= 0 ? BodyPaintKit.ChunkRect(m.Part, chunk) : BodyPaintKit.SolidRect(m.Part);
                AddCapVertex(m, a, n, rect, chunk >= 0 ? FaceU(m, side, m.At(a)) : 0f, rowV);
                AddCapVertex(m, b, n, rect, chunk >= 0 ? FaceU(m, side, m.At(b)) : 1f, rowV);
                AddCapVertex(m, centre, n, rect, chunk >= 0 ? FaceU(m, side, m.At(centre)) : 0.5f, centreV);
            }
        }

        private static void AddCapVertex(PaintMeshBuild m, Vector3 unit, Vector3 n, Rect rect, float u, float v)
        {
            m.Tris.Add(m.Verts.Count);
            m.Verts.Add(unit);
            m.Normals.Add(n);
            m.Uvs.Add(new Vector2(rect.xMin + Mathf.Clamp01(u) * rect.width, rect.yMin + Mathf.Clamp01(v) * rect.height));
        }

        private static float XFrac(Vector3 p, Vector3 bbMin, Vector3 ext) => ext.x > 0f ? (p.x - bbMin.x) / ext.x : 0.5f;

        private static float YFrac(Vector3 p, Vector3 bbMin, Vector3 ext) => ext.y > 0f ? (p.y - bbMin.y) / ext.y : 0.5f;
    }
}

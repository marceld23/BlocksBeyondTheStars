// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Rounded edges on the avatar's boxes, so the figure reads as soft as the rounded animals
    /// (<see cref="RoundedCube"/>) around it instead of being the last angular figure. The share is
    /// <see cref="GameLook.AvatarRoundShare"/>; at 0 nothing here runs and
    /// every part keeps Unity's primitive cube and the flat painted meshes.
    /// <para>
    /// <b>Plain parts</b> (<see cref="RoundLabPart"/>, from <c>AddCube</c> and the first-person hand): a rounded box with
    /// the SAME texture mapping per face as the primitive cube it replaces. The mapping is read from the primitive itself
    /// — on each face its UV is an affine function of the position — because the primitive's faces lay their UVs out
    /// turned and mirrored, and the suit textures must keep their orientation. <see cref="RoundedCube.Build"/> cannot be
    /// used: it expects <see cref="TiledCube"/>'s corner order. One mesh per quantised proportions, shared by every figure
    /// (players, other players, villagers).
    /// </para>
    /// <para>
    /// <b>Painted parts</b> (<see cref="BuildPaintedMesh"/>): every face becomes a grid whose UVs are taken at the point
    /// on the SHARP box, through the very projection the flat mesh uses — so a texel on the flat middle of a face stays
    /// exactly where it was, and only the strip one radius wide at a rim bends round the edge. The radius is capped at one
    /// texel of the 32-texel paint tile (<see cref="PaintRadius"/>), so at most one texel column or row bends. Tops and
    /// soles keep their four triangles of edge rows (#3178), cut into the same grid so the cap closes on the bent sides.
    /// </para>
    /// Display only, the same on every graphics tier; nothing is saved or sent.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>Parts thinner than this in the world (metres) keep the sharp cube: a pupil or a status light is a few
        /// centimetres of plate, and a radius on it would be a fraction of a millimetre for a dozen extra triangles.</summary>
        private const float RoundMinThickness = 0.012f;

        /// <summary>The shared rounded boxes, by quantised proportions and share (<see cref="RoundedBox"/>).</summary>
        private static readonly Dictionary<long, Mesh> RoundBoxes = new Dictionary<long, Mesh>();

        /// <summary>The primitive cube's texture mapping, three vectors per face (face order +x, −x, +y, −y, +z, −z): on
        /// face k the UV is [0] + [1]·p[(k+1)%3] + [2]·p[(k+2)%3] for a point p of the unit cube. Null until read.</summary>
        private static Vector2[] _cubeUvMap;

        /// <summary>Gives a cube part built from the primitive its rounded box (when the avatar rounding is
        /// on and the part is thick enough). <paramref name="scale"/> is the part's local scale; the part must already
        /// hang on its parent, whose scale tells its size in the world.</summary>
        internal static void RoundLabPart(GameObject go, Vector3 scale)
        {
            float share = GameLook.AvatarRoundShare;
            if (share <= 0f || go == null)
            {
                return;
            }

            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            var parent = go.transform.parent;
            var lossy = parent != null ? parent.lossyScale : Vector3.one;
            float thinnest = Mathf.Min(Mathf.Abs(scale.x * lossy.x), Mathf.Min(Mathf.Abs(scale.y * lossy.y), Mathf.Abs(scale.z * lossy.z)));
            if (thinnest < RoundMinThickness)
            {
                return;
            }

            var rounded = RoundedBox(filter.sharedMesh, scale, share);
            if (rounded != null)
            {
                RoundLod.Assign(filter, rounded, filter.sharedMesh, share); // far away the primitive it replaces
            }
        }

        /// <summary>
        /// The head under a drawn face. The pixel face is a flat plate over the head's whole front
        /// (<see cref="EnsureFacePlate"/>); on a fully rounded head its edges stood off the bend like a card stuck on, with
        /// a wedge of air behind them seen from the side. The face is the head's painting, so the head follows the paint
        /// rule while a face shows: its radius is capped at one face texel (1/32 of the head), the plate stays flat and
        /// sits flush. Without a drawn face (the stock eyes and mouth sit well inside the flat middle) the head is fully
        /// round again. Called whenever the face's visibility is reconciled.
        /// </summary>
        private void RoundHeadForFace(bool custom)
        {
            float share = GameLook.AvatarRoundShare;
            var filter = share > 0f && _head != null ? _head.GetComponent<MeshFilter>() : null;
            // Far away the head shows its sharp twin (RoundLod): what counts is the rounded mesh it has, not the one shown.
            var rounded = filter != null ? RoundLod.RoundOf(filter) ?? filter.sharedMesh : null;
            if (rounded == null || rounded.name != "AvatarRoundCube")
            {
                return; // a head RoundLabPart left sharp stays sharp
            }

            float headShare = custom ? Mathf.Min(share, 1f / FacePalette.Size) : share;
            var mesh = RoundedBox(rounded, _head.localScale, headShare);
            if (mesh != null)
            {
                RoundLod.Assign(filter, mesh, RoundLod.SharpOf(filter), headShare);
            }
        }

        /// <summary>Gives a painted segment its mesh (<paramref name="mesh"/>, rounded when the avatar rounding is
        /// on) and — for the far stage (<see cref="RoundLod"/>) — builds its flat twin, the painting on the sharp box as
        /// without rounding. Returns the twin (the caller owns and destroys it with the painting), null when the segment
        /// is flat anyway or the far stage is off.</summary>
        private static Mesh AssignPainted(PaintSeg seg, Mesh mesh, Vector3 bbMin, Vector3 bbMax, int part, int row)
        {
            var filter = seg.Go.GetComponent<MeshFilter>();
            float radius = PaintRadius(seg.Size, bbMin, bbMax);
            float thinnest = Mathf.Min(Mathf.Abs(seg.Size.x), Mathf.Min(Mathf.Abs(seg.Size.y), Mathf.Abs(seg.Size.z)));
            Mesh flat = radius > 0f && thinnest > 0f && RoundLod.On
                ? BuildPaintedMesh(seg.Center, seg.Size, bbMin, bbMax, part, row, seg.FrontChunk, seg.Lip, sharp: true)
                : null;
            RoundLod.Assign(filter, mesh, flat, thinnest > 0f ? radius / thinnest : 0f);
            return flat;
        }

        /// <summary>The face plate reaches a little deeper into the head (its front stays where it is), so the
        /// one-texel bend of the head's front edges (<see cref="RoundHeadForFace"/>) never opens a sliver behind the
        /// plate's rim.</summary>
        private static void LabFacePlateDepth(Transform plate)
        {
            if (GameLook.AvatarRoundShare <= 0f || plate == null)
            {
                return;
            }

            const float front = FacePlateZ + 0.025f, back = FacePlateZ - 0.045f;
            var p = plate.localPosition;
            plate.localPosition = new Vector3(p.x, p.y, (front + back) * 0.5f);
            var s = plate.localScale;
            plate.localScale = new Vector3(s.x, s.y, front - back);
        }

        /// <summary>The rounded unit box for a part of <paramref name="size"/>, mapped like <paramref name="primitive"/>
        /// (Unity's cube). Proportions are quantised to 1/255 of the longest side, as for the creatures' rounded parts, so a
        /// figure's few dozen parts share a handful of meshes and every figure shares them. Null when the primitive is not
        /// the 24-vertex cube this expects (the part then keeps it).</summary>
        private static Mesh RoundedBox(Mesh primitive, Vector3 size, float share)
        {
            if (_cubeUvMap == null && !ReadCubeUvs(primitive))
            {
                return null;
            }

            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= 0f)
            {
                return null;
            }

            int qx = Mathf.Clamp(Mathf.RoundToInt(size.x / longest * 255f), 1, 255);
            int qy = Mathf.Clamp(Mathf.RoundToInt(size.y / longest * 255f), 1, 255);
            int qz = Mathf.Clamp(Mathf.RoundToInt(size.z / longest * 255f), 1, 255);
            long key = qx | ((long)qy << 8) | ((long)qz << 16) | ((long)Mathf.Clamp(Mathf.RoundToInt(share * 100f), 0, 99) << 24);
            if (RoundBoxes.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var shape = new Vector3(qx / 255f, qy / 255f, qz / 255f);
            var build = new RoundBuild(shape, RoundedCube.Radius(shape, share));
            for (int face = 0; face < 6; face++)
            {
                int k = face / 2, i = (k + 1) % 3, j = (k + 2) % 3;
                Vector2 a = _cubeUvMap[face * 3], b = _cubeUvMap[(face * 3) + 1], c = _cubeUvMap[(face * 3) + 2];
                build.AddFace(face, p => a + (b * p[i]) + (c * p[j]));
            }

            var mesh = build.ToMesh("AvatarRoundCube");
            RoundBoxes[key] = mesh;
            return mesh;
        }

        /// <summary>Reads the primitive cube's texture mapping per face (<see cref="_cubeUvMap"/>). A face's four corners sit
        /// at (±½, ±½) in its two tangent axes, so the least-squares fit of uv = A + B·p_i + C·p_j is exact and closed:
        /// A is the mean UV, B = Σ uv·p_i and C = Σ uv·p_j.</summary>
        private static bool ReadCubeUvs(Mesh cube)
        {
            if (cube == null || cube.vertexCount != 24)
            {
                return false;
            }

            var pos = cube.vertices;
            var nrm = cube.normals;
            var uv = cube.uv;
            if (nrm.Length != 24 || uv.Length != 24)
            {
                return false;
            }

            var map = new Vector2[18];
            var count = new int[6];
            for (int v = 0; v < 24; v++)
            {
                int face = FaceOf(nrm[v]);
                int k = face / 2, i = (k + 1) % 3, j = (k + 2) % 3;
                map[face * 3] += uv[v] * 0.25f;
                map[(face * 3) + 1] += uv[v] * pos[v][i];
                map[(face * 3) + 2] += uv[v] * pos[v][j];
                count[face]++;
            }

            for (int face = 0; face < 6; face++)
            {
                if (count[face] != 4)
                {
                    return false;
                }
            }

            _cubeUvMap = map;
            return true;
        }

        /// <summary>The face (+x, −x, +y, −y, +z, −z = 0..5) a normal points out of.</summary>
        private static int FaceOf(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            int k = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
            return (k * 2) + (n[k] < 0f ? 1 : 0);
        }

        /// <summary>The edge radius of a painted segment of <paramref name="size"/> (0 = the flat mesh): the
        /// avatar's share of its thinnest side, but never more than one texel of the paint tile — a tile spans the row's
        /// rest-pose box on each axis, so a texel is that box's extent / 32, and the smallest of the three axes bounds every
        /// face. The arm's tile is only 0.2 m wide, so its segments (the hand among them) get a far smaller radius than
        /// the unpainted arm; the helmet's tile is wide enough for its full radius.</summary>
        private static float PaintRadius(Vector3 size, Vector3 bbMin, Vector3 bbMax)
        {
            float share = GameLook.AvatarRoundShare;
            if (share <= 0f)
            {
                return 0f;
            }

            var ext = bbMax - bbMin;
            float texel = Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) / BodyPaintKit.Face;
            return Mathf.Max(0f, Mathf.Min(RoundedCube.Radius(size, share), texel));
        }

        /// <summary>The rounded painted segment: the faces of <see cref="BuildPaintedMesh"/> (same chunks, lips, pad and
        /// caps), each a grid sampled at the sharp box (see the class note).</summary>
        private static Mesh BuildRoundedPaintedMesh(PaintMeshBuild m, float radius, int frontChunk, int lip)
        {
            var build = new RoundBuild(m.Size, radius);
            for (int f = 0; f < 6; f++)
            {
                int face = f;
                int chunk = m.Chunks[face];
                if (chunk < 0 && (face == 2 || face == 3))
                {
                    AddRoundedCap(m, build, face);
                    continue;
                }

                bool lipFace = face == 4 && chunk < 0 && frontChunk >= 0 && lip != LipNone; // #1776: a helmet bar's front lip
                int useChunk = lipFace ? frontChunk : chunk;
                int useLip = lipFace ? lip : LipNone;
                Rect rect = useChunk >= 0 ? BodyPaintKit.ChunkRect(m.Part, useChunk) : BodyPaintKit.SolidRect(m.Part);
                if (useChunk >= 0)
                {
                    build.AddFace(face, p => QuadUv(m, face, rect, m.At(p), useLip));
                }
                else
                {
                    // The pad, spread over the face as the flat quad's corners spread it (BL = xMin/yMin … TR = xMax/yMax).
                    build.AddFace(face, p => new Vector2(
                        rect.xMin + ((Vector3.Dot(p, FaceR[face]) + 0.5f) * rect.width),
                        rect.yMin + ((Vector3.Dot(p, FaceW[face]) + 0.5f) * rect.height)));
                }
            }

            return build.ToMesh("AvatarRoundPaint");
        }

        /// <summary>
        /// A rounded cap (<paramref name="f"/> = 2 top, 3 bottom): the four triangles of <see cref="AddPaintedCap"/> — each
        /// from a side edge to the centre, its UV that side's edge row — but every grid cell of the face is cut by the
        /// triangles (convex clipping in the cap's plane), so the cap has vertices wherever the bend needs them and the
        /// vertices on the diagonals are the same for both neighbours. Inside a triangle the flat cap's UV is affine in the
        /// position, so a clipped vertex takes it by barycentric weights: the flat middle shows exactly what it showed.
        /// </summary>
        private static void AddRoundedCap(PaintMeshBuild m, RoundBuild build, int f)
        {
            Vector3 n = FaceN[f];
            float y = n.y * 0.5f;
            var centre = new Vector3(0f, y, 0f);
            float edgeV = YFrac(m.At(centre), m.BbMin, m.Ext);
            int texel = BodyPaintEdges.EdgeTexel(edgeV, f == 2, BodyPaintKit.Face); // a top shows the row below its edge
            float rowV = BodyPaintEdges.TexelCentre(texel, BodyPaintKit.Face);
            float centreV = rowV + 0.25f / BodyPaintKit.Face; // still that row — keeps the triangle non-degenerate in UV
            float[] xs = build.Lines[0], zs = build.Lines[2];
            var poly = new List<Vector2>(10);
            var scratch = new List<Vector2>(10);

            foreach (int side in CapSides)
            {
                Vector3 mid = FaceN[side] * 0.5f + centre, along = FaceR[side] * 0.5f;
                Vector3 a = mid - along, b = mid + along;
                if (Vector3.Dot(Vector3.Cross(b - a, centre - a), n) < 0f)
                {
                    (a, b) = (b, a); // as the flat cap orders them
                }

                int chunk = m.Chunks[side];
                Rect rect = chunk >= 0 ? BodyPaintKit.ChunkRect(m.Part, chunk) : BodyPaintKit.SolidRect(m.Part);
                Vector2 uvA = RectUv(rect, chunk >= 0 ? FaceU(m, side, m.At(a)) : 0f, rowV);
                Vector2 uvB = RectUv(rect, chunk >= 0 ? FaceU(m, side, m.At(b)) : 1f, rowV);
                Vector2 uvC = RectUv(rect, chunk >= 0 ? FaceU(m, side, m.At(centre)) : 0.5f, centreV);
                Vector2 pa = new Vector2(a.x, a.z), pb = new Vector2(b.x, b.z), pc = new Vector2(0f, 0f);

                for (int iz = 0; iz + 1 < zs.Length; iz++)
                {
                    for (int ix = 0; ix + 1 < xs.Length; ix++)
                    {
                        poly.Clear();
                        poly.Add(new Vector2(xs[ix], zs[iz]));
                        poly.Add(new Vector2(xs[ix + 1], zs[iz]));
                        poly.Add(new Vector2(xs[ix + 1], zs[iz + 1]));
                        poly.Add(new Vector2(xs[ix], zs[iz + 1]));
                        Clip(poly, scratch, pa, pb, pc);
                        Clip(poly, scratch, pb, pc, pa);
                        Clip(poly, scratch, pc, pa, pb);
                        if (poly.Count >= 3)
                        {
                            EmitCapPolygon(build, poly, y, n, pa, pb, pc, uvA, uvB, uvC);
                        }
                    }
                }
            }
        }

        /// <summary>One clipped cap polygon (convex, in the cap's x/z plane at height <paramref name="y"/>) as a fan, each
        /// vertex's UV from the flat triangle (a, b, c) it lies in.</summary>
        private static void EmitCapPolygon(RoundBuild build, List<Vector2> poly, float y, Vector3 n,
            Vector2 a, Vector2 b, Vector2 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            float denom = Cross2(b - a, c - a);
            if (Mathf.Abs(denom) < 1e-9f)
            {
                return;
            }

            int first = build.Verts.Count;
            foreach (var p in poly)
            {
                float wb = Cross2(p - a, c - a) / denom, wc = Cross2(b - a, p - a) / denom;
                build.Add(new Vector3(p.x, y, p.y), (uvA * (1f - wb - wc)) + (uvB * wb) + (uvC * wc));
            }

            for (int t = 1; t + 1 < poly.Count; t++)
            {
                Vector3 p0 = new Vector3(poly[0].x, y, poly[0].y), p1 = new Vector3(poly[t].x, y, poly[t].y);
                Vector3 p2 = new Vector3(poly[t + 1].x, y, poly[t + 1].y);
                float facing = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n);
                if (Mathf.Abs(facing) < 1e-12f)
                {
                    continue; // a sliver the clipping left on a diagonal
                }

                build.Tris.Add(first);
                build.Tris.Add(facing > 0f ? first + t : first + t + 1);
                build.Tris.Add(facing > 0f ? first + t + 1 : first + t);
            }
        }

        /// <summary>Clips a convex polygon to the half-plane on <paramref name="inside"/>'s side of the line e0 → e1
        /// (Sutherland–Hodgman), in place; points on the line stay.</summary>
        private static void Clip(List<Vector2> poly, List<Vector2> scratch, Vector2 e0, Vector2 e1, Vector2 inside)
        {
            float sign = Cross2(e1 - e0, inside - e0) >= 0f ? 1f : -1f;
            scratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                float dp = sign * Cross2(e1 - e0, p - e0), dq = sign * Cross2(e1 - e0, q - e0);
                if (dp >= 0f)
                {
                    scratch.Add(p);
                }

                if ((dp >= 0f) != (dq >= 0f))
                {
                    var cut = p + ((q - p) * (dp / (dp - dq)));
                    if ((cut - p).sqrMagnitude > 1e-14f && (cut - q).sqrMagnitude > 1e-14f)
                    {
                        scratch.Add(cut);
                    }
                }
            }

            poly.Clear();
            poly.AddRange(scratch);
        }

        private static float Cross2(Vector2 u, Vector2 v) => (u.x * v.y) - (u.y * v.x);

        private static Vector2 RectUv(Rect rect, float u, float v)
            => new Vector2(rect.xMin + Mathf.Clamp01(u) * rect.width, rect.yMin + Mathf.Clamp01(v) * rect.height);

        /// <summary>A rounded unit box under construction: the grid lines per axis and the vertex streams. Every point is
        /// given on the SHARP unit box and moved onto the rounded one here, so two faces that share an edge build it from the
        /// same numbers and close exactly.</summary>
        private sealed class RoundBuild
        {
            public readonly List<Vector3> Verts = new List<Vector3>(256);
            public readonly List<Vector3> Normals = new List<Vector3>(256);
            public readonly List<Vector2> Uvs = new List<Vector2>(256);
            public readonly List<int> Tris = new List<int>(1024);

            /// <summary>Per axis, where the grid lines run in unit coordinates: the two edges, the lines where the flat
            /// middle begins, and one line inside each bend (at half its angle, as in <see cref="RoundedCube"/>).</summary>
            public readonly float[][] Lines = new float[3][];

            private readonly Vector3 _size, _half;
            private readonly float _radius;

            public RoundBuild(Vector3 size, float radius)
            {
                _size = size;
                _half = size * 0.5f;
                _radius = radius;
                for (int a = 0; a < 3; a++)
                {
                    float bend = radius / size[a], mid = radius * 0.5858f / size[a];
                    Lines[a] = new[] { -0.5f, -0.5f + mid, -0.5f + bend, 0.5f - bend, 0.5f - mid, 0.5f };
                }
            }

            /// <summary>Adds the point of the rounded box over <paramref name="unit"/> (a point on the sharp unit box)
            /// with <paramref name="uv"/>; returns its index.</summary>
            public int Add(Vector3 unit, Vector2 uv)
            {
                var at = RoundedCube.Round(Vector3.Scale(unit, _size), _half, _radius, out Vector3 normal);
                Verts.Add(new Vector3(at.x / _size.x, at.y / _size.y, at.z / _size.z));
                Normals.Add(Vector3.Scale(normal, _size).normalized); // the part's scale turns it back into `normal`
                Uvs.Add(uv);
                return Verts.Count - 1;
            }

            /// <summary>One face (+x, −x, +y, −y, +z, −z = 0..5) as a grid over its two tangent axes, the UV of each
            /// point from <paramref name="uvOf"/> at the sharp box.</summary>
            public void AddFace(int face, System.Func<Vector3, Vector2> uvOf)
            {
                int k = face / 2, i = (k + 1) % 3, j = (k + 2) % 3;
                float side = (face & 1) == 0 ? 0.5f : -0.5f;
                float[] us = Lines[i], vs = Lines[j];
                int first = Verts.Count;
                for (int b = 0; b < vs.Length; b++)
                {
                    for (int a = 0; a < us.Length; a++)
                    {
                        var p = Vector3.zero;
                        p[k] = side;
                        p[i] = us[a];
                        p[j] = vs[b];
                        Add(p, uvOf(p));
                    }
                }

                // e_i × e_j = e_k for the cyclic axes, so the grid's own order faces out of a + face (Unity's front face
                // is the one whose (b − a) × (c − a) points at the viewer) and must be turned round on a − face.
                bool plus = side > 0f;
                for (int b = 0; b + 1 < vs.Length; b++)
                {
                    for (int a = 0; a + 1 < us.Length; a++)
                    {
                        int c00 = first + (b * us.Length) + a, c10 = c00 + 1, c01 = c00 + us.Length, c11 = c01 + 1;
                        if (plus)
                        {
                            Tris.Add(c00); Tris.Add(c10); Tris.Add(c11);
                            Tris.Add(c00); Tris.Add(c11); Tris.Add(c01);
                        }
                        else
                        {
                            Tris.Add(c00); Tris.Add(c11); Tris.Add(c10);
                            Tris.Add(c00); Tris.Add(c01); Tris.Add(c11);
                        }
                    }
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}

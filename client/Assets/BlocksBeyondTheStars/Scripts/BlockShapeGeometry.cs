// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Pure geometry for the non-cube building shapes (sphere, dome, pyramid, ramp, …). Produces a list of
    /// outward-facing polygons in the unit cell [0,1]^3 (cell-local), rotated by the placement yaw. The mesher
    /// (<see cref="ChunkMesher"/>) turns these into vertices/triangles + the usual per-vertex attribute streams,
    /// so a shaped block textures, tints and lights exactly like a cube. Winding matches the cube faces
    /// (CCW seen from outside: normal = cross(B-A, D-A)), so the back-face-culling atlas shader shows them.
    /// </summary>
    internal static class BlockShapeGeometry
    {
        /// <summary>One outward-facing polygon — a quad (4 corners) or a triangle (3). Corners are CCW from
        /// outside; the mesher emits tris (0,1,2)[,(0,2,3)].</summary>
        internal readonly struct Face
        {
            public readonly Vector3 A, B, C, D;
            public readonly bool IsQuad;

            /// <summary>Per-vertex texture coordinates as FRACTIONS of the block's atlas tile (0..1): a micro box of a
            /// player-designed form must show the slice of the material it actually covers, or an 8³ form renders as
            /// dozens of shrunken copies of the whole tile. <see cref="HasUv"/> is false only on a face that has not
            /// been through <see cref="Finish"/> yet — everything <see cref="Build"/> returns carries them.</summary>
            public readonly Vector2 UvA, UvB, UvC, UvD;
            public readonly bool HasUv;

            /// <summary>#1900: which piece of the form this face belongs to (<see cref="ShapePart"/>) and which way it
            /// looks in the form's own frame (<see cref="FaceSide"/>) — the keys of a block's texture slots. Every
            /// built-in form gets real texture coordinates too since #1900 (<see cref="Finish"/>): the slice of the
            /// tile each corner covers, so a table leg shows a thin slice of the wood instead of a whole plank tile and
            /// the bed stops showing a complete bed on every face.</summary>
            public readonly ShapePart Part;
            public readonly FaceSide Side;

            public Face(Vector3 a, Vector3 b, Vector3 c)
                : this(a, b, c, default, false, default, default, default, default, false, ShapePart.Body, FaceSide.Top)
            {
            }

            private Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool quad, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
                bool hasUv, ShapePart part, FaceSide side)
            {
                A = a; B = b; C = c; D = d; IsQuad = quad;
                UvA = ua; UvB = ub; UvC = uc; UvD = ud; HasUv = hasUv;
                Part = part; Side = side;
            }

            /// <summary>The outward normal (not normalised) — the same winding rule the mesher shades with.</summary>
            public Vector3 Normal => Vector3.Cross(B - A, (IsQuad ? D : C) - A);

            /// <summary>#1900: stamps the part, derives the side from the face's own-frame normal and — unless the face
            /// already carries texture coordinates (a player-designed micro box) — projects them from its corners along
            /// the dominant axis: top/bottom by X,Z, faces toward ±X by Z,height, faces toward ±Z by X,height (the
            /// micro-box convention). Called on the untransformed form, so yaw and tilt afterwards carry it along.</summary>
            public Face Finish(ShapePart part)
            {
                var n = Normal;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                int axis = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
                var side = axis == 1 ? (n.y >= 0f ? FaceSide.Top : FaceSide.Bottom) : FaceSide.Side;
                if (HasUv)
                {
                    return new Face(A, B, C, D, IsQuad, UvA, UvB, UvC, UvD, true, part, side);
                }

                Vector2 P(Vector3 p) => axis == 1 ? new Vector2(p.x, p.z) : axis == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                return new Face(A, B, C, D, IsQuad, P(A), P(B), P(C), IsQuad ? P(D) : default, true, part, side);
            }

            public Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
                : this(a, b, c, d, true, default, default, default, default, false, ShapePart.Body, FaceSide.Top)
            {
            }

            public Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
                : this(a, b, c, d, true, ua, ub, uc, ud, true, ShapePart.Body, FaceSide.Top)
            {
            }

            /// <summary>The same face with every corner moved through <paramref name="move"/> — the texture
            /// coordinates, part and side ride along, so a rotated form keeps its surface mapping.</summary>
            public Face Map(System.Func<Vector3, Vector3> move)
                => new Face(move(A), move(B), move(C), IsQuad ? move(D) : default, IsQuad, UvA, UvB, UvC, UvD, HasUv, Part, Side);
        }

        /// <summary>Builds the polygons for a shape index (see <see cref="BlockShape"/>), oriented by a yaw
        /// (0..3 quarter-turns about the vertical centre) THEN tilted so the shape's local +Y points to
        /// <paramref name="upFace"/> (0 = +Y = the original behaviour → yaw-only, unchanged). yaw × up-face give
        /// the full 24 cube orientations. Returns null for cube/unknown. <paramref name="cell"/> picks the cell of
        /// a player form that spans several blocks (#1961, <see cref="ShapeCode.CellOf"/>); 0 for everything else.</summary>
        public static List<Face> Build(int shapeIndex, int orientation, int upFace = 0, int cell = 0)
        {
            // Built once per (form, yaw, up-face) and then shared: the mesher asks for this on every shaped
            // cell of every remesh, and a player-designed form can be dozens of boxes. Callers must treat the
            // returned list as read-only (nothing has ever mutated it).
            int cacheKey = ((cell & 0xF) << 11) | (shapeIndex << 5) | ((upFace & 7) << 2) | (orientation & 3);
            if (_faceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var built = BuildUncached(shapeIndex, orientation, upFace, cell);
            _faceCache[cacheKey] = built;
            return built;
        }

        private static List<Face> BuildUncached(int shapeIndex, int orientation, int upFace, int cell)
        {
            var faces = new List<Face>();
            if (ShapeCode.IsCustomShape(shapeIndex))
            {
                // A player-designed form (#844): merged boxes from the registry snapshot. An id this client
                // has not received (or one that was wiped) has no geometry — the cell renders as a plain cube,
                // which is exactly what the server does when placing an unknown form.
                if (!_customVoxels.TryGetValue(shapeIndex, out string voxels))
                {
                    return null;
                }

                // A form over several blocks draws ONE of its cells here; a one-block form is its own cell 0. A
                // cell index the form does not have (a block left over from a wiped-and-reused slot) has no
                // geometry either → plain cube.
                string cellVoxels = CustomShape.CellVoxels(voxels, cell);
                if (cellVoxels.Length == 0)
                {
                    return null;
                }

                foreach (var box in CustomShape.Merge(cellVoxels))
                {
                    MicroBox(faces, box);
                }
            }
            else
            {
                switch ((BlockShape)shapeIndex)
                {
                case BlockShape.Slab: Box(faces, 0f, 0f, 0f, 1f, 0.5f, 1f); break;
                case BlockShape.Pyramid: Pyramid(faces); break;
                case BlockShape.Dome: Dome(faces); break;
                case BlockShape.Sphere: Sphere(faces); break;
                case BlockShape.Ramp: Ramp(faces); break;
                case BlockShape.Stairs: Stairs(faces); break;
                case BlockShape.Cone: Cone(faces); break;
                case BlockShape.Cylinder: Cylinder(faces); break;
                case BlockShape.Panel: Box(faces, 0f, 0f, 0f, 1f, 0.25f, 1f); break;             // thin floor/ceiling plate
                case BlockShape.Post: Box(faces, 0.3f, 0f, 0.3f, 0.7f, 1f, 0.7f); break;          // slim square column
                case BlockShape.Beam: Box(faces, 0f, 0.35f, 0.35f, 1f, 0.65f, 0.65f); break;      // bar along X (yaw → Z)
                case BlockShape.LowRamp: LowRamp(faces); break;                                    // half-height incline
                case BlockShape.QuarterCube: Box(faces, 0f, 0f, 0f, 0.5f, 0.5f, 0.5f); break;       // 0.5³ corner micro-cube
                case BlockShape.Table: Table(faces); break;                                        // top plate on corner legs
                case BlockShape.Chair: Chair(faces); break;                                        // seat + backrest toward +Z
                case BlockShape.Fence: Fence(faces); break;                                        // posts + rails along X
                case BlockShape.Sheet: Box(faces, 0f, 0f, 0f, 1f, 0.0625f, 1f); break;             // 1/16 rug/veneer plate
                case BlockShape.Pot: Pot(faces); break;                                            // small centred planter
                case BlockShape.Bench: Bench(faces); break;                                        // full-width seat + low backrest (#1846)
                case BlockShape.BedHead: BedHead(faces); break;                                    // two-cell bed, head half (#1846)
                case BlockShape.BedFoot: BedFoot(faces); break;                                    // two-cell bed, foot half (#1846)
                default: return null; // Cube / unknown → no custom geometry
                }
            }

            // #1900: every face gets its part (Box stamps one; anything built face by face is the body), its own-frame
            // side and — unless it already has them — texture coordinates, BEFORE the yaw/tilt below.
            for (int i = 0; i < faces.Count; i++)
            {
                faces[i] = faces[i].Finish(faces[i].Part);
            }

            if ((orientation & 3) != 0)
            {
                for (int i = 0; i < faces.Count; i++)
                {
                    faces[i] = faces[i].Map(p => Yaw(p, orientation));
                }
            }

            // Tilt so local +Y points at the up-face (0 = +Y = no tilt). Applied AFTER yaw, so up-face 0
            // reproduces the original yaw-only behaviour exactly (existing placed shapes are unchanged). A pure
            // rotation about the cell centre → winding + outward-facing normals are preserved.
            if (upFace != 0)
            {
                for (int i = 0; i < faces.Count; i++)
                {
                    faces[i] = faces[i].Map(p => Tilt(p, upFace));
                }
            }

            return faces;
        }

        // --- Player-designed forms (#844) ---
        //
        // The registry lives here as an IMMUTABLE snapshot published wholesale from the main thread, because
        // the chunk mesher reads it from worker threads — the same copy-on-write discipline PaintDesignAtlas
        // uses for its UV map. A builder thread only ever sees a complete, never-mutated dictionary.

        private static volatile Dictionary<int, string> _customVoxels = new Dictionary<int, string>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, List<Face>> _faceCache = new();

        /// <summary>Publishes a new form snapshot (main thread) and drops the geometry cache, so a form that
        /// was just registered — or wiped — is picked up by the next remesh.</summary>
        public static void PublishCustomShapes(Dictionary<int, string> snapshot)
        {
            _customVoxels = snapshot ?? new Dictionary<int, string>();
            _faceCache.Clear();
        }

        /// <summary>The registered bitmap of a player form, from the published snapshot — for callers that need
        /// more than one cell's faces (the placement ghost draws every block of a form over several blocks).</summary>
        public static bool TryGetCustomVoxels(int shapeIndex, out string voxels)
            => _customVoxels.TryGetValue(shapeIndex, out voxels);

        /// <summary>Drops every cached face list (session teardown — the next world may register different
        /// forms under the same indices).</summary>
        public static void ClearCache()
        {
            _customVoxels = new Dictionary<int, string>();
            _faceCache.Clear();
        }

        /// <summary>One merged micro box of a player-designed form, with REAL per-vertex UVs: each face shows
        /// the slice of the block's tile it actually covers, so a form reads as carved material instead of
        /// dozens of shrunken copies of the whole texture (and a degenerate UV would render white on the
        /// mipmapped atlas anyway).</summary>
        private static void MicroBox(List<Face> f, CustomShape.Box box)
        {
            float g = box.Grid;
            float x0 = box.X0 / g, y0 = box.Y0 / g, z0 = box.Z0 / g;
            float x1 = box.X1 / g, y1 = box.Y1 / g, z1 = box.Z1 / g;

            // Planar projection per axis: horizontal faces map (x,z), the ±X sides (z,y), the ±Z sides (x,y) —
            // the same convention the cube faces use, so a custom form's surface lines up with its neighbours.
            f.Add(new Face(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0),
                new(x0, z0), new(x0, z1), new(x1, z1), new(x1, z0)));                                     // +Y top
            f.Add(new Face(new(x0, y0, z1), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1),
                new(x0, z1), new(x0, z0), new(x1, z0), new(x1, z1)));                                     // -Y bottom
            f.Add(new Face(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1),
                new(z0, y0), new(z0, y1), new(z1, y1), new(z1, y0)));                                     // +X
            f.Add(new Face(new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0),
                new(z1, y0), new(z1, y1), new(z0, y1), new(z0, y0)));                                     // -X
            f.Add(new Face(new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), new(x0, y0, z1),
                new(x1, y0), new(x1, y1), new(x0, y1), new(x0, y0)));                                     // +Z
            f.Add(new Face(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0),
                new(x0, y0), new(x0, y1), new(x1, y1), new(x1, y0)));                                     // -Z
        }

        // Quaternion per up-face that maps local +Y onto that world face (indices match the mesher's face order:
        // 0 +Y, 1 -Y, 2 +X, 3 -X, 4 +Z, 5 -Z). Index 0 is identity (handled by the caller's != 0 guard).
        private static readonly Quaternion[] UpFaceRot =
        {
            Quaternion.identity,             // +Y
            Quaternion.Euler(180f, 0f, 0f),  // -Y
            Quaternion.Euler(0f, 0f, -90f),  // +X
            Quaternion.Euler(0f, 0f, 90f),   // -X
            Quaternion.Euler(90f, 0f, 0f),   // +Z
            Quaternion.Euler(-90f, 0f, 0f),  // -Z
        };

        /// <summary>Rotates a cell-local point about the cell centre so local +Y points at <paramref name="upFace"/>.</summary>
        private static Vector3 Tilt(Vector3 p, int upFace)
        {
            var c = new Vector3(0.5f, 0.5f, 0.5f);
            return c + UpFaceRot[Mathf.Clamp(upFace, 0, 5)] * (p - c);
        }

        // --- Primitive builders (unit cell, y up, centre at 0.5,*,0.5) ---

        /// <summary>An axis-aligned box [x0,x1]×[y0,y1]×[z0,z1] with all six faces wound outward, tagged with the
        /// form part it builds (#1900 — the block's texture slots can dress each part differently).</summary>
        private static void Box(List<Face> f, float x0, float y0, float z0, float x1, float y1, float z1, ShapePart part = ShapePart.Body)
        {
            f.Add(new Face(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0)).Finish(part)); // +Y top
            f.Add(new Face(new(x0, y0, z1), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1)).Finish(part)); // -Y bottom
            f.Add(new Face(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1)).Finish(part)); // +X
            f.Add(new Face(new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0)).Finish(part)); // -X
            f.Add(new Face(new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), new(x0, y0, z1)).Finish(part)); // +Z
            f.Add(new Face(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0)).Finish(part)); // -Z
        }

        private static void Pyramid(List<Face> f)
        {
            var apex = new Vector3(0.5f, 1f, 0.5f);
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1))); // -Y base
            // Four triangular sides over the base edges (each verified outward-facing).
            f.Add(new Face(new(1, 0, 0), apex, new(1, 0, 1)));
            f.Add(new Face(new(1, 0, 1), apex, new(0, 0, 1)));
            f.Add(new Face(new(0, 0, 1), apex, new(0, 0, 0)));
            f.Add(new Face(new(0, 0, 0), apex, new(1, 0, 0)));
        }

        private static void Ramp(List<Face> f)
        {
            // Wedge rising toward +Z: full floor, a vertical back wall at z=1, the sloped top, two side triangles.
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1))); // -Y floor
            f.Add(new Face(new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1))); // +Z back wall
            f.Add(new Face(new(0, 0, 0), new(0, 1, 1), new(1, 1, 1), new(1, 0, 0))); // slope (up + toward -Z)
            f.Add(new Face(new(0, 0, 0), new(0, 0, 1), new(0, 1, 1)));               // -X side triangle
            f.Add(new Face(new(1, 0, 0), new(1, 1, 1), new(1, 0, 1)));               // +X side triangle
        }

        private static void LowRamp(List<Face> f)
        {
            // Like Ramp but only half height — a gentle incline rising toward +Z (yaw rotates it).
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)));       // -Y floor
            f.Add(new Face(new(1, 0, 1), new(1, 0.5f, 1), new(0, 0.5f, 1), new(0, 0, 1))); // +Z back wall (half height)
            f.Add(new Face(new(0, 0, 0), new(0, 0.5f, 1), new(1, 0.5f, 1), new(1, 0, 0))); // slope
            f.Add(new Face(new(0, 0, 0), new(0, 0, 1), new(0, 0.5f, 1)));                   // -X side triangle
            f.Add(new Face(new(1, 0, 0), new(1, 0.5f, 1), new(1, 0, 1)));                   // +X side triangle
        }

        private static void Stairs(List<Face> f)
        {
            // Two steps rising toward +Z, built as explicit faces (no overlapping interior faces / z-fighting).
            f.Add(new Face(new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1)));         // -Y floor
            f.Add(new Face(new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1)));         // +Z back wall (full height)
            f.Add(new Face(new(0, 0, 0), new(0, 0.5f, 0), new(1, 0.5f, 0), new(1, 0, 0)));   // -Z front riser (step 1)
            f.Add(new Face(new(0, 0.5f, 0), new(0, 0.5f, 0.5f), new(1, 0.5f, 0.5f), new(1, 0.5f, 0))); // +Y tread (step 1)
            f.Add(new Face(new(0, 0.5f, 0.5f), new(0, 1, 0.5f), new(1, 1, 0.5f), new(1, 0.5f, 0.5f))); // -Z riser (step 2)
            f.Add(new Face(new(0, 1, 0.5f), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0.5f)));   // +Y tread (step 2)
            // -X side (two rectangles forming the L silhouette)
            f.Add(new Face(new(0, 0, 0.5f), new(0, 0.5f, 0.5f), new(0, 0.5f, 0), new(0, 0, 0)));
            f.Add(new Face(new(0, 0, 1), new(0, 1, 1), new(0, 1, 0.5f), new(0, 0, 0.5f)));
            // +X side
            f.Add(new Face(new(1, 0, 0), new(1, 0.5f, 0), new(1, 0.5f, 0.5f), new(1, 0, 0.5f)));
            f.Add(new Face(new(1, 0, 0.5f), new(1, 1, 0.5f), new(1, 1, 1), new(1, 0, 1)));
        }

        // Furniture forms (#805). Built from box unions; faces buried inside a sibling box are enclosed
        // (never coplanar with a visible face), so there is no z-fighting — legs deliberately poke INTO
        // the plate above them instead of ending flush with it.

        private static void Table(List<Face> f)
        {
            Box(f, 0f, 0.85f, 0f, 1f, 1f, 1f); // top plate, full cell so adjacent tables join seamlessly
            Box(f, 0.06f, 0f, 0.06f, 0.20f, 0.9f, 0.20f);
            Box(f, 0.80f, 0f, 0.06f, 0.94f, 0.9f, 0.20f);
            Box(f, 0.06f, 0f, 0.80f, 0.20f, 0.9f, 0.94f);
            Box(f, 0.80f, 0f, 0.80f, 0.94f, 0.9f, 0.94f);
        }

        private static void Chair(List<Face> f)
        {
            Box(f, 0.1f, 0.35f, 0.1f, 0.9f, 0.5f, 0.9f);   // seat
            Box(f, 0.1f, 0.4f, 0.72f, 0.9f, 1f, 0.9f);     // backrest (toward +Z; yaw turns it)
            Box(f, 0.12f, 0f, 0.12f, 0.24f, 0.4f, 0.24f);  // four legs, embedded into the seat
            Box(f, 0.76f, 0f, 0.12f, 0.88f, 0.4f, 0.24f);
            Box(f, 0.12f, 0f, 0.76f, 0.24f, 0.4f, 0.88f);
            Box(f, 0.76f, 0f, 0.76f, 0.88f, 0.4f, 0.88f);
        }

        private static void Fence(List<Face> f)
        {
            Box(f, 0.05f, 0f, 0.4f, 0.2f, 0.85f, 0.6f);    // two posts
            Box(f, 0.8f, 0f, 0.4f, 0.95f, 0.85f, 0.6f);
            Box(f, 0f, 0.55f, 0.44f, 1f, 0.7f, 0.56f);     // rails span the full cell → neighbours connect
            Box(f, 0f, 0.2f, 0.44f, 1f, 0.35f, 0.56f);
        }

        private static void Pot(List<Face> f)
        {
            Box(f, 0.28f, 0f, 0.28f, 0.72f, 0.42f, 0.72f); // body
            Box(f, 0.24f, 0.36f, 0.24f, 0.76f, 0.5f, 0.76f, ShapePart.Rim); // slightly wider rim
        }

        // #1846: a bench is a chair whose seat and backrest span the full X width, so a row of benches reads as
        // one long seat (like tables and fence rails, the cell-spanning boxes meet across cells). The backrest is
        // lower than a chair's — a park bench, not a pew. Legs poke into the seat like the chair's.
        private static void Bench(List<Face> f)
        {
            Box(f, 0f, 0.35f, 0.1f, 1f, 0.5f, 0.9f);       // seat, full width
            Box(f, 0f, 0.4f, 0.72f, 1f, 0.78f, 0.9f);      // low backrest toward +Z (yaw turns it), full width
            Box(f, 0.06f, 0f, 0.14f, 0.2f, 0.4f, 0.26f);   // four legs
            Box(f, 0.8f, 0f, 0.14f, 0.94f, 0.4f, 0.26f);
            Box(f, 0.06f, 0f, 0.74f, 0.2f, 0.4f, 0.86f);
            Box(f, 0.8f, 0f, 0.74f, 0.94f, 0.4f, 0.86f);
        }

        // #1846: the two-cell bed. Both halves keep the legacy one-cell bed's slab as their mattress, so a bed
        // from an old save, a ship layout or a cramped procedural room (still a Slab) matches the new ones in
        // height. The head half's foot side is local +Z — the server writes the foot half on the cell that
        // ShapeCode.YawDirection(yaw) points to, and the foot's footboard faces the same way, so the two boards
        // close the bed at both ends. Trim boxes are inset from the mattress faces (never coplanar).
        private static void BedHead(List<Face> f)
        {
            Box(f, 0f, 0f, 0f, 1f, 0.5f, 1f, ShapePart.BedHead);                  // mattress (= the legacy slab)
            Box(f, 0.15f, 0.45f, 0.12f, 0.85f, 0.62f, 0.45f, ShapePart.Pillow);  // pillow, sunk into the mattress
            Box(f, 0.02f, 0.45f, 0.02f, 0.98f, 0.85f, 0.1f, ShapePart.Headboard); // headboard at −Z
        }

        private static void BedFoot(List<Face> f)
        {
            Box(f, 0f, 0f, 0f, 1f, 0.5f, 1f, ShapePart.BedFoot);                  // mattress
            Box(f, 0.02f, 0.45f, 0.9f, 0.98f, 0.68f, 0.98f, ShapePart.Footboard); // footboard at +Z
        }

        private const float Cx = 0.5f, Cz = 0.5f, R = 0.5f;

        private static void Cylinder(List<Face> f)
        {
            var profile = new[] { (R, 0f), (R, 1f) };
            Lathe(f, profile, 12, capBottom: true, capTop: true);
        }

        private static void Cone(List<Face> f)
        {
            var profile = new[] { (R, 0f), (0f, 1f) };
            Lathe(f, profile, 12, capBottom: true, capTop: false);
        }

        private static void Dome(List<Face> f)
        {
            // Half-sphere stretched to fill the cell height: radius R in XZ, apex at y=1.
            const int rings = 4;
            var profile = new (float R, float Y)[rings + 1];
            for (int k = 0; k <= rings; k++)
            {
                float phi = (k / (float)rings) * (Mathf.PI * 0.5f); // 0 (equator) .. 90° (pole)
                profile[k] = (R * Mathf.Cos(phi), Mathf.Sin(phi));
            }

            Lathe(f, profile, 12, capBottom: true, capTop: false);
        }

        private static void Sphere(List<Face> f)
        {
            const int rings = 6;
            var profile = new (float R, float Y)[rings + 1];
            for (int k = 0; k <= rings; k++)
            {
                float phi = -Mathf.PI * 0.5f + (k / (float)rings) * Mathf.PI; // -90° .. +90°
                profile[k] = (R * Mathf.Cos(phi), 0.5f + R * Mathf.Sin(phi));
            }

            Lathe(f, profile, 10, capBottom: false, capTop: false);
        }

        /// <summary>Surface of revolution about the cell's vertical centre, from a bottom→top (radius, y) profile.
        /// A pole ring (radius 0) collapses its band to triangles. Optional flat caps close the ends.</summary>
        private static void Lathe(List<Face> f, (float R, float Y)[] profile, int seg, bool capBottom, bool capTop)
        {
            Vector3 P(float r, float y, int s)
            {
                float a = (s / (float)seg) * Mathf.PI * 2f;
                return new Vector3(Cx + r * Mathf.Cos(a), y, Cz + r * Mathf.Sin(a));
            }

            for (int j = 0; j < profile.Length - 1; j++)
            {
                var (rL, yL) = profile[j];
                var (rU, yU) = profile[j + 1];
                for (int s = 0; s < seg; s++)
                {
                    int s1 = s + 1;
                    if (rL <= 1e-4f) // bottom pole → triangle up to the ring above
                    {
                        f.Add(new Face(new Vector3(Cx, yL, Cz), P(rU, yU, s), P(rU, yU, s1)));
                    }
                    else if (rU <= 1e-4f) // top pole → triangle from the ring below
                    {
                        f.Add(new Face(P(rL, yL, s), new Vector3(Cx, yU, Cz), P(rL, yL, s1)));
                    }
                    else
                    {
                        f.Add(new Face(P(rL, yL, s), P(rU, yU, s), P(rU, yU, s1), P(rL, yL, s1)));
                    }
                }
            }

            if (capBottom)
            {
                var (r0, y0) = profile[0];
                var c = new Vector3(Cx, y0, Cz);
                for (int s = 0; s < seg; s++)
                {
                    f.Add(new Face(c, P(r0, y0, s), P(r0, y0, s + 1))); // -Y
                }
            }

            if (capTop)
            {
                var (rT, yT) = profile[profile.Length - 1];
                var c = new Vector3(Cx, yT, Cz);
                for (int s = 0; s < seg; s++)
                {
                    f.Add(new Face(c, P(rT, yT, s + 1), P(rT, yT, s))); // +Y
                }
            }
        }

        /// <summary>Rotates a cell-local point by <paramref name="quarterTurns"/> × 90° about the vertical
        /// centre axis (0.5, *, 0.5).</summary>
        private static Vector3 Yaw(Vector3 p, int quarterTurns)
        {
            float x = p.x, z = p.z;
            for (int i = 0; i < (quarterTurns & 3); i++)
            {
                float dx = x - 0.5f, dz = z - 0.5f;
                x = 0.5f - dz;
                z = 0.5f + dx;
            }

            return new Vector3(x, p.y, z);
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Chunked combined-mesh voxel renderer shared by the in-game ship + structure editors. Replaces the old
    /// one-GameObject-per-cell approach (a cube + collider per placed cell), which collapsed past a few
    /// thousand cells — so the editors can now hold large builds (ships up to 48³, structures up to 128³).
    ///
    /// Cells live in fixed <see cref="ChunkSize"/>³ chunks; each chunk is ONE combined mesh + one
    /// <see cref="MeshCollider"/>, rebuilt only when one of its cells (or a neighbour-chunk border cell)
    /// changes. Exposed cube faces are culled against solid neighbours, so a hollow station shell stays cheap.
    /// Per-face directional shading is baked into vertex colours, so a single <c>VertexColorOpaque</c> material
    /// reads in 3D with no per-cell materials. Edits mark chunks dirty; <see cref="Flush"/> (called once a
    /// frame) rebuilds them in a batch, so loading a big design is a handful of mesh builds, not thousands.
    /// </summary>
    internal sealed class EditorVoxelChunkView
    {
        /// <summary>One authored cell's render data. The editor resolves the base colour (dye/glow/palette)
        /// and passes it in; the view only renders + culls.</summary>
        internal struct Cell
        {
            public Color Color;   // resolved base colour
            public bool Glow;     // brighten toward full (stand-in for the in-game emissive look)
            public int Shape;     // packed shape+orientation (0 = plain cube)
            public bool Marker;   // interaction marker — drawn as a small inset cube, never occludes
        }

        private const int ChunkSize = 16;

        private sealed class Slot
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshCollider Collider;
            public readonly Dictionary<Vector3i, Cell> Cells = new();
        }

        private readonly Transform _parent;
        private readonly Material _material;
        private readonly Dictionary<Vector3i, Cell> _all = new();    // every cell — occupancy + render data
        private readonly Dictionary<Vector3i, Slot> _slots = new();  // chunk coord -> slot
        private readonly HashSet<Vector3i> _dirty = new();

        public EditorVoxelChunkView(Transform parent)
        {
            _parent = parent;
            var shader = Shader.Find("BlocksBeyondTheStars/VertexColorOpaque") ?? Shader.Find("Unlit/Color");
            _material = new Material(shader);

            // The vertex-colour shader tints by the global day/night light; pin it to neutral white so the
            // editor renders consistently regardless of any tint left over from a prior in-game session.
            Shader.SetGlobalVector("_Sc_Light", new Vector4(1f, 1f, 1f, 1f));
        }

        public int Count => _all.Count;

        public bool Contains(Vector3i cell) => _all.ContainsKey(cell);

        /// <summary>Adds or replaces a cell and marks its chunk (and any bordering neighbour chunk) dirty.</summary>
        public void Set(Vector3i cell, Cell data)
        {
            _all[cell] = data;
            var cc = ChunkOf(cell);
            if (!_slots.TryGetValue(cc, out var slot))
            {
                slot = CreateSlot(cc);
                _slots[cc] = slot;
            }

            slot.Cells[cell] = data;
            MarkDirty(cell, cc);
        }

        /// <summary>Removes a cell (if present) and marks the affected chunks dirty.</summary>
        public void Remove(Vector3i cell)
        {
            if (!_all.Remove(cell))
            {
                return;
            }

            var cc = ChunkOf(cell);
            if (_slots.TryGetValue(cc, out var slot))
            {
                slot.Cells.Remove(cell);
            }

            MarkDirty(cell, cc);
        }

        /// <summary>Drops every cell + chunk (used when loading a design or resetting the room).</summary>
        public void Clear()
        {
            foreach (var slot in _slots.Values)
            {
                if (slot.Filter != null && slot.Filter.sharedMesh != null)
                {
                    Object.Destroy(slot.Filter.sharedMesh);
                }

                if (slot.Go != null)
                {
                    Object.Destroy(slot.Go);
                }
            }

            _slots.Clear();
            _all.Clear();
            _dirty.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (_material != null)
            {
                Object.Destroy(_material);
            }
        }

        /// <summary>Rebuilds every chunk touched since the last call. Returns immediately when nothing changed,
        /// so it is cheap to call every frame.</summary>
        public void Flush()
        {
            if (_dirty.Count == 0)
            {
                return;
            }

            foreach (var cc in _dirty)
            {
                if (_slots.TryGetValue(cc, out var slot))
                {
                    RebuildSlot(slot);
                }
            }

            _dirty.Clear();
        }

        // --- chunk bookkeeping --------------------------------------------------------------------------

        private static int FloorDiv(int a, int b) => (a >= 0 ? a : a - (b - 1)) / b;

        private static Vector3i ChunkOf(Vector3i c)
            => new Vector3i(FloorDiv(c.X, ChunkSize), FloorDiv(c.Y, ChunkSize), FloorDiv(c.Z, ChunkSize));

        private void MarkDirty(Vector3i cell, Vector3i cc)
        {
            _dirty.Add(cc);

            // A cube face is culled against the neighbour cell, which may sit in an adjacent chunk — rebuild
            // those too so a wall flush against a chunk border updates on both sides.
            AddNeighbourChunk(cell, 1, 0, 0);
            AddNeighbourChunk(cell, -1, 0, 0);
            AddNeighbourChunk(cell, 0, 1, 0);
            AddNeighbourChunk(cell, 0, -1, 0);
            AddNeighbourChunk(cell, 0, 0, 1);
            AddNeighbourChunk(cell, 0, 0, -1);
        }

        private void AddNeighbourChunk(Vector3i cell, int dx, int dy, int dz)
        {
            var nc = ChunkOf(new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz));
            if (_slots.ContainsKey(nc))
            {
                _dirty.Add(nc);
            }
        }

        private Slot CreateSlot(Vector3i cc)
        {
            var go = new GameObject($"Chunk {cc.X},{cc.Y},{cc.Z}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(_parent, false); // mesh is authored in world cell coords (identity transform)
            go.GetComponent<MeshRenderer>().sharedMaterial = _material;
            return new Slot
            {
                Go = go,
                Filter = go.GetComponent<MeshFilter>(),
                Collider = go.GetComponent<MeshCollider>(),
            };
        }

        // --- meshing ------------------------------------------------------------------------------------

        // Scratch buffers reused across rebuilds (one chunk is built at a time on the main thread).
        private static readonly List<Vector3> _verts = new();
        private static readonly List<Color> _colors = new();
        private static readonly List<int> _tris = new();

        /// <summary>True if the cell at <paramref name="p"/> is a solid cube that should hide the face turned
        /// toward it. Shaped + marker cells never occlude (they don't fill the cell).</summary>
        private bool Occludes(Vector3i p)
            => _all.TryGetValue(p, out var c) && c.Shape == 0 && !c.Marker;

        private void RebuildSlot(Slot slot)
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            foreach (var kv in slot.Cells)
            {
                var cell = kv.Key;
                var data = kv.Value;
                var origin = new Vector3(cell.X, cell.Y, cell.Z);

                if (data.Marker)
                {
                    // Markers read as small floating cubes so they stand apart from solid blocks.
                    for (int f = 0; f < 6; f++)
                    {
                        AddFace(origin, f, 0.25f, 0.75f, data.Color, data.Glow);
                    }
                }
                else if (data.Shape != 0)
                {
                    AddShape(origin, data);
                }
                else
                {
                    // Plain cube: emit only the faces exposed to a non-occluding neighbour.
                    if (!Occludes(new Vector3i(cell.X, cell.Y + 1, cell.Z))) AddFace(origin, 0, 0f, 1f, data.Color, data.Glow);
                    if (!Occludes(new Vector3i(cell.X, cell.Y - 1, cell.Z))) AddFace(origin, 1, 0f, 1f, data.Color, data.Glow);
                    if (!Occludes(new Vector3i(cell.X + 1, cell.Y, cell.Z))) AddFace(origin, 2, 0f, 1f, data.Color, data.Glow);
                    if (!Occludes(new Vector3i(cell.X - 1, cell.Y, cell.Z))) AddFace(origin, 3, 0f, 1f, data.Color, data.Glow);
                    if (!Occludes(new Vector3i(cell.X, cell.Y, cell.Z + 1))) AddFace(origin, 4, 0f, 1f, data.Color, data.Glow);
                    if (!Occludes(new Vector3i(cell.X, cell.Y, cell.Z - 1))) AddFace(origin, 5, 0f, 1f, data.Color, data.Glow);
                }
            }

            var mesh = slot.Filter.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = "EditorChunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                slot.Filter.sharedMesh = mesh;
            }
            else
            {
                mesh.Clear();
            }

            if (_verts.Count > 0)
            {
                mesh.SetVertices(_verts);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
            }

            // A MeshCollider with a null/empty mesh raycasts nothing — exactly right for an emptied chunk.
            slot.Collider.sharedMesh = null;
            slot.Collider.sharedMesh = _verts.Count > 0 ? mesh : null;
        }

        private void AddShape(Vector3 origin, Cell data)
        {
            var faces = BlockShapeGeometry.Build(ShapeCode.ShapeOf(data.Shape), ShapeCode.OrientationOf(data.Shape));
            if (faces == null)
            {
                return;
            }

            foreach (var face in faces)
            {
                Vector3 a = origin + face.A, b = origin + face.B, c = origin + face.C;
                Vector3 edge1 = b - a;
                Vector3 edge2 = (face.IsQuad ? origin + face.D : c) - a;
                Vector3 nrm = Vector3.Cross(edge1, edge2).normalized;
                float shade = Mathf.Clamp(0.76f + 0.24f * nrm.y, 0.5f, 1f);
                Color col = Tint(data.Color, shade, data.Glow);

                int b0 = _verts.Count;
                _verts.Add(a); _verts.Add(b); _verts.Add(c);
                _colors.Add(col); _colors.Add(col); _colors.Add(col);
                _tris.Add(b0); _tris.Add(b0 + 1); _tris.Add(b0 + 2);
                if (face.IsQuad)
                {
                    _verts.Add(origin + face.D);
                    _colors.Add(col);
                    _tris.Add(b0); _tris.Add(b0 + 2); _tris.Add(b0 + 3);
                }
            }
        }

        /// <summary>Appends one box face (winding + corner order match <c>ChunkMesher.FaceQuad</c> so the
        /// outward side is the front face), shaded by face direction and baked into the vertex colour.</summary>
        private void AddFace(Vector3 p, int face, float lo, float hi, Color color, bool glow)
        {
            Vector3[] q = FaceQuad(p, face, lo, hi);
            Color col = Tint(color, FaceShade(face), glow);

            int b0 = _verts.Count;
            _verts.Add(q[0]); _verts.Add(q[1]); _verts.Add(q[2]); _verts.Add(q[3]);
            _colors.Add(col); _colors.Add(col); _colors.Add(col); _colors.Add(col);
            _tris.Add(b0); _tris.Add(b0 + 1); _tris.Add(b0 + 2);
            _tris.Add(b0); _tris.Add(b0 + 2); _tris.Add(b0 + 3);
        }

        /// <summary>Base colour × face shade; glowing cells keep a bright floor so they read as emissive.</summary>
        private static Color Tint(Color c, float shade, bool glow)
        {
            float s = glow ? Mathf.Max(shade, 0.85f) : shade;
            return new Color(c.r * s, c.g * s, c.b * s, 1f);
        }

        /// <summary>Relative brightness per face (top brightest, bottom darkest), mirroring the in-game
        /// <c>ChunkMesher.FaceShade</c> so editor cubes read the same way they will in the world.</summary>
        private static float FaceShade(int face) => face switch
        {
            0 => 1.00f, // +Y top
            1 => 0.50f, // -Y bottom
            2 => 0.82f, // +X
            3 => 0.72f, // -X
            4 => 0.66f, // +Z
            _ => 0.76f, // -Z
        };

        /// <summary>The four corners of a box face on <c>[lo,hi]³</c> offset by <paramref name="p"/>. Corner
        /// order + winding match <c>ChunkMesher.FaceQuad</c> (front face = outward).</summary>
        private static Vector3[] FaceQuad(Vector3 p, int face, float lo, float hi)
        {
            switch (face)
            {
                case 0: return new[] { p + new Vector3(lo, hi, lo), p + new Vector3(lo, hi, hi), p + new Vector3(hi, hi, hi), p + new Vector3(hi, hi, lo) }; // +Y
                case 1: return new[] { p + new Vector3(lo, lo, hi), p + new Vector3(lo, lo, lo), p + new Vector3(hi, lo, lo), p + new Vector3(hi, lo, hi) }; // -Y
                case 2: return new[] { p + new Vector3(hi, lo, lo), p + new Vector3(hi, hi, lo), p + new Vector3(hi, hi, hi), p + new Vector3(hi, lo, hi) }; // +X
                case 3: return new[] { p + new Vector3(lo, lo, hi), p + new Vector3(lo, hi, hi), p + new Vector3(lo, hi, lo), p + new Vector3(lo, lo, lo) }; // -X
                case 4: return new[] { p + new Vector3(hi, lo, hi), p + new Vector3(hi, hi, hi), p + new Vector3(lo, hi, hi), p + new Vector3(lo, lo, hi) }; // +Z
                default: return new[] { p + new Vector3(lo, lo, lo), p + new Vector3(lo, hi, lo), p + new Vector3(hi, hi, lo), p + new Vector3(hi, lo, lo) }; // -Z
            }
        }
    }
}

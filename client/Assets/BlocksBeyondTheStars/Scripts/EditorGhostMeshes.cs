// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meshes the build editors' placement ghost shows instead of the plain cube (#1975): the cells a click
    /// would write (a form, a bed as head + foot), the door the server would hang (from the shared
    /// <see cref="DoorGeometry"/>), or a marker's silhouette. Built once per distinct request and cached for the
    /// editor's lifetime — the ghost moves every frame, the geometry rarely changes. All meshes are authored
    /// relative to the ghost's origin: the anchor cell's min corner for cells and silhouettes, the doorway centre
    /// on the floor for doors.
    /// </summary>
    internal sealed class EditorGhostMeshes
    {
        private readonly Dictionary<string, Mesh> _cache = new();
        private readonly StringBuilder _key = new();

        /// <summary>The cells a placement writes, drawn at their offsets from <paramref name="anchor"/>: each cell's
        /// form through <see cref="BlockShapeGeometry"/> (a plain cube as a unit box).</summary>
        public Mesh ForWrites(IReadOnlyList<EditorPlacementRules.CellWrite> writes, Vector3i anchor)
        {
            _key.Clear().Append("w");
            foreach (var w in writes)
            {
                _key.Append('|').Append(w.X - anchor.X).Append(',').Append(w.Y - anchor.Y).Append(',').Append(w.Z - anchor.Z).Append(':').Append(w.Shape);
            }

            string key = _key.ToString();
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            foreach (var w in writes)
            {
                var offset = new Vector3(w.X - anchor.X, w.Y - anchor.Y, w.Z - anchor.Z);
                var faces = w.Shape != 0
                    ? BlockShapeGeometry.Build(ShapeCode.ShapeOf(w.Shape), ShapeCode.OrientationOf(w.Shape), ShapeCode.UpFaceOf(w.Shape))
                    : null;
                if (faces == null || faces.Count == 0)
                {
                    DoorMesh.AddBox(verts, cols, tris, offset, offset + Vector3.one, Color.white);
                    continue;
                }

                foreach (var f in faces)
                {
                    int b = verts.Count;
                    verts.Add(f.A + offset); verts.Add(f.B + offset); verts.Add(f.C + offset);
                    cols.Add(Color.white); cols.Add(Color.white); cols.Add(Color.white);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    if (f.IsQuad)
                    {
                        verts.Add(f.D + offset);
                        cols.Add(Color.white);
                        tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                    }
                }
            }

            return Store(key, verts, cols, tris);
        }

        /// <summary>A closed door of <paramref name="kind"/> over a gap <paramref name="width"/> wide, turned for the
        /// wall axis, centred on the doorway centre.</summary>
        public Mesh ForDoor(string kind, float width, bool axisX)
        {
            string key = "d|" + kind + "|" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (axisX ? "x" : "z");
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            DoorMesh.Append(verts, cols, tris, DoorGeometry.Closed(kind, width), axisX, Vector3.zero, _ => Color.white, withField: false);
            return Store(key, verts, cols, tris);
        }

        /// <summary>A marker's or ship part's silhouette in its cell (plus the cell's cube where the part keeps
        /// one); null when the entry has no silhouette and keeps the plain cube ghost.</summary>
        public Mesh ForSilhouette(string id, string kind)
        {
            var sil = MarkerSilhouettes.For(id, kind);
            if (sil == null)
            {
                return null;
            }

            string key = "s|" + kind + "|" + id;
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            if (sil.Value.KeepsCube)
            {
                DoorMesh.AddBox(verts, cols, tris, Vector3.zero, Vector3.one, Color.white);
            }

            foreach (var b in sil.Value.Boxes)
            {
                var c = new Vector3(b.Centre.X, b.Centre.Y, b.Centre.Z);
                var s = new Vector3(b.Size.X, b.Size.Y, b.Size.Z);
                DoorMesh.AddBox(verts, cols, tris, c - s * 0.5f, c + s * 0.5f, Color.white);
            }

            return Store(key, verts, cols, tris);
        }

        private Mesh Store(string key, List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            var mesh = new Mesh { name = "EditorGhost " + key };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _cache[key] = mesh;
            return mesh;
        }

        public void Dispose()
        {
            foreach (var m in _cache.Values)
            {
                if (m != null)
                {
                    Object.Destroy(m);
                }
            }

            _cache.Clear();
        }
    }
}

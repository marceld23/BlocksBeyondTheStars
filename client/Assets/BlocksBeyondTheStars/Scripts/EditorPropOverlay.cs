// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the build editors draw for the cells that are not voxels (#1975): a placed door marker as the door the
    /// server will hang (axis and width read from the design around it, re-fitted whenever a cell within reach
    /// changes), a marker as its silhouette, a ship station's decor on the cell above its block. One small
    /// GameObject per such cell, authored in world coordinates like the chunk view, with a unit collider on the
    /// authored cell so aiming, placing beside it and removing it work exactly as they did on the old inset cube.
    /// </summary>
    internal sealed class EditorPropOverlay
    {
        private sealed class Entry
        {
            public GameObject Go;
            public MeshFilter Filter;
            public Mesh Mesh;
            public string Kind;      // "door" | MarkerSilhouettes kind
            public string Id;        // door kind, or the marker/part id
            public Color Colour;
            public bool? ForceAxisX; // door: an axis the author's structure fixes (the ship's rear-wall hatch), else probed
        }

        private readonly Transform _parent;
        private readonly Material _material;
        private readonly Dictionary<Vector3i, Entry> _entries = new();

        // Scratch buffers reused across rebuilds.
        private static readonly List<Vector3> _verts = new();
        private static readonly List<Color> _cols = new();
        private static readonly List<int> _tris = new();

        public EditorPropOverlay(Transform parent)
        {
            _parent = parent;
            // The chunk view's vertex-colour shader: with no TEXCOORD1 the atlas weight is 0, so these meshes are plain
            // vertex colour (with the directional shade baked in, like the chunk view bakes it).
            var shader = Shader.Find("BlocksBeyondTheStars/VertexColorOpaque") ?? Shader.Find("Unlit/Color");
            _material = new Material(shader);
        }

        public bool Contains(Vector3i cell) => _entries.ContainsKey(cell);

        /// <summary>Draws (or re-draws) the door a marker at <paramref name="cell"/> would hang. <paramref name="forceAxisX"/>
        /// pins the wall axis where the server does (the ship's rear-wall hatch), else the probe decides.</summary>
        public void SetDoor(Vector3i cell, string kind, Func<int, int, int, bool> solid, bool? forceAxisX = null)
        {
            var e = Slot(cell, "door", kind, Color.white);
            e.ForceAxisX = forceAxisX;
            BuildDoor(e, cell, solid);
        }

        /// <summary>Draws (or re-draws) a marker's / part's silhouette in its cell.</summary>
        public void SetSilhouette(Vector3i cell, string id, string kind, Color colour)
        {
            var sil = MarkerSilhouettes.For(id, kind);
            if (sil == null)
            {
                Remove(cell);
                return;
            }

            var e = Slot(cell, kind, id, colour);
            _verts.Clear(); _cols.Clear(); _tris.Clear();
            var origin = new Vector3(cell.X, cell.Y, cell.Z);
            foreach (var b in sil.Value.Boxes)
            {
                var c = origin + new Vector3(b.Centre.X, b.Centre.Y, b.Centre.Z);
                var s = new Vector3(b.Size.X, b.Size.Y, b.Size.Z);
                ShadedBox(c - s * 0.5f, c + s * 0.5f, colour);
            }

            Upload(e);
        }

        public void Remove(Vector3i cell)
        {
            if (!_entries.TryGetValue(cell, out var e))
            {
                return;
            }

            _entries.Remove(cell);
            Destroy(e);
        }

        /// <summary>A cell changed: every door whose probe reads that cell is re-fitted (the jambs decide the axis,
        /// the air run the width — both can change when a block appears or vanishes nearby).</summary>
        public void OnDesignChanged(Vector3i changed, Func<int, int, int, bool> solid)
        {
            List<Vector3i> refit = null;
            foreach (var kv in _entries)
            {
                if (kv.Value.Kind == "door" && EditorPlacementRules.AffectsDoor(changed.X, changed.Y, changed.Z, kv.Key.X, kv.Key.Y, kv.Key.Z))
                {
                    (refit ??= new List<Vector3i>()).Add(kv.Key);
                }
            }

            if (refit == null)
            {
                return;
            }

            foreach (var cell in refit)
            {
                BuildDoor(_entries[cell], cell, solid);
            }
        }

        public void Clear()
        {
            foreach (var e in _entries.Values)
            {
                Destroy(e);
            }

            _entries.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
            }
        }

        // --- internals ------------------------------------------------------------------------------------

        private Entry Slot(Vector3i cell, string kind, string id, Color colour)
        {
            if (!_entries.TryGetValue(cell, out var e))
            {
                var go = new GameObject($"Overlay {kind} {id} {cell.X},{cell.Y},{cell.Z}", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                go.transform.SetParent(_parent, false); // meshes are authored in world cell coords (identity transform)
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var col = go.GetComponent<BoxCollider>();
                col.center = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f); // the authored cell: what the pick ray hits
                col.size = Vector3.one;
                e = new Entry { Go = go, Filter = go.GetComponent<MeshFilter>(), Mesh = new Mesh { name = "EditorOverlay" } };
                e.Filter.sharedMesh = e.Mesh;
                _entries[cell] = e;
            }

            e.Kind = kind;
            e.Id = id;
            e.Colour = colour;
            return e;
        }

        private static void Destroy(Entry e)
        {
            if (e.Mesh != null)
            {
                UnityEngine.Object.Destroy(e.Mesh);
            }

            if (e.Go != null)
            {
                UnityEngine.Object.Destroy(e.Go);
            }
        }

        private static void BuildDoor(Entry e, Vector3i cell, Func<int, int, int, bool> solid)
        {
            var fit = DoorProbe.Measure(solid, cell.X, cell.Y, cell.Z, e.ForceAxisX);
            var origin = new Vector3(fit.CentreX(cell.X), cell.Y, fit.CentreZ(cell.Z));
            string kind = e.Id;
            _verts.Clear(); _cols.Clear(); _tris.Clear();
            foreach (var box in DoorGeometry.Closed(kind, fit.Width))
            {
                if (box.Part == DoorGeometry.Part.Field)
                {
                    continue; // invisible while closed in the world too
                }

                Vector3 c = fit.AxisX
                    ? new Vector3(box.Centre.X, box.Centre.Y, box.Centre.Z)
                    : new Vector3(box.Centre.Z, box.Centre.Y, -box.Centre.X);
                Vector3 s = fit.AxisX
                    ? new Vector3(box.Size.X, box.Size.Y, box.Size.Z)
                    : new Vector3(box.Size.Z, box.Size.Y, box.Size.X);
                ShadedBox(origin + c - s * 0.5f, origin + c + s * 0.5f, DoorMesh.EditorColour(kind, box.Part));
            }

            Upload(e);
        }

        private static void Upload(Entry e)
        {
            e.Mesh.Clear();
            e.Mesh.SetVertices(_verts);
            e.Mesh.SetColors(_cols);
            e.Mesh.SetTriangles(_tris, 0);
            e.Mesh.RecalculateNormals();
            e.Mesh.RecalculateBounds();
        }

        /// <summary>A box with the chunk view's directional shade baked into its vertex colours (top brightest,
        /// bottom darkest), so silhouettes and doors read in 3D under the flat vertex-colour material.</summary>
        private static void ShadedBox(Vector3 lo, Vector3 hi, Color col)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float shade)
            {
                int i = _verts.Count;
                var sc = new Color(col.r * shade, col.g * shade, col.b * shade, 1f);
                _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
                _cols.Add(sc); _cols.Add(sc); _cols.Add(sc); _cols.Add(sc);
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
                _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
            }

            Quad(new(lo.x, hi.y, lo.z), new(lo.x, hi.y, hi.z), new(hi.x, hi.y, hi.z), new(hi.x, hi.y, lo.z), 1.00f); // +Y
            Quad(new(lo.x, lo.y, hi.z), new(lo.x, lo.y, lo.z), new(hi.x, lo.y, lo.z), new(hi.x, lo.y, hi.z), 0.50f); // -Y
            Quad(new(hi.x, lo.y, lo.z), new(hi.x, hi.y, lo.z), new(hi.x, hi.y, hi.z), new(hi.x, lo.y, hi.z), 0.82f); // +X
            Quad(new(lo.x, lo.y, hi.z), new(lo.x, hi.y, hi.z), new(lo.x, hi.y, lo.z), new(lo.x, lo.y, lo.z), 0.72f); // -X
            Quad(new(hi.x, lo.y, hi.z), new(hi.x, hi.y, hi.z), new(lo.x, hi.y, hi.z), new(lo.x, lo.y, hi.z), 0.66f); // +Z
            Quad(new(lo.x, lo.y, lo.z), new(lo.x, hi.y, lo.z), new(hi.x, hi.y, lo.z), new(hi.x, lo.y, lo.z), 0.76f); // -Z
        }
    }
}

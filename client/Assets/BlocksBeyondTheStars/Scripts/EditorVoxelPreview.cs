// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shared preview helpers for the in-game structure/ship editors so a placed cell looks like it will
    /// in-game: a dyed (tint) + glowing (emissive) material, and — for shaped cells — the real
    /// <see cref="BlockShapeGeometry"/> form rotated by the orientation. Cube cells keep using a Unity
    /// primitive cube; only shaped cells get a generated mesh.
    /// </summary>
    internal static class EditorVoxelPreview
    {
        /// <summary>Builds a unit-cell mesh (0..1 on each axis) for a packed shape index + orientation, or
        /// null for cube/unknown (the caller then uses a primitive cube). Mirrors the in-game shape geometry.</summary>
        public static Mesh ShapeMesh(int shapeIndex, int orientation)
        {
            var faces = BlockShapeGeometry.Build(shapeIndex, orientation);
            if (faces == null || faces.Count == 0)
            {
                return null;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                int b = verts.Count;
                verts.Add(f.A); verts.Add(f.B); verts.Add(f.C);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                if (f.IsQuad)
                {
                    verts.Add(f.D);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
            }

            var mesh = new Mesh { name = "EditorShapePreview" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Builds a unit-cell mesh for a form BITMAP that is not registered anywhere yet — what the
        /// form editor shows while the player is still drawing (#845). Same greedy boxes the world mesher
        /// would emit, so the preview cannot promise a shape the game then renders differently.</summary>
        public static Mesh CustomShapeMesh(string voxels)
        {
            if (!CustomShape.IsValidVoxels(voxels))
            {
                return null;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var box in CustomShape.Merge(voxels))
            {
                float g = box.Grid;
                AddBox(verts, tris,
                    new Vector3(box.X0 / g, box.Y0 / g, box.Z0 / g),
                    new Vector3(box.X1 / g, box.Y1 / g, box.Z1 / g));
            }

            if (verts.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh { name = "CustomFormPreview" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A plain lit material for the form preview, created once per session.</summary>
        public static Material PreviewMaterial()
        {
            if (_previewMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _previewMaterial = new Material(shader) { color = new Color(0.55f, 0.78f, 0.92f) };
            }

            return _previewMaterial;
        }

        private static Material _previewMaterial;

        /// <summary>Six outward-wound quads of an axis-aligned box (as triangles).</summary>
        private static void AddBox(List<Vector3> verts, List<int> tris, Vector3 lo, Vector3 hi)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Quad(new(lo.x, hi.y, lo.z), new(lo.x, hi.y, hi.z), new(hi.x, hi.y, hi.z), new(hi.x, hi.y, lo.z)); // +Y
            Quad(new(lo.x, lo.y, hi.z), new(lo.x, lo.y, lo.z), new(hi.x, lo.y, lo.z), new(hi.x, lo.y, hi.z)); // -Y
            Quad(new(hi.x, lo.y, lo.z), new(hi.x, hi.y, lo.z), new(hi.x, hi.y, hi.z), new(hi.x, lo.y, hi.z)); // +X
            Quad(new(lo.x, lo.y, hi.z), new(lo.x, hi.y, hi.z), new(lo.x, hi.y, lo.z), new(lo.x, lo.y, lo.z)); // -X
            Quad(new(hi.x, lo.y, hi.z), new(hi.x, hi.y, hi.z), new(lo.x, hi.y, hi.z), new(lo.x, lo.y, hi.z)); // +Z
            Quad(new(lo.x, lo.y, lo.z), new(lo.x, hi.y, lo.z), new(hi.x, hi.y, lo.z), new(hi.x, lo.y, lo.z)); // -Z
        }

        /// <summary>RGB 0xRRGGBB → Unity colour (alpha 1). 0 stays black, callers treat 0 as "no tint".</summary>
        public static Color RgbToColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}

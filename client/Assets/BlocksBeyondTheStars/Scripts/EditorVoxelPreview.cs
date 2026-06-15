using System.Collections.Generic;
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

        /// <summary>RGB 0xRRGGBB → Unity colour (alpha 1). 0 stays black, callers treat 0 as "no tint".</summary>
        public static Color RgbToColor(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}

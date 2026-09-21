// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Turns the shared <see cref="DoorGeometry"/> boxes into mesh triangles (#1975) for everything that draws a
    /// door WITHOUT the world's door objects: the in-game placement ghost and the build editors. World-space
    /// convention matches <c>DoorView</c>: the door's origin is the centre of the doorway gap on the floor, and a
    /// door in a Z wall is the local frame turned +90° about Y (local +X → world −Z, local +Z → world +X).
    /// </summary>
    internal static class DoorMesh
    {
        /// <summary>A whole closed door as one mesh with per-part vertex colours, centred on the door origin.
        /// <paramref name="colour"/> picks the colour per part; the energy field is skipped unless
        /// <paramref name="withField"/> (it is invisible while closed in the world).</summary>
        public static Mesh Build(string kind, float width, bool axisX, Func<DoorGeometry.Part, Color> colour, bool withField = false,
            bool mirrored = false, DoorPairs.Sides partners = DoorPairs.Sides.None)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            Append(verts, cols, tris, DoorGeometry.Closed(kind, width, mirrored, partners), axisX, Vector3.zero, colour, withField);

            var mesh = new Mesh { name = "DoorMesh" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Appends a door's boxes to shared buffers (the editors batch many doors into one mesh):
        /// every box is turned for the wall axis and shifted by <paramref name="origin"/> (the door centre on the
        /// floor, world space).</summary>
        public static void Append(List<Vector3> verts, List<Color> cols, List<int> tris, IReadOnlyList<DoorGeometry.Box> boxes,
            bool axisX, Vector3 origin, Func<DoorGeometry.Part, Color> colour, bool withField)
        {
            foreach (var box in boxes)
            {
                if (box.Part == DoorGeometry.Part.Field && !withField)
                {
                    continue;
                }

                // Boxes are axis-aligned in the local frame and stay so after a quarter turn: turn the centre and
                // swap the X/Z extents instead of rotating eight corners.
                Vector3 c = axisX
                    ? new Vector3(box.Centre.X, box.Centre.Y, box.Centre.Z)
                    : new Vector3(box.Centre.Z, box.Centre.Y, -box.Centre.X);
                Vector3 s = axisX
                    ? new Vector3(box.Size.X, box.Size.Y, box.Size.Z)
                    : new Vector3(box.Size.Z, box.Size.Y, box.Size.X);
                AddBox(verts, cols, tris, origin + c - s * 0.5f, origin + c + s * 0.5f, colour(box.Part));
            }
        }

        /// <summary>Six outward-wound quads of an axis-aligned box (as triangles), one colour.</summary>
        public static void AddBox(List<Vector3> verts, List<Color> cols, List<int> tris, Vector3 lo, Vector3 hi, Color col)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
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

        /// <summary>The flat colours the editors draw a placed door in: the texturable parts' fallback colours
        /// (<see cref="PropTextures"/>), so an untextured editor door looks like an untextured world door.</summary>
        public static Color EditorColour(string kind, DoorGeometry.Part part)
        {
            bool wood = kind == DoorBlocks.Wood;
            bool hinge = DoorBlocks.IsHandOperated(kind);
            var panel = wood ? PropTextures.DoorWoodPanel : hinge ? PropTextures.DoorHingePanel : PropTextures.DoorSlidePanel;
            var trim = wood ? PropTextures.DoorWoodTrim : hinge ? PropTextures.DoorHingeTrim : PropTextures.DoorSlideTrim;
            return part switch
            {
                DoorGeometry.Part.Field => new Color(0.35f, 0.80f, 1f, 0.4f),
                DoorGeometry.Part.Seam or DoorGeometry.Part.PostMinus or DoorGeometry.Part.PostPlus => trim.Color,
                _ => panel.Color,
            };
        }
    }
}

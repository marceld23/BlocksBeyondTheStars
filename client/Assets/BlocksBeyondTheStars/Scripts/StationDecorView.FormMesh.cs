// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meshes of the station decor's forms: the boxes of <see cref="StationDecorForms"/>, rounded with
    /// <see cref="RoundedCube"/> (the creatures' and tools' rounding) and combined into one mesh per station kind — housing
    /// and deck as two sub-meshes of the body, the screen on its own. No far stage: a ship has a handful of stations and a
    /// body is under two thousand vertices, so the forms are the same at every distance and on every tier.
    /// </summary>
    public sealed partial class StationDecorView
    {
        /// <summary>The body and screen mesh of a station kind, built once and shared by every station of that kind.</summary>
        private static (Mesh Body, Mesh Screen) FormMeshesOf(StationDecorForms.Kind kind)
        {
            if (FormMeshes.TryGetValue(kind, out var cached) && cached.Body != null)
            {
                return cached;
            }

            var boxes = StationDecorForms.BoxesOf(kind);
            var meshes = (BuildFormMesh(boxes, screen: false, "StationForm " + kind), BuildFormMesh(boxes, screen: true, "StationForm screen " + kind));
            FormMeshes[kind] = meshes;
            return meshes;
        }

        /// <summary>The boxes of one part of a form in one mesh: the body (sub-mesh 0 the housing, sub-mesh 1 the deck when
        /// there is one) or the screen. Null when the form has no box of that part.</summary>
        private static Mesh BuildFormMesh(StationDecorForms.FormBox[] boxes, bool screen, string name)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var housing = new List<int>();
            var deck = new List<int>();
            foreach (var box in boxes)
            {
                // A screen box's picture face is the screen mesh; its edges and back are housing in the body.
                bool isScreen = box.Surface == StationDecorForms.Surface.Screen;
                if (screen && !isScreen)
                {
                    continue;
                }

                var faces = !isScreen ? FormFaces.All : screen ? FormFaces.Picture : FormFaces.AllButPicture;
                AppendFormBox(box, faces, verts, normals, uvs, box.Surface == StationDecorForms.Surface.Deck ? deck : housing);
            }

            if (verts.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = deck.Count > 0 ? 2 : 1;
            mesh.SetTriangles(housing, 0);
            if (deck.Count > 0)
            {
                mesh.SetTriangles(deck, 1);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Which faces of a box go into a mesh: all of them, only a screen's picture face, or all but that one.</summary>
        private enum FormFaces
        {
            All,
            Picture,
            AllButPicture,
        }

        /// <summary>Appends one box: its unit cube (rounded when it may be and the share is above 0; a screen is never
        /// rounded), turned into the decor frame. The triangles go to <paramref name="tris"/> and index the shared vertex
        /// list.</summary>
        private static void AppendFormBox(StationDecorForms.FormBox box, FormFaces faces, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            var flat = StationDecorForms.FlatCube(box);
            var size = new Vector3(box.Size.X, box.Size.Y, box.Size.Z);
            float radius = faces == FormFaces.All && box.Rounded ? RoundedCube.Radius(size, DecorFormsRound) : 0f;
            int first = verts.Count;
            if (radius > 0f)
            {
                RoundedCube.Build(size, radius, flat, verts, normals, uvs, tris); // indexes from verts.Count itself
            }
            else
            {
                AppendFlatFaces(box, flat, faces, verts, normals, uvs, tris);
            }

            for (int i = first; i < verts.Count; i++)
            {
                var p = StationDecorForms.PointToCell(box, new NVector3(verts[i].x, verts[i].y, verts[i].z));
                var n = StationDecorForms.NormalToCell(box, new NVector3(normals[i].x, normals[i].y, normals[i].z));
                verts[i] = new Vector3(p.X, p.Y, p.Z);
                normals[i] = new Vector3(n.X, n.Y, n.Z);
            }
        }

        /// <summary>The sharp cube's faces that <paramref name="faces"/> asks for, in the unit cube's space (four vertices
        /// and two triangles per face, <see cref="TiledCube"/>'s layout).</summary>
        private static void AppendFlatFaces(StationDecorForms.FormBox box, TiledCube.Data flat, FormFaces faces, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            for (int face = 0; face < 6; face++)
            {
                bool picture = StationDecorForms.IsScreenFace(box, flat, face);
                if ((faces == FormFaces.Picture && !picture) || (faces == FormFaces.AllButPicture && picture))
                {
                    continue;
                }

                int start = verts.Count;
                for (int i = face * 4; i < (face * 4) + 4; i++)
                {
                    verts.Add(new Vector3(flat.Positions[i * 3], flat.Positions[(i * 3) + 1], flat.Positions[(i * 3) + 2]));
                    normals.Add(new Vector3(flat.Normals[i * 3], flat.Normals[(i * 3) + 1], flat.Normals[(i * 3) + 2]));
                    uvs.Add(new Vector2(flat.Uvs[i * 2], flat.Uvs[(i * 2) + 1]));
                }

                for (int t = face * 6; t < (face * 6) + 6; t++)
                {
                    tris.Add(start + (flat.Triangles[t] - (face * 4)));
                }
            }
        }
    }
}

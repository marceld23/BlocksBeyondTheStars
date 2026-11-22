// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A form of the shape editors drawn as the world draws it: the polygons of
    /// <see cref="BlockShapeGeometry.LookFacets"/> (per colour slot, chamfered where the tier allows) with the part of its block's tile that lies
    /// where the face lies in the block cell, on the world's block atlas and with the block shader, so the look's relief,
    /// soft edge and joint, grain and calm reach it. The soft edge appears where a face reaches the border of the block
    /// cell (that is where the tile's border is) — as in the world; the small boxes' inner edges stay hard there too.
    /// A dyed form carries the dye as the world's player-dye tint (mode 3). The vertex channels are the ones
    /// <see cref="BlockCubeFaces.BuildMesh"/> fills for a block outside the chunk path (full skylight, no AO shade, the
    /// block's gloss, metal and emission). The caller owns the mesh and the material.
    /// </summary>
    internal static class LookShapePreview
    {
        /// <summary>A block-shader material on the world atlas with its normals bound (and kept bound across a repaint);
        /// null when the shader is missing from the build — the caller keeps its plain preview.</summary>
        public static Material CreateMaterial(BlockTextureAtlas atlas)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/BlockAtlas");
            if (shader == null || atlas == null || atlas.Texture == null)
            {
                return null;
            }

            var material = new Material(shader) { name = "LookShapePreview", mainTexture = atlas.Texture };
            atlas.BindNormals(material);
            return material;
        }

        /// <summary>The form's mesh in cell units (cell (0,0,0) spans 0..1), or null for an empty form.
        /// <paramref name="dye"/> with alpha 0 = undyed.</summary>
        public static Mesh Build(IReadOnlyList<(int X, int Y, int Z, string Bitmap)> cells, GameContent content,
            BlockTextureAtlas atlas, BlockId block, Color dye)
        {
            if (cells == null || content == null || atlas == null)
            {
                return null;
            }

            var tiles = new Rect[BlockCubeFaces.FaceCount];
            BlockCubeFaces.Resolve(content, atlas, block, -1, tiles, new int[BlockCubeFaces.FaceCount]);
            var mesh = new MeshLists();
            foreach (var (cx, cy, cz, bitmap) in cells)
            {
                if (string.IsNullOrEmpty(bitmap))
                {
                    continue;
                }

                // The polygons the world draws — per colour slot, chamfered where the tier does.
                var offset = new Vector3(cx, cy, cz);
                foreach (var facet in BlockShapeGeometry.LookFacets(bitmap, BlockShapeGeometry.SoftEdgesNow))
                {
                    mesh.Facet(facet, offset, tiles);
                }
            }

            return mesh.Verts.Count == 0 ? null : mesh.ToMesh(content, block, dye);
        }

        /// <summary>The growing vertex lists of a form preview.</summary>
        private sealed class MeshLists
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector4> _tangents = new List<Vector4>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _tris = new List<int>();
            private readonly List<int> _slots = new List<int>(); // the colour slot of each vertex (0 = none)

            /// <summary>One polygon of <see cref="BlockShapeGeometry.LookFacets"/> — a quad or a
            /// triangle, maybe a slanted chamfer strip — on the tile of the cube face its normal points to most, with the
            /// tile fractions the world mesher gives it and its colour slot.</summary>
            public void Facet(ShapeFacet f, Vector3 offset, Rect[] tiles)
            {
                var n = new Vector3(f.Normal.X, f.Normal.Y, f.Normal.Z).normalized;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
                int face = axis == 1 ? (n.y >= 0f ? 0 : 1) : axis == 0 ? (n.x >= 0f ? 2 : 3) : (n.z >= 0f ? 4 : 5);
                Rect tile = tiles[face];
                Vector3 uDir = axis == 0 ? Vector3.forward : Vector3.right;
                Vector3 vDir = axis == 1 ? Vector3.forward : Vector3.up;
                float hand = Vector3.Dot(Vector3.Cross(n, uDir), vDir) < 0f ? -1f : 1f;
                int b = Verts.Count;
                int count = f.IsQuad ? 4 : 3;
                for (int c = 0; c < count; c++)
                {
                    var p = c == 0 ? f.A : c == 1 ? f.B : c == 2 ? f.C : f.D;
                    var uv = c == 0 ? f.UvA : c == 1 ? f.UvB : c == 2 ? f.UvC : f.UvD;
                    Verts.Add(new Vector3(p.X, p.Y, p.Z) + offset);
                    _normals.Add(n);
                    _tangents.Add(new Vector4(uDir.x, uDir.y, uDir.z, hand));
                    _uvs.Add(new Vector2(tile.xMin + (Mathf.Clamp01(uv.X) * tile.width), tile.yMin + (Mathf.Clamp01(uv.Y) * tile.height)));
                    _slots.Add(f.Slot);
                }

                _tris.Add(b);
                _tris.Add(b + 1);
                _tris.Add(b + 2);
                if (f.IsQuad)
                {
                    _tris.Add(b);
                    _tris.Add(b + 2);
                    _tris.Add(b + 3);
                }
            }

            public Mesh ToMesh(GameContent content, BlockId block, Color dye)
            {
                int n = Verts.Count;
                Vector2 material = ChunkMesher.MaterialFor(content, block);
                float emission = ChunkMesher.EmissionFor(content, block);
                bool dyed = dye.a > 0.5f;
                Color linearDye = dyed ? ShaderColor.Srgb(dye) : Color.black;
                var sky = new List<Vector2>(n);
                var leaf = new List<Vector4>(n);
                var zero = new List<Vector3>(n);
                var colours = new List<Color>(n);
                for (int i = 0; i < n; i++)
                {
                    float mode = dyed ? 3f : 0f; // the player-dye tint mode when dyed
                    var tint = new Vector4(0f, linearDye.r, linearDye.g, linearDye.b);
                    var colour = new Color(material.x, material.y, 1f, emission); // gloss, metal, no AO shade, emission
                    if (_slots[i] > 1)
                    {
                        ChunkMesher.ApplyColourSlot(_slots[i], ref colour, ref tint, ref mode); // as the world
                    }

                    sky.Add(new Vector2(1f, mode)); // full skylight
                    leaf.Add(tint);
                    zero.Add(Vector3.zero);
                    colours.Add(colour);
                }

                var mesh = new Mesh { name = "LookShapePreview" };
                if (n > 65000)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }

                mesh.SetVertices(Verts);
                mesh.SetNormals(_normals);
                mesh.SetTangents(_tangents);
                mesh.SetUVs(0, _uvs);
                mesh.SetUVs(1, sky);
                mesh.SetUVs(2, leaf);
                mesh.SetUVs(3, zero);
                mesh.SetUVs(4, zero);
                mesh.SetColors(colours);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}

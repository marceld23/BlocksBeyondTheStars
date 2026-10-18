// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2744: a block drawn as a cube OUTSIDE the world — held in the hand, lying as a drop packet, in the ship and
    /// structure editors, as a three-quarter inventory icon — with the faces the world gives it: its own tile, the
    /// log's end grain on top and bottom (#837), the per-side tiles of <c>data/blocks.json</c> (#2124: a machine's casing,
    /// a crate's lid) and a picture front with the top turned to stand upright in front of it. All of those used to show
    /// the block's one tile on every face.
    /// <para>
    /// This is the chunk mesher's per-face lookup restated for one cube (<c>ChunkMesher</c> itself stays untouched — its
    /// output is pinned by golden hashes): the same tiles (<see cref="ShapeFaceTextures.CubeTilesFor"/>,
    /// <see cref="BlockTextureAtlas.TryGetCapTile"/>), the same front rules (<see cref="CubeFacing"/>), the mesher's corner
    /// order per face and its UV quarter turns. The natural blocks' random variant tiles and turns are world-position
    /// noise and have no place on a single cube: it shows the block's own tile, unturned. An EditMode test holds every
    /// block with per-side or cap tiles to the mesher's output face by face.
    /// </para>
    /// Faces are numbered like the mesher's: 0 = +Y, 1 = −Y, 2 = +X, 3 = −X, 4 = +Z, 5 = −Z.
    /// </summary>
    public static class BlockCubeFaces
    {
        public const int FaceCount = 6;
        public const int FacePlusY = 0;
        public const int FaceMinusY = 1;
        public const int FacePlusX = 2;
        public const int FaceMinusX = 3;
        public const int FacePlusZ = 4;
        public const int FaceMinusZ = 5;

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };

        // The corners of each face of the unit cell, in ChunkMesher.FaceQuad's order (the order its UVs and winding
        // follow): corner 0 → 3 is the u direction, corner 0 → 1 the v direction of an unturned face.
        private static readonly Vector3[][] Corners =
        {
            new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, // +Y
            new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) }, // −Y
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, // +X
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, // −X
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, // +Z
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // −Z
        };

        /// <summary>The outward normal of face <paramref name="face"/> (0..5).</summary>
        public static Vector3 Normal(int face) => FaceNormals[face];

        /// <summary>Corner <paramref name="corner"/> (0..3) of face <paramref name="face"/> on the unit cell [0, 1]³, in the
        /// mesher's corner order.</summary>
        public static Vector3 Corner(int face, int corner) => Corners[face][corner];

        /// <summary>The texture coordinate of a face corner: the mesher's tile corners (min/min, min/max, max/max,
        /// max/min) cycled by the face's quarter turns, exactly as its <c>AddFace</c> does.</summary>
        public static Vector2 CornerUv(Rect tile, int turns, int corner) => ((turns + corner) & 3) switch
        {
            0 => new Vector2(tile.xMin, tile.yMin),
            1 => new Vector2(tile.xMin, tile.yMax),
            2 => new Vector2(tile.xMax, tile.yMax),
            _ => new Vector2(tile.xMax, tile.yMin),
        };

        /// <summary>The front face a cube of this block shows (2..5): <paramref name="preferred"/> when it names a side
        /// face, else +Z; -1 for a block without a front (<see cref="Shared.Definitions.BlockDefinition.Facing"/>).</summary>
        public static int FrontFor(GameContent content, BlockId id, int preferred)
        {
            var tiles = ShapeFaceTextures.CubeTilesFor(content, id);
            if (tiles == null || !tiles.Facing)
            {
                return -1;
            }

            return CubeFacing.IsFrontFace(preferred) ? preferred : FacePlusZ;
        }

        /// <summary>
        /// The front of a cube cell in an editor's voxel view, by the world's rules: a front stored in the cell's
        /// descriptor (<see cref="CubeFacing.Pack"/> — the structure editor places a facing block that way, #2260) wins;
        /// a cell without one looks into the open, away from the wall it stands against
        /// (<see cref="CubeFacing.DeriveFront"/>, the flags say whether the cell beyond that side leaves it visible).
        /// -1 for a block without a front.
        /// </summary>
        public static int EditorFront(GameContent content, BlockId id, int descriptor,
            bool openPlusX, bool openMinusX, bool openPlusZ, bool openMinusZ)
        {
            if (FrontFor(content, id, FacePlusZ) < 0)
            {
                return -1;
            }

            int stored = CubeFacing.FrontOf(descriptor);
            return stored >= 0 ? stored : CubeFacing.DeriveFront(openPlusX, openMinusX, openPlusZ, openMinusZ);
        }

        /// <summary>True when not every face of the block shows its own tile — a cap tile or a per-side tile. Only such
        /// blocks need a drawn cube icon; every other block reads fine as its flat tile.</summary>
        public static bool HasDistinctFaces(GameContent content, BlockTextureAtlas atlas, BlockId id)
        {
            if (content == null || atlas == null || id.IsAir)
            {
                return false;
            }

            if (atlas.TryGetCapTile(id.Value, out _))
            {
                return true;
            }

            var tiles = ShapeFaceTextures.CubeTilesFor(content, id);
            if (tiles == null)
            {
                return false;
            }

            foreach (ushort slot in tiles.Slots)
            {
                if (slot != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The atlas slot and the UV quarter turns of each face (index = face, 0..5) — the mesher's choice for a cube of
        /// <paramref name="id"/> whose front is <paramref name="front"/>: the cap tile on top and bottom, a per-side tile
        /// where the block names one, the own tile elsewhere; a top picture turned upright for someone in front. The front
        /// is ignored for a block without one; a block with one and no valid <paramref name="front"/> faces +Z.
        /// </summary>
        public static void ResolveSlots(GameContent content, BlockTextureAtlas atlas, BlockId id, int front, ushort[] slots, int[] turns)
        {
            ushort own = id.Value;
            ushort cap = 0;
            bool hasCap = atlas != null && atlas.TryGetCapTile(own, out cap);
            var cubeTiles = atlas != null ? ShapeFaceTextures.CubeTilesFor(content, id) : null;
            if (cubeTiles == null || !cubeTiles.Facing)
            {
                front = -1;
            }
            else if (!CubeFacing.IsFrontFace(front))
            {
                front = FacePlusZ;
            }

            for (int f = 0; f < FaceCount; f++)
            {
                ushort slot = hasCap && f <= FaceMinusY ? cap : own;
                int turn = 0;
                if (cubeTiles != null)
                {
                    ushort side = cubeTiles.SlotOf(CubeFacing.SideOf(f, front));
                    if (side != 0)
                    {
                        slot = side;
                    }

                    if (f == FacePlusY && front >= 0)
                    {
                        turn = CubeFacing.TopUvTurns(front);
                    }
                }

                slots[f] = slot;
                turns[f] = turn;
            }
        }

        /// <summary>As <see cref="ResolveSlots"/>, with each slot as its atlas UV rect (the whole texture without an
        /// atlas).</summary>
        public static void Resolve(GameContent content, BlockTextureAtlas atlas, BlockId id, int front, Rect[] uvs, int[] turns)
        {
            var slots = new ushort[FaceCount];
            ResolveSlots(content, atlas, id, front, slots, turns);
            for (int f = 0; f < FaceCount; f++)
            {
                uvs[f] = atlas != null ? atlas.TileUv(slots[f]) : new Rect(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>
        /// A unit cube of the block centred on the origin (the extent of Unity's primitive cube, so it drops into a
        /// primitive's place): 24 vertices, the mesher's corners, UVs, quarter turns, tangents and winding per face.
        /// <paramref name="chunkChannels"/> adds what the chunk shader reads besides (#972, as the drop packets always
        /// filled it): full skylight and the plain tint mode in TEXCOORD1, empty TEXCOORD2–4, and gloss, metal, no AO
        /// shade and the block's emission in the vertex colour. The caller owns the mesh.
        /// </summary>
        public static Mesh BuildMesh(GameContent content, BlockTextureAtlas atlas, BlockId id, int front, bool chunkChannels)
        {
            var uvRects = new Rect[FaceCount];
            var turns = new int[FaceCount];
            Resolve(content, atlas, id, front, uvRects, turns);

            const int n = FaceCount * 4;
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var tangents = new Vector4[n];
            var uvs = new Vector2[n];
            var tris = new int[FaceCount * 6];
            var half = new Vector3(0.5f, 0.5f, 0.5f);
            for (int f = 0; f < FaceCount; f++)
            {
                var q = Corners[f];
                // The mesher's tangent: the u direction (corner 0 → 3), w = the bitangent handedness.
                Vector3 e1 = q[1] - q[0], e3 = q[3] - q[0];
                Vector3 nrm = Vector3.Cross(e1, e3).normalized;
                Vector3 tan = e3.normalized;
                float hand = Vector3.Dot(Vector3.Cross(nrm, tan), e1) < 0f ? -1f : 1f;
                var tv = new Vector4(tan.x, tan.y, tan.z, hand);
                int b = f * 4;
                for (int i = 0; i < 4; i++)
                {
                    verts[b + i] = q[i] - half;
                    normals[b + i] = FaceNormals[f];
                    tangents[b + i] = tv;
                    uvs[b + i] = CornerUv(uvRects[f], turns[f], i);
                }

                int t = f * 6;
                tris[t] = b;
                tris[t + 1] = b + 1;
                tris[t + 2] = b + 2;
                tris[t + 3] = b;
                tris[t + 4] = b + 2;
                tris[t + 5] = b + 3;
            }

            var mesh = new Mesh { name = "BlockCube" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uvs;
            if (chunkChannels)
            {
                Vector2 material = content != null ? ChunkMesher.MaterialFor(content, id) : Vector2.zero;
                float emission = content != null ? ChunkMesher.EmissionFor(content, id) : 0f;
                var sky = new Vector2[n];
                var zero4 = new Vector4[n];
                var colors = new Color[n];
                var col = new Color(material.x, material.y, 1f, emission); // gloss, metal, no AO shade, emission
                for (int i = 0; i < n; i++)
                {
                    sky[i] = new Vector2(1f, 0f); // full skylight, plain tint mode
                    colors[i] = col;
                }

                mesh.SetUVs(1, sky);
                mesh.SetUVs(2, zero4);
                mesh.SetUVs(3, zero4);
                mesh.SetUVs(4, zero4);
                mesh.colors = colors;
            }

            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Frees a code-created asset (mesh, material, texture): <c>Object.Destroy</c> in play mode,
        /// <c>DestroyImmediate</c> in the EditMode tests, where Destroy only logs an error.</summary>
        internal static void DestroyAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(asset);
            }
            else
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}

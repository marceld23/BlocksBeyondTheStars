// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2744: a block drawn as a cube outside the world — held, dropped, in the editors, as an icon — wears the faces the
    /// world mesher gives it (<see cref="BlockCubeFaces"/>), not its one tile on every side. Meshed with the real atlas.
    /// </summary>
    public sealed class BlockCubeFacesEditModeTests
    {
        private const int C = 8; // chunk interior
        private const string PictureBlock = "station_vendor"; // a picture front, a casing on its sides, a top and a bottom
        private const float Eps = 1e-5f;

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        private static void WithAtlas(System.Action<GameContent, BlockTextureAtlas> body)
        {
            var content = LoadContentOrIgnore();
            LogAssert.ignoreFailingMessages = true; // BlockTextureAtlas' per-tile Object.Destroy logs an error outside play mode
            var atlas = new BlockTextureAtlas(content);
            try
            {
                body(content, atlas);
            }
            finally
            {
                BlockCubeIcon.ClearCache();
                HeldItem.MeshKitResolver = null;
                HeldItem.ReleasePartMaterials();
                Object.DestroyImmediate(atlas.NormalTexture); // EditMode: Object.Destroy logs an error outside play mode
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static BlockId Id(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId;
        }

        /// <summary>The face (0..5) an outward normal belongs to, or -1 for a slanted one (a bevel strip).</summary>
        private static int FaceOf(Vector3 normal)
        {
            for (int f = 0; f < BlockCubeFaces.FaceCount; f++)
            {
                if (Vector3.Dot(normal.normalized, BlockCubeFaces.Normal(f)) > 0.99f)
                {
                    return f;
                }
            }

            return -1;
        }

        /// <summary>The corner index (0..3, the mesher's order) of a unit-cell point on face <paramref name="face"/>; a
        /// bevelled corner sits a little inside the cell, so the point is rounded first.</summary>
        private static int CornerOf(int face, Vector3 local)
        {
            var rounded = new Vector3(Mathf.Round(local.x), Mathf.Round(local.y), Mathf.Round(local.z));
            for (int k = 0; k < 4; k++)
            {
                if ((BlockCubeFaces.Corner(face, k) - rounded).sqrMagnitude < 1e-6f)
                {
                    return k;
                }
            }

            return -1;
        }

        private static void AssertUv(Vector2 expected, Vector2 actual, string what)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Eps), what + " (u)");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Eps), what + " (v)");
        }

        /// <summary>The blocks this is about: a cap tile, a per-side tile or a front.</summary>
        private static List<BlockId> FacedBlocks(GameContent content, BlockTextureAtlas atlas)
        {
            var list = new List<BlockId>();
            foreach (var def in content.Blocks.Values)
            {
                var id = def.NumericId;
                if (id.Value != 0 && (BlockCubeFaces.HasDistinctFaces(content, atlas, id) || BlockCubeFaces.FrontFor(content, id, BlockCubeFaces.FacePlusZ) >= 0))
                {
                    list.Add(id);
                }
            }

            return list;
        }

        [Test]
        public void EveryFacedBlock_ResolvesTheFacesTheWorldMesherDraws()
        {
            WithAtlas((content, atlas) =>
            {
                var blocks = FacedBlocks(content, atlas);
                Assert.That(blocks.Count, Is.GreaterThan(10), "Control: content has machines with casings, crates, logs …");
                var uvs = new Rect[BlockCubeFaces.FaceCount];
                var turns = new int[BlockCubeFaces.FaceCount];
                foreach (var id in blocks)
                {
                    string key = content.BlockById(id)?.Key;
                    bool facing = BlockCubeFaces.FrontFor(content, id, BlockCubeFaces.FacePlusZ) >= 0;
                    var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
                    chunk.Set(C, C, C, id);
                    if (facing)
                    {
                        chunk.SetShape(C, C, C, CubeFacing.Pack(BlockCubeFaces.FacePlusZ)); // a stored front: +Z
                    }

                    BlockId World(int x, int y, int z) =>
                        x >= 0 && y >= 0 && z >= 0 &&
                        x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                            ? chunk.Get(x, y, z)
                            : BlockId.Air;

                    BlockCubeFaces.Resolve(content, atlas, id, BlockCubeFaces.FacePlusZ, uvs, turns);
                    var data = ChunkMesher.BuildGeometry(chunk, content, World, atlas);
                    try
                    {
                        var seen = new HashSet<int>();
                        var perFace = new int[BlockCubeFaces.FaceCount];
                        foreach (var tris in new[] { data.OpaqueTris, data.TransparentTris })
                        {
                            foreach (int i in tris)
                            {
                                if (!seen.Add(i))
                                {
                                    continue;
                                }

                                int f = FaceOf(data.Normals[i]);
                                if (f < 0)
                                {
                                    continue; // a bevel strip or corner
                                }

                                int k = CornerOf(f, data.Verts[i] - new Vector3(C, C, C));
                                Assert.That(k, Is.GreaterThanOrEqualTo(0), $"'{key}': a face vertex at {data.Verts[i]} is no cell corner.");
                                AssertUv(BlockCubeFaces.CornerUv(uvs[f], turns[f], k), data.Uvs[i], $"'{key}' face {f} corner {k}");
                                perFace[f]++;
                            }
                        }

                        for (int f = 0; f < BlockCubeFaces.FaceCount; f++)
                        {
                            Assert.That(perFace[f], Is.EqualTo(4), $"'{key}': the world should draw face {f} of a lone cube once.");
                        }
                    }
                    finally
                    {
                        data.Release();
                    }
                }
            });
        }

        [Test]
        public void TheFacesDiffer_WhereTheDataSaysSo()
        {
            WithAtlas((content, atlas) =>
            {
                var id = Id(content, PictureBlock);
                var uvs = new Rect[BlockCubeFaces.FaceCount];
                var turns = new int[BlockCubeFaces.FaceCount];
                BlockCubeFaces.Resolve(content, atlas, id, BlockCubeFaces.FaceMinusZ, uvs, turns);
                var own = atlas.TileUv(id.Value);
                Assert.That(uvs[BlockCubeFaces.FaceMinusZ], Is.EqualTo(own), "The picture is the front.");
                Assert.That(uvs[BlockCubeFaces.FacePlusX], Is.Not.EqualTo(own), "The sides wear the casing.");
                Assert.That(uvs[BlockCubeFaces.FacePlusY], Is.Not.EqualTo(own), "The top has its own tile.");
                Assert.That(uvs[BlockCubeFaces.FacePlusZ], Is.EqualTo(uvs[BlockCubeFaces.FacePlusX]), "The back is a side.");

                var log = Id(content, "wood_log");
                BlockCubeFaces.Resolve(content, atlas, log, -1, uvs, turns);
                Assert.That(uvs[BlockCubeFaces.FacePlusY], Is.Not.EqualTo(atlas.TileUv(log.Value)), "A log shows its end grain on top.");
                Assert.That(uvs[BlockCubeFaces.FacePlusX], Is.EqualTo(atlas.TileUv(log.Value)), "… and its bark on the sides.");
            });
        }

        [Test]
        public void TheHeldPictureBlock_ShowsItsFrontToTheEye_Upright()
        {
            WithAtlas((content, atlas) =>
            {
                var id = Id(content, PictureBlock);
                HeldItem.MeshKitResolver = () => (content, atlas, (Material)null, (Material)null);
                var root = new GameObject("HeldTestRoot");
                try
                {
                    var holder = HeldItem.Build(root.transform, HeldItem.Kind.Block, Color.white, blockKey: PictureBlock);
                    Assert.IsNotNull(holder);
                    var block = holder.transform.GetChild(0);
                    var mesh = block.GetComponent<MeshFilter>().sharedMesh;
                    var material = block.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(material.mainTexture, Is.SameAs(atlas.Texture), "The held cube samples the block atlas.");
                    Assert.That(material.mainTextureScale, Is.EqualTo(Vector2.one), "… with its own per-face UVs, no tile transform.");

                    // The holder points along +Z: the eye looks along +Z and sees the −Z face, +X to its right.
                    var tile = atlas.TileUv(id.Value);
                    var verts = mesh.vertices;
                    var normals = mesh.normals;
                    var uv = mesh.uv;
                    int front = 0;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        if (FaceOf(normals[i]) != BlockCubeFaces.FaceMinusZ)
                        {
                            continue;
                        }

                        front++;
                        Assert.That(uv[i].x >= tile.xMin - Eps && uv[i].x <= tile.xMax + Eps && uv[i].y >= tile.yMin - Eps && uv[i].y <= tile.yMax + Eps,
                            Is.True, $"The face toward the eye shows the block's own picture tile, got {uv[i]}.");
                        Assert.That(uv[i].y, Is.EqualTo(verts[i].y < 0f ? tile.yMin : tile.yMax).Within(Eps), "The picture stands upright (its bottom down).");
                        Assert.That(uv[i].x, Is.EqualTo(verts[i].x < 0f ? tile.xMin : tile.xMax).Within(Eps), "… and reads left to right.");
                    }

                    Assert.That(front, Is.EqualTo(4), "One −Z face.");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            });
        }

        [Test]
        public void TheDropCube_HasTheResolvedFaces_AndTheChunkShaderChannels()
        {
            WithAtlas((content, atlas) =>
            {
                foreach (string key in new[] { PictureBlock, "wood_log", "crate" })
                {
                    var id = Id(content, key);
                    var uvs = new Rect[BlockCubeFaces.FaceCount];
                    var turns = new int[BlockCubeFaces.FaceCount];
                    BlockCubeFaces.Resolve(content, atlas, id, BlockCubeFaces.FacePlusZ, uvs, turns);
                    var mesh = BlockCubeFaces.BuildMesh(content, atlas, id, BlockCubeFaces.FacePlusZ, chunkChannels: true);
                    try
                    {
                        var verts = mesh.vertices;
                        var normals = mesh.normals;
                        var uv = mesh.uv;
                        Assert.That(verts.Length, Is.EqualTo(24));
                        var min = new Vector2[BlockCubeFaces.FaceCount];
                        var max = new Vector2[BlockCubeFaces.FaceCount];
                        for (int f = 0; f < BlockCubeFaces.FaceCount; f++)
                        {
                            min[f] = new Vector2(float.MaxValue, float.MaxValue);
                            max[f] = new Vector2(float.MinValue, float.MinValue);
                        }

                        for (int i = 0; i < verts.Length; i++)
                        {
                            int f = FaceOf(normals[i]);
                            Assert.That(f, Is.GreaterThanOrEqualTo(0));
                            min[f] = Vector2.Min(min[f], uv[i]);
                            max[f] = Vector2.Max(max[f], uv[i]);
                            int k = CornerOf(f, verts[i] + new Vector3(0.5f, 0.5f, 0.5f));
                            AssertUv(BlockCubeFaces.CornerUv(uvs[f], turns[f], k), uv[i], $"'{key}' face {f} corner {k}");
                        }

                        for (int f = 0; f < BlockCubeFaces.FaceCount; f++)
                        {
                            AssertUv(uvs[f].min, min[f], $"'{key}' face {f} lower bound");
                            AssertUv(uvs[f].max, max[f], $"'{key}' face {f} upper bound");
                        }

                        var sky = new List<Vector2>();
                        mesh.GetUVs(1, sky);
                        Assert.That(sky.Count, Is.EqualTo(24), $"'{key}': the chunk shader reads TEXCOORD1.");
                        foreach (var s in sky)
                        {
                            Assert.That(s, Is.EqualTo(new Vector2(1f, 0f)), "Full skylight, plain tint mode — not a half-dark cube (#2105).");
                        }

                        Assert.That(mesh.colors.Length, Is.EqualTo(24), $"'{key}': gloss, metal, AO and emission ride in the vertex colour.");
                        Assert.That(mesh.tangents.Length, Is.EqualTo(24), $"'{key}': the normal map needs tangents.");
                    }
                    finally
                    {
                        Object.DestroyImmediate(mesh);
                    }
                }
            });
        }

        [Test]
        public void AnEditorFacingBlock_LooksAwayFromItsWall_UnlessItStoresAFront()
        {
            WithAtlas((content, atlas) =>
            {
                var id = Id(content, PictureBlock);
                // A wall behind it at −Z, open everywhere else: the front looks to +Z, into the room.
                Assert.That(BlockCubeFaces.EditorFront(content, id, 0, openPlusX: true, openMinusX: true, openPlusZ: true, openMinusZ: false),
                    Is.EqualTo(BlockCubeFaces.FacePlusZ));
                // A wall at +X: the front looks to −X.
                Assert.That(BlockCubeFaces.EditorFront(content, id, 0, openPlusX: false, openMinusX: true, openPlusZ: true, openMinusZ: true),
                    Is.EqualTo(BlockCubeFaces.FaceMinusX));
                // A front the structure editor stored wins over the walls (#2260).
                Assert.That(BlockCubeFaces.EditorFront(content, id, CubeFacing.Pack(BlockCubeFaces.FacePlusX), true, true, true, false),
                    Is.EqualTo(BlockCubeFaces.FacePlusX));
                // A block without a front has none.
                Assert.That(BlockCubeFaces.EditorFront(content, Id(content, "crate"), 0, true, true, true, false), Is.EqualTo(-1));

                // And that front gets the picture: the open +Z face, not the one against the wall.
                var uvs = new Rect[BlockCubeFaces.FaceCount];
                var turns = new int[BlockCubeFaces.FaceCount];
                BlockCubeFaces.Resolve(content, atlas, id, BlockCubeFaces.FacePlusZ, uvs, turns);
                Assert.That(uvs[BlockCubeFaces.FacePlusZ], Is.EqualTo(atlas.TileUv(id.Value)));
                Assert.That(uvs[BlockCubeFaces.FaceMinusZ], Is.Not.EqualTo(atlas.TileUv(id.Value)));
                Assert.That(turns[BlockCubeFaces.FacePlusY], Is.EqualTo(CubeFacing.TopUvTurns(BlockCubeFaces.FacePlusZ)),
                    "The top picture turns to stand upright for someone in front.");
            });
        }

        [Test]
        public void ABlockWithDistinctFaces_GetsACachedCubeIcon_APlainBlockKeepsItsTile()
        {
            WithAtlas((content, atlas) =>
            {
                ushort picture = Id(content, PictureBlock).Value;
                var icon = BlockCubeIcon.For(atlas, picture);
                Assert.IsNotNull(icon, "A machine with a casing is drawn as a cube.");
                Assert.That(icon.width, Is.EqualTo(BlockCubeIcon.Size));
                Assert.That(BlockCubeIcon.For(atlas, picture), Is.SameAs(icon), "Built once, then cached.");
                Assert.IsNotNull(BlockCubeIcon.For(atlas, Id(content, "wood_log").Value), "A log has its end grain on top.");
                Assert.IsNull(BlockCubeIcon.For(atlas, Id(content, "stone").Value), "Stone is stone on every side — its flat tile says it all.");

                // Not the flat tile: the corners around the cube are empty, its middle is drawn.
                var pixels = icon.GetPixels();
                int n = BlockCubeIcon.Size;
                Assert.That(pixels[0].a, Is.EqualTo(0f), "Bottom-left corner: outside the cube.");
                Assert.That(pixels[(n - 1) * n].a, Is.EqualTo(0f), "Top-left corner: outside the cube.");
                int drawn = 0;
                foreach (var p in pixels)
                {
                    if (p.a > 0.5f)
                    {
                        drawn++;
                    }
                }

                Assert.That(drawn, Is.GreaterThan(n * n / 3), "The cube fills much of the icon.");

                // The image gets the icon with the whole texture; a plain block gets the atlas tile.
                var go = new GameObject("IconTest", typeof(RectTransform));
                try
                {
                    var raw = go.AddComponent<UnityEngine.UI.RawImage>();
                    BlockCubeIcon.Apply(raw, atlas, picture);
                    Assert.That(raw.texture, Is.SameAs(icon));
                    Assert.That(raw.uvRect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
                    ushort stone = Id(content, "stone").Value;
                    BlockCubeIcon.Apply(raw, atlas, stone);
                    Assert.That(raw.texture, Is.SameAs(atlas.Texture));
                    Assert.That(raw.uvRect, Is.EqualTo(atlas.TileUv(stone)));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            });
        }
    }
}

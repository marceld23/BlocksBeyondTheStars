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
    /// #2519 / #2520: the cutout code the mesher writes into TEXCOORD2.x decides how a vertex moves in the wind
    /// (BbtsBlockSway). Torches and lanterns are RIGID cutouts — they swayed like plants, deep underground too — cross
    /// plants bend from the root (under water with their own aquatic code, which skips the shelter ramp), crown faces
    /// sway as one piece. The Crystal Net's unlit twins (torch_off / lantern_off)
    /// are the same slim props: a cross billboard without a collider, not a full cube wearing the darkened tile.
    /// Meshed with the real atlas (the cross-billboard path needs one), one block per chunk.
    /// </summary>
    public sealed class ChunkMesherCutoutCodeEditModeTests
    {
        private const int C = 8; // chunk interior

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        /// <summary>The OPAQUE-submesh vertices of a meshed chunk (the BlockAtlas geometry the cutout code is for — water
        /// drawn around a wet plant goes to the transparent submesh and carries its own data in TEXCOORD2).</summary>
        private sealed class Meshed
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector4> Leaf = new List<Vector4>();
            public readonly List<Vector2> Sky = new List<Vector2>(); // TEXCOORD1: x = skylight, y = tint mode (+ animation code)
            public int ColliderTris;
        }

        private static BlockId Id(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId;
        }

        /// <summary>Meshes a chunk holding the one block <paramref name="key"/> at its centre — in open air, or with
        /// <paramref name="inWater"/> under water (water above it and on all four sides). <paramref name="roofed"/> puts one
        /// stone block two cells above it: the cell above the block is then under cover while the cells beside it stay open,
        /// so a per-face skylight would differ between its top and side faces.</summary>
        private static Meshed MeshSingle(GameContent content, BlockTextureAtlas atlas, string key, bool inWater = false, bool roofed = false)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(C, C, C, Id(content, key));
            if (inWater)
            {
                var water = Id(content, "water");
                chunk.Set(C, C + 1, C, water);
                chunk.Set(C + 1, C, C, water);
                chunk.Set(C - 1, C, C, water);
                chunk.Set(C, C, C + 1, water);
                chunk.Set(C, C, C - 1, water);
            }

            if (roofed)
            {
                chunk.Set(C, C + 2, C, Id(content, "stone"));
            }

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World, atlas);
            try
            {
                var m = new Meshed { ColliderTris = data.ColliderTris.Count / 3 };
                var seen = new HashSet<int>();
                foreach (int i in data.OpaqueTris)
                {
                    if (seen.Add(i))
                    {
                        m.Verts.Add(data.Verts[i]);
                        m.Leaf.Add(data.LeafUv[i]);
                        m.Sky.Add(data.SkyUv[i]);
                    }
                }

                return m;
            }
            finally
            {
                data.Release();
            }
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
                Object.DestroyImmediate(atlas.NormalTexture); // EditMode: Object.Destroy logs an error outside play mode
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static void AssertAllCodes(Meshed m, float code, string key)
        {
            Assert.That(m.Leaf.Count, Is.GreaterThan(0), $"'{key}' must render.");
            foreach (var leaf in m.Leaf)
            {
                Assert.That(leaf.x, Is.EqualTo(code), $"'{key}' must carry cutout code {code} on every vertex.");
            }
        }

        [TestCase("torch")]
        [TestCase("lantern")]
        [TestCase("torch_off")]
        [TestCase("lantern_off")]
        public void TorchAndLanternProps_AreRigidCutoutBillboards_WithoutACollider(string key)
        {
            WithAtlas((content, atlas) =>
            {
                var m = MeshSingle(content, atlas, key);
                AssertAllCodes(m, ChunkMesher.LeafCodeRigid, key);
                Assert.That(m.ColliderTris, Is.Zero, $"'{key}' is a slim prop you walk past — no collider.");

                // A cross billboard stays inside its cell's middle; a full cube would put vertices on the cell walls.
                foreach (var v in m.Verts)
                {
                    float fx = v.x - C, fz = v.z - C;
                    Assert.That(fx > 0.05f && fx < 0.95f && fz > 0.05f && fz < 0.95f, Is.True,
                        $"'{key}' has a vertex at {v} — on the cell wall, i.e. meshed as a cube instead of a prop.");
                }
            });
        }

        [TestCase("flora_fern")]
        [TestCase("flora_grasstuft")]
        public void CrossPlants_ArePlantCutouts(string key)
        {
            WithAtlas((content, atlas) => AssertAllCodes(MeshSingle(content, atlas, key), ChunkMesher.LeafCodePlant, key));
        }

        /// <summary>Under water the skylight counts the water as cover, so a sheltered plant code would freeze kelp and
        /// seagrass — Marcel's call: underwater plants keep their sway (it reads as current). A plant the water surrounds
        /// carries the aquatic code; the same species on dry land is a plain plant.</summary>
        [TestCase("flora_seagrass")]
        [TestCase("flora_kelp")]
        public void CrossPlants_TheWaterSurrounds_AreAquaticCutouts(string key)
        {
            WithAtlas((content, atlas) =>
            {
                AssertAllCodes(MeshSingle(content, atlas, key, inWater: true), ChunkMesher.LeafCodeAquatic, key + " in water");
                AssertAllCodes(MeshSingle(content, atlas, key), ChunkMesher.LeafCodePlant, key + " on dry land");
            });
        }

        [Test]
        public void ATorchUnderWater_StaysRigid()
        {
            WithAtlas((content, atlas) => AssertAllCodes(MeshSingle(content, atlas, "torch", inWater: true), ChunkMesher.LeafCodeRigid, "torch in water"));
        }

        /// <summary>The shader scales the sway of plants AND solid flora by their skylight (BbtsSwayShelter) — so cave
        /// caps and spore pods stand still underground. That is crack-free only while every vertex of one block carries
        /// the same skylight: solid flora is meshed as one shaped form with one skylight value (the open sky above it),
        /// never through the cube-face path, whose skylight is per face. Under a one-block roof the cell above the block
        /// is covered while the cells beside it are open, so per-face values would differ — the crown control shows the
        /// scene tells them apart (and is why crown faces stay unscaled).</summary>
        [TestCase("flora_puffball")]
        [TestCase("flora_cavecap")]
        [TestCase("flora_mushroom")]
        public void SolidFlora_CarriesOneSkylightValue_SoTheShelterScaleCannotCrackIt(string key)
        {
            WithAtlas((content, atlas) =>
            {
                var m = MeshSingle(content, atlas, key, roofed: true);
                var flora = new List<float>();
                for (int i = 0; i < m.Sky.Count; i++)
                {
                    float mode = m.Sky[i].y % 16f;
                    if (mode > 0.5f && mode < 1.5f)
                    {
                        Assert.That(m.Leaf[i].x, Is.EqualTo(0f), $"'{key}' is solid flora — no cutout code.");
                        flora.Add(m.Sky[i].x);
                    }
                }

                Assert.That(flora.Count, Is.GreaterThan(0), $"'{key}' must render in the flora tint mode.");
                foreach (float sky in flora)
                {
                    Assert.That(sky, Is.EqualTo(flora[0]).Within(1e-6f),
                        $"'{key}' carries more than one skylight value — the shelter-scaled sway would open cracks between its faces.");
                }

                var crown = MeshSingle(content, atlas, "tree_leaves", roofed: true);
                bool differs = false;
                foreach (var s in crown.Sky)
                {
                    differs |= s.y % 16f > 0.5f && s.y % 16f < 1.5f && Mathf.Abs(s.x - crown.Sky[0].x) > 1e-3f;
                }

                Assert.That(differs, Is.True, "Control: under the roof a cube's faces must see different skylight, or this test proves nothing.");
            });
        }

        [Test]
        public void CrownFaces_AreCrownCutouts()
        {
            WithAtlas((content, atlas) => AssertAllCodes(MeshSingle(content, atlas, "tree_leaves"), ChunkMesher.LeafCodeCrown, "tree_leaves"));
        }

        [Test]
        public void TheCutoutCodes_StayExactInTheHalfPrecisionChannel()
        {
            // The packed vertex stream stores TEXCOORD2 as Float16; the shader tells the codes apart by ±0.5 bands.
            foreach (float code in new[] { ChunkMesher.LeafCodeCrown, ChunkMesher.LeafCodeRigid, ChunkMesher.LeafCodePlant, ChunkMesher.LeafCodeAquatic })
            {
                Assert.That(Mathf.HalfToFloat(Mathf.FloatToHalf(code)), Is.EqualTo(code));
            }
        }
    }
}

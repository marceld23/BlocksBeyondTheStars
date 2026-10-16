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
    /// #2683 — "the torch stands as if it were on the floor; placed on a wall it should hang on the wall!" A light whose
    /// descriptor carries a wall mount is meshed against that wall (its quads crowd the wall side of the cell instead of
    /// standing in the middle), a lantern carrying the ceiling mount hangs from the top of its cell, and a light without
    /// a mount (every light placed before #2683) still stands in the middle exactly as before. Meshed with the real atlas
    /// (the slim-prop path needs one); the light's own vertices are the ones with the rigid cutout code.
    /// </summary>
    public sealed class ChunkMesherLightMountEditModeTests
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

        private static BlockId Id(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId;
        }

        /// <summary>The light's vertices (rigid cutout code), cell-local: a chunk holding <paramref name="key"/> at its centre
        /// with <paramref name="descriptor"/>, a stone at <paramref name="support"/> (cell-local offset) if given.</summary>
        private static List<Vector3> MeshLight(GameContent content, BlockTextureAtlas atlas, string key, int descriptor, Vector3Int? support)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(C, C, C, Id(content, key));
            chunk.SetShape(C, C, C, descriptor);
            if (support is { } s)
            {
                chunk.Set(C + s.x, C + s.y, C + s.z, Id(content, "stone"));
            }

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World, atlas);
            try
            {
                var verts = new List<Vector3>();
                var seen = new HashSet<int>();
                foreach (int i in data.OpaqueTris)
                {
                    if (seen.Add(i) && Mathf.Approximately(data.LeafUv[i].x, ChunkMesher.LeafCodeRigid))
                    {
                        verts.Add(data.Verts[i] - new Vector3(C, C, C));
                    }
                }

                Assert.That(verts.Count, Is.GreaterThan(0), $"'{key}' must render.");
                return verts;
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
                Object.DestroyImmediate(atlas.NormalTexture);
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        private static float MeanX(List<Vector3> verts)
        {
            float sum = 0f;
            foreach (var v in verts)
            {
                sum += v.x;
            }

            return sum / verts.Count;
        }

        [TestCase("torch")]
        [TestCase("lantern")]
        [TestCase("torch_off")]
        public void ALightOnAWall_IsMeshedAgainstThatWall(string key)
        {
            WithAtlas((content, atlas) =>
            {
                // The wall at −X; the light's up-face +X (2) points away from it.
                var onWall = MeshLight(content, atlas, key, ShapeCode.Pack(PropShapes.LightMountShape, 0, 2), new Vector3Int(-1, 0, 0));
                var unstamped = MeshLight(content, atlas, key, 0, new Vector3Int(-1, 0, 0));

                Assert.That(MeanX(unstamped), Is.EqualTo(0.5f).Within(0.02f), "an unstamped light still stands in the middle of its cell");
                Assert.That(MeanX(onWall), Is.LessThan(0.4f), $"'{key}' on a wall must crowd the wall side of its cell");
                float nearest = float.MaxValue;
                foreach (var v in onWall)
                {
                    nearest = Mathf.Min(nearest, v.x);
                    Assert.That(v.x, Is.GreaterThan(-0.1f), $"'{key}' reaches {v} — through its wall");
                    Assert.That(v.y, Is.InRange(0f, 1f));
                    Assert.That(v.z, Is.InRange(0f, 1f));
                }

                Assert.That(nearest, Is.LessThan(0.05f), $"'{key}' on a wall must touch it");
            });
        }

        [Test]
        public void ALanternOnTheCeilingMount_HangsFromTheTopOfItsCell()
        {
            WithAtlas((content, atlas) =>
            {
                var hanging = MeshLight(content, atlas, "lantern", ShapeCode.Pack(PropShapes.LightMountShape, 0, 1), new Vector3Int(0, 1, 0));
                float low = float.MaxValue, high = float.MinValue;
                foreach (var v in hanging)
                {
                    low = Mathf.Min(low, v.y);
                    high = Mathf.Max(high, v.y);
                }

                Assert.That(low, Is.GreaterThan(0.25f), "a hanging lantern is up near the ceiling, not on the floor");
                Assert.That(high, Is.GreaterThan(0.99f), "its chain reaches the ceiling");
            });
        }
    }
}

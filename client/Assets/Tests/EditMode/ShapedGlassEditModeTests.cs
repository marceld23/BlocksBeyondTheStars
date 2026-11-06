// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    /// #3287: every cube material takes a form — glass too. A shaped see-through cell must be drawn in the transparent
    /// submesh like a glass cube (and clear glass keep its clear-pane code), or a glass stair renders as a solid block.
    /// Meshed with the real atlas.
    /// </summary>
    public sealed class ShapedGlassEditModeTests
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

        private static ChunkMeshData MeshOneShaped(GameContent content, BlockTextureAtlas atlas, string key, BlockShape shape)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(C, C, C, def.NumericId);
            chunk.SetShape(C, C, C, ShapeCode.Pack(shape, 0));

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            return ChunkMesher.BuildGeometry(chunk, content, World, atlas);
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

        [TestCase("glass")]
        [TestCase("glass_clear")]
        public void AShapedGlassStair_IsDrawnSeeThrough(string key)
        {
            WithAtlas((content, atlas) =>
            {
                var data = MeshOneShaped(content, atlas, key, BlockShape.Stairs);
                Assert.That(data.TransparentTris.Count, Is.GreaterThan(0), $"a {key} stair belongs to the transparent submesh");
                Assert.That(data.OpaqueTris.Count, Is.EqualTo(0), $"no face of a {key} stair may be drawn opaque");
            });
        }

        [Test]
        public void AClearGlassStair_KeepsTheClearPaneCode()
        {
            WithAtlas((content, atlas) =>
            {
                var data = MeshOneShaped(content, atlas, "glass_clear", BlockShape.Stairs);
                foreach (int i in data.TransparentTris)
                {
                    Assert.That(data.LeafUv[i].x, Is.EqualTo(-1f), "clear glass is marked by leaf code -1, like the clear cube");
                }
            });
        }

        [Test]
        public void AShapedStoneStair_StaysOpaque()
        {
            WithAtlas((content, atlas) =>
            {
                var data = MeshOneShaped(content, atlas, "stone", BlockShape.Stairs);
                Assert.That(data.OpaqueTris.Count, Is.GreaterThan(0), "control: an opaque material still meshes opaque");
                Assert.That(data.TransparentTris.Count, Is.EqualTo(0));
            });
        }
    }
}

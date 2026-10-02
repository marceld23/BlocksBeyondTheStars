// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2184: a tree crown is walked through like a plant. Its cells still render (the cutout leaves), but they emit
    /// no collider; the trunk stays solid — including the faces it turns toward its own leaves, because foliage never
    /// seals a neighbour's face. Headless with a null atlas, like the flora-floor tests: collision does not need textures.
    /// </summary>
    public sealed class ChunkMesherTreeFoliageEditModeTests
    {
        private const int C = 8; // chunk interior, so all neighbours are in-chunk

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        private static BlockId IdOf(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId;
        }

        /// <summary>Meshes a one-chunk world and returns (rendered tris, collider tris).</summary>
        private static (int Render, int Collider) Mesh(GameContent content, System.Action<ChunkData> fill)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            fill(chunk);

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World); // atlas = null → no textures (EditMode-safe)
            try
            {
                return ((data.OpaqueTris.Count + data.TransparentTris.Count) / 3, data.ColliderTris.Count / 3);
            }
            finally
            {
                data.Release();
            }
        }

        [Test]
        public void EveryCrownCell_RendersButHasNoCollider()
        {
            var content = LoadContentOrIgnore();
            foreach (var key in TreeFoliage.Keys)
            {
                var id = IdOf(content, key);
                var (render, collider) = Mesh(content, c => c.Set(C, C, C, id));
                Assert.That(render, Is.GreaterThan(0), $"'{key}' must still be drawn.");
                Assert.That(collider, Is.Zero, $"'{key}' is walked through — it must not emit a collider.");
            }
        }

        [Test]
        public void ATrunkInsideItsCrown_KeepsAColliderOnEverySide()
        {
            var content = LoadContentOrIgnore();
            var log = IdOf(content, "wood_log");
            var leaves = IdOf(content, TreeFoliage.LeavesKey);
            var (_, collider) = Mesh(content, c =>
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            c.Set(C + dx, C + dy, C + dz, leaves);
                        }

                c.Set(C, C, C, log);
            });

            // Six faces, two triangles each: the leaves around it take none of the trunk's collider away and add none.
            Assert.That(collider, Is.EqualTo(12), "the trunk keeps all six collider faces inside its crown, the leaves add none");
        }
    }
}

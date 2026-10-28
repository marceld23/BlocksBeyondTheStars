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

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2984 (Lyxette's roof-garden pond on a clear-glass floor): a see-through solid draws its faces only toward air, but
    /// its collision face must not follow the picture — with water, a torch or a plant on top the glass top used to have no
    /// collider and the player sank through the floor. Glass on glass still gets no inner face.
    /// </summary>
    public sealed class ChunkMesherSeeThroughColliderEditModeTests
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

        /// <summary>Meshes <paramref name="floor"/> at the chunk centre with <paramref name="above"/> (or air) on top and
        /// answers whether the collision mesh has a face in the plane y = C + 1 over that cell.</summary>
        private static bool HasColliderTop(GameContent content, string floor, string above)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(C, C, C, Id(content, floor));
            if (above != null)
            {
                chunk.Set(C, C + 1, C, Id(content, above));
            }

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World);
            try
            {
                for (int t = 0; t + 2 < data.ColliderTris.Count; t += 3)
                {
                    var a = data.ColliderVerts[data.ColliderTris[t]];
                    var b = data.ColliderVerts[data.ColliderTris[t + 1]];
                    var c = data.ColliderVerts[data.ColliderTris[t + 2]];
                    bool flat = Mathf.Approximately(a.y, C + 1) && Mathf.Approximately(b.y, C + 1) && Mathf.Approximately(c.y, C + 1);
                    var mid = (a + b + c) / 3f;
                    if (flat && mid.x > C && mid.x < C + 1 && mid.z > C && mid.z < C + 1)
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                data.Release();
            }
        }

        [TestCase("glass_clear", "water")]
        [TestCase("glass", "water")]
        [TestCase("glass_clear", "torch")]
        [TestCase("glass_clear", null)]
        public void SeeThroughFloor_KeepsItsCollisionTop(string floor, string above)
        {
            var content = LoadContentOrIgnore();
            Assert.That(HasColliderTop(content, floor, above), Is.True,
                $"'{floor}' under {above ?? "air"} must carry a collision face on top — or the player falls through it.");
        }

        [Test]
        public void GlassOnGlass_HasNoInnerCollisionFace()
        {
            var content = LoadContentOrIgnore();
            Assert.That(HasColliderTop(content, "glass_clear", "glass_clear"), Is.False,
                "two stacked panes need no collision face between them.");
        }
    }
}

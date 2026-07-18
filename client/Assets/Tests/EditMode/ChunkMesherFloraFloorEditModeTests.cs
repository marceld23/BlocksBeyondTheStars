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
    /// Regression test for the "transparent plant floor" bug: a flora/foliage neighbour must NOT cull the
    /// adjacent opaque block's face. Flora renders only as a thin billboard (cross-plants), a cutout shell
    /// (tree crowns) or a rounded shaped block (solid flora) — none of them fill the cell, so they can't seal
    /// the ground/wall behind them. Batch 27afca87 dropped the flora term from the mesher's face-cull, which
    /// culled the ground top UNDER every plant → a see-through hole showing the sky ("weißer/transparenter
    /// Boden"; solid-flora domes left white corner slivers). See ChunkMesher.BuildGeometry `drawFace`.
    ///
    /// Runs headless with a null atlas (no textures, EditMode-safe): the flora branches that need an atlas are
    /// skipped, but the face-cull — which keys off the NEIGHBOUR's block key via IsFloraBlock/IsFoliageBlock —
    /// is exactly what we assert here.
    /// </summary>
    public sealed class ChunkMesherFloraFloorEditModeTests
    {
        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        private static ushort IdOf(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId.Value;
        }

        /// <summary>Meshes a one-chunk world holding a single opaque <paramref name="groundId"/> cell with the
        /// given <paramref name="aboveId"/> block directly on top, and returns whether the ground cell's TOP
        /// face was emitted (a horizontal opaque triangle at the ground's top plane). A flora neighbour must
        /// leave it visible; a genuine opaque neighbour must still cull it.</summary>
        private static bool GroundTopEmitted(GameContent content, ushort groundId, ushort aboveId)
        {
            const int gx = 8, gy = 8, gz = 8; // chunk interior, so all neighbours are in-chunk
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(gx, gy, gz, new BlockId(groundId));
            chunk.Set(gx, gy + 1, gz, new BlockId(aboveId));

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World); // atlas = null → no textures (EditMode-safe)
            try
            {
                // The ground cube spans world-y ∈ [gy, gy+1]; its top face is the horizontal quad at y == gy+1.
                // Only that face can have all three of a triangle's verts on that plane (a vertical face would
                // straddle gy..gy+1). The flora cube's own bottom face there is culled against the ground, so a
                // hit uniquely means the ground kept its top.
                const float topY = gy + 1;
                var verts = data.Verts;
                var tris = data.OpaqueTris;
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                    if (Mathf.Abs(a.y - topY) < 1e-3f && Mathf.Abs(b.y - topY) < 1e-3f && Mathf.Abs(c.y - topY) < 1e-3f)
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

        [Test]
        public void GroundTop_UnderCrossPlantFlora_IsEmitted()
        {
            var content = LoadContentOrIgnore();
            Assert.IsTrue(
                GroundTopEmitted(content, IdOf(content, "dirt"), IdOf(content, "flora_fern")),
                "Ground top under a cross-plant flora cell must render — otherwise it's a see-through hole.");
        }

        [Test]
        public void GroundTop_UnderSolidFlora_IsEmitted()
        {
            var content = LoadContentOrIgnore();
            Assert.IsTrue(
                GroundTopEmitted(content, IdOf(content, "dirt"), IdOf(content, "flora_puffball")),
                "Ground top under a solid-flora cell must render — otherwise white corner slivers show through.");
        }

        [Test]
        public void GroundTop_UnderOpaqueBlock_IsCulled()
        {
            // Guard the other direction: a genuine opaque neighbour MUST still seal the shared face, so the fix
            // opened the cull only for flora/foliage, not for everything.
            var content = LoadContentOrIgnore();
            Assert.IsFalse(
                GroundTopEmitted(content, IdOf(content, "dirt"), IdOf(content, "stone")),
                "An opaque neighbour must still cull the shared face.");
        }
    }
}

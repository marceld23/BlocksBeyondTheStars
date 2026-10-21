// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #2881 — a torch-lit concrete plaza showed its light as a grid of block-sized tiles: every opaque face carried ONE
    /// coloured block-light value for all four corners, so two neighbouring floor faces disagreed along the edge they
    /// share. A torch-lit stone floor is meshed here and every vertex position of its top surface must carry a single
    /// block-light value, whichever face the vertex belongs to — and the light must still fall off across one face.
    /// </summary>
    public sealed class ChunkMesherCornerBlockLightEditModeTests
    {
        private const int FloorY = 4;

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        [Test]
        public void ATorchLitFloor_LightsAsOneSurface_NotBlockByBlock()
        {
            var content = LoadContentOrIgnore();
            var stone = content.GetBlock("stone");
            Assert.IsNotNull(stone, "Block 'stone' missing from content.");

            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            for (int x = 1; x < WorldConstants.ChunkSize - 1; x++)
            {
                for (int z = 1; z < WorldConstants.ChunkSize - 1; z++)
                {
                    chunk.Set(x, FloorY, z, stone.NumericId);
                }
            }

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var lights = new List<(Vector3i Pos, int Rgb)> { (new Vector3i(7, FloorY + 3, 6), 0xFFB060) }; // a warm torch

            LogAssert.ignoreFailingMessages = true; // BlockTextureAtlas' per-tile Object.Destroy logs an error outside play mode
            var atlas = new BlockTextureAtlas(content);
            var data = ChunkMesher.BuildGeometry(chunk, content, World, atlas, null, null, lights);
            try
            {
                // Every vertex of the floor's top surface away from its rim (the rim is bevelled; inside it every corner
                // sits on the block grid), grouped by position.
                var byCorner = new Dictionary<(int X, int Z), List<Vector3>>();
                var counted = new HashSet<int>();
                float brightest = 0f, widestOnOneFace = 0f;
                var tris = data.OpaqueTris;
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    if (!InnerTop(data, tris[t]) || !InnerTop(data, tris[t + 1]) || !InnerTop(data, tris[t + 2]))
                    {
                        continue;
                    }

                    float faceMin = float.MaxValue, faceMax = float.MinValue;
                    for (int k = 0; k < 3; k++)
                    {
                        int i = tris[t + k];
                        var light = data.BlockLight[i];
                        float level = Mathf.Max(light.x, Mathf.Max(light.y, light.z));
                        faceMin = Mathf.Min(faceMin, level);
                        faceMax = Mathf.Max(faceMax, level);
                        brightest = Mathf.Max(brightest, level);
                        if (!counted.Add(i))
                        {
                            continue; // the quad's two triangles share this vertex
                        }

                        var v = data.Verts[i];
                        var key = (Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.z));
                        if (!byCorner.TryGetValue(key, out var list))
                        {
                            byCorner[key] = list = new List<Vector3>();
                        }

                        list.Add(light);
                    }

                    widestOnOneFace = Mathf.Max(widestOnOneFace, faceMax - faceMin);
                }

                Assert.That(byCorner.Count, Is.GreaterThan(50), "the floor's top surface must be meshed");
                Assert.That(brightest, Is.GreaterThan(0.1f), "the torch must light the floor");
                Assert.That(widestOnOneFace, Is.GreaterThan(0.01f), "the light must fall off across a face, not step between faces");

                int shared = 0;
                foreach (var pair in byCorner)
                {
                    if (pair.Value.Count < 2)
                    {
                        continue;
                    }

                    shared++;
                    foreach (var light in pair.Value)
                    {
                        Assert.That((light - pair.Value[0]).magnitude, Is.LessThan(1e-4f),
                            $"corner {pair.Key}: neighbouring faces disagree on the block light ({pair.Value[0]} vs {light})");
                    }
                }

                Assert.That(shared, Is.GreaterThan(50), "inner corners are shared by up to four faces");
            }
            finally
            {
                data.Release();
                Object.DestroyImmediate(atlas.NormalTexture); // EditMode: Object.Destroy logs an error outside play mode
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>A vertex of the floor's upward top surface, inside its rim and on the block grid.</summary>
        private static bool InnerTop(ChunkMeshData data, int i)
        {
            var v = data.Verts[i];
            return Mathf.Abs(v.y - (FloorY + 1)) < 1e-4f && data.Normals[i].y > 0.99f
                   && v.x >= 2f && v.x <= WorldConstants.ChunkSize - 2f && v.z >= 2f && v.z <= WorldConstants.ChunkSize - 2f
                   && Mathf.Abs(v.x - Mathf.Round(v.x)) < 1e-4f && Mathf.Abs(v.z - Mathf.Round(v.z)) < 1e-4f;
        }
    }
}

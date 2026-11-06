// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #3289: a rune stone or an ancient brick wears the tile of the biome palette its column has (the chunk build's
    /// <c>biomePalette</c> lookup), on every face; without a lookup (ships, the speeder, the held miniatures), in a column
    /// without a palette, and for a block without looks the own tile stays. A monument rune's glow-only cell keeps its
    /// colours (tint mode 0) and still shines; a real dye still recolours it, and the glow still recolours other blocks.
    /// Meshed with the real atlas, one block per chunk.
    /// </summary>
    public sealed class ChunkMesherBiomeLooksEditModeTests
    {
        private const int C = 8; // chunk interior

        private sealed class Meshed
        {
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector2> Sky = new List<Vector2>(); // TEXCOORD1: y = tint mode (+ animation code)
            public readonly List<Color> Col = new List<Color>();     // a = emission
        }

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

        private static Meshed MeshSingle(GameContent content, BlockTextureAtlas atlas, string key,
            System.Func<int, int, int> biomePalette, int tint = 0, int glow = 0)
        {
            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            chunk.Set(C, C, C, Id(content, key));
            if (tint != 0 || glow != 0)
            {
                chunk.SetModifier(C, C, C, tint, glow);
            }

            BlockId World(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 &&
                x < WorldConstants.ChunkSize && y < WorldConstants.ChunkSize && z < WorldConstants.ChunkSize
                    ? chunk.Get(x, y, z)
                    : BlockId.Air;

            var data = ChunkMesher.BuildGeometry(chunk, content, World, atlas, biomePalette: biomePalette);
            try
            {
                var m = new Meshed();
                var seen = new HashSet<int>();
                foreach (int i in data.OpaqueTris)
                {
                    if (seen.Add(i))
                    {
                        m.Uv.Add(data.Uvs[i]);
                        m.Sky.Add(data.SkyUv[i]);
                        m.Col.Add(data.Colors[i]);
                    }
                }

                Assert.That(m.Uv.Count, Is.GreaterThan(0), $"'{key}' must render.");
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

        private static void AssertInside(Meshed m, Rect tile, string what)
        {
            const float eps = 1e-4f;
            foreach (var uv in m.Uv)
            {
                Assert.That(uv.x >= tile.xMin - eps && uv.x <= tile.xMax + eps && uv.y >= tile.yMin - eps && uv.y <= tile.yMax + eps,
                    Is.True, $"{what}: uv {uv} lies outside the expected tile {tile}");
            }
        }

        [TestCase("rune_stone")]
        [TestCase("ancient_brick")]
        public void ALookBlock_WearsTheTileOfItsColumnsPalette(string key)
        {
            WithAtlas((content, atlas) =>
            {
                ushort id = Id(content, key).Value;
                Assert.That(atlas.WearsBiomeLooks(id), Is.True, $"'{key}' wears biome looks");
                var slots = new HashSet<ushort>();
                for (int p = 0; p < BiomePalettes.Count; p++)
                {
                    ushort slot = atlas.BiomeLookSlot(id, p);
                    Assert.That(slot, Is.Not.EqualTo((ushort)0), $"{key}: palette {BiomePalettes.Names[p]} has no tile in the atlas");
                    Assert.That(slot, Is.Not.EqualTo(id));
                    Assert.That(slots.Add(slot), Is.True, $"{key}: palette {BiomePalettes.Names[p]} shares a slot");

                    int palette = p;
                    var asked = new List<(int, int)>();
                    var m = MeshSingle(content, atlas, key, (x, z) =>
                    {
                        asked.Add((x, z));
                        return palette;
                    });
                    AssertInside(m, atlas.TileUv(slot), $"{key} in a {BiomePalettes.Names[p]} column");
                    Assert.That(asked, Does.Contain((C, C)), "the lookup is asked for the block's own column");
                }
            });
        }

        [Test]
        public void WithoutALookup_OrAPalette_TheOwnTileStays()
        {
            WithAtlas((content, atlas) =>
            {
                ushort rune = Id(content, "rune_stone").Value;
                AssertInside(MeshSingle(content, atlas, "rune_stone", null), atlas.TileUv(rune), "no lookup (a ship)");
                AssertInside(MeshSingle(content, atlas, "rune_stone", (x, z) => -1), atlas.TileUv(rune), "a column without a palette");

                ushort stone = Id(content, "stone").Value;
                Assert.That(atlas.WearsBiomeLooks(stone), Is.False);
                var plain = MeshSingle(content, atlas, "stone", (x, z) => 1);
                foreach (var uv in plain.Uv)
                {
                    var own = atlas.TileUv(stone);
                    bool inOwn = uv.x >= own.xMin - 1e-4f && uv.x <= own.xMax + 1e-4f && uv.y >= own.yMin - 1e-4f && uv.y <= own.yMax + 1e-4f;
                    bool inLook = false;
                    for (int p = 0; p < BiomePalettes.Count; p++)
                    {
                        var look = atlas.TileUv(atlas.BiomeLookSlot(rune, p));
                        inLook |= uv.x >= look.xMin && uv.x <= look.xMax && uv.y >= look.yMin && uv.y <= look.yMax;
                    }

                    Assert.That(inOwn || !inLook, Is.True, "stone wears no biome look");
                }
            });
        }

        [Test]
        public void AGlowOnlyRune_KeepsItsColoursAndItsLight_ButADyeStillWins()
        {
            WithAtlas((content, atlas) =>
            {
                const int glow = 0x3FD8E8;
                var glowing = MeshSingle(content, atlas, "rune_stone", (x, z) => 2, glow: glow);
                foreach (var s in glowing.Sky)
                {
                    Assert.That(s.y % 16f, Is.EqualTo(0f).Within(1e-3f), "a glow-only rune is not recoloured (tint mode 0)");
                }

                foreach (var c in glowing.Col)
                {
                    Assert.That(c.a, Is.GreaterThanOrEqualTo(ChunkMesher.GlowCellEmission - 1e-3f), "the glowing rune still shines");
                }

                var dyed = MeshSingle(content, atlas, "rune_stone", (x, z) => 2, tint: 0xFF2020, glow: glow);
                foreach (var s in dyed.Sky)
                {
                    Assert.That(s.y % 16f, Is.EqualTo(3f).Within(1e-3f), "a dyed rune takes the dye (tint mode 3)");
                }

                // Any other block keeps the old rule: its glow recolours its surface.
                var stone = MeshSingle(content, atlas, "stone", (x, z) => 2, glow: glow);
                foreach (var s in stone.Sky)
                {
                    Assert.That(s.y % 16f, Is.EqualTo(3f).Within(1e-3f), "a glowing stone takes its glow's colour");
                }
            });
        }
    }
}

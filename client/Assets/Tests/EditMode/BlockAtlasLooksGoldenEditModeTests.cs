// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Golden checksums of the code-painted block tiles (#2906). <see cref="BlockTextureAtlas"/> paints a block without
    /// a tile in code: its base colour with a per-pixel grain and a darker edge, then the block's own look (lamp lens,
    /// hazard stripes, grass blades, crystal shards …). Most looks are dormant fallbacks behind a bundled tile, so the
    /// test reads them through <see cref="BlockTextureAtlas.ProceduralTileForTest"/>, which forces the code path for
    /// every block, and hashes the 64×64 RGBA32 bytes (FNV-1a 64). The pins were taken from the switch statement that
    /// painted the looks before it became a look table, so the table has to reproduce every pixel.
    /// </summary>
    public sealed class BlockAtlasLooksGoldenEditModeTests
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>The blocks that carry a code-painted look, in the order the old switch listed them.</summary>
        private static readonly string[] LookKeys =
        {
            "obsidian", "ancient_brick", "rune_stone", "iron_wall", "strip_light_cyan", "strip_light_warm",
            "medbay_panel", "lab_panel", "cargo_floor", "engine_nozzle", "engine_panel",
            "iron_ore", "copper_ore", "titanium_ore", "silicate", "data_cache", "ice", "glass", "force_field",
            "water", "lava", "grass",
            "flora_plant", "flora_crystal", "flora_fern", "flora_flower", "flora_bush", "flora_vine", "flora_mushroom",
            "flora_cactus", "flora_dryshrub", "flora_reed", "flora_glowcap",
            "flora_cavecap", "flora_glowmoss", "flora_glowthread", "flora_prismbloom", "flora_frostflower",
            "flora_emberbloom", "flora_cropberry", "flora_cropgrain", "flora_cropshroom", "hydro_tray",
            "light_white", "light_red", "light_green",
        };

        private const string AllBlocks = "all blocks";

        // Pinned 2026-10-22 (Windows, Unity 6000.6) from the switch-statement painter, before the look table (#2906).
        // 0 = not pinned yet: the test then fails with the value to paste here.
        // Re-pin rule: a per-key pin moves only when that block's painting changes on purpose — its look, its palette
        // colour (BlockTextureAtlas.BaseColor, else its authored blocks.json colour) or the shared grain/edge. The
        // "all blocks" pin covers every block in the content, so it ALSO moves when a block is added, removed or renamed,
        // or any block's colour changes; when the per-key pins still pass, that is the cause and re-pinning it is the fix.
        // Re-pinned 2026-10-30 (#3005): "all blocks" only — the pumpkin and the pumpkin lantern joined the content; every
        // per-key pin still passed.
        // Re-pinned 2026-10-30 (#3024): "all blocks" only — the four festival decorations joined the content; every per-key
        // pin still passed.
        private static readonly Dictionary<string, ulong> Pinned = new Dictionary<string, ulong>
        {
            ["obsidian"] = 0x28231fc804c6b95aUL,
            ["ancient_brick"] = 0x9d955ed51677fa00UL,
            ["rune_stone"] = 0x45ec1da987b7f58aUL,
            ["iron_wall"] = 0x2423b950a9a9b865UL,
            ["strip_light_cyan"] = 0xc453493f95b11e79UL,
            ["strip_light_warm"] = 0x21ee07a339875f59UL,
            ["medbay_panel"] = 0xad91453398970fe6UL,
            ["lab_panel"] = 0x2b432a546703ed51UL,
            ["cargo_floor"] = 0x068e535e5694343dUL,
            ["engine_nozzle"] = 0x52c83a6ac5ad6033UL,
            ["engine_panel"] = 0xaca30ebd34973e11UL,
            ["iron_ore"] = 0xf30558e532f6fdd5UL,
            ["copper_ore"] = 0xcffb6ca4273c7553UL,
            ["titanium_ore"] = 0x2ba99984238da021UL,
            ["silicate"] = 0x6beafe9d4be59f11UL,
            ["data_cache"] = 0x0a013eb022c09734UL,
            ["ice"] = 0x7617399536188709UL,
            ["glass"] = 0x23a0e8d6d09f4664UL,
            ["force_field"] = 0x440ef2896e905755UL,
            ["water"] = 0xbd4cbb9c4a2d30a4UL,
            ["lava"] = 0x4628e24133b012b9UL,
            ["grass"] = 0xc4d05a3724019601UL,
            ["flora_plant"] = 0xbef13706e46f90efUL,
            ["flora_crystal"] = 0xc1815054a5d6470cUL,
            ["flora_fern"] = 0x951421109ba82df4UL,
            ["flora_flower"] = 0x505527f3b9a0505fUL,
            ["flora_bush"] = 0x43012424ca363079UL,
            ["flora_vine"] = 0x4614505ac82a6217UL,
            ["flora_mushroom"] = 0x97d9becfe9f43fe7UL,
            ["flora_cactus"] = 0x5dedd64839fb05e9UL,
            ["flora_dryshrub"] = 0x77b6a1cbbddd6fa8UL,
            ["flora_reed"] = 0x344fc6f70f15d6a6UL,
            ["flora_glowcap"] = 0x2472f0b28335feccUL,
            ["flora_cavecap"] = 0xf01a831304937914UL,
            ["flora_glowmoss"] = 0x42f33743334e8671UL,
            ["flora_glowthread"] = 0x1f8c861cb1ece439UL,
            ["flora_prismbloom"] = 0x1bf0e69c282a21e5UL,
            ["flora_frostflower"] = 0x490d0bffe9430969UL,
            ["flora_emberbloom"] = 0x71c8137d128c05cbUL,
            ["flora_cropberry"] = 0x1c998f640d0514caUL,
            ["flora_cropgrain"] = 0x53932443ac5e1955UL,
            ["flora_cropshroom"] = 0x67bc1fab31ec6133UL,
            ["hydro_tray"] = 0x6eee16866320a1f9UL,
            ["light_white"] = 0x8ad9ac33d897e9d5UL,
            ["light_red"] = 0x532e9caef6687ac5UL,
            ["light_green"] = 0x6725b9a585b93bd5UL,
            [AllBlocks] = 0x7be9ca71505df451UL,
        };

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        private static ulong Fnv(ulong h, byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                h ^= b;
                h *= FnvPrime;
            }

            return h;
        }

        [Test]
        public void CodePaintedTiles_MatchThePinnedChecksums()
        {
            var content = LoadContentOrIgnore();
            LogAssert.ignoreFailingMessages = true; // BlockTextureAtlas' per-tile Object.Destroy logs an error outside play mode
            var atlas = new BlockTextureAtlas(content);
            var failures = new List<string>();
            var report = new List<string>();

            void Check(string name, ulong actual)
            {
                report.Add($"[\"{name}\"] = 0x{actual:x16}UL,");
                ulong pinned = Pinned[name];
                if (pinned != actual)
                {
                    failures.Add($"{name}: pinned 0x{pinned:x16}, actual 0x{actual:x16}");
                }
            }

            try
            {
                foreach (string key in LookKeys)
                {
                    var def = content.GetBlock(key);
                    Assert.IsNotNull(def, $"Block '{key}' missing from content.");
                    Check(key, Fnv(FnvOffset, atlas.ProceduralTileForTest(def)));
                }

                var keys = new List<string>(content.Blocks.Keys);
                keys.Sort(string.CompareOrdinal);
                ulong all = FnvOffset;
                foreach (string key in keys)
                {
                    all = Fnv(all, atlas.ProceduralTileForTest(content.Blocks[key]));
                }

                Check(AllBlocks, all);
                report.Add($"({keys.Count} blocks in \"{AllBlocks}\")");
            }
            finally
            {
                Object.DestroyImmediate(atlas.NormalTexture); // EditMode: Object.Destroy logs an error outside play mode
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }

            TestContext.WriteLine(string.Join("\n", report));
            Assert.IsEmpty(failures, "Code-painted tiles changed:\n" + string.Join("\n", failures) + "\n\n" + string.Join("\n", report));
        }
    }
}

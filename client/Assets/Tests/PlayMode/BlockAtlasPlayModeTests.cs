// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// PlayMode test for the Unity-coupled <see cref="BlockTextureAtlas"/>: it builds a real
    /// <see cref="Texture2D"/> atlas from the synced game content. Runs in PlayMode (not EditMode)
    /// because the atlas builder calls <c>Object.Destroy</c>, which is illegal in edit mode.
    /// Requires the content under StreamingAssets/data (run scripts/sync-client-libs.ps1 first);
    /// skipped gracefully otherwise.
    /// </summary>
    public sealed class BlockAtlasPlayModeTests
    {
        [Test]
        public void BuildsASquareAtlasTextureFromContent()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
                return;
            }

            var content = ContentLoader.LoadFromDirectory(dataDir);
            var atlas = new BlockTextureAtlas(content);

            Assert.IsNotNull(atlas.Texture, "Atlas texture should be created.");
            Assert.AreEqual(BlockTextureAtlas.Cols * BlockTextureAtlas.Tile, atlas.Texture.width, "Atlas width = Cols * Tile.");
            Assert.AreEqual(BlockTextureAtlas.Rows * BlockTextureAtlas.Tile, atlas.Texture.height, "Atlas height = Rows * Tile.");
        }
    }
}

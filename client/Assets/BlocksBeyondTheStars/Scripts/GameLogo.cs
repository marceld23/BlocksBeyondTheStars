// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Showcase;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The game's logo as a texture (#3706): <see cref="BlockLogo"/> — "BLOCKS BEYOND / THE STARS" built from real blocks —
    /// rendered once from the SHIPPED block tiles (<see cref="GameTextures.Official"/>: a texture pack never changes the
    /// brand) and kept for the session. The title splash, the main menu and the loading screen show it
    /// (<see cref="UiKit.AddGameLogo"/>); the poster capture stamps the same raster on its pictures. About 1.9 MB with mip maps,
    /// made in a few milliseconds (no texture asset, nothing in the browser download); null when the tiles cannot be read —
    /// the callers then fall back to the old text title.
    /// </summary>
    public static class GameLogo
    {
        private static Texture2D _texture;
        private static bool _failed;

        /// <summary>The logo texture (rows bottom-up, straight alpha), or null when it could not be made.</summary>
        public static Texture2D Texture
        {
            get
            {
                if (_texture == null && !_failed)
                {
                    _texture = Build();
                    _failed = _texture == null;
                }

                return _texture;
            }
        }

        /// <summary>The logo raster at <paramref name="blockPixels"/> per block, from the shipped tiles (rows bottom-up — the
        /// game's frame order). The poster capture composes its pictures from it.</summary>
        public static RgbaImage Raster(int blockPixels = BlockLogo.GameBlockPixels)
            => BlockLogo.Render(blockPixels, ShippedTile);

        /// <summary>The share of the logo image's width that is transparent margin on each side (the halo's room), so a caller
        /// can line the letters up with a column edge.</summary>
        public static float MarginFraction
        {
            get
            {
                var (w, _) = BlockLogo.ImageSize(BlockLogo.GameBlockPixels);
                return BlockLogo.GameBlockPixels / (float)w;
            }
        }

        private static byte[] ShippedTile(string key)
        {
            var frames = GameTextures.Official(key);
            return frames != null && frames.Frames.Length > 0 ? frames.Frames[0] : null;
        }

        private static Texture2D Build()
        {
            try
            {
                var img = Raster();
                var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, true)
                {
                    name = "GameLogo",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear, // mostly shown smaller than its 16 px blocks: the mips keep it calm
                    hideFlags = HideFlags.DontUnloadUnusedAsset, // one per session, never swept by the return-to-menu pass
                };
                tex.SetPixelData(img.Rgba, 0); // mip 0; Apply builds the rest
                tex.Apply(true, true);         // upload and drop the CPU copy — the capture re-renders the raster itself
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GameLogo] The block logo could not be made ({e.Message}) — the text title stands in.");
                return null;
            }
        }
    }
}

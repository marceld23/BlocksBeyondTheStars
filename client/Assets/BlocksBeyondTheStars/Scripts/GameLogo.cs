// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Showcase;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The game's official logo (#3706): the rocket icon and the "Blocks Beyond the Stars" wordmark, white on a transparent
    /// ground for dark backgrounds — the same mark as the README and the print material (Marcel: "my lettering", not a
    /// generated one). Shipped as <c>Resources/branding/bbts-logo.bytes</c> (a 1600 px PNG kept as raw bytes, like the block
    /// tiles in <c>Resources/textures</c>, so no import step can recompress it) and decoded once per session. The title
    /// splash, the main menu and the loading screen show it (<see cref="UiKit.AddGameLogo"/>); the poster capture stamps the
    /// same image on its pictures. Null when the asset is missing — the callers then fall back to the text title.
    /// </summary>
    public static class GameLogo
    {
        /// <summary>The logo's resource path (a TextAsset holding the PNG bytes).</summary>
        public const string ResourcePath = "branding/bbts-logo";

        private static Texture2D _texture;
        private static bool _failed;

        /// <summary>The logo texture (rows bottom-up, straight alpha, readable), or null when it could not be loaded.</summary>
        public static Texture2D Texture
        {
            get
            {
                if (_texture == null && !_failed)
                {
                    _texture = Load();
                    _failed = _texture == null;
                }

                return _texture;
            }
        }

        /// <summary>The share of the image's width that is transparent margin on each side — none: the icon starts at the
        /// image's left edge, so the mark lines up with a column edge as it is.</summary>
        public static float MarginFraction => 0f;

        /// <summary>The logo as an image <paramref name="width"/> pixels wide (rows bottom-up — the game's frame order), for
        /// the poster capture; null without the logo.</summary>
        public static RgbaImage Raster(int width)
        {
            var tex = Texture;
            if (tex == null)
            {
                return null;
            }

            var px = tex.GetPixels32();
            var rgba = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                rgba[i * 4] = px[i].r;
                rgba[i * 4 + 1] = px[i].g;
                rgba[i * 4 + 2] = px[i].b;
                rgba[i * 4 + 3] = px[i].a;
            }

            var image = new RgbaImage(tex.width, tex.height, rgba);
            int height = Mathf.Max(1, Mathf.RoundToInt(width * tex.height / (float)tex.width));
            return width == tex.width ? image : image.Resize(width, height);
        }

        private static Texture2D Load()
        {
            var bytes = Resources.Load<TextAsset>(ResourcePath);
            if (bytes == null)
            {
                Debug.LogWarning($"[GameLogo] Resources/{ResourcePath} is missing — the text title stands in.");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = "GameLogo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, // mostly shown smaller than its 1600 px: the mips keep the edges calm
                hideFlags = HideFlags.DontUnloadUnusedAsset, // one per session, never swept by the return-to-menu pass
            };
            bool ok = tex.LoadImage(bytes.bytes, false); // stays readable: the poster capture reads its pixels
            Resources.UnloadAsset(bytes);
            if (!ok)
            {
                EditorSceneKit.Discard(tex);
                Debug.LogWarning("[GameLogo] The logo image could not be decoded — the text title stands in.");
                return null;
            }

            return tex;
        }
    }
}

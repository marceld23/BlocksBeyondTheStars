// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one tileable noise texture the mist cards (#3273) and the sea of clouds (#3272) scroll: R = a coarse 4-octave
    /// fractal, G = a finer, independent 3-octave one (the shaders sample it at an integer multiple of the R tiling, so a
    /// wrapped offset never shows a seam). 128², mip-mapped (the deck reaches the horizon), repeat-wrapped. Built once from
    /// <see cref="MistNoise"/> (Client.Core) and shared for the session.
    /// </summary>
    internal static class MistNoiseTexture
    {
        private const int Size = 128;
        private static Texture2D _texture;

        /// <summary>The shared texture (built on first use).</summary>
        public static Texture2D Get()
        {
            if (_texture != null)
            {
                return _texture;
            }

            float[] coarse = MistNoise.Tileable(Size, 4, 4, 3273);
            float[] fine = MistNoise.Tileable(Size, 8, 3, 3272);
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(ToByte(coarse[i]), ToByte(fine[i]), 0, 255);
            }

            _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                name = "MistNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
            };
            _texture.SetPixels32(px);
            _texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return _texture;
        }

        private static byte ToByte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
    }
}

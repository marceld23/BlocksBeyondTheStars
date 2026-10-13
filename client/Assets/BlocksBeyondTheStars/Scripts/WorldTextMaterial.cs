// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one shared material for world-space <see cref="TextMesh"/> labels (#2531): a sleeping animal's or NPC's
    /// "z z z" and a companion's nameplate. The font's own material is the built-in GUI/Text shader, which ignores the
    /// depth buffer — those labels shone through terrain and told the player where a cave ends and land begins. This
    /// material uses <c>BlocksBeyondTheStars/WorldText</c> (same look, depth-tested), so hills and walls hide the text.
    /// The font is dynamic: its atlas is rebuilt (and may be replaced) as new glyphs arrive, so the material follows
    /// <see cref="Font.textureRebuilt"/>. Labels meant to show through walls (beacons, thermal contacts, ore markers)
    /// do not use TextMesh and are unaffected. If the shader is missing (a stripped build), the font's own material is
    /// returned — the old through-wall look, but never invisible text.
    /// </summary>
    public static class WorldTextMaterial
    {
        private static Material _material;
        private static Font _font;
        private static bool _hooked;

        /// <summary>The shared depth-tested text material for <see cref="UiKit.Font"/> (null only without a font).</summary>
        public static Material Shared
        {
            get
            {
                var font = UiKit.Font;
                if (font == null)
                {
                    return null;
                }

                if (_material != null && ReferenceEquals(_font, font))
                {
                    return _material;
                }

                var shader = Shader.Find("BlocksBeyondTheStars/WorldText");
                if (shader == null || !shader.isSupported)
                {
                    return font.material; // fallback: the old GUI/Text look (visible through walls, but visible)
                }

                if (_material == null)
                {
                    _material = new Material(shader) { name = "World Text", hideFlags = HideFlags.HideAndDontSave };
                }

                _font = font;
                _material.mainTexture = font.material != null ? font.material.mainTexture : null;
                if (!_hooked)
                {
                    Font.textureRebuilt += OnTextureRebuilt;
                    _hooked = true;
                }

                return _material;
            }
        }

        /// <summary>A dynamic font re-packs its atlas when new characters are requested; keep the material's texture
        /// pointing at the current one or the labels sample a stale (or destroyed) atlas.</summary>
        private static void OnTextureRebuilt(Font font)
        {
            if (_material != null && ReferenceEquals(font, _font) && font.material != null)
            {
                _material.mainTexture = font.material.mainTexture;
            }
        }
    }
}

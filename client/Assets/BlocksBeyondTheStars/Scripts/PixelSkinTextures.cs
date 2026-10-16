// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#2682: hands a <see cref="PlayerAvatar"/> ready-baked textures for its face and body paintings. A texture it
    /// returns stays the cache's (the figure never destroys it); null = the figure bakes its own.</summary>
    public interface IAvatarTextureCache
    {
        Texture2D Face(string face, Color skin);

        Texture2D PaintAtlas(int part, string pixels, Color baseColor);
    }

    /// <summary>
    /// #2682: the pioneers' pixel skins on the client — the pool (<c>StreamingAssets/data/pioneer_skins.json</c>, loaded once,
    /// the seeded pick and recolour in Client.Core's <see cref="PixelSkinPool"/>) and the baked textures, kept per skin so a
    /// pioneer walking out of reach and back (an NPC is rebuilt every time) is not baked again on a weak laptop or in the
    /// browser. A world holds one or two pioneers; <see cref="Clear"/> (on a world change) frees the textures.
    /// </summary>
    public sealed class PixelSkinTextures : IAvatarTextureCache
    {
        /// <summary>The textures kept at most before the oldest set is dropped (a pioneer's skin is four textures).</summary>
        private const int Cap = 32;

        public static readonly PixelSkinTextures Shared = new PixelSkinTextures();

        private static PixelSkinPool _pool;

        private readonly Dictionary<(int Part, string Pixels, Color32 Base), Texture2D> _textures =
            new Dictionary<(int, string, Color32), Texture2D>();

        private readonly List<(int, string, Color32)> _order = new List<(int, string, Color32)>();

        /// <summary>The skin pool, read from the bundled data on first need (an unreadable file = an empty pool: the
        /// pioneers then wear the plain NPC look).</summary>
        public static PixelSkinPool Pool
        {
            get
            {
                if (_pool != null)
                {
                    return _pool;
                }

                try
                {
                    string path = Path.Combine(StreamingAssetsCache.DataDir, "pioneer_skins.json");
                    _pool = File.Exists(path) ? PixelSkinPool.Parse(File.ReadAllText(path)) : new PixelSkinPool(Array.Empty<PixelSkin>());
                    if (_pool.Skins.Count == 0)
                    {
                        Debug.LogWarning($"[PixelSkins] no pioneer skins at {path} — pioneers wear the plain look.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PixelSkins] could not read the pioneer skins: {e.Message}");
                    _pool = new PixelSkinPool(Array.Empty<PixelSkin>());
                }

                return _pool;
            }
        }

        public Texture2D Face(string face, Color skin)
            => Get(-1, face, skin, () => FacePalette.BuildAvatarTexture(face, skin));

        public Texture2D PaintAtlas(int part, string pixels, Color baseColor)
            => Get(part, pixels, baseColor, () => BodyPaintKit.BuildAtlas(part, pixels, baseColor));

        private Texture2D Get(int part, string pixels, Color tint, Func<Texture2D> bake)
        {
            if (string.IsNullOrEmpty(pixels))
            {
                return null;
            }

            var key = (part, pixels, (Color32)tint);
            if (_textures.TryGetValue(key, out var tex) && tex != null)
            {
                return tex;
            }

            while (_order.Count >= Cap)
            {
                var oldest = _order[0];
                _order.RemoveAt(0);
                if (_textures.TryGetValue(oldest, out var old))
                {
                    _textures.Remove(oldest);
                    if (old != null)
                    {
                        UnityEngine.Object.Destroy(old);
                    }
                }
            }

            tex = bake();
            if (tex != null)
            {
                _textures[key] = tex;
                _order.Add(key);
            }

            return tex;
        }

        /// <summary>Frees every texture (a world change: every NPC figure of the old world is gone with it).</summary>
        public void Clear()
        {
            foreach (var tex in _textures.Values)
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }

            _textures.Clear();
            _order.Clear();
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2744: the inventory icon of a block whose faces differ — a machine with its picture front and casing, a crate
    /// with its lid, a log with its end grain. A flat tile shows only one of those faces (a casing reads as nothing),
    /// so such a block is drawn as a small three-quarter cube: its top, its front and its right side, lit from the top
    /// left, with the faces the world gives it (<see cref="BlockCubeFaces"/>). Every other block keeps its flat tile —
    /// the familiar look, and one face says everything there.
    /// <para>
    /// The icons are painted on the CPU from the readable colour atlas (2×2 samples per pixel, so the slanted edges come
    /// out smooth), once per block, and kept for as long as their atlas: a repaint of the atlas (a texture pack, a
    /// world's own textures, #1952) repaints them in place, so every image showing one stays valid; a new atlas (a new
    /// content snapshot) drops them. They are few and small (64 × 64 for the few dozen blocks with distinct faces).
    /// </para>
    /// </summary>
    public static class BlockCubeIcon
    {
        /// <summary>Icon edge length in pixels.</summary>
        public const int Size = 64;

        private const int Samples = 2;      // per axis, per pixel
        private const float Margin = 2f;    // pixels left free around the cube
        private const float TopShade = 1f;
        private const float FrontShade = 0.84f;
        private const float SideShade = 0.66f;

        private static readonly Dictionary<ushort, Texture2D> Cache = new Dictionary<ushort, Texture2D>();
        private static BlockTextureAtlas _atlas;

        /// <summary>The cube icon of block <paramref name="id"/>, or null for a block that shows one tile on every face
        /// (use its flat tile), without a readable atlas, or for air. Cached per block.</summary>
        public static Texture2D For(BlockTextureAtlas atlas, ushort id)
        {
            if (atlas == null || atlas.Texture == null || atlas.Content == null || id == BlockId.AirValue)
            {
                return null;
            }

            if (!ReferenceEquals(atlas, _atlas))
            {
                Adopt(atlas);
            }

            if (Cache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            Texture2D tex = null;
            if (BlockCubeFaces.HasDistinctFaces(atlas.Content, atlas, new BlockId(id)))
            {
                tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: true)
                {
                    name = "BlockCubeIcon",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                if (!Paint(tex, atlas, id))
                {
                    BlockCubeFaces.DestroyAsset(tex); // the atlas is not readable — the flat tile it is
                    tex = null;
                }
            }

            Cache[id] = tex; // null too: a plain block is not probed again
            return tex;
        }

        /// <summary>Points <paramref name="raw"/> at the block's icon: the cube icon where the block has one, else its
        /// tile in the atlas.</summary>
        public static void Apply(RawImage raw, BlockTextureAtlas atlas, ushort id)
        {
            if (raw == null || atlas == null)
            {
                return;
            }

            var icon = For(atlas, id);
            if (icon != null)
            {
                raw.texture = icon;
                raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
            else
            {
                raw.texture = atlas.Texture;
                raw.uvRect = atlas.TileUv(id);
            }
        }

        /// <summary>The cube icon as a UI sprite, or null when the block has none (use its tile).</summary>
        public static Sprite SpriteFor(BlockTextureAtlas atlas, ushort id)
        {
            var icon = For(atlas, id);
            return icon == null ? null : Sprite.Create(icon, new Rect(0f, 0f, icon.width, icon.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Destroys every cached icon (tests; a switch to another atlas does it on its own).</summary>
        public static void ClearCache()
        {
            foreach (var tex in Cache.Values)
            {
                BlockCubeFaces.DestroyAsset(tex);
            }

            Cache.Clear();
            if (_atlas != null)
            {
                _atlas.Changed -= OnAtlasChanged;
                _atlas = null;
            }
        }

        private static void Adopt(BlockTextureAtlas atlas)
        {
            ClearCache(); // icons of another content snapshot
            _atlas = atlas;
            _atlas.Changed += OnAtlasChanged;
        }

        /// <summary>The atlas repainted tiles in place: repaint the icons in place too, so the images showing them keep
        /// valid textures and show the new look.</summary>
        private static void OnAtlasChanged()
        {
            var atlas = _atlas;
            if (atlas == null || atlas.Texture == null)
            {
                return;
            }

            foreach (var kv in Cache)
            {
                if (kv.Value != null)
                {
                    Paint(kv.Value, atlas, kv.Key);
                }
            }
        }

        /// <summary>
        /// Draws the cube. The view looks from the front-right, from above: a cube point (x, y, z) in [0, 1]³ lands at
        /// s = x + z, t = y + (z − x) / 2 (in units of half the icon's drawing width), so the front (−Z) face fills the
        /// left half, the +X side the right half and the top the diamond above them. Each pixel is mapped back onto the
        /// face it shows and sampled with the face's own UV rotation, so a picture stands upright and reads unmirrored.
        /// </summary>
        private static bool Paint(Texture2D tex, BlockTextureAtlas atlas, ushort id)
        {
            var content = atlas.Content;
            var block = new BlockId(id);
            var slots = new ushort[BlockCubeFaces.FaceCount];
            var turns = new int[BlockCubeFaces.FaceCount];
            BlockCubeFaces.ResolveSlots(content, atlas, block, BlockCubeFaces.FrontFor(content, block, BlockCubeFaces.FaceMinusZ), slots, turns);

            var top = TilePixels(atlas.Texture, slots[BlockCubeFaces.FacePlusY]);
            var front = TilePixels(atlas.Texture, slots[BlockCubeFaces.FaceMinusZ]);
            var side = TilePixels(atlas.Texture, slots[BlockCubeFaces.FacePlusX]);
            if (top == null || front == null || side == null)
            {
                return false;
            }

            float unit = (Size - 2f * Margin) * 0.5f; // half the drawing width = one cube edge along s
            float x0 = Margin, y0 = Margin + unit * 0.5f; // s = 0 / t = 0; the lowest corner (t = −½) sits on the margin
            var pixels = new Color[Size * Size];
            const float inv = 1f / (Samples * Samples);
            for (int py = 0; py < Size; py++)
            {
                for (int px = 0; px < Size; px++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float s = (px + (sx + 0.5f) / Samples - x0) / unit;
                            float t = (py + (sy + 0.5f) / Samples - y0) / unit;
                            if (!Sample(s, t, top, front, side, turns, out var c))
                            {
                                continue;
                            }

                            r += c.r * c.a;
                            g += c.g * c.a;
                            b += c.b * c.a;
                            a += c.a;
                        }
                    }

                    pixels[py * Size + px] = a > 0f ? new Color(r / a, g / a, b / a, a * inv) : new Color(0f, 0f, 0f, 0f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: true);
            return true;
        }

        /// <summary>The shaded colour the cube shows at view point (<paramref name="s"/>, <paramref name="t"/>); false
        /// outside the cube.</summary>
        private static bool Sample(float s, float t, Color[] top, Color[] front, Color[] side, int[] turns, out Color c)
        {
            c = default;
            // Top (y = 1): s = x + z, t = 1 + (z − x) / 2.
            float tz = (s + 2f * (t - 1f)) * 0.5f, tx = (s - 2f * (t - 1f)) * 0.5f;
            if (tx >= 0f && tx <= 1f && tz >= 0f && tz <= 1f)
            {
                // Top corners 0 → 3 run along +x, 0 → 1 along +z.
                c = Texel(top, turns[BlockCubeFaces.FacePlusY], tx, tz, TopShade);
                return true;
            }

            if (s >= 0f && s <= 1f)
            {
                // Front (z = 0): s = x, t = y − x / 2. Corners 0 → 3 run along +x, 0 → 1 along +y.
                float y = t + s * 0.5f;
                if (y >= 0f && y <= 1f)
                {
                    c = Texel(front, turns[BlockCubeFaces.FaceMinusZ], s, y, FrontShade);
                    return true;
                }

                return false;
            }

            if (s > 1f && s <= 2f)
            {
                // Side (x = 1): s = 1 + z, t = y + (z − 1) / 2. Corners 0 → 3 run along +z, 0 → 1 along +y.
                float z = s - 1f, y = t - (z - 1f) * 0.5f;
                if (y >= 0f && y <= 1f)
                {
                    c = Texel(side, turns[BlockCubeFaces.FacePlusX], z, y, SideShade);
                    return true;
                }
            }

            return false;
        }

        /// <summary>The tile texel at face parameters (<paramref name="along03"/> along corner 0 → 3,
        /// <paramref name="along01"/> along corner 0 → 1), with the face's quarter turns applied the way the mesher
        /// assigns its corner UVs — the bilinear blend of the four turned tile corners.</summary>
        private static Color Texel(Color[] tile, int turns, float along03, float along01, float shade)
        {
            Vector2 c0 = Unit(turns), c1 = Unit(turns + 1), c2 = Unit(turns + 2), c3 = Unit(turns + 3);
            float a = Mathf.Clamp01(along03), b = Mathf.Clamp01(along01);
            Vector2 f = (1f - a) * (1f - b) * c0 + (1f - a) * b * c1 + a * b * c2 + a * (1f - b) * c3;
            int n = BlockTextureAtlas.Tile;
            int x = Mathf.Clamp((int)(f.x * n), 0, n - 1), y = Mathf.Clamp((int)(f.y * n), 0, n - 1);
            var p = tile[y * n + x];
            return new Color(p.r * shade, p.g * shade, p.b * shade, p.a);
        }

        /// <summary>The unit-tile corner the mesher's <c>AddFace</c> puts at turned corner index <paramref name="i"/>.</summary>
        private static Vector2 Unit(int i) => (i & 3) switch
        {
            0 => new Vector2(0f, 0f),
            1 => new Vector2(0f, 1f),
            2 => new Vector2(1f, 1f),
            _ => new Vector2(1f, 0f),
        };

        /// <summary>The pixels of one atlas slot, or null when the atlas cannot be read back.</summary>
        private static Color[] TilePixels(Texture2D atlas, ushort slot)
        {
            int n = BlockTextureAtlas.Tile;
            try
            {
                return atlas.GetPixels((slot % BlockTextureAtlas.Cols) * n, (slot / BlockTextureAtlas.Cols) * n, n, n);
            }
            catch
            {
                return null; // uploaded non-readable — the caller keeps the flat tile (as ShapeIconFactory does)
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds distinct hotbar/inventory icons for SHAPED building blocks (sphere, pyramid, slab, …) so a
    /// crafted form no longer reads as a plain cube of its base material (#125). A shaped item key carries
    /// only the shape index (e.g. <c>"stone#s04"</c>); given that index we take the block's atlas tile and
    /// mask it to a 2-D front silhouette of the form — a stone sphere shows a stone-textured disc, a stone
    /// pyramid a stone-textured triangle, and so on. The material stays recognisable while the form becomes
    /// obvious. A 1-px darkened rim lifts the silhouette off any slot background. Results are cached per
    /// (tile id, shape). Cube (shape 0) returns null — callers keep their existing full-tile path.
    /// </summary>
    public static class ShapeIconFactory
    {
        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        /// <summary>The shape-masked icon texture for a block tile + shape, or null for cube / when the atlas
        /// is not ready or not CPU-readable.</summary>
        public static Texture2D ForBlock(BlockTextureAtlas atlas, ushort tileId, int shape)
        {
            if (atlas?.Texture == null || shape <= 0 || shape >= ShapeCode.Count)
            {
                return null;
            }

            int cacheKey = (tileId << 8) | (shape & 0xFF);
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var tex = Build(atlas.Texture, tileId, (BlockShape)shape);
            _cache[cacheKey] = tex; // cache null too, so a non-readable atlas isn't probed every frame
            return tex;
        }

        private static Texture2D Build(Texture2D atlasTex, ushort tileId, BlockShape shape)
        {
            const int n = BlockTextureAtlas.Tile; // tiles are square (64px); icon matches the tile resolution
            int ox = (tileId % BlockTextureAtlas.Cols) * BlockTextureAtlas.Tile;
            int oy = (tileId / BlockTextureAtlas.Cols) * BlockTextureAtlas.Tile;

            Color[] src;
            try
            {
                src = atlasTex.GetPixels(ox, oy, n, n);
            }
            catch
            {
                return null; // atlas was uploaded as non-readable — nothing we can do, keep the cube fallback
            }

            var mask = new bool[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n;
                    mask[y * n + x] = Inside(shape, u, v);
                }
            }

            var outp = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!mask[i])
                    {
                        outp[i] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }

                    var c = src[i];
                    if (IsRim(mask, n, x, y))
                    {
                        c = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, Mathf.Max(c.a, 0.9f));
                    }

                    outp[i] = c;
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(outp);
            tex.Apply();
            return tex;
        }

        /// <summary>A pixel is on the rim when it is inside the silhouette but touches the edge or an outside
        /// neighbour — darkened so the form stands out against the slot.</summary>
        private static bool IsRim(bool[] mask, int n, int x, int y)
        {
            if (x == 0 || y == 0 || x == n - 1 || y == n - 1)
            {
                return true;
            }

            return !mask[y * n + x - 1] || !mask[y * n + x + 1]
                || !mask[(y - 1) * n + x] || !mask[(y + 1) * n + x];
        }

        /// <summary>The 2-D front silhouette of each shape in unit coordinates (u right 0..1, v up 0..1, the
        /// flat-bottomed forms resting on v=0). Kept simple and visually distinct rather than a true
        /// projection — the goal is "tell the forms apart at a glance".</summary>
        private static bool Inside(BlockShape shape, float u, float v)
        {
            switch (shape)
            {
                case BlockShape.Slab: // short, full-width bar across the bottom
                    return v <= 0.5f;
                case BlockShape.Pyramid: // straight-sided triangle, apex centred at the top
                    return Mathf.Abs(u - 0.5f) <= 0.5f * (1f - v);
                case BlockShape.Cone: // like the pyramid but with convex sides → a rounded peak
                    return Mathf.Abs(u - 0.5f) <= 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - v));
                case BlockShape.Dome: // flat-bottomed half-ellipse
                {
                    float du = (u - 0.5f) / 0.5f;
                    return du * du + v * v <= 1f;
                }
                case BlockShape.Sphere: // full disc
                {
                    float du = u - 0.5f, dv = v - 0.5f;
                    return du * du + dv * dv <= 0.25f;
                }
                case BlockShape.Ramp: // right triangle: floor on the bottom, hypotenuse rising to the right
                    return v <= u;
                case BlockShape.Stairs: // two-step staircase rising to the right
                    return u < 0.5f ? v <= 0.5f : v <= 1f;
                case BlockShape.Cylinder: // upright column narrower than a full cube
                    return u >= 0.18f && u <= 0.82f;
                case BlockShape.Panel: // thin plate across the bottom
                    return v <= 0.25f;
                case BlockShape.Post: // slim full-height column
                    return u >= 0.34f && u <= 0.66f;
                case BlockShape.Beam: // horizontal bar across the middle
                    return v >= 0.35f && v <= 0.65f;
                case BlockShape.LowRamp: // right triangle rising to the right, half height
                    return v <= 0.5f * u;
                default:
                    return true; // cube — full tile (callers never ask us for this)
            }
        }
    }
}

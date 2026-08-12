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

        /// <summary>Destroys every cached icon texture and empties the cache. Called from
        /// <see cref="GameBootstrap"/>'s teardown — the icons are built from THAT session's atlas, and the
        /// (tile id, shape) key would serve stale art for a different world's content (#423).
        /// <para>The textures are destroyed explicitly (#966): this used to only clear the dictionary and
        /// rely on the return-to-menu <c>Resources.UnloadUnusedAssets</c> pass, but these are code-created
        /// textures, and a dropped reference alone never frees one.</para></summary>
        public static void ClearCache()
        {
            foreach (var tex in _cache.Values)
            {
                if (tex != null)
                {
                    Object.Destroy(tex);
                }
            }

            _cache.Clear();
        }

        /// <summary>The shape-masked icon texture for a block tile + shape, or null for cube / when the atlas
        /// is not ready or not CPU-readable.</summary>
        public static Texture2D ForBlock(BlockTextureAtlas atlas, ushort tileId, int shape)
            => ForBlock(atlas, tileId, shape, null);

        /// <summary>As above, plus the player-designed forms (#844): their silhouette is projected out of the
        /// form's own micro-voxel grid instead of a hand-written curve, so a self-made form is as recognisable
        /// in the hotbar as a built-in one. <paramref name="registry"/> may be null (built-in forms only).</summary>
        public static Texture2D ForBlock(BlockTextureAtlas atlas, ushort tileId, int shape, CustomShapeRegistry registry)
        {
            if (atlas?.Texture == null || shape <= 0)
            {
                return null;
            }

            bool custom = ShapeCode.IsCustomShape(shape);
            if (!custom && shape >= ShapeCode.Count)
            {
                return null;
            }

            string voxels = null;
            if (custom && (registry == null || !registry.TryGetVoxels(shape, out voxels)))
            {
                return null; // unknown form — the caller keeps its plain-cube fallback
            }

            int cacheKey = (tileId << 8) | (shape & 0xFF);
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var tex = custom
                ? BuildFromVoxels(atlas.Texture, tileId, voxels)
                : Build(atlas.Texture, tileId, (BlockShape)shape);
            _cache[cacheKey] = tex; // cache null too, so a non-readable atlas isn't probed every frame
            return tex;
        }

        /// <summary>Builds the icon for a player-designed form: the silhouette is the form's own front
        /// projection, point-scaled up to the tile resolution.</summary>
        private static Texture2D BuildFromVoxels(Texture2D atlasTex, ushort tileId, string voxels)
        {
            var mask = CustomShape.Silhouette(voxels, out int grid);
            if (grid == 0)
            {
                return null;
            }

            // The voxel grid has y UP; the icon mask is sampled with v up as well, so rows map straight across.
            return BuildMasked(atlasTex, tileId, (u, v) =>
            {
                int gx = Mathf.Clamp((int)(u * grid), 0, grid - 1);
                int gy = Mathf.Clamp((int)(v * grid), 0, grid - 1);
                return mask[gy * grid + gx];
            });
        }

        private static Texture2D Build(Texture2D atlasTex, ushort tileId, BlockShape shape)
            => BuildMasked(atlasTex, tileId, (u, v) => Inside(shape, u, v));

        /// <summary>Masks a block's atlas tile with a silhouette test — shared by the built-in forms (analytic
        /// curves) and the player-designed ones (their own voxel projection).</summary>
        private static Texture2D BuildMasked(Texture2D atlasTex, ushort tileId, System.Func<float, float, bool> inside)
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
                    mask[y * n + x] = inside(u, v);
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
                case BlockShape.QuarterCube: // small square in the lower-left quadrant
                    return u <= 0.5f && v <= 0.5f;
                case BlockShape.Table: // top plate on two visible legs
                    return v >= 0.8f || ((u >= 0.08f && u <= 0.24f) || (u >= 0.76f && u <= 0.92f));
                case BlockShape.Chair: // side view: seat bar, backrest on the right, two legs
                    return (v >= 0.35f && v <= 0.5f && u <= 0.9f)
                        || (u >= 0.72f && u <= 0.9f && v >= 0.35f)
                        || (v <= 0.35f && ((u >= 0.12f && u <= 0.26f) || (u >= 0.74f && u <= 0.88f)));
                case BlockShape.Fence: // two posts + two rails
                    return (u >= 0.08f && u <= 0.24f && v <= 0.85f) || (u >= 0.76f && u <= 0.92f && v <= 0.85f)
                        || (v >= 0.55f && v <= 0.7f) || (v >= 0.2f && v <= 0.35f);
                case BlockShape.Sheet: // hairline plate across the bottom
                    return v <= 0.1f;
                case BlockShape.Pot: // small centred planter with a wider rim
                    return (u >= 0.28f && u <= 0.72f && v <= 0.42f) || (u >= 0.22f && u <= 0.78f && v >= 0.34f && v <= 0.5f);
                default:
                    return true; // cube — full tile (callers never ask us for this)
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The atlases derived from the colour atlas: the normal atlas (with the cavity AO in alpha) and, on a
    /// desktop client, the height atlas the High preset's parallax marches (#3055).</summary>
    public sealed partial class BlockTextureAtlas
    {
        private static readonly int HeightTexId = Shader.PropertyToID("_HeightTex");

        /// <summary>Per-texel height of every tile (R8, linear, 1 = the face's surface, darker = deeper), the same layout
        /// as <see cref="Texture"/>. Null in the browser and on mobile, where the High extras never run — the shader's
        /// default (white = flat) then stands in.</summary>
        public Texture2D HeightTexture { get; private set; }

        private Texture2D _retiredHeight;

        /// <summary>Derives the normal (and height) atlas from the finished colour atlas, one tile at a time: the
        /// luminance gradient (brighter = higher) becomes a surface normal, its steepness the cavity AO in alpha. The
        /// neighbour lookups wrap inside the tile (#3065 — they used to clamp to the atlas edge, so every tile's border
        /// row took its relief from the neighbouring texture); block tiles repeat seamlessly, so wrapping is exact.</summary>
        private void BuildNormalAtlas()
        {
            int w = Cols * Tile, h = Rows * Tile;
            // linear: normals are data, not colour — without this flag a Linear-color-space build would
            // sRGB-decode the encoded normals on sample and break the per-pixel lighting on every block.
            NormalTexture = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            // Color32 (4 B/px) instead of Color (16 B/px): the float arrays were the largest contiguous managed
            // allocations in the shell and the first thing to fail with OutOfMemory in a long session (#1463).
            var src = Texture.GetPixels32();
            var dst = new Color32[w * h];
            bool heights = HighDetails.DesktopClass;
            var height = heights ? new byte[w * h] : null;
            var lum = new float[Tile * Tile];
            var blur = new float[Tile * Tile];
            for (int ty = 0; ty < Rows; ty++)
            {
                for (int tx = 0; tx < Cols; tx++)
                {
                    int ox = tx * Tile, oy = ty * Tile;
                    LoadTileLuminance(src, w, ox, oy, lum);
                    WriteTileNormals(lum, dst, w, ox, oy);
                    if (heights)
                    {
                        WriteTileHeights(lum, blur, height, w, ox, oy);
                    }
                }
            }

            NormalTexture.SetPixels32(dst);
            // #1523: nothing reads the normal atlas back → drop its CPU copy (-5 MiB). The colour atlas stays
            // readable on purpose: AverageColor, the editor palette and the shape icons sample its tiles.
            NormalTexture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            _retiredHeight = HeightTexture; // a repaint's old one, destroyed after the owners re-bound (DestroyRetired)
            HeightTexture = heights ? BuildHeightTexture(height, w, h) : null;
        }

        private static void LoadTileLuminance(Color32[] src, int w, int ox, int oy, float[] lum)
        {
            const float inv255 = 1f / 255f;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    var c = src[(oy + y) * w + ox + x];
                    lum[y * Tile + x] = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) * inv255;
                }
            }
        }

        private static float Wrapped(float[] tile, int x, int y) => tile[((y + Tile) % Tile) * Tile + (x + Tile) % Tile];

        private static void WriteTileNormals(float[] lum, Color32[] dst, int w, int ox, int oy)
        {
            const float strength = 1.6f;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float dx = Wrapped(lum, x + 1, y) - Wrapped(lum, x - 1, y);
                    float dy = Wrapped(lum, x, y + 1) - Wrapped(lum, x, y - 1);
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    // Cavity/crevice AO packed into the (otherwise unused) alpha: steep luminance edges —
                    // painted cracks, seams, rivets, grain — read as shallow pits, so the block shader can
                    // darken them a touch for texture-scale depth on top of the per-vertex AO.
                    float cav = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.0f);
                    dst[(oy + y) * w + ox + x] = new Color32(
                        (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt(cav * 255f));
                }
            }
        }

        /// <summary>The tile's height field: a 3×3 box-blurred luminance (smooth enough to march), normalised so the
        /// brightest texel is the face itself (1) and the darkest the deepest pit (0). A low-contrast tile keeps a
        /// shallow relief — its range is measured against a floor of 0.15 — so a plain tile does not sink as a whole.</summary>
        private static void WriteTileHeights(float[] lum, float[] blur, byte[] height, int w, int ox, int oy)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float sum = 0f;
                    for (int j = -1; j <= 1; j++)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            sum += Wrapped(lum, x + i, y + j);
                        }
                    }

                    float b = sum / 9f;
                    blur[y * Tile + x] = b;
                    lo = Mathf.Min(lo, b);
                    hi = Mathf.Max(hi, b);
                }
            }

            float range = Mathf.Max(hi - lo, 0.15f);
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    float hgt = 1f - (hi - blur[y * Tile + x]) / range;
                    height[(oy + y) * w + ox + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(hgt) * 255f);
                }
            }
        }

        private static Texture2D BuildHeightTexture(byte[] height, int w, int h)
        {
            // Bilinear (unlike the point-filtered colour): the parallax march wants a smooth field. Linear data.
            var tex = new Texture2D(w, h, TextureFormat.R8, mipChain: true, linear: true)
            {
                name = "BlockHeightAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixelData(height, 0);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>Binds the derived atlases to a block material (see <see cref="BindNormals"/>).</summary>
        private void SetDerivedTextures(Material material)
        {
            material.SetTexture(NormalTexId, NormalTexture);
            if (HeightTexture != null)
            {
                material.SetTexture(HeightTexId, HeightTexture);
            }
        }

        /// <summary>Destroys a repaint's replaced normal and height atlases once every owner holds the new ones.</summary>
        private void DestroyRetired(Texture2D oldNormals)
        {
            if (oldNormals != null)
            {
                Object.Destroy(oldNormals);
            }

            if (_retiredHeight != null && _retiredHeight != HeightTexture)
            {
                Object.Destroy(_retiredHeight);
            }

            _retiredHeight = null;
        }
    }
}

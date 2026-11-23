// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The small atlas the texture editor's block wall draws from. It has the world atlas's layout
    /// (32 × 32 cells: the block shader finds a cell, its edge and its mean by <c>floor(uv * 32)</c>), so the chunk mesher's
    /// UVs land on the same slots, but only the cells the wall shows are written — each with its own mip chain down to one
    /// texel, which is all the shader reads (the texture calm reads a cell's mean at mip <see cref="MeanMip"/>). Colour,
    /// normal (cavity in alpha) and, on a desktop client, height, derived per tile exactly as the world atlas derives them.
    /// <para>
    /// The cell size is 64 px (the true pixels) on a desktop client on Medium or High, 32 px elsewhere (the browser,
    /// mobile, Potato and Low), where a 2048² atlas would cost too much memory for a 200 px picture: 32 cells of 32 px are
    /// 1024², about 5.5 MB per texture with its mips. The textures ignore the global mip limit, so the mean is where
    /// <see cref="MeanMip"/> says on every tier.
    /// </para>
    /// </summary>
    internal sealed class LookPreviewAtlas
    {
        private const int Cells = LookPreviewCells.AtlasCells;
        private const int Tile = LookPreviewCells.TileSize;

        public LookPreviewAtlas()
        {
            Cell = HighDetails.DesktopClass && FxKit.Preset >= QualityPreset.Medium ? Tile : Tile / 2;
            int size = Cells * Cell;
            Colour = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "LookPreviewAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                ignoreMipmapLimit = true,
            };
            Normal = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: true)
            {
                name = "LookPreviewNormals",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                ignoreMipmapLimit = true,
            };
            if (HighDetails.DesktopClass)
            {
                Height = new Texture2D(size, size, TextureFormat.R8, mipChain: true, linear: true)
                {
                    name = "LookPreviewHeights",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    ignoreMipmapLimit = true,
                };
            }
        }

        /// <summary>Pixels per cell (64 or 32).</summary>
        public int Cell { get; }

        /// <summary>The mip with one texel per cell — the block shader's <c>_Sc_Look2.x</c> for this atlas.</summary>
        public int MeanMip => LookPreviewCells.Log2(Cell);

        public Texture2D Colour { get; private set; }

        public Texture2D Normal { get; private set; }

        /// <summary>The height field for the High preset's parallax; null in the browser and on mobile (the shader's
        /// default, flat, stands in — as for the world there).</summary>
        public Texture2D Height { get; private set; }

        /// <summary>Writes one 64 × 64 tile (RGBA32, rows bottom-up) into <paramref name="slot"/>: colour, normal and height,
        /// each with the cell's mip chain. <see cref="Apply"/> uploads.</summary>
        public void Write(int slot, byte[] tile)
        {
            if (Colour == null || tile == null || tile.Length < Tile * Tile * 4 || slot < 0 || slot >= Cells * Cells)
            {
                return;
            }

            var colour = new Color32[Tile * Tile];
            for (int i = 0; i < colour.Length; i++)
            {
                colour[i] = new Color32(tile[i * 4], tile[(i * 4) + 1], tile[(i * 4) + 2], tile[(i * 4) + 3]);
            }

            var normals = new Color32[Tile * Tile];
            var heights = Height != null ? new byte[Tile * Tile] : null;
            BlockTextureAtlas.PreviewDerived(colour, normals, heights);

            WriteChain(Colour, slot, ToCell(tile, 4), 4);
            WriteChain(Normal, slot, ToCell(Bytes(normals), 4), 4);
            if (heights != null)
            {
                WriteChain(Height, slot, ToCell(heights, 1), 1);
            }
        }

        /// <summary>Uploads what <see cref="Write"/> changed (every mip as written — no mip generation over the empty cells).</summary>
        public void Apply()
        {
            Colour?.Apply(updateMipmaps: false);
            Normal?.Apply(updateMipmaps: false);
            Height?.Apply(updateMipmaps: false);
        }

        public void Destroy()
        {
            EditorSceneKit.Discard(Colour);
            EditorSceneKit.Discard(Normal);
            EditorSceneKit.Discard(Height);
            Colour = null;
            Normal = null;
            Height = null;
        }

        /// <summary>The 64 px tile at the cell size: itself, or halved once for 32 px cells.</summary>
        private byte[] ToCell(byte[] tile, int channels)
        {
            if (Cell == Tile)
            {
                return tile;
            }

            return channels == 4 ? LookPreviewCells.Halve(tile, Tile) : LookPreviewCells.HalveR8(tile, Tile);
        }

        /// <summary>The cell's level 0 and its halvings into the cell's region of every mip it has its own texels in.</summary>
        private void WriteChain(Texture2D texture, int slot, byte[] level0, int channels)
        {
            List<byte[]> chain = channels == 4 ? LookPreviewCells.MipChain(level0, Cell) : LookPreviewCells.MipChainR8(level0, Cell);
            int cx = slot % Cells, cy = slot / Cells;
            for (int m = 0; m < chain.Count && m < texture.mipmapCount; m++)
            {
                int size = Mathf.Max(1, Cell >> m);
                if (channels == 4)
                {
                    texture.SetPixels32(cx * size, cy * size, size, size, Colours(chain[m]), m);
                }
                else
                {
                    texture.SetPixels(cx * size, cy * size, size, size, Greys(chain[m]), m);
                }
            }
        }

        private static byte[] Bytes(Color32[] px)
        {
            var raw = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                raw[i * 4] = px[i].r;
                raw[(i * 4) + 1] = px[i].g;
                raw[(i * 4) + 2] = px[i].b;
                raw[(i * 4) + 3] = px[i].a;
            }

            return raw;
        }

        private static Color32[] Colours(byte[] raw)
        {
            var px = new Color32[raw.Length / 4];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(raw[i * 4], raw[(i * 4) + 1], raw[(i * 4) + 2], raw[(i * 4) + 3]);
            }

            return px;
        }

        /// <summary>One byte per texel as colours for an R8 texture (SetPixels writes the red channel).</summary>
        private static Color[] Greys(byte[] raw)
        {
            var px = new Color[raw.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float v = raw[i] / 255f;
                px[i] = new Color(v, v, v, 1f);
            }

            return px;
        }
    }
}

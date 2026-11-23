// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- the texture editor's block wall
    //
    // The wall (LookBlockWall) draws a few blocks with the block shader from a small preview atlas of the same layout, so
    // the tile being painted shows at once without the world's atlas being touched. What it needs from this atlas: a
    // cell's current pixels, whether a slot counts as a calm tile, which slots are a block's variants and whether those
    // are the flora kind, and the normal and height of a tile derived exactly as BuildNormalAtlas derives them.
    public sealed partial class BlockTextureAtlas
    {
        /// <summary>The pixels in an atlas slot right now (RGBA32, rows bottom-up).</summary>
        internal byte[] PreviewSlotPixels(int slot) => slot > 0 && slot < Cols * Rows ? ReadSlot(slot % Cols * Tile, slot / Cols * Tile) : null;

        /// <summary>True when the slot holds a calm tile (its variants are mirrored, not cracked).</summary>
        internal bool PreviewIsCalmSlot(int slot) => IsCalmSlot(slot);

        /// <summary>The index (0 or 1) of <paramref name="slot"/> among <paramref name="baseId"/>'s variant tiles, -1 when
        /// it is none of them; <paramref name="flora"/> tells the plant kind (painted along the silhouette).</summary>
        internal int PreviewVariantIndex(ushort baseId, int slot, out bool flora)
        {
            flora = false;
            if (!_variants.TryGetValue(baseId, out var slots))
            {
                return -1;
            }

            for (int v = 0; v < slots.Length; v++)
            {
                if (slots[v] == slot)
                {
                    var def = _content?.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(baseId));
                    flora = def != null && System.Array.IndexOf(FloraVariantKeys, def.Key) >= 0;
                    return v;
                }
            }

            return -1;
        }

        /// <summary>The normal (with the cavity in alpha) and, when <paramref name="heights"/> is given, the height field of
        /// one 64 × 64 tile — the very maths of <see cref="BuildNormalAtlas"/>, wrapping inside the tile.</summary>
        internal static void PreviewDerived(Color32[] tile, Color32[] normals, byte[] heights)
        {
            var lum = new float[Tile * Tile];
            LoadTileLuminance(tile, Tile, 0, 0, lum);
            WriteTileNormals(lum, normals, Tile, 0, 0);
            if (heights != null)
            {
                WriteTileHeights(lum, new float[Tile * Tile], heights, Tile, 0, 0);
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3289: the rune stone and the ancient brick take the colours of the biome they stand in. A planet chunk's build gets
    /// the column → palette lookup (<c>biomePalette</c> of <see cref="BuildGeometry"/>, the Client.Core
    /// <c>BiomeLookResolver</c>); a look block's cell then shows its palette's tile (<see cref="BlockTextureAtlas.BiomeLookSlot"/>)
    /// on every face that shows the block's own tile — cube and shape alike. Ships, the speeder and the held miniatures pass no
    /// lookup and keep the own tile, and so does a column whose biome names no palette.
    /// </summary>
    public static partial class ChunkMesher
    {
        /// <summary>Swaps <paramref name="uv"/> to the look of the palette the column (<paramref name="worldX"/>,
        /// <paramref name="worldZ"/>) wears, when the block has looks and the column a palette. The look is another tile than
        /// the block's own, so its animation code (the own tile's frame strip) is dropped with it.</summary>
        private static void ApplyBiomeLook(BlockTextureAtlas atlas, System.Func<int, int, int> biomePalette, BlockId id,
            int worldX, int worldZ, ref Rect uv, ref float animCode)
        {
            if (atlas == null || biomePalette == null || !atlas.WearsBiomeLooks(id.Value))
            {
                return;
            }

            int palette = biomePalette(worldX, worldZ);
            ushort slot = palette >= 0 ? atlas.BiomeLookSlot(id.Value, palette) : (ushort)0;
            if (slot == 0)
            {
                return;
            }

            uv = atlas.TileUv(slot);
            animCode = 0f;
        }

        /// <summary>The colour a cell's surface is recoloured with (0 = none): the player's dye, else the colour of the
        /// cell's glow (#2036). A look block keeps its own colours under a glow — the glow still lights it and its
        /// surroundings, but repainting it would wash out the notes of a monument's runes; a real dye still wins.</summary>
        private static int SurfaceTintOf(BlockTextureAtlas atlas, BlockId id, int modTint, int modGlow)
            => modTint != 0 ? modTint : atlas != null && atlas.WearsBiomeLooks(id.Value) ? 0 : modGlow;
    }
}

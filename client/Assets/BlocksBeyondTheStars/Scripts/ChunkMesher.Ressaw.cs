// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3335: Ressaw, the still liquid that floods the buried G.D.S. ruins — milky, see-through and glowing. It is a
    /// see-through block (<see cref="IsTransparentSlow"/>) that meshes like water (<c>TraitWater</c>: the inset surface, the
    /// per-corner light on its top, faces only toward air), so its top sits in the basin like a liquid surface and the walls
    /// behind it keep their faces. But every one of its faces carries <see cref="MilkyLiquidMode"/> in TEXCOORD2.x instead of
    /// the water-body weights, and the transparent shader draws that mode in the liquid's own milky white with its glow —
    /// never in the world's water colour, and without waves, foam, refraction or reflection (it stands still). The light it
    /// casts is the block data's (<c>lightColor</c>, surface cells only). Oil and every other liquid keep the opaque cube.
    /// </summary>
    public static partial class ChunkMesher
    {
        /// <summary>The TEXCOORD2.x code of a Ressaw face in the transparent shader (water 1..4, the gas sea 5, the dense
        /// gas 6, Ressaw 7).</summary>
        public const float MilkyLiquidMode = 7f;

        private sealed partial class BlockTraits
        {
            private int _ressawId = -2; // -2 = not looked up yet, -1 = this content has no Ressaw

            /// <summary>Whether <paramref name="id"/> is Ressaw. The id is looked up once per content snapshot (a benign race
            /// between worker threads writes the same value twice).</summary>
            public bool IsRessaw(BlockId id)
            {
                int ressaw = _ressawId;
                if (ressaw == -2)
                {
                    ressaw = Content.GetBlock(LiquidLook.RessawKey)?.NumericId.Value ?? -1;
                    _ressawId = ressaw;
                }

                return id.Value == ressaw;
            }
        }
    }
}

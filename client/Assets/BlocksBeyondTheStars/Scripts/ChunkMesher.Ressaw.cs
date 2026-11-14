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
    /// the water-body weights, and the transparent shader draws that mode in the liquid's own colour (its tile) with its glow —
    /// never in the world's water colour, and without waves, foam, refraction or reflection (it stands still). The light it
    /// casts is the block data's (<c>lightColor</c>, surface cells only). Oil and every other liquid keep the opaque cube.
    /// <para>#3644: the Grand Technicians' red fluid (<see cref="LiquidLook.RedFluidKey"/>) wears the same look — its tile is
    /// red, so it is drawn red. <see cref="LiquidLook.GlowsStill(string)"/> names both; only the look is shared.</para>
    /// </summary>
    public static partial class ChunkMesher
    {
        /// <summary>The TEXCOORD2.x code of a still, glowing liquid's face (Ressaw, the red fluid) in the transparent shader
        /// (water 1..4, the gas sea 5, the dense gas 6, the milky liquids 7).</summary>
        public const float MilkyLiquidMode = 7f;

        private sealed partial class BlockTraits
        {
            /// <summary>Set once both ids are looked up (so content without either liquid is not looked up again).</summary>
            private const int StillGlowLookedUp = 1 << 30;

            /// <summary>0 = not looked up yet; else <see cref="StillGlowLookedUp"/> | (Ressaw id + 1) | (red fluid id + 1) &lt;&lt; 15,
            /// with 0 for a liquid this content lacks. One int, so worker threads read both ids or neither (a benign race writes
            /// the same value twice).</summary>
            private int _stillGlowIds;

            /// <summary>Whether <paramref name="id"/> is one of the still, glowing liquids (Ressaw, the red fluid). The ids are
            /// looked up once per content snapshot.</summary>
            public bool GlowsStill(BlockId id)
            {
                int ids = _stillGlowIds;
                if (ids == 0)
                {
                    ids = StillGlowLookedUp | IdPlusOne(LiquidLook.RessawKey) | (IdPlusOne(LiquidLook.RedFluidKey) << 15);
                    _stillGlowIds = ids;
                }

                int v = id.Value + 1;
                return v == (ids & 0x7FFF) || v == ((ids >> 15) & 0x7FFF);
            }

            private int IdPlusOne(string key) => (Content.GetBlock(key)?.NumericId.Value ?? -1) + 1;
        }
    }
}

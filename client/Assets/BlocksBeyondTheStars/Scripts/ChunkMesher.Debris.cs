// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    public static partial class ChunkMesher
    {
        /// <summary>Textured debris: the tint mode a planet chunk gives a block's faces by its kind alone —
        /// 4 for a trunk or a solid plant body (the bark recolour), 1 for a wild plant or a tinted crown (the flora
        /// recolour), 0 for a block in its tile's own colours. The rule of the build loop without its per-cell cases
        /// (a dyed cell, a painted hull), so the debris of a block wears the colour its faces wore.</summary>
        public static int WorldTintMode(GameContent content, BlockId id)
        {
            if (content == null || id.IsAir)
            {
                return 0;
            }

            uint flags = TraitsFor(content).FlagsOf(id);
            if ((flags & (TraitWood | TraitTintedBody)) != 0)
            {
                return 4;
            }

            return (flags & TraitFlora) != 0 && (flags & TraitCultivated) == 0 ? 1 : 0;
        }

        /// <summary>Regrow marker: true for a plant that is drawn as a crossed cutout picture (its tile
        /// has holes) — false for a solid plant form (a cactus, a cap, a crystal), whose tile is whole.</summary>
        public static bool IsCutoutPlant(GameContent content, BlockId id)
            => content != null && (TraitsFor(content).FlagsOf(id) & TraitThinPlant) != 0;

        /// <summary>Debris sizes: how the pieces of a broken block fit what the block was in its cell.
        /// <c>Size</c> = their size against a full cube's pieces, <c>Spread</c> = how far from the cell's middle they start
        /// (1 = the eight octants of a full block), <c>Lift</c> = their start moved up or down (metres). A block that
        /// fills its cell — earth, rock, a trunk, a machine, the saguaro, a pumpkin — keeps the classic pieces. A thing
        /// that stands slim in its cell breaks into much smaller pieces that start where it stood: a plant drawn as a
        /// picture or a body, a solid plant form (cactus, cap, crystal), a torch, a lantern, a ladder, a thin wall, a
        /// flower pot. A crown cell is a cluster of small cubes and breaks into pieces of about their size.</summary>
        public static (float Size, float Spread, float Lift) DebrisFit(GameContent content, BlockId id)
        {
            if (content == null || id.IsAir)
            {
                return (1f, 1f, 0f);
            }

            uint flags = TraitsFor(content).FlagsOf(id);
            if ((flags & TraitTintedBody) != 0)
            {
                return (1f, 1f, 0f); // a solid plant body that fills its cell
            }

            if ((flags & (TraitSlimProp | TraitFlowerPot | TraitIronWall)) != 0)
            {
                return (0.28f, 0.45f, -0.1f);
            }

            if ((flags & (TraitFloraPrefix | TraitBredPlant)) != 0)
            {
                if ((flags & TraitSolidFlora) != 0)
                {
                    return (0.34f, 0.55f, -0.1f); // a cactus, a cap, a crystal: a form of its own inside the cell
                }

                return (0.28f, 0.5f, (flags & TraitHangingFlora) != 0 ? 0.15f : -0.15f); // rooted below — or above
            }

            return (flags & TraitFoliage) != 0 ? (0.55f, 0.9f, 0f) : (1f, 1f, 0f);
        }
    }
}

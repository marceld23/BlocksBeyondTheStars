// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How deep a block's faces read under the High preset's parallax occlusion mapping (#3055): 0 = flat .. 1 = deep.
    /// A block's own <see cref="BlockDefinition.Relief"/> wins; otherwise the built-in look decides — brick and masonry
    /// deepest (mortar joints), rock and ore deep (cracks, nuggets), metal plating shallow (seams, rivets), everything
    /// soft, built, glowing or alive stays flat. Unity-free so the headless suite pins it.
    /// </summary>
    public static class BlockRelief
    {
        /// <summary>The relief of masonry: brick, cobble and tile joints.</summary>
        public const float Masonry = 1.0f;

        /// <summary>The relief of natural rock and ore.</summary>
        public const float Rock = 0.8f;

        /// <summary>The relief of metal plating and panels.</summary>
        public const float Metal = 0.5f;

        /// <summary>The relief of a block: its data value (clamped to 0..1) or <see cref="Default"/>.</summary>
        public static float For(BlockDefinition? def)
        {
            if (def == null)
            {
                return 0f;
            }

            return def.Relief.HasValue ? Math.Clamp(def.Relief.Value, 0f, 1f) : Default(def);
        }

        /// <summary>The built-in relief by key and category (see the class summary). Only plain building, terrain and ore
        /// blocks get depth — machines, lights, doors, flora and picture tiles (signs, screens) keep their flat faces.</summary>
        public static float Default(BlockDefinition def)
        {
            string key = def.Key ?? string.Empty;
            bool plain = def.Category is "building" or "terrain" or "ore";
            if (!plain || string.Equals(def.TileKind, "picture", StringComparison.Ordinal))
            {
                return 0f;
            }

            if (Has(key, "brick") || Has(key, "masonry") || Has(key, "cobble"))
            {
                return Masonry;
            }

            if (Has(key, "iron") || Has(key, "steel") || Has(key, "metal") || Has(key, "panel") || Has(key, "plate")
                || Has(key, "hull"))
            {
                // An iron or metal ORE is rock first — its nuggets stand out like any other ore.
                return def.Category == "ore" ? Rock : Metal;
            }

            bool drilled = def.RequiredTool == ToolKind.Drill;
            if (def.Category == "ore" || (def.Category == "terrain" && drilled && !def.Granular))
            {
                return Rock;
            }

            return 0f;
        }

        private static bool Has(string key, string part) => key.Contains(part, StringComparison.Ordinal);
    }
}

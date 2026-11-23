// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Which bundled tiles are calm tiles. The bundled set (<c>Resources/textures</c>) is drawn in the calm style — soft
    /// forms instead of pixel grit (<c>tools/ai-assets/calm_tiles</c>) — so the block atlas leaves such a tile alone: the
    /// block shader's texture calm (the fade toward a tile's mean colour) skips its slot, and a ground block's variants are
    /// the same form mirrored with a small tone step instead of cracks and speckles.
    /// <para>
    /// Two families of bundled tiles were not redrawn and keep the classic treatment: the creature coat pattern masks
    /// (<c>creature_pattern_*</c>, pure black-and-white data) and the horn ramps (<c>creature_horn_*</c>, grey data). Neither
    /// is ever drawn into the block atlas today; the rule names them so a future use cannot pick up the calm handling by
    /// accident. Tiles painted in code (no bundled texture) and animated tiles are no calm tiles either — the caller checks
    /// those, because it knows where the pixels come from.
    /// </para>
    /// </summary>
    public static class CalmTileRule
    {
        /// <summary>Key prefixes of the bundled tiles that keep the classic treatment.</summary>
        public static readonly string[] ClassicPrefixes = { "creature_pattern_", "creature_horn_" };

        /// <summary>True when the bundled tile of <paramref name="key"/> is drawn in the calm style.</summary>
        public static bool IsCalmBundledKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (string prefix in ClassicPrefixes)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

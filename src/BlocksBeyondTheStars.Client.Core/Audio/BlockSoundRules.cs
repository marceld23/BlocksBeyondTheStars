// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a block sounds like when it is set down. Placing used to knock the same for every material
    /// (<see cref="Fallback"/>); a placed block now sounds like what it is made of — stone, metal, crystal, earth, wood
    /// or glass. The material comes from the block key (the footsteps' own heuristic, <see cref="FootstepSurface"/>), so
    /// a new block needs no entry here as long as its key says what it is.
    /// </summary>
    public static class BlockSoundRules
    {
        /// <summary>The old one-for-all knock — still played when a material's clip is not bundled.</summary>
        public const string Fallback = "place_block";

        /// <summary>The place sound of a block by its key.</summary>
        public static string PlaceCue(string? blockKey)
        {
            string k = blockKey ?? string.Empty;
            if (k.Length == 0)
            {
                return Fallback;
            }

            if (Has(k, "glass") || Has(k, "window"))
            {
                return "place_glass";
            }

            string surface = FootstepSurface.CueFor(k);
            if (surface == FootstepSurface.WoodCue)
            {
                return "place_wood";
            }

            if (surface == FootstepSurface.CrystalCue)
            {
                return "place_crystal";
            }

            if (surface == FootstepSurface.MetalCue || surface == FootstepSurface.GrateCue)
            {
                return "place_metal";
            }

            if (surface != FootstepSurface.DefaultCue || IsEarth(k))
            {
                return "place_dirt"; // sand, grass, snow, leaves — and the soft grounds the footsteps count as stone
            }

            return "place_stone";
        }

        /// <summary>The clip to play for a placed block: its material's, or <see cref="Fallback"/> when that is not
        /// bundled (<paramref name="has"/> answers whether a clip exists).</summary>
        public static string PlaceClip(string? blockKey, Func<string, bool> has)
        {
            string cue = PlaceCue(blockKey);
            return has == null || has(cue) ? cue : Fallback;
        }

        private static bool IsEarth(string k)
            => Has(k, "dirt") || Has(k, "soil") || Has(k, "mud") || Has(k, "clay") || Has(k, "gravel") || Has(k, "moss")
               || Has(k, "peat") || Has(k, "ash");

        private static bool Has(string k, string word) => k.IndexOf(word, StringComparison.Ordinal) >= 0;
    }
}

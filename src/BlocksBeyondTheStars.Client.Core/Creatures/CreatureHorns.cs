// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Horns, tusks, fangs and teeth (#2867): a grayscale horn tile and a colour per species, where every horn of every
    /// species used to be one flat dark brown and every tooth flat white. The tile (<c>creature_horn_&lt;name&gt;</c>) runs
    /// along the horn's length (rings across it), and is tinted like a hide.
    /// </summary>
    public static class CreatureHorns
    {
        /// <summary>The horn tiles (<c>creature_horn_&lt;name&gt;</c>).</summary>
        public static readonly IReadOnlyList<string> Tiles = new[] { "ridged", "grain", "spiral", "crystal", "bark" };

        /// <summary>The horn colours (crystal worlds add a light tint of the body hue).</summary>
        public static readonly IReadOnlyList<int> Palette = new[]
        {
            0xE6DCC2, // ivory
            0xCFC2A0, // bone
            0x2B2623, // black
            0xA8692A, // amber
            0x5E6670, // slate
            0x4A3A2C, // the classic dark brown
        };

        /// <summary>The dark colours of chelicerae and spikes.</summary>
        public static readonly IReadOnlyList<int> DarkPalette = new[] { 0x2B2623, 0x3A2A22, 0x4A3A2C, 0x5E6670 };

        /// <summary>Tusk ivories.</summary>
        public static readonly IReadOnlyList<int> TuskPalette = new[] { 0xEDE4CC, 0xE2D5B5, 0xD8CBA8 };

        /// <summary>Tooth whites.</summary>
        public static readonly IReadOnlyList<int> ToothPalette = new[] { 0xF7F3E6, 0xEFE6CF, 0xE7DCC0 };

        private static readonly string[] Plain = { "ridged", "grain", "spiral", "bark" };

        /// <summary>The texture key of a horn tile.</summary>
        public static string TileKey(string tile) => CreatureHides.TilePrefix + "horn_" + tile;

        /// <summary>The horn tile of a species: half the rolled species of a crystal world wear crystal horns, the rest pick
        /// by the seed (crystal rarely elsewhere). An authored species ignores the world, like its hide.</summary>
        public static string TileFor(NetCreature c, FaunaThemes.Look? look)
        {
            uint h = CoatMix.Mix(CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed), CoatMix.SaltHorn);
            if (look?.Theme == "crystal" && h % 2u == 0u && !CreatureCoat.IsAuthored(c.SpeciesId))
            {
                return "crystal";
            }

            return h % 10u == 0u ? "crystal" : Plain[(int)((h >> 4) % (uint)Plain.Length)];
        }

        /// <summary>The horn colour of a species (packed 0xRRGGBB): one of <see cref="Palette"/>; crystal horns take a light
        /// tint of the body hue instead.</summary>
        public static int ColourFor(NetCreature c, string tile)
        {
            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            if (tile == "crystal")
            {
                return CreatureEyes.Shade(c.ColorRgb & 0xFFFFFF, 1.55f);
            }

            return Palette[(int)(CoatMix.Mix(seed, CoatMix.SaltHornColour) % (uint)Palette.Count)];
        }

        /// <summary>The dark colour of an arachnid's fangs or a sandworm's spikes.</summary>
        public static int DarkColourFor(NetCreature c)
            => DarkPalette[(int)(CoatMix.Mix(CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed), CoatMix.SaltHornColour) % (uint)DarkPalette.Count)];

        /// <summary>The tusk ivory of a species.</summary>
        public static int TuskColourFor(NetCreature c)
            => TuskPalette[(int)(CoatMix.Mix(CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed), CoatMix.SaltHornColour) % (uint)TuskPalette.Count)];

        /// <summary>The tooth white of a species.</summary>
        public static int ToothColourFor(NetCreature c)
            => ToothPalette[(int)((CoatMix.Mix(CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed), CoatMix.SaltHornColour) >> 5) % (uint)ToothPalette.Count)];
    }
}

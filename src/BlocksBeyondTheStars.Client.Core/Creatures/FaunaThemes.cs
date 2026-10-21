// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The look of a world's fauna (#2868): every planet has a THEME (cold worlds wear warm coats, deserts scales and
    /// leather, …) and every world also rolls a random MOTIF — a favourite hide family and a favourite pattern group — so
    /// two desert worlds still differ. Pure functions of the planet type and the world's look seed; the server puts both on
    /// the wire per creature (<c>NetCreature.LookPlanet</c>/<c>LookSeed</c>, stamped into a companion's or a clone's
    /// species so it keeps its home look elsewhere). Without them (an older server) there is no theme and the hide pools
    /// pick freely.
    /// </summary>
    public static class FaunaThemes
    {
        /// <summary>The hide families a theme or a motif names (every member is a hide of <see cref="CreatureHides.All"/>).
        /// A hide may sit in more than one family.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Families = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["warmcoat"] = new[] { "fur", "wool", "shaggy", "down", "velvet", "bristles" },
            ["softskin"] = new[] { "skin", "leather", "wrinkled", "hide" },
            ["scales"] = new[] { "scales", "finescales", "coarsescales", "lizard", "snake", "pebbled" },
            ["armour"] = new[] { "plated", "chitin", "quilted", "honeycomb", "ridged", "metallic", "spined" },
            ["feathers"] = new[] { "feathers", "finefeathers", "down", "featherscales" },
            ["wet"] = new[] { "slime", "slick", "warty", "knobbly", "finned", "tentacled" },
            ["bark"] = new[] { "barkskin", "barkmoss", "roughbark", "mossy", "lichen" },
            ["fungal"] = new[] { "fungal", "lichen", "mossy", "coralskin" },
            ["crystal"] = new[] { "crystalline", "crystalveins", "iridescent", "metallic", "honeycomb" },
            ["dry"] = new[] { "cracked", "leather", "lizard", "wrinkled", "plated" },
            ["marked"] = new[] { "spots", "stripes", "banded", "mottled" },
            ["reef"] = new[] { "coralskin", "finescales", "iridescent", "slick", "banded" },
        };

        /// <summary>The families every theme prefers, with weights (a weight-2 family is twice as likely).</summary>
        public static readonly IReadOnlyDictionary<string, (string Family, int Weight)[]> Themes =
            new Dictionary<string, (string, int)[]>(StringComparer.Ordinal)
            {
                ["cold"] = new[] { ("warmcoat", 3), ("feathers", 1), ("softskin", 1) },
                ["arid"] = new[] { ("dry", 2), ("scales", 2), ("armour", 1) },
                ["savanna"] = new[] { ("marked", 2), ("warmcoat", 1), ("dry", 1), ("softskin", 1) },
                ["tropical"] = new[] { ("feathers", 2), ("wet", 1), ("marked", 1), ("scales", 1) },
                ["swamp"] = new[] { ("wet", 3), ("bark", 1), ("scales", 1) },
                ["fungal"] = new[] { ("fungal", 3), ("wet", 1), ("bark", 1) },
                ["crystal"] = new[] { ("crystal", 3), ("armour", 1) },
                ["ashen"] = new[] { ("armour", 2), ("dry", 1), ("crystal", 1) },
                ["alien"] = new[] { ("crystal", 1), ("fungal", 1), ("wet", 1), ("marked", 1) },
                ["floral"] = new[] { ("feathers", 1), ("softskin", 1), ("marked", 1), ("warmcoat", 1) },
                ["ocean"] = new[] { ("reef", 2), ("scales", 1), ("wet", 1) },
                ["temperate"] = new[] { ("warmcoat", 1), ("softskin", 1), ("feathers", 1), ("scales", 1), ("bark", 1) },
            };

        /// <summary>The pattern groups a world's motif can favour.</summary>
        public static readonly IReadOnlyList<string> PatternGroups = new[] { "stripes", "spots", "patches", "scales", "placed" };

        /// <summary>The motif families in a stable order (the per-world roll indexes this list).</summary>
        public static readonly IReadOnlyList<string> MotifFamilies = new[]
        {
            "warmcoat", "softskin", "scales", "armour", "feathers", "wet", "bark", "fungal", "crystal", "dry", "marked", "reef",
        };

        /// <summary>Percent of rolled species whose torso comes from the planet theme (when its pool has a member there).</summary>
        public const int ThemePercent = 50;

        /// <summary>Percent of rolled species (after the theme share) whose torso comes from the world's motif family.</summary>
        public const int MotifPercent = 20;

        /// <summary>Percent of patterned rolled species that take a pattern from the world's favourite group.</summary>
        public const int PatternMotifPercent = 35;

        /// <summary>A world's fauna look: its theme key, its motif family and favourite pattern group.</summary>
        public sealed class Look
        {
            public Look(string theme, string motifFamily, string patternGroup)
            {
                Theme = theme;
                MotifFamily = motifFamily;
                PatternGroup = patternGroup;
            }

            /// <summary>A key of <see cref="Themes"/>.</summary>
            public string Theme { get; }

            /// <summary>A key of <see cref="Families"/>.</summary>
            public string MotifFamily { get; }

            /// <summary>One of <see cref="PatternGroups"/>.</summary>
            public string PatternGroup { get; }
        }

        /// <summary>The theme of a planet: its data override (<c>faunaTheme</c>) when that names a known theme, else derived
        /// from its flora theme, temperature and water.</summary>
        public static string ThemeOf(PlanetType? planet)
        {
            if (planet == null)
            {
                return "temperate";
            }

            if (!string.IsNullOrEmpty(planet.FaunaTheme) && Themes.ContainsKey(planet.FaunaTheme))
            {
                return planet.FaunaTheme;
            }

            string flora = planet.FloraTheme ?? string.Empty;
            if (flora is "tundra" or "alpine" || planet.BaseTemperature <= -5.0)
            {
                return "cold";
            }

            switch (flora)
            {
                case "desert": return "arid";
                case "savanna": return "savanna";
                case "tropical": return planet.WaterAbundance is double w && w >= 0.9 ? "ocean" : "tropical";
                case "swamp": return "swamp";
                case "fungal": return "fungal";
                case "crystal": return "crystal";
                case "ashen": return "ashen";
                case "alien": return "alien";
                case "floral": return "floral";
            }

            if (planet.BaseTemperature >= 60.0)
            {
                return "ashen"; // lava worlds and other hot rock without flora
            }

            return planet.WaterAbundance is double water && water >= 0.9 ? "ocean" : "temperate";
        }

        /// <summary>The look of the world a creature belongs to, or null when the wire carries none (an older server) or the
        /// planet is unknown to this client.</summary>
        public static Look? For(NetCreature c, Func<string, PlanetType?>? planets)
        {
            if (string.IsNullOrEmpty(c.LookPlanet) || planets == null)
            {
                return null;
            }

            var planet = planets(c.LookPlanet);
            if (planet == null)
            {
                return null;
            }

            uint seed = unchecked((uint)c.LookSeed);
            string family = MotifFamilies[(int)(CoatMix.Mix(seed, CoatMix.SaltMotif) % (uint)MotifFamilies.Count)];
            string group = PatternGroups[(int)(CoatMix.Mix(seed, CoatMix.SaltPatternMotif) % (uint)PatternGroups.Count)];
            return new Look(ThemeOf(planet), family, group);
        }

        /// <summary>The theme's hides that are in <paramref name="pool"/>, each repeated by its family weight (a hide in two
        /// preferred families counts twice). Empty when the theme and the pool share nothing.</summary>
        public static List<string> ThemeCandidates(string theme, IReadOnlyList<string> pool)
        {
            var list = new List<string>();
            if (!Themes.TryGetValue(theme, out var families))
            {
                return list;
            }

            var inPool = new HashSet<string>(pool, StringComparer.Ordinal);
            foreach (var (family, weight) in families)
            {
                foreach (string hide in Families[family])
                {
                    if (inPool.Contains(hide))
                    {
                        for (int w = 0; w < weight; w++)
                        {
                            list.Add(hide);
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>The members of a family that are in <paramref name="pool"/>.</summary>
        public static List<string> FamilyCandidates(string family, IReadOnlyList<string> pool)
        {
            var list = new List<string>();
            if (!Families.TryGetValue(family, out var members))
            {
                return list;
            }

            var inPool = new HashSet<string>(pool, StringComparer.Ordinal);
            foreach (string hide in members)
            {
                if (inPool.Contains(hide))
                {
                    list.Add(hide);
                }
            }

            return list;
        }

        /// <summary>The coat patterns of a pattern group.</summary>
        public static bool InGroup(CoatPattern pattern, string group) => group switch
        {
            "stripes" => pattern is CoatPattern.CrossStripes or CoatPattern.TigerStripes or CoatPattern.Tabby
                or CoatPattern.Brindle or CoatPattern.Okapi or CoatPattern.Chevrons or CoatPattern.Lengthwise
                or CoatPattern.TailRings,
            "spots" => pattern is CoatPattern.Spots or CoatPattern.Rosettes or CoatPattern.Speckles or CoatPattern.Dalmatian
                or CoatPattern.Cheetah or CoatPattern.Jaguar or CoatPattern.Ladybird or CoatPattern.EyeSpots,
            "patches" => pattern is CoatPattern.Patches or CoatPattern.Marbling or CoatPattern.Giraffe or CoatPattern.Pinto
                or CoatPattern.PoisonFrog or CoatPattern.Salamander,
            "scales" => pattern is CoatPattern.ScaleBands or CoatPattern.Tortoise or CoatPattern.Diamondback,
            "placed" => pattern is CoatPattern.DorsalStripe or CoatPattern.Saddle or CoatPattern.TwoTone
                or CoatPattern.Countershade or CoatPattern.EyeMask or CoatPattern.Socks or CoatPattern.CrestBand
                or CoatPattern.BellyBlaze,
            _ => false,
        };
    }
}

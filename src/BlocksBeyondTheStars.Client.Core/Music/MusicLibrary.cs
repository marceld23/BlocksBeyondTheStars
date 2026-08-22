// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>Coarse phase of the local day, as far as the music cares.</summary>
    public enum DayPhase
    {
        Day,
        Dawn,
        Night,
    }

    /// <summary>
    /// The background-music track library: which tracks fit which context (the "pools"), which neutral
    /// tracks may be blended into a biome pool as fillers, and how the local time of day tints a pool.
    ///
    /// Pure data + lookups (no UnityEngine) so it lives in Client.Core, is unit-tested headless, and the
    /// pools can be guarded by a test that checks every referenced file really ships in
    /// <c>client/Music/</c>. The Unity director (<c>ClientMusic</c>) maps its context enum to the string
    /// keys here and asks <see cref="MusicPicker"/> for the next track. Context keys are stable strings
    /// so they can double as picker bag keys.
    ///
    /// Design rules (decided 2026-08-22, #1172/#1174):
    /// <list type="bullet">
    /// <item>a biome's own tracks always hold the majority of picks (<see cref="FillerShare"/> ≤ 0.4) —
    /// every planet of one biome sounds like that biome, no per-planet randomisation;</item>
    /// <item>the neutral fillers rotate in a single shared bag, so the same neutral is not heard twice in
    /// a row across contexts;</item>
    /// <item>the time of day only changes the <em>filler</em> set (sunrise at dawn, the nocturnal track at
    /// night) — biome identity stays, the colour shifts.</item>
    /// </list>
    /// </summary>
    public static class MusicLibrary
    {
        // Context keys (also the picker's bag keys).
        public const string Menu = "menu";
        public const string Loading = "loading";
        public const string ShipInterior = "ship_interior";
        public const string Station = "station";
        public const string Space = "space";
        public const string PlanetGeneric = "planet_generic";
        public const string PlanetIce = "planet_ice";
        public const string PlanetDesert = "planet_desert";
        public const string PlanetLava = "planet_lava";
        public const string PlanetToxic = "planet_toxic";
        public const string PlanetOcean = "planet_ocean";
        public const string PlanetVerdant = "planet_verdant";
        public const string PlanetCrystal = "planet_crystal";
        public const string PlanetCave = "planet_cave";
        /// <summary>Head under water for a while on any planet (#1174): the deep-water angle.</summary>
        public const string PlanetDeep = "planet_deep";
        /// <summary>The flight system chart is open (#1174).</summary>
        public const string StarChart = "star_chart";
        /// <summary>The crafting tab has been open for a while (#1174).</summary>
        public const string Workshop = "workshop";
        /// <summary>The tech / research tab has been open for a while (#1174).</summary>
        public const string Research = "research";

        /// <summary>The track the director plays once on the first landing on a planet in this session.</summary>
        public const string ArrivalTrack = "music_planet_sunrise";

        /// <summary>The all-round neutral beds that may be blended into any surface biome pool.</summary>
        private static readonly string[] NeutralDay = { "music_idle_default", "music_idle_default_2", "music_explore_planet", "music_explore_planet_2" };
        private static readonly string[] NeutralNight = { "music_planet_night", "music_planet_night_2", "music_idle_default", "music_idle_default_2" };
        private static readonly string[] NeutralDawn = { "music_planet_sunrise", "music_planet_sunrise_2", "music_explore_planet", "music_explore_planet_2" };
        private static readonly string[] GenericDawn = { "music_planet_sunrise", "music_planet_sunrise_2" };
        private static readonly string[] GenericNight = { "music_planet_night", "music_planet_night_2" };
        /// <summary>Underground there is no sky: only the calm all-round beds, never sunrise/night.</summary>
        private static readonly string[] NeutralCave = { "music_idle_default", "music_idle_default_2" };

        private static readonly Dictionary<string, string[]> Primary = new()
        {
            [Menu] = new[] { "music_main_menu", "music_main_menu_2", "music_main_menu_3" },
            [Loading] = new[] { "music_loading", "music_loading_2", "music_loading_3" },
            [ShipInterior] = new[] { "music_ship_interior", "music_crafting_workshop", "music_research_blueprints" },
            [Station] = new[] { "music_multiplayer_hub", "music_multiplayer_hub_2", "music_multiplayer_hub_3" },
            [Space] = new[] { "music_space_orbit", "music_deep_space_lonely", "music_mystery_signal", "music_asteroid_mining", "music_cockpit_starmap" },
            [PlanetGeneric] = new[] { "music_explore_planet", "music_explore_planet_2", "music_idle_default", "music_idle_default_2" },
            [PlanetIce] = new[] { "music_planet_ice", "music_planet_ice_2", "music_planet_ice_3" },
            [PlanetDesert] = new[] { "music_planet_desert", "music_planet_desert_2", "music_planet_desert_3" },
            [PlanetLava] = new[] { "music_planet_lava", "music_planet_lava_2", "music_planet_lava_3" },
            [PlanetToxic] = new[] { "music_planet_toxic", "music_planet_toxic_2", "music_planet_toxic_3" },
            [PlanetOcean] = new[] { "music_planet_ocean", "music_planet_ocean_2", "music_planet_ocean_3" },
            [PlanetVerdant] = new[] { "music_planet_verdant", "music_planet_verdant_2" },
            [PlanetCrystal] = new[] { "music_moon_crystal", "music_explore_planet", "music_explore_planet_2" },
            [PlanetCave] = new[] { "music_planet_cave", "music_planet_cave_2", "music_planet_cave_3" },
            [PlanetDeep] = new[] { "music_planet_ocean_2" },
            [StarChart] = new[] { "music_cockpit_starmap" },
            [Workshop] = new[] { "music_crafting_workshop" },
            [Research] = new[] { "music_research_blueprints" },
        };

        private static readonly string[] Empty = Array.Empty<string>();

        /// <summary>Every context key the library knows (for tests and docs).</summary>
        public static IEnumerable<string> Contexts => Primary.Keys;

        /// <summary>The context's own tracks — the majority of what plays there.</summary>
        public static IReadOnlyList<string> PrimaryTracks(string context)
            => Primary.TryGetValue(context, out var list) ? list : Empty;

        /// <summary>The neutral tracks that may be blended into <paramref name="context"/> at the current
        /// <paramref name="phase"/> of the day. Empty for everything that is not a planet surface / cave, for
        /// the generic pool by day (its primary tracks ARE the neutrals) and for the single-track contexts.</summary>
        public static IReadOnlyList<string> FillerTracks(string context, DayPhase phase)
        {
            if (context == PlanetCave)
            {
                return NeutralCave;
            }

            if (context == PlanetGeneric)
            {
                // The generic pool already holds the all-round beds; the time of day only adds its tint.
                return phase switch
                {
                    DayPhase.Dawn => GenericDawn,
                    DayPhase.Night => GenericNight,
                    _ => Empty,
                };
            }

            if (!IsSurfaceBiome(context))
            {
                return Empty;
            }

            return phase switch
            {
                DayPhase.Dawn => NeutralDawn,
                DayPhase.Night => NeutralNight,
                _ => NeutralDay,
            };
        }

        /// <summary>Probability that a pick in <paramref name="context"/> comes from the filler set rather
        /// than the context's own tracks. Biome tracks keep the majority by design.</summary>
        public static double FillerShare(string context)
        {
            if (context == PlanetCave)
            {
                return 0.25;
            }

            if (context == PlanetGeneric)
            {
                return 0.3;
            }

            return IsSurfaceBiome(context) ? 0.35 : 0.0;
        }

        /// <summary>True for the planet-surface biome pools (ice … crystal, and verdant); the generic pool and
        /// the cave are handled separately, everything else (menu, ship, space, UI contexts) never blends.</summary>
        public static bool IsSurfaceBiome(string context) => context switch
        {
            PlanetIce or PlanetDesert or PlanetLava or PlanetToxic or PlanetOcean or PlanetVerdant or PlanetCrystal => true,
            _ => false,
        };

        /// <summary>True for every on-foot planet context (surface biomes, generic, cave, deep water).</summary>
        public static bool IsPlanet(string context)
            => IsSurfaceBiome(context) || context == PlanetGeneric || context == PlanetCave || context == PlanetDeep;

        /// <summary>Local time of day (0 = midnight, 0.5 = noon, wraps at 1) → coarse phase. Night matches the
        /// generic pool's old tint (t &lt; 0.23 or t ≥ 0.78); dawn is the first stretch of daylight.</summary>
        public static DayPhase PhaseOf(float timeOfDay)
        {
            float t = timeOfDay - (float)Math.Floor(timeOfDay);
            if (t < 0.23f || t >= 0.78f)
            {
                return DayPhase.Night;
            }

            return t < 0.30f ? DayPhase.Dawn : DayPhase.Day;
        }

        /// <summary>Maps the server's planet/biome key (data/planets.json) to a surface context key.</summary>
        public static string ContextForBiome(string? biome)
        {
            string key = (biome ?? string.Empty).ToLowerInvariant();
            switch (key)
            {
                case "ice":
                case "tundra":
                case "glacier": return PlanetIce;
                case "desert":
                case "salt_flats": return PlanetDesert;
                case "lava":
                case "ashen":
                case "volcanic": return PlanetLava;
                case "fungal":
                case "corrupted": return PlanetToxic;
                case "ocean": return PlanetOcean;
                case "swamp":
                case "jungle":
                case "forest":
                case "savanna": return PlanetVerdant;
                case "orbital_station": return Station;     // standing on a station hub
                case "ship_interior": return ShipInterior;  // safety net; Aboard usually catches this
                default:
                    // crystal / crystal_living → the sparkling moon track; rocky / varied / highland /
                    // skylands / asteroid → the generic idle pool.
                    return key.Contains("crystal") ? PlanetCrystal : PlanetGeneric;
            }
        }

        /// <summary>Every distinct track name referenced by any pool or filler set (plus the arrival track),
        /// so a test can assert that each one ships as <c>client/Music/&lt;name&gt;.mp3</c>.</summary>
        public static IReadOnlyCollection<string> AllTracks()
        {
            var all = new HashSet<string>(StringComparer.Ordinal) { ArrivalTrack };
            foreach (var list in Primary.Values)
            {
                all.UnionWith(list);
            }

            all.UnionWith(NeutralDay);
            all.UnionWith(NeutralNight);
            all.UnionWith(NeutralDawn);
            all.UnionWith(NeutralCave);
            return all;
        }
    }
}

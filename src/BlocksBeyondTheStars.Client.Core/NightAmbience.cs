// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The night layer of the ambience (#2825): a quiet loop that fades in at dusk and out at dawn on top of the day bed.
    /// The worlds are procedural, so the loop is picked from what THIS world and THIS spot actually have — never from a
    /// planet's name (Marcel: "frogs on a lava planet would be wrong"):
    /// <list type="bullet">
    /// <item>silence — no air (airless body, space sky), a cabin or station deck, a gas giant (its endless wind bed already
    /// plays), a poisoned world (contaminated fauna, corrosive air, acid falling) or a scorched one (ash flora, lava close
    /// by or abundant, ash falling, 45 °C or hotter);</item>
    /// <item>soft chimes — a living world whose flora is crystal;</item>
    /// <item>a low hum — a living world whose flora is fungal or alien;</item>
    /// <item>frogs — breathable air, life, vegetation in reach, water in reach, 12 °C or warmer and a humid world (tropical
    /// or swamp flora, or the wet season);</item>
    /// <item>crickets — breathable air, life, vegetation in reach and 6 °C or warmer;</item>
    /// <item>a soft night wind — every other world with air: frozen, barren, or air no animal breathes (a lush spot of a
    /// living world that just has no plant in reach stays quiet instead — the wind is for the world, not the spot).</item>
    /// </list>
    /// Unity-free so the headless tests can sweep every planet type of the data.
    /// </summary>
    public static class NightAmbience
    {
        /// <summary>The loop ids (bundled clips under <c>Resources/audio</c>).</summary>
        public const string Crickets = "night_crickets";

        public const string Frogs = "night_frogs";

        public const string Wind = "night_wind";

        public const string Hum = "night_hum";

        public const string Chimes = "night_chimes";

        /// <summary>No night loop (silence).</summary>
        public const string None = "";

        /// <summary>Every loop id the layer can pick.</summary>
        public static readonly string[] All = { Crickets, Frogs, Wind, Hum, Chimes };

        /// <summary>Insects sing from this air temperature up (°C, the reading at the player).</summary>
        public const float InsectMinC = 6f;

        /// <summary>Frogs call from this air temperature up (°C).</summary>
        public const float FrogMinC = 12f;

        /// <summary>From this air temperature up a spot is scorched — no night chorus (°C).</summary>
        public const float ScorchedC = 45f;

        /// <summary>A world this lava-rich is volcanic (the planet type's lava abundance).</summary>
        public const double VolcanicLava = 0.3;

        /// <summary>The wet season counts as humid from here (<see cref="WorldEnvironment.SeasonWetness"/>).</summary>
        public const float HumidSeason = 0.6f;

        /// <summary>At least this many vegetation cells in the sampled surroundings make the spot green.</summary>
        public const int MinVegetation = 3;

        /// <summary>The sun height (sin of the day angle) where the layer starts to fade in at dusk (and is gone at dawn) …</summary>
        public const float DuskSunHeight = 0.15f;

        /// <summary>… and where it is at full strength.</summary>
        public const float NightSunHeight = -0.15f;

        /// <summary>What the client's scan found around the player (a sparse sample of the nearby cells).</summary>
        public readonly struct Surroundings
        {
            public Surroundings(int vegetation, bool water, bool lava)
            {
                Vegetation = vegetation;
                Water = water;
                Lava = lava;
            }

            /// <summary>Sampled cells that are plants, leaves or grass (<see cref="IsVegetation"/>).</summary>
            public int Vegetation { get; }

            /// <summary>Water in reach.</summary>
            public bool Water { get; }

            /// <summary>Lava in reach.</summary>
            public bool Lava { get; }
        }

        /// <summary>How strongly the night layer plays at a local time of day (0 = midnight, 0.5 = noon): 0 by day, rising
        /// from a little before sunset to 1 shortly after it, and the same way back at dawn. The sun height is the sky's own
        /// day curve, <c>sin((t − 0.25)·2π)</c>.</summary>
        public static float NightLevel(float localTimeOfDay)
        {
            double sun = Math.Sin((localTimeOfDay - 0.25) * 2.0 * Math.PI);
            double x = (DuskSunHeight - sun) / (DuskSunHeight - NightSunHeight);
            x = x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
            return (float)(x * x * (3.0 - 2.0 * x));
        }

        /// <summary>A cell that counts as vegetation for the night chorus: any flora block (plants, leaves, mushrooms,
        /// crystal flowers) or grassy ground.</summary>
        public static bool IsVegetation(string? key, string? category)
            => string.Equals(category, "flora", StringComparison.Ordinal)
               || string.Equals(key, "grass", StringComparison.Ordinal)
               || string.Equals(key, "alien_grass", StringComparison.Ordinal);

        /// <summary>A water cell (the block itself, not a machine with "water" in its name).</summary>
        public static bool IsWater(string? key) => string.Equals(key, "water", StringComparison.Ordinal);

        /// <summary>A lava cell.</summary>
        public static bool IsLava(string? key) => string.Equals(key, "lava", StringComparison.Ordinal);

        /// <summary>The night loop for this world and spot, or <see cref="None"/> for silence (see the class summary for the
        /// rules). <paramref name="planet"/> is the world's planet type (null = unknown → silence).</summary>
        public static string Pick(PlanetType? planet, WorldEnvironment? env, Surroundings near)
        {
            if (planet == null || env == null)
            {
                return None;
            }

            // No air, no night chorus: an airless body or a space sky, a cabin / station deck, and a gas giant (whose own bed
            // is already an endless wind).
            if (planet.Void || planet.IsAirless || planet.SpaceSky || env.SpaceSky || env.AtmosphereDensity <= 0f || planet.IsGasWorld)
            {
                return None;
            }

            if (IsPoisoned(planet, env) || IsScorched(planet, env, near))
            {
                return None;
            }

            bool living = planet.HasLife;
            string theme = planet.FloraTheme;
            if (living && string.Equals(theme, "crystal", StringComparison.Ordinal))
            {
                return Chimes;
            }

            if (living && (string.Equals(theme, "fungal", StringComparison.Ordinal) || string.Equals(theme, "alien", StringComparison.Ordinal)))
            {
                return Hum;
            }

            bool frozen = env.Temperature < InsectMinC;
            if (frozen || !living || !env.Breathable)
            {
                return Wind; // a frozen or barren world, or air no animal of ours breathes: only the wind is out at night
            }

            if (near.Vegetation < MinVegetation)
            {
                return None; // a bare spot of a living world (a beach, a quarry): the day bed carries on alone
            }

            bool humid = string.Equals(theme, "tropical", StringComparison.Ordinal)
                         || string.Equals(theme, "swamp", StringComparison.Ordinal)
                         || env.SeasonWetness >= HumidSeason;
            return near.Water && humid && env.Temperature >= FrogMinC ? Frogs : Crickets;
        }

        /// <summary>A poisoned world: contaminated animals, corrosive air (any chance of it), or acid falling right now.</summary>
        public static bool IsPoisoned(PlanetType planet, WorldEnvironment env)
            => planet.ContaminatedFauna
               || planet.CorrosiveAirChance > 0.0
               || string.Equals(env.Precipitation, "acid", StringComparison.Ordinal)
               || string.Equals(env.Weather, "acid_rain", StringComparison.Ordinal)
               || string.Equals(env.Weather, "toxic_storm", StringComparison.Ordinal);

        /// <summary>A scorched world or spot: ash flora, a lava-rich world, lava in reach, ash falling, or simply too hot.</summary>
        public static bool IsScorched(PlanetType planet, WorldEnvironment env, Surroundings near)
            => string.Equals(planet.FloraTheme, "ashen", StringComparison.Ordinal)
               || (planet.LavaAbundance is { } lava && lava >= VolcanicLava)
               || near.Lava
               || string.Equals(env.Precipitation, "ash", StringComparison.Ordinal)
               || env.Temperature >= ScorchedC;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The season look per profile and hemisphere (#3996): what the player sees from the server's season reading
    /// (<see cref="WorldEnvironment.Season"/>, <c>SeasonProgress</c>, <c>SeasonCycle</c>, <c>SeasonStrength</c>,
    /// <c>SeasonHemisphere</c> and the wet/dry curve <c>SeasonWetness</c>).
    /// <list type="bullet">
    /// <item><b>Four seasons</b>: <see cref="Target"/> as before.</item>
    /// <item><b>Summer and winter</b>: winter pales without autumn's amber, summer melts it over its first quarter.</item>
    /// <item><b>Wet and dry</b>: the dry season yellows the plants a little (a share of the autumn channel), the wet season
    /// lends a little bloom — both follow the wet/dry curve, so the turn is gradual.</item>
    /// <item><b>One season</b>: that season's full look all year.</item>
    /// <item><b>Hemispheres</b>: the season's look fades with its strength toward the tropics, where the wet/dry look takes
    /// over — continuous, so walking south never snaps the plants from winter to summer.</item>
    /// </list>
    /// Only the existing three shader channels are filled, so desktop High, Potato and the browser behave the same.
    /// </summary>
    public static partial class SeasonLook
    {
        /// <summary>Below this season strength a place on a planet with hemispheres counts as the tropics.</summary>
        public const float TropicsBelow = 0.5f;

        /// <summary>The progress that shows a season's full look (past every opening transition of <see cref="Target"/>).</summary>
        private const float FullLookProgress = 0.6f;

        /// <summary>Whether the player stands in the tropics of a planet with hemispheres.</summary>
        public static bool InTropics(WorldEnvironment env)
            => env != null && !string.IsNullOrEmpty(env.Season) && !string.IsNullOrEmpty(env.SeasonHemisphere)
               && env.SeasonStrength < TropicsBelow;

        /// <summary>The season the player is shown: the wet or dry season in the tropics, else the server's season (empty on
        /// a world without seasons).</summary>
        public static string DisplaySeason(WorldEnvironment env)
        {
            if (env == null || string.IsNullOrEmpty(env.Season))
            {
                return string.Empty;
            }

            return InTropics(env) ? WetOrDry(env.SeasonWetness) : env.Season;
        }

        /// <summary>The wet or the dry season by the wet/dry curve (0 dry … 1 wet).</summary>
        public static string WetOrDry(float wetness) => wetness >= 0.5f ? PlanetSeasons.Wet : PlanetSeasons.Dry;

        /// <summary>The shader targets (autumn, winter, bloom) for the player's season reading; an evergreen world
        /// (<c>PlanetType.EvergreenFlora</c>) keeps only the bloom.</summary>
        public static (float Autumn, float Winter, float Bloom) LookFor(WorldEnvironment env, bool evergreenFlora)
        {
            if (env == null || string.IsNullOrEmpty(env.Season))
            {
                return (0f, 0f, 0f);
            }

            var look = env.SeasonCycle switch
            {
                SeasonProfiles.OneCycle => Target(env.Season, FullLookProgress),
                SeasonProfiles.TwoCycle => SummerWinterTarget(env.Season, env.SeasonProgress),
                SeasonProfiles.RainCycle => WetDryTarget(env.SeasonWetness),
                _ => Target(env.Season, env.SeasonProgress),
            };
            if (!string.IsNullOrEmpty(env.SeasonHemisphere))
            {
                float s = Math.Max(0f, Math.Min(1f, env.SeasonStrength));
                var tropics = WetDryTarget(env.SeasonWetness);
                look = (Mix(tropics.Autumn, look.Autumn, s), Mix(tropics.Winter, look.Winter, s), Mix(tropics.Bloom, look.Bloom, s));
            }

            return evergreenFlora ? (0f, 0f, look.Bloom) : look;
        }

        /// <summary>The look of a summer-and-winter year: winter pales over its first third, summer melts it over its first
        /// quarter — no autumn amber in between.</summary>
        public static (float Autumn, float Winter, float Bloom) SummerWinterTarget(string? season, float progress)
        {
            float p = Math.Max(0f, Math.Min(1f, progress));
            return season switch
            {
                PlanetSeasons.Winter => (0f, Smooth(0f, 0.3f, p), 0f),
                PlanetSeasons.Summer => (0f, 1f - Smooth(0f, 0.25f, p), 0f),
                _ => Target(season, progress),
            };
        }

        /// <summary>The wet/dry look from the wet/dry curve: the drier, the more the plants yellow (up to 0.35 of the autumn
        /// channel), the wetter, the more bloom (up to 0.3).</summary>
        public static (float Autumn, float Winter, float Bloom) WetDryTarget(float wetness)
        {
            float dry = Math.Max(0f, Math.Min(1f, (0.5f - wetness) / 0.3f));
            float wet = Math.Max(0f, Math.Min(1f, (wetness - 0.5f) / 0.3f));
            return (0.35f * dry, 0f, 0.3f * wet);
        }

        /// <summary>The season's pull on the colour grade for the player's reading: the shown season's grade, faded with the
        /// season's strength outside the tropics.</summary>
        public static (float Warmth, float Saturation) GradeFor(WorldEnvironment env)
        {
            string season = DisplaySeason(env);
            var (warmth, saturation) = Grade(season);
            if (env == null || string.IsNullOrEmpty(env.SeasonHemisphere) || InTropics(env))
            {
                return (warmth, saturation);
            }

            float s = Math.Max(0f, Math.Min(1f, env.SeasonStrength));
            return (warmth * s, 1f + ((saturation - 1f) * s));
        }

        /// <summary>Whether a winter frost lies on the tops for the player's reading (never in the tropics).</summary>
        public static bool FrostFor(WorldEnvironment env, float temperatureC) => Frost(DisplaySeason(env), temperatureC);

        /// <summary>The locale keys of the HUD's season label: the season and, on a planet with hemispheres outside the
        /// tropics, the short hemisphere mark ("N"/"S"); both empty on a world without seasons.</summary>
        public static (string SeasonKey, string HemisphereKey) HudKeys(WorldEnvironment env)
        {
            string season = DisplaySeason(env);
            if (season.Length == 0)
            {
                return (string.Empty, string.Empty);
            }

            string hemisphere = env.SeasonHemisphere ?? string.Empty;
            return ("ui.season." + season, hemisphere.Length > 0 && !InTropics(env) ? "ui.season.hemi_short." + hemisphere : string.Empty);
        }

        /// <summary>The locale key of where the player's season holds, for the weather reading's long form: the tropics, the
        /// northern or the southern hemisphere; empty on a planet without hemispheres.</summary>
        public static string PlaceKey(WorldEnvironment env)
        {
            if (env == null || string.IsNullOrEmpty(env.Season) || string.IsNullOrEmpty(env.SeasonHemisphere))
            {
                return string.Empty;
            }

            return InTropics(env) ? "ui.season.tropics" : "ui.season.hemi." + env.SeasonHemisphere;
        }

        private static float Mix(float a, float b, float t) => a + ((b - a) * t);
    }
}

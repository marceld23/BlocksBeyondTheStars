// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When animals call (#3243). The voice genome fixed each species' calling rate, but it ignored the hour and the
    /// weather: a day bird called at midnight as often as at dawn, and a thunderstorm did not hush anyone. Here the
    /// species' own interval is multiplied by a factor:
    /// <list type="bullet">
    /// <item>the hour against the species' activity — day animals call most at dawn and dusk (a chorus), night animals at
    /// night, twilight animals in the twilight;</item>
    /// <item>the weather — rain hushes, a storm nearly silences; when a storm ends there is a short burst of calls;</item>
    /// <item>the herd — bigger groups answer each other more often.</item>
    /// </list>
    /// Unity-free and unit-tested; the client feeds it the sky's time of day.
    /// </summary>
    public sealed class CreatureCallRhythm
    {
        /// <summary>How long the after-storm burst lasts (seconds).</summary>
        public const float BurstSeconds = 25f;

        private bool _storm;
        private float _burstUntil = float.NegativeInfinity;

        /// <summary>The sun's height for a time of day (0..1, 0.25 = sunrise, 0.5 = noon), the sky's own curve.</summary>
        public static float SunHeight(float timeOfDay) => (float)Math.Sin((timeOfDay - 0.25f) * 2.0 * Math.PI);

        /// <summary>The factor on the species' calling interval (&lt; 1 = calls more often, &gt; 1 = less often).</summary>
        public static float CadenceFactor(string? activity, float sunHeight, bool rain, bool storm)
        {
            bool twilight = Math.Abs(sunHeight) < 0.2f;
            bool day = sunHeight >= 0.2f;
            float hour = (activity ?? "Diurnal") switch
            {
                "Nocturnal" => twilight ? 0.8f : (day ? 2.2f : 0.85f),
                "Crepuscular" => twilight ? 0.5f : 1.4f,
                "Cathemeral" => 1f,
                _ => twilight ? 0.55f : (day ? 1f : 2.2f),
            };

            float weather = storm ? 5f : (rain ? 2.5f : 1f);
            return hour * weather;
        }

        /// <summary>The chance that a call is answered by a herd member (0..1).</summary>
        public static float AnswerChance(int nearbyConspecifics) => Math.Min(0.4f, 0.12f + 0.05f * Math.Max(0, nearbyConspecifics));

        /// <summary>Feeds the weather once a frame; a storm that ends starts the burst.</summary>
        public void Observe(bool storm, float now)
        {
            if (_storm && !storm)
            {
                _burstUntil = now + BurstSeconds;
            }

            _storm = storm;
        }

        /// <summary>0.5 during the after-storm burst (twice as many calls), else 1.</summary>
        public float BurstFactor(float now) => now < _burstUntil ? 0.5f : 1f;
    }
}

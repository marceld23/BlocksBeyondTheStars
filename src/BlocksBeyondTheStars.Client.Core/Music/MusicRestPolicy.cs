// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>
    /// When the music takes a breath (#1173): after a track <em>ends</em> in a long-stay context the director
    /// may leave a rest window in which only the ambience beds play (wind, rain, the biome bed, a nearby
    /// waterfall), then the next track fades in. Continuous music makes a small library sound repetitive
    /// much faster than its size suggests; the rests are where most of the perceived variety comes from in
    /// games with sparse scores. Menu, loading, station, the UI contexts and the finale never rest — they
    /// are short stays or scripted.
    ///
    /// Pure policy (no UnityEngine); the Unity director rolls once per track end and runs the timer.
    /// </summary>
    public static class MusicRestPolicy
    {
        /// <summary>Probability that a rest follows a track that ended in <paramref name="context"/>.</summary>
        public static double RestChance(string context)
        {
            if (context == MusicLibrary.Space)
            {
                return 0.5;
            }

            if (context == MusicLibrary.ShipInterior)
            {
                return 0.3;
            }

            if (context == MusicLibrary.PlanetCave || context == MusicLibrary.PlanetDeep)
            {
                return 0.45;
            }

            return MusicLibrary.IsPlanet(context) ? 0.55 : 0.0;
        }

        /// <summary>Rest length bounds in seconds for <paramref name="context"/> (meaningless when
        /// <see cref="RestChance"/> is 0).</summary>
        public static (float Min, float Max) RestRange(string context)
        {
            if (context == MusicLibrary.Space)
            {
                return (60f, 150f);
            }

            if (context == MusicLibrary.ShipInterior)
            {
                return (45f, 120f);
            }

            return (60f, 180f);
        }

        /// <summary>Rolls a rest for a track that just ended in <paramref name="context"/>: 0 = no rest,
        /// otherwise the rest length in seconds (uniform within <see cref="RestRange"/>).</summary>
        public static float RollRest(string context, Random rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            double chance = RestChance(context);
            if (chance <= 0.0 || rng.NextDouble() >= chance)
            {
                return 0f;
            }

            var (min, max) = RestRange(context);
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}

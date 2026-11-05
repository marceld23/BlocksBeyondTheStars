// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Stylised vacuum audio (#3241, maintainer decision: stylised, not silent). In vacuum the player hears the world
    /// "through the suit": the wind and weather beds stop, a soft suit room tone plays, sounds out there arrive muffled and
    /// quieter, and what the player touches (their own steps, the drill, the mining target) stays clear but a little duller.
    /// UI, VEGA and the music are untouched. Before this an asteroid had a wind bed and an airless lift-off a sonic boom.
    /// Unity-free so the rules are unit-tested.
    /// </summary>
    public static class VacuumAudio
    {
        /// <summary>A one-shot within this distance of the listener counts as "touched" — carried through the body.</summary>
        public const float ContactDistance = 3.5f;

        /// <summary>The level of a touched sound in vacuum (clear, only a little duller).</summary>
        public const float ContactLevel = 0.8f;

        /// <summary>The level of a sound out there in vacuum, on top of the baked muffle.</summary>
        public const float RemoteLevel = 0.45f;

        /// <summary>The suit room tone's share of the bed level.</summary>
        public const float SuitToneLevel = 0.35f;

        /// <summary>True when the player is in vacuum: on a spacewalk, on foot in space, or on the surface of a body without
        /// air. Inside a cabin, a station deck or the ship there is air again.</summary>
        public static bool InVacuum(bool eva, bool onFootInSpace, bool airlessWorld, bool aboard, bool onStation)
        {
            if (aboard || onStation)
            {
                return false;
            }

            return eva || onFootInSpace || airlessWorld;
        }

        /// <summary>A world counts as airless by its own data — the night layer's test (#2825): an airless body, a space
        /// sky, or no atmosphere density at all.</summary>
        public static bool IsAirless(bool planetAirless, bool spaceSky, float atmosphereDensity)
            => planetAirless || spaceSky || atmosphereDensity <= 0f;

        /// <summary>How a 3D one-shot plays in vacuum: (muffled, level factor).</summary>
        public static (bool Muffle, float Level) Shape(float distanceToListener)
            => distanceToListener <= ContactDistance ? (false, ContactLevel) : (true, RemoteLevel);

        /// <summary>Cues that need air and never play in vacuum (a sonic boom, wind).</summary>
        public static bool NeedsAir(string? id)
        {
            string k = id ?? string.Empty;
            return k == "sonic_boom" || k.StartsWith("wind_", StringComparison.Ordinal) || k == "cloud_rush"
                || k == "amb_wind_high" || k == "glider_wind";
        }
    }
}

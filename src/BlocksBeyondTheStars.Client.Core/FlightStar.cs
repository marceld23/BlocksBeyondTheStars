// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3075: the colour of the star the flight view draws. It used to be the environment's sun colour — but that belongs to
    /// the world the server last activated, and a hyperjump in flight sends no new environment, so every system showed the
    /// star of the planet the pilot last stood on. The star map names the system the flight is in (the one holding
    /// <see cref="StarMapData.ActiveLocationId"/>) and carries its real colour (#1604, the same key as the planet sky).
    /// </summary>
    public static class FlightStar
    {
        /// <summary>The packed RGB of the star of the system the flight is in; <paramref name="fallback"/> (the environment's
        /// sun colour) when the map does not know the active body or carries no star colour (an older server).</summary>
        public static int ColorOf(StarMapData? map, int fallback)
        {
            var system = SystemOf(map, map?.ActiveLocationId);
            return system != null && system.StarColor != 0 ? system.StarColor : fallback;
        }

        /// <summary>The system holding the body <paramref name="bodyId"/>, or null.</summary>
        public static NetStarSystem? SystemOf(StarMapData? map, string? bodyId)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var system in map.Systems)
            {
                if (system?.Bodies == null)
                {
                    continue;
                }

                foreach (var body in system.Bodies)
                {
                    if (body != null && body.Id == bodyId)
                    {
                        return system;
                    }
                }
            }

            return null;
        }
    }
}

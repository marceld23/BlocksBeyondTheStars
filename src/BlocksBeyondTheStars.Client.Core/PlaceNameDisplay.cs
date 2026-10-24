// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2952: how the client shows a star system, planet or moon the player may have named for themselves. The server
    /// sends the player's own name as <see cref="NetBody.CustomName"/> / <see cref="NetStarSystem.CustomName"/> next to
    /// the generated <c>Name</c>. Labels read <see cref="Shown(NetBody)"/>; every key and seed (flora looks, orbit looks,
    /// cloud shells, the location name the world's colours hash) keeps reading the generated <c>Name</c>.
    /// </summary>
    public static class PlaceNameDisplay
    {
        /// <summary>The body's name as this player reads it: their own name, else the generated one.</summary>
        public static string Shown(this NetBody? body)
            => body == null ? string.Empty : string.IsNullOrEmpty(body.CustomName) ? body.Name : body.CustomName;

        /// <summary>The system's name as this player reads it: their own name, else the generated one.</summary>
        public static string Shown(this NetStarSystem? system)
            => system == null ? string.Empty : string.IsNullOrEmpty(system.CustomName) ? system.Name : system.CustomName;

        /// <summary>Whether the player gave the body a name of their own.</summary>
        public static bool IsRenamed(this NetBody? body) => body != null && !string.IsNullOrEmpty(body.CustomName);

        /// <summary>Whether the player gave the system a name of their own.</summary>
        public static bool IsRenamed(this NetStarSystem? system) => system != null && !string.IsNullOrEmpty(system.CustomName);

        /// <summary>
        /// The location line ("System · Body", as the join and the world reset compose it) with the player's own names for
        /// the body they are on and its system; <paramref name="original"/> holds the generated names of just the renamed
        /// parts ("Kraegre", or "Gheyee · Kraegre" when both were renamed), else empty. Only when <paramref name="map"/>
        /// still describes that location (its active body's generated line equals <paramref name="locationName"/>) — right
        /// after a world change the map may still be the old one, and the line stays the generated one until the new
        /// map arrives.
        /// </summary>
        public static string ShownLocation(StarMapData? map, string locationName, out string original)
        {
            original = string.Empty;
            if (map?.Systems == null || string.IsNullOrEmpty(locationName) || string.IsNullOrEmpty(map.ActiveLocationId))
            {
                return locationName ?? string.Empty;
            }

            foreach (var system in map.Systems)
            {
                if (system?.Bodies == null)
                {
                    continue;
                }

                foreach (var body in system.Bodies)
                {
                    if (body == null || body.Id != map.ActiveLocationId)
                    {
                        continue;
                    }

                    if (!body.IsRenamed() && !system.IsRenamed())
                    {
                        return locationName;
                    }

                    if (locationName == body.Name)
                    {
                        original = body.IsRenamed() ? body.Name : string.Empty;
                        return body.Shown();
                    }

                    if (locationName == system.Name + " · " + body.Name)
                    {
                        original = system.IsRenamed() && body.IsRenamed() ? locationName
                            : system.IsRenamed() ? system.Name : body.Name;
                        return system.Shown() + " · " + body.Shown();
                    }

                    return locationName;
                }
            }

            return locationName;
        }

        /// <summary>The name a body arriving by a world change is shown with: the player's own name for the body with this
        /// generated <paramref name="bodyName"/> in the system named <paramref name="systemName"/>, else the generated
        /// name. The world reset carries names only, so the last star map is searched by them.</summary>
        public static string ShownBodyName(StarMapData? map, string systemName, string bodyName)
        {
            foreach (var system in map?.Systems ?? System.Array.Empty<NetStarSystem>())
            {
                if (system?.Bodies == null || (!string.IsNullOrEmpty(systemName) && system.Name != systemName))
                {
                    continue;
                }

                foreach (var body in system.Bodies)
                {
                    if (body != null && body.Name == bodyName)
                    {
                        return body.Shown();
                    }
                }
            }

            return bodyName ?? string.Empty;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2714: the words and the lookup behind a held wish's village — the place a villager's wish is handed over. The server
    /// marks the village on the player's marker list (<see cref="NetMarker.Wish"/>, labelled with the village's name, while a
    /// wish from there is open and the player is on its body) and names it on the wish (<see cref="NetMission.PlaceName"/>,
    /// <see cref="NetMission.PlaceMarkerId"/>). The planet map labels the marker "Wish · Testville"; the Missions tab says
    /// "Village: Testville · 120 m" and offers "Navigate" while the marker is on this body. Formatting never throws: a
    /// translation without its slot gets the value appended. Unity-free, unit-tested headless.
    /// </summary>
    public static class WishPlaceText
    {
        /// <summary>The map label of a wish village ("Wish · {0}", the village's name).</summary>
        public const string MapKey = "ui.map.wish_target";

        /// <summary>The Missions tab's place line without a distance ("Village: {0}").</summary>
        public const string PlaceKey = "ui.missions.wish_place";

        /// <summary>The Missions tab's place line with the distance ("Village: {0} · {1} m").</summary>
        public const string PlaceDistanceKey = "ui.missions.wish_place_dist";

        /// <summary>Whether a marker is a held wish's village (an older server never sets the flag).</summary>
        public static bool IsWish(NetMarker? marker) => marker is { Wish: true };

        /// <summary>The map label of a wish village: "Wish · Testville".</summary>
        public static string MapLabel(NetMarker marker, Func<string, string> localize)
            => Fill(localize(MapKey), "{0}", marker.Label ?? string.Empty);

        /// <summary>The village marker of a mission in the player's marker list — present only while the player is on the
        /// village's body; null for any other mission, an older server, or another body.</summary>
        public static NetMarker? MarkerFor(NetMission? mission, NetMarker[]? markers)
        {
            if (mission is null || string.IsNullOrEmpty(mission.PlaceMarkerId) || markers is null)
            {
                return null;
            }

            foreach (var m in markers)
            {
                if (m is { Wish: true } && string.Equals(m.Id, mission.PlaceMarkerId, StringComparison.Ordinal))
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>The Missions tab's place line: "Village: Testville · 120 m", or without a distance
        /// (<paramref name="metres"/> &lt; 0 — the village lies on another body) "Village: Testville". Empty for a mission
        /// without a place.</summary>
        public static string MissionLine(NetMission? mission, int metres, Func<string, string> localize)
        {
            string place = mission?.PlaceName ?? string.Empty;
            if (place.Length == 0)
            {
                return string.Empty;
            }

            if (metres < 0)
            {
                return Fill(localize(PlaceKey), "{0}", place);
            }

            // The distance first: a brace in the village's name is never read as a slot.
            string withDistance = Fill(localize(PlaceDistanceKey), "{1}", metres.ToString(CultureInfo.InvariantCulture));
            return Fill(withDistance, "{0}", place);
        }

        /// <summary>The template with <paramref name="slot"/> replaced (no <c>string.Format</c>: a stray brace in a translation
        /// or a village name must not throw); a template without the slot gets the value appended.</summary>
        private static string Fill(string? template, string slot, string value)
        {
            string t = template ?? string.Empty;
            return t.Contains(slot) ? t.Replace(slot, value) : (t + " " + value).Trim();
        }
    }
}

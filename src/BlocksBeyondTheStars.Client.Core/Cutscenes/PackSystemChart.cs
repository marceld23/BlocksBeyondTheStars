// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// The <c>system:&lt;id&gt;</c> chart anchor (#3357, the Sounders' Ornemis): which star the chart rings and routes to. The
    /// server sends a pack system on a player's star map only while they may see it, so "on the map" is the test — without it
    /// the scene plays as a card (<see cref="CutsceneRules.NeedsObject"/>). Unity-free, unit-tested.
    /// </summary>
    public static class PackSystemChart
    {
        /// <summary>The system a chart shot's anchor points at, when it is on this client's star map; else null.</summary>
        public static string? TargetOf(string? anchor, StarMapData? map)
            => CutsceneAnchors.SystemIdOf(anchor) is { } id && HasSystem(map, id) ? id : null;

        /// <summary>Whether the star map holds the system.</summary>
        public static bool HasSystem(StarMapData? map, string? systemId)
        {
            if (map?.Systems is null || string.IsNullOrEmpty(systemId))
            {
                return false;
            }

            foreach (var sys in map.Systems)
            {
                if (sys is not null && string.Equals(sys.Id, systemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

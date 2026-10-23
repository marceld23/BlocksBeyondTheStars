// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's crafting-station affordances (#2912): which stations the server says are usable right now, and
    /// the station lookups by position and by view.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        // --- Station affordances (#1070/#1072): the SERVER says which stations are usable right now. ---

        /// <summary>Lower-case CraftingStation names usable now ("workshop", "refinery", …); hand is implicit.
        /// Replaces the old guess from ship station markers, which never saw a base workbench.</summary>
        public HashSet<string> StationsAvailable { get; } = new HashSet<string>();

        /// <summary>Research (Tech tab) is possible right now — at the cockpit (#1074).</summary>
        public bool ResearchOk { get; private set; }

        /// <summary>Ship modules can be built right now — aboard, with a workshop module (#1074).</summary>
        public bool ShipBuildOk { get; private set; }

        /// <summary>False until the first StationsInReach arrived — a pre-#1070 host never sends one, and the menu
        /// then falls back to its old ship-marker heuristic instead of dimming everything.</summary>
        public bool StationsKnown { get; private set; }

        /// <summary>Raised after every StationsInReach update (the menu re-evaluates its gates).</summary>
        public event System.Action StationsInReachChanged;

        /// <summary>The last "where is the nearest …?" answer (#1072); null before the first one.</summary>
        public StationLocation LastStationLocation { get; private set; }

        public event System.Action<StationLocation> StationLocated;

        /// <summary>Type of the nearest station within <paramref name="range"/> blocks, or empty.</summary>
        public string NearestStationType(Vector3 pos, float range)
        {
            string best = string.Empty;
            float bestSq = range * range;
            foreach (var s in Stations)
            {
                float dx = (float)WorldConstants.WrapDeltaX(s.X - pos.x, Circumference), dy = s.Y - pos.y; // both ground axes wrap (torus)
                float dz = (float)WorldConstants.WrapDeltaZ(s.Z - pos.z, Circumference);
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Type;
                }
            }

            return best;
        }

        /// <summary>The station the player is looking AT: raycast forward and return the station nearest the
        /// hit point (so a cramped ship's many stations don't all read as the one you happen to stand on).
        /// Empty if the aim doesn't land on a station. Used in preference to <see cref="NearestStationType"/>.</summary>
        public string LookedStationType(Camera cam, float range)
        {
            if (cam == null || Stations.Length == 0
                || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, range))
            {
                return string.Empty;
            }

            string best = string.Empty;
            float bestSq = 1.6f * 1.6f; // the hit must land on (or right next to) a station tile
            foreach (var s in Stations)
            {
                float dx = (float)WorldConstants.WrapDeltaX(s.X - hit.point.x, Circumference), dy = s.Y - hit.point.y;
                float dz = (float)WorldConstants.WrapDeltaZ(s.Z - hit.point.z, Circumference);
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Type;
                }
            }

            return best;
        }
    }
}

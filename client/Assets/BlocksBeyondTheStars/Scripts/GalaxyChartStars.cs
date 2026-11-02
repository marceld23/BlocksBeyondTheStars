// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the hyperspace chart draws, for every host of <see cref="GalaxyChartWidget"/> — the flight chart's Hyperspace
    /// tab and the travel screen's Galaxy view (#3130) — so both read the galaxy the same way: the name gated by the #1113
    /// rule (a bare "?" for a system never entered, unless a radar array is aboard), the player's own name for it (#2952),
    /// the frontier tag, the server's star colour (#1604) with a warm-yellow fallback, the party's whereabouts, the
    /// finale accent, the relay lanes (#1125) and the wormhole pairs the player knows (#2242).
    /// </summary>
    public static class GalaxyChartStars
    {
        /// <summary>The finale system's id (#1605) — mirrors the server's reserved id; the chart gives it the
        /// hyperspace-violet accent once the story has revealed it.</summary>
        public const string FinaleSystemId = "guardian_finale";

        private static readonly Color StarFallbackCol = new Color(1f, 0.94f, 0.74f); // an older server sends no star colour

        /// <summary>Every system of the star map as the chart should draw it; the relay lanes come out alongside.</summary>
        public static List<GalaxyChartWidget.Star> Build(GameBootstrap game, string currentSystemId, bool radar,
            System.Func<string, string> localize, out List<(string A, string B)> lanes)
        {
            var stars = new List<GalaxyChartWidget.Star>();
            lanes = new List<(string, string)>();
            var map = game?.StarMap;
            if (map?.Systems == null)
            {
                return stars;
            }

            string frontier = localize("ui.map.frontier");
            var playersBySystem = PlayersBySystem(game, map);
            foreach (var sys in map.Systems)
            {
                bool current = sys.Id == currentSystemId;
                bool known = game.KnowsSystem(sys.Id);
                string label = GalaxyChartLayout.DisplayName(sys.Shown(), known, current, radar, "?"); // #2952: own name
                if (sys.Tier >= 2 && label != "?")
                {
                    label = $"{label} · {frontier}";
                }

                stars.Add(new GalaxyChartWidget.Star
                {
                    Id = sys.Id,
                    MapX = sys.MapX,
                    MapY = sys.MapY,
                    Label = label,
                    Color = sys.StarColor != 0 ? Rgb(sys.StarColor) : StarFallbackCol,
                    Current = current,
                    Known = known,
                    Finale = sys.Id == FinaleSystemId,
                    Players = playersBySystem.TryGetValue(sys.Id, out var there) ? string.Join(", ", there) : string.Empty,
                });
            }

            var net = game.RelayNetwork;
            if (net?.LaneSystemA != null && net.LaneSystemB != null)
            {
                int n = Mathf.Min(net.LaneSystemA.Length, net.LaneSystemB.Length);
                for (int i = 0; i < n; i++)
                {
                    lanes.Add((net.LaneSystemA[i], net.LaneSystemB[i]));
                }
            }

            return stars;
        }

        /// <summary>Other players by system, so a star can carry "Anna, Ben" above it (the current star's ring already
        /// says where I am).</summary>
        private static Dictionary<string, List<string>> PlayersBySystem(GameBootstrap game, StarMapData map)
        {
            var playersBySystem = new Dictionary<string, List<string>>();
            if (map.Players == null)
            {
                return playersBySystem;
            }

            foreach (var p in map.Players)
            {
                if (p.Name == game.PlayerName)
                {
                    continue;
                }

                var sys = SystemOfBody(map, p.LocationId);
                if (sys == null)
                {
                    continue;
                }

                if (!playersBySystem.TryGetValue(sys.Id, out var names))
                {
                    playersBySystem[sys.Id] = names = new List<string>();
                }

                names.Add(p.Name);
            }

            return playersBySystem;
        }

        /// <summary>#2242: the wormhole pairs this player knows (both system ids known), each once.</summary>
        public static List<(string A, string B)> KnownWormholePairs(GameBootstrap game)
        {
            var pairs = new List<(string A, string B)>();
            var list = game?.StarMap?.Wormholes;
            if (list == null)
            {
                return pairs;
            }

            var seen = new HashSet<string>();
            foreach (var w in list)
            {
                if (string.IsNullOrEmpty(w.LinkedSystemId))
                {
                    continue; // not known yet
                }

                bool ordered = string.CompareOrdinal(w.SystemId, w.LinkedSystemId) <= 0;
                var pair = ordered ? (w.SystemId, w.LinkedSystemId) : (w.LinkedSystemId, w.SystemId);
                if (seen.Add(pair.Item1 + "|" + pair.Item2)) // the twin end lists the same pair
                {
                    pairs.Add(pair);
                }
            }

            return pairs;
        }

        /// <summary>The star system a body lives in, or null.</summary>
        public static NetStarSystem SystemOfBody(StarMapData map, string bodyId)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == bodyId)
                    {
                        return sys;
                    }
                }
            }

            return null;
        }

        /// <summary>A system by id, or null.</summary>
        public static NetStarSystem FindSystem(StarMapData map, string id)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                if (sys.Id == id)
                {
                    return sys;
                }
            }

            return null;
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}

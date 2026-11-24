// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3979: the planet maps on demand. In the browser the maps (<c>data/maps</c>) are not part of the eager data download;
    /// <see cref="For"/> hands every generator site the process's <see cref="PlanetMapDemand"/>, which fetches a missing
    /// map here through <see cref="StreamingAssetsCache.FetchOnDemand"/> (<c>UnityWebRequest</c>) and registers it with the
    /// content. Where StreamingAssets is a folder on disk (desktop, the Editor) the demand fetches nothing: the content
    /// reads the map file itself on first use, as before.
    /// </summary>
    public static class PlanetMapFetcher
    {
        private static PlanetMapDemand _demand;

        /// <summary>The demand of <paramref name="content"/> (the process has one content; a reloaded content gets a fresh
        /// demand), or null without content.</summary>
        public static PlanetMapDemand For(GameContent content)
        {
            if (content == null)
            {
                return null;
            }

            if (_demand == null || !ReferenceEquals(_demand.Content, content))
            {
                Action<string> fetch = null;
                if (StreamingAssetsCache.UsesRemoteStreamingAssets)
                {
                    PlanetMapDemand demand = null;
                    fetch = key => Fetch(demand, key);
                    demand = new PlanetMapDemand(content, fetch);
                    _demand = demand;
                }
                else
                {
                    _demand = new PlanetMapDemand(content, fetch);
                }
            }

            return _demand;
        }

        private static void Fetch(PlanetMapDemand demand, string key)
        {
            Debug.Log($"[PlanetMap] Fetching the map '{key}' on demand.");
            StreamingAssetsCache.FetchOnDemand(PlanetMapDemand.DataPathOf(key), bytes =>
            {
                if (demand.Arrived(key, bytes, out string error))
                {
                    Debug.Log($"[PlanetMap] Map '{key}' arrived ({bytes.Length / 1024} KB).");
                }
                else
                {
                    Debug.LogWarning($"[PlanetMap] Map '{key}' arrived damaged: {error}");
                }
            }, problem =>
            {
                Debug.LogWarning($"[PlanetMap] {problem}");
                demand.Failed(key);
            });
        }
    }
}

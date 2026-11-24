// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3979: which planet maps (<c>data/maps/&lt;key&gt;.bbmap</c>) a client still has to fetch before a map world may
    /// generate. The browser does not download the maps with the rest of the data (a few megabytes nobody needs before the
    /// Sol system appears): every place that builds a generator asks <see cref="IsReady"/> first, which starts the fetch
    /// of a missing map once and answers false until it arrived — the far terrain, the biome looks and the orbit and
    /// landing-map bakes wait meanwhile (the bakes show their flat colour), and the in-process browser server does not
    /// tick. A client whose maps come from disk (desktop) is built without a fetch: every map is ready, read lazily by the
    /// content as before.
    /// <para>Unity-free so it is tested headless; the download itself (<c>UnityWebRequest</c>) lives in the client's
    /// <c>PlanetMapFetcher</c>, which calls <see cref="Arrived"/> or <see cref="Failed"/>. Main thread only.</para>
    /// </summary>
    public sealed class PlanetMapDemand
    {
        /// <summary>How often one map is tried before the demand gives up on it for the session (the world that needs it
        /// then stays waiting; a reload tries again).</summary>
        public const int MaxAttempts = 3;

        private readonly GameContent _content;
        private readonly Action<string>? _fetch;
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <param name="content">The content the maps are registered with.</param>
        /// <param name="fetch">Starts the download of one map key; it must end in <see cref="Arrived"/> or
        /// <see cref="Failed"/>. Null: the content's own source has every map (the files on disk).</param>
        public PlanetMapDemand(GameContent content, Action<string>? fetch)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _fetch = fetch;
        }

        /// <summary>The content this demand serves.</summary>
        public GameContent Content => _content;

        /// <summary>Whether maps are fetched on demand (the browser) rather than read from disk.</summary>
        public bool FetchesOnDemand => _fetch != null;

        /// <summary>Counts the fetches that ended (arrived, damaged or failed) — a view that waits for a map compares it
        /// with the value it saw last and asks again when it moved.</summary>
        public int Version { get; private set; }

        /// <summary>Whether a data file is left out of the eager browser download (its manifest, its fallback list):
        /// everything under <c>maps/</c>.</summary>
        public static bool IsOnDemandDataFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            string path = relativePath!.Replace('\\', '/').TrimStart('/');
            return path.StartsWith(PlanetMapCodec.DataFolder + "/", StringComparison.Ordinal);
        }

        /// <summary>The data-relative path of a map key (<c>maps/earth.bbmap</c>).</summary>
        public static string DataPathOf(string key) => PlanetMapCodec.DataFolder + "/" + key + PlanetMapCodec.Extension;

        /// <summary>The map key a planet type takes its terrain from, or null (no such type, procedural terrain, or a key
        /// that is no valid map key).</summary>
        public static string? MapKeyOf(GameContent content, string? planetTypeKey)
        {
            if (content is null || string.IsNullOrEmpty(planetTypeKey) || content.GetPlanet(planetTypeKey!) is not { } planet
                || !planet.HasTerrainMap || !GameContent.IsPlanetMapKey(planet.TerrainMap))
            {
                return null;
            }

            return planet.TerrainMap;
        }

        /// <summary>True when a world of <paramref name="planetTypeKey"/> may generate now: the type needs no map, the maps
        /// come from disk, or its map is registered. Otherwise starts the map's fetch (once) and answers false.</summary>
        public bool IsReady(string? planetTypeKey)
        {
            string? key = MapKeyOf(_content, planetTypeKey);
            return key is null || IsMapReady(key);
        }

        /// <summary>Asks for the maps of all these planet types (the bodies the client just learned of), so they are
        /// there by the time a world of one of them generates. Returns how many of them are still on their way (a map that
        /// failed <see cref="MaxAttempts"/> times is no longer coming and does not count).</summary>
        public int Need(IEnumerable<string?> planetTypeKeys)
        {
            var coming = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? type in planetTypeKeys ?? Array.Empty<string?>())
            {
                if (!IsReady(type) && MapKeyOf(_content, type) is { } key && _inFlight.Contains(key))
                {
                    coming.Add(key);
                }
            }

            return coming.Count;
        }

        /// <summary>The planet types of every body on a star map (the systems the server sent, bodies of unknown systems
        /// included when it sent them) — what <see cref="Need"/> asks for when the map arrives.</summary>
        public static IEnumerable<string?> PlanetTypesOf(StarMapData? starMap)
        {
            foreach (var system in starMap?.Systems ?? Array.Empty<NetStarSystem>())
            {
                foreach (var body in system?.Bodies ?? Array.Empty<NetBody>())
                {
                    yield return body?.PlanetType;
                }
            }
        }

        /// <summary>The planet types of every body of a galaxy — the in-process browser server's whole universe.</summary>
        public static IEnumerable<string?> PlanetTypesOf(Galaxy? galaxy)
        {
            if (galaxy is null)
            {
                yield break;
            }

            foreach (var body in galaxy.AllBodies())
            {
                yield return body.PlanetType;
            }
        }

        /// <summary>Whether the map of <paramref name="key"/> is here; starts its fetch when it is not and none runs.</summary>
        public bool IsMapReady(string key)
        {
            if (_fetch is null || _inFlight.Contains(key))
            {
                return _fetch is null;
            }

            if (HasMap(key))
            {
                return true;
            }

            _attempts.TryGetValue(key, out int attempts);
            if (attempts >= MaxAttempts)
            {
                return false;
            }

            _attempts[key] = attempts + 1;
            _inFlight.Add(key);
            _fetch(key);
            return false;
        }

        /// <summary>The bytes of a fetched map: registered with the content (decoded and checked now). False when the file
        /// is damaged (<paramref name="error"/> says why) — the next <see cref="IsReady"/> fetches it again.</summary>
        public bool Arrived(string key, byte[] bytes, out string? error)
        {
            _inFlight.Remove(key);
            Version++;
            try
            {
                _content.RegisterPlanetMap(key, bytes);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                error = ex.Message;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>A fetch of <paramref name="key"/> failed: the next <see cref="IsReady"/> tries again, up to
        /// <see cref="MaxAttempts"/> in all.</summary>
        public void Failed(string key)
        {
            _inFlight.Remove(key);
            Version++;
        }

        /// <summary>Whether the content already has the map — registered, or readable from its source (a copy the browser
        /// kept from an earlier session). A damaged copy counts as missing, so it is fetched again.</summary>
        private bool HasMap(string key)
        {
            try
            {
                return _content.GetPlanetMap(key) != null;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}

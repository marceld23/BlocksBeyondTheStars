// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet map previews (#2172): an equirect map (full circumference × full latitude band) of a body's REAL
    /// generated world, baked by <see cref="PlanetMapBakeJob"/> — the generator ships with the client and is
    /// deterministic, so the map is the terrain you will land on. Used as the orbit sphere texture, by the bodies in
    /// the surface sky and by the landing-pad map; the per-pixel terrain facts it keeps (height, biome, temperature)
    /// are what the weather layers project onto.
    /// <para><b>Never on the main thread in one go.</b> A whole system took seconds on Mono when every body baked
    /// synchronously. On desktop each bake runs on a worker thread; in the browser, where managed threads do not run,
    /// it is time-sliced into rows under a per-frame budget. Callers <see cref="Request"/> a map and get a callback
    /// once it exists; until then they show the flat data-driven colour. Cached per body + size, textures destroyed on
    /// world exit (#966).</para>
    /// </summary>
    public static class WorldMinimap
    {
        private sealed class Entry
        {
            public PlanetMapRequest Request = null!;
            public PlanetMapBakeJob Job = null!;
            public System.Threading.Tasks.Task Task;
            public Texture2D Texture;
            public PlanetMapData Data;
            public bool Failed;
            public int Priority;
            public readonly List<Action<Texture2D, PlanetMapData>> Waiters = new List<Action<Texture2D, PlanetMapData>>();
        }

        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();
        private static readonly List<Entry> _pending = new List<Entry>();

        /// <summary>Worker bakes running at once on desktop.</summary>
        private const int MaxConcurrentBakes = 2;

        /// <summary>Main-thread budget per frame for time-sliced bakes (browser).</summary>
        private const double SliceBudgetMs = 4.0;

#if UNITY_WEBGL && !UNITY_EDITOR
        private static readonly bool Threaded = false;
#else
        private static readonly bool Threaded = true;
#endif

        /// <summary>Builds a bake request on the main thread: reads the ground colours this planet type can show from
        /// the block atlas (a Unity texture — the worker must not touch it).</summary>
        public static PlanetMapRequest MakeRequest(GameContent content, BlockTextureAtlas atlas, long worldSeed,
            string floraKey, string planetTypeKey, int circumference, int texW, int texH,
            string bodyId, bool continents, int generation, bool cratered, bool lavaCoreVolcanoes)
        {
            var colors = new Dictionary<ushort, int>();
            if (content != null && atlas != null)
            {
                foreach (ushort id in PlanetMapBakeJob.GroundBlockIds(content, planetTypeKey))
                {
                    var c = atlas.AverageColor(id);
                    colors[id] = (Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255) << 16)
                        | (Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255) << 8)
                        | Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                }
            }

            return new PlanetMapRequest
            {
                Content = content,
                WorldSeed = worldSeed,
                BodyId = bodyId ?? string.Empty,
                FloraKey = floraKey ?? string.Empty,
                PlanetTypeKey = planetTypeKey ?? string.Empty,
                Circumference = circumference,
                Width = texW,
                Height = texH,
                Continents = continents,
                Generation = generation,
                Cratered = cratered,
                LavaCoreVolcanoes = lavaCoreVolcanoes,
                GroundColors = colors,
            };
        }

        /// <summary>The finished map of a request, if it has been baked.</summary>
        public static bool TryGet(PlanetMapRequest request, out Texture2D texture, out PlanetMapData data)
        {
            if (request != null && _cache.TryGetValue(request.Key, out var e) && e.Texture != null)
            {
                texture = e.Texture;
                data = e.Data;
                return true;
            }

            texture = null;
            data = null;
            return false;
        }

        /// <summary>Asks for a map: <paramref name="onReady"/> runs on the main thread once it exists (at once when it
        /// is cached). <paramref name="priority"/> orders the queue (higher first — the planet below you, the landing
        /// map). A failed bake never calls back; the caller keeps its flat look.</summary>
        public static void Request(PlanetMapRequest request, Action<Texture2D, PlanetMapData> onReady, int priority = 0)
        {
            if (request == null)
            {
                return;
            }

            if (_cache.TryGetValue(request.Key, out var e))
            {
                if (e.Texture != null)
                {
                    onReady?.Invoke(e.Texture, e.Data);
                    return;
                }

                if (onReady != null && !e.Failed)
                {
                    e.Waiters.Add(onReady);
                }

                e.Priority = Math.Max(e.Priority, priority);
                return;
            }

            e = new Entry { Request = request, Job = new PlanetMapBakeJob(request), Priority = priority };
            if (onReady != null)
            {
                e.Waiters.Add(onReady);
            }

            _cache[request.Key] = e;
            _pending.Add(e);
        }

        /// <summary>Drives the queue — call once per frame (GameBootstrap does). Starts worker bakes on desktop,
        /// time-slices them in the browser, and turns finished maps into textures.</summary>
        public static void Pump()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            _pending.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            if (Threaded)
            {
                int running = 0;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var e = _pending[i];
                    if (e.Task == null)
                    {
                        continue;
                    }

                    if (!e.Task.IsCompleted)
                    {
                        running++;
                        continue;
                    }

                    _pending.RemoveAt(i);
                    if (e.Task.IsFaulted)
                    {
                        Fail(e, e.Task.Exception?.GetBaseException());
                    }
                    else
                    {
                        Finish(e);
                    }
                }

                for (int i = 0; i < _pending.Count && running < MaxConcurrentBakes; i++)
                {
                    var e = _pending[i];
                    if (e.Task == null)
                    {
                        var job = e.Job;
                        e.Task = System.Threading.Tasks.Task.Run(() => job.Run());
                        running++;
                    }
                }

                return;
            }

            // Browser: bake rows of the most urgent map until the frame's budget is spent.
            var sw = Stopwatch.StartNew();
            while (_pending.Count > 0 && sw.Elapsed.TotalMilliseconds < SliceBudgetMs)
            {
                var e = _pending[0];
                bool done;
                try
                {
                    done = e.Job.Step(4);
                }
                catch (Exception ex)
                {
                    _pending.RemoveAt(0);
                    Fail(e, ex);
                    continue;
                }

                if (done)
                {
                    _pending.RemoveAt(0);
                    Finish(e);
                }
            }
        }

        private static void Finish(Entry e)
        {
            if (!_cache.TryGetValue(e.Request.Key, out var live) || !ReferenceEquals(live, e))
            {
                return; // the cache was cleared (world exit) while the bake ran
            }

            var data = e.Job.Data;
            var tex = new Texture2D(data.Width, data.Height, TextureFormat.RGBA32, mipChain: true)
            {
                wrapMode = TextureWrapMode.Repeat, // longitude wraps; the sphere/strips tile seamlessly
                filterMode = FilterMode.Bilinear,
                name = "PlanetMap_" + e.Request.BodyId,
            };
            tex.SetPixelData(data.Rgba, 0);
            tex.Apply(true);
            e.Texture = tex;
            e.Data = data;
            var waiters = e.Waiters.ToArray();
            e.Waiters.Clear();
            foreach (var w in waiters)
            {
                try
                {
                    w(tex, data);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private static void Fail(Entry e, Exception ex)
        {
            e.Failed = true;
            e.Waiters.Clear();
            Debug.LogWarning($"[PlanetMap] Bake of {e.Request.BodyId} ({e.Request.PlanetTypeKey}) failed: {ex?.Message}");
        }

        /// <summary>Number of baked previews currently held (diagnostics).</summary>
        public static int CachedCount => _cache.Count;

        /// <summary>Destroys every cached preview and empties the cache (#966). The cache is static, so it used to
        /// outlive the world that filled it, and an unreferenced Texture2D is not garbage-collected by Unity — it has to
        /// be destroyed explicitly. A bake still running finishes into the void (its entry is gone).</summary>
        public static void ClearCache()
        {
            foreach (var e in _cache.Values)
            {
                if (e.Texture != null)
                {
                    Object.Destroy(e.Texture);
                }
            }

            _cache.Clear();
            _pending.Clear();
        }
    }
}

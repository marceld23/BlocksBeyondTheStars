// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Resolves bundled StreamingAssets content for native and WebGL builds.
    /// WebGL exposes StreamingAssets through HTTP, while the shared content loader expects a filesystem tree.
    /// </summary>
    public static class StreamingAssetsCache
    {
        private const string DataFolder = "data";
        private const string CacheFolder = "streaming-assets-cache";
        private const string StampFile = "cache.stamp";

        /// <summary>How many data files are fetched at once. The browser caps concurrent requests per host
        /// at ~6 anyway, and the old one-at-a-time loop paid a full round-trip per file — with 30+ files
        /// that pushed the localizer past the splash/intro screens, which then showed raw keys (#831).</summary>
        private const int MaxConcurrentDownloads = 6;

        /// <summary>Used only when <c>data/manifest.json</c> is missing — the real list is generated at build
        /// time by <c>BuildScript.EnsureStreamingAssetsManifest</c>, which enumerates the folder and therefore
        /// picks up new locale files on its own. Keep this list in sync anyway: a build without the manifest
        /// would otherwise silently ship content the client can never fetch (a language, a ship, the
        /// achievements). <c>EnsureStreamingAssetsManifest</c> compares the two and warns when they drift.</summary>
        private static readonly string[] FallbackManifest =
        {
            "achievements.json",
            "blocks.json",
            "blueprints.json",
            "items.json",
            "locales/de.json",
            "locales/en.json",
            "locales/es.json",
            "locales/fr.json",
            "locales/it.json",
            "minigames/catalog.json",
            "missions.json",
            "planets.json",
            "recipes.json",
            "settlement_templates.json",
            "ship_layouts/ship_corvette.json",
            "ship_layouts/ship_courier.json",
            "ship_layouts/ship_deathblock.json",
            "ship_layouts/ship_hammerhead.json",
            "ship_layouts/ship_hauler.json",
            "ship_layouts/ship_scout.json",
            "ship_layouts/ship_thunderbolt.json",
            "ship_modules.json",
            "ships.json",
            "station_templates.json",
            "whatsnew.json",
            "stories/vega_protocol/locales/de.json",
            "stories/vega_protocol/locales/en.json",
            "stories/vega_protocol/locales/es.json",
            "stories/vega_protocol/locales/fr.json",
            "stories/vega_protocol/story.json",
            "wiki/articles.json",
        };

        private static bool _ready;
        private static bool _loading;
        private static string _dataDir;
        private static int _remoteFileCount;

        [Serializable]
        private sealed class Manifest
        {
            public string[] files;
        }

        public static bool UsesRemoteStreamingAssets => IsHttpUrl(Application.streamingAssetsPath);
        public static bool IsReady => _ready;
        public static int RemoteFileCount => _remoteFileCount;

        /// <summary>The built-in file list, for the build-time staleness check in <c>BuildScript</c>.</summary>
        public static IReadOnlyList<string> FallbackManifestFiles => FallbackManifest;

        public static string DataDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_dataDir))
                {
                    return _dataDir;
                }

                return Path.Combine(Application.streamingAssetsPath, DataFolder);
            }
        }

        public static void EnsureLocalReady()
        {
            if (_ready)
            {
                return;
            }

            if (UsesRemoteStreamingAssets)
            {
                throw new InvalidOperationException("Remote StreamingAssets must be prepared with EnsureReady().");
            }

            _dataDir = Path.Combine(Application.streamingAssetsPath, DataFolder);
            _ready = true;
        }

        public static IEnumerator EnsureReady(Action<Exception> onError = null)
        {
            if (_ready)
            {
                yield break;
            }

            if (!UsesRemoteStreamingAssets)
            {
                EnsureLocalReady();
                yield break;
            }

            if (_loading)
            {
                while (_loading)
                {
                    yield return null;
                }

                if (!_ready)
                {
                    onError?.Invoke(new InvalidOperationException("Remote StreamingAssets cache did not complete."));
                }

                yield break;
            }

            _loading = true;
            Exception failure = null;
            string cacheDir = Path.Combine(Application.persistentDataPath, CacheFolder, DataFolder);
            yield return DownloadRemoteData(cacheDir, ex => failure = ex);

            if (failure == null)
            {
                _dataDir = cacheDir;
                _ready = true;
                Debug.Log($"StreamingAssets data cached at '{_dataDir}' ({_remoteFileCount} files).");
            }
            else
            {
                Debug.LogError($"StreamingAssets data cache failed: {failure.Message}");
                onError?.Invoke(failure);
            }

            _loading = false;
        }

        /// <summary>
        /// Fetches ONE data file as text, ahead of (or independently of) the full cache. Used by the
        /// browser startup to pull the locale tables first, so the splash/intro screens localize while the
        /// rest of the content is still streaming in (#831). Best-effort: <paramref name="onText"/> simply
        /// isn't invoked when the file is missing or the request fails — the caller keeps its fallback.
        /// </summary>
        public static IEnumerator FetchDataText(string relativePath, Action<string> onText)
        {
            string file = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(file) || onText == null)
            {
                yield break;
            }

            // Cache (or a native install) already on disk: read it instead of re-fetching.
            if (_ready)
            {
                string path = Path.Combine(DataDir, file.Replace('/', Path.DirectorySeparatorChar));
                string text = null;
                try
                {
                    if (File.Exists(path))
                    {
                        text = File.ReadAllText(path);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Could not read '{path}': {ex.Message}");
                }

                if (text != null)
                {
                    onText(text);
                }

                yield break;
            }

            if (!UsesRemoteStreamingAssets)
            {
                yield break; // native, but not prepared yet — the caller's own load path handles it
            }

            string url = JoinUrl(Application.streamingAssetsPath, DataFolder + "/" + file);
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (RequestSucceeded(request))
                {
                    onText(request.downloadHandler.text);
                }
                else
                {
                    Debug.LogWarning($"Could not fetch '{url}': {RequestError(request)}");
                }
            }
        }

        private static IEnumerator DownloadRemoteData(string cacheDir, Action<Exception> onFailure)
        {
            string[] files = null;
            string manifestText = null;
            string manifestUrl = JoinUrl(Application.streamingAssetsPath, DataFolder + "/manifest.json");
            using (var request = UnityWebRequest.Get(manifestUrl))
            {
                yield return request.SendWebRequest();
                if (RequestSucceeded(request))
                {
                    manifestText = request.downloadHandler.text;
                    files = ParseManifest(manifestText);
                }
                else
                {
                    Debug.LogWarning($"StreamingAssets manifest not found at '{manifestUrl}'; using built-in fallback list.");
                }
            }

            if (files == null || files.Length == 0)
            {
                files = FallbackManifest;
                manifestText = null; // stamp the built-in list, not a half-read response
            }

            var wanted = new List<string>(files.Length);
            foreach (string rawFile in files)
            {
                string file = NormalizeRelativePath(rawFile);
                if (!string.IsNullOrEmpty(file))
                {
                    wanted.Add(file);
                }
            }

            // The content only changes with a new build, so a cache that already matches this build is
            // reused as-is. Before that every startup deleted and re-downloaded the whole ~1.6 MB tree.
            string stamp = BuildStamp(manifestText, wanted);
            if (CacheMatches(cacheDir, stamp, wanted))
            {
                _remoteFileCount = wanted.Count;
                Debug.Log($"StreamingAssets cache reused ({wanted.Count} files, stamp {stamp}).");
                yield break;
            }

            try
            {
                // Drop the stamp first: a download that dies halfway must not leave a stamp claiming a
                // complete cache (the per-file existence check would catch it, but this is the honest state).
                string stampPath = StampPath(cacheDir);
                if (File.Exists(stampPath))
                {
                    File.Delete(stampPath);
                }

                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, true);
                }

                Directory.CreateDirectory(cacheDir);
            }
            catch (Exception ex)
            {
                onFailure(ex);
                yield break;
            }

            _remoteFileCount = 0;
            Exception failure = null;
            yield return DownloadFiles(wanted, cacheDir, ex => failure = ex);
            if (failure != null)
            {
                onFailure(failure);
                yield break;
            }

            WriteStamp(cacheDir, stamp);
        }

        /// <summary>Downloads the manifest's files, up to <see cref="MaxConcurrentDownloads"/> at a time.</summary>
        private static IEnumerator DownloadFiles(List<string> files, string cacheDir, Action<Exception> onFailure)
        {
            var inFlight = new List<UnityWebRequest>();
            var targets = new List<string>();
            Exception failure = null;
            int next = 0;

            try
            {
                while (failure == null && (next < files.Count || inFlight.Count > 0))
                {
                    while (failure == null && inFlight.Count < MaxConcurrentDownloads && next < files.Count)
                    {
                        string file = files[next++];
                        var request = UnityWebRequest.Get(JoinUrl(Application.streamingAssetsPath, DataFolder + "/" + file));
                        request.SendWebRequest();
                        inFlight.Add(request);
                        targets.Add(Path.Combine(cacheDir, file.Replace('/', Path.DirectorySeparatorChar)));
                    }

                    yield return null;

                    for (int i = inFlight.Count - 1; i >= 0 && failure == null; i--)
                    {
                        var request = inFlight[i];
                        if (!request.isDone)
                        {
                            continue;
                        }

                        if (!RequestSucceeded(request))
                        {
                            failure = new IOException($"Could not download '{request.url}': {RequestError(request)}");
                            break;
                        }

                        try
                        {
                            string parent = Path.GetDirectoryName(targets[i]);
                            if (!string.IsNullOrEmpty(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            File.WriteAllBytes(targets[i], request.downloadHandler.data);
                            _remoteFileCount++;
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                            break;
                        }

                        request.Dispose();
                        inFlight.RemoveAt(i);
                        targets.RemoveAt(i);
                    }
                }
            }
            finally
            {
                // Whatever is still open when we stop (failure, or the caller stopping the coroutine)
                // must not leak a native request handle.
                foreach (var request in inFlight)
                {
                    request.Abort();
                    request.Dispose();
                }
            }

            if (failure != null)
            {
                onFailure(failure);
            }
        }

        /// <summary>Identifies the cached content: build version plus the manifest itself, so a rebuilt
        /// player (or edited content served under the same version) invalidates the cache.</summary>
        private static string BuildStamp(string manifestText, List<string> files)
        {
            string source = manifestText ?? string.Join("\n", files.ToArray());
            return Application.version + "-" + files.Count + "-" + Fnv1a(source).ToString("x8");
        }

        /// <summary>FNV-1a over the manifest text — a content fingerprint, not a security hash;
        /// hand-rolled so it needs no crypto assembly in a stripped WebGL build.</summary>
        private static uint Fnv1a(string value)
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }

        private static string StampPath(string cacheDir)
            => Path.Combine(Path.GetDirectoryName(cacheDir) ?? cacheDir, StampFile);

        private static bool CacheMatches(string cacheDir, string stamp, List<string> files)
        {
            try
            {
                string stampPath = StampPath(cacheDir);
                if (!File.Exists(stampPath) || File.ReadAllText(stampPath).Trim() != stamp)
                {
                    return false;
                }

                foreach (string file in files)
                {
                    if (!File.Exists(Path.Combine(cacheDir, file.Replace('/', Path.DirectorySeparatorChar))))
                    {
                        return false; // a partial cache (interrupted download) re-downloads in full
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"StreamingAssets cache check failed: {ex.Message}");
                return false;
            }
        }

        private static void WriteStamp(string cacheDir, string stamp)
        {
            try
            {
                File.WriteAllText(StampPath(cacheDir), stamp);
            }
            catch (Exception ex)
            {
                // A missing stamp only costs one extra download next time.
                Debug.LogWarning($"Could not write the StreamingAssets cache stamp: {ex.Message}");
            }
        }

        private static string[] ParseManifest(string json)
        {
            try
            {
                var manifest = JsonUtility.FromJson<Manifest>(json);
                return manifest?.files ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"StreamingAssets manifest parse failed: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static string NormalizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace('\\', '/').Trim().TrimStart('/');
            if (normalized.Contains(".."))
            {
                Debug.LogWarning($"Skipping unsafe StreamingAssets path '{value}'.");
                return string.Empty;
            }

            return normalized;
        }

        private static string JoinUrl(string baseUrl, string relative)
            => baseUrl.TrimEnd('/') + "/" + relative.Replace('\\', '/').TrimStart('/');

        private static bool IsHttpUrl(string value)
            => value != null
               && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        private static bool RequestSucceeded(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return request.result == UnityWebRequest.Result.Success;
#else
            return !request.isNetworkError && !request.isHttpError;
#endif
        }

        private static string RequestError(UnityWebRequest request)
        {
#if UNITY_2020_2_OR_NEWER
            return string.IsNullOrEmpty(request.error) ? request.result.ToString() : request.error;
#else
            return string.IsNullOrEmpty(request.error) ? "request failed" : request.error;
#endif
        }
    }
}

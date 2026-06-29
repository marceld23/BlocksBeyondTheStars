// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
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

        private static readonly string[] FallbackManifest =
        {
            "blocks.json",
            "blueprints.json",
            "items.json",
            "locales/de.json",
            "locales/en.json",
            "minigames/catalog.json",
            "missions.json",
            "planets.json",
            "recipes.json",
            "settlement_templates.json",
            "ship_layouts/ship_corvette.json",
            "ship_layouts/ship_hauler.json",
            "ship_layouts/ship_scout.json",
            "ship_modules.json",
            "ships.json",
            "station_templates.json",
            "stories/vega_protocol/locales/de.json",
            "stories/vega_protocol/locales/en.json",
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

        private static IEnumerator DownloadRemoteData(string cacheDir, Action<Exception> onFailure)
        {
            string[] files = null;
            string manifestUrl = JoinUrl(Application.streamingAssetsPath, DataFolder + "/manifest.json");
            using (var request = UnityWebRequest.Get(manifestUrl))
            {
                yield return request.SendWebRequest();
                if (RequestSucceeded(request))
                {
                    files = ParseManifest(request.downloadHandler.text);
                }
                else
                {
                    Debug.LogWarning($"StreamingAssets manifest not found at '{manifestUrl}'; using built-in fallback list.");
                }
            }

            if (files == null || files.Length == 0)
            {
                files = FallbackManifest;
            }

            try
            {
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
            foreach (string rawFile in files)
            {
                string file = NormalizeRelativePath(rawFile);
                if (string.IsNullOrEmpty(file))
                {
                    continue;
                }

                string sourceUrl = JoinUrl(Application.streamingAssetsPath, DataFolder + "/" + file);
                string targetPath = Path.Combine(cacheDir, file.Replace('/', Path.DirectorySeparatorChar));
                using (var request = UnityWebRequest.Get(sourceUrl))
                {
                    yield return request.SendWebRequest();
                    if (!RequestSucceeded(request))
                    {
                        onFailure(new IOException($"Could not download '{sourceUrl}': {RequestError(request)}"));
                        yield break;
                    }

                    try
                    {
                        string parent = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        File.WriteAllBytes(targetPath, request.downloadHandler.data);
                        _remoteFileCount++;
                    }
                    catch (Exception ex)
                    {
                        onFailure(ex);
                        yield break;
                    }
                }
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

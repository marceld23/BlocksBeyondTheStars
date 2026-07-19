// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Cloud sync for the browser singleplayer on glitch.fun: the world snapshot blob round-trips
    /// through the WorldHost's cloud-save relay (<c>/api/glitch/save</c>), which forwards to Glitch's
    /// per-player Cloud Save slot 0 — the title token never ships in this build. Version bookkeeping
    /// follows Glitch's optimistic concurrency: we track the last cloud version we synced from; a 409
    /// on upload is resolved explicitly with <c>use_client</c> (this tab is the live session — the
    /// cloud keeps the losing state in its version history). Guests get 403 from Glitch (Cloud Save
    /// needs a logged-in account): the world then stays local-only (IndexedDB) and we stop asking.
    /// </summary>
    public static class GlitchCloudSaves
    {
        /// <summary>Uploads ride the durable-save cadence but never more often than this — the relay
        /// rate-limits per install, and the local blob is the primary copy anyway.</summary>
        private const float MinUploadIntervalSeconds = 120f;

        private static int _lastVersion;
        private static bool _blocked;         // guest / banned / relay off — stop trying this session
        private static bool _uploadInFlight;
        private static float _lastUploadTime = float.NegativeInfinity;
        private static byte[] _pending;
        private static BrowserLocalServer _attachedHost; // the persistent host we already subscribed to (#423)

        /// <summary>True when this build can sync: served by Glitch (install id present) with a portal baked in.</summary>
        public static bool Enabled
            => GlitchIntegration.PortalUrl.Length > 0 && GlitchIntegration.ArcadeInstallId.Length > 0;

        private static string MetaPath => Path.Combine(BrowserLocalServer.SaveDirectory, "cloud.meta.json");

        [Serializable]
        private sealed class CloudMeta
        {
            public int version;
        }

        [Serializable]
        private sealed class SaveResponse
        {
            public int version;
            public string payload;
        }

        [Serializable]
        private sealed class ConflictResponse
        {
            public string saveId;
            public string conflictId;
            public int serverVersion;
        }

        /// <summary>Fetches the latest cloud save before the world starts. Reports the cloud blob when
        /// it is NEWER than what this browser last synced (continuing on another device), else null —
        /// the caller keeps its local blob. Guests are detected here and stay local-only.</summary>
        public static IEnumerator FetchLatest(Action<byte[]> done)
        {
            _blocked = false;
            _lastVersion = LoadMeta();

            using (var request = UnityWebRequest.Get(
                $"{GlitchIntegration.PortalUrl}/api/glitch/save?installId={UnityWebRequest.EscapeURL(GlitchIntegration.ArcadeInstallId)}"))
            {
                request.timeout = 20;
                yield return request.SendWebRequest();

                if (request.responseCode == 403)
                {
                    _blocked = true; // guest install — Glitch Cloud Save needs a logged-in account
                    Debug.Log("[CloudSave] Guest session: the world stays in this browser (log in on Glitch to sync).");
                    done?.Invoke(null);
                    yield break;
                }

                if (request.responseCode != 200)
                {
                    Debug.Log($"[CloudSave] No cloud save used (HTTP {request.responseCode}).");
                    done?.Invoke(null);
                    yield break;
                }

                SaveResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<SaveResponse>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                    // fall through — treated as "no usable cloud save"
                }

                if (response == null || string.IsNullOrEmpty(response.payload))
                {
                    done?.Invoke(null);
                    yield break;
                }

                bool localExists = File.Exists(BrowserLocalServer.SaveBlobPath);
                if (response.version <= _lastVersion && localExists)
                {
                    // This browser already has everything the cloud has (it uploaded that version).
                    done?.Invoke(null);
                    yield break;
                }

                byte[] blob = null;
                try
                {
                    blob = Convert.FromBase64String(response.payload);
                }
                catch (FormatException)
                {
                    Debug.LogWarning("[CloudSave] Cloud payload was not valid base64 — keeping the local world.");
                }

                if (blob != null)
                {
                    _lastVersion = response.version;
                    SaveMeta(_lastVersion);
                    Debug.Log($"[CloudSave] Continuing from cloud version {response.version} ({blob.Length} B).");
                }

                done?.Invoke(blob);
            }
        }

        /// <summary>Hooks the host's durable saves: every persisted blob is queued for upload (throttled;
        /// silently off for guests/non-Glitch hosts). The host is a DontDestroyOnLoad singleton but Attach is
        /// called on EVERY menu → browser-SP boot — subscribe once per host, or after N sessions each save
        /// fires N stacked handlers and a stale duplicate blob can be re-uploaded (#423).</summary>
        public static void Attach(AppShell shell, BrowserLocalServer host)
        {
            if (!Enabled || host == _attachedHost)
            {
                return;
            }

            _attachedHost = host;
            host.BlobPersisted += blob =>
            {
                if (_blocked)
                {
                    return;
                }

                _pending = blob;
                shell.StartCoroutine(UploadPending(shell));
            };
        }

        private static IEnumerator UploadPending(AppShell shell)
        {
            if (_uploadInFlight || _pending == null
                || Time.realtimeSinceStartup - _lastUploadTime < MinUploadIntervalSeconds)
            {
                yield break; // the next durable save re-queues; the local blob already has this state
            }

            _uploadInFlight = true;
            byte[] blob = _pending;
            _pending = null;

            string body = "{\"installId\":\"" + GlitchIntegration.ArcadeInstallId + "\"," +
                          "\"payload\":\"" + Convert.ToBase64String(blob) + "\"," +
                          "\"baseVersion\":" + _lastVersion + "}";
            using (var request = NewJsonPost($"{GlitchIntegration.PortalUrl}/api/glitch/save", body))
            {
                yield return request.SendWebRequest();

                if (request.responseCode == 200)
                {
                    CommitVersion(request.downloadHandler.text);
                }
                else if (request.responseCode == 409)
                {
                    // Another device wrote in between. This tab holds the LIVE world the player is
                    // acting in, so resolve with use_client — Glitch archives the losing version in
                    // the slot's history, nothing is silently destroyed.
                    yield return ResolveConflict(request.downloadHandler.text, blob);
                }
                else if (request.responseCode == 403)
                {
                    _blocked = true;
                    Debug.Log("[CloudSave] Cloud sync unavailable for this session (guest or banned) — staying local.");
                }
                else
                {
                    Debug.LogWarning($"[CloudSave] Upload failed (HTTP {request.responseCode}) — retrying on the next save.");
                }
            }

            _lastUploadTime = Time.realtimeSinceStartup;
            _uploadInFlight = false;
        }

        private static IEnumerator ResolveConflict(string conflictJson, byte[] blob)
        {
            ConflictResponse conflict = null;
            try
            {
                conflict = JsonUtility.FromJson<ConflictResponse>(conflictJson);
            }
            catch (Exception)
            {
                // malformed conflict — drop; the next durable save retries the whole flow
            }

            if (conflict == null || string.IsNullOrEmpty(conflict.saveId) || string.IsNullOrEmpty(conflict.conflictId))
            {
                yield break;
            }

            string body = "{\"installId\":\"" + GlitchIntegration.ArcadeInstallId + "\"," +
                          "\"saveId\":\"" + conflict.saveId + "\"," +
                          "\"conflictId\":\"" + conflict.conflictId + "\"," +
                          "\"choice\":\"use_client\"}";
            using (var request = NewJsonPost($"{GlitchIntegration.PortalUrl}/api/glitch/save/resolve", body))
            {
                yield return request.SendWebRequest();
                if (request.responseCode == 200)
                {
                    CommitVersion(request.downloadHandler.text);

                    // The resolve kept the SERVER state as the slot content when Glitch honours
                    // keep_server only — for use_client our local blob is now the cloud state, but its
                    // version advanced past our base: re-upload once so cloud bytes == local bytes.
                    _pending = blob;
                    Debug.Log("[CloudSave] Conflict resolved (this session wins); cloud history keeps the other state.");
                }
                else
                {
                    Debug.LogWarning($"[CloudSave] Conflict resolution failed (HTTP {request.responseCode}).");
                }
            }
        }

        private static void CommitVersion(string responseJson)
        {
            try
            {
                var response = JsonUtility.FromJson<SaveResponse>(responseJson);
                if (response != null && response.version > 0)
                {
                    _lastVersion = response.version;
                    SaveMeta(_lastVersion);
                }
            }
            catch (Exception)
            {
                // version bookkeeping is best-effort; a stale base just triggers the conflict flow once
            }
        }

        private static UnityWebRequest NewJsonPost(string url, string body)
        {
            var request = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 60, // multi-MB base64 payloads on slow lines
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            return request;
        }

        private static int LoadMeta()
        {
            try
            {
                if (File.Exists(MetaPath))
                {
                    var meta = JsonUtility.FromJson<CloudMeta>(File.ReadAllText(MetaPath));
                    return meta?.version ?? 0;
                }
            }
            catch (Exception)
            {
                // unreadable meta = start from version 0; worst case is one conflict round-trip
            }

            return 0;
        }

        private static void SaveMeta(int version)
        {
            try
            {
                Directory.CreateDirectory(BrowserLocalServer.SaveDirectory);
                File.WriteAllText(MetaPath, JsonUtility.ToJson(new CloudMeta { version = version }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CloudSave] Could not persist the version meta: {ex.Message}");
            }
        }
    }
}

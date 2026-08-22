// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
#endif

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Browser persistence helpers. Unity's <c>Application.persistentDataPath</c> lives on an in-memory
    /// Emscripten FS backed by IndexedDB (IDBFS): writes only become durable after an explicit
    /// <c>FS.syncfs</c> (<c>Plugins/BbsFileSync.jslib</c>), and every glitch.fun deployment gets a fresh,
    /// empty folder under the same mount (#345, #1177). Both concerns live here so every durable write —
    /// the world blob, the settings — syncs the same way, and so a new deployment can adopt what the
    /// previous one left behind. Everything is a no-op outside browser builds.
    /// </summary>
    public static class WebGlStorage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BbsSyncIndexedDb();
#endif

        /// <summary>Flushes the IDBFS writes to IndexedDB so they survive a tab close / reload (#1179).
        /// Cheap to call after every durable write; no-op outside browser builds.</summary>
        public static void Sync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                BbsSyncIndexedDb();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WebGL] IndexedDB sync failed: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Browser builds only (#1177): when the current deployment's storage has no <paramref name="relativePath"/>
        /// yet, adopts the newest copy a previous deployment left in a sibling folder (plus the
        /// <paramref name="companions"/> next to it) and makes the copy durable. Returns true when something
        /// was adopted. Never overwrites, never deletes — see <see cref="PreviousDeploymentStorage"/>.
        /// </summary>
        public static bool TryAdoptFromPreviousDeployment(string relativePath, params string[] companions)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string source = PreviousDeploymentStorage.TryAdopt(Application.persistentDataPath, relativePath, companions);
            if (source == null)
            {
                return false;
            }

            Sync();
            Debug.Log($"[WebGL] Adopted '{relativePath}' from a previous deployment's storage ({source}).");
            return true;
#else
            return false;
#endif
        }
    }
}

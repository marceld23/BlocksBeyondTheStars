// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one place that knows where the client's persistent data lives. <see cref="Root"/> is Unity's
    /// <see cref="Application.persistentDataPath"/> unless a <c>portable_data_dir.txt</c> sits next to the
    /// executable (#1285, rules in <see cref="PortableDataDir"/>) — then it is the folder that file names, so a
    /// portable copy keeps settings, saves, exports and spools with it. Every <c>persistentDataPath</c> consumer
    /// goes through here; the only exceptions are the WebGL storage adoption (IDBFS sibling folders) and the
    /// one-time "Spacecraft" rename migration, which by definition operate on Unity's own folder. Resolved once,
    /// on the main thread, before anything reads or writes (AppShell.Awake).
    /// </summary>
    public static class AppPaths
    {
        private static string _root;

        /// <summary>The persistent-data root — with the portable redirect applied when one is configured.</summary>
        public static string Root
        {
            get
            {
                if (string.IsNullOrEmpty(_root))
                {
                    _root = Resolve();
                }

                return _root;
            }
        }

        /// <summary>True when the root was redirected by a marker file (for the log / diagnostics).</summary>
        public static bool IsPortableRedirect { get; private set; }

        /// <summary>The folder holding the game executable (next to the <c>.exe</c> / Linux binary; the folder
        /// containing the <c>.app</c> bundle on macOS; <c>client/</c> in the Editor). Null on WebGL.</summary>
        public static string ExecutableDirectory
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return null;
#else
                string dataPath = Application.dataPath; // <exe dir>/<Product>_Data  |  <X>.app/Contents  |  <repo>/client/Assets
                return Application.platform == RuntimePlatform.OSXPlayer
                    ? Path.GetFullPath(Path.Combine(dataPath, "..", ".."))
                    : Path.GetDirectoryName(dataPath);
#endif
            }
        }

        private static string Resolve()
        {
            string fallback = Application.persistentDataPath;
#if UNITY_WEBGL && !UNITY_EDITOR
            return fallback;
#else
            string exeDir;
            try
            {
                exeDir = ExecutableDirectory;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AppPaths] Could not determine the executable folder: {e.Message}");
                return fallback;
            }

            if (string.IsNullOrEmpty(exeDir))
            {
                return fallback;
            }

            string redirect = PortableDataDir.ResolveFromMarker(exeDir, out string error);
            if (error != null)
            {
                Debug.LogWarning($"[AppPaths] {error} — using the default data folder '{fallback}'.");
            }

            if (string.IsNullOrEmpty(redirect))
            {
                return fallback;
            }

            if (!PortableDataDir.TryPrepare(redirect, out string prepareError))
            {
                Debug.LogWarning($"[AppPaths] {prepareError} — using the default data folder '{fallback}'.");
                return fallback;
            }

            IsPortableRedirect = true;
            Debug.Log($"[AppPaths] Portable data folder active ({PortableDataDir.MarkerFileName}): '{redirect}'.");
            return redirect;
#endif
        }
    }
}

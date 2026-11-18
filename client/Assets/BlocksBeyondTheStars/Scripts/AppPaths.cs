// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one place that knows where the client's persistent data lives. <see cref="Root"/> is Unity's
    /// <see cref="Application.persistentDataPath"/> unless a start option moves it (rules in <see cref="LaunchOptions"/>):
    /// a <c>portable_data_dir.txt</c> next to the executable (#1285, <see cref="PortableDataDir"/>) or <c>-dataDir</c> on
    /// the command line — then it is that folder, so a portable copy keeps settings, saves, exports and spools with it.
    /// The same pass reads the other start option, the bundled server's port (<see cref="Launch"/>). Every
    /// <c>persistentDataPath</c> consumer goes through here; the only exceptions are the WebGL storage adoption (IDBFS
    /// sibling folders) and the one-time "Spacecraft" rename migration, which by definition operate on Unity's own
    /// folder. Resolved once, on the main thread, before anything reads or writes (AppShell.Awake).
    /// </summary>
    public static class AppPaths
    {
        private static string _root;
        private static LaunchOptions _launch;

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

        /// <summary>The start options read from the marker file and the command line (resolved together with
        /// <see cref="Root"/>). <see cref="LaunchOptions.Defaults"/> in the browser.</summary>
        public static LaunchOptions Launch
        {
            get
            {
                _ = Root;
                return _launch ?? LaunchOptions.Defaults;
            }
        }

        /// <summary>True when the root was redirected by a start option (for the log / diagnostics).</summary>
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

            _launch = ReadLaunchOptions(exeDir, fallback);
            string redirect = _launch.DataDir;
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
            Debug.Log($"[AppPaths] Data folder from {_launch.DataDirSource}: '{redirect}'.");
            return redirect;
#endif
        }

        /// <summary>The marker next to the executable and the process arguments, combined and logged. Never throws.</summary>
        private static LaunchOptions ReadLaunchOptions(string exeDir, string fallback)
        {
            string marker = PortableDataDir.ReadMarker(exeDir, out string error);
            if (error != null)
            {
                Debug.LogWarning($"[AppPaths] {error} — using the default data folder '{fallback}'.");
            }

            string[] args;
            try
            {
                args = System.Environment.GetCommandLineArgs();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AppPaths] Could not read the command line: {e.Message}");
                args = System.Array.Empty<string>();
            }

            var options = LaunchOptions.Resolve(exeDir, marker, args);
            foreach (string warning in options.Warnings)
            {
                Debug.LogWarning($"[AppPaths] {warning}");
            }

            if (options.HostPort is int port)
            {
                Debug.Log($"[AppPaths] Port of the bundled server from {options.HostPortSource}: {port}.");
            }

            return options;
        }
    }
}

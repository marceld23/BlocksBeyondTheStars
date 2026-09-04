// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Did the last session end cleanly?" marker (#1564). A blue screen, a driver reset or a hard power-off
    /// leaves no log line and no exception for <see cref="CrashReporter"/> to catch — the only trace is that
    /// the game never got to say goodbye. So on startup a tiny marker file is written next to the settings
    /// (<c>AppPaths.Root/session_running.marker</c>), touched once a minute while the game runs, and deleted on
    /// a clean <c>OnApplicationQuit</c>. A marker that is already there at the next startup means the previous
    /// session died: <see cref="LastSessionUnclean"/> is set for this whole session and the marker's last touch
    /// (<see cref="LastSessionEndedAt"/>, accurate to about a minute) tells the inbox roughly WHEN — both ride
    /// along with every F1 feedback and crash report sent this session as <c>lastSessionUnclean</c> /
    /// <c>lastSessionEndedAt</c>.
    /// <para>
    /// Best effort throughout: every file operation is guarded, nothing here can throw into the caller. Skipped
    /// in the browser, where a closed tab never quits cleanly and the marker would always say "unclean".
    /// </para>
    /// </summary>
    public static class SessionMarker
    {
        /// <summary>File name of the marker, next to <c>client_settings.json</c>.</summary>
        public const string FileName = "session_running.marker";

        /// <summary>How often <see cref="Touch"/> actually refreshes the file's timestamp.</summary>
        public const float TouchIntervalSeconds = 60f;

        /// <summary>True when the marker was already present at startup — the previous session did not quit cleanly.</summary>
        public static bool LastSessionUnclean { get; private set; }

        /// <summary>ISO-8601 UTC of the previous session's last marker touch when <see cref="LastSessionUnclean"/>
        /// is set (accurate to <see cref="TouchIntervalSeconds"/>); empty otherwise.</summary>
        public static string LastSessionEndedAt { get; private set; } = string.Empty;

        private static string _path;
        private static bool _begun;
        private static float _nextTouchAt;

        /// <summary>Reads the previous session's verdict and writes this session's marker. Main thread, once —
        /// called by <see cref="CrashReporter"/> in Awake, before anything can crash.</summary>
        public static void Begin()
        {
#if !UNITY_WEBGL
            if (_begun)
            {
                return;
            }

            _begun = true;
            try
            {
                string root = AppPaths.Root;
                string path = Path.Combine(root, FileName);
                if (File.Exists(path))
                {
                    LastSessionUnclean = true;
                    LastSessionEndedAt = File.GetLastWriteTimeUtc(path).ToString("o");
                    Debug.Log($"[SessionMarker] The previous session did not quit cleanly (last alive {LastSessionEndedAt}).");
                }

                Directory.CreateDirectory(root);
                File.WriteAllText(path, DateTime.UtcNow.ToString("o"));
                _path = path;
                _nextTouchAt = Time.realtimeSinceStartup + TouchIntervalSeconds;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionMarker] Could not write the session marker: {e.Message}");
            }
#endif
        }

        /// <summary>Refreshes the marker's timestamp at most every <see cref="TouchIntervalSeconds"/> so a dead
        /// session's last-alive time is known to the minute. Cheap to call every frame; main thread.</summary>
        public static void Touch()
        {
#if !UNITY_WEBGL
            if (_path == null || Time.realtimeSinceStartup < _nextTouchAt)
            {
                return;
            }

            _nextTouchAt = Time.realtimeSinceStartup + TouchIntervalSeconds;
            try
            {
                File.SetLastWriteTimeUtc(_path, DateTime.UtcNow);
            }
            catch
            {
                // a missing/locked marker only costs timestamp accuracy — never disturb the game
            }
#endif
        }

        /// <summary>Removes the marker: this session is ending the normal way (<c>OnApplicationQuit</c>).</summary>
        public static void End()
        {
#if !UNITY_WEBGL
            try
            {
                if (_path != null && File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SessionMarker] Could not remove the session marker: {e.Message}");
            }

            _path = null;
#endif
        }

        /// <summary>Adds <c>lastSessionUnclean</c> / <c>lastSessionEndedAt</c> to a report's <c>ReportJson</c>.
        /// Reads only plain statics set once in <see cref="Begin"/>, so it is safe on the threaded log callback.</summary>
        public static void WriteTo(Dictionary<string, object> json)
        {
            if (json == null)
            {
                return;
            }

            json["lastSessionUnclean"] = LastSessionUnclean;
            json["lastSessionEndedAt"] = LastSessionEndedAt;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
// Velopack is the desktop installer/auto-updater. It is excluded from the Editor AND the WebGL player:
// the browser build has no Velopack DLL (you "update" by reloading the page), so referencing it there
// fails the WebGL script compile with CS0246 'Velopack'.
#if !UNITY_EDITOR && !UNITY_WEBGL
using Velopack;
#endif
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Lifecycle of an in-app update check; the settings screen maps each value to a localized label.</summary>
    public enum UpdateState
    {
        Idle,
        Checking,
        Downloading,
        Restarting,
        UpToDate,
        NotInstalled,
        NoUrl,
        Failed,
    }

    /// <summary>
    /// In-app updates via Velopack (MIT), pulling from the self-hosting server's feed served by
    /// <c>BlocksBeyondTheStars.Api</c> at <c>/updates</c>. Two responsibilities:
    ///   1. <see cref="Bootstrap"/> runs Velopack's startup hooks that the installer/updater rely on
    ///      (first-run shortcut creation, post-update fast callbacks). It MUST run before anything else —
    ///      hence <c>[RuntimeInitializeOnLoadMethod(BeforeSplashScreen)]</c> — and returns within
    ///      milliseconds on a normal launch (it only does real work when invoked with hook arguments by
    ///      Setup.exe / Update.exe, each of which exits the process itself).
    ///   2. <see cref="CheckForUpdates"/> checks the configured feed; if a newer build exists it downloads
    ///      it and restarts into the new version.
    /// Only effective in an installed build — a dev/Editor run or a portable (zip) copy reports
    /// <see cref="UpdateState.NotInstalled"/>. The client stays presentation-only: the feed is plain static
    /// files, so this never grants the client authority over game state.
    /// </summary>
    public static class ClientUpdater
    {
        /// <summary>True while a check/download is in flight (the settings button disables + shows progress).</summary>
        public static bool Busy { get; private set; }

        /// <summary>Current status, mapped to a localized label by the settings UI.</summary>
        public static UpdateState State { get; private set; } = UpdateState.Idle;

        /// <summary>Extra detail (target version, or error text) appended after the localized status label.</summary>
        public static string Detail { get; private set; } = string.Empty;

#if !UNITY_EDITOR && !UNITY_WEBGL
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Bootstrap()
        {
            try
            {
                VelopackApp.Build().Run();
            }
            catch (Exception e)
            {
                // A missing/!installed locator must never block startup — just run unmanaged.
                Debug.LogWarning($"Velopack startup hook skipped (continuing unmanaged): {e.Message}");
            }
        }
#endif

        /// <summary>Checks <paramref name="feedUrl"/> for a newer release; if found, downloads it and
        /// restarts into the new version. <paramref name="onChanged"/> is invoked on the Unity main thread
        /// each time <see cref="State"/>/<see cref="Busy"/> change, so the settings screen can refresh.</summary>
        public static async void CheckForUpdates(string feedUrl, Action onChanged)
        {
            if (Busy)
            {
                return;
            }

#pragma warning disable CS1998 // the Editor/WebGL branch has no awaits by design
#if UNITY_EDITOR || UNITY_WEBGL
            // No Velopack in the Editor or the browser build — there is no installed app to update.
            State = UpdateState.NotInstalled;
            Detail = string.Empty;
            onChanged?.Invoke();
#else
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                State = UpdateState.NoUrl;
                Detail = string.Empty;
                onChanged?.Invoke();
                return;
            }

            Busy = true;
            State = UpdateState.Checking;
            Detail = string.Empty;
            onChanged?.Invoke();
            try
            {
                var mgr = new UpdateManager(feedUrl.Trim());
                if (!mgr.IsInstalled)
                {
                    State = UpdateState.NotInstalled;
                    return;
                }

                var info = await mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    State = UpdateState.UpToDate;
                    return;
                }

                State = UpdateState.Downloading;
                Detail = info.TargetFullRelease.Version.ToString();
                onChanged?.Invoke();
                await mgr.DownloadUpdatesAsync(info);

                State = UpdateState.Restarting;
                onChanged?.Invoke();
                mgr.ApplyUpdatesAndRestart(info.TargetFullRelease); // exits this process and relaunches the new build
            }
            catch (Exception e)
            {
                State = UpdateState.Failed;
                Detail = e.Message;
                Debug.LogWarning($"Velopack update check failed: {e}");
            }
            finally
            {
                Busy = false;
                onChanged?.Invoke();
            }
#endif
#pragma warning restore CS1998
        }
    }
}

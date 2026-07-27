// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
// Velopack is the desktop installer/auto-updater. It is excluded from the Editor AND the WebGL player:
// the browser build has no Velopack DLL (you "update" by reloading the page), so referencing it there
// fails the WebGL script compile with CS0246 'Velopack'.
#if !UNITY_EDITOR && !UNITY_WEBGL
using Velopack;
using Velopack.Sources;
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
    /// In-app updates via Velopack (MIT). The feed is either the official GitHub release (the default
    /// <see cref="ClientSettings.DefaultUpdateFeedUrl"/> — release assets carry <c>releases.win.json</c>
    /// plus the <c>.nupkg</c> payload, read through Velopack's <c>GithubSource</c>) or a self-hosting
    /// server's static feed served by <c>BlocksBeyondTheStars.Api</c> at <c>/updates</c>. Three
    /// responsibilities:
    ///   1. <see cref="Bootstrap"/> runs Velopack's startup hooks that the installer/updater rely on
    ///      (first-run shortcut creation, post-update fast callbacks). It MUST run before anything else —
    ///      hence <c>[RuntimeInitializeOnLoadMethod(BeforeSplashScreen)]</c> — and returns within
    ///      milliseconds on a normal launch (it only does real work when invoked with hook arguments by
    ///      Setup.exe / Update.exe, each of which exits the process itself).
    ///   2. <see cref="CheckForNoticeOnStartup"/> runs a quiet check-only pass once per launch; a found
    ///      version lands in <see cref="NoticeVersion"/> and the main menu offers it (#543).
    ///   3. <see cref="CheckForUpdates"/> checks the configured feed; if a newer build exists it downloads
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

        /// <summary>Version found by the quiet startup check ("" = none found / not checked). While
        /// non-empty and not <see cref="NoticeDismissed"/>, the main menu shows the update notice.</summary>
        public static string NoticeVersion { get; private set; } = string.Empty;

        /// <summary>Set when the player answers the startup notice with "later" — the notice stays away
        /// for the rest of the session (returning to the menu must not nag again).</summary>
        public static bool NoticeDismissed;

        /// <summary>Quiet startup pass: checks <paramref name="feedUrl"/> once and records a found newer
        /// version in <see cref="NoticeVersion"/> — no download, no UI, and silent on every failure (a
        /// launch must never be slowed or interrupted by update plumbing). Runs concurrently with the
        /// splash screens; the main menu picks the result up whenever it lands (#543).</summary>
#pragma warning disable CS1998 // the Editor/WebGL branch has no awaits by design (reported at the signature)
        public static async void CheckForNoticeOnStartup(string feedUrl)
        {
#if !UNITY_EDITOR && !UNITY_WEBGL
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                return;
            }

            try
            {
                var mgr = CreateManager(feedUrl);
                if (!mgr.IsInstalled)
                {
                    return; // portable/dev copy — nothing Velopack could apply an update to
                }

                var info = await mgr.CheckForUpdatesAsync();
                if (info != null)
                {
                    NoticeVersion = info.TargetFullRelease.Version.ToString();
                }
            }
            catch (Exception e)
            {
                // Offline, rate-limited, feed missing: the startup check simply has no result.
                Debug.Log($"Startup update check skipped: {e.Message}");
            }
#endif
        }
#pragma warning restore CS1998

#if !UNITY_EDITOR && !UNITY_WEBGL
        /// <summary>Builds the UpdateManager for a feed URL: a github.com repository URL reads the official
        /// feed straight off the GitHub release assets (releases.win.json + .nupkg) via GithubSource;
        /// anything else stays the plain static-file feed a self-hosting server serves at /updates.</summary>
        private static UpdateManager CreateManager(string feedUrl)
        {
            string url = feedUrl.Trim();
            return url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(url, null, false))
                : new UpdateManager(url);
        }
#endif

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
#pragma warning disable CS1998 // the Editor/WebGL branch has no awaits by design (reported at the signature)
        public static async void CheckForUpdates(string feedUrl, Action onChanged)
        {
            if (Busy)
            {
                return;
            }

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
                var mgr = CreateManager(feedUrl);
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

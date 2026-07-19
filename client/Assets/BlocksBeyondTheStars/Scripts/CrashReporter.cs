// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Diagnostics;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
#endif

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Catches unhandled client exceptions and reports them automatically — the client-side mirror of the
    /// server's tick-fault / crash pipeline. It hooks Unity's threaded log callback, keeps only genuine
    /// <see cref="LogType.Exception"/> entries (handled <c>LogError</c>s are noise), de-dups + rate-limits them
    /// (<see cref="CrashReportThrottle"/>) so a per-frame fault can't flood anything, writes each to a durable
    /// local queue FIRST (<see cref="CrashReportSpool"/> under <c>persistentDataPath/crashreports</c>) and then
    /// uploads it off the main thread via the existing <see cref="FeedbackUploader"/> (on WebGL, which has no
    /// threads, via <see cref="FeedbackWebGlTransport"/> coroutines instead). No dialog — unlike F1
    /// feedback this is silent. On start it also retries whatever a previous session couldn't send (a
    /// crash-on-exit, or an offline run). When no API key is configured (dev builds) reports still accumulate
    /// locally for a manual send; the local file is the source of truth either way.
    ///
    /// Installed once by <see cref="AppShell"/> on its own <see cref="DontDestroyOnLoad"/> object so it spans
    /// the whole app (splash → menu → in-game). The dedup/spool/throttle logic lives in the Unity-free
    /// <c>Client.Core</c> assembly and is unit-tested headless.
    /// </summary>
    public sealed class CrashReporter : MonoBehaviour
    {
        /// <summary>Assigned by the installer so reports carry the anonymous install id, player name and
        /// language. Read (never mutated) from the log callback, which may run off the main thread.</summary>
        public ClientSettings Settings;

        private FeedbackUploader _uploader;
        private CrashReportSpool _spool;
        private readonly CrashReportThrottle _throttle = new CrashReportThrottle(20);
        private string _sessionId = string.Empty;

        // Cached on the main thread: the threaded log callback can fire off-thread, where Unity API is unsafe.
        private string _version = string.Empty;
        private string _platform = string.Empty;
        private string _buildGuid = string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL ships without threads (webGLThreadsSupport:0), so the Task.Run upload bodies below never
        // execute in the browser — telemetry would only ever pile up in the IndexedDB-backed spool. Uploads
        // therefore run as one-at-a-time UnityWebRequest coroutines on the main thread instead (#421 M14);
        // this queue feeds the pump. Single-threaded platform: the log callback fires on the main thread too,
        // so plain fields need no locking.
        private readonly Queue<(string Path, string Json)> _webglUploads = new Queue<(string Path, string Json)>();
        private bool _webglPumpRunning;
#endif

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _sessionId = Guid.NewGuid().ToString("N");
            _version = AppShell.Version;
            _platform = Application.platform.ToString();
            _buildGuid = Application.buildGUID ?? string.Empty;
            _spool = new CrashReportSpool(Path.Combine(Application.persistentDataPath, "crashreports"));
            _uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);

            // Threaded variant so a crash on a worker thread (e.g. chunk meshing) is caught too.
            Application.logMessageReceivedThreaded += OnLog;
        }

        private void Start()
        {
            if (_uploader == null || !_uploader.IsConfigured)
            {
                return; // dev build / no key: leave the queue on disk for a manual send
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(FlushPendingWebGl());
#else
            var spool = _spool;
            var uploader = _uploader;
            _ = Task.Run(() => FlushPending(spool, uploader));
#endif
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception)
            {
                return;
            }

            try
            {
                string signature = CrashReportThrottle.Signature(condition, stackTrace);
                if (!_throttle.ShouldReport(signature))
                {
                    return; // duplicate this session, or per-session cap reached
                }

                string json = BuildReportJson(condition, stackTrace);
                string path = _spool.Write(json, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));

                if (path != null && _uploader.IsConfigured)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    _webglUploads.Enqueue((path, json));
                    if (!_webglPumpRunning)
                    {
                        StartCoroutine(PumpWebGlUploads());
                    }
#else
                    var spool = _spool;
                    var uploader = _uploader;
                    string body = json;
                    string filePath = path;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var result = uploader.UploadRawJson(body);
                            if (result != null && result.Ok)
                            {
                                spool.MarkSent(filePath);
                            }
                        }
                        catch
                        {
                            // leave it queued; the next session's Start() retries it
                        }
                    });
#endif
                }
            }
            catch
            {
                // a crash reporter must never throw back into Unity's log pump
            }
        }

        /// <summary>Builds the report body. Touches only cached strings + plain <see cref="ClientSettings"/>
        /// fields (immutable strings) — NO Unity API — so it is safe on the threaded log callback.</summary>
        private string BuildReportJson(string condition, string stackTrace)
        {
            try
            {
                // Scrub PII (OS user name in paths, e-mails) out of the free text before it touches disk or the wire.
                string condition2 = CrashPiiScrubber.Scrub(condition ?? string.Empty);
                string stack = CrashPiiScrubber.Scrub(stackTrace ?? string.Empty);

                string firstLine = condition2;
                int nl = firstLine.IndexOf('\n');
                if (nl >= 0)
                {
                    firstLine = firstLine.Substring(0, nl);
                }

                firstLine = firstLine.Trim();
                if (firstLine.Length > 100)
                {
                    firstLine = firstLine.Substring(0, 100);
                }

                // The endpoint caps description at 5000 chars, so keep the (possibly long) stack out of it and
                // carry the full trace in reportJson, which the backend stores verbatim.
                string description = condition2 + "\n\n" + stack;
                if (description.Length > FeedbackUploader.MaxDescriptionLength)
                {
                    description = description.Substring(0, FeedbackUploader.MaxDescriptionLength);
                }

                var settings = Settings; // local copy: may be reassigned on the main thread
                var report = new FeedbackReport
                {
                    Title = string.IsNullOrEmpty(firstLine) ? "Client crash" : "Client crash: " + firstLine,
                    Description = description,
                    Email = string.Empty,
                    GameVersion = _version,
                    BuildNumber = _buildGuid,
                    PlayerId = settings != null ? settings.PlayerToken : string.Empty,
                    PlayerName = settings != null ? settings.PlayerName : string.Empty,
                    SessionId = _sessionId,
                    Platform = _platform,
                    ClientTimestamp = DateTime.UtcNow.ToString("o"),
                    ReportJson = new Dictionary<string, object>
                    {
                        ["kind"] = "crash",
                        ["source"] = "client",
                        ["logType"] = "Exception",
                        ["stackTrace"] = stack,
                    },
                };

                return FeedbackUploader.Serialize(report, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Retries the on-disk queue: send each, move accepted ones to <c>sent/</c>, and stop at the
        /// first failure (a down/offline endpoint) so the rest wait for the next launch. Runs on a background
        /// thread; best-effort.</summary>
        private static void FlushPending(CrashReportSpool spool, FeedbackUploader uploader)
        {
            try
            {
                foreach (var path in spool.ListPending())
                {
                    string json = spool.Read(path);
                    if (string.IsNullOrEmpty(json))
                    {
                        continue;
                    }

                    var result = uploader.UploadRawJson(json);
                    if (result == null || !result.Ok)
                    {
                        break;
                    }

                    spool.MarkSent(path);
                }
            }
            catch
            {
                // best-effort startup catch-up
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>WebGL flavor of the per-report upload: drains <see cref="_webglUploads"/> one
        /// UnityWebRequest at a time. A failed send stays queued on disk (IndexedDB-backed
        /// persistentDataPath) and is retried by <see cref="FlushPendingWebGl"/> next session.</summary>
        private IEnumerator PumpWebGlUploads()
        {
            _webglPumpRunning = true;
            while (_webglUploads.Count > 0)
            {
                var (path, json) = _webglUploads.Dequeue();
                FeedbackUploadResult result = null;
                yield return FeedbackWebGlTransport.PostJson(json, r => result = r);
                if (result != null && result.Ok)
                {
                    _spool.MarkSent(path);
                }
            }

            _webglPumpRunning = false;
        }

        /// <summary>WebGL flavor of <see cref="FlushPending"/>: retries the on-disk queue on a coroutine,
        /// stopping at the first failure (a down/offline endpoint) so the rest wait for the next launch.</summary>
        private IEnumerator FlushPendingWebGl()
        {
            foreach (var path in _spool.ListPending())
            {
                string json = _spool.Read(path);
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                FeedbackUploadResult result = null;
                yield return FeedbackWebGlTransport.PostJson(json, r => result = r);
                if (result == null || !result.Ok)
                {
                    yield break;
                }

                _spool.MarkSent(path);
            }
        }
#endif
    }
}

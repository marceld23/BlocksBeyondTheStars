// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Text;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;
using UnityEngine.Networking;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Browser transport for the report inbox: posts an already-serialized body / fetches a JSON document via
    /// <see cref="UnityWebRequest"/> — the WebGL stand-in for <see cref="FeedbackUploader.UploadRawJson"/> and
    /// <see cref="FeedbackReplyClient"/> (same endpoints, header and never-throws contract). WASM has neither
    /// sockets nor threads, so HttpClient/Task.Run can't run in the browser; callers run these as coroutines on
    /// the main thread (WebGL requests are async under the hood, so nothing blocks). Shared by
    /// <see cref="FeedbackUi"/> (F2 reports + reply polling, #1328) and <see cref="CrashReporter"/>
    /// (automatic crash telemetry, #421 M14).
    /// </summary>
    internal static class FeedbackWebGlTransport
    {
        /// <summary>Posts one report body to the ingest endpoint and calls <paramref name="done"/> with the
        /// outcome (incl. the inbox's <c>bugReportId</c>). Never throws.</summary>
        public static IEnumerator PostJson(string json, Action<FeedbackUploadResult> done)
            => Request(FeedbackUploader.DefaultEndpoint, UnityWebRequest.kHttpVerbPOST, json, (result, body) =>
            {
                if (result.Ok)
                {
                    result.ReportId = ReadReportId(body);
                }

                done?.Invoke(result);
            });

        /// <summary>Generic JSON request against any inbox route (GET when <paramref name="json"/> is null);
        /// <paramref name="done"/> receives the outcome and the raw response body. Never throws.</summary>
        public static IEnumerator Request(string url, string method, string json, Action<FeedbackUploadResult, string> done)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
                }

                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader(FeedbackUploader.ApiKeyHeader, BugReportBuildSecrets.ApiKey);
                req.timeout = 15; // mirrors the HttpClient timeout

                yield return req.SendWebRequest();

                var result = new FeedbackUploadResult
                {
                    StatusCode = (int)req.responseCode,
                    Ok = req.result == UnityWebRequest.Result.Success,
                };
                if (!result.Ok)
                {
                    result.Error = result.StatusCode > 0 ? "http_" + result.StatusCode : (req.error ?? "network");
                }

                string body = string.Empty;
                try { body = req.downloadHandler != null ? req.downloadHandler.text ?? string.Empty : string.Empty; }
                catch { /* a torn-down handler just means no body */ }

                done?.Invoke(result, body);
            }
        }

        private static string ReadReportId(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            // System.Text.Json is not part of Unity's runtime (the WebGL build is the only one that compiles
            // this file, so the desktop players never noticed) — JsonUtility reads the one field we need.
            try
            {
                var dto = JsonUtility.FromJson<IngestResponse>(body);
                return dto?.bugReportId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        [Serializable]
        private sealed class IngestResponse
        {
            // Field name = the inbox's JSON property (JsonUtility maps by name, no attributes).
            public string bugReportId;
        }
    }
}
#endif

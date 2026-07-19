// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Text;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine.Networking;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Browser upload for report bodies: posts an already-serialized report via <see cref="UnityWebRequest"/> —
    /// the WebGL stand-in for <see cref="FeedbackUploader.UploadRawJson"/> (same endpoint, header and
    /// never-throws contract). WASM has neither sockets nor threads, so HttpClient/Task.Run can't run in the
    /// browser; callers run this as a coroutine on the main thread (WebGL requests are async under the hood, so
    /// nothing blocks). Shared by <see cref="FeedbackUi"/> (F2 reports) and <see cref="CrashReporter"/>
    /// (automatic crash telemetry, #421 M14).
    /// </summary>
    internal static class FeedbackWebGlTransport
    {
        /// <summary>Posts one JSON body and calls <paramref name="done"/> with the outcome. Never throws.</summary>
        public static IEnumerator PostJson(string json, Action<FeedbackUploadResult> done)
        {
            using (var req = new UnityWebRequest(FeedbackUploader.DefaultEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
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

                done?.Invoke(result);
            }
        }
    }
}
#endif

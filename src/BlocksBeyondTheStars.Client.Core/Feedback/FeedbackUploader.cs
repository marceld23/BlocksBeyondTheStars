// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>The outcome of a feedback upload attempt. Never throws back to the caller — failures are
    /// reported here so the game can show a friendly message and (optionally) keep a local copy.</summary>
    public sealed class FeedbackUploadResult
    {
        /// <summary>True when the website accepted the report (HTTP 2xx).</summary>
        public bool Ok { get; set; }

        /// <summary>HTTP status code, or 0 when the request never reached the server (timeout/DNS/offline).</summary>
        public int StatusCode { get; set; }

        /// <summary>A short, non-localized reason code for diagnostics/logs (e.g. <c>not_configured</c>,
        /// <c>empty_description</c>, <c>http_403</c>, <c>HttpRequestException</c>). Empty on success.</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>The CMS id the backend returned (<c>bugReportId</c>), when present.</summary>
        public string ReportId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Posts a <see cref="FeedbackReport"/> (JSON + optional base64 screenshot) to the website API. Uses
    /// <see cref="HttpClient"/> rather than UnityWebRequest so the exact same code runs inside the Unity
    /// player AND inside the headless test suite, which points it at a local <see cref="System.Net.HttpListener"/>
    /// ("simulierte lokale Schnittstelle"). The call is synchronous (blocking); the Unity layer runs it on a
    /// background task and marshals the result back so it never stalls the game loop.
    ///
    /// The API key is a spam/abuse gate only, not a real secret (it ships inside the client and can be
    /// extracted) — see the requirements doc. The endpoint must therefore only accept feedback.
    /// </summary>
    public sealed class FeedbackUploader
    {
        /// <summary>Production endpoint (Wix/Velo HTTP function).</summary>
        public const string DefaultEndpoint = "https://www.blocksbeyondthestars.com/_functions/bugreport";

        /// <summary>Header carrying the spam-gate key (matches the Wix backend's <c>x-bugreport-key</c>).</summary>
        public const string ApiKeyHeader = "x-bugreport-key";

        /// <summary>Upper bound for the base64 screenshot length; larger shots are dropped (the report is
        /// still sent without an image) rather than rejected, mirroring the server-side <c>/bump</c> cap.</summary>
        public const int MaxScreenshotBase64Length = 2_000_000; // ~1.5 MB binary

        /// <summary>Hard cap on the description so a runaway paste can't make an oversized request.</summary>
        public const int MaxDescriptionLength = 5000;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly HttpClient _http;

        public FeedbackUploader(string? endpoint, string? apiKey, HttpClient? http = null)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint!.Trim();
            _apiKey = apiKey ?? string.Empty;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>False when no API key is configured (e.g. a local dev build) — the caller should then not
        /// attempt to send to production and can tell the player so.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>Builds the JSON body for a report (without sending). Exposed for tests and for an optional
        /// local-fallback file when the upload fails.</summary>
        public static string Serialize(FeedbackReport report, byte[]? screenshotJpg)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            AttachScreenshot(report, screenshotJpg);
            return JsonSerializer.Serialize(report, JsonOptions);
        }

        /// <summary>Attaches the screenshot to the report when present and within the size cap; otherwise leaves
        /// <see cref="FeedbackReport.Screenshot"/> null so the report is sent without an image.</summary>
        private static void AttachScreenshot(FeedbackReport report, byte[]? screenshotJpg)
        {
            if (screenshotJpg == null || screenshotJpg.Length == 0)
            {
                return;
            }

            string base64 = Convert.ToBase64String(screenshotJpg);
            if (base64.Length > MaxScreenshotBase64Length)
            {
                return; // too large — drop the image, keep the text report
            }

            report.Screenshot = new FeedbackScreenshot
            {
                FileName = string.IsNullOrWhiteSpace(report.ScreenshotFileName) ? "feedback.jpg" : report.ScreenshotFileName,
                MimeType = "image/jpeg",
                Base64 = base64,
            };
        }

        /// <summary>Sends the report. Blocking; returns a result rather than throwing. Safe to call off the
        /// Unity main thread.</summary>
        public FeedbackUploadResult Upload(FeedbackReport report, byte[]? screenshotJpg)
        {
            var result = new FeedbackUploadResult();

            if (!IsConfigured)
            {
                result.Error = "not_configured";
                return result;
            }

            if (report == null || string.IsNullOrWhiteSpace(report.Description))
            {
                result.Error = "empty_description";
                return result;
            }

            if (report.Description.Length > MaxDescriptionLength)
            {
                report.Description = report.Description.Substring(0, MaxDescriptionLength);
            }

            try
            {
                AttachScreenshot(report, screenshotJpg);
                string json = JsonSerializer.Serialize(report, JsonOptions);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);

#pragma warning disable VSTHRD002 // Runs on a background uploader thread (no SynchronizationContext) — cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                result.StatusCode = (int)response.StatusCode;
                result.Ok = response.IsSuccessStatusCode;

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                if (result.Ok)
                {
                    result.ReportId = TryReadReportId(body);
                }
                else
                {
                    result.Error = "http_" + result.StatusCode;
                }
            }
            catch (Exception ex)
            {
                // Offline, DNS failure, timeout, TLS error, … — never crash the game over a feedback send.
                result.Ok = false;
                result.Error = ex.GetType().Name;
            }

            return result;
        }

        /// <summary>Pulls <c>bugReportId</c> out of the backend's JSON response, tolerating any shape.</summary>
        private static string TryReadReportId(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("bugReportId", out var id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    return id.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // non-JSON body is fine; we just don't get an id
            }

            return string.Empty;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>The outcome of one catalogue fetch.</summary>
    public sealed class PlaytestFetchResult
    {
        /// <summary>The inbox answered (with a catalogue or "not modified").</summary>
        public bool Ok { get; set; }

        /// <summary>The catalogue had not changed since the last fetch.</summary>
        public bool NotModified { get; set; }

        public int StatusCode { get; set; }

        public string Error { get; set; } = string.Empty;

        /// <summary>The catalogue to show now — the fetched one, or the cached one when the fetch failed.</summary>
        public PlaytestCatalog Catalog { get; set; } = new PlaytestCatalog { Enabled = false };
    }

    /// <summary>One of this install's own playtest reports as the inbox knows it.</summary>
    public sealed class PlaytestOwnReport
    {
        public string ReportId { get; set; } = string.Empty;

        public string TaskId { get; set; } = string.Empty;

        public int Rev { get; set; }

        public string Status { get; set; } = string.Empty;

        /// <summary>The developers confirmed the report.</summary>
        public bool Confirmed { get; set; }

        public string FixedInVersion { get; set; } = string.Empty;

        public long CreatedUnix { get; set; }
    }

    /// <summary>
    /// Fetches the playtest catalogue from the report inbox and keeps the last answer on disk
    /// (<c>playtests/catalog.json</c>), so the Test Pilot page has something to show without a connection and the
    /// inbox is asked rarely: at most every <see cref="MinFetchSeconds"/> by itself, with the stored ETag, so the usual
    /// answer is "not modified". Same shape as <see cref="FeedbackReplyClient"/> — blocking <see cref="HttpClient"/>
    /// calls meant for a background task, the spam key in the same header. The request carries nothing about the
    /// player. Best-effort: a failed fetch is never an error the player sees — the cached catalogue stays.
    /// </summary>
    public sealed class PlaytestCatalogClient
    {
        /// <summary>Production route — sibling of <see cref="FeedbackUploader.DefaultEndpoint"/>.</summary>
        public const string DefaultEndpoint = "https://reports.blocksbeyondthestars.de/api/playtests";

        /// <summary>How long a fetched catalogue is good for before the client asks again by itself.</summary>
        public const long MinFetchSeconds = 6 * 3600;

        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _cachePath;
        private readonly HttpClient _http;
        private readonly object _gate = new object();

        /// <param name="cachePath">The cache file (typically <c>&lt;data root&gt;/playtests/catalog.json</c>); empty
        /// keeps nothing on disk.</param>
        public PlaytestCatalogClient(string? endpoint, string? apiKey, string? cachePath, HttpClient? http = null)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint!.Trim().TrimEnd('/');
            _apiKey = apiKey ?? string.Empty;
            _cachePath = cachePath ?? string.Empty;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>False without an API key (a local or forked build) — then nothing is fetched.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>Derives the catalogue endpoint from the ingest endpoint (<c>…/api/bugreport</c> →
        /// <c>…/api/playtests</c>), so whoever points the game at another inbox gets the catalogue from there too.</summary>
        public static string EndpointFor(string? ingestEndpoint)
        {
            if (string.IsNullOrWhiteSpace(ingestEndpoint))
            {
                return DefaultEndpoint;
            }

            string e = ingestEndpoint!.Trim();
            const string Ingest = "/api/bugreport";
            return e.EndsWith(Ingest, StringComparison.OrdinalIgnoreCase)
                ? e.Substring(0, e.Length - Ingest.Length) + "/api/playtests"
                : DefaultEndpoint;
        }

        /// <summary>The catalogue of the last successful fetch (disabled and empty when there was none), and when it
        /// was fetched.</summary>
        public (PlaytestCatalog Catalog, long FetchedUnix) Cached()
        {
            lock (_gate)
            {
                var cache = ReadCache();
                PlaytestCatalogJson.TryReadCatalog(cache.Json, out var catalog);
                return (catalog, cache.FetchedUnix);
            }
        }

        /// <summary>Whether a fetch is due: never without a key; otherwise when the caller insists (the player opened
        /// the page) or the cached catalogue is older than <see cref="MinFetchSeconds"/>.</summary>
        public bool ShouldFetch(long nowUnix, bool playerAsked)
        {
            if (!IsConfigured)
            {
                return false;
            }

            long fetched = Cached().FetchedUnix;
            return playerAsked ? nowUnix - fetched >= 60 || fetched > nowUnix : nowUnix - fetched >= MinFetchSeconds || fetched > nowUnix;
        }

        /// <summary>Asks the inbox for the catalogue (blocking — call it from a background task).</summary>
        public PlaytestFetchResult Fetch(long nowUnix)
        {
            var result = new PlaytestFetchResult();
            (string Json, string ETag, long FetchedUnix) cache;
            lock (_gate)
            {
                cache = ReadCache();
            }

            PlaytestCatalogJson.TryReadCatalog(cache.Json, out var cached);
            result.Catalog = cached;
            if (!IsConfigured)
            {
                result.Error = "not_configured";
                return result;
            }

            try
            {
                Request(result, cache.Json, cache.ETag, nowUnix);
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name;
            }

            return result;
        }

        /// <summary>The request itself: "not modified" keeps the cached catalogue and renews its date, a new catalogue
        /// replaces it, anything else leaves <paramref name="result"/> on the cached one with the error named.</summary>
        private void Request(PlaytestFetchResult result, string cachedJson, string cachedETag, long nowUnix)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.TryAddWithoutValidation(FeedbackUploader.ApiKeyHeader, _apiKey);
            if (cachedETag.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", cachedETag);
            }

#pragma warning disable VSTHRD002 // Runs on a background task (no SynchronizationContext) — cannot deadlock.
            using var response = _http.SendAsync(request).GetAwaiter().GetResult();
            result.StatusCode = (int)response.StatusCode;
            if (result.StatusCode == 304)
            {
                result.Ok = result.NotModified = true;
                Store(cachedJson, cachedETag, nowUnix);
                return;
            }

            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            if (!response.IsSuccessStatusCode)
            {
                result.Error = "http_" + result.StatusCode;
                return;
            }

            if (!PlaytestCatalogJson.TryReadCatalog(body, out var fetched))
            {
                result.Error = "invalid_catalog";
                return;
            }

            Store(body, response.Headers.ETag?.ToString() ?? string.Empty, nowUnix);
            result.Ok = true;
            result.Catalog = fetched;
        }

        /// <summary>This install's own playtest reports as the inbox knows them (blocking). Null when the inbox could
        /// not be asked — an empty list means "none".</summary>
        public List<PlaytestOwnReport>? FetchMine(string replyKey)
        {
            if (!IsConfigured || string.IsNullOrEmpty(replyKey))
            {
                return null;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint + "/mine?key=" + Uri.EscapeDataString(replyKey));
                request.Headers.TryAddWithoutValidation(FeedbackUploader.ApiKeyHeader, _apiKey);
#pragma warning disable VSTHRD002 // Runs on a background task (no SynchronizationContext) — cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return ParseMine(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Reads the answer of <c>/api/playtests/mine</c>; null when it is not the expected JSON.</summary>
        public static List<PlaytestOwnReport>? ParseMine(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json!);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var list = new List<PlaytestOwnReport>();
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    list.Add(new PlaytestOwnReport
                    {
                        ReportId = Str(item, "reportId"),
                        TaskId = Str(item, "taskId"),
                        Rev = (int)Int(item, "rev"),
                        Status = Str(item, "status"),
                        Confirmed = item.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True,
                        FixedInVersion = Str(item, "fixedInVersion"),
                        CreatedUnix = Int(item, "createdUnix"),
                    });
                }

                return list;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- the cache file

        private (string Json, string ETag, long FetchedUnix) ReadCache()
        {
            if (string.IsNullOrEmpty(_cachePath))
            {
                return (string.Empty, string.Empty, 0);
            }

            try
            {
                if (!File.Exists(_cachePath))
                {
                    return (string.Empty, string.Empty, 0);
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(_cachePath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("catalog", out var catalog)
                    || catalog.ValueKind != JsonValueKind.Object)
                {
                    return (string.Empty, string.Empty, 0);
                }

                return (catalog.GetRawText(), Str(root, "etag"), Int(root, "fetchedUnix"));
            }
            catch (Exception)
            {
                return (string.Empty, string.Empty, 0); // a broken cache is no cache
            }
        }

        private void Store(string catalogJson, string etag, long nowUnix)
        {
            if (string.IsNullOrEmpty(_cachePath) || string.IsNullOrWhiteSpace(catalogJson))
            {
                return;
            }

            lock (_gate)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(_cachePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using var stream = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(stream))
                    using (var catalog = JsonDocument.Parse(catalogJson))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("etag", etag);
                        writer.WriteNumber("fetchedUnix", nowUnix);
                        writer.WritePropertyName("catalog");
                        catalog.RootElement.WriteTo(writer);
                        writer.WriteEndObject();
                    }

                    string temp = _cachePath + ".tmp";
                    File.WriteAllBytes(temp, stream.ToArray());
                    if (File.Exists(_cachePath))
                    {
                        File.Delete(_cachePath);
                    }

                    File.Move(temp, _cachePath);
                }
                catch (Exception)
                {
                    // best-effort: without a cache the next start simply fetches again
                }
            }
        }

        private static string Str(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        private static long Int(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
    }
}

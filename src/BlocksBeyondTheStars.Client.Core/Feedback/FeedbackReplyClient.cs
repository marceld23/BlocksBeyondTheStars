// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>One entry of a report's reply thread as the inbox returns it.</summary>
    public sealed class FeedbackReplyEntry
    {
        public long Id { get; set; }

        /// <summary><c>dev</c> or <c>player</c>.</summary>
        public string Author { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool IsQuestion { get; set; }
        public long CreatedUnix { get; set; }
        public bool Seen { get; set; }

        public bool IsDev => Author == "dev";
    }

    /// <summary>A report of this install that has unread developer replies, with its whole thread.</summary>
    public sealed class FeedbackReplyThread
    {
        public string ReportId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string FixedInVersion { get; set; } = string.Empty;
        public long CreatedUnix { get; set; }
        public List<FeedbackReplyEntry> Replies { get; } = new List<FeedbackReplyEntry>();

        /// <summary>The developer entries the client must acknowledge once it showed them.</summary>
        public List<long> UnseenIds { get; } = new List<long>();

        /// <summary>True when the player can answer from the game: the newest entry of the thread is a
        /// developer question (nothing from the player after it). Derived from the thread alone, NOT from
        /// <see cref="Status"/> (#1369): an operator who flips the status by hand to <c>triaged</c> while the
        /// question is still open used to make the answer box vanish — and the inbox accepts an answer in
        /// any status as long as a developer entry exists.</summary>
        public bool AwaitsAnswer
        {
            get
            {
                if (Replies.Count == 0)
                {
                    return false;
                }

                var last = Replies[Replies.Count - 1];
                return last.IsDev && last.IsQuestion; // a player entry after the question means it was answered
            }
        }
    }

    /// <summary>Outcome of one call against the reply routes; never throws back into the game.</summary>
    public sealed class FeedbackReplyResult
    {
        public bool Ok { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Threads returned by <see cref="FeedbackReplyClient.Fetch"/> (empty for the other calls).</summary>
        public List<FeedbackReplyThread> Threads { get; } = new List<FeedbackReplyThread>();

        /// <summary>Of the report ids the poll asked about, the ones the inbox no longer has for this key
        /// (deleted, pruned, or never readable with it) — the caller forgets them (#1369).</summary>
        public List<string> Gone { get; } = new List<string>();
    }

    /// <summary>
    /// The pull side of the feedback channel (#1328): asks the report inbox for developer answers to this
    /// install's reports, acknowledges the ones shown, and posts the player's answer to a question. Same
    /// shape as <see cref="FeedbackUploader"/> — blocking <see cref="HttpClient"/> calls meant for a
    /// background task, testable against a local <see cref="System.Net.HttpListener"/>; the WebGL player uses
    /// the static body/parse helpers with its own UnityWebRequest transport.
    /// </summary>
    public sealed class FeedbackReplyClient
    {
        /// <summary>Production route — sibling of <see cref="FeedbackUploader.DefaultEndpoint"/>.</summary>
        public const string DefaultEndpoint = "https://reports.blocksbeyondthestars.de/api/replies";

        /// <summary>Same cap as a report description (the inbox trims beyond it).</summary>
        public const int MaxAnswerLength = FeedbackUploader.MaxDescriptionLength;

        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly HttpClient _http;

        public FeedbackReplyClient(string? endpoint, string? apiKey, HttpClient? http = null)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint!.Trim().TrimEnd('/');
            _apiKey = apiKey ?? string.Empty;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        /// <summary>False without an API key (dev builds) — then nothing is polled.</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <summary>Derives the replies endpoint from the ingest endpoint (<c>…/api/bugreport</c> →
        /// <c>…/api/replies</c>) so a self-hoster who overrides one gets the other for free.</summary>
        public static string EndpointFor(string ingestEndpoint)
        {
            if (string.IsNullOrWhiteSpace(ingestEndpoint))
            {
                return DefaultEndpoint;
            }

            string e = ingestEndpoint.Trim();
            const string Ingest = "/api/bugreport";
            return e.EndsWith(Ingest, StringComparison.OrdinalIgnoreCase)
                ? e.Substring(0, e.Length - Ingest.Length) + "/api/replies"
                : DefaultEndpoint;
        }

        // ---------------- Wire helpers (shared with the WebGL transport) ----------------

        /// <summary>Upper bound on the remembered ids one poll names — matches the inbox's cap.</summary>
        public const int MaxKnownIds = 50;

        /// <summary>The poll URL for a key: <c>{endpoint}?key=…&amp;since=…</c>, plus <c>&amp;ids=a,b,c</c> when the
        /// caller names the report ids it still remembers (#1369) — the inbox answers with the ones it no
        /// longer has for this key as <c>gone</c>.</summary>
        public string FetchUrl(string replyKey, long sinceUnix, IEnumerable<string>? knownReportIds = null)
        {
            var sb = new StringBuilder(_endpoint)
                .Append("?key=").Append(Uri.EscapeDataString(replyKey ?? string.Empty))
                .Append("&since=").Append(Math.Max(0, sinceUnix));

            if (knownReportIds != null)
            {
                var ids = new List<string>();
                foreach (string id in knownReportIds)
                {
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id) && ids.Count < MaxKnownIds)
                    {
                        ids.Add(id);
                    }
                }

                if (ids.Count > 0)
                {
                    sb.Append("&ids=").Append(Uri.EscapeDataString(string.Join(",", ids)));
                }
            }

            return sb.ToString();
        }

        public string AckUrl => _endpoint + "/ack";

        public string AnswerUrl => _endpoint;

        public static string AckBody(string replyKey, IEnumerable<long> replyIds)
            => JsonSerializer.Serialize(new { key = replyKey ?? string.Empty, replyIds = new List<long>(replyIds) });

        public static string AnswerBody(string replyKey, string reportId, string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length > MaxAnswerLength)
            {
                text = text.Substring(0, MaxAnswerLength);
            }

            return JsonSerializer.Serialize(new { key = replyKey ?? string.Empty, reportId = reportId ?? string.Empty, text });
        }

        /// <summary>Parses the poll response (<c>{ items: [ … ] }</c>) tolerantly — anything malformed yields an
        /// empty list rather than an exception.</summary>
        public static List<FeedbackReplyThread> ParseThreads(string? json)
        {
            var threads = new List<FeedbackReplyThread>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return threads;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    return threads;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var thread = new FeedbackReplyThread
                    {
                        ReportId = Str(item, "reportId"),
                        Title = Str(item, "title"),
                        Status = Str(item, "status"),
                        FixedInVersion = Str(item, "fixedInVersion"),
                        CreatedUnix = Int(item, "createdUnix"),
                    };
                    if (thread.ReportId.Length == 0)
                    {
                        continue;
                    }

                    if (item.TryGetProperty("replies", out var replies) && replies.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in replies.EnumerateArray())
                        {
                            if (r.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            thread.Replies.Add(new FeedbackReplyEntry
                            {
                                Id = Int(r, "id"),
                                Author = Str(r, "author"),
                                Text = Str(r, "text"),
                                IsQuestion = r.TryGetProperty("isQuestion", out var q) && q.ValueKind == JsonValueKind.True,
                                CreatedUnix = Int(r, "createdUnix"),
                                Seen = r.TryGetProperty("seen", out var s) && s.ValueKind == JsonValueKind.True,
                            });
                        }
                    }

                    if (item.TryGetProperty("unseenIds", out var unseen) && unseen.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var u in unseen.EnumerateArray())
                        {
                            if (u.ValueKind == JsonValueKind.Number && u.TryGetInt64(out long id))
                            {
                                thread.UnseenIds.Add(id);
                            }
                        }
                    }

                    threads.Add(thread);
                }
            }
            catch
            {
                // malformed body — treat as "nothing new"
            }

            return threads;
        }

        /// <summary>Parses the poll response's <c>gone</c> list (#1369) — the report ids the inbox no longer
        /// has for this key. Tolerant like <see cref="ParseThreads"/>: anything malformed yields an empty
        /// list, so an older inbox without the field forgets nothing.</summary>
        public static List<string> ParseGone(string? json)
        {
            var gone = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return gone;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("gone", out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    return gone;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(item.GetString()))
                    {
                        gone.Add(item.GetString()!);
                    }
                }
            }
            catch
            {
                // malformed body — nothing is gone
            }

            return gone;
        }

        // ---------------- Blocking calls (desktop; run on a background task) ----------------

        /// <summary>Asks for threads with unread developer entries; <paramref name="knownReportIds"/> (the
        /// reports this install still remembers) come back as <see cref="FeedbackReplyResult.Gone"/> when the
        /// inbox no longer has them. Blocking; never throws.</summary>
        public FeedbackReplyResult Fetch(string replyKey, long sinceUnix = 0, IEnumerable<string>? knownReportIds = null)
        {
            var result = new FeedbackReplyResult();
            if (!Preflight(result, replyKey))
            {
                return result;
            }

            Send(result, HttpMethod.Get, FetchUrl(replyKey, sinceUnix, knownReportIds), null, body =>
            {
                result.Threads.AddRange(ParseThreads(body));
                result.Gone.AddRange(ParseGone(body));
            });
            return result;
        }

        /// <summary>Marks developer entries as read. Blocking; never throws.</summary>
        public FeedbackReplyResult Ack(string replyKey, IEnumerable<long> replyIds)
        {
            var result = new FeedbackReplyResult();
            if (!Preflight(result, replyKey))
            {
                return result;
            }

            Send(result, HttpMethod.Post, AckUrl, AckBody(replyKey, replyIds), null);
            return result;
        }

        /// <summary>Posts the player's answer to a developer question. Blocking; never throws.</summary>
        public FeedbackReplyResult Answer(string replyKey, string reportId, string text)
        {
            var result = new FeedbackReplyResult();
            if (!Preflight(result, replyKey))
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Error = "empty_text";
                return result;
            }

            Send(result, HttpMethod.Post, AnswerUrl, AnswerBody(replyKey, reportId, text), null);
            return result;
        }

        private bool Preflight(FeedbackReplyResult result, string replyKey)
        {
            if (!IsConfigured)
            {
                result.Error = "not_configured";
                return false;
            }

            if (string.IsNullOrEmpty(replyKey))
            {
                result.Error = "no_key";
                return false;
            }

            return true;
        }

        private void Send(FeedbackReplyResult result, HttpMethod method, string url, string? jsonBody, Action<string>? onBody)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (jsonBody != null)
                {
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                request.Headers.TryAddWithoutValidation(FeedbackUploader.ApiKeyHeader, _apiKey);

#pragma warning disable VSTHRD002 // Runs on a background task (no SynchronizationContext) — cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                result.StatusCode = (int)response.StatusCode;
                result.Ok = response.IsSuccessStatusCode;
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                if (result.Ok)
                {
                    onBody?.Invoke(body);
                }
                else
                {
                    result.Error = "http_" + result.StatusCode;
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.GetType().Name;
            }
        }

        private static string Str(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        private static long Int(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
    }
}

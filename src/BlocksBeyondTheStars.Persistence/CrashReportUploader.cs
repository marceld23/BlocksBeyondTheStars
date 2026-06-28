// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Net.Http;
using System.Text;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>Where a pending crash report is delivered. Abstracted so <see cref="CrashReportWriter.FlushPending"/>
/// can be driven by a fake in tests, and so the disk queue stays independent of the transport.</summary>
public interface ICrashReportSink
{
    /// <summary>False when no endpoint/key is configured — the queue then stays on disk untouched (a
    /// self-hosted server opts in; official builds inject the key). The flush is a no-op while false.</summary>
    bool IsConfigured { get; }

    /// <summary>Delivers one report's raw JSON. Returns true only when the remote accepted it (so the file may
    /// be marked sent). Must NOT throw — a transport failure returns false and the file stays queued.</summary>
    bool Send(string json);
}

/// <summary>
/// Posts a crash report's raw JSON to the website endpoint, mirroring <c>FeedbackUploader</c> but for the
/// server's automatic crash pipeline (the player-facing feedback uploader lives in the netstandard
/// <c>Client.Core</c> assembly, which the .NET server can't reference — hence this small sibling here).
/// Endpoint + key come from server config and are EMPTY by default, so nothing is auto-sent unless a host
/// opts in; the on-disk reports remain the source of truth either way. Every call is best-effort and never
/// throws back into the flush loop.
/// </summary>
public sealed class CrashReportUploader : ICrashReportSink
{
    /// <summary>Header carrying the spam-gate key (matches the website's <c>x-bugreport-key</c>, shared with
    /// the player-feedback path).</summary>
    public const string ApiKeyHeader = "x-bugreport-key";

    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public CrashReportUploader(string? endpoint, string? apiKey, HttpClient? http = null)
    {
        _endpoint = (endpoint ?? string.Empty).Trim();
        _apiKey = apiKey ?? string.Empty;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Both an endpoint and a key are required; either missing leaves uploading disabled.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_apiKey);

    public bool Send(string json)
    {
        if (!IsConfigured || string.IsNullOrEmpty(json))
        {
            return false;
        }

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);

#pragma warning disable VSTHRD002 // Runs on a background flush thread (no SynchronizationContext) — cannot deadlock.
            using var response = _http.SendAsync(request).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false; // offline / DNS / timeout / TLS — leave the report queued for the next flush
        }
    }
}

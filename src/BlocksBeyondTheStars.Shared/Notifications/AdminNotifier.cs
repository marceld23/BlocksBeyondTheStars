// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BlocksBeyondTheStars.Shared.Notifications;

/// <summary>
/// Fire-and-forget operator push notifications (issue #938): one plain <c>POST</c> of a short text
/// body with <c>Title</c>/<c>Tags</c> headers to a configured URL. That is exactly the ntfy publish
/// contract, so an operator points the URL at any ntfy topic (self-hosted or ntfy.sh) and gets phone
/// pushes — and most generic webhook receivers accept the same shape. Everything about it is
/// best-effort by design: no retry, no queue, errors swallowed after one attempt — a moderation ping
/// must never take a game tick or an HTTP request down with it. Unconfigured (empty URL) = disabled,
/// matching the crash-uploader's deliberate no-phone-home default.
/// </summary>
public sealed class AdminNotifier
{
    /// <summary>Shared client: notifications are rare one-shot posts; 10 s is generous for a push
    /// gateway and bounds how long a stuck endpoint can pin the background task.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _url;
    private readonly string _source;

    /// <summary>The <paramref name="source"/> prefixes every title ("[worldhost] …") so one topic can
    /// carry all services and still read unambiguously on the phone.</summary>
    public AdminNotifier(string? url, string? source)
    {
        _url = (url ?? string.Empty).Trim();
        _source = (source ?? string.Empty).Trim();
    }

    public bool IsConfigured => _url.Length > 0;

    /// <summary>Posts one notification on a background task and returns immediately. <paramref
    /// name="tags"/> is the optional ntfy tag list (comma-separated emoji shortcodes, e.g.
    /// "triangular_flag_on_post"); other receivers simply ignore the header.</summary>
    public void Post(string title, string message, string tags = "")
    {
        if (!IsConfigured)
        {
            return;
        }

        string headerTitle = HeaderValue(_source.Length > 0 ? $"[{_source}] {title}" : title, 140);
        string headerTags = HeaderValue(tags, 100);
        string body = message ?? string.Empty;
        if (body.Length > 1000)
        {
            body = body.Substring(0, 1000);
        }

        string url = _url;
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain"),
                };
                if (headerTitle.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("Title", headerTitle);
                }

                if (headerTags.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("Tags", headerTags);
                }

                using var response = await Http.SendAsync(request).ConfigureAwait(false);
                // Status deliberately unchecked: one best-effort attempt, nothing to do with a failure.
            }
            catch
            {
                // Never let a notification failure surface anywhere — the log/DB row upstream is the
                // source of truth, this is only the ping.
            }
        });
    }

    /// <summary>HTTP header values must be single-line ASCII: anything else (umlauts in a world name,
    /// a newline smuggled into a player name) is replaced rather than trusted, and the result is
    /// length-capped. Public for tests.</summary>
    public static string HeaderValue(string? value, int max)
    {
        var sb = new StringBuilder();
        foreach (char c in (value ?? string.Empty).Trim())
        {
            if (sb.Length >= max)
            {
                break;
            }

            sb.Append(c is >= ' ' and <= '~' ? c : '?');
        }

        return sb.ToString();
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using BlocksBeyondTheStars.Shared.Notifications;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ReportHost over real HTTP: the same <see cref="ReportHostApp"/> the container runs, started
/// in-process on a loopback port. Covers the player reply routes end to end (issue #1327 had only
/// store-level tests) and the limiter split of issue #1352 — polling for answers must never spend the
/// per-IP budget a real F1 report from the same NAT needs.
/// </summary>
public sealed class ReportHostHttpTests : IAsyncLifetime, IDisposable
{
    private const string WriteKey = "write-key-for-tests";
    private const int IngestPerMinute = 2;
    private const int ReplyPerMinute = 5;

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rhh_" + Guid.NewGuid().ToString("N"));
    private ReportStore _store = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _disposed;

    public async Task InitializeAsync()
    {
        System.IO.Directory.CreateDirectory(_root);
        var config = new ReportHostConfig
        {
            BindAddress = "127.0.0.1",
            Port = 0, // Kestrel picks a free port; the bound address is read back from app.Urls
            DataDir = _root,
            WriteKey = WriteKey,
            IngestPerMinute = IngestPerMinute,
            ReplyPerMinute = ReplyPerMinute,
        };
        _store = new ReportStore(config, System.IO.Path.Combine(_root, "reports.db"));
        _app = ReportHostApp.Create(config, _store, new AdminNotifier(string.Empty, "reports"), Array.Empty<string>());
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Dispose();
    }

    /// <summary>xunit drives <see cref="DisposeAsync"/>; this is the synchronous half (client, store,
    /// temp dir) it ends with — idempotent, so an extra call is harmless.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
        _store.Dispose();
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }

    // ---------------- helpers ----------------

    /// <summary>A reply key as a client derives it — the inbox back-fills the same formula from a
    /// report's playerId when the payload carries none.</summary>
    private static string KeyFor(string secret) => FeedbackReplyKey.Derive(secret);

    private static string FeedbackPayload(string playerId, string title = "Hat eaten by door") => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["title"] = title,
        ["description"] = "The door on my ship eats my hat.",
        ["email"] = "",
        ["gameVersion"] = "2026.8.22",
        ["playerId"] = playerId,
        ["playerName"] = "Justus",
        ["platform"] = "WindowsPlayer",
        ["clientTimestamp"] = "2026-08-29T12:00:00Z",
        ["reportJson"] = new Dictionary<string, object?> { ["scene"] = "planet" },
    });

    private HttpRequestMessage Request(HttpMethod method, string path, string? json = null, string? writeKey = WriteKey)
    {
        var request = new HttpRequestMessage(method, path);
        if (writeKey != null)
        {
            request.Headers.Add("x-bugreport-key", writeKey);
        }

        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<HttpStatusCode> PollAsync(string key)
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key));
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, JsonDocument? Body)> PostReportAsync(string playerId)
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/bugreport", FeedbackPayload(playerId)));
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    // ---------------- #1352: polling never spends the ingest budget ----------------

    [Fact]
    public async Task ThirtyInstallsBehindOneIp_AllPoll_AndAReportStillGetsThroughAsync()
    {
        // A school class: 30 installs, 30 reply keys, one NAT address — every poll is answered.
        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 30; i++)
        {
            statuses.Add(await PollAsync(KeyFor("install-" + i)));
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));

        // A real F1 report from that same address in the same minute is accepted: the polls did not
        // touch the per-IP ingest limiter (budget 2 here).
        var (first, body) = await PostReportAsync("install-7");
        Assert.Equal(HttpStatusCode.OK, first);
        Assert.True(body!.RootElement.GetProperty("ok").GetBoolean());

        // …and the ingest limiter itself is still on: within 2 × budget + 1 posts one must be a 429,
        // even if a minute boundary inside the loop resets the window once.
        var posts = new List<HttpStatusCode>();
        for (int i = 0; i < 2 * IngestPerMinute; i++)
        {
            posts.Add((await PostReportAsync("install-7")).Status);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, posts);
    }

    [Fact]
    public async Task ReplyLimiter_IsPerKey_NotPerIpAsync()
    {
        // One key hammering the poll runs into its own budget (5/min here) — within 2 × budget + 2
        // requests even if a minute boundary resets the window once — …
        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 2 * ReplyPerMinute + 2; i++)
        {
            statuses.Add(await PollAsync(KeyFor("chatty")));
        }

        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // …while another install on the same IP is unaffected.
        Assert.Equal(HttpStatusCode.OK, await PollAsync(KeyFor("quiet")));

        // A malformed key is rejected before it can occupy a counter.
        Assert.Equal(HttpStatusCode.BadRequest, await PollAsync("not-a-key"));
    }

    // ---------------- reply routes end to end ----------------

    [Fact]
    public async Task ReplyRoutes_ServeAckAndAcceptAnAnswer_OverHttpAsync()
    {
        var (status, posted) = await PostReportAsync("token-abc");
        Assert.Equal(HttpStatusCode.OK, status);
        string reportId = posted!.RootElement.GetProperty("bugReportId").GetString()!;
        string key = KeyFor("token-abc");

        // Nothing to show until a developer writes something.
        using (var empty = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var doc = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        long replyId = _store.AddDevReply(reportId, "Does it happen always?", isQuestion: true, nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // The poll returns the thread with the unread developer entry…
        using (var poll = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
            Assert.Equal("*", poll.Headers.GetValues("Access-Control-Allow-Origin").Single()); // WebGL client
            using var doc = JsonDocument.Parse(await poll.Content.ReadAsStringAsync());
            var item = doc.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.Equal(reportId, item.GetProperty("reportId").GetString());
            Assert.Equal(replyId, item.GetProperty("unseenIds").EnumerateArray().Single().GetInt64());
            Assert.True(item.GetProperty("replies").EnumerateArray().Single().GetProperty("isQuestion").GetBoolean());
        }

        // …the ack marks it read, so the next poll is empty again…
        string ack = JsonSerializer.Serialize(new { key, replyIds = new[] { replyId } });
        using (var acked = await _client.SendAsync(Request(HttpMethod.Post, "/api/replies/ack", ack)))
        {
            Assert.Equal(HttpStatusCode.OK, acked.StatusCode);
            using var doc = JsonDocument.Parse(await acked.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("acked").GetInt32());
        }

        using (var again = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            using var doc = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        // …and the player's answer lands in the thread and flips the status.
        string answer = JsonSerializer.Serialize(new { key, reportId, text = "Yes, every time." });
        using (var answered = await _client.SendAsync(Request(HttpMethod.Post, "/api/replies", answer)))
        {
            Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        }

        Assert.Equal(BugReportStatus.PlayerReplied, _store.Get(reportId)!.Status);
        Assert.Equal(2, _store.ListReplies(reportId).Count);

        // Another install's key sees none of it.
        using (var foreign = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + KeyFor("someone-else"))))
        {
            using var doc = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task ReplyRoutes_StillRequireTheWriteKeyAsync()
    {
        string key = KeyFor("token-abc");
        using var wrong = await _client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key, writeKey: "nope"));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        using var missing = await _client.SendAsync(Request(HttpMethod.Post, "/api/replies/ack", "{}", writeKey: null));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }
}

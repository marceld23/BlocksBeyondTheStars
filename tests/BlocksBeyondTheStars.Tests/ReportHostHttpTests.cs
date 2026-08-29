// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using BlocksBeyondTheStars.Shared.Notifications;
using Microsoft.AspNetCore.Builder;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// One in-process ReportHost for the whole <see cref="ReportHostHttpTests"/> class — the host's
/// <c>Create</c> is the one genuinely expensive step (~1 s on the runners: builder, DI container, route
/// table, SQLite open), so it is paid once per class instead of once per test.
/// </summary>
public sealed class ReportHostHttpFixture : IAsyncLifetime
{
    public const string WriteKey = "write-key-for-tests";
    public const int IngestPerMinute = 2;
    public const int ReplyPerMinute = 5;

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rhh_" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;

    public ReportStore Store { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Milliseconds <see cref="ReportHostApp.Create"/> took (builder + store start-up work).</summary>
    public long CreateMs { get; private set; }

    /// <summary>Milliseconds <c>StartAsync</c> took (Kestrel bind + host start).</summary>
    public long StartMs { get; private set; }

    /// <summary>Milliseconds the process's first HTTP request itself took, measured off xunit's
    /// synchronization context (see <see cref="SendMeasuredAsync"/>).</summary>
    public long WarmupMs { get; private set; }

    /// <summary>Milliseconds that same request then waited to be let back onto an xunit worker thread.
    /// This is the number that reached 82 s in #1362; with <see cref="RealTimeSensitiveCollection"/>
    /// keeping the class out of the parallel queue it is ~0, and a regression shows up here first.</summary>
    public long WarmupQueueWaitMs { get; private set; }

    /// <summary>Status of that warm-up request (200 when the host answered a well-formed poll).</summary>
    public int WarmupStatus { get; private set; }

    /// <summary>
    /// Sends a request and reports how long the REQUEST took, measured on the thread the response arrived
    /// on. The <c>ConfigureAwait(false)</c> is the point: it stops the stopwatch before the continuation is
    /// handed back to xunit's scheduler, so the caller can see the two costs apart (#1362 — while they were
    /// one number, a 1 ms request looked like 82 s).
    /// </summary>
    public static async Task<(HttpResponseMessage Response, long RequestMs)> SendMeasuredAsync(HttpClient client, HttpRequestMessage request)
    {
        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(request).ConfigureAwait(false);
        return (response, sw.ElapsedMilliseconds);
    }

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
            // The per-IP ingest limiter reads X-Forwarded-For when the proxy is trusted — every test sends
            // its own address, so the tests share one host without sharing its ingest budget.
            TrustProxy = true,
        };
        Store = new ReportStore(config, System.IO.Path.Combine(_root, "reports.db"));

        var sw = Stopwatch.StartNew();
        _app = ReportHostApp.Create(config, Store, new AdminNotifier(string.Empty, "reports"), Array.Empty<string>());
        CreateMs = sw.ElapsedMilliseconds;

        sw.Restart();
        await _app.StartAsync();
        StartMs = sw.ElapsedMilliseconds;

        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };

        // The process's first request also warms the HTTP stack (JIT, socket engine, route table), so it is
        // made here rather than on some test's clock. Its two costs — the request, and the wait for an
        // xunit worker thread afterwards — are recorded apart for the CI log (#1362).
        sw.Restart();
        using var warmup = new HttpRequestMessage(HttpMethod.Get, "/api/replies?key=" + FeedbackReplyKey.Derive("fixture-warm-up"));
        warmup.Headers.Add("x-bugreport-key", WriteKey);
        warmup.Headers.Add("X-Forwarded-For", "10.0.0.0");
        var (warmupResponse, requestMs) = await SendMeasuredAsync(Client, warmup);
        using (warmupResponse)
        {
            WarmupMs = requestMs;
            WarmupQueueWaitMs = Math.Max(0, sw.ElapsedMilliseconds - requestMs);
            WarmupStatus = (int)warmupResponse.StatusCode;
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose(); // close the keep-alive connection before the host stops, so the stop never waits for it
        await _app.StopAsync();
        await _app.DisposeAsync();
        Store.Dispose();
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }
}

/// <summary>
/// The ReportHost over real HTTP: the same <see cref="ReportHostApp"/> the container runs, started
/// in-process on a loopback port. Covers the player reply routes end to end (issue #1327 had only
/// store-level tests) and the limiter split of issue #1352 — polling for answers must never spend the
/// per-IP budget a real F1 report from the same NAT needs.
///
/// <para>The class belongs in <see cref="RealTimeSensitiveCollection"/> and must stay there: #1362 was
/// this class missing it. Its loopback round trips are 1 ms of work whose continuation, in the parallel
/// suite, waits behind every collection that has not started yet — 34 s, 82 s and 134 s were measured,
/// and the 134 s failed the fast-tier duration guardrail on a test whose two requests were plain
/// 403s.</para>
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // 1 ms loopback requests, billed at up to 134 s in the parallel queue (#1362)
public sealed class ReportHostHttpTests : IClassFixture<ReportHostHttpFixture>
{
    private const string WriteKey = ReportHostHttpFixture.WriteKey;
    private const int IngestPerMinute = ReportHostHttpFixture.IngestPerMinute;
    private const int ReplyPerMinute = ReportHostHttpFixture.ReplyPerMinute;

    private static int _nextClientIp;

    private readonly ReportHostHttpFixture _host;
    private readonly ITestOutputHelper _output;
    private readonly string _clientIp = "10.0.0." + Interlocked.Increment(ref _nextClientIp); // one NAT address per test

    public ReportHostHttpTests(ReportHostHttpFixture host, ITestOutputHelper output)
    {
        _host = host;
        _output = output;
    }

    private ReportStore Store => _host.Store;

    private static int _processRequests;

    /// <summary>Every request of the class goes through here: it logs the request's own time and, next to
    /// it, the time the continuation then waited for an xunit worker thread. Keeping the two apart is what
    /// #1362 was missing — one stopwatch around <c>SendAsync</c> reported 82 s for a 1 ms request.</summary>
    private async Task<HttpResponseMessage> TimedSendAsync(HttpRequestMessage request)
    {
        int seq = Interlocked.Increment(ref _processRequests);
        var sw = Stopwatch.StartNew();
        var (response, requestMs) = await ReportHostHttpFixture.SendMeasuredAsync(_host.Client, request);
        long queueMs = Math.Max(0, sw.ElapsedMilliseconds - requestMs);
        string flag = requestMs >= 1000 || queueMs >= 1000 ? "SLOW " : string.Empty;
        _output.WriteLine($"{flag}request #{seq}: {request.Method} {request.RequestUri} → {(int)response.StatusCode} in {requestMs} ms (+{queueMs} ms xunit queue)");
        return response;
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
        request.Headers.Add("X-Forwarded-For", _clientIp);
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
        using var response = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key));
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, JsonDocument? Body)> PostReportAsync(string playerId)
    {
        using var response = await TimedSendAsync(Request(HttpMethod.Post, "/api/bugreport", FeedbackPayload(playerId)));
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    // ---------------- host start-up cost (#1362) ----------------

    [Fact]
    public async Task HostStartup_IsReportedForTheCiLogAsync()
    {
        Assert.Equal(HttpStatusCode.OK, await PollAsync(KeyFor("startup-probe")));
        _output.WriteLine($"ReportHost host: Create {_host.CreateMs} ms, StartAsync {_host.StartMs} ms, "
            + $"process-first request {_host.WarmupMs} ms → {_host.WarmupStatus}, xunit queue wait {_host.WarmupQueueWaitMs} ms");

        // The regression guard for #1362: take this class out of RealTimeSensitiveCollection and the
        // fixture's first request is posted behind every collection that has not started yet again, so
        // this wait goes back to tens of seconds — which is how a 1 ms request once cost a test 134 s and
        // failed the fast-tier guardrail. The threshold sits far above any real scheduling hiccup and far
        // below the values the bug produced.
        Assert.True(_host.WarmupQueueWaitMs < 20_000,
            $"The fixture's first request waited {_host.WarmupQueueWaitMs} ms for an xunit worker thread — "
            + $"this class must stay in the {RealTimeSensitiveCollection.Name} collection (#1362).");
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
        using (var empty = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var doc = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        long replyId = Store.AddDevReply(reportId, "Does it happen always?", isQuestion: true, nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // The poll returns the thread with the unread developer entry…
        using (var poll = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
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
        using (var acked = await TimedSendAsync(Request(HttpMethod.Post, "/api/replies/ack", ack)))
        {
            Assert.Equal(HttpStatusCode.OK, acked.StatusCode);
            using var doc = JsonDocument.Parse(await acked.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("acked").GetInt32());
        }

        using (var again = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            using var doc = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        // …and the player's answer lands in the thread and flips the status.
        string answer = JsonSerializer.Serialize(new { key, reportId, text = "Yes, every time." });
        using (var answered = await TimedSendAsync(Request(HttpMethod.Post, "/api/replies", answer)))
        {
            Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        }

        Assert.Equal(BugReportStatus.PlayerReplied, Store.Get(reportId)!.Status);
        Assert.Equal(2, Store.ListReplies(reportId).Count);

        // Another install's key sees none of it.
        using (var foreign = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + KeyFor("someone-else"))))
        {
            using var doc = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task ReplyRoutes_StillRequireTheWriteKeyAsync()
    {
        string key = KeyFor("token-abc");
        using var wrong = await TimedSendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key, writeKey: "nope"));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        using var missing = await TimedSendAsync(Request(HttpMethod.Post, "/api/replies/ack", "{}", writeKey: null));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }
}

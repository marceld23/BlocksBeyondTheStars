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
/// One in-process ReportHost for the whole <see cref="ReportHostHttpTests"/> class. The first Kestrel
/// host a test process starts costs 84–134 s on the Linux CI runners (under a second locally), which
/// blew the fast-tier budget when every test started its own host and the cost landed on whichever test
/// ran first (#1362). Starting the host once per class pays that price once, outside any test's clock;
/// the timings are kept so <see cref="ReportHostHttpTests.HostStartup_IsReportedForTheCiLogAsync"/> can put
/// them into the test log where the CI artifact keeps them.
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
/// </summary>
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

    private HttpClient Client => _host.Client;

    private ReportStore Store => _host.Store;

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
        using var response = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key));
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, JsonDocument? Body)> PostReportAsync(string playerId)
    {
        using var response = await Client.SendAsync(Request(HttpMethod.Post, "/api/bugreport", FeedbackPayload(playerId)));
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
    }

    // ---------------- host start-up cost (#1362) ----------------

    [Fact]
    public async Task HostStartup_IsReportedForTheCiLogAsync()
    {
        // No budget assertion — the point is the number in the log: the first Kestrel host of a test
        // process takes 84–134 s on the Linux CI runners and under a second locally, and the cause is
        // still open (#1362). One more request here so the first-request cost is on record too.
        var sw = Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, await PollAsync(KeyFor("startup-probe")));
        _output.WriteLine($"ReportHost host: Create {_host.CreateMs} ms, StartAsync {_host.StartMs} ms, first request {sw.ElapsedMilliseconds} ms");
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
        using (var empty = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using var doc = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        long replyId = Store.AddDevReply(reportId, "Does it happen always?", isQuestion: true, nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // The poll returns the thread with the unread developer entry…
        using (var poll = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
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
        using (var acked = await Client.SendAsync(Request(HttpMethod.Post, "/api/replies/ack", ack)))
        {
            Assert.Equal(HttpStatusCode.OK, acked.StatusCode);
            using var doc = JsonDocument.Parse(await acked.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("acked").GetInt32());
        }

        using (var again = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key)))
        {
            using var doc = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }

        // …and the player's answer lands in the thread and flips the status.
        string answer = JsonSerializer.Serialize(new { key, reportId, text = "Yes, every time." });
        using (var answered = await Client.SendAsync(Request(HttpMethod.Post, "/api/replies", answer)))
        {
            Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        }

        Assert.Equal(BugReportStatus.PlayerReplied, Store.Get(reportId)!.Status);
        Assert.Equal(2, Store.ListReplies(reportId).Count);

        // Another install's key sees none of it.
        using (var foreign = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + KeyFor("someone-else"))))
        {
            using var doc = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task ReplyRoutes_StillRequireTheWriteKeyAsync()
    {
        string key = KeyFor("token-abc");
        using var wrong = await Client.SendAsync(Request(HttpMethod.Get, "/api/replies?key=" + key, writeKey: "nope"));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        using var missing = await Client.SendAsync(Request(HttpMethod.Post, "/api/replies/ack", "{}", writeKey: null));
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }
}

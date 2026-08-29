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
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using BlocksBeyondTheStars.Shared.Notifications;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The poll's "gone" half (#1369) over real HTTP, on the shared <see cref="ReportHostHttpFixture"/> host:
/// a client names the report ids it still remembers, and the inbox returns the ones this key can no
/// longer read (deleted, pruned, never its own) so the client forgets them instead of polling for up to
/// 90 days for a report that no longer exists.
/// </summary>
public sealed class ReportHostReplyLifecycleTests : IClassFixture<ReportHostHttpFixture>
{
    private static int _nextClientIp;

    private readonly ReportHostHttpFixture _host;
    private readonly string _clientIp = "10.0.1." + Interlocked.Increment(ref _nextClientIp); // own NAT address, own ingest budget

    public ReportHostReplyLifecycleTests(ReportHostHttpFixture host)
    {
        _host = host;
    }

    private static string KeyFor(string secret) => FeedbackReplyKey.Derive(secret);

    private string AddReport(string playerId)
    {
        var parsed = ReportIngest.Parse(ReportHostTestPayloads.Feedback(playerId), new ReportHostConfig(), out string error);
        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, error);
        return _host.Store.Add(parsed!, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private async Task<JsonDocument> PollAsync(string key, IEnumerable<string>? ids = null)
    {
        string url = "/api/replies?key=" + key;
        if (ids != null)
        {
            url += "&ids=" + Uri.EscapeDataString(string.Join(",", ids));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Forwarded-For", _clientIp);
        request.Headers.Add("x-bugreport-key", ReportHostHttpFixture.WriteKey);
        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Gone(JsonDocument doc)
        => doc.RootElement.GetProperty("gone").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task Poll_NamesTheRememberedReportsTheKeyCanNoLongerRead_AsGoneAsync()
    {
        string key = KeyFor("lifecycle-owner");
        string owned = AddReport("lifecycle-owner");
        string foreign = AddReport("lifecycle-someone-else");
        string deleted = AddReport("lifecycle-owner");
        Assert.True(_host.Store.Delete(deleted));

        // The client asks about everything it remembers: its live report stays, the deleted one, a report
        // stored under another key and an id that never existed are gone.
        using (var doc = await PollAsync(key, new[] { owned, deleted, foreign, "never-existed" }))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
            string[] gone = Gone(doc);
            Assert.DoesNotContain(owned, gone);
            Assert.Contains(deleted, gone);
            Assert.Contains(foreign, gone);
            Assert.Contains("never-existed", gone);
        }

        // Without ids the field is present and empty — nothing is ever reported unasked.
        using (var doc = await PollAsync(key))
        {
            Assert.Empty(Gone(doc));
        }

        // The operator deletes the live report → the next poll retires it as well.
        Assert.True(_host.Store.Delete(owned));
        using (var doc = await PollAsync(key, new[] { owned }))
        {
            Assert.Equal(new[] { owned }, Gone(doc));
        }
    }
}

/// <summary>Payloads shared by the reply-lifecycle and admin-CSRF classes (the game's camelCase FeedbackReport).</summary>
internal static class ReportHostTestPayloads
{
    public static string Feedback(string playerId, string platform = "WindowsPlayer", string? replyKey = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Hat eaten by door",
            ["description"] = "The door on my ship eats my hat.",
            ["email"] = "",
            ["gameVersion"] = "2026.8.22",
            ["playerId"] = playerId,
            ["playerName"] = "Justus",
            ["platform"] = platform,
            ["clientTimestamp"] = "2026-08-29T12:00:00Z",
            ["reportJson"] = new Dictionary<string, object?> { ["scene"] = "planet" },
        };
        if (replyKey != null)
        {
            body["replyKey"] = replyKey;
        }

        return JsonSerializer.Serialize(body);
    }
}

/// <summary>
/// A second in-process host with the admin UI switched ON (the shared fixture leaves it off) for the
/// CSRF guard of #1369. Redirects are not followed so a form POST's own status is observable.
/// </summary>
public sealed class ReportHostAdminHttpFixture : IAsyncLifetime
{
    public const string WriteKey = "write-key-for-admin-tests";
    public const string AdminUser = "admin";
    public const string AdminPassword = "pw-123";

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rha_" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;

    public ReportStore Store { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        System.IO.Directory.CreateDirectory(_root);
        var config = new ReportHostConfig
        {
            BindAddress = "127.0.0.1",
            Port = 0,
            DataDir = _root,
            WriteKey = WriteKey,
            AdminUser = AdminUser,
            AdminPassword = AdminPassword,
            TrustProxy = true,
        };
        Store = new ReportStore(config, System.IO.Path.Combine(_root, "reports.db"));
        _app = ReportHostApp.Create(config, Store, new AdminNotifier(string.Empty, "reports"), Array.Empty<string>());
        await _app.StartAsync();
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
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
/// The admin forms' CSRF guard (#1369): every form on the detail page carries the process token, and the
/// three form routes (reply, status, delete) refuse a POST without it — a page elsewhere can make the
/// browser send the Basic credentials, but not the token. The scriptable JSON routes refuse anything a
/// browser form could send (they need a JSON content type).
/// </summary>
public sealed class ReportHostAdminCsrfTests : IClassFixture<ReportHostAdminHttpFixture>
{
    private readonly ReportHostAdminHttpFixture _host;

    public ReportHostAdminCsrfTests(ReportHostAdminHttpFixture host)
    {
        _host = host;
    }

    private static readonly string Basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        ReportHostAdminHttpFixture.AdminUser + ":" + ReportHostAdminHttpFixture.AdminPassword));

    private string AddReport()
    {
        var parsed = ReportIngest.Parse(ReportHostTestPayloads.Feedback("csrf-player"), new ReportHostConfig(), out _);
        return _host.Store.Add(parsed!, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private HttpRequestMessage Request(HttpMethod method, string path, bool authorized = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-For", "10.0.2.1");
        if (authorized)
        {
            request.Headers.TryAddWithoutValidation("Authorization", Basic);
        }

        return request;
    }

    private async Task<HttpStatusCode> PostFormAsync(string path, Dictionary<string, string> fields)
    {
        using var request = Request(HttpMethod.Post, path);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _host.Client.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<string> TokenFromDetailPageAsync(string id)
    {
        using var request = Request(HttpMethod.Get, "/admin/report/" + id);
        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        const string Marker = "name='csrf' value='";
        var tokens = html.Split(Marker).Skip(1).Select(rest => rest.Substring(0, rest.IndexOf('\''))).ToArray();
        Assert.Equal(1 + BugReportStatus.All.Length + 1, tokens.Length); // reply + status buttons + delete
        Assert.Single(tokens.Distinct()); // one process token, in every form
        Assert.Matches("^[0-9a-f]{64}$", tokens[0]);
        return tokens[0];
    }

    [Fact]
    public async Task AdminForms_CarryTheToken_AndAPostWithoutItIsRefusedAsync()
    {
        string id = AddReport();
        string token = await TokenFromDetailPageAsync(id);

        // Status: no token / a wrong token → 403 and nothing changes; the page's token → redirect + change.
        Assert.Equal(HttpStatusCode.Forbidden, await PostFormAsync($"/admin/report/{id}/status", new() { ["status"] = "done" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostFormAsync($"/admin/report/{id}/status", new() { ["status"] = "done", ["csrf"] = new string('0', 64) }));
        Assert.Equal(BugReportStatus.New, _host.Store.Get(id)!.Status);
        Assert.Equal(HttpStatusCode.Found, await PostFormAsync($"/admin/report/{id}/status", new() { ["status"] = "done", ["csrf"] = token }));
        Assert.Equal(BugReportStatus.Done, _host.Store.Get(id)!.Status);

        // Reply: the route that can put text in front of a player.
        Assert.Equal(HttpStatusCode.Forbidden, await PostFormAsync($"/admin/report/{id}/reply", new() { ["text"] = "forged" }));
        Assert.Empty(_host.Store.ListReplies(id));
        Assert.Equal(HttpStatusCode.Found, await PostFormAsync($"/admin/report/{id}/reply", new() { ["text"] = "genuine", ["csrf"] = token }));
        Assert.Equal("genuine", _host.Store.ListReplies(id).Single().Text);

        // Delete.
        Assert.Equal(HttpStatusCode.Forbidden, await PostFormAsync($"/admin/report/{id}/delete", new()));
        Assert.NotNull(_host.Store.Get(id));
        Assert.Equal(HttpStatusCode.Found, await PostFormAsync($"/admin/report/{id}/delete", new() { ["csrf"] = token }));
        Assert.Null(_host.Store.Get(id));

        // The token is a second factor, never a substitute for the credentials.
        using var anonymous = Request(HttpMethod.Post, $"/admin/report/{id}/status", authorized: false);
        anonymous.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "done", ["csrf"] = token });
        using var refused = await _host.Client.SendAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task AdminJsonRoutes_RefuseWhatABrowserFormCouldSendAsync()
    {
        string id = AddReport();

        // text/plain is the one encoding a <form> can use to smuggle a JSON-shaped body — 415, untouched.
        using (var forged = Request(HttpMethod.Patch, "/api/reports/" + id))
        {
            forged.Content = new StringContent("{\"status\":\"done\"}", Encoding.UTF8, "text/plain");
            using var response = await _host.Client.SendAsync(forged);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        using (var forged = Request(HttpMethod.Post, $"/api/reports/{id}/replies"))
        {
            forged.Content = new StringContent("{\"text\":\"forged\"}", Encoding.UTF8, "text/plain");
            using var response = await _host.Client.SendAsync(forged);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        Assert.Equal(BugReportStatus.New, _host.Store.Get(id)!.Status);
        Assert.Empty(_host.Store.ListReplies(id));

        // A real script sends application/json and is served as before.
        using (var genuine = Request(HttpMethod.Patch, "/api/reports/" + id))
        {
            genuine.Content = new StringContent("{\"status\":\"triaged\"}", Encoding.UTF8, "application/json");
            using var response = await _host.Client.SendAsync(genuine);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(BugReportStatus.Triaged, _host.Store.Get(id)!.Status);
    }
}

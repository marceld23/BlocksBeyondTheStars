// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ReportHost routes that had no HTTP coverage before <see cref="ReportHostApp.Create"/> was split into route
/// groups (#2903): the keyed read API (list, item, screenshot, attachment), <c>/healthz</c>, <c>/</c>, the admin list,
/// the export download, the admin screenshot, the scriptable operator reply and DELETE. It pins status codes,
/// headers, redirects and the JSON shapes — property NAMES and their ORDER, which is the wire order of the
/// anonymous objects the handlers return — so a handler moved between files provably answers as before.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // loopback requests are billed the parallel queue wait otherwise (#1362)
public sealed class ReportHostReadApiHttpTests : IClassFixture<ReportHostAdminHttpFixture>
{
    private static readonly string[] PageKeys = { "items", "nextCursor", "hasMore" };

    private static readonly string[] ItemKeys =
    {
        "id", "title", "description", "email", "gameVersion", "buildNumber", "playerId", "playerName", "sessionId",
        "platform", "clientTimestamp", "category", "source", "kind", "status", "createdAt", "screenshotUrl",
        "attachments", "reportJson", "fixedInVersion", "replies",
    };

    private static readonly string[] AttachmentKeys = { "index", "fileName", "mimeType", "bytes", "url" };
    private static readonly string[] ReplyKeys = { "id", "author", "text", "isQuestion", "createdUnix", "seen" };
    private static readonly string[] PollKeys = { "items", "gone" };
    private static readonly string[] ThreadKeys = { "reportId", "title", "status", "fixedInVersion", "createdUnix", "replies", "unseenIds" };
    private static readonly string[] ExportKeys = { "exportedUtc", "status", "category", "count", "reports" };

    private static readonly byte[] Shot = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7 };

    private static readonly string PollKey = FeedbackReplyKey.Derive("read-api-install");

    private static readonly string Basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        ReportHostAdminHttpFixture.AdminUser + ":" + ReportHostAdminHttpFixture.AdminPassword));

    private readonly ReportHostAdminHttpFixture _host;

    public ReportHostReadApiHttpTests(ReportHostAdminHttpFixture host)
    {
        _host = host;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, bool authorized = true)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-For", "10.0.3.1");
        if (authorized)
        {
            request.Headers.TryAddWithoutValidation("Authorization", Basic);
        }

        return request;
    }

    private static HttpRequestMessage WithReadKey(HttpRequestMessage request)
    {
        request.Headers.Add("x-report-read-key", ReportHostAdminHttpFixture.ReadKey);
        return request;
    }

    /// <summary>GET without Basic credentials, with or without the read key.</summary>
    private async Task<HttpResponseMessage> ReadAsync(string path, bool readKey = true)
    {
        using var request = Request(HttpMethod.Get, path, authorized: false);
        if (readKey)
        {
            WithReadKey(request);
        }

        return await _host.Client.SendAsync(request);
    }

    /// <summary>A request with the operator's Basic credentials (or none), optionally with a body.</summary>
    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string path, bool authorized = true, HttpContent? content = null)
    {
        using var request = Request(method, path, authorized);
        request.Content = content;
        return await _host.Client.SendAsync(request);
    }

    private string Seed(string payload)
        => _host.Store.Add(ReportIngest.Parse(payload, new ReportHostConfig(), out _)!, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static async Task<JsonDocument> JsonOfAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task ReadApiAndAdminPages_KeepTheirStatusCodesAndShapesAsync()
    {
        // Three unrelated rows (different reply keys / categories, so no two of them pair up, #1380): a plain
        // F1 report, the same with a screenshot, and a texture submission with its two files.
        string id = Seed(ReportHostTestPayloads.Feedback("read-api-player", replyKey: PollKey));
        var withShot = JsonNode.Parse(ReportHostTestPayloads.Feedback("read-api-shot-player"))!.AsObject();
        withShot["screenshot"] = new JsonObject { ["base64"] = Convert.ToBase64String(Shot), ["mimeType"] = "image/png" };
        string shotId = Seed(withShot.ToJsonString());
        string textureId = Seed(ReportHostTextureSubmissionTests.SubmissionPayload());

        await AssertGatesAndRedirectsAsync();
        await AssertReadApiAsync(id, shotId, textureId);
        await AssertAdminPagesAsync(id, shotId);
        await AssertOperatorReplyAndPollAsync(id);
        await AssertDeleteAsync(id);
    }

    private async Task AssertGatesAndRedirectsAsync()
    {
        using (var health = await ReadAsync("/healthz", readKey: false))
        {
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("ok\n", await health.Content.ReadAsStringAsync());
        }

        using (var root = await AdminAsync(HttpMethod.Get, "/", authorized: false))
        {
            Assert.Equal(HttpStatusCode.Found, root.StatusCode);
            Assert.Equal("/admin", root.Headers.Location?.OriginalString);
        }

        using (var anonymous = await AdminAsync(HttpMethod.Get, "/admin", authorized: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.True(anonymous.Headers.TryGetValues("WWW-Authenticate", out var challenge));
            Assert.StartsWith("Basic realm=", string.Join(",", challenge!), StringComparison.Ordinal);
        }

        using (var noKey = await ReadAsync("/api/reports", readKey: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
            Assert.Contains("forbidden", await noKey.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    private async Task AssertReadApiAsync(string id, string shotId, string textureId)
    {
        using (var page = await ReadAsync("/api/reports?limit=1"))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            using var doc = await JsonOfAsync(page);
            Assert.Equal(PageKeys, Names(doc.RootElement));
            var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(ItemKeys, Names(item));
            Assert.Equal(JsonValueKind.Null, item.GetProperty("replies").ValueKind); // the list carries no threads
            Assert.True(doc.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("nextCursor").ValueKind);
        }

        using (var single = await ReadAsync("/api/reports/" + id))
        {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            using var doc = await JsonOfAsync(single);
            var item = doc.RootElement;
            Assert.Equal(ItemKeys, Names(item));
            Assert.Equal(id, item.GetProperty("id").GetString());
            Assert.Equal("feedback", item.GetProperty("category").GetString());
            Assert.Equal(BugReportStatus.New, item.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("screenshotUrl").ValueKind);
            Assert.Equal(0, item.GetProperty("attachments").GetArrayLength());
            Assert.Equal(0, item.GetProperty("replies").GetArrayLength());

            // reportJson is the whole stored payload minus the screenshot — the game's own reportJson nests inside.
            var stored = item.GetProperty("reportJson");
            Assert.Equal("Hat eaten by door", stored.GetProperty("title").GetString());
            Assert.Equal("planet", stored.GetProperty("reportJson").GetProperty("scene").GetString());
        }

        using (var noShot = await ReadAsync($"/api/reports/{id}/screenshot"))
        {
            Assert.Equal(HttpStatusCode.NotFound, noShot.StatusCode);
        }

        using (var noKey = await ReadAsync($"/api/reports/{id}/screenshot", readKey: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        }

        string shotUrl = $"/api/reports/{shotId}/screenshot";
        using (var shotItem = await ReadAsync("/api/reports/" + shotId))
        {
            using var doc = await JsonOfAsync(shotItem);
            Assert.Equal(shotUrl, doc.RootElement.GetProperty("screenshotUrl").GetString());
        }

        using (var shot = await ReadAsync(shotUrl))
        {
            Assert.Equal(HttpStatusCode.OK, shot.StatusCode);
            Assert.Equal("image/png", shot.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Shot, await shot.Content.ReadAsByteArrayAsync());
        }

        using (var textureItem = await ReadAsync("/api/reports/" + textureId))
        {
            using var doc = await JsonOfAsync(textureItem);
            var attachments = doc.RootElement.GetProperty("attachments");
            Assert.Equal(2, attachments.GetArrayLength());
            Assert.Equal(AttachmentKeys, Names(attachments[1]));
            Assert.Equal($"/api/reports/{textureId}/attachment/1", attachments[1].GetProperty("url").GetString());
        }

        using (var file = await ReadAsync($"/api/reports/{textureId}/attachment/1"))
        {
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            var disposition = file.Content.Headers.ContentDisposition;
            Assert.Equal("texture1.bytes", disposition?.FileNameStar ?? disposition?.FileName);
            Assert.Equal(ReportHostTextureSubmissionTests.Raw, await file.Content.ReadAsByteArrayAsync());
        }
    }

    private async Task AssertAdminPagesAsync(string id, string shotId)
    {
        using (var list = await AdminAsync(HttpMethod.Get, "/admin"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("text/html", list.Content.Headers.ContentType?.MediaType);
        }

        using (var export = await AdminAsync(HttpMethod.Get, "/admin/export"))
        {
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
            var disposition = export.Content.Headers.ContentDisposition;
            Assert.StartsWith("bbs-reports-", (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"'), StringComparison.Ordinal);
            using var doc = await JsonOfAsync(export);
            Assert.Equal(ExportKeys, Names(doc.RootElement));
            Assert.Equal(3, doc.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(3, doc.RootElement.GetProperty("reports").GetArrayLength());
        }

        using (var noShot = await AdminAsync(HttpMethod.Get, $"/admin/report/{id}/screenshot"))
        {
            Assert.Equal(HttpStatusCode.NotFound, noShot.StatusCode);
        }

        using (var shot = await AdminAsync(HttpMethod.Get, $"/admin/report/{shotId}/screenshot"))
        {
            Assert.Equal(HttpStatusCode.OK, shot.StatusCode);
            Assert.Equal("image/png", shot.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Shot, await shot.Content.ReadAsByteArrayAsync());
        }
    }

    private async Task AssertOperatorReplyAndPollAsync(string id)
    {
        // The scriptable operator answer resolves the thread owner (#1378) — for an unpaired row, the row itself.
        var body = new StringContent("{\"text\":\"pinned\",\"fixedInVersion\":\"2026.1.1\"}", Encoding.UTF8, "application/json");
        using (var reply = await AdminAsync(HttpMethod.Post, $"/api/reports/{id}/replies", content: body))
        {
            Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
            using var doc = await JsonOfAsync(reply);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(id, doc.RootElement.GetProperty("reportId").GetString());
        }

        Assert.Equal("pinned", _host.Store.ListReplies(id).Single().Text);
        Assert.Equal("2026.1.1", _host.Store.Get(id)!.FixedInVersion);

        using (var single = await ReadAsync("/api/reports/" + id))
        {
            using var doc = await JsonOfAsync(single);
            Assert.Equal("2026.1.1", doc.RootElement.GetProperty("fixedInVersion").GetString());
            var entry = Assert.Single(doc.RootElement.GetProperty("replies").EnumerateArray());
            Assert.Equal(ReplyKeys, Names(entry));
            Assert.Equal("pinned", entry.GetProperty("text").GetString());
        }

        // The game's poll for that key sees the thread in the client's shape.
        using var poll = Request(HttpMethod.Get, "/api/replies?key=" + PollKey, authorized: false);
        poll.Headers.Add("x-bugreport-key", ReportHostAdminHttpFixture.WriteKey);
        using var polled = await _host.Client.SendAsync(poll);
        Assert.Equal(HttpStatusCode.OK, polled.StatusCode);
        using var threads = await JsonOfAsync(polled);
        Assert.Equal(PollKeys, Names(threads.RootElement));
        var thread = Assert.Single(threads.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(ThreadKeys, Names(thread));
        Assert.Equal(id, thread.GetProperty("reportId").GetString());
    }

    private async Task AssertDeleteAsync(string id)
    {
        using (var deleted = await AdminAsync(HttpMethod.Delete, "/api/reports/" + id))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var gone = await ReadAsync("/api/reports/" + id))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        using (var again = await AdminAsync(HttpMethod.Delete, "/api/reports/" + id))
        {
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The playtest catalogue in the report inbox: the store's bookkeeping (revision check, reserved ids, the served
/// catalogue and its ETag) and the routes over HTTP against the real app — the game's fetch behind the spam key, and
/// the operator's scriptable routes behind Basic Auth and the JSON gate.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // loopback requests are billed the parallel queue wait otherwise (#1362)
public sealed class ReportHostPlaytestTests : IClassFixture<ReportHostAdminHttpFixture>
{
    private static readonly string Basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        ReportHostAdminHttpFixture.AdminUser + ":" + ReportHostAdminHttpFixture.AdminPassword));

    private readonly ReportHostAdminHttpFixture _host;

    public ReportHostPlaytestTests(ReportHostAdminHttpFixture host)
    {
        _host = host;
    }

    private static string TaskJson(string id, int rev = 1, bool enabled = true, int sort = 0, string title = "Titel")
        => $$"""
            { "id": "{{id}}", "rev": {{rev}}, "enabled": {{(enabled ? "true" : "false")}}, "sort": {{sort}},
              "title": { "de": "{{title}}", "en": "Title" }, "intro": { "de": "Worum es geht.", "en": "What it is about." },
              "steps": [ { "id": "one", "text": { "de": "Mach das.", "en": "Do this." } } ],
              "questions": [ { "id": "fun", "type": "stars", "text": { "de": "Spaß?", "en": "Fun?" } } ] }
            """;

    private static PlaytestTask Task(string id, int rev = 1, bool enabled = true, int sort = 0)
    {
        Assert.True(PlaytestCatalogJson.TryReadTask(TaskJson(id, rev, enabled, sort), out var task, PlaytestReadMode.AsWritten));
        return task;
    }

    private static PlaytestStore NewStore(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "bbts_pts_" + Guid.NewGuid().ToString("N"));
        return new PlaytestStore(Path.Combine(root, "playtests.db"));
    }

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }

    // ---------------------------------------------------------------- the store

    [Fact]
    public void Store_SavesReplacesAndDeletes()
    {
        var store = NewStore(out string root);
        try
        {
            Assert.Equal(PlaytestSaveResult.Saved, store.Save(Task("alpha"), 100));
            Assert.Equal(PlaytestSaveResult.Saved, store.Save(Task("alpha", rev: 2), 200));

            var record = store.Get("alpha")!;
            Assert.Equal(2, record.Rev);
            Assert.Equal(200, record.UpdatedUnix);
            Assert.Equal("alpha", record.Task().Id);

            Assert.True(store.Delete("alpha"));
            Assert.False(store.Delete("alpha"));
            Assert.Null(store.Get("alpha"));
        }
        finally
        {
            store.Dispose();
            Cleanup(root);
        }
    }

    [Fact]
    public void Store_RefusesAnOlderRevision_UnlessForced()
    {
        var store = NewStore(out string root);
        try
        {
            store.Save(Task("alpha", rev: 5), 100);

            Assert.Equal(PlaytestSaveResult.StaleRev, store.Save(Task("alpha", rev: 4), 200));
            Assert.Equal(5, store.Get("alpha")!.Rev);
            Assert.Equal(PlaytestSaveResult.Saved, store.Save(Task("alpha", rev: 5), 200)); // the same revision may be re-sent
            Assert.Equal(PlaytestSaveResult.Saved, store.Save(Task("alpha", rev: 4), 300, force: true));
            Assert.Equal(4, store.Get("alpha")!.Rev);
        }
        finally
        {
            store.Dispose();
            Cleanup(root);
        }
    }

    [Fact]
    public void Store_RefusesTheRouteWordsAsIds()
    {
        var store = NewStore(out string root);
        try
        {
            foreach (string reserved in PlaytestStore.ReservedIds)
            {
                Assert.Equal(PlaytestSaveResult.ReservedId, store.Save(Task(reserved), 100));
            }

            Assert.Empty(store.All());
        }
        finally
        {
            store.Dispose();
            Cleanup(root);
        }
    }

    [Fact]
    public void Served_HoldsOnlySwitchedOnTasks_InOrder_AndItsETagFollowsTheContent()
    {
        var store = NewStore(out string root);
        try
        {
            var (emptyJson, emptyTag) = store.Served();
            Assert.True(PlaytestCatalogJson.TryReadCatalog(emptyJson, out var empty));
            Assert.True(empty.Enabled);
            Assert.Empty(empty.Tasks);

            store.Save(Task("later", sort: 20), 100);
            store.Save(Task("first", sort: 10), 100);
            store.Save(Task("hidden", enabled: false, sort: 5), 100);

            var (json, tag) = store.Served();
            Assert.NotEqual(emptyTag, tag);
            Assert.Equal(tag, store.Served().ETag); // unchanged content, unchanged tag
            Assert.True(PlaytestCatalogJson.TryReadCatalog(json, out var catalog));
            Assert.Equal(new[] { "first", "later" }, catalog.Tasks.Select(t => t.Id));
            Assert.Equal(new[] { "hidden", "first", "later" }, store.All().Select(r => r.Id)); // the operator sees all three

            store.SetCatalogEnabled(false);
            Assert.True(PlaytestCatalogJson.TryReadCatalog(store.Served().Json, out var off));
            Assert.False(off.Enabled);
            Assert.NotEqual(tag, store.Served().ETag);
            Assert.False(store.CatalogEnabled);
        }
        finally
        {
            store.Dispose();
            Cleanup(root);
        }
    }

    [Fact]
    public void Store_KeepsItsTasksAcrossAReopen()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_pts_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "playtests.db");
        try
        {
            using (var first = new PlaytestStore(path))
            {
                first.Save(Task("alpha", rev: 3), 100);
                first.SetCatalogEnabled(false);
            }

            using var second = new PlaytestStore(path);
            Assert.Equal(3, second.Get("alpha")!.Rev);
            Assert.False(second.CatalogEnabled);
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ---------------------------------------------------------------- the routes

    private static HttpRequestMessage Request(HttpMethod method, string path, string ip)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-For", ip);
        return request;
    }

    private async Task<HttpResponseMessage> FetchAsync(string ip, string? key = ReportHostAdminHttpFixture.WriteKey, string? etag = null)
    {
        using var request = Request(HttpMethod.Get, "/api/playtests", ip);
        if (key != null)
        {
            request.Headers.Add("x-bugreport-key", key);
        }

        if (etag != null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        return await _host.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string path, string? json = null,
        bool authorized = true, string mediaType = "application/json")
    {
        using var request = Request(method, path, "10.0.9.1");
        if (authorized)
        {
            request.Headers.TryAddWithoutValidation("Authorization", Basic);
        }

        if (json != null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, mediaType);
        }

        return await _host.Client.SendAsync(request);
    }

    private static async Task<JsonDocument> JsonOfAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Fetch_NeedsTheSpamKey_AndAnswersNotModifiedForAKnownETagAsync()
    {
        using (var noKey = await FetchAsync("10.0.9.10", key: null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
        }

        using (var wrongKey = await FetchAsync("10.0.9.10", key: "nope"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        }

        string etag;
        using (var first = await FetchAsync("10.0.9.10"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal("application/json", first.Content.Headers.ContentType!.MediaType);
            etag = first.Headers.ETag!.ToString();
            Assert.True(PlaytestCatalogJson.TryReadCatalog(await first.Content.ReadAsStringAsync(), out _));
        }

        using var again = await FetchAsync("10.0.9.10", etag: etag);
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
    }

    [Fact]
    public async Task Put_StoresATask_TheGameThenFetchesItAsync()
    {
        using (var put = await AdminAsync(HttpMethod.Put, "/api/playtests/route-put", TaskJson("route-put", title: "Über Umlaute")))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            using var doc = await JsonOfAsync(put);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("route-put", doc.RootElement.GetProperty("id").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("rev").GetInt32());
            Assert.Equal(0, doc.RootElement.GetProperty("warnings").GetArrayLength());
        }

        using var fetch = await FetchAsync("10.0.9.11");
        Assert.True(PlaytestCatalogJson.TryReadCatalog(await fetch.Content.ReadAsStringAsync(), out var catalog));
        var task = Assert.Single(catalog.Tasks, t => t.Id == "route-put");
        Assert.Equal("Über Umlaute", task.Title.De);
    }

    [Fact]
    public async Task Put_IsTheOperatorsAlone_AndTakesOnlyJsonAsync()
    {
        using (var anonymous = await AdminAsync(HttpMethod.Put, "/api/playtests/guarded", TaskJson("guarded"), authorized: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var form = await AdminAsync(HttpMethod.Put, "/api/playtests/guarded", TaskJson("guarded"), mediaType: "text/plain"))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode); // a forged HTML form cannot send JSON (#1369)
        }

        Assert.Null(_host.Playtests.Get("guarded"));
    }

    [Fact]
    public async Task Put_NamesEveryProblem_AndStoresNothingAsync()
    {
        string bad = """{ "id": "broken", "title": { "de": "nur deutsch" }, "steps": [] }""";

        using var put = await AdminAsync(HttpMethod.Put, "/api/playtests/broken", bad);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        using var doc = await JsonOfAsync(put);
        Assert.Equal("invalid_task", doc.RootElement.GetProperty("error").GetString());
        var where = doc.RootElement.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("where").GetString()).ToList();
        Assert.Contains("title.en", where);
        Assert.Contains("steps", where);
        Assert.Null(_host.Playtests.Get("broken"));
    }

    [Fact]
    public async Task Put_RefusesAnIdThatIsNotTheRoutes_AnOlderRevision_AndARouteWordAsync()
    {
        using (var mismatch = await AdminAsync(HttpMethod.Put, "/api/playtests/one-id", TaskJson("other-id")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }

        using (var first = await AdminAsync(HttpMethod.Put, "/api/playtests/revved", TaskJson("revved", rev: 3)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var stale = await AdminAsync(HttpMethod.Put, "/api/playtests/revved", TaskJson("revved", rev: 2)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var doc = await JsonOfAsync(stale);
            Assert.Equal("stale_rev", doc.RootElement.GetProperty("error").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("storedRev").GetInt32());
        }

        using (var forced = await AdminAsync(HttpMethod.Put, "/api/playtests/revved?force=true", TaskJson("revved", rev: 2)))
        {
            Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        }

        // "mine" is a route word; the validator accepts it as an id, the store does not.
        using var reserved = await AdminAsync(HttpMethod.Put, "/api/playtests/mine", TaskJson("mine"));
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
    }

    [Fact]
    public async Task Export_ListsEverythingAsWritten_ForTheOperatorOnlyAsync()
    {
        using (var put = await AdminAsync(HttpMethod.Put, "/api/playtests/export-off", TaskJson("export-off", enabled: false)))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        using (var anonymous = await AdminAsync(HttpMethod.Get, "/api/playtests/export", authorized: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var export = await AdminAsync(HttpMethod.Get, "/api/playtests/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(PlaytestCatalogJson.TryReadCatalog(await export.Content.ReadAsStringAsync(), out var all, PlaytestReadMode.AsWritten));
        Assert.Contains(all.Tasks, t => t.Id == "export-off" && !t.Enabled); // a switched-off task is still the operator's

        using var fetch = await FetchAsync("10.0.9.12");
        Assert.True(PlaytestCatalogJson.TryReadCatalog(await fetch.Content.ReadAsStringAsync(), out var served));
        Assert.DoesNotContain(served.Tasks, t => t.Id == "export-off");
    }

    [Fact]
    public async Task Delete_RemovesATaskAsync()
    {
        using (var put = await AdminAsync(HttpMethod.Put, "/api/playtests/to-delete", TaskJson("to-delete")))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        using (var anonymous = await AdminAsync(HttpMethod.Delete, "/api/playtests/to-delete", authorized: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var delete = await AdminAsync(HttpMethod.Delete, "/api/playtests/to-delete"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using var again = await AdminAsync(HttpMethod.Delete, "/api/playtests/to-delete");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }
}

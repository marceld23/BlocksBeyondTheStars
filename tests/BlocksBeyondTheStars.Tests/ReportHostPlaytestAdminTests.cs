// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Payloads of playtest reports as the game sends them, for the inbox's tests.</summary>
internal static class PlaytestTestPayloads
{
    public const string Description = "Playtest task: Der schwere Schuss [heavy-shot, rev 2]";

    /// <summary>The client-direct playtest report of <paramref name="replyKey"/>.</summary>
    public static string Report(string replyKey, string taskId = "heavy-shot", string fun = "4", string hard = "tough",
        string feel = "failed", bool withWork = false, bool fromAWorld = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Test Pilot: The heavy shot",
            ["description"] = Description,
            ["gameVersion"] = "2026.11.22",
            ["playerName"] = "Layex",
            ["platform"] = "WindowsPlayer",
            ["replyKey"] = replyKey,
            ["reportJson"] = new Dictionary<string, object?>
            {
                ["reportType"] = ReportIngest.PlaytestReportType,
                ["playtest"] = new Dictionary<string, object?>
                {
                    ["taskId"] = taskId,
                    ["rev"] = 2,
                    ["locale"] = "de",
                    ["seconds"] = 412,
                    ["freeText"] = "Der Ring ist schwer zu sehen.",
                    ["workAttached"] = withWork,
                    ["steps"] = new Dictionary<string, object?>
                    {
                        ["launch"] = new Dictionary<string, object?> { ["state"] = "done", ["auto"] = true, ["count"] = 1, ["note"] = "" },
                        ["feel"] = new Dictionary<string, object?> { ["state"] = feel, ["auto"] = false, ["count"] = 0, ["note"] = "Kein Rückstoß." },
                    },
                    ["answers"] = new Dictionary<string, object?> { ["fun"] = fun, ["hard"] = hard },
                },
            },
        };
        if (fromAWorld)
        {
            // #3838: sent from inside a world, the report says how the game was drawn (a bug report's graphics node).
            ((Dictionary<string, object?>)body["reportJson"]!)["graphics"] = new Dictionary<string, object?>
            {
                ["api"] = "Direct3D11",
                ["preset"] = "High",
                ["presetAuto"] = true,
                ["renderScale"] = 0.85,
                ["screen"] = "1920x1080",
            };
        }

        if (withWork)
        {
            body["attachments"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["fileName"] = "ship.json", ["mimeType"] = "application/json",
                    ["base64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"formatVersion\":1}")),
                },
            };
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>The game server's /bump snapshot of the same moment — plain feedback from <c>source: server</c>.</summary>
    public static string Snapshot(string replyKey) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["title"] = "Bump [Testwelt]: [feedback] Test Pilot: The heavy shot",
        ["description"] = "[feedback] Test Pilot: The heavy shot — " + Description,
        ["gameVersion"] = "2026.11.22",
        ["playerId"] = "Layex",
        ["playerName"] = "Layex",
        ["replyKey"] = replyKey,
        ["reportJson"] = new Dictionary<string, object?> { ["source"] = "server", ["reportType"] = "bump" },
    });

    public const string Task = """
        { "id": "heavy-shot", "rev": 2,
          "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
          "intro": { "de": "Worum es geht.", "en": "What it is about." },
          "steps": [ { "id": "launch", "text": { "de": "Starte ins All.", "en": "Launch into space." }, "check": { "event": "space:enter" } },
                     { "id": "feel", "text": { "de": "Fühlt er sich schwer an?", "en": "Does it feel heavy?" } } ],
          "questions": [ { "id": "fun", "type": "stars", "text": { "de": "Spaß?", "en": "How much fun?" } },
                         { "id": "hard", "type": "choice", "text": { "de": "Wie schwer?", "en": "How hard?" },
                           "options": [ { "id": "easy", "text": { "de": "Leicht", "en": "Easy" } },
                                        { "id": "tough", "text": { "de": "Schwer", "en": "Hard" } } ] } ],
          "thanks": { "de": "Danke, Testpilot!", "en": "Thank you, test pilot!" } }
        """;

    public static BugReportRecord Stored(ReportStore store, string payload, long nowUnix)
    {
        var parsed = ReportIngest.Parse(payload, new ReportHostConfig(), out _)!;
        return store.Get(store.Add(parsed, nowUnix))!;
    }
}

/// <summary>
/// What the inbox does with playtest reports beyond filing them: read the machine-readable half, add a task's reports
/// up, confirm one (status on both halves + the thank-you that carries it to the game), answer an install about its
/// own reports, drop old attached work — and the admin pages of the catalogue over HTTP with their CSRF guard.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // loopback requests are billed the parallel queue wait otherwise (#1362)
public sealed class ReportHostPlaytestAdminTests : IClassFixture<ReportHostAdminHttpFixture>
{
    private static readonly string Basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        ReportHostAdminHttpFixture.AdminUser + ":" + ReportHostAdminHttpFixture.AdminPassword));

    private readonly ReportHostAdminHttpFixture _host;

    public ReportHostPlaytestAdminTests(ReportHostAdminHttpFixture host)
    {
        _host = host;
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private void StoreTask()
    {
        Assert.True(PlaytestCatalogJson.TryReadTask(PlaytestTestPayloads.Task, out var task, PlaytestReadMode.AsWritten));
        _host.Playtests.Save(task, Now, force: true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, bool admin = true, bool spamKey = false,
        HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Forwarded-For", "10.0.8.1");
        if (admin)
        {
            request.Headers.TryAddWithoutValidation("Authorization", Basic);
        }

        if (spamKey)
        {
            request.Headers.Add("x-bugreport-key", ReportHostAdminHttpFixture.WriteKey);
        }

        return await _host.Client.SendAsync(request);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<string> PageAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> TokenAsync()
    {
        string html = await PageAsync("/admin/playtests");
        const string Marker = "name='csrf' value='";
        string rest = html.Split(Marker)[1];
        return rest.Substring(0, rest.IndexOf('\''));
    }

    private async Task<HttpResponseMessage> PostFormAsync(string path, Dictionary<string, string> fields)
        => await SendAsync(HttpMethod.Post, path, content: new FormUrlEncodedContent(fields));

    // ---------------------------------------------------------------- reading and adding up

    [Fact]
    public void ReportData_ReadsTheTaskTheStepsAndTheAnswers()
    {
        var record = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("data")), Now);

        var data = PlaytestReportData.Read(record)!;

        Assert.Equal("heavy-shot", data.TaskId);
        Assert.Equal(2, data.Rev);
        Assert.Equal("de", data.Locale);
        Assert.Equal(412, data.Seconds);
        Assert.Equal(4, data.Stars("fun"));
        Assert.Equal(0, data.Stars("hard"));
        Assert.Equal("tough", data.Answers["hard"]);
        var feel = Assert.Single(data.Steps, s => s.Id == "feel");
        Assert.True(feel.Failed);
        Assert.Equal("Kein Rückstoß.", feel.Note);
        Assert.True(data.Steps.Single(s => s.Id == "launch").Auto);

        var feedback = PlaytestTestPayloads.Stored(_host.Store, ReportHostTestPayloads.Feedback("no-playtest"), Now);
        Assert.Null(PlaytestReportData.Read(feedback));
    }

    [Fact]
    public void ReportData_SaysHowTheGameWasDrawn_ForAReportFromAWorld_AndNothingForOneFromTheMenu()
    {
        var world = PlaytestTestPayloads.Stored(_host.Store,
            PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("gfx-world"), fromAWorld: true), Now);
        var menu = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("gfx-menu")), Now);

        Assert.Equal("preset High (picked automatically) · render scale 0.85 · 1920x1080 · Direct3D11", PlaytestReportData.Read(world)!.Graphics);
        Assert.Equal(string.Empty, PlaytestReportData.Read(menu)!.Graphics); // #3838: no node, no line
    }

    [Fact]
    public void Stats_AddUpStarsChoicesAndFailedSteps_PerTask()
    {
        string key = FeedbackReplyKey.Derive("stats");
        var records = new[]
        {
            PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, "stats-task", fun: "4", hard: "tough"), Now),
            PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, "stats-task", fun: "2", hard: "easy", feel: "done"), Now),
            PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, "stats-task", fun: "5", hard: "tough"), Now),
            PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, "other-task"), Now),
        };

        var stats = PlaytestStats.ByTask(records);

        var task = stats["stats-task"];
        Assert.Equal(3, task.Reports);
        Assert.Equal((11, 3), task.Stars["fun"]);
        Assert.Equal(11.0 / 3, task.AverageStars, 5);
        Assert.Equal(2, task.Choices["hard"]["tough"]);
        Assert.Equal(1, task.Choices["hard"]["easy"]);
        Assert.Equal(2, task.FailedSteps["feel"]);
        Assert.Equal(1, stats["other-task"].Reports);
    }

    [Fact]
    public void OldAttachedWork_IsRemoved_TheReportStays()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_ptw_" + Guid.NewGuid().ToString("N"));
        var config = new ReportHostConfig { DataDir = root };
        using (var store = new ReportStore(config, Path.Combine(root, "reports.db")))
        {
            long old = Now - (400 * 86400L);
            var withWork = PlaytestTestPayloads.Stored(store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("work"), withWork: true), old);
            var fresh = PlaytestTestPayloads.Stored(store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("work"), withWork: true), Now);
            string file = store.AttachmentPath(Assert.Single(store.Attachments(withWork.Id)))!;

            Assert.Equal(0, store.PrunePlaytestAttachments(0, Now)); // 0 = keep forever
            Assert.Equal(1, store.PrunePlaytestAttachments(365, Now));

            Assert.Empty(store.Attachments(withWork.Id));
            Assert.False(File.Exists(file));
            Assert.NotNull(store.Get(withWork.Id)); // the answers stay for the evaluation
            Assert.Single(store.Attachments(fresh.Id));
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }

    // ---------------------------------------------------------------- an install's own reports

    [Fact]
    public async Task Mine_ListsOnlyTheKeysOwnPlaytestReportsAsync()
    {
        string key = FeedbackReplyKey.Derive("mine-install");
        var own = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key), Now);
        PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("someone-else")), Now);
        PlaytestTestPayloads.Stored(_host.Store, ReportHostTestPayloads.Feedback("plain", replyKey: key), Now); // not a playtest

        using (var noSpamKey = await SendAsync(HttpMethod.Get, "/api/playtests/mine?key=" + key, admin: false))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noSpamKey.StatusCode);
        }

        using (var badKey = await SendAsync(HttpMethod.Get, "/api/playtests/mine?key=nope", admin: false, spamKey: true))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        }

        using var response = await SendAsync(HttpMethod.Get, "/api/playtests/mine?key=" + key, admin: false, spamKey: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(own.Id, item.GetProperty("reportId").GetString());
        Assert.Equal("heavy-shot", item.GetProperty("taskId").GetString());
        Assert.Equal(2, item.GetProperty("rev").GetInt32());
        Assert.False(item.GetProperty("confirmed").GetBoolean());
    }

    // ---------------------------------------------------------------- confirm

    [Fact]
    public async Task Confirm_MarksBothHalvesDone_AndTheThankYouCarriesItToTheGameAsync()
    {
        StoreTask();
        string key = FeedbackReplyKey.Derive("confirm-install");
        long now = Now;
        var client = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key), now);
        var snapshot = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Snapshot(key), now + 1);

        using (var anonymous = await SendAsync(HttpMethod.Post, $"/api/reports/{snapshot.Id}/confirm", admin: false, content: Json("{}")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var form = await SendAsync(HttpMethod.Post, $"/api/reports/{snapshot.Id}/confirm",
                   content: new StringContent("{}", Encoding.UTF8, "text/plain")))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);
        }

        // Named by the SNAPSHOT half — what the admin list links to — and still both rows are covered.
        using (var confirm = await SendAsync(HttpMethod.Post, $"/api/reports/{snapshot.Id}/confirm", content: Json("{\"fixedInVersion\":\"2026.11.23\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            using var doc = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync());
            Assert.Equal(client.Id, doc.RootElement.GetProperty("reportId").GetString());
            Assert.Equal("Danke, Testpilot!", doc.RootElement.GetProperty("text").GetString()); // the task's own, in the tester's language
        }

        Assert.Equal(BugReportStatus.Done, _host.Store.Get(client.Id)!.Status);
        Assert.Equal(BugReportStatus.Done, _host.Store.Get(snapshot.Id)!.Status);
        Assert.Equal("2026.11.23", _host.Store.Get(client.Id)!.FixedInVersion);

        // The game's poll: one thread, on the client's row, already saying "done".
        var thread = Assert.Single(_host.Store.UnreadThreads(key));
        Assert.Equal(client.Id, thread.Report.Id);
        Assert.Equal(BugReportStatus.Done, thread.Report.Status);
        Assert.Equal("Danke, Testpilot!", Assert.Single(thread.Replies).Text);

        // Confirming again thanks nobody twice.
        using (var again = await SendAsync(HttpMethod.Post, $"/api/reports/{client.Id}/confirm", content: Json("{}")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        Assert.Single(_host.Store.ListReplies(client.Id));
    }

    [Fact]
    public async Task Confirm_UsesTheOperatorsText_AndRefusesAReportThatIsNoPlaytestAsync()
    {
        string key = FeedbackReplyKey.Derive("confirm-text");
        var client = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, taskId: "task-without-store"), Now);
        var feedback = PlaytestTestPayloads.Stored(_host.Store, ReportHostTestPayloads.Feedback("plain-feedback"), Now - 100);

        using (var notPlaytest = await SendAsync(HttpMethod.Post, $"/api/reports/{feedback.Id}/confirm", content: Json("{}")))
        {
            Assert.Equal(HttpStatusCode.NotFound, notPlaytest.StatusCode);
        }

        using (var unknown = await SendAsync(HttpMethod.Post, "/api/reports/no-such-id/confirm", content: Json("{}")))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        using var confirm = await SendAsync(HttpMethod.Post, $"/api/reports/{client.Id}/confirm", content: Json("{\"text\":\"  Super getestet!  \"}"));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("Super getestet!", Assert.Single(_host.Store.ListReplies(client.Id)).Text);

        // No stored task and no text: the default, in the tester's language (the report says "de").
        var second = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("confirm-default"), taskId: "task-without-store"), Now - 500);
        Assert.Equal(PlaytestActions.DefaultThanksDe, PlaytestActions.ThanksFor(_host.Playtests, second));
    }

    // ---------------------------------------------------------------- the admin pages

    [Fact]
    public async Task Pages_AreTheOperatorsAlone_AndShowTheListTheFormAndTheCardAsync()
    {
        using (var anonymous = await SendAsync(HttpMethod.Get, "/admin/playtests", admin: false))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        StoreTask();
        string key = FeedbackReplyKey.Derive("pages-install");
        var report = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(key, withWork: true, fromAWorld: true), Now);

        string list = await PageAsync("/admin/playtests");
        Assert.Contains("Playtest tasks", list);
        Assert.Contains("The heavy shot", list);

        Assert.Contains("my-task", await PageAsync("/admin/playtests/new"));

        string edit = await PageAsync("/admin/playtests/heavy-shot");
        Assert.Contains("Preview", edit);
        Assert.Contains("Evaluation", edit);
        Assert.Contains(WebUtility.HtmlEncode("Fühlt er sich schwer an?"), edit); // stored text is HTML-encoded on the way out

        using (var missing = await SendAsync(HttpMethod.Get, "/admin/playtests/no-such-task"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        string detail = await PageAsync("/admin/report/" + report.Id);
        Assert.Contains("Playtest report", detail);
        Assert.Contains("did not work", detail);
        Assert.Contains(WebUtility.HtmlEncode("Kein Rückstoß."), detail);
        Assert.Contains("★★★★☆", detail);
        Assert.Contains("Hard", detail); // the chosen option in words, not its id
        Assert.Contains("preset High (picked automatically)", detail); // #3838: how the game was drawn
        Assert.Contains($"/admin/report/{report.Id}/attachment/0", detail);
        Assert.Contains($"/admin/report/{report.Id}/confirm", detail);
    }

    [Fact]
    public async Task SaveForm_NeedsTheToken_CountsTheRevisionUpOnlyForContent_AndShowsWhatIsWrongAsync()
    {
        string token = await TokenAsync();
        string json = PlaytestTestPayloads.Task.Replace("heavy-shot", "form-task").Replace("\"rev\": 2", "\"rev\": 1");

        using (var forged = await PostFormAsync("/admin/playtests/save", new() { ["json"] = json }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        }

        Assert.Null(_host.Playtests.Get("form-task"));

        using (var saved = await PostFormAsync("/admin/playtests/save", new() { ["json"] = json, ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, saved.StatusCode);
            Assert.Equal("/admin/playtests/form-task?saved=1", saved.Headers.Location!.ToString());
        }

        Assert.Equal(1, _host.Playtests.Get("form-task")!.Rev);

        // The same content again, and only the list position changed: the revision stays.
        using (var same = await PostFormAsync("/admin/playtests/save", new() { ["json"] = json.Replace("\"rev\": 1", "\"rev\": 1, \"sort\": 7"), ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, same.StatusCode);
        }

        Assert.Equal(1, _host.Playtests.Get("form-task")!.Rev);
        Assert.Equal(7, _host.Playtests.Get("form-task")!.Task().Sort);

        // A changed text: the revision counts up by itself.
        using (var changed = await PostFormAsync("/admin/playtests/save", new() { ["json"] = json.Replace("Worum es geht.", "Neu erklärt."), ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
        }

        Assert.Equal(2, _host.Playtests.Get("form-task")!.Rev);

        // Wrong content: the form comes back with the findings and the text as typed; nothing is stored.
        using var refused = await PostFormAsync("/admin/playtests/save", new() { ["json"] = "{ \"id\": \"broken-form\", \"steps\": [] }", ["csrf"] = token });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        string html = await refused.Content.ReadAsStringAsync();
        Assert.Contains("Not saved", html);
        Assert.Contains("broken-form", html);
        Assert.Null(_host.Playtests.Get("broken-form"));
    }

    [Fact]
    public async Task ToggleSwitchDeleteAndConfirmForms_WorkWithTheTokenOnlyAsync()
    {
        string token = await TokenAsync();
        Assert.True(PlaytestCatalogJson.TryReadTask(PlaytestTestPayloads.Task.Replace("heavy-shot", "toggle-task"), out var task, PlaytestReadMode.AsWritten));
        _host.Playtests.Save(task, Now, force: true);

        using (var forged = await PostFormAsync("/admin/playtests/toggle-task/toggle", new()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        }

        using (var toggled = await PostFormAsync("/admin/playtests/toggle-task/toggle", new() { ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, toggled.StatusCode);
        }

        Assert.False(_host.Playtests.Get("toggle-task")!.Task().Enabled);
        Assert.Equal(2, _host.Playtests.Get("toggle-task")!.Rev); // switching off is not a new revision

        using (var off = await PostFormAsync("/admin/playtests/switch", new() { ["enabled"] = "0", ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, off.StatusCode);
        }

        Assert.False(_host.Playtests.CatalogEnabled);
        using (var on = await PostFormAsync("/admin/playtests/switch", new() { ["enabled"] = "1", ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, on.StatusCode);
        }

        Assert.True(_host.Playtests.CatalogEnabled);

        var report = PlaytestTestPayloads.Stored(_host.Store, PlaytestTestPayloads.Report(FeedbackReplyKey.Derive("form-confirm")), Now - 900);
        using (var forgedConfirm = await PostFormAsync($"/admin/report/{report.Id}/confirm", new() { ["text"] = "forged" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forgedConfirm.StatusCode);
        }

        Assert.Empty(_host.Store.ListReplies(report.Id));
        using (var confirmed = await PostFormAsync($"/admin/report/{report.Id}/confirm", new() { ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, confirmed.StatusCode);
        }

        Assert.Equal(BugReportStatus.Done, _host.Store.Get(report.Id)!.Status);
        Assert.Single(_host.Store.ListReplies(report.Id));
        Assert.Contains("Confirmed", await PageAsync("/admin/report/" + report.Id));

        using (var deleted = await PostFormAsync("/admin/playtests/toggle-task/delete", new() { ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, deleted.StatusCode);
        }

        Assert.Null(_host.Playtests.Get("toggle-task"));
    }

    [Fact]
    public async Task DeleteForm_TakesAnIdThatIsNoTask_AndALoggedValueNeverBreaksTheLineAsync()
    {
        string token = await TokenAsync();

        // A route value is whatever the caller typed — here with an encoded line break in it. Nothing is stored
        // under it, so the page answers as for any delete and writes no "deleted" line.
        using (var unknown = await PostFormAsync("/admin/playtests/no-such-task%0Aforged/delete", new() { ["csrf"] = token }))
        {
            Assert.Equal(HttpStatusCode.Found, unknown.StatusCode);
        }

        Assert.Equal("heavy-shot", ReportHostRuntime.LogSafe("heavy-shot"));
        Assert.Equal("oneforged line", ReportHostRuntime.LogSafe("one\r\nforged line"));
        Assert.Equal(string.Empty, ReportHostRuntime.LogSafe(null));
    }
}

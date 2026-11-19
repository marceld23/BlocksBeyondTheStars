// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Text;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using BlocksBeyondTheStars.Shared.Notifications;
using BlocksBeyondTheStars.Shared.Playtests;
using Microsoft.AspNetCore.Builder;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The playtest flow end to end, headless: the REAL client classes (catalogue client, tracker, progress, report
/// builder, uploader, reply client) against the REAL report inbox started in-process on a loopback port. It is the
/// one place the two sides meet before a player does: a task authored in the inbox reaches the client, its steps are
/// ticked, the report lands in the inbox under its own category, the operator confirms it, and the badge turns gold —
/// also on a fresh install that only has its reply key left.
/// </summary>
public sealed class PlaytestInboxEndToEndTests : IAsyncLifetime, IDisposable
{
    private const string WriteKey = "e2e-write-key";
    private const string AdminUser = "admin";
    private const string AdminPassword = "e2e-pw";

    private const string TaskJson = """
        { "id": "heavy-shot", "rev": 1, "minVersion": "2026.11.1",
          "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
          "intro": { "de": "Worum es geht.", "en": "What it is about." },
          "steps": [ { "id": "launch", "text": { "de": "Starte ins All.", "en": "Launch into space." },
                       "check": { "event": "space:enter", "arg": "launch" } },
                     { "id": "feel", "text": { "de": "Fühlt er sich schwer an?", "en": "Does it feel heavy?" } } ],
          "questions": [ { "id": "fun", "type": "stars", "text": { "de": "Spaß?", "en": "How much fun?" } } ],
          "thanks": { "de": "Danke, Testpilot!", "en": "Thank you, test pilot!" } }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_pte2e_" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private ReportStore _store = null!;
    private HttpClient _admin = null!;
    private string _base = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var config = new ReportHostConfig
        {
            BindAddress = "127.0.0.1",
            Port = 0, // Kestrel picks a free port
            DataDir = Path.Combine(_root, "inbox"),
            WriteKey = WriteKey,
            AdminUser = AdminUser,
            AdminPassword = AdminPassword,
        };
        _store = new ReportStore(config, Path.Combine(_root, "inbox", "reports.db"));
        _app = ReportHostApp.Create(config, _store, new AdminNotifier(string.Empty, "reports"), Array.Empty<string>());
        await _app.StartAsync();
        _base = _app.Urls.First();
        _admin = new HttpClient { BaseAddress = new Uri(_base) };
        _admin.DefaultRequestHeaders.TryAddWithoutValidation("Authorization",
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(AdminUser + ":" + AdminPassword)));
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Runs after <see cref="DisposeAsync"/>: the host is down, so its store and the temp folder can go.</summary>
    public void Dispose()
    {
        _admin.Dispose();
        _store.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }

    private string Ingest => _base + "/api/bugreport";

    private string InstallDir(string name) => Path.Combine(_root, name, "playtests");

    private PlaytestCatalogClient CatalogClient(string install)
        => new(PlaytestCatalogClient.EndpointFor(Ingest), WriteKey, Path.Combine(InstallDir(install), "catalog.json"));

    private async Task AuthorTaskAsync(string json)
    {
        using var put = await _admin.PutAsync("/api/playtests/heavy-shot", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    [Fact]
    public async Task ATask_GoesFromTheInboxToTheClient_AndItsReportComesBackConfirmedAsync()
    {
        const string Version = "2026.11.22";
        string replyKey = FeedbackReplyKey.Derive("e2e-install-secret");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await AuthorTaskAsync(TaskJson);

        // --- the client fetches the catalogue, and asks again cheaply ---
        var catalogClient = CatalogClient("install-a");
        Assert.True(catalogClient.ShouldFetch(now, playerAsked: false));
        var fetched = await Task.Run(() => catalogClient.Fetch(now));
        Assert.True(fetched.Ok, fetched.Error);
        Assert.False(fetched.NotModified);
        Assert.False(catalogClient.ShouldFetch(now + 3600, playerAsked: false));
        Assert.True(catalogClient.ShouldFetch(now + 3600, playerAsked: true));
        var again = await Task.Run(() => catalogClient.Fetch(now + 3600));
        Assert.True(again.Ok && again.NotModified);
        Assert.Equal("heavy-shot", Assert.Single(again.Catalog.Tasks).Id); // "not modified" still shows the cached task
        Assert.Equal(now + 3600, CatalogClient("install-a").Cached().FetchedUnix); // a new start reads the same cache

        // --- the tracker shows the task to this build, the player works on it ---
        var progress = new PlaytestProgress(Path.Combine(InstallDir("install-a"), "progress.json"));
        var tracker = new PlaytestTracker(progress);
        tracker.SetCatalog(fetched.Catalog, "2026.10.1");
        Assert.Empty(tracker.Tasks); // an older build does not see a task made for a newer one
        tracker.SetCatalog(fetched.Catalog, Version);
        var task = Assert.Single(tracker.Tasks);
        Assert.Equal(1, tracker.NewCount);

        var recognised = new List<string>();
        tracker.StepRecognised += (_, step) => recognised.Add(step.Id);
        Assert.True(tracker.Activate(task.Id, now));
        Assert.Equal(new[] { "space:enter" }, tracker.WatchPatterns());
        Assert.Equal(1, tracker.OnEvent("space:enter", "launch", 1, now + 10));
        Assert.Equal(new[] { "launch" }, recognised);
        Assert.Empty(tracker.WatchPatterns()); // nothing left for the server to watch
        Assert.Equal("feel", tracker.CurrentStep!.Id);
        progress.SetStep(task, "feel", PlaytestStepStates.Failed, "Kein Rückstoß zu spüren.", now + 20);

        // --- the report goes through the ordinary feedback channel ---
        var form = new PlaytestReportForm { Locale = "de", Nickname = "Layex", FreeText = "Der Ring ist schwer zu sehen." };
        form.Answers["fun"] = "4";
        form.Work = PlaytestWork.OfFields("form", new[] { new KeyValuePair<string, string?>("draft", "{\"kind\":\"form\"}") }); // "send my work along"
        var built = PlaytestReport.Build(task, progress, form, Version, replyKey, "session", "WindowsPlayer", DateTime.UtcNow);
        Assert.Equal(PlaytestReportRefusal.None, built.Refusal);
        var upload = await Task.Run(() => new FeedbackUploader(Ingest, WriteKey).Upload(built.Report!, null));
        Assert.True(upload.Ok, upload.Error);
        progress.RecordSent(task, upload.ReportId, now + 30);
        Assert.Equal(PlaytestTaskStatus.Submitted, progress.StatusOf(task));
        Assert.Equal(0, tracker.NewCount);

        // --- the inbox filed it under its own category, readable without a special view ---
        var stored = _store.Get(upload.ReportId)!;
        Assert.Equal(ReportIngest.PlaytestCategory, stored.Category);
        Assert.Equal(replyKey, stored.ReplyKey);
        Assert.Equal(string.Empty, stored.Email);
        Assert.Equal(string.Empty, stored.PlayerId);
        Assert.Contains("DID NOT WORK", stored.Description);
        var data = PlaytestReportData.Read(stored)!;
        Assert.Equal(4, data.Stars("fun"));
        Assert.True(data.Steps.Single(s => s.Id == "feel").Failed);

        // --- the work the player sent along is kept next to the report, as the file it was ---
        var work = Assert.Single(_store.Attachments(upload.ReportId));
        Assert.Equal("application/json", work.Mime);
        string workText = await File.ReadAllTextAsync(_store.AttachmentPath(work)!);
        Assert.Contains("\"editor\":\"form\"", workText);
        Assert.Contains("\"draft\":", workText);

        // --- nothing is confirmed yet; then the operator confirms ---
        var mine = await Task.Run(() => catalogClient.FetchMine(replyKey));
        Assert.False(Assert.Single(mine!).Confirmed);
        Assert.Empty(tracker.ApplyOwnReports(mine!, now + 40));
        using (var confirm = await _admin.PostAsync($"/api/reports/{upload.ReportId}/confirm", new StringContent("{}", Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        }

        // --- the thank-you arrives through the reply channel, and the badge turns gold ---
        var replies = await Task.Run(() =>
            new FeedbackReplyClient(FeedbackReplyClient.EndpointFor(Ingest), WriteKey).Fetch(replyKey, 0, new[] { upload.ReportId }));
        var thread = Assert.Single(replies.Threads);
        Assert.Equal("done", thread.Status);
        Assert.Equal("Danke, Testpilot!", Assert.Single(thread.Replies).Text);

        mine = await Task.Run(() => catalogClient.FetchMine(replyKey));
        Assert.Equal(new[] { "heavy-shot" }, tracker.ApplyOwnReports(mine!, now + 50));
        Assert.Empty(tracker.ApplyOwnReports(mine!, now + 60)); // told once
        Assert.Equal(PlaytestTaskStatus.Confirmed, progress.StatusOf(task));
        Assert.Equal((1, 1), progress.BadgeCount());

        // --- a fresh install with the same reply key gets its badge back ---
        var freshProgress = new PlaytestProgress(Path.Combine(InstallDir("install-b"), "progress.json"));
        var freshTracker = new PlaytestTracker(freshProgress);
        freshTracker.SetCatalog(fetched.Catalog, Version);
        Assert.Equal((0, 0), freshProgress.BadgeCount());
        freshTracker.ApplyOwnReports(mine!, now + 70);
        Assert.Equal((1, 1), freshProgress.BadgeCount());
        Assert.Equal(PlaytestTaskStatus.Confirmed, freshProgress.StatusOf(task));
        Assert.False(freshTracker.Activate(task.Id, now + 70)); // done is done — until the developers ask for a re-test
    }

    [Fact]
    public async Task AReopenedTask_ReachesTheClientAsARetest_AndTheSwitchedOffCatalogueShowsNothingAsync()
    {
        const string Version = "2026.11.22";
        string replyKey = FeedbackReplyKey.Derive("e2e-retest-secret");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await AuthorTaskAsync(TaskJson);
        var catalogClient = CatalogClient("install-c");
        var progress = new PlaytestProgress(Path.Combine(InstallDir("install-c"), "progress.json"));
        var tracker = new PlaytestTracker(progress);
        tracker.SetCatalog((await Task.Run(() => catalogClient.Fetch(now))).Catalog, Version);
        var task = tracker.Find("heavy-shot")!;
        tracker.Activate(task.Id, now);
        tracker.OnEvent("space:enter", "launch", 1, now);
        progress.SetStep(task, "feel", PlaytestStepStates.Done, null, now);
        var form = new PlaytestReportForm();
        form.Answers["fun"] = "5";
        var report = PlaytestReport.Build(task, progress, form, Version, replyKey, "s", "WindowsPlayer", DateTime.UtcNow).Report!;
        var upload = await Task.Run(() => new FeedbackUploader(Ingest, WriteKey).Upload(report, null));
        progress.RecordSent(task, upload.ReportId, now);

        // The developers fixed something and ask for another look: a higher revision that says "retest".
        await AuthorTaskAsync(TaskJson.Replace("\"rev\": 1", "\"rev\": 2, \"retest\": true"));
        var refetched = await Task.Run(() => catalogClient.Fetch(now + 100));
        Assert.True(refetched.Ok);
        Assert.False(refetched.NotModified); // the ETag follows the content
        tracker.SetCatalog(refetched.Catalog, Version);
        var reopened = tracker.Find("heavy-shot")!;
        Assert.Equal(PlaytestTaskStatus.Retest, progress.StatusOf(reopened));
        Assert.Equal(1, tracker.NewCount);
        Assert.True(tracker.Activate(reopened.Id, now + 100));
        Assert.Equal(2, progress.OpenSteps(reopened));
        Assert.Equal((1, 0), progress.BadgeCount()); // the earned badge stays through the re-test

        // The kill switch: every client shows nothing, the task being worked on is put aside.
        using (var off = await _admin.PostAsync("/api/playtests/enabled", new StringContent("{\"enabled\":false}", Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }

        tracker.SetCatalog((await Task.Run(() => catalogClient.Fetch(now + 200))).Catalog, Version);
        Assert.Empty(tracker.Tasks);
        Assert.Null(tracker.Active);
    }

    [Fact]
    public async Task WithoutAKeyOrAConnection_TheClientKeepsWhatItHas_AndNeverThrowsAsync()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await AuthorTaskAsync(TaskJson);
        string cache = Path.Combine(InstallDir("install-d"), "catalog.json");

        var noKey = new PlaytestCatalogClient(PlaytestCatalogClient.EndpointFor(Ingest), string.Empty, cache);
        Assert.False(noKey.IsConfigured);
        Assert.False(noKey.ShouldFetch(now, playerAsked: true));
        Assert.Equal("not_configured", noKey.Fetch(now).Error);
        Assert.Null(noKey.FetchMine(FeedbackReplyKey.Derive("x")));

        var wrongKey = new PlaytestCatalogClient(PlaytestCatalogClient.EndpointFor(Ingest), "wrong", cache);
        var refused = await Task.Run(() => wrongKey.Fetch(now));
        Assert.False(refused.Ok);
        Assert.Equal("http_403", refused.Error);
        Assert.False(refused.Catalog.Enabled); // nothing cached yet: an empty, switched-off catalogue

        var good = new PlaytestCatalogClient(PlaytestCatalogClient.EndpointFor(Ingest), WriteKey, cache);
        Assert.True((await Task.Run(() => good.Fetch(now))).Ok);

        // The inbox is unreachable: the cached catalogue is what the page shows.
        var offline = new PlaytestCatalogClient("http://127.0.0.1:9/api/playtests", WriteKey, cache,
            new HttpClient { Timeout = TimeSpan.FromSeconds(2) });
        var failed = await Task.Run(() => offline.Fetch(now + 10));
        Assert.False(failed.Ok);
        Assert.NotEqual(string.Empty, failed.Error);
        Assert.Equal("heavy-shot", Assert.Single(failed.Catalog.Tasks).Id);
        Assert.Null(offline.FetchMine(FeedbackReplyKey.Derive("x")));
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.ReportHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Bug-report inbox (ReportHost): the Wix-compatible ingest parser, the SQLite + screenshot-file store
/// with its keyset pagination and retention pruning, the per-IP rate limiter, and the admin Basic-Auth
/// check — all pure logic, no HTTP server needed (the Program.cs wiring stays thin, like WorldHost).
/// </summary>
public sealed class ReportHostTests : IDisposable
{
    private readonly string _root;
    private readonly List<ReportStore> _stores = new();

    public ReportHostTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rh_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private ReportStore NewStore(ReportHostConfig? config = null)
    {
        string dir = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var store = new ReportStore(config ?? new ReportHostConfig(), System.IO.Path.Combine(dir, "reports.db"));
        _stores.Add(store);
        return store;
    }

    /// <summary>A payload exactly as the game's FeedbackUploader serializes it (camelCase FeedbackReport).</summary>
    private static string FeedbackPayload(string description = "The door on my ship eats my hat.", bool withScreenshot = false)
    {
        var report = new Dictionary<string, object?>
        {
            ["title"] = "Hat eaten by door",
            ["description"] = description,
            ["email"] = "pilot@example.com",
            ["gameVersion"] = "0.4.2",
            ["buildNumber"] = "1234",
            ["playerId"] = "token-abc",
            ["playerName"] = "Justus",
            ["sessionId"] = "s-1",
            ["platform"] = "WindowsPlayer",
            ["clientTimestamp"] = "2026-07-04T12:00:00Z",
            ["reportJson"] = new Dictionary<string, object?> { ["scene"] = "planet", ["seed"] = 42 },
        };
        if (withScreenshot)
        {
            report["screenshot"] = new Dictionary<string, object?>
            {
                ["fileName"] = "feedback.jpg",
                ["mimeType"] = "image/jpeg",
                ["base64"] = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }),
            };
        }

        return JsonSerializer.Serialize(report);
    }

    /// <summary>A payload shaped like CrashReportWriter's automatic server crash report.</summary>
    private static string CrashPayload() => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["title"] = "Server crash [tick-fault] Creatures: NullReferenceException",
        ["description"] = "System.NullReferenceException: boom\n\nat Tick()",
        ["email"] = "",
        ["gameVersion"] = "0.4.2",
        ["platform"] = "server",
        ["clientTimestamp"] = "2026-07-04T12:00:00Z",
        ["reportJson"] = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["kind"] = "tick-fault",
            ["source"] = "server",
            ["world"] = "world_001",
        },
    });

    // ---------------- Ingest parsing ----------------

    [Fact]
    public void Parse_FeedbackPayload_ExtractsFields_AndScreenshot()
    {
        var parsed = ReportIngest.Parse(FeedbackPayload(withScreenshot: true), new ReportHostConfig(), out var error);

        Assert.NotNull(parsed);
        Assert.Equal("", error);
        Assert.Equal("Hat eaten by door", parsed!.Title);
        Assert.Equal("pilot@example.com", parsed.Email);
        Assert.Equal("Justus", parsed.PlayerName);
        Assert.Equal("feedback", parsed.Category);
        Assert.Equal("", parsed.Kind);
        Assert.NotNull(parsed.ScreenshotBytes);
        Assert.Equal("jpg", parsed.ScreenshotExtension);

        // The stored raw JSON keeps everything EXCEPT the screenshot (no megabytes of base64 in the DB).
        using var doc = JsonDocument.Parse(parsed.ReportJson);
        Assert.False(doc.RootElement.TryGetProperty("screenshot", out _));
        Assert.Equal("Hat eaten by door", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("reportJson").GetProperty("seed").GetInt32());
    }

    [Fact]
    public void Parse_CrashPayload_IsCategorizedAsCrash()
    {
        var parsed = ReportIngest.Parse(CrashPayload(), new ReportHostConfig(), out _);

        Assert.NotNull(parsed);
        Assert.Equal("crash", parsed!.Category);
        Assert.Equal("tick-fault", parsed.Kind);
        Assert.Equal("server", parsed.Source);
        Assert.Null(parsed.ScreenshotBytes);
    }

    [Fact]
    public void Parse_RejectsMissingDescription_AndInvalidJson()
    {
        Assert.Null(ReportIngest.Parse("{\"title\":\"no text\"}", new ReportHostConfig(), out var e1));
        Assert.Equal("empty_description", e1);

        Assert.Null(ReportIngest.Parse("{\"description\":\"   \"}", new ReportHostConfig(), out var e2));
        Assert.Equal("empty_description", e2);

        Assert.Null(ReportIngest.Parse("not json at all", new ReportHostConfig(), out var e3));
        Assert.Equal("invalid_json", e3);

        Assert.Null(ReportIngest.Parse("[1,2,3]", new ReportHostConfig(), out var e4));
        Assert.Equal("invalid_json", e4);
    }

    [Fact]
    public void Parse_OversizedOrBrokenScreenshot_DropsImage_KeepsReport()
    {
        var config = new ReportHostConfig { MaxScreenshotBase64Length = 8 };
        var oversized = ReportIngest.Parse(FeedbackPayload(withScreenshot: true), config, out _);
        Assert.NotNull(oversized);
        Assert.Null(oversized!.ScreenshotBytes);

        string broken = FeedbackPayload(withScreenshot: true).Replace(
            Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }), "!!!not-base64!!!");
        var parsed = ReportIngest.Parse(broken, new ReportHostConfig(), out _);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.ScreenshotBytes);
    }

    [Fact]
    public void Parse_CapsDescriptionAndTitle()
    {
        var config = new ReportHostConfig { MaxDescriptionLength = 10, MaxTitleLength = 5 };
        var parsed = ReportIngest.Parse(FeedbackPayload(description: new string('x', 100)), config, out _);

        Assert.NotNull(parsed);
        Assert.Equal(10, parsed!.Description.Length);
        Assert.Equal(5, parsed.Title.Length);
    }

    // ---------------- Store ----------------

    [Fact]
    public void Add_Get_RoundTrips_WithScreenshotFile()
    {
        var store = NewStore();
        var parsed = ReportIngest.Parse(FeedbackPayload(withScreenshot: true), new ReportHostConfig(), out _)!;

        string id = store.Add(parsed, nowUnix: 1000);
        var record = store.Get(id);

        Assert.NotNull(record);
        Assert.Equal("Hat eaten by door", record!.Title);
        Assert.Equal(BugReportStatus.New, record.Status);
        Assert.Equal(1000, record.CreatedUnix);
        Assert.Equal(id + ".jpg", record.ScreenshotFile);

        string? path = store.ScreenshotPath(record);
        Assert.NotNull(path);
        Assert.True(System.IO.File.Exists(path));
        Assert.Null(store.Get("does-not-exist"));
    }

    [Fact]
    public void Export_LatestWithBigLimit_ReturnsAllMatching_AndSerializes()
    {
        // Backs the admin UI's "Download JSON" button: Latest with the export-sized limit must return
        // every matching report (not the page's 200) and the records must serialize as one JSON blob.
        var store = NewStore();
        store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1000);
        store.Add(ReportIngest.Parse(CrashPayload(), new ReportHostConfig(), out _)!, nowUnix: 2000);

        var all = store.Latest(status: null, category: null, limit: 100_000);
        Assert.Equal(2, all.Count);

        var onlyCrash = store.Latest(status: null, category: "crash", limit: 100_000);
        Assert.Single(onlyCrash);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { count = all.Count, reports = all });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("reports").GetArrayLength());
    }

    [Fact]
    public void Query_PaginatesWithKeysetCursor_AndFilters()
    {
        var store = NewStore();
        var config = new ReportHostConfig();
        for (int i = 0; i < 5; i++)
        {
            store.Add(ReportIngest.Parse(FeedbackPayload(description: "report " + i), config, out _)!, nowUnix: 1000 + i);
        }

        store.Add(ReportIngest.Parse(CrashPayload(), config, out _)!, nowUnix: 2000);

        // Page 1 of 3 → cursor → page 2; ascending created order, no gaps, no repeats.
        var (page1, more1) = store.Query(limit: 3);
        Assert.Equal(3, page1.Count);
        Assert.True(more1);
        var (page2, more2) = store.Query(limit: 3, afterCreatedUnix: page1[^1].CreatedUnix, afterId: page1[^1].Id);
        Assert.Equal(3, page2.Count);
        Assert.False(more2);
        Assert.Empty(page1.Select(r => r.Id).Intersect(page2.Select(r => r.Id)));
        Assert.Equal("report 0", page1[0].Description);

        // since filter is exclusive (createdAt > since) so delta syncs never re-fetch the last row.
        var (delta, _) = store.Query(sinceUnix: 1003);
        Assert.Equal(2, delta.Count);

        var (crashes, _) = store.Query(category: "crash");
        Assert.Single(crashes);
        Assert.Equal("tick-fault", crashes[0].Kind);

        var (fromServer, _) = store.Query(source: "server");
        Assert.Single(fromServer);
    }

    [Fact]
    public void SetStatus_ValidatesAndUpdates()
    {
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1000);

        Assert.True(store.SetStatus(id, BugReportStatus.Triaged));
        Assert.Equal(BugReportStatus.Triaged, store.Get(id)!.Status);

        Assert.False(store.SetStatus(id, "nonsense"));
        Assert.False(store.SetStatus("missing", BugReportStatus.Done));

        var (triaged, _) = store.Query(status: BugReportStatus.Triaged);
        Assert.Single(triaged);
    }

    [Fact]
    public void Delete_RemovesRowAndScreenshotFile()
    {
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(withScreenshot: true), new ReportHostConfig(), out _)!, nowUnix: 1000);
        string path = store.ScreenshotPath(store.Get(id)!)!;

        Assert.True(store.Delete(id));
        Assert.Null(store.Get(id));
        Assert.False(System.IO.File.Exists(path));
        Assert.False(store.Delete(id));
    }

    [Fact]
    public void Prune_RemovesOldReports_IncludingScreenshots_ZeroKeepsForever()
    {
        var store = NewStore();
        var config = new ReportHostConfig();
        string oldId = store.Add(ReportIngest.Parse(FeedbackPayload(withScreenshot: true), config, out _)!, nowUnix: 0);
        string newId = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 100 * 86400);
        string oldShot = store.ScreenshotPath(store.Get(oldId)!)!;

        Assert.Equal(0, store.Prune(retentionDays: 0, nowUnix: 200 * 86400));

        Assert.Equal(1, store.Prune(retentionDays: 30, nowUnix: 100 * 86400));
        Assert.Null(store.Get(oldId));
        Assert.NotNull(store.Get(newId));
        Assert.False(System.IO.File.Exists(oldShot));
    }

    [Fact]
    public void CountByStatus_ReportsBuckets()
    {
        var store = NewStore();
        var config = new ReportHostConfig();
        string a = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 1);
        store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 2);
        store.SetStatus(a, BugReportStatus.Done);

        var counts = store.CountByStatus();
        Assert.Equal(1, counts[BugReportStatus.New]);
        Assert.Equal(1, counts[BugReportStatus.Done]);
    }

    // ---------------- Reply threads (#1327) ----------------

    private static string KeyFor(string secret) => BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.Derive(secret);

    [Fact]
    public void ReplyKey_IsDerivedFromPlayerId_WhenClientSentNone_AndKeptWhenSent()
    {
        var store = NewStore();
        var config = new ReportHostConfig();

        // Old client: playerId only → the store derives the key with the shared formula.
        string legacy = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 1);
        Assert.Equal(KeyFor("token-abc"), store.Get(legacy)!.ReplyKey);

        // New client: sends its own key → stored verbatim; a malformed one is ignored (derived instead).
        string sent = KeyFor("some-other-secret");
        string withKey = FeedbackPayload().Replace("\"playerId\":\"token-abc\"", $"\"playerId\":\"token-abc\",\"replyKey\":\"{sent}\"");
        Assert.Equal(sent, store.Get(store.Add(ReportIngest.Parse(withKey, config, out _)!, nowUnix: 2))!.ReplyKey);

        string bad = FeedbackPayload().Replace("\"playerId\":\"token-abc\"", "\"playerId\":\"token-abc\",\"replyKey\":\"NOT-A-KEY\"");
        Assert.Equal(KeyFor("token-abc"), store.Get(store.Add(ReportIngest.Parse(bad, config, out _)!, nowUnix: 3))!.ReplyKey);

        // A crash report without a player id gets no key at all — nothing can be routed to a player.
        Assert.Equal("", store.Get(store.Add(ReportIngest.Parse(CrashPayload(), config, out _)!, nowUnix: 4))!.ReplyKey);
    }

    [Fact]
    public void BackfillReplyKeys_FillsOnlyEmptyRows_AndIsIdempotent()
    {
        var store = NewStore();
        var parsed = ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!;
        parsed.ReplyKey = KeyFor("explicit");
        string kept = store.Add(parsed, nowUnix: 1);

        // Simulate a pre-#1327 row: key column empty although a player id is present.
        string legacy = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 2);
        Assert.True(store.SetReplyKey(legacy, ""));

        Assert.Equal(1, store.BackfillReplyKeys());
        Assert.Equal(KeyFor("token-abc"), store.Get(legacy)!.ReplyKey);
        Assert.Equal(KeyFor("explicit"), store.Get(kept)!.ReplyKey);
        Assert.Equal(0, store.BackfillReplyKeys());
    }

    /// <summary>What the game server forwards for a /bump (GameServerBump.ForwardBumpSnapshot): the player id
    /// is the player NAME, and the reply key is whatever the client passed through — or nothing.</summary>
    private static string ServerBumpPayload(string replyKey = "") => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["title"] = "Bump [Minecraft]: [feedback] Hat eaten by door — The door on my ship eats my hat.",
        ["description"] = "[feedback] Hat eaten by door — The door on my ship eats my hat.",
        ["email"] = "",
        ["gameVersion"] = "0.4.2",
        ["playerId"] = "Justus",
        ["playerName"] = "Justus",
        ["platform"] = "server",
        ["replyKey"] = replyKey,
        ["reportJson"] = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["reportType"] = "bump",
            ["source"] = "server",
            ["snapshot"] = new Dictionary<string, object?>(),
        },
    });

    /// <summary>#1359: a server forward's player id is the public player name — deriving a key from it would
    /// hand anyone who knows the name the reply thread, and the client never polls with it anyway. Only a key
    /// the client passed through /bump is kept.</summary>
    [Fact]
    public void ServerForward_NeverGetsAKeyDerivedFromThePlayerName_ButKeepsAPassedThroughOne()
    {
        var store = NewStore();
        var config = new ReportHostConfig();

        string bare = store.Add(ReportIngest.Parse(ServerBumpPayload(), config, out _)!, nowUnix: 1);
        Assert.Equal("", store.Get(bare)!.ReplyKey);

        string key = KeyFor("token-abc");
        string keyed = store.Add(ReportIngest.Parse(ServerBumpPayload(key), config, out _)!, nowUnix: 2);
        Assert.Equal(key, store.Get(keyed)!.ReplyKey);

        // The client-direct half of the same report keeps its derived key — that is the reporter's own secret.
        string client = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 2);
        Assert.Equal(key, store.Get(client)!.ReplyKey);
    }

    [Fact]
    public void BackfillReplyKeys_SkipsServerForwards()
    {
        var store = NewStore();
        var config = new ReportHostConfig();
        string server = store.Add(ReportIngest.Parse(ServerBumpPayload(), config, out _)!, nowUnix: 1);
        string client = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 1);
        Assert.True(store.SetReplyKey(client, ""));

        Assert.Equal(1, store.BackfillReplyKeys());
        Assert.Equal(KeyFor("token-abc"), store.Get(client)!.ReplyKey);
        Assert.Equal("", store.Get(server)!.ReplyKey);
    }

    /// <summary>Rows the pre-#1359 store already stamped with a name-derived key are repaired once at startup;
    /// a key the client passed through, and every client-direct row, stay untouched.</summary>
    [Fact]
    public void RevokeNameDerivedServerKeys_ClearsOnlyDerivedKeysOnServerRows()
    {
        var store = NewStore();
        var config = new ReportHostConfig();

        string derived = store.Add(ReportIngest.Parse(ServerBumpPayload(), config, out _)!, nowUnix: 1);
        Assert.True(store.SetReplyKey(derived, KeyFor("Justus"))); // what the old store did
        string passedThrough = store.Add(ReportIngest.Parse(ServerBumpPayload(KeyFor("token-abc")), config, out _)!, nowUnix: 2);
        string client = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 3);

        Assert.Equal(1, store.RevokeNameDerivedServerKeys());
        Assert.Equal("", store.Get(derived)!.ReplyKey);
        Assert.Equal(KeyFor("token-abc"), store.Get(passedThrough)!.ReplyKey);
        Assert.Equal(KeyFor("token-abc"), store.Get(client)!.ReplyKey);
        Assert.Equal(0, store.RevokeNameDerivedServerKeys());
    }

    [Fact]
    public void DevReply_ShowsUpForOwnerOnly_QuestionFlipsStatus_AckHidesIt()
    {
        var store = NewStore();
        string key = KeyFor("token-abc");
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1);

        // Nothing to pull before an answer exists.
        Assert.Empty(store.UnreadThreads(key));

        long answer = store.AddDevReply(id, "Thanks — fixed!", isQuestion: false, nowUnix: 10);
        Assert.True(answer > 0);
        Assert.Equal(BugReportStatus.New, store.Get(id)!.Status); // a plain answer leaves the status alone
        Assert.Equal(-1, store.AddDevReply("missing", "x", false, 10));

        var threads = store.UnreadThreads(key);
        Assert.Single(threads);
        Assert.Equal(id, threads[0].Report.Id);
        Assert.Single(threads[0].Replies);
        Assert.Equal(ReplyRecord.AuthorDev, threads[0].Replies[0].Author);

        // Another install's key sees nothing; a malformed key sees nothing.
        Assert.Empty(store.UnreadThreads(KeyFor("someone-else")));
        Assert.Empty(store.UnreadThreads("garbage"));

        // A question flips the report to waiting_for_player.
        long question = store.AddDevReply(id, "Does it happen without the helmet?", isQuestion: true, nowUnix: 11);
        Assert.Equal(BugReportStatus.WaitingForPlayer, store.Get(id)!.Status);

        // Ack: only the owner's key can mark entries read; afterwards the thread drops out of the poll.
        Assert.Equal(0, store.AckReplies(KeyFor("someone-else"), new[] { answer, question }, 20));
        Assert.Single(store.UnreadThreads(key));
        Assert.Equal(2, store.AckReplies(key, new[] { answer, question, 999L }, 20));
        Assert.Empty(store.UnreadThreads(key));
        Assert.All(store.ListReplies(id), r => Assert.True(r.SeenUnix > 0));

        // since filter: entries created at/before `since` don't qualify.
        long late = store.AddDevReply(id, "one more", false, 30);
        Assert.Empty(store.UnreadThreads(key, sinceUnix: 30));
        Assert.Single(store.UnreadThreads(key, sinceUnix: 29));
        Assert.True(late > 0);
    }

    [Fact]
    public void PlayerReply_RequiresOwnership_AnExistingDevEntry_AndRespectsTheLimit()
    {
        var store = NewStore();
        string key = KeyFor("token-abc");
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1);

        Assert.Equal(-2, store.AddPlayerReply(key, id, "hello?", 5));             // nothing to answer yet
        store.AddDevReply(id, "Does it happen without the helmet?", true, 10);
        Assert.Equal(-1, store.AddPlayerReply(KeyFor("other"), id, "yes", 11));  // not the owner
        Assert.Equal(-1, store.AddPlayerReply(key, "missing", "yes", 11));

        Assert.True(store.AddPlayerReply(key, id, "Yes, also without it.", 12) > 0);
        Assert.Equal(BugReportStatus.PlayerReplied, store.Get(id)!.Status);
        Assert.True(store.AddPlayerReply(key, id, "second", 13) > 0);
        Assert.True(store.AddPlayerReply(key, id, "third", 14) > 0);
        Assert.Equal(-3, store.AddPlayerReply(key, id, "fourth", 15));          // MaxPlayerRepliesPerReport

        var thread = store.ListReplies(id);
        Assert.Equal(4, thread.Count);
        Assert.Equal(3, thread.Count(r => r.Author == ReplyRecord.AuthorPlayer));
        Assert.All(thread.Where(r => r.Author == ReplyRecord.AuthorPlayer), r => Assert.True(r.SeenUnix > 0));
    }

    [Fact]
    public void FixedInVersion_RoundTrips()
    {
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1);
        Assert.Equal("", store.Get(id)!.FixedInVersion);
        Assert.True(store.SetFixedInVersion(id, " 2026.8.23 "));
        Assert.Equal("2026.8.23", store.Get(id)!.FixedInVersion);
        Assert.False(store.SetFixedInVersion("missing", "1"));
    }

    [Fact]
    public void Delete_AndPrune_RemoveReplyThreads()
    {
        var store = NewStore();
        var config = new ReportHostConfig();
        string deleted = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 0);
        string pruned = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 1);
        string kept = store.Add(ReportIngest.Parse(FeedbackPayload(), config, out _)!, nowUnix: 100 * 86400);
        foreach (var id in new[] { deleted, pruned, kept })
        {
            store.AddDevReply(id, "hi", false, 5);
        }

        Assert.True(store.Delete(deleted));
        Assert.Empty(store.ListReplies(deleted));

        Assert.Equal(1, store.Prune(retentionDays: 30, nowUnix: 100 * 86400));
        Assert.Empty(store.ListReplies(pruned));
        Assert.Single(store.ListReplies(kept));
    }

    [Fact]
    public void ExistingDatabase_GetsReplyColumnsAdded()
    {
        // A pre-#1327 schema (no reply_key / fixed_in_version, no report_reply table) must open and work.
        string dir = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string db = System.IO.Path.Combine(dir, "reports.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE bugreport(
                    id TEXT PRIMARY KEY, title TEXT NOT NULL DEFAULT '', description TEXT NOT NULL DEFAULT '',
                    email TEXT NOT NULL DEFAULT '', game_version TEXT NOT NULL DEFAULT '', build_number TEXT NOT NULL DEFAULT '',
                    player_id TEXT NOT NULL DEFAULT '', player_name TEXT NOT NULL DEFAULT '', session_id TEXT NOT NULL DEFAULT '',
                    platform TEXT NOT NULL DEFAULT '', client_timestamp TEXT NOT NULL DEFAULT '', category TEXT NOT NULL DEFAULT 'feedback',
                    source TEXT NOT NULL DEFAULT '', kind TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'new',
                    screenshot_file TEXT NOT NULL DEFAULT '', report_json TEXT NOT NULL DEFAULT '{}', created_unix INTEGER NOT NULL);
                INSERT INTO bugreport(id, description, player_id, created_unix) VALUES('old1', 'legacy row', 'token-old', 5);
                """;
            cmd.ExecuteNonQuery();
        }

        var store = new ReportStore(new ReportHostConfig(), db);
        _stores.Add(store);
        var legacy = store.Get("old1");
        Assert.NotNull(legacy);
        Assert.Equal("", legacy!.ReplyKey);
        Assert.Equal(1, store.BackfillReplyKeys());
        Assert.Equal(KeyFor("token-old"), store.Get("old1")!.ReplyKey);
        Assert.True(store.AddDevReply("old1", "welcome back", false, 6) > 0);
        Assert.Single(store.UnreadThreads(KeyFor("token-old")));
    }

    [Fact]
    public void Status_AcceptsTheReplyStates()
    {
        Assert.True(BugReportStatus.IsValid(BugReportStatus.WaitingForPlayer));
        Assert.True(BugReportStatus.IsValid(BugReportStatus.PlayerReplied));
        Assert.Equal(5, BugReportStatus.All.Length);

        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1);
        Assert.True(store.SetStatus(id, BugReportStatus.WaitingForPlayer));
        var (waiting, _) = store.Query(status: BugReportStatus.WaitingForPlayer);
        Assert.Single(waiting);
    }

    [Fact]
    public void DetailPage_RendersThread_EncodesHostileText_AndOffersTheForm()
    {
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(FeedbackPayload(), new ReportHostConfig(), out _)!, nowUnix: 1);
        store.AddDevReply(id, "Does it happen <b>always</b>?", true, 2);
        store.AddPlayerReply(KeyFor("token-abc"), id, "<script>alert(1)</script> yes", 3);
        store.SetFixedInVersion(id, "2026.8.23");

        string html = ReportHostPages.Detail(store.Get(id)!, store.ListReplies(id));
        Assert.Contains("Conversation with the player", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; yes", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("Does it happen &lt;b&gt;always&lt;/b&gt;?", html);
        Assert.Contains($"/admin/report/{id}/reply", html);
        Assert.Contains("2026.8.23", html);
        Assert.Contains("mark waiting_for_player", html);

        // A key-less report says so instead of offering a thread that can never reach anyone.
        string crashId = store.Add(ReportIngest.Parse(CrashPayload(), new ReportHostConfig(), out _)!, nowUnix: 4);
        Assert.Contains("no reply key", ReportHostPages.Detail(store.Get(crashId)!, store.ListReplies(crashId)));
    }

    // ---------------- Rate limiter ----------------

    [Fact]
    public void RateLimiter_BlocksBeyondBudget_ResetsNextMinute_AndIsPerKey()
    {
        var limiter = new IngestRateLimiter(perMinute: 2);

        Assert.True(limiter.Allow("1.2.3.4", nowUnix: 60));
        Assert.True(limiter.Allow("1.2.3.4", nowUnix: 61));
        Assert.False(limiter.Allow("1.2.3.4", nowUnix: 62));

        Assert.True(limiter.Allow("5.6.7.8", nowUnix: 62));   // other key unaffected
        Assert.True(limiter.Allow("1.2.3.4", nowUnix: 120));  // next window resets

        Assert.True(new IngestRateLimiter(perMinute: 0).Allow("x", 0)); // 0 = disabled
    }

    // ---------------- Admin Basic Auth ----------------

    private static string Header(string user, string pass)
        => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass));

    [Fact]
    public void BasicAuth_AcceptsOnlyExactCredentials()
    {
        Assert.True(BasicAuth.IsAuthorized(Header("admin", "pw-123"), "admin", "pw-123"));

        Assert.False(BasicAuth.IsAuthorized(Header("admin", "wrong"), "admin", "pw-123"));
        Assert.False(BasicAuth.IsAuthorized(Header("other", "pw-123"), "admin", "pw-123"));
        Assert.False(BasicAuth.IsAuthorized(null, "admin", "pw-123"));
        Assert.False(BasicAuth.IsAuthorized("", "admin", "pw-123"));
        Assert.False(BasicAuth.IsAuthorized("Bearer abc", "admin", "pw-123"));
        Assert.False(BasicAuth.IsAuthorized("Basic %%%not-base64%%%", "admin", "pw-123"));

        // Unconfigured credentials NEVER match — the admin surface is off by default.
        Assert.False(BasicAuth.IsAuthorized(Header("", ""), "", ""));
        Assert.False(BasicAuth.IsAuthorized(Header("admin", ""), "admin", ""));
    }

    // ---------------- #1369: gone marker, derived-key hint, CSRF field ----------------

    private static string PayloadFor(string playerId, string platform, string? replyKey = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = "Hat eaten by door",
            ["description"] = "The door on my ship eats my hat.",
            ["gameVersion"] = "2026.8.22",
            ["playerId"] = playerId,
            ["playerName"] = "Justus",
            ["platform"] = platform,
            ["reportJson"] = new Dictionary<string, object?> { ["scene"] = "planet" },
        };
        if (replyKey != null)
        {
            body["replyKey"] = replyKey;
        }

        return JsonSerializer.Serialize(body);
    }

    [Fact]
    public void MissingReports_NamesForeignDeletedAndUnknownIds_NeverTheKeysOwn_AndCapsTheQuery()
    {
        var store = NewStore();
        string key = KeyFor("token-abc");
        string owned = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 1);
        string foreign = store.Add(ReportIngest.Parse(PayloadFor("token-xyz", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 2);
        string deleted = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 3);
        Assert.True(store.Delete(deleted));

        var gone = store.MissingReports(key, new[] { owned, foreign, deleted, "unknown", "", owned });
        Assert.Equal(new[] { foreign, deleted, "unknown" }, gone);

        // A key that cannot read anything sees everything gone; a retention prune retires a report the same way.
        Assert.Equal(new[] { owned }, store.MissingReports("not-a-key", new[] { owned }));
        Assert.Equal(2, store.Prune(retentionDays: 1, nowUnix: 1 + 2 * 86400)); // owned + foreign
        Assert.Equal(new[] { owned }, store.MissingReports(key, new[] { owned }));

        // At most 50 ids per poll are looked at — the client remembers no more than that.
        var many = Enumerable.Range(0, ReportStore.MaxGoneQueryIds + 10).Select(i => "id" + i).ToArray();
        Assert.Equal(ReportStore.MaxGoneQueryIds, store.MissingReports(key, many).Count);
    }

    /// <summary>The server's /bump forward of the same report, exactly as it lands in production: the player
    /// id is the NAME, no reply key, the description wraps the player's wording, and the snapshot carries the
    /// screenshot — which is why the admin list links to this half.</summary>
    private static string ServerForwardPayloadFor(string playerName)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = $"Bump [World]: [feedback] Hat eaten by door — The door on my ship eats my hat.",
            ["description"] = "[feedback] Hat eaten by door — The door on my ship eats my hat.",
            ["gameVersion"] = "2026.8.22",
            ["playerId"] = playerName,
            ["playerName"] = playerName,
            ["platform"] = "WindowsPlayer",
            ["reportJson"] = new Dictionary<string, object?> { ["source"] = "server", ["snapshot"] = new Dictionary<string, object?>() },
            ["screenshot"] = new Dictionary<string, object?>
            {
                ["fileName"] = "bump.jpg",
                ["mimeType"] = "image/jpeg",
                ["base64"] = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }),
            },
        };
        return JsonSerializer.Serialize(body);
    }

    /// <summary>#1378: a report filed before the reply channel is a pair whose screenshot half carries no reply
    /// key (the #1359 repair blanks name-derived keys on server rows). Opening THAT half must still show the
    /// pair's thread, and an answer written on it must land on the keyed half — the only one a game polls.</summary>
    [Fact]
    public void PairedScreenshotRow_ShowsTheClientHalfsThread_AndReceivesRepliesThere()
    {
        var store = NewStore();
        string client = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 100);
        string server = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Justus"), new ReportHostConfig(), out _)!, nowUnix: 101);

        var clientRow = store.Get(client)!;
        var serverRow = store.Get(server)!;
        Assert.NotEmpty(clientRow.ReplyKey);
        Assert.Empty(serverRow.ReplyKey); // the production shape after #1359

        // Resolution: the key-less half hands over to its keyed partner; the keyed half owns itself; an
        // unrelated key-less row (another player, same minute) stays its own owner.
        var around = store.Around(serverRow.CreatedUnix, ReportHostPages.DuplicateWindowSeconds);
        Assert.Equal(client, ReportHostPages.ThreadOwner(serverRow, around).Id);
        Assert.Equal(client, ReportHostPages.ThreadOwner(clientRow, around).Id);
        string stranger = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Pilot"), new ReportHostConfig(), out _)!, nowUnix: 102);
        var strangerRow = store.Get(stranger)!;
        Assert.Equal(stranger, ReportHostPages.ThreadOwner(strangerRow, store.Around(102, ReportHostPages.DuplicateWindowSeconds)).Id);

        // The answer is stored on the client half; the screenshot half's page renders it and says where it lives.
        long replyId = store.AddDevReply(client, "Fixed — thanks for the hat.", isQuestion: false, nowUnix: 200);
        Assert.True(replyId > 0);
        store.SetFixedInVersion(client, "2026.8.23");
        Assert.Empty(store.ListReplies(server));

        // Re-resolve after the write — records are snapshots (the routes resolve per request anyway).
        var owner = ReportHostPages.ThreadOwner(serverRow, store.Around(serverRow.CreatedUnix, ReportHostPages.DuplicateWindowSeconds));
        string html = ReportHostPages.Detail(serverRow, store.ListReplies(owner.Id), null, owner);
        Assert.Contains("Fixed — thanks for the hat.", html);
        Assert.Contains("Fixed in version: <b>2026.8.23</b>", html);
        Assert.Contains($"/admin/report/{client}'>client row</a>", html);
        Assert.DoesNotContain("no reply key", html);
        Assert.DoesNotContain("No replies yet", html);
        Assert.Contains($"action='/admin/report/{server}/reply'", html); // the route resolves the owner again

        // Opened on its own half nothing changes: no hand-over hint.
        html = ReportHostPages.Detail(clientRow, store.ListReplies(client), null, owner);
        Assert.DoesNotContain("conversation lives on the paired", html);
        Assert.Contains("Fixed — thanks for the hat.", html);
    }

    [Fact]
    public void DetailPage_SaysNoInGameReplyForOldArcadeReports_AndEveryFormCarriesTheCsrfToken()
    {
        var store = NewStore();
        var csrf = new AdminCsrf("tok-for-the-test");

        // A browser report filed before the reply channel: key derived from the browser-local player id —
        // the glitch.fun arcade never polls with it.
        string oldArcade = store.Add(ReportIngest.Parse(PayloadFor("browser-token", "WebGLPlayer"), new ReportHostConfig(), out _)!, nowUnix: 1);
        Assert.Equal(ReportHostPages.ReplyKeyOrigin.DerivedFromPlayerId, ReportHostPages.KeyOrigin(store.Get(oldArcade)!));
        string html = ReportHostPages.Detail(store.Get(oldArcade)!, store.ListReplies(oldArcade), csrf);
        Assert.Contains("No in-game reply possible", html);

        // An old desktop report: the same derivation, but a desktop install DOES poll with it — a softer hint.
        string oldDesktop = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 2);
        html = ReportHostPages.Detail(store.Get(oldDesktop)!, store.ListReplies(oldDesktop), csrf);
        Assert.DoesNotContain("No in-game reply possible", html);
        Assert.Contains("Reply key derived from the player id", html);

        // A report that carried its key (any platform): no caveat at all.
        string current = store.Add(ReportIngest.Parse(PayloadFor("browser-token", "WebGLPlayer", KeyFor("glitch-install-id")), new ReportHostConfig(), out _)!, nowUnix: 3);
        Assert.Equal(ReportHostPages.ReplyKeyOrigin.SentByClient, ReportHostPages.KeyOrigin(store.Get(current)!));
        html = ReportHostPages.Detail(store.Get(current)!, store.ListReplies(current), csrf);
        Assert.DoesNotContain("No in-game reply possible", html);
        Assert.DoesNotContain("derived from the player id", html);
        Assert.Contains("No replies yet", html);

        // No key at all (a crash from a server): the existing wording.
        string crash = store.Add(ReportIngest.Parse(CrashPayload(), new ReportHostConfig(), out _)!, nowUnix: 4);
        Assert.Equal(ReportHostPages.ReplyKeyOrigin.None, ReportHostPages.KeyOrigin(store.Get(crash)!));
        Assert.Contains("no reply key", ReportHostPages.Detail(store.Get(crash)!, store.ListReplies(crash), csrf));

        // Every form on the page — reply, one per status button, delete — carries the token; without a
        // guard instance (older callers, tests) no field is rendered.
        int forms = html.Split("<form method='post'").Length - 1;
        Assert.Equal(1 + BugReportStatus.All.Length + 1, forms);
        Assert.Equal(forms, html.Split(csrf.HiddenField()).Length - 1);
        Assert.DoesNotContain("name='csrf'", ReportHostPages.Detail(store.Get(current)!, store.ListReplies(current)));

        // The guard itself: fixed-time compare, nothing empty ever matches, a fresh instance is 64 hex chars.
        Assert.True(csrf.IsValid("tok-for-the-test"));
        Assert.False(csrf.IsValid("tok-for-the-tesT"));
        Assert.False(csrf.IsValid(""));
        Assert.False(csrf.IsValid(null));
        Assert.Matches("^[0-9a-f]{64}$", new AdminCsrf().Token);
        Assert.NotEqual(new AdminCsrf().Token, new AdminCsrf().Token);
    }

    /// <summary>#1380: the list shows the two rows of an F1 report as ONE report, so "mark done", delete and the
    /// reply-driven status flips must cover both halves — before, the other row stayed <c>new</c> under the
    /// status filter or survived a delete as a lone row. An unrelated report in the same window is untouched.</summary>
    [Fact]
    public void PairActions_StatusDeleteAndReplyFlips_CoverBothHalves_AndLeaveAStrangerAlone()
    {
        var store = NewStore();
        string client = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 100);
        string server = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Justus"), new ReportHostConfig(), out _)!, nowUnix: 101);
        string stranger = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Pilot"), new ReportHostConfig(), out _)!, nowUnix: 102);

        // The pair resolves from either half (the addressed row first); the stranger is a pair of one.
        Assert.Equal(new[] { server, client }, ReportPairActions.PairOf(store, store.Get(server)!).Select(r => r.Id));
        Assert.Equal(new[] { client, server }, ReportPairActions.PairOf(store, store.Get(client)!).Select(r => r.Id));
        Assert.Equal(new[] { stranger }, ReportPairActions.PairOf(store, store.Get(stranger)!).Select(r => r.Id));

        // "mark done" on the screenshot half — the row the list links — sets both; the stranger stays new.
        Assert.Equal(new[] { server, client }, ReportPairActions.SetStatus(store, server, BugReportStatus.Done));
        Assert.Equal(BugReportStatus.Done, store.Get(client)!.Status);
        Assert.Equal(BugReportStatus.Done, store.Get(server)!.Status);
        Assert.Equal(BugReportStatus.New, store.Get(stranger)!.Status);
        Assert.Null(ReportPairActions.SetStatus(store, "no-such-id", BugReportStatus.Done));
        Assert.Null(ReportPairActions.SetStatus(store, server, "bogus"));

        // A developer question flips the thread owner (the keyed client half) on its own; the mirror carries
        // it over to the screenshot half, and is a no-op once the two agree.
        Assert.True(store.AddDevReply(client, "Which world was that?", isQuestion: true, nowUnix: 200) > 0);
        Assert.Equal(BugReportStatus.WaitingForPlayer, store.Get(client)!.Status);
        Assert.Equal(BugReportStatus.Done, store.Get(server)!.Status);
        Assert.Equal(1, ReportPairActions.MirrorStatus(store, client));
        Assert.Equal(BugReportStatus.WaitingForPlayer, store.Get(server)!.Status);
        Assert.Equal(0, ReportPairActions.MirrorStatus(store, client));
        Assert.Equal(0, ReportPairActions.MirrorStatus(store, "no-such-id"));

        // Delete from the client half: both rows and the thread are gone, the stranger survives.
        Assert.Equal(new[] { client, server }, ReportPairActions.Delete(store, client));
        Assert.Null(store.Get(client));
        Assert.Null(store.Get(server));
        Assert.Empty(store.ListReplies(client));
        Assert.NotNull(store.Get(stranger));
        Assert.Null(ReportPairActions.Delete(store, client));
    }

    /// <summary>#1380: pairs triaged apart before actions covered both halves settle on the most advanced state
    /// once, at startup — idempotent, and a lone row is never touched.</summary>
    [Fact]
    public void SyncPairStatuses_SettlesPairsTriagedApart_OnTheMostAdvancedState()
    {
        var store = NewStore();
        string client = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 100);
        string server = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Justus"), new ReportHostConfig(), out _)!, nowUnix: 101);
        string stranger = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Pilot"), new ReportHostConfig(), out _)!, nowUnix: 102);

        // The shape the old admin UI left behind: done on the list's row, new on the other.
        Assert.True(store.SetStatus(server, BugReportStatus.Done));
        Assert.Equal(1, store.SyncPairStatuses(ReportHostPages.GroupDuplicates));
        Assert.Equal(BugReportStatus.Done, store.Get(client)!.Status);
        Assert.Equal(BugReportStatus.Done, store.Get(server)!.Status);
        Assert.Equal(BugReportStatus.New, store.Get(stranger)!.Status);
        Assert.Equal(0, store.SyncPairStatuses(ReportHostPages.GroupDuplicates));

        // The ranking is the admin UI's order; unknown values never win.
        Assert.Equal(BugReportStatus.PlayerReplied, BugReportStatus.MostAdvanced(new[] { BugReportStatus.Triaged, BugReportStatus.PlayerReplied, BugReportStatus.WaitingForPlayer }));
        Assert.Equal(BugReportStatus.Done, BugReportStatus.MostAdvanced(new[] { BugReportStatus.Done, "bogus", BugReportStatus.New }));
        Assert.Equal(BugReportStatus.New, BugReportStatus.MostAdvanced(Array.Empty<string>()));
    }

    /// <summary>#1380: the detail page says when the buttons act on both rows, names the partner, and greys a
    /// status out only while BOTH halves already have it. A row on its own keeps the old wording.</summary>
    [Fact]
    public void DetailPage_SaysActionsCoverBothRows_OnlyForAPair()
    {
        var store = NewStore();
        string client = store.Add(ReportIngest.Parse(PayloadFor("token-abc", "WindowsPlayer"), new ReportHostConfig(), out _)!, nowUnix: 100);
        string server = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Justus"), new ReportHostConfig(), out _)!, nowUnix: 101);
        var serverRow = store.Get(server)!;

        string html = ReportHostPages.Detail(serverRow, pair: ReportPairActions.PairOf(store, serverRow));
        Assert.Contains("mark done (both rows)", html);
        Assert.Contains(">delete (both rows)<", html);
        Assert.Contains("Delete this report and its paired row permanently?", html);
        Assert.Contains($"<th>Paired row</th><td><a href='/admin/report/{client}'>client row</a>", html);
        Assert.Contains("value='new'><button disabled>mark new (both rows)", html);

        // Triage the partner alone (the pre-#1380 shape): "mark new" is live again, because it would still change a row.
        Assert.True(store.SetStatus(client, BugReportStatus.Triaged));
        html = ReportHostPages.Detail(serverRow, pair: ReportPairActions.PairOf(store, serverRow));
        Assert.Contains("value='new'><button>mark new (both rows)", html);

        string stranger = store.Add(ReportIngest.Parse(ServerForwardPayloadFor("Pilot"), new ReportHostConfig(), out _)!, nowUnix: 102);
        var strangerRow = store.Get(stranger)!;
        html = ReportHostPages.Detail(strangerRow, pair: ReportPairActions.PairOf(store, strangerRow));
        Assert.DoesNotContain("both rows", html);
        Assert.DoesNotContain("Paired row", html);
        Assert.Contains("Delete this report permanently?", html);
        Assert.Contains("value='new'><button disabled>mark new</button>", html);
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

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

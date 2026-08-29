// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Feedback;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>One stored bug report. <c>ScreenshotFile</c> is the bare file name inside the store's
/// screenshots/ folder ("" = no screenshot); the image bytes never live in the database.
/// <c>ReplyKey</c> is the reporter's pull credential for the reply thread (#1327; "" = none — the
/// report came without a player id, e.g. a server crash) and <c>FixedInVersion</c> the operator's
/// "shipped in" note shown to the player with the replies.</summary>
public sealed record BugReportRecord(
    string Id,
    string Title,
    string Description,
    string Email,
    string GameVersion,
    string BuildNumber,
    string PlayerId,
    string PlayerName,
    string SessionId,
    string Platform,
    string ClientTimestamp,
    string Category,
    string Source,
    string Kind,
    string Status,
    string ScreenshotFile,
    string ReportJson,
    long CreatedUnix,
    string ReplyKey,
    string FixedInVersion);

/// <summary>One entry of a report's reply thread: written by the developer (<see cref="AuthorDev"/> —
/// an answer, or a follow-up question when <c>IsQuestion</c>) or by the player answering from inside
/// the game (<see cref="AuthorPlayer"/>). <c>SeenUnix</c> is set once the player's client acknowledged
/// a developer entry (0 = unread); player entries are "seen" by definition.</summary>
public sealed record ReplyRecord(
    long Id,
    string ReportId,
    string Author,
    string Text,
    bool IsQuestion,
    long CreatedUnix,
    long SeenUnix)
{
    public const string AuthorDev = "dev";
    public const string AuthorPlayer = "player";
}

/// <summary>A report together with its reply thread, as the client's poll returns it.</summary>
public sealed record ReportThread(BugReportRecord Report, IReadOnlyList<ReplyRecord> Replies);

/// <summary>Triage states a report moves through in the admin UI. <see cref="WaitingForPlayer"/> is set
/// automatically when the developer asks a follow-up question, <see cref="PlayerReplied"/> when the
/// player answers it (#1327) — both are ordinary states the operator can leave by hand.</summary>
public static class BugReportStatus
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string WaitingForPlayer = "waiting_for_player";
    public const string PlayerReplied = "player_replied";
    public const string Done = "done";

    /// <summary>Every state, in the order the admin UI lists them.</summary>
    public static readonly string[] All = { New, Triaged, WaitingForPlayer, PlayerReplied, Done };

    public static bool IsValid(string status) => status is New or Triaged or WaitingForPlayer or PlayerReplied or Done;
}

/// <summary>
/// The inbox storage: reports in one SQLite file, screenshots as plain files next to it. Every mutation
/// is serialized on one connection behind a lock, mirroring the WorldHost registry pattern — the write
/// volume (hand-typed feedback + occasional crash bursts) is tiny. Listing is keyset-paginated on
/// (created_unix, id) ascending so a <c>since</c>-based delta sync never skips rows.
/// </summary>
public sealed class ReportStore : IDisposable
{
    /// <summary>How many answers a player may post per report — enough for a real back-and-forth, small
    /// enough that a stolen reply key cannot turn a thread into a spam channel (#1327).</summary>
    public const int MaxPlayerRepliesPerReport = 3;

    private readonly Lock _gate = new();
    private readonly SqliteConnection _db;
    private readonly string _screenshotsDir;

    public ReportStore(ReportHostConfig config, string? databasePath = null)
    {
        string path = databasePath ?? Path.Combine(config.DataDir, "reports.db");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _screenshotsDir = Path.Combine(dir ?? ".", "screenshots");
        Directory.CreateDirectory(_screenshotsDir);

        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS bugreport(
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                email TEXT NOT NULL DEFAULT '',
                game_version TEXT NOT NULL DEFAULT '',
                build_number TEXT NOT NULL DEFAULT '',
                player_id TEXT NOT NULL DEFAULT '',
                player_name TEXT NOT NULL DEFAULT '',
                session_id TEXT NOT NULL DEFAULT '',
                platform TEXT NOT NULL DEFAULT '',
                client_timestamp TEXT NOT NULL DEFAULT '',
                category TEXT NOT NULL DEFAULT 'feedback',
                source TEXT NOT NULL DEFAULT '',
                kind TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'new',
                screenshot_file TEXT NOT NULL DEFAULT '',
                report_json TEXT NOT NULL DEFAULT '{}',
                created_unix INTEGER NOT NULL,
                reply_key TEXT NOT NULL DEFAULT '',
                fixed_in_version TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS idx_bugreport_created ON bugreport(created_unix, id);
            CREATE INDEX IF NOT EXISTS idx_bugreport_status ON bugreport(status);
            CREATE TABLE IF NOT EXISTS report_reply(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                report_id TEXT NOT NULL,
                author TEXT NOT NULL,
                text TEXT NOT NULL DEFAULT '',
                is_question INTEGER NOT NULL DEFAULT 0,
                created_unix INTEGER NOT NULL,
                seen_unix INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS idx_report_reply_report ON report_reply(report_id, id);
            """);

        // Databases created before the reply channel (#1327) lack the two columns — add them in place.
        EnsureColumn("reply_key", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("fixed_in_version", "TEXT NOT NULL DEFAULT ''");
        Exec("CREATE INDEX IF NOT EXISTS idx_bugreport_reply_key ON bugreport(reply_key);");
    }

    /// <summary>Stores a parsed report (and its screenshot file, when present) and returns the new id.
    /// <paramref name="nowUnix"/> is injectable for tests; production passes the current time. A report
    /// that came without a <c>replyKey</c> (pre-#1327 client) gets one derived from its player id, so
    /// the reporter can still receive answers once they update — except a server forward (#1359): its
    /// player id is the public player NAME, and a key derived from that would be guessable.</summary>
    public string Add(ParsedReport report, long nowUnix)
    {
        string id = Guid.NewGuid().ToString("N");
        string screenshotFile = string.Empty;

        if (report.ScreenshotBytes is { Length: > 0 })
        {
            screenshotFile = id + "." + report.ScreenshotExtension;
            File.WriteAllBytes(Path.Combine(_screenshotsDir, screenshotFile), report.ScreenshotBytes);
        }

        string replyKey = report.ReplyKey.Length > 0 ? report.ReplyKey
            : report.Source == ServerSource ? string.Empty
            : FeedbackReplyKey.Derive(report.PlayerId);

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO bugreport(id, title, description, email, game_version, build_number, player_id,
                    player_name, session_id, platform, client_timestamp, category, source, kind, status,
                    screenshot_file, report_json, created_unix, reply_key, fixed_in_version)
                VALUES ($id, $title, $desc, $email, $gv, $bn, $pid, $pname, $sid, $plat, $cts, $cat, $src,
                    $kind, 'new', $shot, $json, $created, $rkey, '');
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$title", report.Title);
            cmd.Parameters.AddWithValue("$desc", report.Description);
            cmd.Parameters.AddWithValue("$email", report.Email);
            cmd.Parameters.AddWithValue("$gv", report.GameVersion);
            cmd.Parameters.AddWithValue("$bn", report.BuildNumber);
            cmd.Parameters.AddWithValue("$pid", report.PlayerId);
            cmd.Parameters.AddWithValue("$pname", report.PlayerName);
            cmd.Parameters.AddWithValue("$sid", report.SessionId);
            cmd.Parameters.AddWithValue("$plat", report.Platform);
            cmd.Parameters.AddWithValue("$cts", report.ClientTimestamp);
            cmd.Parameters.AddWithValue("$cat", report.Category);
            cmd.Parameters.AddWithValue("$src", report.Source);
            cmd.Parameters.AddWithValue("$kind", report.Kind);
            cmd.Parameters.AddWithValue("$shot", screenshotFile);
            cmd.Parameters.AddWithValue("$json", report.ReportJson);
            cmd.Parameters.AddWithValue("$created", nowUnix);
            cmd.Parameters.AddWithValue("$rkey", replyKey);
            cmd.ExecuteNonQuery();
        }

        return id;
    }

    /// <summary><c>reportJson.source</c> of a game server's forward (<c>/bump</c>, paint/shape reports, crashes).
    /// Such rows identify the player by NAME, not by the install secret — see <see cref="Add"/>.</summary>
    public const string ServerSource = "server";

    /// <summary>One-time migration for rows stored before the reply channel existed: derives the reply
    /// key from the stored player id with the client's own formula. Idempotent (only touches rows with
    /// an empty key); returns how many rows were filled. Called at startup. Server forwards are skipped
    /// for the reason given on <see cref="Add"/> (#1359).</summary>
    public int BackfillReplyKeys()
    {
        lock (_gate)
        {
            var pending = new List<(string Id, string PlayerId)>();
            using (var select = _db.CreateCommand())
            {
                select.CommandText = "SELECT id, player_id FROM bugreport WHERE reply_key = '' AND player_id != '' AND source != $server;";
                select.Parameters.AddWithValue("$server", ServerSource);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    pending.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            foreach (var (id, playerId) in pending)
            {
                using var update = _db.CreateCommand();
                update.CommandText = "UPDATE bugreport SET reply_key = $key WHERE id = $id;";
                update.Parameters.AddWithValue("$key", FeedbackReplyKey.Derive(playerId));
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            return pending.Count;
        }
    }

    /// <summary>One-time repair for server-forwarded rows (#1359): before the fix, a forward without a reply
    /// key got one derived from its player id — for a server row that is the public player NAME, i.e. a key
    /// anyone who knows the name can compute (and the client never polls with it). Blanks exactly those keys;
    /// a key the client passed through <c>/bump</c> does not equal the name derivation and stays. Idempotent;
    /// returns how many rows were cleared. Called at startup after <see cref="BackfillReplyKeys"/>.</summary>
    public int RevokeNameDerivedServerKeys()
    {
        lock (_gate)
        {
            var derived = new List<string>();
            using (var select = _db.CreateCommand())
            {
                select.CommandText = "SELECT id, player_id, reply_key FROM bugreport WHERE source = $server AND reply_key != '';";
                select.Parameters.AddWithValue("$server", ServerSource);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.GetString(2) == FeedbackReplyKey.Derive(reader.GetString(1)))
                    {
                        derived.Add(reader.GetString(0));
                    }
                }
            }

            foreach (string id in derived)
            {
                using var update = _db.CreateCommand();
                update.CommandText = "UPDATE bugreport SET reply_key = '' WHERE id = $id;";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            return derived.Count;
        }
    }

    public BugReportRecord? Get(string id)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM bugreport WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }
    }

    /// <summary>Lists reports ascending by (created_unix, id). <paramref name="afterCreatedUnix"/> +
    /// <paramref name="afterId"/> form the keyset cursor (exclusive); <paramref name="sinceUnix"/> is the
    /// delta-sync filter (also exclusive). Returns up to <paramref name="limit"/> rows plus whether more
    /// rows follow.</summary>
    public (List<BugReportRecord> Items, bool HasMore) Query(
        long? sinceUnix = null,
        string? status = null,
        string? category = null,
        string? source = null,
        int limit = 100,
        long afterCreatedUnix = -1,
        string afterId = "")
    {
        limit = Math.Clamp(limit, 1, 200);
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM bugreport
                WHERE ($since IS NULL OR created_unix > $since)
                  AND ($status IS NULL OR status = $status)
                  AND ($cat IS NULL OR category = $cat)
                  AND ($src IS NULL OR source = $src)
                  AND (created_unix > $afterCreated OR (created_unix = $afterCreated AND id > $afterId))
                ORDER BY created_unix ASC, id ASC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$since", (object?)sinceUnix ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$src", (object?)source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$afterCreated", afterCreatedUnix);
            cmd.Parameters.AddWithValue("$afterId", afterId);
            cmd.Parameters.AddWithValue("$limit", limit + 1); // one extra row = "has more"

            var items = new List<BugReportRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadRecord(reader));
            }

            bool hasMore = items.Count > limit;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            return (items, hasMore);
        }
    }

    /// <summary>Newest-first page for the admin UI (triage reads top-down, unlike the sync API).</summary>
    public List<BugReportRecord> Latest(string? status = null, string? category = null, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM bugreport
                WHERE ($status IS NULL OR status = $status)
                  AND ($cat IS NULL OR category = $cat)
                ORDER BY created_unix DESC, id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$limit", limit);

            var items = new List<BugReportRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadRecord(reader));
            }

            return items;
        }
    }

    /// <summary>Every row stamped within <paramref name="windowSeconds"/> of <paramref name="createdUnix"/> —
    /// the candidates for the other half of a report pair (#1378); the caller applies the pairing rule.</summary>
    public List<BugReportRecord> Around(long createdUnix, long windowSeconds)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM bugreport WHERE created_unix BETWEEN $from AND $to ORDER BY created_unix, id;";
            cmd.Parameters.AddWithValue("$from", createdUnix - windowSeconds);
            cmd.Parameters.AddWithValue("$to", createdUnix + windowSeconds);

            var items = new List<BugReportRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(ReadRecord(reader));
            }

            return items;
        }
    }

    public bool SetStatus(string id, string status)
    {
        if (!BugReportStatus.IsValid(status))
        {
            return false;
        }

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE bugreport SET status = $status WHERE id = $id;";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>Overwrites a report's reply key ("" detaches it from every player — an operator lever for a
    /// key that leaked, and the test hook for simulating pre-#1327 rows).</summary>
    public bool SetReplyKey(string id, string replyKey)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE bugreport SET reply_key = $k WHERE id = $id;";
            cmd.Parameters.AddWithValue("$k", replyKey ?? string.Empty);
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    /// <summary>Records the version a report's fix shipped in ("" clears it). Shown to the player with the
    /// reply thread; not a status change on its own.</summary>
    public bool SetFixedInVersion(string id, string version)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE bugreport SET fixed_in_version = $v WHERE id = $id;";
            cmd.Parameters.AddWithValue("$v", (version ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() == 1;
        }
    }

    // ---------------- Reply threads (#1327) ----------------

    /// <summary>Appends a developer entry (answer, or follow-up question when <paramref name="isQuestion"/>).
    /// A question flips the report to <see cref="BugReportStatus.WaitingForPlayer"/>; a plain answer leaves
    /// the status alone. Returns the reply id, or -1 when the report does not exist.</summary>
    public long AddDevReply(string reportId, string text, bool isQuestion, long nowUnix)
    {
        lock (_gate)
        {
            if (!ExistsLocked(reportId))
            {
                return -1;
            }

            long id = InsertReplyLocked(reportId, ReplyRecord.AuthorDev, text, isQuestion, nowUnix);
            if (isQuestion)
            {
                SetStatusLocked(reportId, BugReportStatus.WaitingForPlayer);
            }

            return id;
        }
    }

    /// <summary>Appends the player's answer, after checking the key owns the report, a developer entry exists
    /// to answer (no unsolicited threads) and the per-report limit is not exhausted. Flips the status to
    /// <see cref="BugReportStatus.PlayerReplied"/>. Returns the reply id, or a negative code: -1 = no such
    /// report for this key, -2 = nothing to answer yet, -3 = limit reached.</summary>
    public long AddPlayerReply(string replyKey, string reportId, string text, long nowUnix)
    {
        lock (_gate)
        {
            if (!OwnsLocked(replyKey, reportId))
            {
                return -1;
            }

            if (CountRepliesLocked(reportId, ReplyRecord.AuthorDev) == 0)
            {
                return -2;
            }

            if (CountRepliesLocked(reportId, ReplyRecord.AuthorPlayer) >= MaxPlayerRepliesPerReport)
            {
                return -3;
            }

            long id = InsertReplyLocked(reportId, ReplyRecord.AuthorPlayer, text, isQuestion: false, nowUnix);
            SetStatusLocked(reportId, BugReportStatus.PlayerReplied);
            return id;
        }
    }

    /// <summary>The whole thread of one report, oldest first.</summary>
    public List<ReplyRecord> ListReplies(string reportId)
    {
        lock (_gate)
        {
            return ListRepliesLocked(reportId);
        }
    }

    /// <summary>The client's poll: every report owned by <paramref name="replyKey"/> that still has an
    /// unread developer entry, each with its full thread. Reports are returned oldest first; only
    /// threads with an unread developer entry created after <paramref name="sinceUnix"/> qualify.</summary>
    public List<ReportThread> UnreadThreads(string replyKey, long sinceUnix = 0)
    {
        if (!FeedbackReplyKey.IsWellFormed(replyKey))
        {
            return new List<ReportThread>();
        }

        lock (_gate)
        {
            var reports = new List<BugReportRecord>();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT * FROM bugreport
                    WHERE reply_key = $key AND id IN (
                        SELECT report_id FROM report_reply
                        WHERE author = 'dev' AND seen_unix = 0 AND created_unix > $since)
                    ORDER BY created_unix ASC, id ASC
                    LIMIT 50;
                    """;
                cmd.Parameters.AddWithValue("$key", replyKey);
                cmd.Parameters.AddWithValue("$since", sinceUnix);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    reports.Add(ReadRecord(reader));
                }
            }

            return reports.Select(r => new ReportThread(r, ListRepliesLocked(r.Id))).ToList();
        }
    }

    /// <summary>Upper bound on the ids one poll may ask about (the client remembers at most 50 sent reports).</summary>
    public const int MaxGoneQueryIds = 50;

    /// <summary>The "gone" half of the client's poll (#1369): of the report ids the client still remembers,
    /// the ones that are NOT a report this key can read — deleted, pruned by retention, or stored under
    /// a different key (an arcade report filed before the reply channel). The client forgets those and
    /// stops polling for them. Only ids the caller named are ever reported, so nothing is enumerable;
    /// a malformed key makes every id gone (it can read nothing). Capped at <see cref="MaxGoneQueryIds"/>.</summary>
    public List<string> MissingReports(string replyKey, IEnumerable<string> reportIds)
    {
        var gone = new List<string>();
        bool keyOk = FeedbackReplyKey.IsWellFormed(replyKey);
        lock (_gate)
        {
            foreach (string id in reportIds.Where(i => !string.IsNullOrEmpty(i)).Distinct().Take(MaxGoneQueryIds))
            {
                if (!keyOk || !OwnsLocked(replyKey, id))
                {
                    gone.Add(id);
                }
            }
        }

        return gone;
    }

    /// <summary>Marks developer entries as read. Scoped to the key: ids belonging to other players' reports
    /// are silently ignored (reply ids are guessable integers). Returns how many rows changed.</summary>
    public int AckReplies(string replyKey, IEnumerable<long> replyIds, long nowUnix)
    {
        if (!FeedbackReplyKey.IsWellFormed(replyKey))
        {
            return 0;
        }

        lock (_gate)
        {
            int changed = 0;
            foreach (long id in replyIds.Distinct().Take(200))
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    UPDATE report_reply SET seen_unix = $now
                    WHERE id = $id AND author = 'dev' AND seen_unix = 0
                      AND report_id IN (SELECT id FROM bugreport WHERE reply_key = $key);
                    """;
                cmd.Parameters.AddWithValue("$now", nowUnix);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$key", replyKey);
                changed += cmd.ExecuteNonQuery();
            }

            return changed;
        }
    }

    private bool ExistsLocked(string reportId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM bugreport WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", reportId);
        return cmd.ExecuteScalar() != null;
    }

    private bool OwnsLocked(string replyKey, string reportId)
    {
        if (!FeedbackReplyKey.IsWellFormed(replyKey))
        {
            return false;
        }

        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM bugreport WHERE id = $id AND reply_key = $key;";
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$key", replyKey);
        return cmd.ExecuteScalar() != null;
    }

    private int CountRepliesLocked(string reportId, string author)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM report_reply WHERE report_id = $id AND author = $a;";
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.Parameters.AddWithValue("$a", author);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private long InsertReplyLocked(string reportId, string author, string text, bool isQuestion, long nowUnix)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO report_reply(report_id, author, text, is_question, created_unix, seen_unix)
            VALUES ($r, $a, $t, $q, $c, $s);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$r", reportId);
        cmd.Parameters.AddWithValue("$a", author);
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$q", isQuestion ? 1 : 0);
        cmd.Parameters.AddWithValue("$c", nowUnix);
        // Player entries need no acknowledgement — the player wrote them.
        cmd.Parameters.AddWithValue("$s", author == ReplyRecord.AuthorPlayer ? nowUnix : 0L);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private void SetStatusLocked(string reportId, string status)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE bugreport SET status = $status WHERE id = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$id", reportId);
        cmd.ExecuteNonQuery();
    }

    private List<ReplyRecord> ListRepliesLocked(string reportId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, report_id, author, text, is_question, created_unix, seen_unix FROM report_reply WHERE report_id = $id ORDER BY id ASC;";
        cmd.Parameters.AddWithValue("$id", reportId);
        var list = new List<ReplyRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ReplyRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4) != 0, reader.GetInt64(5), reader.GetInt64(6)));
        }

        return list;
    }

    // ---------------- Delete / retention ----------------

    /// <summary>Deletes a report, its reply thread AND its screenshot file (reports may carry an e-mail —
    /// deletion must not leave partial personal data behind).</summary>
    public bool Delete(string id)
    {
        var record = Get(id);
        if (record == null)
        {
            return false;
        }

        lock (_gate)
        {
            using (var replies = _db.CreateCommand())
            {
                replies.CommandText = "DELETE FROM report_reply WHERE report_id = $id;";
                replies.Parameters.AddWithValue("$id", id);
                replies.ExecuteNonQuery();
            }

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM bugreport WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        DeleteScreenshotFile(record.ScreenshotFile);
        return true;
    }

    /// <summary>Removes reports older than <paramref name="retentionDays"/> (0 = keep forever) including
    /// their reply threads and screenshot files; returns how many were pruned. Called at startup and after
    /// each ingest.</summary>
    public int Prune(int retentionDays, long nowUnix)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        long cutoff = nowUnix - retentionDays * 86400L;
        List<string> screenshots;
        int pruned;
        lock (_gate)
        {
            using (var select = _db.CreateCommand())
            {
                select.CommandText = "SELECT screenshot_file FROM bugreport WHERE created_unix < $cutoff AND screenshot_file != '';";
                select.Parameters.AddWithValue("$cutoff", cutoff);
                screenshots = new List<string>();
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    screenshots.Add(reader.GetString(0));
                }
            }

            using (var replies = _db.CreateCommand())
            {
                replies.CommandText = "DELETE FROM report_reply WHERE report_id IN (SELECT id FROM bugreport WHERE created_unix < $cutoff);";
                replies.Parameters.AddWithValue("$cutoff", cutoff);
                replies.ExecuteNonQuery();
            }

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM bugreport WHERE created_unix < $cutoff;";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
            pruned = cmd.ExecuteNonQuery();
        }

        foreach (var file in screenshots)
        {
            DeleteScreenshotFile(file);
        }

        return pruned;
    }

    /// <summary>Status → count, for the admin header line.</summary>
    public Dictionary<string, int> CountByStatus()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT status, COUNT(*) FROM bugreport GROUP BY status;";
            var counts = new Dictionary<string, int>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }

            return counts;
        }
    }

    /// <summary>Absolute path of a report's screenshot, or null when it has none / the file is gone.</summary>
    public string? ScreenshotPath(BugReportRecord record)
    {
        if (string.IsNullOrEmpty(record.ScreenshotFile))
        {
            return null;
        }

        string path = Path.Combine(_screenshotsDir, record.ScreenshotFile);
        return File.Exists(path) ? path : null;
    }

    private void DeleteScreenshotFile(string screenshotFile)
    {
        if (string.IsNullOrEmpty(screenshotFile))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(_screenshotsDir, screenshotFile));
        }
        catch (IOException)
        {
            // best-effort: an undeletable orphan file is not worth failing the request over
        }
    }

    private static BugReportRecord ReadRecord(SqliteDataReader r) => new(
        Id: r.GetString(r.GetOrdinal("id")),
        Title: r.GetString(r.GetOrdinal("title")),
        Description: r.GetString(r.GetOrdinal("description")),
        Email: r.GetString(r.GetOrdinal("email")),
        GameVersion: r.GetString(r.GetOrdinal("game_version")),
        BuildNumber: r.GetString(r.GetOrdinal("build_number")),
        PlayerId: r.GetString(r.GetOrdinal("player_id")),
        PlayerName: r.GetString(r.GetOrdinal("player_name")),
        SessionId: r.GetString(r.GetOrdinal("session_id")),
        Platform: r.GetString(r.GetOrdinal("platform")),
        ClientTimestamp: r.GetString(r.GetOrdinal("client_timestamp")),
        Category: r.GetString(r.GetOrdinal("category")),
        Source: r.GetString(r.GetOrdinal("source")),
        Kind: r.GetString(r.GetOrdinal("kind")),
        Status: r.GetString(r.GetOrdinal("status")),
        ScreenshotFile: r.GetString(r.GetOrdinal("screenshot_file")),
        ReportJson: r.GetString(r.GetOrdinal("report_json")),
        CreatedUnix: r.GetInt64(r.GetOrdinal("created_unix")),
        ReplyKey: r.GetString(r.GetOrdinal("reply_key")),
        FixedInVersion: r.GetString(r.GetOrdinal("fixed_in_version")));

    /// <summary>Adds a column to <c>bugreport</c> when an older database lacks it (SQLite has no
    /// ADD COLUMN IF NOT EXISTS).</summary>
    private void EnsureColumn(string column, string definition)
    {
        using (var check = _db.CreateCommand())
        {
            check.CommandText = "PRAGMA table_info(bugreport);";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        Exec($"ALTER TABLE bugreport ADD COLUMN {column} {definition};");
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}

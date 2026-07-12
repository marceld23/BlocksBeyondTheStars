// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>One stored bug report. <c>ScreenshotFile</c> is the bare file name inside the store's
/// screenshots/ folder ("" = no screenshot); the image bytes never live in the database.</summary>
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
    long CreatedUnix);

/// <summary>Triage states a report moves through in the admin UI.</summary>
public static class BugReportStatus
{
    public const string New = "new";
    public const string Triaged = "triaged";
    public const string Done = "done";

    public static bool IsValid(string status) => status is New or Triaged or Done;
}

/// <summary>
/// The inbox storage: reports in one SQLite file, screenshots as plain files next to it. Every mutation
/// is serialized on one connection behind a lock, mirroring the WorldHost registry pattern — the write
/// volume (hand-typed feedback + occasional crash bursts) is tiny. Listing is keyset-paginated on
/// (created_unix, id) ascending so a <c>since</c>-based delta sync never skips rows.
/// </summary>
public sealed class ReportStore : IDisposable
{
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
                created_unix INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS idx_bugreport_created ON bugreport(created_unix, id);
            CREATE INDEX IF NOT EXISTS idx_bugreport_status ON bugreport(status);
            """);
    }

    /// <summary>Stores a parsed report (and its screenshot file, when present) and returns the new id.
    /// <paramref name="nowUnix"/> is injectable for tests; production passes the current time.</summary>
    public string Add(ParsedReport report, long nowUnix)
    {
        string id = Guid.NewGuid().ToString("N");
        string screenshotFile = string.Empty;

        if (report.ScreenshotBytes is { Length: > 0 })
        {
            screenshotFile = id + "." + report.ScreenshotExtension;
            File.WriteAllBytes(Path.Combine(_screenshotsDir, screenshotFile), report.ScreenshotBytes);
        }

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO bugreport(id, title, description, email, game_version, build_number, player_id,
                    player_name, session_id, platform, client_timestamp, category, source, kind, status,
                    screenshot_file, report_json, created_unix)
                VALUES ($id, $title, $desc, $email, $gv, $bn, $pid, $pname, $sid, $plat, $cts, $cat, $src,
                    $kind, 'new', $shot, $json, $created);
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
            cmd.ExecuteNonQuery();
        }

        return id;
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

    /// <summary>Deletes a report AND its screenshot file (reports may carry an e-mail — deletion must not
    /// leave partial personal data behind).</summary>
    public bool Delete(string id)
    {
        var record = Get(id);
        if (record == null)
        {
            return false;
        }

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM bugreport WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        DeleteScreenshotFile(record.ScreenshotFile);
        return true;
    }

    /// <summary>Removes reports older than <paramref name="retentionDays"/> (0 = keep forever) including
    /// their screenshot files; returns how many were pruned. Called at startup and after each ingest.</summary>
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
        CreatedUnix: r.GetInt64(r.GetOrdinal("created_unix")));

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}

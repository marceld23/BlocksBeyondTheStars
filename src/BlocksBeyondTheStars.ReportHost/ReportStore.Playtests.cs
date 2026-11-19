// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The report store's queries for playtest reports (category <see cref="ReportIngest.PlaytestCategory"/>): what one
/// install sent, what came in for the evaluation of a task, and the retention of the testers' attached work.
/// </summary>
public sealed partial class ReportStore
{
    /// <summary>The playtest reports sent under <paramref name="replyKey"/>, newest first — what the game reads back to
    /// restore its badges and to learn which reports were confirmed. A malformed key reads nothing.</summary>
    public List<BugReportRecord> PlaytestReportsOf(string replyKey, int limit = 200)
    {
        if (!BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.IsWellFormed(replyKey))
        {
            return new List<BugReportRecord>();
        }

        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM bugreport
                WHERE category = $cat AND reply_key = $key
                ORDER BY created_unix DESC, id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$cat", ReportIngest.PlaytestCategory);
            cmd.Parameters.AddWithValue("$key", replyKey);
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
            return ReadAll(cmd);
        }
    }

    /// <summary>Every playtest report, newest first — the evaluation of the tasks reads them.</summary>
    public List<BugReportRecord> PlaytestReports(int limit = 5000)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM bugreport WHERE category = $cat ORDER BY created_unix DESC, id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$cat", ReportIngest.PlaytestCategory);
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 20_000));
            return ReadAll(cmd);
        }
    }

    /// <summary>
    /// Removes the attached work of playtest reports older than <paramref name="retentionDays"/> — the files and
    /// their rows, never the report: the answers stay for the evaluation, the tester's ship or texture was only ever
    /// there to find a fault. 0 keeps them forever. Returns how many reports lost their attachments.
    /// </summary>
    public int PrunePlaytestAttachments(int retentionDays, long nowUnix)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var ids = new List<string>();
        lock (_gate)
        {
            using var select = _db.CreateCommand();
            select.CommandText = """
                SELECT DISTINCT b.id FROM bugreport b JOIN report_attachment a ON a.report_id = b.id
                WHERE b.category = $cat AND b.created_unix < $cutoff;
                """;
            select.Parameters.AddWithValue("$cat", ReportIngest.PlaytestCategory);
            select.Parameters.AddWithValue("$cutoff", nowUnix - (retentionDays * 86400L));
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        DeleteAttachments(ids);
        return ids.Count;
    }

    /// <summary>Runs <paramref name="cmd"/> and reads every row. The caller holds the lock.</summary>
    private static List<BugReportRecord> ReadAll(Microsoft.Data.Sqlite.SqliteCommand cmd)
    {
        var items = new List<BugReportRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadRecord(reader));
        }

        return items;
    }
}

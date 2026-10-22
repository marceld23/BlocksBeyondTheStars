// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Player reports and feedback behind <see cref="HostRegistry"/> (table <c>report</c>), reviewed by hand through the
/// operator admin endpoints. Shares the registry's one <see cref="RegistryDb"/> connection and lock.
/// </summary>
internal sealed class PlayerReportStore
{
    private readonly RegistryDb _db;

    public PlayerReportStore(RegistryDb db)
    {
        _db = db;
    }

    /// <inheritdoc cref="HostRegistry.CreateReport"/>
    public (bool Ok, string Error) CreateReport(string reporterAccountId, string worldId, string reportedName, string category, string message)
    {
        category = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (category is not ("chat" or "name" or "griefing" or "other" or "feedback"))
        {
            return (false, "Unknown report category.");
        }

        reportedName = (reportedName ?? string.Empty).Trim();
        int minNameLength = category == "feedback" ? 0 : 1;
        if (reportedName.Length < minNameLength || reportedName.Length > 24)
        {
            return (false, "Reported player name must be 1-24 characters.");
        }

        message = (message ?? string.Empty).Trim();
        if (category == "feedback" && message.Length == 0)
        {
            return (false, "Feedback needs a message.");
        }

        if (message.Length > 500)
        {
            message = message.Substring(0, 500);
        }

        // The world id is optional context (which world's logs to check) — anything that is not a
        // well-formed id is stored as empty rather than rejected, so a stale client never loses a report.
        worldId = (worldId ?? string.Empty).Trim();
        worldId = HostRegistry.IsValidWorldId(worldId) ? worldId : string.Empty;

        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                INSERT INTO report(world_id, reporter_account_id, reported_name, category, message, created_unix)
                VALUES($w, $r, $n, $c, $m, $now)
                """);
            cmd.Parameters.AddWithValue("$w", worldId ?? string.Empty);
            cmd.Parameters.AddWithValue("$r", reporterAccountId);
            cmd.Parameters.AddWithValue("$n", reportedName);
            cmd.Parameters.AddWithValue("$c", category);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.ExecuteNonQuery();
            return (true, string.Empty);
        }
    }

    public IReadOnlyList<ReportRecord> ListOpenReports()
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT id, world_id, reporter_account_id, reported_name, category, message, status, created_unix
                FROM report WHERE status = 'open' ORDER BY created_unix
                """);
            using var reader = cmd.ExecuteReader();
            var list = new List<ReportRecord>();
            while (reader.Read())
            {
                list.Add(new ReportRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt64(7)));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.CloseReport"/>
    public void CloseReport(long reportId, string status)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("UPDATE report SET status = $s WHERE id = $i");
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$i", reportId);
            cmd.ExecuteNonQuery();
        }
    }
}

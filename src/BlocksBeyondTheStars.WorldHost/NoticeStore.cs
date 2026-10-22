// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The player notices behind <see cref="HostRegistry"/> (table <c>account_notice</c>) — the "why can't I play any
/// more?" inbox. <see cref="AccountStore"/> files ban/unban notices through <see cref="AddNoticeLocked"/> inside its
/// own locked update, so this store shares the registry's one <see cref="RegistryDb"/> and its lock.
/// </summary>
internal sealed class NoticeStore
{
    private readonly RegistryDb _db;

    public NoticeStore(RegistryDb db)
    {
        _db = db;
    }

    /// <inheritdoc cref="HostRegistry.AddNotice"/>
    public void AddNotice(string accountId, string kind, string subject, string reason, string reasonCode = "", long untilUnix = 0)
    {
        lock (_db.Gate)
        {
            AddNoticeLocked(accountId, kind, subject, reason, reasonCode, untilUnix, RegistryDb.NowUnix());
        }
    }

    /// <summary>Inserts one notice row. The caller already holds <see cref="RegistryDb.Gate"/> (see
    /// <see cref="AddNotice"/> and the ban update in <see cref="AccountStore.SetBannedUntil"/>).</summary>
    internal void AddNoticeLocked(string accountId, string kind, string subject, string reason, string reasonCode, long untilUnix, long now)
    {
        using var cmd = _db.Cmd("""
            INSERT INTO account_notice(account_id, kind, subject, reason, reason_code, until_unix, created_unix)
            VALUES($a, $k, $s, $r, $rc, $u, $c)
            """);
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$s", subject ?? string.Empty);
        cmd.Parameters.AddWithValue("$r", reason ?? string.Empty);
        cmd.Parameters.AddWithValue("$rc", reasonCode ?? string.Empty);
        cmd.Parameters.AddWithValue("$u", untilUnix);
        cmd.Parameters.AddWithValue("$c", now);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc cref="HostRegistry.ListNotices"/>
    public IReadOnlyList<NoticeRecord> ListNotices(string accountId, bool unseenOnly = true, int limit = 20)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd($"""
                SELECT id, kind, subject, reason, reason_code, until_unix, created_unix, seen_unix
                FROM account_notice
                WHERE account_id = $a {(unseenOnly ? "AND seen_unix = 0" : string.Empty)}
                ORDER BY created_unix DESC, id DESC LIMIT $l
                """);
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$l", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<NoticeRecord>();
            while (reader.Read())
            {
                list.Add(new NoticeRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7)));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.MarkNoticesSeen"/>
    public void MarkNoticesSeen(string accountId, long noticeId = 0)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd(noticeId > 0
                ? "UPDATE account_notice SET seen_unix = $now WHERE account_id = $a AND id = $i AND seen_unix = 0"
                : "UPDATE account_notice SET seen_unix = $now WHERE account_id = $a AND seen_unix = 0");
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.Parameters.AddWithValue("$a", accountId);
            if (noticeId > 0)
            {
                cmd.Parameters.AddWithValue("$i", noticeId);
            }

            cmd.ExecuteNonQuery();
        }
    }
}

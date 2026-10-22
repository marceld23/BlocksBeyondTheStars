// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The world owner's own lever behind <see cref="HostRegistry"/>: per-world bans and the visitor list their ban UI
/// picks from (tables <c>world_ban</c>, <c>world_visitor</c>). Shares the registry's one <see cref="RegistryDb"/>
/// connection and lock.
/// </summary>
internal sealed class WorldModerationStore
{
    private readonly RegistryDb _db;

    public WorldModerationStore(RegistryDb db)
    {
        _db = db;
    }

    /// <inheritdoc cref="HostRegistry.AddWorldBan"/>
    public bool AddWorldBan(string worldId, string accountId, string playerName, string reason)
    {
        playerName = (playerName ?? string.Empty).Trim();
        accountId ??= string.Empty;
        if (!HostRegistry.IsValidWorldId(worldId) || (playerName.Length == 0 && accountId.Length == 0))
        {
            return false;
        }

        lock (_db.Gate)
        {
            using var check = _db.Cmd("""
                SELECT 1 FROM world_ban
                WHERE world_id = $w AND account_id = $a AND lower(player_name) = lower($n)
                """);
            check.Parameters.AddWithValue("$w", worldId);
            check.Parameters.AddWithValue("$a", accountId);
            check.Parameters.AddWithValue("$n", playerName);
            if (check.ExecuteScalar() != null)
            {
                return true;
            }

            using var cmd = _db.Cmd("""
                INSERT INTO world_ban(world_id, account_id, player_name, reason, created_unix)
                VALUES($w, $a, $n, $r, $c)
                """);
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$n", playerName);
            cmd.Parameters.AddWithValue("$r", (reason ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$c", RegistryDb.NowUnix());
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    /// <inheritdoc cref="HostRegistry.RemoveWorldBan"/>
    public void RemoveWorldBan(string worldId, long banId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("DELETE FROM world_ban WHERE world_id = $w AND id = $i");
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$i", banId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<WorldBanRecord> ListWorldBans(string worldId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT id, world_id, account_id, player_name, reason, created_unix
                FROM world_ban WHERE world_id = $w ORDER BY created_unix DESC
                """);
            cmd.Parameters.AddWithValue("$w", worldId ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldBanRecord>();
            while (reader.Read())
            {
                list.Add(new WorldBanRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt64(5)));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.FindWorldBan"/>
    public WorldBanRecord? FindWorldBan(string worldId, string accountId, string playerName)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT id, world_id, account_id, player_name, reason, created_unix
                FROM world_ban
                WHERE world_id = $w
                  AND ((account_id <> '' AND account_id = $a) OR (player_name <> '' AND lower(player_name) = lower($n)))
                ORDER BY created_unix DESC LIMIT 1
                """);
            cmd.Parameters.AddWithValue("$w", worldId ?? string.Empty);
            cmd.Parameters.AddWithValue("$a", accountId ?? string.Empty);
            cmd.Parameters.AddWithValue("$n", (playerName ?? string.Empty).Trim());
            using var reader = cmd.ExecuteReader();
            return reader.Read()
                ? new WorldBanRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt64(5))
                : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.RecordWorldVisitor"/>
    public void RecordWorldVisitor(string worldId, string accountId, string playerName)
    {
        playerName = (playerName ?? string.Empty).Trim();
        if (playerName.Length == 0)
        {
            return;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                INSERT INTO world_visitor(world_id, account_id, player_name, first_seen_unix, last_seen_unix)
                VALUES($w, $a, $n, $now, $now)
                ON CONFLICT(world_id, account_id, player_name) DO UPDATE SET last_seen_unix = $now
                """);
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$a", accountId ?? string.Empty);
            cmd.Parameters.AddWithValue("$n", playerName);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.ListWorldVisitors"/>
    public IReadOnlyList<WorldVisitorRecord> ListWorldVisitors(string worldId, int limit = 30)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT world_id, account_id, player_name, first_seen_unix, last_seen_unix
                FROM world_visitor WHERE world_id = $w ORDER BY last_seen_unix DESC LIMIT $l
                """);
            cmd.Parameters.AddWithValue("$w", worldId ?? string.Empty);
            cmd.Parameters.AddWithValue("$l", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldVisitorRecord>();
            while (reader.Read())
            {
                list.Add(new WorldVisitorRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4)));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.ListVisitorNamesForAccount"/>
    public IReadOnlyList<(string WorldId, string PlayerName)> ListVisitorNamesForAccount(string accountId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("SELECT world_id, player_name FROM world_visitor WHERE account_id = $a");
            cmd.Parameters.AddWithValue("$a", accountId ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            var list = new List<(string, string)>();
            while (reader.Read())
            {
                list.Add((reader.GetString(0), reader.GetString(1)));
            }

            return list;
        }
    }
}

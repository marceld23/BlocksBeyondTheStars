// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// glitch.fun guests and install-id bans behind <see cref="HostRegistry"/> (tables <c>glitch_guest</c>,
/// <c>glitch_ban</c>) — the arcade twin of accounts and account bans. Shares the registry's one
/// <see cref="RegistryDb"/> connection and lock.
/// </summary>
internal sealed class GlitchStore
{
    private readonly RegistryDb _db;

    public GlitchStore(RegistryDb db)
    {
        _db = db;
    }

    /// <inheritdoc cref="HostRegistry.TouchGlitchGuest"/>
    public void TouchGlitchGuest(string installId, string playerName)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                INSERT INTO glitch_guest(install_id, player_name, first_seen_unix, last_seen_unix, sessions)
                VALUES($i, $n, $now, $now, 1)
                ON CONFLICT(install_id) DO UPDATE SET
                    player_name = $n, last_seen_unix = $now, sessions = sessions + 1
                """);
            cmd.Parameters.AddWithValue("$i", installId);
            cmd.Parameters.AddWithValue("$n", playerName ?? string.Empty);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.ListGlitchGuests"/>
    public IReadOnlyList<GlitchGuestRecord> ListGlitchGuests(int limit = 50)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT install_id, player_name, first_seen_unix, last_seen_unix, sessions
                FROM glitch_guest ORDER BY last_seen_unix DESC LIMIT $l
                """);
            cmd.Parameters.AddWithValue("$l", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<GlitchGuestRecord>();
            while (reader.Read())
            {
                list.Add(new GlitchGuestRecord(reader.GetString(0), reader.GetString(1),
                    reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.SetGlitchBanned"/>
    public void SetGlitchBanned(string installId, bool banned, string reason, string playerName = "")
    {
        installId = (installId ?? string.Empty).Trim();
        if (installId.Length == 0)
        {
            return;
        }

        lock (_db.Gate)
        {
            if (!banned)
            {
                using var del = _db.Cmd("DELETE FROM glitch_ban WHERE install_id = $i");
                del.Parameters.AddWithValue("$i", installId);
                del.ExecuteNonQuery();
                return;
            }

            using var cmd = _db.Cmd("""
                INSERT INTO glitch_ban(install_id, player_name, reason, created_unix)
                VALUES($i, $n, $r, $now)
                ON CONFLICT(install_id) DO UPDATE SET player_name = $n, reason = $r
                """);
            cmd.Parameters.AddWithValue("$i", installId);
            cmd.Parameters.AddWithValue("$n", playerName ?? string.Empty);
            cmd.Parameters.AddWithValue("$r", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.GetGlitchBan"/>
    public GlitchBanRecord? GetGlitchBan(string installId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("SELECT install_id, player_name, reason, created_unix FROM glitch_ban WHERE install_id = $i");
            cmd.Parameters.AddWithValue("$i", (installId ?? string.Empty).Trim());
            using var reader = cmd.ExecuteReader();
            return reader.Read()
                ? new GlitchBanRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3))
                : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.ListGlitchBans"/>
    public IReadOnlyList<GlitchBanRecord> ListGlitchBans()
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("SELECT install_id, player_name, reason, created_unix FROM glitch_ban ORDER BY created_unix DESC");
            using var reader = cmd.ExecuteReader();
            var list = new List<GlitchBanRecord>();
            while (reader.Read())
            {
                list.Add(new GlitchBanRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            }

            return list;
        }
    }
}

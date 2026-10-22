// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Worlds behind <see cref="HostRegistry"/> (table <c>world</c>): creation with quota and host-port allocation, the
/// owner/public/channel listings, status and archive bookkeeping, and the fleet overview. <see cref="DeleteWorld"/>
/// also clears the world's ban and visitor rows as one locked sequence, which is why every store shares the one
/// <see cref="RegistryDb"/> connection and lock.
/// </summary>
internal sealed class WorldStore
{
    private readonly RegistryDb _db;
    private readonly WorldHostConfig _config;
    private readonly RegistryNameRules _names;

    public WorldStore(RegistryDb db, WorldHostConfig config, RegistryNameRules names)
    {
        _db = db;
        _config = config;
        _names = names;
    }

    /// <inheritdoc cref="HostRegistry.ListAllWorldsAdmin"/>
    public IReadOnlyList<(WorldRecord World, string OwnerName)> ListAllWorldsAdmin(int limit = 500)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                SELECT w.id, w.owner_account_id, w.display_name, w.join_secret, w.host_port, w.status,
                       w.container_id, w.created_unix, w.last_started_unix, w.channel, a.name,
                       w.save_too_new_image, w.save_too_new_unix
                FROM world w LEFT JOIN account a ON a.id = w.owner_account_id
                ORDER BY CASE w.status WHEN 'running' THEN 0 WHEN 'starting' THEN 1 WHEN 'stopped' THEN 2 ELSE 3 END,
                         w.last_started_unix DESC
                LIMIT $l
                """);
            cmd.Parameters.AddWithValue("$l", limit);
            using var reader = cmd.ExecuteReader();
            var list = new List<(WorldRecord, string)>();
            while (reader.Read())
            {
                string channel = reader.GetString(9);
                string ownerName = !reader.IsDBNull(10) ? reader.GetString(10)
                    : channel == WorldChannel.Glitch ? "glitch.fun" : "(deleted)";
                list.Add((new WorldRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                    reader.GetInt64(7), reader.GetInt64(8), Channel: channel,
                    SaveTooNewImage: reader.GetString(11), SaveTooNewUnix: reader.GetInt64(12)), ownerName));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.ValidateWorldPassword"/>
    public static (bool Ok, string Error) ValidateWorldPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return (true, string.Empty);
        }

        if (password.Length is < 4 or > 24 || password.Any(char.IsControl))
        {
            return (false, "World password must be 4-24 printable characters.");
        }

        return (true, string.Empty);
    }

    /// <inheritdoc cref="HostRegistry.CreateWorld"/>
    public (bool Ok, string Error, WorldRecord? World) CreateWorld(string ownerAccountId, string displayName, string? password = null)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 40 || displayName.Any(char.IsControl))
        {
            return (false, "World name must be 1-40 printable characters.", null);
        }

        if (_names.IsBlockedName(displayName))
        {
            return (false, "Please choose a different world name.", null);
        }

        if (ValidateWorldPassword(password) is (false, var passwordError))
        {
            return (false, passwordError, null);
        }

        lock (_db.Gate)
        {
            using (var count = _db.Cmd("SELECT COUNT(*) FROM world WHERE owner_account_id = $o"))
            {
                count.Parameters.AddWithValue("$o", ownerAccountId);
                if (Convert.ToInt32(count.ExecuteScalar()) >= _config.MaxWorldsPerAccount)
                {
                    return (false, $"World limit reached ({_config.MaxWorldsPerAccount} per account).", null);
                }
            }

            int? port = NextFreePortLocked();
            if (port is null)
            {
                return (false, "No capacity available right now — please try again later.", null);
            }

            var world = new WorldRecord(
                Id: RegistryDb.RandomHex(6), // 6 random bytes = the 12 hex chars WorldIdRx/subdomains are built on
                OwnerAccountId: ownerAccountId,
                DisplayName: displayName,
                JoinSecret: RegistryDb.RandomHex(32),
                HostPort: port.Value,
                Status: WorldStatus.Stopped,
                ContainerId: string.Empty,
                CreatedUnix: RegistryDb.NowUnix(),
                LastStartedUnix: 0,
                PasswordHash: string.IsNullOrEmpty(password) ? string.Empty : PasswordHasher.Hash(password));

            using var ins = _db.Cmd("""
                INSERT INTO world(id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix, password_hash)
                VALUES($i, $o, $d, $s, $p, $st, '', $c, 0, $ph)
                """);
            ins.Parameters.AddWithValue("$i", world.Id);
            ins.Parameters.AddWithValue("$o", world.OwnerAccountId);
            ins.Parameters.AddWithValue("$d", world.DisplayName);
            ins.Parameters.AddWithValue("$s", world.JoinSecret);
            ins.Parameters.AddWithValue("$p", world.HostPort);
            ins.Parameters.AddWithValue("$st", world.Status);
            ins.Parameters.AddWithValue("$c", world.CreatedUnix);
            ins.Parameters.AddWithValue("$ph", world.PasswordHash);
            ins.ExecuteNonQuery();

            return (true, string.Empty, world);
        }
    }

    /// <inheritdoc cref="HostRegistry.CreateGlitchWorld"/>
    public (bool Ok, string Error, WorldRecord? World) CreateGlitchWorld(string displayName)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 40 || displayName.Any(char.IsControl))
        {
            return (false, "World name must be 1-40 printable characters.", null);
        }

        lock (_db.Gate)
        {
            int? port = NextFreePortLocked();
            if (port is null)
            {
                return (false, "No capacity available right now — please try again later.", null);
            }

            var world = new WorldRecord(
                Id: RegistryDb.RandomHex(6),
                OwnerAccountId: WorldChannel.Glitch,
                DisplayName: displayName,
                JoinSecret: RegistryDb.RandomHex(32),
                HostPort: port.Value,
                Status: WorldStatus.Stopped,
                ContainerId: string.Empty,
                CreatedUnix: RegistryDb.NowUnix(),
                LastStartedUnix: 0,
                Channel: WorldChannel.Glitch);

            using var ins = _db.Cmd("""
                INSERT INTO world(id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix, channel)
                VALUES($i, $o, $d, $s, $p, $st, '', $c, 0, $ch)
                """);
            ins.Parameters.AddWithValue("$i", world.Id);
            ins.Parameters.AddWithValue("$o", world.OwnerAccountId);
            ins.Parameters.AddWithValue("$d", world.DisplayName);
            ins.Parameters.AddWithValue("$s", world.JoinSecret);
            ins.Parameters.AddWithValue("$p", world.HostPort);
            ins.Parameters.AddWithValue("$st", world.Status);
            ins.Parameters.AddWithValue("$c", world.CreatedUnix);
            ins.Parameters.AddWithValue("$ch", world.Channel);
            ins.ExecuteNonQuery();

            return (true, string.Empty, world);
        }
    }

    /// <inheritdoc cref="HostRegistry.ListWorldsByChannel"/>
    public IReadOnlyList<WorldRecord> ListWorldsByChannel(string channel)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd(SelectWorld + " WHERE channel = $ch ORDER BY created_unix");
            cmd.Parameters.AddWithValue("$ch", channel ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.SetWorldPassword"/>
    public (bool Ok, string Error) SetWorldPassword(string worldId, string? password)
    {
        if (ValidateWorldPassword(password) is (false, var error))
        {
            return (false, error);
        }

        lock (_db.Gate)
        {
            bool removing = string.IsNullOrEmpty(password);
            // A public world must always have a password (safety rule): removing the password also
            // un-lists it, so it can never end up publicly joinable by anyone with no gate at all.
            using var cmd = _db.Cmd(removing
                ? "UPDATE world SET password_hash = '', is_public = 0 WHERE id = $i"
                : "UPDATE world SET password_hash = $ph WHERE id = $i");
            if (!removing)
            {
                cmd.Parameters.AddWithValue("$ph", PasswordHasher.Hash(password!));
            }

            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            return cmd.ExecuteNonQuery() == 1 ? (true, string.Empty) : (false, "World not found.");
        }
    }

    /// <inheritdoc cref="HostRegistry.SetWorldVisibility"/>
    public (bool Ok, string Error) SetWorldVisibility(string worldId, bool isPublic)
    {
        lock (_db.Gate)
        {
            if (isPublic)
            {
                using var check = _db.Cmd("SELECT password_hash FROM world WHERE id = $i");
                check.Parameters.AddWithValue("$i", worldId ?? string.Empty);
                if (check.ExecuteScalar() is not string hash)
                {
                    return (false, "World not found.");
                }

                if (hash.Length == 0)
                {
                    return (false, "A public world needs a join password first.");
                }
            }

            using var cmd = _db.Cmd("UPDATE world SET is_public = $p WHERE id = $i");
            cmd.Parameters.AddWithValue("$p", isPublic ? 1 : 0);
            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            return cmd.ExecuteNonQuery() == 1 ? (true, string.Empty) : (false, "World not found.");
        }
    }

    /// <inheritdoc cref="HostRegistry.ListPublicWorlds"/>
    public IReadOnlyList<WorldRecord> ListPublicWorlds()
    {
        lock (_db.Gate)
        {
            // running (2) → starting (1) → everything else (0), then alphabetical. Portal channel only:
            // glitch arcade worlds must never surface in the portal's public browser.
            using var cmd = _db.Cmd(SelectWorld + @" WHERE is_public = 1 AND channel = ''
                ORDER BY CASE status WHEN 'running' THEN 2 WHEN 'starting' THEN 1 ELSE 0 END DESC,
                         display_name COLLATE NOCASE");
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    public IReadOnlyList<WorldRecord> ListWorlds(string ownerAccountId)
    {
        lock (_db.Gate)
        {
            // Portal channel only (defense in depth: the glitch pool's synthetic owner id is not an
            // account, but no account listing should ever surface an arcade world either way).
            using var cmd = _db.Cmd(SelectWorld + " WHERE owner_account_id = $o AND channel = '' ORDER BY created_unix");
            cmd.Parameters.AddWithValue("$o", ownerAccountId);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    public WorldRecord? GetWorld(string worldId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd(SelectWorld + " WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadWorld(reader) : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.ListActiveWorlds"/>
    public IReadOnlyList<WorldRecord> ListActiveWorlds()
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd(SelectWorld + " WHERE status IN ($starting, $running)");
            cmd.Parameters.AddWithValue("$starting", WorldStatus.Starting);
            cmd.Parameters.AddWithValue("$running", WorldStatus.Running);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    /// <inheritdoc cref="HostRegistry.FindBySubdomain"/>
    public WorldRecord? FindBySubdomain(string subdomain)
    {
        if (subdomain is null || !subdomain.StartsWith("w-", StringComparison.Ordinal))
        {
            return null;
        }

        string id = subdomain.Substring(2);
        return HostRegistry.IsValidWorldId(id) ? GetWorld(id) : null;
    }

    public void SetWorldStatus(string worldId, string status, string containerId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
                UPDATE world SET status = $st, container_id = $c,
                    last_started_unix = CASE WHEN $st = 'starting' THEN $now ELSE last_started_unix END
                WHERE id = $i
                """);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$c", containerId);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.MarkSaveTooNew"/>
    public void MarkSaveTooNew(string worldId, string serverImage, long atUnix)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("UPDATE world SET save_too_new_image = $img, save_too_new_unix = $at WHERE id = $i");
            cmd.Parameters.AddWithValue("$img", serverImage ?? string.Empty);
            cmd.Parameters.AddWithValue("$at", atUnix);
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.ClearSaveTooNew"/>
    public void ClearSaveTooNew(string worldId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("UPDATE world SET save_too_new_image = '', save_too_new_unix = 0 WHERE id = $i AND save_too_new_image <> ''");
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.TouchWorldActive"/>
    public void TouchWorldActive(string worldId)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("UPDATE world SET last_active_unix = $now WHERE id = $i");
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.ListArchiveCandidates"/>
    public IReadOnlyList<WorldRecord> ListArchiveCandidates(long cutoffUnix)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd(SelectWorld + " WHERE status = $st AND MAX(last_active_unix, created_unix) < $cutoff");
            cmd.Parameters.AddWithValue("$st", WorldStatus.Stopped);
            cmd.Parameters.AddWithValue("$cutoff", cutoffUnix);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    public void DeleteWorld(string worldId)
    {
        lock (_db.Gate)
        {
            // The world's own side tables go with it — a ban or visitor row for a world that no longer
            // exists is dead weight that a recycled id would also inherit.
            foreach (var sql in new[]
            {
                "DELETE FROM world_ban WHERE world_id = $i",
                "DELETE FROM world_visitor WHERE world_id = $i",
                "DELETE FROM world WHERE id = $i",
            })
            {
                using var cmd = _db.Cmd(sql);
                cmd.Parameters.AddWithValue("$i", worldId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    // ---------------- Internals ----------------

    private const string SelectWorld =
        "SELECT id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix, password_hash, is_public, channel, save_too_new_image, save_too_new_unix FROM world";

    private static WorldRecord ReadWorld(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
        r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8), r.GetString(9), r.GetInt64(10) != 0,
        r.GetString(11), r.GetString(12), r.GetInt64(13));

    /// <summary>Smallest unused port in the configured range. Ports stay allocated for a world's lifetime
    /// (they are its stable native-UDP endpoint), so a deleted world's port returns to the pool.</summary>
    private int? NextFreePortLocked()
    {
        var used = new HashSet<int>();
        using (var cmd = _db.Cmd("SELECT host_port FROM world"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                used.Add(reader.GetInt32(0));
            }
        }

        for (int p = _config.PortRangeStart; p < _config.PortRangeStart + _config.PortRangeSize; p++)
        {
            if (!used.Contains(p))
            {
                return p;
            }
        }

        return null;
    }
}

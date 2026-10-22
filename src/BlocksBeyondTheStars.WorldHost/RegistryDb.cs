// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The storage core behind <see cref="HostRegistry"/>: the one SQLite connection, the schema with its tolerant column
/// upgrades, and the one lock. Every per-aggregate store (<see cref="AccountStore"/>, <see cref="NoticeStore"/>,
/// <see cref="WorldStore"/>, <see cref="WorldModerationStore"/>, <see cref="PlayerReportStore"/>,
/// <see cref="GlitchStore"/>) shares this instance and serializes on <see cref="Gate"/>, so an operation that spans
/// tables — a ban and its notice, a new account and its first session, an account or world erasure — still runs as one
/// locked sequence on one connection.
/// </summary>
internal sealed class RegistryDb : IDisposable
{
    private readonly SqliteConnection _db;

    public RegistryDb(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Unpooled (#2768): one connection for the registry's whole life — a pool gains nothing, and no pool clearing
        // anywhere in the process can reach an unpooled handle.
        _db = new SqliteConnection($"Data Source={path};Pooling=False");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS account(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                password_hash TEXT NOT NULL,
                is_developer INTEGER NOT NULL DEFAULT 0,
                banned INTEGER NOT NULL DEFAULT 0,
                ban_reason TEXT NOT NULL DEFAULT '',
                ban_reason_code TEXT NOT NULL DEFAULT '',
                banned_at_unix INTEGER NOT NULL DEFAULT 0,
                banned_until_unix INTEGER NOT NULL DEFAULT 0,
                terms_version INTEGER NOT NULL DEFAULT 0,
                terms_accepted_unix INTEGER NOT NULL DEFAULT 0,
                created_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS session(
                token_hash TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                expires_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS world(
                id TEXT PRIMARY KEY,
                owner_account_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                join_secret TEXT NOT NULL,
                host_port INTEGER NOT NULL UNIQUE,
                status TEXT NOT NULL,
                container_id TEXT NOT NULL DEFAULT '',
                created_unix INTEGER NOT NULL,
                last_started_unix INTEGER NOT NULL DEFAULT 0,
                last_active_unix INTEGER NOT NULL DEFAULT 0,
                password_hash TEXT NOT NULL DEFAULT '',
                is_public INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS report(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                world_id TEXT NOT NULL,
                reporter_account_id TEXT NOT NULL,
                reported_name TEXT NOT NULL,
                category TEXT NOT NULL,
                message TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'open',
                created_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS glitch_guest(
                install_id TEXT PRIMARY KEY,
                player_name TEXT NOT NULL DEFAULT '',
                first_seen_unix INTEGER NOT NULL,
                last_seen_unix INTEGER NOT NULL,
                sessions INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS glitch_ban(
                install_id TEXT PRIMARY KEY,
                player_name TEXT NOT NULL DEFAULT '',
                reason TEXT NOT NULL DEFAULT '',
                created_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS account_notice(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                subject TEXT NOT NULL DEFAULT '',
                reason TEXT NOT NULL DEFAULT '',
                reason_code TEXT NOT NULL DEFAULT '',
                until_unix INTEGER NOT NULL DEFAULT 0,
                created_unix INTEGER NOT NULL,
                seen_unix INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_notice_account ON account_notice(account_id, seen_unix);
            CREATE TABLE IF NOT EXISTS world_ban(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                world_id TEXT NOT NULL,
                account_id TEXT NOT NULL DEFAULT '',
                player_name TEXT NOT NULL DEFAULT '',
                reason TEXT NOT NULL DEFAULT '',
                created_unix INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_world_ban_world ON world_ban(world_id);
            CREATE TABLE IF NOT EXISTS recovery_code(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id TEXT NOT NULL,
                code_hash TEXT NOT NULL,
                created_unix INTEGER NOT NULL,
                used_unix INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_recovery_account ON recovery_code(account_id);
            CREATE TABLE IF NOT EXISTS world_visitor(
                world_id TEXT NOT NULL,
                account_id TEXT NOT NULL,
                player_name TEXT NOT NULL,
                first_seen_unix INTEGER NOT NULL,
                last_seen_unix INTEGER NOT NULL,
                PRIMARY KEY(world_id, account_id, player_name));
            """);

        // Tolerant upgrades for registries created before newer account columns existed (pre-deployment
        // dev databases only); SQLite has no ADD COLUMN IF NOT EXISTS.
        foreach (var alter in new[]
        {
            "ALTER TABLE account ADD COLUMN is_developer INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE account ADD COLUMN banned INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE account ADD COLUMN ban_reason TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE account ADD COLUMN banned_at_unix INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE account ADD COLUMN banned_until_unix INTEGER NOT NULL DEFAULT 0;", // 0 = until an operator lifts it
            "ALTER TABLE account ADD COLUMN ban_reason_code TEXT NOT NULL DEFAULT '';", // localizable canned reason
            "ALTER TABLE account ADD COLUMN terms_version INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE account ADD COLUMN terms_accepted_unix INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE world ADD COLUMN last_active_unix INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE world ADD COLUMN password_hash TEXT NOT NULL DEFAULT '';", // #250: creator-set join password
            "ALTER TABLE world ADD COLUMN is_public INTEGER NOT NULL DEFAULT 0;", // public world browser (opt-in, requires password)
            "ALTER TABLE world ADD COLUMN channel TEXT NOT NULL DEFAULT '';", // world channel ('' portal, 'glitch' arcade)
            "ALTER TABLE world ADD COLUMN save_too_new_image TEXT NOT NULL DEFAULT '';", // #2464: the server image that refused the save (exit 3)
            "ALTER TABLE world ADD COLUMN save_too_new_unix INTEGER NOT NULL DEFAULT 0;", // #2464: when it did
            "ALTER TABLE account ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 0;", // set by an admin reset, cleared by the next change
        })
        {
            try
            {
                Exec(alter);
            }
            catch (SqliteException)
            {
                // column already exists
            }
        }
    }

    /// <summary>The registry's single lock. Each public store operation takes it once (never nested); the
    /// <c>…Locked</c> helpers expect their caller to hold it already.</summary>
    public Lock Gate { get; } = new();

    public SqliteCommand Cmd(string sql)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    public void Exec(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time string equality — used for the reserved-name claim code so a wrong code
    /// can't be probed character by character through timing.</summary>
    public static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));

    public void Dispose() => _db.Dispose();
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

public sealed record AccountRecord(
    string Id,
    string Name,
    bool IsDeveloper = false,
    bool IsBanned = false,
    string BanReason = "",
    int AcceptedTermsVersion = 0,
    long BannedAtUnix = 0,
    long BannedUntilUnix = 0,
    string BanReasonCode = "")
{
    /// <summary>True for a ban that ends by itself (a timeout); false for "until an operator lifts it".</summary>
    public bool BanExpires => IsBanned && BannedUntilUnix > 0;
}

/// <summary>
/// A message waiting for a player: why they were banned, that a ban was lifted, that one of their worlds
/// was deleted. Bans could be re-derived from the account row, but a deleted world leaves nothing behind
/// to derive anything from — the notice IS the record, written at the moment of the action.
/// </summary>
public sealed record NoticeRecord(
    long Id,
    string Kind,
    string Subject,
    string Reason,
    string ReasonCode,
    long UntilUnix,
    long CreatedUnix,
    long SeenUnix)
{
    public const string KindBanned = "banned";
    public const string KindUnbanned = "unbanned";
    public const string KindWorldDeleted = "world_deleted";
}

/// <summary>A player barred from ONE world by its owner (the lever a world owner actually needs — the
/// fleet-wide ban is the operator's). Keyed on the account when there is one; arcade guests have none,
/// so the player name is matched too.</summary>
public sealed record WorldBanRecord(
    long Id,
    string WorldId,
    string AccountId,
    string PlayerName,
    string Reason,
    long CreatedUnix);

/// <summary>Who played on a world under which in-game name — written at the join grant. Exists so the
/// owner's ban UI can offer a pick list (nobody remembers account ids) and so a fleet ban knows which
/// in-game names to kick.</summary>
public sealed record WorldVisitorRecord(
    string WorldId,
    string AccountId,
    string PlayerName,
    long FirstSeenUnix,
    long LastSeenUnix);

public sealed record WorldRecord(
    string Id,
    string OwnerAccountId,
    string DisplayName,
    string JoinSecret,
    int HostPort,
    string Status,
    string ContainerId,
    long CreatedUnix,
    long LastStartedUnix,
    string PasswordHash = "",
    bool IsPublic = false,
    string Channel = WorldChannel.Portal)
{
    /// <summary>The public routing label: <c>w-&lt;id&gt;.&lt;BaseDomain&gt;</c> resolves to this world's instance.</summary>
    public string Subdomain => "w-" + Id;

    /// <summary>True when the creator protected this world with a join password (#250).</summary>
    public bool HasPassword => PasswordHash.Length > 0;
}

/// <summary>Which storefront a world belongs to. Portal worlds ('') follow the Baumhaus rules
/// (player-created, password-gated, listed on the portal); glitch worlds exist ONLY for the
/// glitch.fun arcade — they never appear in the public browser or any account's world list, and are
/// joinable solely through the glitch session gateway's tokens.</summary>
public static class WorldChannel
{
    public const string Portal = "";
    public const string Glitch = "glitch";
}

/// <summary>A glitch.fun visitor seen by the session gateway — the admin UI's ban targets. The
/// install id is Glitch's pseudonymous per-player UUID; no further identity is stored.</summary>
public sealed record GlitchGuestRecord(
    string InstallId,
    string PlayerName,
    long FirstSeenUnix,
    long LastSeenUnix,
    long Sessions);

/// <summary>An install-id ban for the glitch.fun arcade (accounts don't exist on that channel, so
/// bans key on Glitch's install id instead).</summary>
public sealed record GlitchBanRecord(
    string InstallId,
    string PlayerName,
    string Reason,
    long CreatedUnix);

/// <summary>A filed player report awaiting (or after) operator review.</summary>
public sealed record ReportRecord(
    long Id,
    string WorldId,
    string ReporterAccountId,
    string ReportedName,
    string Category,
    string Message,
    string Status,
    long CreatedUnix);

/// <summary>World lifecycle states tracked in the registry.</summary>
public static class WorldStatus
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";

    /// <summary>Long-inactive: saves moved to the archive folder, instance claim ended. A join
    /// transparently restores + wakes the world (it just takes a moment longer).</summary>
    public const string Archived = "archived";
}

/// <summary>Registry gauges for the /metrics scrape.</summary>
public sealed record RegistryCounts(
    long Accounts,
    long OpenReports,
    IReadOnlyList<(string Status, long Count)> WorldsByStatus);

/// <summary>
/// The control plane's registry — accounts, bearer sessions and worlds in one SQLite file. Deliberately
/// privacy-minimal for the kid-facing free tier: an account is a display name + password hash, no email,
/// no personal data (the plan's account MVP). Every mutation is serialized on one connection behind a
/// lock, mirroring the game's SqliteWorldRepository pattern; the write volume here is tiny.
/// </summary>
public sealed class HostRegistry : IDisposable
{
    // Account names double as visible player identity; same cap as in-game names (24) and a conservative
    // character set so they are safe in URLs, logs and docker args without escaping anywhere.
    private static readonly Regex AccountNameRx = new("^[A-Za-z0-9_-]{3,24}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex WorldIdRx = new("^[a-f0-9]{12}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly Lock _gate = new();
    private readonly SqliteConnection _db;
    private readonly WorldHostConfig _config;

    public HostRegistry(WorldHostConfig config, string? databasePath = null)
    {
        _config = config;
        string path = databasePath ?? Path.Combine(config.DataDir, "worldhost.db");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new SqliteConnection($"Data Source={path}");
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

    // ---------------- Reserved names ----------------

    /// <summary>True when a name collides with a developer-reserved name. Both sides are normalized —
    /// lowercased with spaces/'-'/'_' stripped — so padding or separator tricks ("ju ju", "J_ustus")
    /// don't slip past the reservation.</summary>
    public bool IsReservedName(string? name)
    {
        string normalized = NormalizeName(name);
        return normalized.Length > 0 && _config.ReservedNames.Any(r => NormalizeName(r) == normalized);
    }

    private static string NormalizeName(string? name)
        => new((name ?? string.Empty).ToLowerInvariant().Where(c => c is not (' ' or '-' or '_')).ToArray());

    /// <summary>True when a name contains a blocked word (kid-facing name hygiene) — same normalization
    /// as the reservation check, so separator tricks don't slip past. Substring match on a deliberately
    /// short, unambiguous list.</summary>
    public bool IsBlockedName(string? name)
    {
        string normalized = NormalizeName(name);
        return normalized.Length > 0
               && _config.BlockedNameWords.Any(w => NormalizeName(w) is { Length: > 0 } bad && normalized.Contains(bad));
    }

    public static bool IsValidWorldId(string id) => WorldIdRx.IsMatch(id);

    // ---------------- Accounts & sessions ----------------

    /// <summary>Creates an account and returns a fresh session token. Fails on invalid/taken/reserved
    /// names, a too-short password, or when the caller has not accepted the CURRENT community rules
    /// (<paramref name="acceptedTermsVersion"/> must match the configured version — the signup UI sends it
    /// with the required checkbox). A developer registering a reserved name presents the operator's claim
    /// code, which permanently flags the account as a developer account. The error string is safe to show
    /// to the player.</summary>
    public (bool Ok, string Error, string AccountId, string SessionToken) CreateAccount(
        string name, string password, string? claimCode = null, int acceptedTermsVersion = 0)
    {
        if (acceptedTermsVersion != _config.TermsVersion)
        {
            return (false, "Please accept the community rules to create an account.", string.Empty, string.Empty);
        }

        if (!AccountNameRx.IsMatch(name ?? string.Empty))
        {
            return (false, "Name must be 3-24 characters: letters, digits, '-' or '_'.", string.Empty, string.Empty);
        }

        if ((password ?? string.Empty).Length < 8)
        {
            return (false, "Password must be at least 8 characters.", string.Empty, string.Empty);
        }

        if (IsBlockedName(name))
        {
            return (false, "Please choose a different name.", string.Empty, string.Empty);
        }

        bool isDeveloper = false;
        if (IsReservedName(name))
        {
            // With no claim code configured, reserved names are simply unclaimable — the safe default.
            if (string.IsNullOrEmpty(_config.ReservedClaimCode) || !FixedTimeEquals(claimCode ?? string.Empty, _config.ReservedClaimCode))
            {
                return (false, "This name is reserved.", string.Empty, string.Empty);
            }

            isDeveloper = true;
        }

        lock (_gate)
        {
            using (var check = Cmd("SELECT 1 FROM account WHERE name = $n"))
            {
                check.Parameters.AddWithValue("$n", name);
                if (check.ExecuteScalar() != null)
                {
                    return (false, "This name is already taken.", string.Empty, string.Empty);
                }
            }

            string id = "acc-" + RandomHex(12);
            using (var ins = Cmd("""
                INSERT INTO account(id, name, password_hash, is_developer, terms_version, terms_accepted_unix, created_unix)
                VALUES($i, $n, $p, $d, $tv, $ta, $c)
                """))
            {
                ins.Parameters.AddWithValue("$i", id);
                ins.Parameters.AddWithValue("$n", name);
                ins.Parameters.AddWithValue("$p", PasswordHasher.Hash(password!));
                ins.Parameters.AddWithValue("$d", isDeveloper ? 1 : 0);
                ins.Parameters.AddWithValue("$tv", acceptedTermsVersion);
                ins.Parameters.AddWithValue("$ta", NowUnix());
                ins.Parameters.AddWithValue("$c", NowUnix());
                ins.ExecuteNonQuery();
            }

            return (true, string.Empty, id, CreateSessionLocked(id));
        }
    }

    /// <summary>Verifies credentials and returns a fresh session token, or null. One generic failure —
    /// it never reveals whether the name exists.</summary>
    public (string AccountId, string SessionToken)? Login(string name, string password)
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT id, password_hash FROM account WHERE name = $n");
            cmd.Parameters.AddWithValue("$n", name ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || !PasswordHasher.Verify(password ?? string.Empty, reader.GetString(1)))
            {
                return null;
            }

            string id = reader.GetString(0);
            reader.Close();
            return (id, CreateSessionLocked(id));
        }
    }

    /// <summary>Column list every account read shares — see <see cref="ReadAccount"/> for the order.</summary>
    private const string AccountColumns =
        "id, name, is_developer, banned, ban_reason, terms_version, banned_at_unix, banned_until_unix, ban_reason_code";

    /// <summary>Materializes an account row. A timeout whose end has passed reads as NOT banned: the row
    /// keeps the history for the admin list, but every gate asking <c>IsBanned</c> lets the player back in
    /// without an operator having to lift anything by hand.</summary>
    private static AccountRecord ReadAccount(SqliteDataReader reader)
    {
        long until = reader.GetInt64(7);
        bool banned = reader.GetInt32(3) != 0 && (until <= 0 || until > NowUnix());
        return new AccountRecord(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0,
            banned, reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6), until, reader.GetString(8));
    }

    /// <summary>Resolves a bearer token to its account, or null when unknown/expired.</summary>
    public AccountRecord? ResolveSession(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_gate)
        {
            // Unqualified column names are unambiguous here: `session` carries none of them.
            using var cmd = Cmd($"""
                SELECT {AccountColumns}
                FROM session s JOIN account a ON a.id = s.account_id
                WHERE s.token_hash = $t AND s.expires_unix >= $now
                """);
            cmd.Parameters.AddWithValue("$t", Sha256Hex(token));
            cmd.Parameters.AddWithValue("$now", NowUnix());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    /// <summary>Records that an account accepted the (current) community rules version — the re-acceptance
    /// path after the operator bumps <see cref="WorldHostConfig.TermsVersion"/>.</summary>
    public void AcceptTerms(string accountId, int version)
    {
        lock (_gate)
        {
            using var cmd = Cmd("UPDATE account SET terms_version = $v, terms_accepted_unix = $now WHERE id = $i");
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.Parameters.AddWithValue("$i", accountId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Bans (or, with <paramref name="banned"/> false, unbans) an account. Banned accounts keep
    /// their session but every world action is refused with the reason. <paramref name="days"/> 0 means
    /// "until an operator lifts it"; anything greater is a timeout that ends by itself — the kid-facing
    /// default, and the reason the notice can promise a date. Both directions leave a notice behind, so
    /// the player learns what happened at their next login instead of finding a silently dead account.</summary>
    /// <returns>False when the ban was refused — an operator account can never be banned.</returns>
    public bool SetBanned(string accountId, bool banned, string reason, string reasonCode = "", int days = 0)
        => SetBannedUntil(accountId, banned, reason, reasonCode, banned && days > 0 ? NowUnix() + ((long)days * 86400) : 0);

    /// <summary>The primitive behind <see cref="SetBanned"/>, with the end of the timeout as an absolute
    /// time (unix seconds; 0 = until an operator lifts it). The admin UI uses the day-count form.</summary>
    public bool SetBannedUntil(string accountId, bool banned, string reason, string reasonCode, long untilUnix)
    {
        // Operator accounts are never bannable. The developer flag is only obtainable with the operator's
        // secret claim code, and the fleet ban is the operator's OWN lever — a banned operator would be
        // locked out of the fleet they run, with nobody left who could lift it. Unbanning stays allowed.
        if (banned && GetAccount(accountId) is { IsDeveloper: true })
        {
            return false;
        }

        long now = NowUnix();
        long until = banned ? untilUnix : 0;
        lock (_gate)
        {
            using var cmd = Cmd("""
                UPDATE account SET banned = $b, ban_reason = $r, ban_reason_code = $rc,
                    banned_at_unix = $at, banned_until_unix = $until
                WHERE id = $i
                """);
            cmd.Parameters.AddWithValue("$b", banned ? 1 : 0);
            cmd.Parameters.AddWithValue("$r", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("$rc", reasonCode ?? string.Empty);
            cmd.Parameters.AddWithValue("$at", banned ? now : 0L);
            cmd.Parameters.AddWithValue("$until", until);
            cmd.Parameters.AddWithValue("$i", accountId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                return false; // unknown account — no notice for a player who does not exist
            }

            AddNoticeLocked(accountId, banned ? NoticeRecord.KindBanned : NoticeRecord.KindUnbanned,
                subject: string.Empty, reason ?? string.Empty, reasonCode ?? string.Empty, until, now);
            return true;
        }
    }

    /// <summary>Case-insensitive account lookup by name — the admin UI's bridge from a reported in-game
    /// name to the account behind it (when they match; players can of course play under other names).</summary>
    public AccountRecord? FindAccountByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_gate)
        {
            using var cmd = Cmd($"SELECT {AccountColumns} FROM account WHERE lower(name) = lower($n)");
            cmd.Parameters.AddWithValue("$n", name.Trim());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    /// <summary>Accounts under an active ban, for the admin UI's ban list. Expired timeouts are filtered
    /// out here as well — they are no longer bans, they are history.</summary>
    public IReadOnlyList<AccountRecord> ListBannedAccounts()
    {
        lock (_gate)
        {
            using var cmd = Cmd($"SELECT {AccountColumns} FROM account WHERE banned = 1 ORDER BY name");
            using var reader = cmd.ExecuteReader();
            var list = new List<AccountRecord>();
            while (reader.Read())
            {
                var account = ReadAccount(reader);
                if (account.IsBanned)
                {
                    list.Add(account);
                }
            }

            return list;
        }
    }

    /// <summary>Looks an account up by id (the admin UI's ban forms carry ids, not names).</summary>
    public AccountRecord? GetAccount(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return null;
        }

        lock (_gate)
        {
            using var cmd = Cmd($"SELECT {AccountColumns} FROM account WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", accountId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    // ---------------- Player notices (the "why can't I play any more?" inbox) ----------------

    /// <summary>Files a notice for a player. Called for bans/unbans (see <see cref="SetBanned"/>) and when
    /// an operator deletes someone's world — that one leaves no other trace, the world row is gone.</summary>
    public void AddNotice(string accountId, string kind, string subject, string reason, string reasonCode = "", long untilUnix = 0)
    {
        lock (_gate)
        {
            AddNoticeLocked(accountId, kind, subject, reason, reasonCode, untilUnix, NowUnix());
        }
    }

    private void AddNoticeLocked(string accountId, string kind, string subject, string reason, string reasonCode, long untilUnix, long now)
    {
        using var cmd = Cmd("""
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

    /// <summary>The player's notices, newest first. Unseen only by default — that is what login and the
    /// portal poll show; the client acknowledges them once the player has read them.</summary>
    public IReadOnlyList<NoticeRecord> ListNotices(string accountId, bool unseenOnly = true, int limit = 20)
    {
        lock (_gate)
        {
            using var cmd = Cmd($"""
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

    /// <summary>Marks one notice (or, with <paramref name="noticeId"/> &lt;= 0, all of them) as read.
    /// Always scoped to the caller's own account — a notice id is a guessable integer.</summary>
    public void MarkNoticesSeen(string accountId, long noticeId = 0)
    {
        lock (_gate)
        {
            using var cmd = Cmd(noticeId > 0
                ? "UPDATE account_notice SET seen_unix = $now WHERE account_id = $a AND id = $i AND seen_unix = 0"
                : "UPDATE account_notice SET seen_unix = $now WHERE account_id = $a AND seen_unix = 0");
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.Parameters.AddWithValue("$a", accountId);
            if (noticeId > 0)
            {
                cmd.Parameters.AddWithValue("$i", noticeId);
            }

            cmd.ExecuteNonQuery();
        }
    }

    // ---------------- Per-world bans & visitors (the world owner's own lever) ----------------

    /// <summary>Bars a player from ONE world. Idempotent per (world, account, name) so a double click
    /// cannot pile up rows. Returns false when the world/name pair is unusable.</summary>
    public bool AddWorldBan(string worldId, string accountId, string playerName, string reason)
    {
        playerName = (playerName ?? string.Empty).Trim();
        accountId ??= string.Empty;
        if (!IsValidWorldId(worldId) || (playerName.Length == 0 && accountId.Length == 0))
        {
            return false;
        }

        lock (_gate)
        {
            using var check = Cmd("""
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

            using var cmd = Cmd("""
                INSERT INTO world_ban(world_id, account_id, player_name, reason, created_unix)
                VALUES($w, $a, $n, $r, $c)
                """);
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$n", playerName);
            cmd.Parameters.AddWithValue("$r", (reason ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("$c", NowUnix());
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    /// <summary>Lifts a world ban. The world id is part of the WHERE so an id from another world cannot
    /// be deleted by guessing.</summary>
    public void RemoveWorldBan(string worldId, long banId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("DELETE FROM world_ban WHERE world_id = $w AND id = $i");
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$i", banId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<WorldBanRecord> ListWorldBans(string worldId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
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

    /// <summary>The world ban that applies to this join, or null. Matches on the account — and on the
    /// in-game name too, because arcade guests have no account and because a name is what the owner
    /// actually recognises.</summary>
    public WorldBanRecord? FindWorldBan(string worldId, string accountId, string playerName)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
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

    /// <summary>Records that an account entered a world under an in-game name (upsert, written at the
    /// join grant). This is the pick list the owner's ban UI offers, and the only place the fleet knows
    /// which in-game names belong to an account.</summary>
    public void RecordWorldVisitor(string worldId, string accountId, string playerName)
    {
        playerName = (playerName ?? string.Empty).Trim();
        if (playerName.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            using var cmd = Cmd("""
                INSERT INTO world_visitor(world_id, account_id, player_name, first_seen_unix, last_seen_unix)
                VALUES($w, $a, $n, $now, $now)
                ON CONFLICT(world_id, account_id, player_name) DO UPDATE SET last_seen_unix = $now
                """);
            cmd.Parameters.AddWithValue("$w", worldId);
            cmd.Parameters.AddWithValue("$a", accountId ?? string.Empty);
            cmd.Parameters.AddWithValue("$n", playerName);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Recent visitors of a world, newest first — the owner's ban pick list.</summary>
    public IReadOnlyList<WorldVisitorRecord> ListWorldVisitors(string worldId, int limit = 30)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
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

    /// <summary>Every in-game name an account has played under, across all worlds — a fleet ban has to
    /// kick the PERSON, and the instances only know names.</summary>
    public IReadOnlyList<(string WorldId, string PlayerName)> ListVisitorNamesForAccount(string accountId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT world_id, player_name FROM world_visitor WHERE account_id = $a");
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

    /// <summary>Every world with its owner's account name — the admin UI's fleet overview (active first,
    /// then by recent activity). The fleet is small by design (per-account quota), so no paging.</summary>
    public IReadOnlyList<(WorldRecord World, string OwnerName)> ListAllWorldsAdmin(int limit = 500)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
                SELECT w.id, w.owner_account_id, w.display_name, w.join_secret, w.host_port, w.status,
                       w.container_id, w.created_unix, w.last_started_unix, w.channel, a.name
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
                    reader.GetInt64(7), reader.GetInt64(8), Channel: channel), ownerName));
            }

            return list;
        }
    }

    // ---------------- Player reports ----------------

    /// <summary>Files a player report ("Spieler melden") or a game-feedback entry ("Feedback &amp; Ideen"):
    /// who (in-game name) misbehaved on which world, a category and an optional free-text message. The
    /// <c>feedback</c> category carries ideas/suggestions instead of a complaint, so it is the only one
    /// that allows an empty reported name (and requires a message instead). Length-capped server-side;
    /// review is manual via the operator admin endpoints (an open report never auto-punishes anyone).</summary>
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
        worldId = IsValidWorldId(worldId) ? worldId : string.Empty;

        lock (_gate)
        {
            using var cmd = Cmd("""
                INSERT INTO report(world_id, reporter_account_id, reported_name, category, message, created_unix)
                VALUES($w, $r, $n, $c, $m, $now)
                """);
            cmd.Parameters.AddWithValue("$w", worldId ?? string.Empty);
            cmd.Parameters.AddWithValue("$r", reporterAccountId);
            cmd.Parameters.AddWithValue("$n", reportedName);
            cmd.Parameters.AddWithValue("$c", category);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.ExecuteNonQuery();
            return (true, string.Empty);
        }
    }

    /// <summary>Account self-deletion (DSGVO Art. 17): removes the account row, its sessions and the
    /// reports it filed. The caller deletes the account's WORLDS (registry rows + on-disk saves) first —
    /// they need the orchestrator to stop live instances.</summary>
    public void DeleteAccount(string accountId)
    {
        lock (_gate)
        {
            foreach (var sql in new[]
            {
                "DELETE FROM session WHERE account_id = $i",
                "DELETE FROM report WHERE reporter_account_id = $i",
                "DELETE FROM account_notice WHERE account_id = $i",
                "DELETE FROM world_visitor WHERE account_id = $i",
                "DELETE FROM world_ban WHERE account_id = $i",
                "DELETE FROM account WHERE id = $i",
            })
            {
                using var cmd = Cmd(sql);
                cmd.Parameters.AddWithValue("$i", accountId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public IReadOnlyList<ReportRecord> ListOpenReports()
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
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

    /// <summary>Closes a report after operator review (status "reviewed" or "dismissed").</summary>
    public void CloseReport(long reportId, string status)
    {
        lock (_gate)
        {
            using var cmd = Cmd("UPDATE report SET status = $s WHERE id = $i");
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$i", reportId);
            cmd.ExecuteNonQuery();
        }
    }

    private string CreateSessionLocked(string accountId)
    {
        string token = RandomHex(32);
        using var cmd = Cmd("INSERT INTO session(token_hash, account_id, expires_unix) VALUES($t, $a, $e)");
        cmd.Parameters.AddWithValue("$t", Sha256Hex(token));
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$e", NowUnix() + (long)_config.SessionDays * 86400);
        cmd.ExecuteNonQuery();
        return token;
    }

    // ---------------- Worlds ----------------

    /// <summary>Validates a creator-set world join password (#250). Empty/null is fine — it means "open
    /// world". 4 chars is deliberate: this protects a family world from strangers, it is not an account
    /// credential (PBKDF2-hashed at rest all the same).</summary>
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

    /// <summary>Creates a world for an account: enforces the per-account quota, allocates the world id,
    /// per-world join secret and a stable host port from the configured range. An optional join password
    /// (#250) is stored PBKDF2-hashed; empty = open world.</summary>
    public (bool Ok, string Error, WorldRecord? World) CreateWorld(string ownerAccountId, string displayName, string? password = null)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 40 || displayName.Any(char.IsControl))
        {
            return (false, "World name must be 1-40 printable characters.", null);
        }

        if (IsBlockedName(displayName))
        {
            return (false, "Please choose a different world name.", null);
        }

        if (ValidateWorldPassword(password) is (false, var passwordError))
        {
            return (false, passwordError, null);
        }

        lock (_gate)
        {
            using (var count = Cmd("SELECT COUNT(*) FROM world WHERE owner_account_id = $o"))
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
                Id: RandomHex(6), // 6 random bytes = the 12 hex chars WorldIdRx/subdomains are built on
                OwnerAccountId: ownerAccountId,
                DisplayName: displayName,
                JoinSecret: RandomHex(32),
                HostPort: port.Value,
                Status: WorldStatus.Stopped,
                ContainerId: string.Empty,
                CreatedUnix: NowUnix(),
                LastStartedUnix: 0,
                PasswordHash: string.IsNullOrEmpty(password) ? string.Empty : PasswordHasher.Hash(password));

            using var ins = Cmd("""
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

    /// <summary>Creates one world of the glitch.fun arcade pool (channel 'glitch'). Unlike
    /// <see cref="CreateWorld"/> there is no owning account (a fixed synthetic owner id), no per-account
    /// quota and no join password — arcade worlds are joinable exclusively through the glitch session
    /// gateway's HMAC tokens and never surface in any portal listing. The display name is
    /// operator/gateway-authored, so only basic validation applies.</summary>
    public (bool Ok, string Error, WorldRecord? World) CreateGlitchWorld(string displayName)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 40 || displayName.Any(char.IsControl))
        {
            return (false, "World name must be 1-40 printable characters.", null);
        }

        lock (_gate)
        {
            int? port = NextFreePortLocked();
            if (port is null)
            {
                return (false, "No capacity available right now — please try again later.", null);
            }

            var world = new WorldRecord(
                Id: RandomHex(6),
                OwnerAccountId: WorldChannel.Glitch,
                DisplayName: displayName,
                JoinSecret: RandomHex(32),
                HostPort: port.Value,
                Status: WorldStatus.Stopped,
                ContainerId: string.Empty,
                CreatedUnix: NowUnix(),
                LastStartedUnix: 0,
                Channel: WorldChannel.Glitch);

            using var ins = Cmd("""
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

    /// <summary>All worlds of one channel, oldest first — the glitch gateway's stable pool listing.</summary>
    public IReadOnlyList<WorldRecord> ListWorldsByChannel(string channel)
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE channel = $ch ORDER BY created_unix");
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

    // ---------------- glitch.fun guests & install bans ----------------

    /// <summary>Records a session grant for a glitch.fun install id (upsert): the guest list the admin
    /// UI bans from. Stores only Glitch's pseudonymous install id + the assigned player name.</summary>
    public void TouchGlitchGuest(string installId, string playerName)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
                INSERT INTO glitch_guest(install_id, player_name, first_seen_unix, last_seen_unix, sessions)
                VALUES($i, $n, $now, $now, 1)
                ON CONFLICT(install_id) DO UPDATE SET
                    player_name = $n, last_seen_unix = $now, sessions = sessions + 1
                """);
            cmd.Parameters.AddWithValue("$i", installId);
            cmd.Parameters.AddWithValue("$n", playerName ?? string.Empty);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Most recently seen glitch.fun guests (ban targets for the admin UI).</summary>
    public IReadOnlyList<GlitchGuestRecord> ListGlitchGuests(int limit = 50)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
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

    /// <summary>Bans (or unbans) a glitch.fun install id. A banned install gets no more session grants
    /// and its heartbeat relay answers 403, which the client treats as "stop the game" — the arcade
    /// twin of the account ban (arcade guests have no account).</summary>
    public void SetGlitchBanned(string installId, bool banned, string reason, string playerName = "")
    {
        installId = (installId ?? string.Empty).Trim();
        if (installId.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!banned)
            {
                using var del = Cmd("DELETE FROM glitch_ban WHERE install_id = $i");
                del.Parameters.AddWithValue("$i", installId);
                del.ExecuteNonQuery();
                return;
            }

            using var cmd = Cmd("""
                INSERT INTO glitch_ban(install_id, player_name, reason, created_unix)
                VALUES($i, $n, $r, $now)
                ON CONFLICT(install_id) DO UPDATE SET player_name = $n, reason = $r
                """);
            cmd.Parameters.AddWithValue("$i", installId);
            cmd.Parameters.AddWithValue("$n", playerName ?? string.Empty);
            cmd.Parameters.AddWithValue("$r", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>The ban entry for a glitch.fun install id, or null when not banned.</summary>
    public GlitchBanRecord? GetGlitchBan(string installId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT install_id, player_name, reason, created_unix FROM glitch_ban WHERE install_id = $i");
            cmd.Parameters.AddWithValue("$i", (installId ?? string.Empty).Trim());
            using var reader = cmd.ExecuteReader();
            return reader.Read()
                ? new GlitchBanRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3))
                : null;
        }
    }

    /// <summary>All currently banned glitch.fun installs, for the admin UI.</summary>
    public IReadOnlyList<GlitchBanRecord> ListGlitchBans()
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT install_id, player_name, reason, created_unix FROM glitch_ban ORDER BY created_unix DESC");
            using var reader = cmd.ExecuteReader();
            var list = new List<GlitchBanRecord>();
            while (reader.Read())
            {
                list.Add(new GlitchBanRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
            }

            return list;
        }
    }

    /// <summary>Sets, changes or removes (empty password) a world's join password (#250). The caller has
    /// already verified ownership; validation happens here so every write path shares it.</summary>
    public (bool Ok, string Error) SetWorldPassword(string worldId, string? password)
    {
        if (ValidateWorldPassword(password) is (false, var error))
        {
            return (false, error);
        }

        lock (_gate)
        {
            bool removing = string.IsNullOrEmpty(password);
            // A public world must always have a password (safety rule): removing the password also
            // un-lists it, so it can never end up publicly joinable by anyone with no gate at all.
            using var cmd = Cmd(removing
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

    /// <summary>Lists or un-lists a world in the public browser (#public-browser). The caller has already
    /// verified ownership. Listing requires a join password to be set — public worlds are always
    /// password-gated so strangers still need the owner-shared password to actually join.</summary>
    public (bool Ok, string Error) SetWorldVisibility(string worldId, bool isPublic)
    {
        lock (_gate)
        {
            if (isPublic)
            {
                using var check = Cmd("SELECT password_hash FROM world WHERE id = $i");
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

            using var cmd = Cmd("UPDATE world SET is_public = $p WHERE id = $i");
            cmd.Parameters.AddWithValue("$p", isPublic ? 1 : 0);
            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            return cmd.ExecuteNonQuery() == 1 ? (true, string.Empty) : (false, "World not found.");
        }
    }

    /// <summary>Every world the owner opted into the public browser — running ones first, then by name.
    /// All are password-gated by construction (see <see cref="SetWorldVisibility"/>).</summary>
    public IReadOnlyList<WorldRecord> ListPublicWorlds()
    {
        lock (_gate)
        {
            // running (2) → starting (1) → everything else (0), then alphabetical. Portal channel only:
            // glitch arcade worlds must never surface in the portal's public browser.
            using var cmd = Cmd(SelectWorld + @" WHERE is_public = 1 AND channel = ''
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
        lock (_gate)
        {
            // Portal channel only (defense in depth: the glitch pool's synthetic owner id is not an
            // account, but no account listing should ever surface an arcade world either way).
            using var cmd = Cmd(SelectWorld + " WHERE owner_account_id = $o AND channel = '' ORDER BY created_unix");
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
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadWorld(reader) : null;
        }
    }

    /// <summary>Worlds currently marked running/starting — the reaper reconciles these against Docker.
    /// Explicitly excludes archived worlds (they have no container by definition).</summary>
    public IReadOnlyList<WorldRecord> ListActiveWorlds()
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE status IN ($starting, $running)");
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

    /// <summary>Resolves a routing subdomain ("w-&lt;id&gt;") to its world — Caddy's on-demand-TLS "ask"
    /// endpoint uses this to only ever issue certificates for subdomains that really exist.</summary>
    public WorldRecord? FindBySubdomain(string subdomain)
    {
        if (subdomain is null || !subdomain.StartsWith("w-", StringComparison.Ordinal))
        {
            return null;
        }

        string id = subdomain.Substring(2);
        return IsValidWorldId(id) ? GetWorld(id) : null;
    }

    public void SetWorldStatus(string worldId, string status, string containerId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
                UPDATE world SET status = $st, container_id = $c,
                    last_started_unix = CASE WHEN $st = 'starting' THEN $now ELSE last_started_unix END
                WHERE id = $i
                """);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$c", containerId);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Stamps a world as active now — set on every successful join/wake so the archive sweep
    /// measures real inactivity, not time since creation.</summary>
    public void TouchWorldActive(string worldId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("UPDATE world SET last_active_unix = $now WHERE id = $i");
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Stopped worlds whose last activity (or creation, if never joined) predates the cutoff —
    /// the archive sweep's work list.</summary>
    public IReadOnlyList<WorldRecord> ListArchiveCandidates(long cutoffUnix)
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE status = $st AND MAX(last_active_unix, created_unix) < $cutoff");
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

    /// <summary>Gauge snapshot for /metrics.</summary>
    public RegistryCounts CountForMetrics()
    {
        lock (_gate)
        {
            long accounts;
            using (var cmd = Cmd("SELECT COUNT(*) FROM account"))
            {
                accounts = Convert.ToInt64(cmd.ExecuteScalar());
            }

            long openReports;
            using (var cmd = Cmd("SELECT COUNT(*) FROM report WHERE status = 'open'"))
            {
                openReports = Convert.ToInt64(cmd.ExecuteScalar());
            }

            var byStatus = new List<(string, long)>();
            using (var cmd = Cmd("SELECT status, COUNT(*) FROM world GROUP BY status"))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    byStatus.Add((reader.GetString(0), reader.GetInt64(1)));
                }
            }

            return new RegistryCounts(accounts, openReports, byStatus);
        }
    }

    public void DeleteWorld(string worldId)
    {
        lock (_gate)
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
                using var cmd = Cmd(sql);
                cmd.Parameters.AddWithValue("$i", worldId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    // ---------------- Internals ----------------

    private const string SelectWorld =
        "SELECT id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix, password_hash, is_public, channel FROM world";

    private static WorldRecord ReadWorld(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
        r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8), r.GetString(9), r.GetInt64(10) != 0,
        r.GetString(11));

    /// <summary>Smallest unused port in the configured range. Ports stay allocated for a world's lifetime
    /// (they are its stable native-UDP endpoint), so a deleted world's port returns to the pool.</summary>
    private int? NextFreePortLocked()
    {
        var used = new HashSet<int>();
        using (var cmd = Cmd("SELECT host_port FROM world"))
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

    private SqliteCommand Cmd(string sql)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time string equality — used for the reserved-name claim code so a wrong code
    /// can't be probed character by character through timing.</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));

    public void Dispose() => _db.Dispose();
}

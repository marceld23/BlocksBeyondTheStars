// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Accounts, bearer sessions and rescue codes behind <see cref="HostRegistry"/> (tables <c>account</c>,
/// <c>session</c>, <c>recovery_code</c>). Ban and unban notices go through <see cref="NoticeStore.AddNoticeLocked"/>
/// inside the ban update, and <see cref="DeleteAccount"/> erases the account's rows in the other tables as one locked
/// sequence — both rely on the one <see cref="RegistryDb"/> lock every store shares.
/// </summary>
internal sealed class AccountStore
{
    // Account names double as visible player identity; same cap as in-game names (24) and a conservative
    // character set so they are safe in URLs, logs and docker args without escaping anywhere.
    private static readonly Regex AccountNameRx = new("^[A-Za-z0-9_-]{3,24}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly RegistryDb _db;
    private readonly WorldHostConfig _config;
    private readonly RegistryNameRules _names;
    private readonly NoticeStore _notices;

    public AccountStore(RegistryDb db, WorldHostConfig config, RegistryNameRules names, NoticeStore notices)
    {
        _db = db;
        _config = config;
        _names = names;
        _notices = notices;
    }

    /// <inheritdoc cref="HostRegistry.CreateAccount"/>
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

        if (_names.IsBlockedName(name))
        {
            return (false, "Please choose a different name.", string.Empty, string.Empty);
        }

        bool isDeveloper = false;
        if (_names.IsReservedName(name))
        {
            // With no claim code configured, reserved names are simply unclaimable — the safe default.
            if (string.IsNullOrEmpty(_config.ReservedClaimCode) || !RegistryDb.FixedTimeEquals(claimCode ?? string.Empty, _config.ReservedClaimCode))
            {
                return (false, "This name is reserved.", string.Empty, string.Empty);
            }

            isDeveloper = true;
        }

        lock (_db.Gate)
        {
            using (var check = _db.Cmd("SELECT 1 FROM account WHERE name = $n"))
            {
                check.Parameters.AddWithValue("$n", name);
                if (check.ExecuteScalar() != null)
                {
                    return (false, "This name is already taken.", string.Empty, string.Empty);
                }
            }

            string id = "acc-" + RegistryDb.RandomHex(12);
            using (var ins = _db.Cmd("""
                INSERT INTO account(id, name, password_hash, is_developer, terms_version, terms_accepted_unix, created_unix)
                VALUES($i, $n, $p, $d, $tv, $ta, $c)
                """))
            {
                ins.Parameters.AddWithValue("$i", id);
                ins.Parameters.AddWithValue("$n", name);
                ins.Parameters.AddWithValue("$p", PasswordHasher.Hash(password!));
                ins.Parameters.AddWithValue("$d", isDeveloper ? 1 : 0);
                ins.Parameters.AddWithValue("$tv", acceptedTermsVersion);
                ins.Parameters.AddWithValue("$ta", RegistryDb.NowUnix());
                ins.Parameters.AddWithValue("$c", RegistryDb.NowUnix());
                ins.ExecuteNonQuery();
            }

            return (true, string.Empty, id, CreateSessionLocked(id));
        }
    }

    /// <inheritdoc cref="HostRegistry.Login"/>
    public (string AccountId, string SessionToken)? Login(string name, string password)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("SELECT id, password_hash FROM account WHERE name = $n");
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

    /// <inheritdoc cref="HostRegistry.ChangePassword"/>
    public (bool Ok, string Error) ChangePassword(string accountId, string oldPassword, string newPassword, string? keepSessionToken)
    {
        if ((newPassword ?? string.Empty).Length < 8)
        {
            return (false, "Password must be at least 8 characters.");
        }

        lock (_db.Gate)
        {
            using (var cmd = _db.Cmd("SELECT password_hash FROM account WHERE id = $i"))
            {
                cmd.Parameters.AddWithValue("$i", accountId);
                if (cmd.ExecuteScalar() is not string stored || !PasswordHasher.Verify(oldPassword ?? string.Empty, stored))
                {
                    return (false, "Wrong password.");
                }
            }

            using (var upd = _db.Cmd("UPDATE account SET password_hash = $p, must_change_password = 0 WHERE id = $i"))
            {
                upd.Parameters.AddWithValue("$p", PasswordHasher.Hash(newPassword!));
                upd.Parameters.AddWithValue("$i", accountId);
                upd.ExecuteNonQuery();
            }

            using (var del = _db.Cmd("DELETE FROM session WHERE account_id = $i AND token_hash != $keep"))
            {
                del.Parameters.AddWithValue("$i", accountId);
                del.Parameters.AddWithValue("$keep", string.IsNullOrEmpty(keepSessionToken) ? string.Empty : RegistryDb.Sha256Hex(keepSessionToken));
                del.ExecuteNonQuery();
            }

            return (true, string.Empty);
        }
    }

    /// <inheritdoc cref="HostRegistry.VerifyPassword"/>
    public bool VerifyPassword(string accountId, string? password)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("SELECT password_hash FROM account WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", accountId);
            return cmd.ExecuteScalar() is string stored && PasswordHasher.Verify(password ?? string.Empty, stored);
        }
    }

    /// <inheritdoc cref="HostRegistry.RecoveryCodeCount"/>
    public const int RecoveryCodeCount = 3;

    // Rescue codes and admin temp passwords are read out loud and typed by kids (often from paper), so the
    // alphabet skips lookalikes (0/O, 1/I/L) entirely. 8 symbols of 31 ≈ 40 bits — unguessable behind the
    // login rate limits, short enough to copy without despair.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private static string NewReadableCode()
    {
        Span<char> chars = stackalloc char[9];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = i == 4 ? '-' : CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Uppercases and strips separators/whitespace so "abcd-efgh", "ABCD EFGH" and "abcdefgh"
    /// all redeem the same code — paper transcription must not fail on formatting.</summary>
    private static string NormalizeCode(string? code)
        => new((code ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <inheritdoc cref="HostRegistry.CreateRecoveryCodes"/>
    public IReadOnlyList<string> CreateRecoveryCodes(string accountId)
    {
        var codes = new List<string>(RecoveryCodeCount);
        for (int i = 0; i < RecoveryCodeCount; i++)
        {
            codes.Add(NewReadableCode());
        }

        lock (_db.Gate)
        {
            using (var del = _db.Cmd("DELETE FROM recovery_code WHERE account_id = $a"))
            {
                del.Parameters.AddWithValue("$a", accountId);
                del.ExecuteNonQuery();
            }

            foreach (string code in codes)
            {
                using var ins = _db.Cmd("INSERT INTO recovery_code(account_id, code_hash, created_unix) VALUES($a, $h, $c)");
                ins.Parameters.AddWithValue("$a", accountId);
                ins.Parameters.AddWithValue("$h", PasswordHasher.Hash(NormalizeCode(code)));
                ins.Parameters.AddWithValue("$c", RegistryDb.NowUnix());
                ins.ExecuteNonQuery();
            }
        }

        return codes;
    }

    /// <inheritdoc cref="HostRegistry.RedeemRecoveryCode"/>
    public (bool Ok, string Error, string AccountId, string AccountName, string SessionToken) RedeemRecoveryCode(
        string name, string code, string newPassword)
    {
        const string Failed = "Wrong account name or rescue code.";
        if ((newPassword ?? string.Empty).Length < 8)
        {
            return (false, "Password must be at least 8 characters.", string.Empty, string.Empty, string.Empty);
        }

        string normalized = NormalizeCode(code);
        if (normalized.Length == 0)
        {
            return (false, Failed, string.Empty, string.Empty, string.Empty);
        }

        lock (_db.Gate)
        {
            string? accountId = null, accountName = null;
            using (var acc = _db.Cmd("SELECT id, name FROM account WHERE name = $n"))
            {
                acc.Parameters.AddWithValue("$n", name ?? string.Empty);
                using var reader = acc.ExecuteReader();
                if (reader.Read())
                {
                    accountId = reader.GetString(0);
                    accountName = reader.GetString(1);
                }
            }

            if (accountId is null)
            {
                return (false, Failed, string.Empty, string.Empty, string.Empty);
            }

            long matchedRow = 0;
            using (var sel = _db.Cmd("SELECT id, code_hash FROM recovery_code WHERE account_id = $a AND used_unix = 0"))
            {
                sel.Parameters.AddWithValue("$a", accountId);
                using var reader = sel.ExecuteReader();
                while (reader.Read())
                {
                    if (PasswordHasher.Verify(normalized, reader.GetString(1)))
                    {
                        matchedRow = reader.GetInt64(0);
                        break;
                    }
                }
            }

            if (matchedRow == 0)
            {
                return (false, Failed, string.Empty, string.Empty, string.Empty);
            }

            using (var burn = _db.Cmd("UPDATE recovery_code SET used_unix = $now WHERE id = $id"))
            {
                burn.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
                burn.Parameters.AddWithValue("$id", matchedRow);
                burn.ExecuteNonQuery();
            }

            using (var upd = _db.Cmd("UPDATE account SET password_hash = $p, must_change_password = 0 WHERE id = $i"))
            {
                upd.Parameters.AddWithValue("$p", PasswordHasher.Hash(newPassword!));
                upd.Parameters.AddWithValue("$i", accountId);
                upd.ExecuteNonQuery();
            }

            using (var del = _db.Cmd("DELETE FROM session WHERE account_id = $i"))
            {
                del.Parameters.AddWithValue("$i", accountId);
                del.ExecuteNonQuery();
            }

            return (true, string.Empty, accountId, accountName!, CreateSessionLocked(accountId));
        }
    }

    /// <inheritdoc cref="HostRegistry.AdminResetPassword"/>
    public (bool Ok, string TempPassword) AdminResetPassword(string accountId)
    {
        string temp = NewReadableCode();
        lock (_db.Gate)
        {
            using (var check = _db.Cmd("SELECT is_developer FROM account WHERE id = $i"))
            {
                check.Parameters.AddWithValue("$i", accountId);
                object? dev = check.ExecuteScalar();
                if (dev is null || Convert.ToInt64(dev, System.Globalization.CultureInfo.InvariantCulture) != 0)
                {
                    return (false, string.Empty);
                }
            }

            using (var upd = _db.Cmd("UPDATE account SET password_hash = $p, must_change_password = 1 WHERE id = $i"))
            {
                upd.Parameters.AddWithValue("$p", PasswordHasher.Hash(temp));
                upd.Parameters.AddWithValue("$i", accountId);
                upd.ExecuteNonQuery();
            }

            using (var del = _db.Cmd("DELETE FROM session WHERE account_id = $i"))
            {
                del.Parameters.AddWithValue("$i", accountId);
                del.ExecuteNonQuery();
            }

            return (true, temp);
        }
    }

    /// <summary>Column list every account read shares — see <see cref="ReadAccount"/> for the order.</summary>
    private const string AccountColumns =
        "id, name, is_developer, banned, ban_reason, terms_version, banned_at_unix, banned_until_unix, ban_reason_code, must_change_password";

    /// <summary>Materializes an account row. A timeout whose end has passed reads as NOT banned: the row
    /// keeps the history for the admin list, but every gate asking <c>IsBanned</c> lets the player back in
    /// without an operator having to lift anything by hand.</summary>
    private static AccountRecord ReadAccount(SqliteDataReader reader)
    {
        long until = reader.GetInt64(7);
        bool banned = reader.GetInt32(3) != 0 && (until <= 0 || until > RegistryDb.NowUnix());
        return new AccountRecord(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0,
            banned, reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6), until, reader.GetString(8),
            reader.GetInt32(9) != 0);
    }

    /// <inheritdoc cref="HostRegistry.ResolveSession"/>
    public AccountRecord? ResolveSession(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_db.Gate)
        {
            // Unqualified column names are unambiguous here: `session` carries none of them.
            using var cmd = _db.Cmd($"""
                SELECT {AccountColumns}
                FROM session s JOIN account a ON a.id = s.account_id
                WHERE s.token_hash = $t AND s.expires_unix >= $now
                """);
            cmd.Parameters.AddWithValue("$t", RegistryDb.Sha256Hex(token));
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.AcceptTerms"/>
    public void AcceptTerms(string accountId, int version)
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("UPDATE account SET terms_version = $v, terms_accepted_unix = $now WHERE id = $i");
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$now", RegistryDb.NowUnix());
            cmd.Parameters.AddWithValue("$i", accountId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <inheritdoc cref="HostRegistry.SetBanned"/>
    public bool SetBanned(string accountId, bool banned, string reason, string reasonCode = "", int days = 0)
        => SetBannedUntil(accountId, banned, reason, reasonCode, banned && days > 0 ? RegistryDb.NowUnix() + ((long)days * 86400) : 0);

    /// <inheritdoc cref="HostRegistry.SetBannedUntil"/>
    public bool SetBannedUntil(string accountId, bool banned, string reason, string reasonCode, long untilUnix)
    {
        // Operator accounts are never bannable. The developer flag is only obtainable with the operator's
        // secret claim code, and the fleet ban is the operator's OWN lever — a banned operator would be
        // locked out of the fleet they run, with nobody left who could lift it. Unbanning stays allowed.
        if (banned && GetAccount(accountId) is { IsDeveloper: true })
        {
            return false;
        }

        long now = RegistryDb.NowUnix();
        long until = banned ? untilUnix : 0;
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd("""
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

            _notices.AddNoticeLocked(accountId, banned ? NoticeRecord.KindBanned : NoticeRecord.KindUnbanned,
                subject: string.Empty, reason ?? string.Empty, reasonCode ?? string.Empty, until, now);
            return true;
        }
    }

    /// <inheritdoc cref="HostRegistry.FindAccountByName"/>
    public AccountRecord? FindAccountByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Cmd($"SELECT {AccountColumns} FROM account WHERE lower(name) = lower($n)");
            cmd.Parameters.AddWithValue("$n", name.Trim());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.ListBannedAccounts"/>
    public IReadOnlyList<AccountRecord> ListBannedAccounts()
    {
        lock (_db.Gate)
        {
            using var cmd = _db.Cmd($"SELECT {AccountColumns} FROM account WHERE banned = 1 ORDER BY name");
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

    /// <inheritdoc cref="HostRegistry.GetAccount"/>
    public AccountRecord? GetAccount(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return null;
        }

        lock (_db.Gate)
        {
            using var cmd = _db.Cmd($"SELECT {AccountColumns} FROM account WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", accountId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }
    }

    /// <inheritdoc cref="HostRegistry.DeleteAccount"/>
    public void DeleteAccount(string accountId)
    {
        lock (_db.Gate)
        {
            foreach (var sql in new[]
            {
                "DELETE FROM session WHERE account_id = $i",
                "DELETE FROM report WHERE reporter_account_id = $i",
                "DELETE FROM account_notice WHERE account_id = $i",
                "DELETE FROM world_visitor WHERE account_id = $i",
                "DELETE FROM world_ban WHERE account_id = $i",
                "DELETE FROM recovery_code WHERE account_id = $i",
                "DELETE FROM account WHERE id = $i",
            })
            {
                using var cmd = _db.Cmd(sql);
                cmd.Parameters.AddWithValue("$i", accountId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private string CreateSessionLocked(string accountId)
    {
        string token = RegistryDb.RandomHex(32);
        using var cmd = _db.Cmd("INSERT INTO session(token_hash, account_id, expires_unix) VALUES($t, $a, $e)");
        cmd.Parameters.AddWithValue("$t", RegistryDb.Sha256Hex(token));
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$e", RegistryDb.NowUnix() + (long)_config.SessionDays * 86400);
        cmd.ExecuteNonQuery();
        return token;
    }
}

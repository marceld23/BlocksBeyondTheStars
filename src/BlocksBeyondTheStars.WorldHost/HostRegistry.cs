// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The control plane's registry — accounts, bearer sessions and worlds in one SQLite file. Deliberately
/// privacy-minimal for the kid-facing free tier: an account is a display name + password hash, no email,
/// no personal data (the plan's account MVP). Every mutation is serialized on one connection behind a
/// lock, mirroring the game's SqliteWorldRepository pattern; the write volume here is tiny. That connection and
/// lock are one <see cref="RegistryDb"/> shared by per-aggregate stores — accounts/sessions, notices, worlds, world
/// moderation, reports, glitch guests; this class is the public surface and keeps every signature (records in
/// HostRegistryRecords.cs).
/// </summary>
public sealed class HostRegistry : IDisposable
{
    private static readonly Regex WorldIdRx = new("^[a-f0-9]{12}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly RegistryNameRules _names;
    private readonly RegistryDb _db;
    private readonly NoticeStore _notices;
    private readonly AccountStore _accounts;
    private readonly WorldStore _worlds;
    private readonly WorldModerationStore _worldModeration;
    private readonly PlayerReportStore _reports;
    private readonly GlitchStore _glitch;

    public HostRegistry(WorldHostConfig config, string? databasePath = null)
    {
        // Same order as before the split: the name screen is built before the database is opened.
        _names = new RegistryNameRules(config);
        string path = databasePath ?? Path.Combine(config.DataDir, "worldhost.db");
        _db = new RegistryDb(path);
        _notices = new NoticeStore(_db);
        _accounts = new AccountStore(_db, config, _names, _notices);
        _worlds = new WorldStore(_db, config, _names);
        _worldModeration = new WorldModerationStore(_db);
        _reports = new PlayerReportStore(_db);
        _glitch = new GlitchStore(_db);
    }

    // ---------------- Reserved names ----------------

    /// <summary>True when a name collides with a developer-reserved name. Both sides are normalized —
    /// lowercased with spaces/'-'/'_' stripped — so padding or separator tricks ("ju ju", "J_ustus")
    /// don't slip past the reservation.</summary>
    public bool IsReservedName(string? name) => _names.IsReservedName(name);

    /// <summary>True when a name contains a blocked word (kid-facing name hygiene). Screening lives in
    /// the shared <see cref="Shared.Moderation.NameScreen"/> (issue #938): stronger normalization than
    /// the reservation check — diacritics, every separator class and leetspeak digits are folded, so
    /// "H-i-t-l-e-r", "h.i.t.l.e.r" and "h1tl3r" are all caught.</summary>
    public bool IsBlockedName(string? name) => _names.IsBlockedName(name);

    /// <summary>Full screening verdict (block / watch / ok) for the operator-notification call sites —
    /// watch hits are allowed through but flagged (#938).</summary>
    public Shared.Moderation.NameScreenResult ScreenName(string? name) => _names.ScreenName(name);

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
        => _accounts.CreateAccount(name, password, claimCode, acceptedTermsVersion);

    /// <summary>Verifies credentials and returns a fresh session token, or null. One generic failure —
    /// it never reveals whether the name exists.</summary>
    public (string AccountId, string SessionToken)? Login(string name, string password) => _accounts.Login(name, password);

    /// <summary>Rotates an account's password after verifying the current one — the missing half of
    /// "write your password down": a KNOWN password can now be changed (a forgotten one still cannot,
    /// there is no recovery channel by design). Every other session is revoked so a stolen or shared
    /// sign-in dies with the old password; <paramref name="keepSessionToken"/> (the caller's own bearer)
    /// survives. The error string is safe to show to the player.</summary>
    public (bool Ok, string Error) ChangePassword(string accountId, string oldPassword, string newPassword, string? keepSessionToken)
        => _accounts.ChangePassword(accountId, oldPassword, newPassword, keepSessionToken);

    /// <summary>True when <paramref name="password"/> matches the account's stored hash — the gate for
    /// self-service actions that must not be reachable with a stolen session alone (recovery-code
    /// regeneration mirrors the password-change rule).</summary>
    public bool VerifyPassword(string accountId, string? password) => _accounts.VerifyPassword(accountId, password);

    /// <summary>How many rescue codes an account gets — enough that losing one slip of paper is not fatal,
    /// few enough to fit on one.</summary>
    public const int RecoveryCodeCount = AccountStore.RecoveryCodeCount;

    /// <summary>(Re)issues the account's rescue codes ("Rettungscodes"): the previous set is void, the
    /// fresh plaintexts are returned EXACTLY ONCE for the player to write down — only PBKDF2 hashes are
    /// stored, so a leaked registry cannot redeem them (they are password-equivalent secrets).</summary>
    public IReadOnlyList<string> CreateRecoveryCodes(string accountId) => _accounts.CreateRecoveryCodes(accountId);

    /// <summary>Self-service reset with a rescue code: consumes the code (single use), sets the new
    /// password and revokes every session — the flow a player uses when the password is GONE, so there is
    /// no session to keep. Uniform failure text: it never reveals whether the account exists, has codes
    /// left, or the code was merely mistyped. Returns a fresh session, so a successful rescue is a
    /// sign-in.</summary>
    public (bool Ok, string Error, string AccountId, string AccountName, string SessionToken) RedeemRecoveryCode(
        string name, string code, string newPassword)
        => _accounts.RedeemRecoveryCode(name, code, newPassword);

    /// <summary>Operator lever for "my kid forgot the password" support mails: sets a readable one-time
    /// password (returned exactly once, to the admin page), flags the account so the next login nags to
    /// change it, and revokes every session. Refused for developer accounts — an admin-token holder must
    /// not be able to take over the operator account itself.</summary>
    public (bool Ok, string TempPassword) AdminResetPassword(string accountId) => _accounts.AdminResetPassword(accountId);

    /// <summary>Resolves a bearer token to its account, or null when unknown/expired.</summary>
    public AccountRecord? ResolveSession(string? token) => _accounts.ResolveSession(token);

    /// <summary>Records that an account accepted the (current) community rules version — the re-acceptance
    /// path after the operator bumps <see cref="WorldHostConfig.TermsVersion"/>.</summary>
    public void AcceptTerms(string accountId, int version) => _accounts.AcceptTerms(accountId, version);

    /// <summary>Bans (or, with <paramref name="banned"/> false, unbans) an account. Banned accounts keep
    /// their session but every world action is refused with the reason. <paramref name="days"/> 0 means
    /// "until an operator lifts it"; anything greater is a timeout that ends by itself — the kid-facing
    /// default, and the reason the notice can promise a date. Both directions leave a notice behind, so
    /// the player learns what happened at their next login instead of finding a silently dead account.</summary>
    /// <returns>False when the ban was refused — an operator account can never be banned.</returns>
    public bool SetBanned(string accountId, bool banned, string reason, string reasonCode = "", int days = 0)
        => _accounts.SetBanned(accountId, banned, reason, reasonCode, days);

    /// <summary>The primitive behind <see cref="SetBanned"/>, with the end of the timeout as an absolute
    /// time (unix seconds; 0 = until an operator lifts it). The admin UI uses the day-count form.</summary>
    public bool SetBannedUntil(string accountId, bool banned, string reason, string reasonCode, long untilUnix)
        => _accounts.SetBannedUntil(accountId, banned, reason, reasonCode, untilUnix);

    /// <summary>Case-insensitive account lookup by name — the admin UI's bridge from a reported in-game
    /// name to the account behind it (when they match; players can of course play under other names).</summary>
    public AccountRecord? FindAccountByName(string? name) => _accounts.FindAccountByName(name);

    /// <summary>Accounts under an active ban, for the admin UI's ban list. Expired timeouts are filtered
    /// out here as well — they are no longer bans, they are history.</summary>
    public IReadOnlyList<AccountRecord> ListBannedAccounts() => _accounts.ListBannedAccounts();

    /// <summary>Looks an account up by id (the admin UI's ban forms carry ids, not names).</summary>
    public AccountRecord? GetAccount(string? accountId) => _accounts.GetAccount(accountId);

    // ---------------- Player notices (the "why can't I play any more?" inbox) ----------------

    /// <summary>Files a notice for a player. Called for bans/unbans (see <see cref="SetBanned"/>) and when
    /// an operator deletes someone's world — that one leaves no other trace, the world row is gone.</summary>
    public void AddNotice(string accountId, string kind, string subject, string reason, string reasonCode = "", long untilUnix = 0)
        => _notices.AddNotice(accountId, kind, subject, reason, reasonCode, untilUnix);

    /// <summary>The player's notices, newest first. Unseen only by default — that is what login and the
    /// portal poll show; the client acknowledges them once the player has read them.</summary>
    public IReadOnlyList<NoticeRecord> ListNotices(string accountId, bool unseenOnly = true, int limit = 20)
        => _notices.ListNotices(accountId, unseenOnly, limit);

    /// <summary>Marks one notice (or, with <paramref name="noticeId"/> &lt;= 0, all of them) as read.
    /// Always scoped to the caller's own account — a notice id is a guessable integer.</summary>
    public void MarkNoticesSeen(string accountId, long noticeId = 0) => _notices.MarkNoticesSeen(accountId, noticeId);

    // ---------------- Per-world bans & visitors (the world owner's own lever) ----------------

    /// <summary>Bars a player from ONE world. Idempotent per (world, account, name) so a double click
    /// cannot pile up rows. Returns false when the world/name pair is unusable.</summary>
    public bool AddWorldBan(string worldId, string accountId, string playerName, string reason)
        => _worldModeration.AddWorldBan(worldId, accountId, playerName, reason);

    /// <summary>Lifts a world ban. The world id is part of the WHERE so an id from another world cannot
    /// be deleted by guessing.</summary>
    public void RemoveWorldBan(string worldId, long banId) => _worldModeration.RemoveWorldBan(worldId, banId);

    public IReadOnlyList<WorldBanRecord> ListWorldBans(string worldId) => _worldModeration.ListWorldBans(worldId);

    /// <summary>The world ban that applies to this join, or null. Matches on the account — and on the
    /// in-game name too, because arcade guests have no account and because a name is what the owner
    /// actually recognises.</summary>
    public WorldBanRecord? FindWorldBan(string worldId, string accountId, string playerName)
        => _worldModeration.FindWorldBan(worldId, accountId, playerName);

    /// <summary>Records that an account entered a world under an in-game name (upsert, written at the
    /// join grant). This is the pick list the owner's ban UI offers, and the only place the fleet knows
    /// which in-game names belong to an account.</summary>
    public void RecordWorldVisitor(string worldId, string accountId, string playerName)
        => _worldModeration.RecordWorldVisitor(worldId, accountId, playerName);

    /// <summary>Recent visitors of a world, newest first — the owner's ban pick list.</summary>
    public IReadOnlyList<WorldVisitorRecord> ListWorldVisitors(string worldId, int limit = 30)
        => _worldModeration.ListWorldVisitors(worldId, limit);

    /// <summary>Every in-game name an account has played under, across all worlds — a fleet ban has to
    /// kick the PERSON, and the instances only know names.</summary>
    public IReadOnlyList<(string WorldId, string PlayerName)> ListVisitorNamesForAccount(string accountId)
        => _worldModeration.ListVisitorNamesForAccount(accountId);

    /// <summary>Every world with its owner's account name — the admin UI's fleet overview (active first,
    /// then by recent activity). The fleet is small by design (per-account quota), so no paging.</summary>
    public IReadOnlyList<(WorldRecord World, string OwnerName)> ListAllWorldsAdmin(int limit = 500)
        => _worlds.ListAllWorldsAdmin(limit);

    // ---------------- Player reports ----------------

    /// <summary>Files a player report ("Spieler melden") or a game-feedback entry ("Feedback &amp; Ideen"):
    /// who (in-game name) misbehaved on which world, a category and an optional free-text message. The
    /// <c>feedback</c> category carries ideas/suggestions instead of a complaint, so it is the only one
    /// that allows an empty reported name (and requires a message instead). Length-capped server-side;
    /// review is manual via the operator admin endpoints (an open report never auto-punishes anyone).</summary>
    public (bool Ok, string Error) CreateReport(string reporterAccountId, string worldId, string reportedName, string category, string message)
        => _reports.CreateReport(reporterAccountId, worldId, reportedName, category, message);

    /// <summary>Account self-deletion (DSGVO Art. 17): removes the account row, its sessions and the
    /// reports it filed. The caller deletes the account's WORLDS (registry rows + on-disk saves) first —
    /// they need the orchestrator to stop live instances.</summary>
    public void DeleteAccount(string accountId) => _accounts.DeleteAccount(accountId);

    public IReadOnlyList<ReportRecord> ListOpenReports() => _reports.ListOpenReports();

    /// <summary>Closes a report after operator review (status "reviewed" or "dismissed").</summary>
    public void CloseReport(long reportId, string status) => _reports.CloseReport(reportId, status);

    // ---------------- Worlds ----------------

    /// <summary>Validates a creator-set world join password (#250). Empty/null is fine — it means "open
    /// world". 4 chars is deliberate: this protects a family world from strangers, it is not an account
    /// credential (PBKDF2-hashed at rest all the same).</summary>
    public static (bool Ok, string Error) ValidateWorldPassword(string? password) => WorldStore.ValidateWorldPassword(password);

    /// <summary>Creates a world for an account: enforces the per-account quota, allocates the world id,
    /// per-world join secret and a stable host port from the configured range. An optional join password
    /// (#250) is stored PBKDF2-hashed; empty = open world.</summary>
    public (bool Ok, string Error, WorldRecord? World) CreateWorld(string ownerAccountId, string displayName, string? password = null)
        => _worlds.CreateWorld(ownerAccountId, displayName, password);

    /// <summary>Creates one world of the glitch.fun arcade pool (channel 'glitch'). Unlike
    /// <see cref="CreateWorld"/> there is no owning account (a fixed synthetic owner id), no per-account
    /// quota and no join password — arcade worlds are joinable exclusively through the glitch session
    /// gateway's HMAC tokens and never surface in any portal listing. The display name is
    /// operator/gateway-authored, so only basic validation applies.</summary>
    public (bool Ok, string Error, WorldRecord? World) CreateGlitchWorld(string displayName)
        => _worlds.CreateGlitchWorld(displayName);

    /// <summary>All worlds of one channel, oldest first — the glitch gateway's stable pool listing.</summary>
    public IReadOnlyList<WorldRecord> ListWorldsByChannel(string channel) => _worlds.ListWorldsByChannel(channel);

    // ---------------- glitch.fun guests & install bans ----------------

    /// <summary>Records a session grant for a glitch.fun install id (upsert): the guest list the admin
    /// UI bans from. Stores only Glitch's pseudonymous install id + the assigned player name.</summary>
    public void TouchGlitchGuest(string installId, string playerName) => _glitch.TouchGlitchGuest(installId, playerName);

    /// <summary>Most recently seen glitch.fun guests (ban targets for the admin UI).</summary>
    public IReadOnlyList<GlitchGuestRecord> ListGlitchGuests(int limit = 50) => _glitch.ListGlitchGuests(limit);

    /// <summary>Bans (or unbans) a glitch.fun install id. A banned install gets no more session grants
    /// and its heartbeat relay answers 403, which the client treats as "stop the game" — the arcade
    /// twin of the account ban (arcade guests have no account).</summary>
    public void SetGlitchBanned(string installId, bool banned, string reason, string playerName = "")
        => _glitch.SetGlitchBanned(installId, banned, reason, playerName);

    /// <summary>The ban entry for a glitch.fun install id, or null when not banned.</summary>
    public GlitchBanRecord? GetGlitchBan(string installId) => _glitch.GetGlitchBan(installId);

    /// <summary>All currently banned glitch.fun installs, for the admin UI.</summary>
    public IReadOnlyList<GlitchBanRecord> ListGlitchBans() => _glitch.ListGlitchBans();

    /// <summary>Sets, changes or removes (empty password) a world's join password (#250). The caller has
    /// already verified ownership; validation happens here so every write path shares it.</summary>
    public (bool Ok, string Error) SetWorldPassword(string worldId, string? password)
        => _worlds.SetWorldPassword(worldId, password);

    /// <summary>Lists or un-lists a world in the public browser (#public-browser). The caller has already
    /// verified ownership. Listing requires a join password to be set — public worlds are always
    /// password-gated so strangers still need the owner-shared password to actually join.</summary>
    public (bool Ok, string Error) SetWorldVisibility(string worldId, bool isPublic)
        => _worlds.SetWorldVisibility(worldId, isPublic);

    /// <summary>Every world the owner opted into the public browser — running ones first, then by name.
    /// All are password-gated by construction (see <see cref="SetWorldVisibility"/>).</summary>
    public IReadOnlyList<WorldRecord> ListPublicWorlds() => _worlds.ListPublicWorlds();

    public IReadOnlyList<WorldRecord> ListWorlds(string ownerAccountId) => _worlds.ListWorlds(ownerAccountId);

    public WorldRecord? GetWorld(string worldId) => _worlds.GetWorld(worldId);

    /// <summary>Worlds currently marked running/starting — the reaper reconciles these against Docker.
    /// Explicitly excludes archived worlds (they have no container by definition).</summary>
    public IReadOnlyList<WorldRecord> ListActiveWorlds() => _worlds.ListActiveWorlds();

    /// <summary>Resolves a routing subdomain ("w-&lt;id&gt;") to its world — Caddy's on-demand-TLS "ask"
    /// endpoint uses this to only ever issue certificates for subdomains that really exist.</summary>
    public WorldRecord? FindBySubdomain(string subdomain) => _worlds.FindBySubdomain(subdomain);

    public void SetWorldStatus(string worldId, string status, string containerId)
        => _worlds.SetWorldStatus(worldId, status, containerId);

    /// <summary>Records that <paramref name="serverImage"/> refused the world's save as written by a newer build
    /// (#2464 — the instance exited with code 3) at <paramref name="atUnix"/>. The orchestrator then answers
    /// "needs a newer server" instead of starting that image on the same save again.</summary>
    public void MarkSaveTooNew(string worldId, string serverImage, long atUnix)
        => _worlds.MarkSaveTooNew(worldId, serverImage, atUnix);

    /// <summary>Drops the "save too new" mark (#2464): the world came up, its save was replaced, or an operator
    /// asked for a fresh try.</summary>
    public void ClearSaveTooNew(string worldId) => _worlds.ClearSaveTooNew(worldId);

    /// <summary>Stamps a world as active now — set on every successful join/wake so the archive sweep
    /// measures real inactivity, not time since creation.</summary>
    public void TouchWorldActive(string worldId) => _worlds.TouchWorldActive(worldId);

    /// <summary>Stopped worlds whose last activity (or creation, if never joined) predates the cutoff —
    /// the archive sweep's work list.</summary>
    public IReadOnlyList<WorldRecord> ListArchiveCandidates(long cutoffUnix) => _worlds.ListArchiveCandidates(cutoffUnix);

    /// <summary>Gauge snapshot for /metrics.</summary>
    public RegistryCounts CountForMetrics()
    {
        lock (_db.Gate)
        {
            long accounts;
            using (var cmd = _db.Cmd("SELECT COUNT(*) FROM account"))
            {
                accounts = Convert.ToInt64(cmd.ExecuteScalar());
            }

            long openReports;
            using (var cmd = _db.Cmd("SELECT COUNT(*) FROM report WHERE status = 'open'"))
            {
                openReports = Convert.ToInt64(cmd.ExecuteScalar());
            }

            var byStatus = new List<(string, long)>();
            using (var cmd = _db.Cmd("SELECT status, COUNT(*) FROM world GROUP BY status"))
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

    public void DeleteWorld(string worldId) => _worlds.DeleteWorld(worldId);

    public void Dispose() => _db.Dispose();
}

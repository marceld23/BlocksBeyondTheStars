// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Security;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The gates and helpers the WorldHost endpoint groups share (#2902): caller resolution from the bearer session,
/// the error shape with its machine <c>code</c>, the admin token and the admin-UI Basic-Auth gate, and the portal's
/// page language. These were the local functions of the old single-file Program.cs; what they captured from it is
/// now a leading parameter.
/// </summary>
internal static class WorldHostApi
{
    internal static string CallerIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Every error response carries a stable machine `code` alongside the (English) `error` text, so the
    // game client and the portal can show properly localized messages (DE/EN) instead of raw API English.
    // The mapping lives here — the single place all player-safe error TEXTS of this assembly pass through.
    internal static string? CodeFor(string error) => error switch
    {
        _ when error.StartsWith("This account is banned", StringComparison.Ordinal) => "banned",
        _ when error.StartsWith("The owner of this world has blocked you", StringComparison.Ordinal) => "banned_from_world",
        "You cannot block yourself from your own world." => "self_block",
        "Operator accounts cannot be banned." => "operator_protected",
        _ when error.StartsWith("World limit reached", StringComparison.Ordinal) => "world_limit",
        _ when error.StartsWith("Save exceeds", StringComparison.Ordinal) => "upload_too_large",
        "Please accept the community rules to create an account." => "accept_rules",
        "Name must be 3-24 characters: letters, digits, '-' or '_'." => "name_invalid",
        "Password must be at least 8 characters." => "password_short",
        "This name is already taken." => "name_taken",
        "This name is reserved." or "This player name is reserved." => "name_reserved",
        "Please choose a different name." or "Please choose a different world name." or "Please choose a different player name." => "name_blocked",
        "World name must be 1-40 printable characters." => "world_name_invalid",
        "No capacity available right now — please try again later." => "no_capacity",
        "Player name must be 1-24 printable characters." => "player_name_invalid",
        "The community rules have changed — please accept them on the portal first."
            or "The community rules have changed — please accept them first." => "terms_outdated",
        "World not found." => "world_not_found",
        "The world could not be started — please try again in a moment." => "world_start_failed",
        "The world did not come up in time — please try again." => "world_wake_failed",
        WorldOrchestrator.SaveTooNewError => "world_needs_newer_server", // #2464: the server image refused the save (exit 3)
        SavePaths.SaveTooNewError => "save_too_new", // #2464: an upload from a newer game build
        "Stop the world before uploading a save." or "Stop the world before downloading its save." => "stop_first",
        "This world needs a password." => "password_required",
        "Wrong world password." => "wrong_password",
        "Wrong password." => "wrong_account_password", // own code: the world text above localizes as "Welt-Passwort"
        "Wrong account name or rescue code." => "recover_failed",
        "Too many password attempts — please wait a few minutes." => "too_many_attempts",
        "World password must be 4-24 printable characters." => "world_password_invalid",
        "Empty upload." => "upload_empty",
        "This file is not a Blocks Beyond the Stars save (world.db)."
            or "The save file is damaged (integrity check failed)."
            or "This database is not a Blocks Beyond the Stars world save."
            or "The save file could not be read." => "save_invalid",
        "This world has no save yet (it was never started)." => "save_missing",
        _ when error.StartsWith("This player is banned", StringComparison.Ordinal) => "banned",
        "The glitch.fun gateway is disabled." => "glitch_disabled",
        "This install could not be verified with glitch.fun." => "glitch_invalid_install",
        "All arcade worlds are full right now — please try again in a few minutes." => "glitch_full",
        _ => null,
    };

    internal static IResult ApiError(string error, int status = StatusCodes.Status400BadRequest)
        => Results.Json(new { error, code = CodeFor(error) }, statusCode: status);

    internal static IResult RateLimited(WorldHostMetrics metrics)
    {
        metrics.RateLimited();
        return Results.Json(new { error = "Too many requests — please wait a bit and try again.", code = "rate_limited" },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    // Resolves the caller's account from the Authorization: Bearer <session> header; null = not signed in.
    internal static AccountRecord? Caller(HostRegistry registry, HttpContext ctx)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? registry.ResolveSession(header.Substring(prefix.Length).Trim())
            : null;
    }

    // The caller's raw bearer token (empty when absent) — needed where the session itself is the subject,
    // e.g. a password change that revokes every OTHER session but must keep the one making the request.
    internal static string BearerToken(HttpContext ctx)
    {
        string header = ctx.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header.Substring(prefix.Length).Trim() : string.Empty;
    }

    // Strips CR/LF so a player-supplied string (name, reason) can never forge extra log lines. Account and
    // world names are charset-validated anyway; this covers free-text fields and satisfies defense in depth.
    internal static string LogSafe(string? value)
        => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

    // Uniform account-state gate for world actions: banned accounts and accounts that haven't accepted the
    // CURRENT rules version are refused (the join path re-checks inside the orchestrator as well — that is
    // the choke point native clients will use directly).
    internal static IResult? GuardAccount(WorldHostConfig config, AccountRecord account)
    {
        if (account.IsBanned)
        {
            return Results.Json(new
            {
                error = string.IsNullOrEmpty(account.BanReason) ? "This account is banned." : $"This account is banned: {account.BanReason}",
                code = "banned",
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (account.AcceptedTermsVersion < config.TermsVersion)
        {
            return Results.Json(new
            {
                error = "The community rules have changed — please accept them first.",
                code = "terms_outdated",
                termsOutdated = true,
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    // The player's unread notices, shaped for the wire (#496). Kind and reasonCode are stable machine strings
    // the client localizes; `reason` is operator/owner free text and travels as written, like ban reasons do.
    internal static object[] NoticesJson(HostRegistry registry, string accountId)
        => registry.ListNotices(accountId).Select(n => (object)new
        {
            id = n.Id,
            kind = n.Kind,
            subject = n.Subject,
            reason = n.Reason,
            reasonCode = n.ReasonCode,
            until = n.UntilUnix,
            created = n.CreatedUnix,
        }).ToArray();

    // An operator deleting someone's world has to be written down BEFORE the row disappears — afterwards
    // there is nothing left to derive a message from, and the owner would just find their world gone. Skipped
    // for the account-less arcade pool, which has no owner to tell.
    internal static void NotifyWorldDeleted(HostRegistry registry, WorldRecord world, string? reason)
    {
        if (world.Channel != WorldChannel.Portal || registry.GetAccount(world.OwnerAccountId) is null)
        {
            return;
        }

        registry.AddNotice(world.OwnerAccountId, NoticeRecord.KindWorldDeleted, world.DisplayName, (reason ?? string.Empty).Trim());
    }

    // True when a moderation target is the fleet operator — by the account (the developer flag is only
    // obtainable with the operator's secret claim code) or by one of the configured fleet-admin names (which
    // config load reserves fleet-wide). The operator is unbannable and unkickable on every path: oversight of
    // worlds where kids play must not be something a world owner — or a stolen owner session — can switch off.
    internal static bool IsOperatorTarget(WorldHostServices s, string? accountId, string? playerName)
        => s.Config.IsFleetAdminName(playerName)
           || (!string.IsNullOrEmpty(accountId) && s.Registry.GetAccount(accountId) is { IsDeveloper: true });

    internal static bool IsAdmin(WorldHostConfig config, HttpContext ctx)
        => BasicAuth.TokenEquals(ctx.Request.Headers["X-Admin-Token"].ToString(), config.AdminToken);

    // Browser admin UI gate (Basic Auth — browsers can't send X-Admin-Token). Returns the 401 challenge
    // to send, or null when authorized. Off until BBS_WH_ADMIN_USER + _PASSWORD are configured.
    internal static IResult? GuardAdminUi(WorldHostConfig config, HttpContext ctx)
    {
        if (BasicAuth.IsAuthorized(ctx.Request.Headers.Authorization.ToString(), config.AdminUser, config.AdminPassword))
        {
            return null;
        }

        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"BBS fleet admin\", charset=\"UTF-8\"";
        return Results.Text("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
    }

    // Extracts joinedPlayers from an instance's /status JSON (shared with the glitch gateway's world
    // pick); null when unreadable — callers show "?" (admin page) or count 0 (aggregates) rather than fail.
    internal static int? ParseJoinedPlayers(string statusJson) => GlitchGateway.ParseJoinedPlayers(statusJson);

    // Sum of players currently on running instances, probed in parallel. Callers are throttled (the
    // admin page and the CACHED public snapshot) — never wire this to an uncached public path.
    internal static async Task<int> CountPlayersOnlineAsync(HostRegistry registry, WorldOrchestrator orchestrator)
    {
        var running = registry.ListAllWorldsAdmin().Where(e => e.World.Status == WorldStatus.Running);
        var counts = await Task.WhenAll(running.Select(async e =>
            await orchestrator.ReadInstanceStatusAsync(e.World) is { } json ? ParseJoinedPlayers(json) ?? 0 : 0));
        return counts.Sum();
    }

    // Page language: explicit ?lang= wins (and is remembered in a cookie so plain links keep the choice);
    // otherwise the cookie; otherwise the browser's Accept-Language (first visit — auto-detection sets NO
    // cookie, only an explicit switch persists); otherwise German — the portal's primary audience. Every
    // language the game ships is honored (#970), not just de/en.
    internal static string PageLang(HttpContext ctx)
    {
        string? q = ctx.Request.Query["lang"];
        if (PortalLocales.IsSupported(q))
        {
            ctx.Response.Cookies.Append("bbs_lang", q!, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                // Server-side fallback only (the JS carries ?lang= itself), so it can be locked down fully.
                Secure = true,
                HttpOnly = true,
            });
            return q!;
        }

        string? cookie = ctx.Request.Cookies["bbs_lang"];
        return PortalLocales.IsSupported(cookie)
            ? cookie!
            : PortalLocales.LangFromAcceptHeader(ctx.Request.Headers.AcceptLanguage);
    }
}

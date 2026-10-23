// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The account routes under <c>/api</c> (#2902): community rules, signup and login, the player's notices, the
/// password change, rescue-code recovery and re-issue, and account self-deletion.
/// </summary>
internal static class AccountEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        // ---------------- Accounts ----------------

        var api = app.MapGroup("/api");

        api.MapGet("/terms", (HttpContext ctx) => Terms(s, ctx));

        api.MapPost("/signup", (HttpContext ctx, SignupRequest req) => Signup(s, log, ctx, req));

        api.MapPost("/login", (HttpContext ctx, SignupRequest req) => Login(s, ctx, req));

        api.MapGet("/notices", (HttpContext ctx) => Notices(s, ctx));

        api.MapPost("/notices/ack", (HttpContext ctx, NoticeAckRequest req) => AckNotices(s, ctx, req));

        api.MapPost("/accept-terms", (HttpContext ctx) => AcceptTerms(s, ctx));

        api.MapPost("/account/password", (HttpContext ctx, ChangePasswordRequest req) => ChangePassword(s, log, ctx, req));

        api.MapPost("/recover", (HttpContext ctx, RecoverRequest req) => Recover(s, log, ctx, req));

        api.MapPost("/account/recovery-codes", (HttpContext ctx, RecoveryCodesRequest req) => ReissueRecoveryCodes(s, log, ctx, req));

        api.MapDelete("/account", (HttpContext ctx) => DeleteAccount(s, log, ctx));
    }

    // Current community-rules version + plain text, anonymous: the desktop client shows the rules in-game
    // and needs the version number for signup (login only reports a boolean termsOutdated). `text` carries
    // the requested ?lang= (every game language, #970); textDe/textEn stay for clients that predate it.
    // Still cacheable: the language rides in the query string, which is part of the HTTP cache key.
    internal static IResult Terms(WorldHostServices s, HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "public, max-age=300";
        string lang = PortalLocales.Normalize(ctx.Request.Query["lang"]);
        return Results.Json(new
        {
            version = s.Config.TermsVersion,
            lang,
            text = CommunityRules.PlainText(lang),
            textDe = CommunityRules.PlainText("de"),
            textEn = CommunityRules.PlainText("en"),
        });
    }

    internal static IResult Signup(WorldHostServices s, ILogger log, HttpContext ctx, SignupRequest req)
    {
        if (!s.SignupLimit.TryPass(WorldHostApi.CallerIp(ctx)))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        // Name screening with operator visibility (#938): blocked attempts previously left no trace at all.
        // Watch hits are allowed (flagged below, after the account actually exists).
        var nameScreen = s.Registry.ScreenName(req.Name);
        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Block)
        {
            s.Metrics.NameBlocked();
            log.LogWarning("Signup name blocked (matched '{Term}').", nameScreen.MatchedTerm);
            s.Notifier.Post("Blocked name at signup", $"A signup was rejected: the requested account name matched blocked term '{nameScreen.MatchedTerm}'.", "no_entry");
            return WorldHostApi.ApiError("Please choose a different name.");
        }

        var (ok, error, accountId, session) = s.Registry.CreateAccount(req.Name, req.Password, req.ClaimCode, req.AcceptedTermsVersion);
        if (!ok)
        {
            return WorldHostApi.ApiError(error);
        }

        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Watch)
        {
            s.Metrics.NameFlagged();
            log.LogWarning("Account name '{Name}' flagged (matched watch term '{Term}'); signup allowed.", WorldHostApi.LogSafe(req.Name), nameScreen.MatchedTerm);
            s.Notifier.Post("Name flagged at signup", $"New account name '{req.Name}' matched watch-list term '{nameScreen.MatchedTerm}'. The signup was allowed — review manually.", "triangular_flag_on_post");
        }

        // Deliberately no account id in the log: ids act as stable references in the registry and appearing
        // in log files would let anyone with log access correlate them (CodeQL cs/cleartext-storage).
        log.LogInformation("Account created: {Name}.", WorldHostApi.LogSafe(req.Name));
        // Rescue codes ride the signup answer — the ONE moment the plaintexts exist; only hashes remain
        // server-side, so this is also the one moment the UI can tell the player to write them down.
        return Results.Json(new { accountId, sessionToken = session, recoveryCodes = s.Registry.CreateRecoveryCodes(accountId) });
    }

    internal static IResult Login(WorldHostServices s, HttpContext ctx, SignupRequest req)
    {
        if (!s.LoginLimit.TryPass(WorldHostApi.CallerIp(ctx)))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        // Per-account failed-login backoff (#418), the world-password pattern: the per-IP window keys on a
        // value a distributed attacker multiplies at will, this one keys on the TARGET name — lowercased so
        // case rotation doesn't mint fresh windows. Only failures consume budget (IsExhausted checks without
        // spending), so the account owner with the right password sails through even mid-attack.
        string name = req.Name ?? string.Empty; // declared non-nullable, but JSON binding can deliver null
        string accountKey = name.Trim().ToLowerInvariant();
        if (s.LoginFailLimit.IsExhausted(accountKey))
        {
            return WorldHostApi.ApiError("Too many password attempts — please wait a few minutes.", StatusCodes.Status429TooManyRequests);
        }

        if (s.Registry.Login(name, req.Password) is not { } login)
        {
            s.LoginFailLimit.TryPass(accountKey);
            return Results.Unauthorized();
        }

        // termsOutdated tells the portal/client to show the re-acceptance screen before world actions.
        var account = s.Registry.ResolveSession(login.SessionToken)!;
        return Results.Json(new
        {
            accountId = login.AccountId,
            sessionToken = login.SessionToken,
            // Canonical stored name: lookups are COLLATE NOCASE, so the typed name may differ in casing
            // from the account row — the client shows/persists this one, not what was typed.
            accountName = account.Name,
            // Set by an operator reset: the login works (temp password), but the client nags the player to
            // pick their own password now. Cleared by the next successful change/rescue.
            mustChangePassword = account.MustChangePassword,
            termsOutdated = account.AcceptedTermsVersion < s.Config.TermsVersion,
            // #496: a banned account logs in exactly like any other and only found out at the first blocked
            // action — a dead end nobody explained. The state travels with the login now, and the notices
            // carry what state alone cannot: a world an operator deleted leaves nothing behind to derive.
            banned = account.IsBanned,
            banReason = account.BanReason,
            banReasonCode = account.BanReasonCode,
            bannedAt = account.BannedAtUnix,
            bannedUntil = account.BannedUntilUnix,
            notices = WorldHostApi.NoticesJson(s.Registry, account.Id),
        });
    }

    // The player's unread notices. Polled by the client/portal alongside the world list, because a ban that
    // lands mid-session never passes through /api/login again — sessions outlive it by weeks.
    internal static IResult Notices(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        return Results.Json(new
        {
            banned = account.IsBanned,
            banReason = account.BanReason,
            banReasonCode = account.BanReasonCode,
            bannedAt = account.BannedAtUnix,
            bannedUntil = account.BannedUntilUnix,
            notices = WorldHostApi.NoticesJson(s.Registry, account.Id),
        });
    }

    // Acknowledges notices once the player has read them (id 0 = all of them). Always scoped to the caller.
    internal static IResult AckNotices(WorldHostServices s, HttpContext ctx, NoticeAckRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        s.Registry.MarkNoticesSeen(account.Id, req.Id);
        return Results.Ok();
    }

    internal static IResult AcceptTerms(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        s.Registry.AcceptTerms(account.Id, s.Config.TermsVersion);
        return Results.Ok();
    }

    // Password change — a KNOWN password can be rotated (a forgotten one still cannot: no recovery channel
    // exists by design). Wrong-old-password guesses burn the same per-account budget as failed logins, so a
    // stolen session cannot brute-force its way from "signed in" to "owns the password".
    internal static IResult ChangePassword(WorldHostServices s, ILogger log, HttpContext ctx, ChangePasswordRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        string accountKey = account.Name.Trim().ToLowerInvariant();
        if (s.LoginFailLimit.IsExhausted(accountKey))
        {
            return WorldHostApi.ApiError("Too many password attempts — please wait a few minutes.", StatusCodes.Status429TooManyRequests);
        }

        var (ok, error) = s.Registry.ChangePassword(account.Id, req.OldPassword, req.NewPassword, WorldHostApi.BearerToken(ctx));
        if (!ok)
        {
            if (error == "Wrong password.")
            {
                s.LoginFailLimit.TryPass(accountKey);
            }

            return WorldHostApi.ApiError(error);
        }

        log.LogInformation("Account '{Name}' changed its password (other sessions revoked).", WorldHostApi.LogSafe(account.Name));
        return Results.Ok();
    }

    // Self-service reset with a rescue code — anonymous by nature (the password is gone, so there is no
    // session). Same limiter pair as login: per-IP window + per-account failure budget, and the registry
    // answers one uniform failure so the endpoint is no existence/typo oracle.
    internal static IResult Recover(WorldHostServices s, ILogger log, HttpContext ctx, RecoverRequest req)
    {
        if (!s.LoginLimit.TryPass(WorldHostApi.CallerIp(ctx)))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        string accountKey = (req.Name ?? string.Empty).Trim().ToLowerInvariant();
        if (s.LoginFailLimit.IsExhausted(accountKey))
        {
            return WorldHostApi.ApiError("Too many password attempts — please wait a few minutes.", StatusCodes.Status429TooManyRequests);
        }

        var (ok, error, accountId, accountName, session) = s.Registry.RedeemRecoveryCode(req.Name ?? string.Empty, req.Code, req.NewPassword);
        if (!ok)
        {
            if (error == "Wrong account name or rescue code.")
            {
                s.LoginFailLimit.TryPass(accountKey);
            }

            return WorldHostApi.ApiError(error);
        }

        log.LogInformation("Account '{Name}' reset its password with a rescue code (all previous sessions revoked).", WorldHostApi.LogSafe(accountName));
        return Results.Json(new { accountId, sessionToken = session, accountName });
    }

    // Re-issues the caller's rescue codes (the old set is void). Password-gated like the change endpoint:
    // a stolen 30-day session must not be able to mint itself a permanent recovery secret.
    internal static IResult ReissueRecoveryCodes(WorldHostServices s, ILogger log, HttpContext ctx, RecoveryCodesRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        string accountKey = account.Name.Trim().ToLowerInvariant();
        if (s.LoginFailLimit.IsExhausted(accountKey))
        {
            return WorldHostApi.ApiError("Too many password attempts — please wait a few minutes.", StatusCodes.Status429TooManyRequests);
        }

        if (!s.Registry.VerifyPassword(account.Id, req.Password))
        {
            s.LoginFailLimit.TryPass(accountKey);
            return WorldHostApi.ApiError("Wrong password.");
        }

        log.LogInformation("Account '{Name}' re-issued its rescue codes.", WorldHostApi.LogSafe(account.Name));
        return Results.Json(new { recoveryCodes = s.Registry.CreateRecoveryCodes(account.Id) });
    }

    // Account self-deletion (DSGVO Art. 17): erases the account and every registry row keyed by it (sessions, rescue
    // codes, reports, notices, ...) AND all of its worlds including their saves on disk (live + archive).
    // Deliberately available to banned accounts too.
    internal static IResult DeleteAccount(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        foreach (var world in s.Registry.ListWorlds(account.Id))
        {
            s.Orchestrator.DeleteWorld(world, purgeSaves: true);
        }

        s.Registry.DeleteAccount(account.Id);
        log.LogInformation("Account '{Name}' deleted on request (worlds + saves removed).", WorldHostApi.LogSafe(account.Name));
        return Results.Ok();
    }
}

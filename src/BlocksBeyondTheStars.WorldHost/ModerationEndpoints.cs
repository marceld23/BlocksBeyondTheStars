// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Moderation over the API (#2902): the player's report ("Spieler melden") and the operator's scriptable admin
/// routes under <c>/api/admin</c> (X-Admin-Token).
/// </summary>
internal static class ModerationEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        var api = app.MapGroup("/api");

        // ---------------- Player reports ("Spieler melden") ----------------

        api.MapPost("/reports", (HttpContext ctx, ReportRequest req) => FileReport(s, ctx, req));

        // ---------------- Operator admin (X-Admin-Token; disabled when no token is configured) ----------------

        var admin = api.MapGroup("/admin");

        admin.MapPost("/reset-password", (HttpContext ctx, AdminResetPasswordRequest req) => ResetPassword(s, log, ctx, req));

        admin.MapGet("/reports", (HttpContext ctx) => ListReports(s, ctx));

        admin.MapPost("/reports/{id:long}/close", (HttpContext ctx, long id, CloseReportRequest req) => CloseReport(s, ctx, id, req));

        admin.MapPost("/ban", Task<IResult> (HttpContext ctx, BanRequest req) => BanAsync(s, log, ctx, req));

        admin.MapPost("/announce", Task<IResult> (HttpContext ctx, AnnounceRequest req) => AnnounceAsync(s, log, ctx, req));

        admin.MapDelete("/worlds/{id}", (HttpContext ctx, string id) => DeleteWorld(s, log, ctx, id));
    }

    internal static IResult FileReport(WorldHostServices s, HttpContext ctx, ReportRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!s.ReportLimit.TryPass(account.Id))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        // Banned players may still file reports (they can't play, but silencing them buys nothing);
        // reports are length-capped and reviewed manually — nobody is auto-punished by a report.
        var (ok, error) = s.Registry.CreateReport(account.Id, req.WorldId ?? string.Empty, req.ReportedName, req.Category, req.Message ?? string.Empty);
        if (ok)
        {
            // The inbox was pull-only before (#938): the operator had to open /admin to learn a report exists.
            string reported = string.IsNullOrWhiteSpace(req.ReportedName) ? "(none)" : req.ReportedName.Trim();
            s.Notifier.Post($"New player report ({(req.Category ?? string.Empty).Trim().ToLowerInvariant()})",
                $"Reported: {reported}. World: {(string.IsNullOrWhiteSpace(req.WorldId) ? "(none)" : req.WorldId)}. Review on /admin.", "postbox");
        }

        return ok ? Results.Ok() : WorldHostApi.ApiError(error);
    }

    // Operator password reset, scriptable twin of the /admin form: one-time readable temp password in the
    // answer, must-change flag set, all sessions revoked. Developer accounts are refused — the admin token
    // must not be a path to taking over the operator account itself.
    internal static IResult ResetPassword(WorldHostServices s, ILogger log, HttpContext ctx, AdminResetPasswordRequest req)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        var account = s.Registry.GetAccount(req.AccountId ?? string.Empty);
        var (ok, temp) = account is null ? (false, string.Empty) : s.Registry.AdminResetPassword(account.Id);
        if (!ok)
        {
            return WorldHostApi.ApiError("Operator accounts cannot be reset here.", StatusCodes.Status403Forbidden);
        }

        // Name, not id (CodeQL cs/cleartext-storage): account ids are stable registry references and
        // deliberately stay out of log files — same rule as the signup log line.
        log.LogInformation("Admin reset the password of account '{Name}'.", WorldHostApi.LogSafe(account!.Name));
        return Results.Json(new { tempPassword = temp });
    }

    internal static IResult ListReports(WorldHostServices s, HttpContext ctx)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        return Results.Json(new { reports = s.Registry.ListOpenReports() });
    }

    internal static IResult CloseReport(WorldHostServices s, HttpContext ctx, long id, CloseReportRequest req)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        string status = req.Status is "dismissed" ? "dismissed" : "reviewed";
        s.Registry.CloseReport(id, status);
        return Results.Ok();
    }

    internal static async Task<IResult> BanAsync(WorldHostServices s, ILogger log, HttpContext ctx, BanRequest req)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        if (!s.Registry.SetBanned(req.AccountId, req.Banned, req.Reason ?? string.Empty, req.ReasonCode ?? string.Empty, req.Days))
        {
            return WorldHostApi.ApiError("Operator accounts cannot be banned.", StatusCodes.Status403Forbidden);
        }

        log.LogInformation("Account {Id} {Action} ({Reason}).", WorldHostApi.LogSafe(req.AccountId), req.Banned ? "BANNED" : "unbanned", WorldHostApi.LogSafe(req.Reason));
        int kicked = req.Banned ? await s.Orchestrator.KickAccountEverywhereAsync(req.AccountId) : 0;
        return Results.Json(new { ok = true, kicked });
    }

    // Maintenance announcement (#249), scriptable twin of the /admin form below: pushes an info message or a
    // restart countdown into one world or the whole fleet. The message text is operator-authored free text —
    // never logged raw (LogSafe), sanitized again instance-side before broadcasting.
    internal static async Task<IResult> AnnounceAsync(WorldHostServices s, ILogger log, HttpContext ctx, AnnounceRequest req)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        if (req.WorldId is { } wid && !HostRegistry.IsValidWorldId(wid))
        {
            return WorldHostApi.ApiError("World not found.", StatusCodes.Status404NotFound);
        }

        var (reached, targets) = await s.Orchestrator.AnnounceAsync(req.Kind, req.Text, req.Seconds, req.WorldId);
        log.LogInformation("Admin API: announce kind {Kind} to {Target} — reached {Reached}/{Targets}.",
            req.Kind, req.WorldId is null ? "fleet" : WorldHostApi.LogSafe(req.WorldId), reached, targets);
        return Results.Json(new { reached, targets });
    }

    // Operator world deletion, scriptable twin of the /admin buttons below — the lever for bulk cleanup of
    // dead worlds. `purge=true` also erases the saves (live + archive); without it they stay on disk exactly
    // like an owner's own delete. Unlike the owner route this is NOT ownership-scoped: it deletes any world,
    // including the account-less glitch.fun arcade pool (which the gateway then re-creates empty).
    internal static IResult DeleteWorld(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (!WorldHostApi.IsAdmin(s.Config, ctx))
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return WorldHostApi.ApiError("World not found.", StatusCodes.Status404NotFound);
        }

        bool purge = ctx.Request.Query["purge"].ToString() == "true";
        WorldHostApi.NotifyWorldDeleted(s.Registry, world, ctx.Request.Query["reason"].ToString());
        s.Orchestrator.DeleteWorld(world, purge);
        log.LogInformation("Admin API: world '{Name}' ({Id}) deleted — saves {Fate}.",
            WorldHostApi.LogSafe(world.DisplayName), world.Id, purge ? "PURGED" : "retained");
        return Results.Ok();
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Notifications;
using BlocksBeyondTheStars.Shared.Security;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The per-process state every route handler shares, plus the gates built on it — what Create()'s local
/// functions used to close over. <see cref="ReportHostApp.Create"/> builds exactly one per app and hands it to
/// the three route groups (<see cref="ReportHostPlayerEndpoints"/>, <see cref="ReportHostApiEndpoints"/>,
/// <see cref="ReportHostAdminEndpoints"/>). Every member is a process-lifetime singleton; nothing derived from
/// them is cached here, so a handler reads the same values it read as a closure.
/// </summary>
public sealed class ReportHostRuntime
{
    public ReportHostRuntime(ReportHostConfig config, ReportStore store, AdminNotifier notifier, ILogger log,
        IngestRateLimiter ingestLimiter, IngestRateLimiter replyLimiter, AdminCsrf csrf)
    {
        Config = config;
        Store = store;
        Notifier = notifier;
        Log = log;
        IngestLimiter = ingestLimiter;
        ReplyLimiter = replyLimiter;
        Csrf = csrf;
    }

    public ReportHostConfig Config { get; }

    /// <summary>The inbox storage — owned by the caller (Program.cs / the test fixture), never disposed here.</summary>
    public ReportStore Store { get; }

    public AdminNotifier Notifier { get; }

    /// <summary>The app's own logger (<c>app.Logger</c>), so log lines keep their category.</summary>
    public ILogger Log { get; }

    /// <summary>Per-IP fixed-window limiter for report submission only (#1352).</summary>
    public IngestRateLimiter IngestLimiter { get; }

    /// <summary>The reply routes' limiter, keyed by reply key (see <see cref="GuardReplyKey"/>).</summary>
    public IngestRateLimiter ReplyLimiter { get; }

    /// <summary>The process's admin-form CSRF token (#1369).</summary>
    public AdminCsrf Csrf { get; }

    /// <summary>The current time in unix seconds — read on every call (a method, never a cached value).</summary>
    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // The client IP used for rate limiting; behind the reverse proxy the socket address is the proxy's,
    // so a trusted deployment opts into X-Forwarded-For.
    public string ClientIp(HttpContext ctx)
    {
        if (Config.TrustProxy)
        {
            string forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (forwarded.Length > 0)
            {
                return forwarded.Split(',')[0].Trim();
            }
        }

        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Read-API gate: 503 while unconfigured (fail closed, and distinguishable from a bad key), 403 on a
    // wrong key. Returns null when the request may proceed.
    public IResult? GuardReadKey(HttpContext ctx)
    {
        if (Config.ReadKey.Length == 0)
        {
            return Results.Json(new { error = "read_api_disabled" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return BasicAuth.TokenEquals(ctx.Request.Headers["x-report-read-key"].ToString(), Config.ReadKey)
            ? null
            : Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
    }

    // Admin gate (UI + mutating API): Basic Auth with a browser challenge. Null = authorized.
    public IResult? GuardAdmin(HttpContext ctx)
    {
        if (Config.AdminUser.Length == 0 || Config.AdminPassword.Length == 0)
        {
            return Results.Text("Admin UI is disabled — set BBS_REPORTS_ADMIN_USER and BBS_REPORTS_ADMIN_PASSWORD.\n",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (BasicAuth.IsAuthorized(ctx.Request.Headers.Authorization.ToString(), Config.AdminUser, Config.AdminPassword))
        {
            return null;
        }

        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"BBS ReportHost\", charset=\"UTF-8\"";
        return Results.Text("Unauthorized.\n", statusCode: StatusCodes.Status401Unauthorized);
    }

    // Admin FORM gate (#1369): the submitted hidden field must be this process's CSRF token. Null = OK.
    public IResult? GuardAdminForm(IFormCollection form)
    {
        return Csrf.IsValid(form[AdminCsrf.FieldName].ToString())
            ? null
            : Results.Text("Forbidden — the form's CSRF token does not match this inbox (reload the page and try again).\n",
                statusCode: StatusCodes.Status403Forbidden);
    }

    // Admin JSON gate (#1369): the scriptable routes only take a real JSON body. An HTML form can post
    // urlencoded, multipart or text/plain — never application/json — so a forged <form> cannot reach
    // them even though the browser attaches the Basic credentials. Null = OK.
    public static IResult? GuardAdminJson(HttpContext ctx)
    {
        return ctx.Request.HasJsonContentType()
            ? null
            : Results.Json(new { error = "json_body_required" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
    }

    // The player-facing reply routes (#1327) share the ingest's write-key gate (spam gate, not a secret)
    // but NOT its per-IP limiter: every client polls for answers, so a school / LAN group behind one NAT
    // would burn the ingest budget on polls and see its real F1 reports bounced with 429 (#1352). Returns
    // null when the request may proceed; the reply key itself is checked by GuardReplyKey once the
    // caller has read it from the query (GET) or the JSON body (POST).
    public IResult? GuardPlayerRoute(HttpContext ctx)
    {
        if (Config.WriteKey.Length == 0)
        {
            return Results.Json(new { error = "ingest_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!BasicAuth.TokenEquals(ctx.Request.Headers["x-bugreport-key"].ToString(), Config.WriteKey))
        {
            return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    // The reply key must be well-formed (the inbox ignores anything else), and each key gets its own
    // per-minute budget — generous for a real client (one poll at world start, then every 10 min, plus
    // an ack and the odd answer) and independent of how many installs share an IP. Malformed keys are
    // rejected before they can occupy a counter.
    public IResult? GuardReplyKey(string key)
    {
        if (!BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.IsWellFormed(key))
        {
            return Results.BadRequest(new { error = "invalid_key" });
        }

        if (!ReplyLimiter.Allow(key, NowUnix()))
        {
            return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return null;
    }

    // Attachments of a texture submission (#1966) — served as DOWNLOADS with their name, for the pull tool
    // (read key) and for the operator (admin).
    public IResult ServeAttachment(string id, int index)
    {
        var attachment = Store.Attachments(id).FirstOrDefault(a => a.Index == index);
        if (attachment == null || Store.AttachmentPath(attachment) is not { } path)
        {
            return Results.NotFound();
        }

        return Results.File(path, attachment.Mime, fileDownloadName: attachment.FileName);
    }
}

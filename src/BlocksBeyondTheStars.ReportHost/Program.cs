// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.ReportHost;

// Bug-report inbox ("ReportHost"): receives the game's player feedback (F1) and automatic crash
// reports on the SAME wire contract as the original Wix/Velo endpoint, stores them in SQLite +
// screenshot files, and serves them back through a keyed read API (pull scripts / CI) and a
// Basic-Auth admin UI. Independent of the game/WorldHost deployment — one small container, one
// volume. See docs/developer/REPORT_HOST.md.

var config = ReportHostConfig.FromEnvironment();
using var store = new ReportStore(config);
var limiter = new IngestRateLimiter(config.IngestPerMinute);

// Operator push notifications (#938): one fire-and-forget ping per stored report, so the operator no
// longer has to poll the admin UI to learn something arrived. Off by default (empty NOTIFY_URL). Note
// the known double-ping for in-game F1 feedback: those reports arrive twice by design (client-direct
// POST + the server /bump forward) — see ReportDuplicateGroupingTests.
var notifier = new BlocksBeyondTheStars.Shared.Notifications.AdminNotifier(config.NotifyUrl, "reports");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = config.MaxBodyBytes);

var app = builder.Build();
var log = app.Logger;

long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

int prunedAtStart = store.Prune(config.RetentionDays, NowUnix());
log.LogInformation(
    "ReportHost on {Bind}:{Port} — ingest {Ingest}, read API {Read}, admin UI {Admin}, retention {Retention}, pruned {Pruned} at start.",
    config.BindAddress, config.Port,
    config.WriteKey.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_WRITE_KEY)",
    config.ReadKey.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_READ_KEY)",
    config.AdminUser.Length > 0 && config.AdminPassword.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_ADMIN_USER/PASSWORD)",
    config.RetentionDays > 0 ? config.RetentionDays + "d" : "forever",
    prunedAtStart);

// The client IP used for rate limiting; behind the reverse proxy the socket address is the proxy's,
// so a trusted deployment opts into X-Forwarded-For.
string ClientIp(HttpContext ctx)
{
    if (config.TrustProxy)
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
IResult? GuardReadKey(HttpContext ctx)
{
    if (config.ReadKey.Length == 0)
    {
        return Results.Json(new { error = "read_api_disabled" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return BasicAuth.TokenEquals(ctx.Request.Headers["x-report-read-key"].ToString(), config.ReadKey)
        ? null
        : Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
}

// Admin gate (UI + mutating API): Basic Auth with a browser challenge. Null = authorized.
IResult? GuardAdmin(HttpContext ctx)
{
    if (config.AdminUser.Length == 0 || config.AdminPassword.Length == 0)
    {
        return Results.Text("Admin UI is disabled — set BBS_REPORTS_ADMIN_USER and BBS_REPORTS_ADMIN_PASSWORD.\n",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (BasicAuth.IsAuthorized(ctx.Request.Headers.Authorization.ToString(), config.AdminUser, config.AdminPassword))
    {
        return null;
    }

    ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"BBS ReportHost\", charset=\"UTF-8\"";
    return Results.Text("Unauthorized.\n", statusCode: StatusCodes.Status401Unauthorized);
}

// The read API's item shape — matches the planned puller contract (camelCase, ISO createdAt, parsed
// reportJson, relative screenshotUrl or null).
object ToItem(BugReportRecord r)
{
    JsonElement reportJson;
    try
    {
        using var doc = JsonDocument.Parse(r.ReportJson);
        reportJson = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        using var doc = JsonDocument.Parse("{}");
        reportJson = doc.RootElement.Clone();
    }

    return new
    {
        id = r.Id,
        title = r.Title,
        description = r.Description,
        email = r.Email,
        gameVersion = r.GameVersion,
        buildNumber = r.BuildNumber,
        playerId = r.PlayerId,
        playerName = r.PlayerName,
        sessionId = r.SessionId,
        platform = r.Platform,
        clientTimestamp = r.ClientTimestamp,
        category = r.Category,
        source = r.Source,
        kind = r.Kind,
        status = r.Status,
        createdAt = DateTimeOffset.FromUnixTimeSeconds(r.CreatedUnix).UtcDateTime.ToString("o"),
        screenshotUrl = r.ScreenshotFile.Length > 0 ? $"/api/reports/{r.Id}/screenshot" : null,
        reportJson,
    };
}

// CORS, ingest only: the WebGL client posts feedback cross-origin (from play.* / glitch.fun pages),
// which needs the OPTIONS preflight answered and the POST response readable. Any origin is fine —
// the endpoint already requires the write key and rate-limits, so this widens nothing else.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/api/bugreport")
    {
        ctx.Response.Headers.AccessControlAllowOrigin = "*";
        if (HttpMethods.IsOptions(ctx.Request.Method))
        {
            ctx.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
            ctx.Response.Headers.AccessControlAllowHeaders = "content-type, x-bugreport-key";
            ctx.Response.Headers.AccessControlMaxAge = "86400";
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }
    }

    await next();
});

app.MapGet("/healthz", () => Results.Text("ok\n"));
app.MapGet("/", () => Results.Redirect("/admin"));

// ---------------- Ingest (the game's endpoint — Wix wire contract) ----------------

app.MapPost("/api/bugreport", async (HttpContext ctx) =>
{
    if (config.WriteKey.Length == 0)
    {
        return Results.Json(new { error = "ingest_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!BasicAuth.TokenEquals(ctx.Request.Headers["x-bugreport-key"].ToString(), config.WriteKey))
    {
        return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (!limiter.Allow(ClientIp(ctx), NowUnix()))
    {
        return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    string body;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        body = await reader.ReadToEndAsync();
    }
    catch (BadHttpRequestException)
    {
        return Results.Json(new { error = "payload_too_large" }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    var parsed = ReportIngest.Parse(body, config, out string error);
    if (parsed == null)
    {
        return Results.BadRequest(new { error });
    }

    string id = store.Add(parsed, NowUnix());
    store.Prune(config.RetentionDays, NowUnix());
    log.LogInformation("Report {Id} stored ({Category}{Kind}, v{Version}, screenshot: {Shot}).",
        id, parsed.Category, parsed.Kind.Length > 0 ? "/" + parsed.Kind : "", parsed.GameVersion,
        parsed.ScreenshotBytes != null);
    notifier.Post($"New {parsed.Category} report{(parsed.Kind.Length > 0 ? $" ({parsed.Kind})" : string.Empty)}",
        $"{parsed.Title}\n(v{parsed.GameVersion}, player '{parsed.PlayerName}') — review on /admin.", "postbox");
    return Results.Json(new { ok = true, bugReportId = id });
});

// ---------------- Read API (pull scripts / CI; delta sync via since + cursor) ----------------

app.MapGet("/api/reports", (HttpContext ctx) =>
{
    if (GuardReadKey(ctx) is { } denied)
    {
        return denied;
    }

    var q = ctx.Request.Query;
    long? sinceUnix = null;
    if (DateTimeOffset.TryParse(q["since"], null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var since))
    {
        sinceUnix = since.ToUnixTimeSeconds();
    }

    int limit = int.TryParse(q["limit"], out var l) ? l : 100;

    // Keyset cursor "<createdUnix>:<id>" issued by the previous page; malformed cursors just start over.
    long afterCreated = -1;
    string afterId = "";
    string cursor = q["cursor"].ToString();
    int sep = cursor.IndexOf(':');
    if (sep > 0 && long.TryParse(cursor[..sep], out var cu))
    {
        afterCreated = cu;
        afterId = cursor[(sep + 1)..];
    }

    static string? Opt(Microsoft.Extensions.Primitives.StringValues v)
        => string.IsNullOrEmpty(v.ToString()) ? null : v.ToString();

    var (items, hasMore) = store.Query(sinceUnix, Opt(q["status"]), Opt(q["category"]), Opt(q["source"]), limit, afterCreated, afterId);
    string? nextCursor = hasMore && items.Count > 0 ? $"{items[^1].CreatedUnix}:{items[^1].Id}" : null;
    return Results.Json(new { items = items.Select(ToItem).ToArray(), nextCursor, hasMore });
});

app.MapGet("/api/reports/{id}", (HttpContext ctx, string id) =>
{
    if (GuardReadKey(ctx) is { } denied)
    {
        return denied;
    }

    return store.Get(id) is { } record ? Results.Json(ToItem(record)) : Results.NotFound();
});

app.MapGet("/api/reports/{id}/screenshot", (HttpContext ctx, string id) =>
{
    if (GuardReadKey(ctx) is { } denied)
    {
        return denied;
    }

    if (store.Get(id) is not { } record || store.ScreenshotPath(record) is not { } path)
    {
        return Results.NotFound();
    }

    return Results.File(path, record.ScreenshotFile.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
});

app.MapPatch("/api/reports/{id}", async (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    string status;
    try
    {
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        status = doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() ?? "" : "";
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "invalid_json" });
    }

    if (!BugReportStatus.IsValid(status))
    {
        return Results.BadRequest(new { error = "invalid_status" });
    }

    return store.SetStatus(id, status) ? Results.Json(new { ok = true, status }) : Results.NotFound();
});

app.MapDelete("/api/reports/{id}", (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    return store.Delete(id) ? Results.NoContent() : Results.NotFound();
});

// ---------------- Admin UI (Basic Auth; server-rendered, no script) ----------------

app.MapGet("/admin", (HttpContext ctx) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    static string? Opt(string v) => string.IsNullOrEmpty(v) ? null : v;
    string? status = Opt(ctx.Request.Query["status"].ToString());
    string? category = Opt(ctx.Request.Query["category"].ToString());
    var items = store.Latest(status, category, 200);
    return Results.Content(ReportHostPages.List(items, store.CountByStatus(), status, category), "text/html; charset=utf-8");
});

// One-click bulk export of everything matching the current filters, as a JSON file download — the
// browser-friendly sibling of the keyed read API (GET /api/reports) that pull scripts use.
app.MapGet("/admin/export", (HttpContext ctx) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    static string? Opt(string v) => string.IsNullOrEmpty(v) ? null : v;
    string? status = Opt(ctx.Request.Query["status"].ToString());
    string? category = Opt(ctx.Request.Query["category"].ToString());
    var items = store.Latest(status, category, 100_000);
    byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
        new { exportedUtc = DateTimeOffset.UtcNow, status, category, count = items.Count, reports = items },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
    return Results.File(json, "application/json", $"bbs-reports-{stamp}.json");
});

app.MapGet("/admin/report/{id}", (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    return store.Get(id) is { } record
        ? Results.Content(ReportHostPages.Detail(record), "text/html; charset=utf-8")
        : Results.NotFound();
});

app.MapGet("/admin/report/{id}/screenshot", (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    if (store.Get(id) is not { } record || store.ScreenshotPath(record) is not { } path)
    {
        return Results.NotFound();
    }

    return Results.File(path, record.ScreenshotFile.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
});

app.MapPost("/admin/report/{id}/status", async (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    var form = await ctx.Request.ReadFormAsync();
    string status = form["status"].ToString();
    if (!BugReportStatus.IsValid(status) || !store.SetStatus(id, status))
    {
        return Results.NotFound();
    }

    return Results.Redirect($"/admin/report/{id}");
});

app.MapPost("/admin/report/{id}/delete", (HttpContext ctx, string id) =>
{
    if (GuardAdmin(ctx) is { } denied)
    {
        return denied;
    }

    store.Delete(id);
    return Results.Redirect("/admin");
});

app.Run();

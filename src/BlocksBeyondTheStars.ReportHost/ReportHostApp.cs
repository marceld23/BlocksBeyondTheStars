// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Notifications;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The inbox's HTTP surface — every route of the ReportHost, wired onto a <see cref="WebApplication"/>
/// that the entry point runs and the tests start in-process on a loopback port (#1352). Program.cs only
/// reads the environment, opens the store and calls <see cref="Create"/>.
/// </summary>
public static class ReportHostApp
{
    /// <summary>Builds the configured app (not yet started). <paramref name="store"/> stays owned by the
    /// caller; <paramref name="args"/> are the command-line arguments handed to the host builder.</summary>
    public static WebApplication Create(ReportHostConfig config, ReportStore store, AdminNotifier notifier, string[] args)
    {
        // Per-IP fixed-window limiter for report submission only. The reply routes have their own
        // limiter keyed by reply key (see below) so a LAN class behind one NAT polling for answers
        // cannot exhaust the budget a real F1 report from the same network needs (#1352).
        var limiter = new IngestRateLimiter(config.IngestPerMinute);
        var replyLimiter = new IngestRateLimiter(config.ReplyPerMinute);

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = config.MaxBodyBytes);

        var app = builder.Build();
        var log = app.Logger;

        long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        int prunedAtStart = store.Prune(config.RetentionDays, NowUnix());
        // Reports stored before the reply channel (#1327) get their reply key derived from the player id they
        // already carry, so their reporters can be answered too. Idempotent — a no-op after the first start.
        int backfilled = store.BackfillReplyKeys();
        if (backfilled > 0)
        {
            log.LogInformation("Back-filled reply keys for {Count} pre-existing report(s).", backfilled);
        }

        // Server forwards that got a key derived from the player NAME (guessable, and never what the client
        // polls with — #1359) lose it again. Idempotent — a no-op once repaired.
        int revoked = store.RevokeNameDerivedServerKeys();
        if (revoked > 0)
        {
            log.LogInformation("Revoked name-derived reply keys on {Count} server-forwarded report(s).", revoked);
        }

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

        // One reply-thread entry as JSON (shared by the read API and the client's poll).
        static object ReplyJson(ReplyRecord reply) => new
        {
            id = reply.Id,
            author = reply.Author,
            text = reply.Text,
            isQuestion = reply.IsQuestion,
            createdUnix = reply.CreatedUnix,
            seen = reply.SeenUnix > 0,
        };

        // The read API's item shape — matches the planned puller contract (camelCase, ISO createdAt, parsed
        // reportJson, relative screenshotUrl or null). The single-report endpoint adds the reply thread;
        // the reply key itself is never exposed (it is the player's credential).
        object ToItem(BugReportRecord r, IReadOnlyList<ReplyRecord>? replies = null)
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
                fixedInVersion = r.FixedInVersion,
                replies = replies?.Select(x => ReplyJson(x)).ToArray(),
            };
        }

        // The player-facing reply routes (#1327) share the ingest's write-key gate (spam gate, not a secret)
        // but NOT its per-IP limiter: every client polls for answers, so a school / LAN group behind one NAT
        // would burn the ingest budget on polls and see its real F1 reports bounced with 429 (#1352). Returns
        // null when the request may proceed; the reply key itself is checked by GuardReplyKey once the
        // caller has read it from the query (GET) or the JSON body (POST).
        IResult? GuardPlayerRoute(HttpContext ctx)
        {
            if (config.WriteKey.Length == 0)
            {
                return Results.Json(new { error = "ingest_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!BasicAuth.TokenEquals(ctx.Request.Headers["x-bugreport-key"].ToString(), config.WriteKey))
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            return null;
        }

        // The reply key must be well-formed (the inbox ignores anything else), and each key gets its own
        // per-minute budget — generous for a real client (one poll at world start, then every 10 min, plus
        // an ack and the odd answer) and independent of how many installs share an IP. Malformed keys are
        // rejected before they can occupy a counter.
        IResult? GuardReplyKey(string key)
        {
            if (!BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.IsWellFormed(key))
            {
                return Results.BadRequest(new { error = "invalid_key" });
            }

            if (!replyLimiter.Allow(key, NowUnix()))
            {
                return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            return null;
        }

        // One thread as the client's poll returns it: the report's title/status/fixed-in line, the whole
        // conversation, and the ids the client must acknowledge once shown.
        object ThreadJson(ReportThread thread) => new
        {
            reportId = thread.Report.Id,
            title = thread.Report.Title,
            status = thread.Report.Status,
            fixedInVersion = thread.Report.FixedInVersion,
            createdUnix = thread.Report.CreatedUnix,
            replies = thread.Replies.Select(x => ReplyJson(x)).ToArray(),
            unseenIds = thread.Replies.Where(x => x.Author == ReplyRecord.AuthorDev && x.SeenUnix == 0).Select(x => x.Id).ToArray(),
        };

        // CORS, player-facing routes only: the WebGL client posts feedback and polls its reply threads
        // cross-origin (from play.* / glitch.fun pages), which needs the OPTIONS preflight answered and the
        // responses readable. Any origin is fine — every one of these routes already requires the write key
        // and rate-limits, so this widens nothing else.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path == "/api/bugreport" || ctx.Request.Path.StartsWithSegments("/api/replies"))
            {
                ctx.Response.Headers.AccessControlAllowOrigin = "*";
                if (HttpMethods.IsOptions(ctx.Request.Method))
                {
                    ctx.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
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
            return Results.Json(new { items = items.Select(r => ToItem(r)).ToArray(), nextCursor, hasMore });
        });

        app.MapGet("/api/reports/{id}", (HttpContext ctx, string id) =>
        {
            if (GuardReadKey(ctx) is { } denied)
            {
                return denied;
            }

            return store.Get(id) is { } record ? Results.Json(ToItem(record, store.ListReplies(id))) : Results.NotFound();
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

        // ---------------- Reply threads (#1327): player pull + answer, operator post ----------------

        // The client's poll: every thread of this key with an unread developer entry. `since` (unix seconds,
        // optional) narrows to entries created after that point — the client passes its last poll time.
        app.MapGet("/api/replies", (HttpContext ctx) =>
        {
            if (GuardPlayerRoute(ctx) is { } denied)
            {
                return denied;
            }

            string key = ctx.Request.Query["key"].ToString();
            if (GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            long since = long.TryParse(ctx.Request.Query["since"], out var s) ? s : 0;
            var threads = store.UnreadThreads(key, since);
            return Results.Json(new { items = threads.Select(t => ThreadJson(t)).ToArray() });
        });

        // Marks developer entries as read once the client showed them. Scoped to the key inside the store.
        app.MapPost("/api/replies/ack", async (HttpContext ctx) =>
        {
            if (GuardPlayerRoute(ctx) is { } denied)
            {
                return denied;
            }

            string key;
            var ids = new List<long>();
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                var root = doc.RootElement;
                key = root.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : "";
                if (root.TryGetProperty("replyIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long id))
                        {
                            ids.Add(id);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }

            if (GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            int acked = store.AckReplies(key, ids, NowUnix());
            return Results.Json(new { ok = true, acked });
        });

        // The player's answer to a developer question, typed in the game's reply dialog. Same text cap as a
        // report description; bounded per report so a leaked key cannot flood the inbox.
        app.MapPost("/api/replies", async (HttpContext ctx) =>
        {
            if (GuardPlayerRoute(ctx) is { } denied)
            {
                return denied;
            }

            string key, reportId, text;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                var root = doc.RootElement;
                static string Str(JsonElement o, string name)
                    => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                key = Str(root, "key");
                reportId = Str(root, "reportId");
                text = Str(root, "text").Trim();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }

            if (GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            if (text.Length == 0)
            {
                return Results.BadRequest(new { error = "empty_text" });
            }

            if (text.Length > config.MaxDescriptionLength)
            {
                text = text[..config.MaxDescriptionLength];
            }

            long replyId = store.AddPlayerReply(key, reportId, text, NowUnix());
            switch (replyId)
            {
                case -1:
                    return Results.NotFound(new { error = "not_found" });
                case -2:
                    return Results.Json(new { error = "nothing_to_answer" }, statusCode: StatusCodes.Status409Conflict);
                case -3:
                    return Results.Json(new { error = "reply_limit" }, statusCode: StatusCodes.Status409Conflict);
            }

            var report = store.Get(reportId);
            log.LogInformation("Player replied on report {Id} (reply {ReplyId}).", reportId, replyId);
            notifier.Post("Player replied",
                $"{report?.Title}\n(player '{report?.PlayerName}') — /admin/report/{reportId}", "speech_balloon");
            return Results.Json(new { ok = true, replyId }, statusCode: StatusCodes.Status201Created);
        });

        // Operator answer / follow-up question — the scriptable twin of the admin form below.
        // Body: { "text": "...", "question": bool, "fixedInVersion": "2026.8.23" (optional) }.
        app.MapPost("/api/reports/{id}/replies", async (HttpContext ctx, string id) =>
        {
            if (GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            string text;
            bool question;
            string? fixedIn;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                var root = doc.RootElement;
                text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? "").Trim() : "";
                question = root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.True;
                fixedIn = root.TryGetProperty("fixedInVersion", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }

            if (text.Length == 0)
            {
                return Results.BadRequest(new { error = "empty_text" });
            }

            long replyId = store.AddDevReply(id, text, question, NowUnix());
            if (replyId < 0)
            {
                return Results.NotFound();
            }

            if (fixedIn != null)
            {
                store.SetFixedInVersion(id, fixedIn);
            }

            return Results.Json(new { ok = true, replyId }, statusCode: StatusCodes.Status201Created);
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
                ? Results.Content(ReportHostPages.Detail(record, store.ListReplies(id)), "text/html; charset=utf-8")
                : Results.NotFound();
        });

        // The reply form on the detail page: an answer, or a follow-up question (flips the status to
        // waiting_for_player), plus the optional "fixed in version" note the player sees with the thread.
        app.MapPost("/admin/report/{id}/reply", async (HttpContext ctx, string id) =>
        {
            if (GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var form = await ctx.Request.ReadFormAsync();
            string text = form["text"].ToString().Trim();
            bool question = form["question"].ToString() == "1";
            string fixedIn = form["fixed_in_version"].ToString().Trim();

            if (text.Length > 0 && store.AddDevReply(id, text, question, NowUnix()) < 0)
            {
                return Results.NotFound();
            }

            if (store.Get(id) is { } record && fixedIn != record.FixedInVersion)
            {
                store.SetFixedInVersion(id, fixedIn);
            }

            return Results.Redirect($"/admin/report/{id}");
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

        return app;
    }
}

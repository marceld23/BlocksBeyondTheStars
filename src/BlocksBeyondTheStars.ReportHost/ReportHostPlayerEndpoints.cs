// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Security;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The routes the game client calls — exactly the ones the CORS middleware in <see cref="ReportHostApp.Create"/>
/// covers: report submission (write key + per-IP limiter) and the reply threads (write key + per-reply-key
/// limiter).
/// </summary>
public static class ReportHostPlayerEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>; every handler reads its state and gates from
    /// <paramref name="runtime"/>.</summary>
    public static void Map(WebApplication app, ReportHostRuntime runtime)
    {
        // ---------------- Ingest (the game's endpoint — Wix wire contract) ----------------

        app.MapPost("/api/bugreport", async (HttpContext ctx) =>
        {
            if (runtime.Config.WriteKey.Length == 0)
            {
                return Results.Json(new { error = "ingest_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!BasicAuth.TokenEquals(ctx.Request.Headers["x-bugreport-key"].ToString(), runtime.Config.WriteKey))
            {
                return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!runtime.IngestLimiter.Allow(runtime.ClientIp(ctx), ReportHostRuntime.NowUnix()))
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

            var parsed = ReportIngest.Parse(body, runtime.Config, out string error);
            if (parsed == null)
            {
                return Results.BadRequest(new { error });
            }

            string id = runtime.Store.Add(parsed, ReportHostRuntime.NowUnix());
            runtime.Store.Prune(runtime.Config.RetentionDays, ReportHostRuntime.NowUnix());
            runtime.Store.PruneTextureSubmissions(runtime.Config.TextureRetentionDays, ReportHostRuntime.NowUnix());
            runtime.Log.LogInformation("Report {Id} stored ({Category}{Kind}, v{Version}, screenshot: {Shot}).",
                id, parsed.Category, parsed.Kind.Length > 0 ? "/" + parsed.Kind : "", parsed.GameVersion,
                parsed.ScreenshotBytes != null);
            runtime.Notifier.Post($"New {parsed.Category} report{(parsed.Kind.Length > 0 ? $" ({parsed.Kind})" : string.Empty)}",
                $"{parsed.Title}\n(v{parsed.GameVersion}, player '{parsed.PlayerName}') — review on /admin.", "postbox");
            return Results.Json(new { ok = true, bugReportId = id });
        });

        // ---------------- Reply threads (#1327): player pull + answer, operator post ----------------

        // The client's poll: every thread of this key with an unread developer entry. `since` (unix seconds,
        // optional) narrows to entries created after that point — the client passes its last poll time.
        // `ids` (comma-separated, optional, #1369) are the report ids the client still remembers; the ones
        // this key can no longer read (deleted, pruned, or never its own) come back as `gone`, so the
        // client forgets them instead of polling for up to 90 days for a report that no longer exists.
        app.MapGet("/api/replies", (HttpContext ctx) =>
        {
            if (runtime.GuardPlayerRoute(ctx) is { } denied)
            {
                return denied;
            }

            string key = ctx.Request.Query["key"].ToString();
            if (runtime.GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            long since = long.TryParse(ctx.Request.Query["since"], out var s) ? s : 0;
            var threads = runtime.Store.UnreadThreads(key, since);
            string[] asked = ctx.Request.Query["ids"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(id => id.Length <= 64)
                .ToArray();
            var gone = asked.Length > 0 ? runtime.Store.MissingReports(key, asked) : new List<string>();
            return Results.Json(new { items = threads.Select(t => ReportApiJson.Thread(t)).ToArray(), gone });
        });

        // Marks developer entries as read once the client showed them. Scoped to the key inside the store.
        app.MapPost("/api/replies/ack", async (HttpContext ctx) =>
        {
            if (runtime.GuardPlayerRoute(ctx) is { } denied)
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

            if (runtime.GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            int acked = runtime.Store.AckReplies(key, ids, ReportHostRuntime.NowUnix());
            return Results.Json(new { ok = true, acked });
        });

        // The player's answer to a developer question, typed in the game's reply dialog. Same text cap as a
        // report description; bounded per report so a leaked key cannot flood the inbox.
        app.MapPost("/api/replies", async (HttpContext ctx) =>
        {
            if (runtime.GuardPlayerRoute(ctx) is { } denied)
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

            if (runtime.GuardReplyKey(key) is { } keyDenied)
            {
                return keyDenied;
            }

            if (text.Length == 0)
            {
                return Results.BadRequest(new { error = "empty_text" });
            }

            if (text.Length > runtime.Config.MaxDescriptionLength)
            {
                text = text[..runtime.Config.MaxDescriptionLength];
            }

            long replyId = runtime.Store.AddPlayerReply(key, reportId, text, ReportHostRuntime.NowUnix());
            switch (replyId)
            {
                case -1:
                    return Results.NotFound(new { error = "not_found" });
                case -2:
                    return Results.Json(new { error = "nothing_to_answer" }, statusCode: StatusCodes.Status409Conflict);
                case -3:
                    return Results.Json(new { error = "reply_limit" }, statusCode: StatusCodes.Status409Conflict);
            }

            ReportPairActions.MirrorStatus(runtime.Store, reportId); // player_replied on the screenshot half too (#1380)
            var report = runtime.Store.Get(reportId);
            runtime.Log.LogInformation("Player replied on report {Id} (reply {ReplyId}).", reportId, replyId);
            runtime.Notifier.Post("Player replied",
                $"{report?.Title}\n(player '{report?.PlayerName}') — /admin/report/{reportId}", "speech_balloon");
            return Results.Json(new { ok = true, replyId }, statusCode: StatusCodes.Status201Created);
        });
    }
}

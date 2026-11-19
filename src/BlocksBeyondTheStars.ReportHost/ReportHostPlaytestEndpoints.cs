// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The playtest catalogue's routes: the one the game client fetches (<c>GET /api/playtests</c>, behind the same spam
/// key as report submission, with its own per-IP budget and an ETag) and the scriptable operator routes the sync tool
/// uses (Basic Auth; mutations take a real JSON body, like every scriptable admin route — #1369). The catalogue is
/// authored content, not player data: nothing here reads or writes a report.
/// </summary>
public static class ReportHostPlaytestEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    public static void Map(WebApplication app, ReportHostRuntime runtime)
    {
        MapFetch(app, runtime);
        MapOperatorRoutes(app, runtime);
        MapSwitch(app, runtime);
        MapReports(app, runtime);
    }

    /// <summary>The routes about playtest REPORTS: what an install reads back about its own, and the operator's
    /// scriptable confirm.</summary>
    private static void MapReports(WebApplication app, ReportHostRuntime runtime)
    {
        // The game's own playtest reports, by reply key: which task and revision each answered and whether it was
        // confirmed. It restores the badges after a reinstall and settles "confirmed" even when the thank-you was
        // read on another start. Same gates as the reply poll (spam key + the key's own budget).
        app.MapGet("/api/playtests/mine", (HttpContext ctx) =>
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

            var items = runtime.Store.PlaytestReportsOf(key)
                .Select(r => (Record: r, Data: PlaytestReportData.Read(r)))
                .Where(x => x.Data != null)
                .Select(x => new
                {
                    reportId = x.Record.Id,
                    taskId = x.Data!.TaskId,
                    rev = x.Data.Rev,
                    status = x.Record.Status,
                    confirmed = x.Record.Status == BugReportStatus.Done,
                    fixedInVersion = x.Record.FixedInVersion,
                    createdUnix = x.Record.CreatedUnix,
                })
                .ToArray();
            return Results.Json(new { items });
        });

        MapConfirm(app, runtime);
    }

    private static void MapConfirm(WebApplication app, ReportHostRuntime runtime)
    {
        // Confirms a playtest report — the scriptable twin of the detail page's button.
        // Body: { "text": "..." (optional), "fixedInVersion": "2026.11.23" (optional) }.
        app.MapPost("/api/reports/{id}/confirm", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (ReportHostRuntime.GuardAdminJson(ctx) is { } notJson)
            {
                return notJson;
            }

            string? text, fixedIn;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                var root = doc.RootElement;
                text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                fixedIn = root.TryGetProperty("fixedInVersion", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }

            return PlaytestActions.Confirm(runtime.Store, runtime.Playtests, id, text, fixedIn, ReportHostRuntime.NowUnix()) is { } done
                ? Results.Json(new { ok = true, reportIds = done.ReportIds, reportId = done.ThreadReportId, replyId = done.ReplyId, text = done.Text })
                : Results.NotFound();
        });
    }

    private static void MapFetch(WebApplication app, ReportHostRuntime runtime)
    {
        // The game's fetch. "Not modified" is the usual answer — a client asks at most every few hours and the
        // catalogue changes a few times a month.
        app.MapGet("/api/playtests", (HttpContext ctx) =>
        {
            if (runtime.GuardPlayerRoute(ctx) is { } denied)
            {
                return denied;
            }

            if (!runtime.PlaytestLimiter.Allow(runtime.ClientIp(ctx), ReportHostRuntime.NowUnix()))
            {
                return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            var (json, etag) = runtime.Playtests.Served();
            ctx.Response.Headers.ETag = etag;
            ctx.Response.Headers.CacheControl = "no-cache"; // always revalidate; the ETag makes that cheap
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag)
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            return Results.Content(json, "application/json", Encoding.UTF8);
        });
    }

    private static void MapOperatorRoutes(WebApplication app, ReportHostRuntime runtime)
    {
        // Everything stored, as written (switched-off tasks included) — what the sync tool pulls into the repository.
        app.MapGet("/api/playtests/export", (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            return Results.Content(ExportJson(runtime.Playtests), "application/json", Encoding.UTF8);
        });

        // Creates or replaces one task. Body: the task's JSON. `?force=true` overrides the revision check.
        app.MapPut("/api/playtests/{id}", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (ReportHostRuntime.GuardAdminJson(ctx) is { } notJson)
            {
                return notJson;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            bool force = string.Equals(ctx.Request.Query["force"], "true", StringComparison.OrdinalIgnoreCase);
            return SaveTask(runtime, id, body, force);
        });

        app.MapDelete("/api/playtests/{id}", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            return runtime.Playtests.Delete(id) ? Results.NoContent() : Results.NotFound();
        });
    }

    /// <summary>The whole store as one catalogue document — every task as it was written, the kill switch's state.</summary>
    private static string ExportJson(PlaytestStore store)
    {
        var sb = new StringBuilder();
        sb.Append("{\"schema\":").Append(PlaytestLimits.Schema)
            .Append(",\"enabled\":").Append(store.CatalogEnabled ? "true" : "false")
            .Append(",\"tasks\":[");
        bool first = true;
        foreach (var record in store.All())
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append(record.Json); // normalised by the store when it was saved
        }

        return sb.Append("]}").ToString();
    }

    private static void MapSwitch(WebApplication app, ReportHostRuntime runtime)
    {
        // The kill switch. Body: { "enabled": bool }.
        app.MapPost("/api/playtests/enabled", async (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (ReportHostRuntime.GuardAdminJson(ctx) is { } notJson)
            {
                return notJson;
            }

            bool enabled;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
                if (!doc.RootElement.TryGetProperty("enabled", out var e) || (e.ValueKind != JsonValueKind.True && e.ValueKind != JsonValueKind.False))
                {
                    return Results.BadRequest(new { error = "enabled_required" });
                }

                enabled = e.GetBoolean();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_json" });
            }

            runtime.Playtests.SetCatalogEnabled(enabled);
            runtime.Log.LogInformation("Playtest catalogue switched {State}.", enabled ? "on" : "off");
            return Results.Json(new { ok = true, enabled });
        });
    }

    /// <summary>
    /// Validates and stores one task — the one path for the scriptable route and the admin form. The answer names
    /// every finding: errors refuse the save (400), warnings ride along with a successful one.
    /// </summary>
    public static IResult SaveTask(ReportHostRuntime runtime, string id, string body, bool force)
    {
        if (!PlaytestCatalogJson.TryReadTask(body, out var task, PlaytestReadMode.AsWritten))
        {
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (task.Id.Length == 0)
        {
            task.Id = id; // the route names the task; a body may leave the id out
        }
        else if (!string.Equals(task.Id, id, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "id_mismatch" });
        }

        var problems = PlaytestCatalogValidator.Validate(task);
        if (PlaytestCatalogValidator.HasErrors(problems))
        {
            return Results.BadRequest(new { error = "invalid_task", problems = problems.Select(ProblemJson).ToArray() });
        }

        switch (runtime.Playtests.Save(task, ReportHostRuntime.NowUnix(), force))
        {
            case PlaytestSaveResult.ReservedId:
                return Results.BadRequest(new { error = "reserved_id" });
            case PlaytestSaveResult.TooMany:
                return Results.Json(new { error = "too_many_tasks" }, statusCode: StatusCodes.Status409Conflict);
            case PlaytestSaveResult.StaleRev:
                return Results.Json(new { error = "stale_rev", storedRev = runtime.Playtests.Get(id)?.Rev ?? 0 },
                    statusCode: StatusCodes.Status409Conflict);
        }

        runtime.Log.LogInformation("Playtest task {Id} saved (rev {Rev}).", task.Id, task.Rev);
        return Results.Json(new { ok = true, id = task.Id, rev = task.Rev, warnings = problems.Select(ProblemJson).ToArray() });
    }

    private static object ProblemJson(PlaytestProblem p)
        => new { severity = p.IsError ? "error" : "warning", where = p.Where, message = p.Message };
}

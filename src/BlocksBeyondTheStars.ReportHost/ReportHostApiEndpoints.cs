// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The operator's <c>/api/reports*</c> surface: the keyed read API for pull scripts (<c>x-report-read-key</c>)
/// and the scriptable admin mutations (Basic Auth + JSON body).
/// </summary>
public static class ReportHostApiEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>; every handler reads its state and gates from
    /// <paramref name="runtime"/>.</summary>
    public static void Map(WebApplication app, ReportHostRuntime runtime)
    {
        // ---------------- Read API (pull scripts / CI; delta sync via since + cursor) ----------------

        app.MapGet("/api/reports", (HttpContext ctx) =>
        {
            if (runtime.GuardReadKey(ctx) is { } denied)
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

            var (items, hasMore) = runtime.Store.Query(sinceUnix, Opt(q["status"]), Opt(q["category"]), Opt(q["source"]), limit, afterCreated, afterId);
            string? nextCursor = hasMore && items.Count > 0 ? $"{items[^1].CreatedUnix}:{items[^1].Id}" : null;
            return Results.Json(new { items = items.Select(r => ReportApiJson.Item(runtime.Store, r)).ToArray(), nextCursor, hasMore });
        });

        app.MapGet("/api/reports/{id}", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardReadKey(ctx) is { } denied)
            {
                return denied;
            }

            return runtime.Store.Get(id) is { } record ? Results.Json(ReportApiJson.Item(runtime.Store, record, runtime.Store.ListReplies(id))) : Results.NotFound();
        });

        app.MapGet("/api/reports/{id}/screenshot", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardReadKey(ctx) is { } denied)
            {
                return denied;
            }

            if (runtime.Store.Get(id) is not { } record || runtime.Store.ScreenshotPath(record) is not { } path)
            {
                return Results.NotFound();
            }

            return Results.File(path, record.ScreenshotFile.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
        });

        // A texture submission's files (#1966) as downloads, for the pull tool — see ReportHostRuntime.ServeAttachment.
        app.MapGet("/api/reports/{id}/attachment/{index:int}", (HttpContext ctx, string id, int index)
            => runtime.GuardReadKey(ctx) is { } denied ? denied : runtime.ServeAttachment(id, index));

        app.MapPatch("/api/reports/{id}", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (ReportHostRuntime.GuardAdminJson(ctx) is { } notJson)
            {
                return notJson;
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

            // The whole pair (#1380) — `reportIds` names every row changed, the addressed one first.
            return ReportPairActions.SetStatus(runtime.Store, id, status) is { } reportIds
                ? Results.Json(new { ok = true, status, reportIds })
                : Results.NotFound();
        });

        app.MapDelete("/api/reports/{id}", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            return ReportPairActions.Delete(runtime.Store, id) != null ? Results.NoContent() : Results.NotFound();
        });

        // Operator answer / follow-up question — the scriptable twin of the admin form below.
        // Body: { "text": "...", "question": bool, "fixedInVersion": "2026.8.23" (optional) }.
        app.MapPost("/api/reports/{id}/replies", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (ReportHostRuntime.GuardAdminJson(ctx) is { } notJson)
            {
                return notJson;
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

            // The thread lives on the keyed half of the pair (#1378) — answering the screenshot row of an old
            // report must not store the text where no game can read it.
            if (runtime.Store.Get(id) is not { } target)
            {
                return Results.NotFound();
            }

            var owner = ReportPairActions.ThreadOwnerOf(runtime.Store, target);
            long replyId = runtime.Store.AddDevReply(owner.Id, text, question, ReportHostRuntime.NowUnix());
            if (replyId < 0)
            {
                return Results.NotFound();
            }

            if (fixedIn != null)
            {
                runtime.Store.SetFixedInVersion(owner.Id, fixedIn);
            }

            if (question)
            {
                ReportPairActions.MirrorStatus(runtime.Store, owner.Id); // waiting_for_player on the screenshot half too (#1380)
            }

            return Results.Json(new { ok = true, replyId, reportId = owner.Id }, statusCode: StatusCodes.Status201Created);
        });
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The Basic-Auth admin UI — server-rendered pages, their forms (CSRF token, #1369) and the operator's file
/// views.
/// </summary>
public static class ReportHostAdminEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>; every handler reads its state and gates from
    /// <paramref name="runtime"/>.</summary>
    public static void Map(WebApplication app, ReportHostRuntime runtime)
    {
        // ---------------- Admin UI (Basic Auth; server-rendered, no script) ----------------

        app.MapGet("/admin", (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            static string? Opt(string v) => string.IsNullOrEmpty(v) ? null : v;
            string? status = Opt(ctx.Request.Query["status"].ToString());
            string? category = Opt(ctx.Request.Query["category"].ToString());
            var items = runtime.Store.Latest(status, category, 200);
            return Results.Content(ReportHostPages.List(items, runtime.Store.CountByStatus(), status, category), "text/html; charset=utf-8");
        });

        // One-click bulk export of everything matching the current filters, as a JSON file download — the
        // browser-friendly sibling of the keyed read API (GET /api/reports) that pull scripts use. The file carries
        // no reporter's reply key (#2921, see ReportApiJson.ExportItem).
        app.MapGet("/admin/export", (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            static string? Opt(string v) => string.IsNullOrEmpty(v) ? null : v;
            string? status = Opt(ctx.Request.Query["status"].ToString());
            string? category = Opt(ctx.Request.Query["category"].ToString());
            var items = runtime.Store.Latest(status, category, 100_000);
            byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new { exportedUtc = DateTimeOffset.UtcNow, status, category, count = items.Count, reports = items.Select(ReportApiJson.ExportItem).ToArray() },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
            return Results.File(json, "application/json", $"bbs-reports-{stamp}.json");
        });

        app.MapGet("/admin/report/{id}", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (runtime.Store.Get(id) is not { } record)
            {
                return Results.NotFound();
            }

            var owner = ReportPairActions.ThreadOwnerOf(runtime.Store, record);
            var pair = ReportPairActions.PairOf(runtime.Store, record);
            // Every half's entries, merged (#1642): a pair keyed alike on both halves used to own two threads
            // depending on which half was answered — the old split conversations stay readable from either side.
            var replies = pair.Select(p => p.Id).Append(owner.Id).Distinct()
                .SelectMany(pid => runtime.Store.ListReplies(pid))
                .OrderBy(x => x.CreatedUnix).ThenBy(x => x.Id)
                .ToList();
            return Results.Content(ReportHostPages.Detail(record, replies, runtime.Csrf, owner, pair, runtime.Store.Attachments(record.Id)), "text/html; charset=utf-8");
        });

        // The reply form on the detail page: an answer, or a follow-up question (flips the status to
        // waiting_for_player), plus the optional "fixed in version" note the player sees with the thread.
        app.MapPost("/admin/report/{id}/reply", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var form = await ctx.Request.ReadFormAsync();
            if (runtime.GuardAdminForm(form) is { } forged)
            {
                return forged;
            }

            string text = form["text"].ToString().Trim();
            bool question = form["question"].ToString() == "1";
            string fixedIn = form["fixed_in_version"].ToString().Trim();

            if (runtime.Store.Get(id) is not { } record)
            {
                return Results.NotFound();
            }

            // Same resolution as the detail page (#1378): the reply goes to the half that carries the key.
            var owner = ReportPairActions.ThreadOwnerOf(runtime.Store, record);
            if (text.Length > 0 && runtime.Store.AddDevReply(owner.Id, text, question, ReportHostRuntime.NowUnix()) < 0)
            {
                return Results.NotFound();
            }

            if (fixedIn != owner.FixedInVersion)
            {
                runtime.Store.SetFixedInVersion(owner.Id, fixedIn);
            }

            if (text.Length > 0 && question)
            {
                ReportPairActions.MirrorStatus(runtime.Store, owner.Id); // waiting_for_player on the screenshot half too (#1380)
            }

            return Results.Redirect($"/admin/report/{id}");
        });
        // Attachments of a texture submission (#1966) as downloads with their name, for the operator — see
        // ReportHostRuntime.ServeAttachment.
        app.MapGet("/admin/report/{id}/attachment/{index:int}", (HttpContext ctx, string id, int index)
            => runtime.GuardAdmin(ctx) is { } denied ? denied : runtime.ServeAttachment(id, index));

        // The picture of a texture submission, INLINE for the admin page's preview.
        app.MapGet("/admin/report/{id}/attachment/{index:int}/view", (HttpContext ctx, string id, int index) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var attachment = runtime.Store.Attachments(id).FirstOrDefault(a => a.Index == index);
            if (attachment == null || attachment.Mime != "image/png" || runtime.Store.AttachmentPath(attachment) is not { } path)
            {
                return Results.NotFound();
            }

            return Results.File(path, "image/png");
        });

        app.MapGet("/admin/report/{id}/screenshot", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (runtime.Store.Get(id) is not { } record || runtime.Store.ScreenshotPath(record) is not { } path)
            {
                return Results.NotFound();
            }

            return Results.File(path, record.ScreenshotFile.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
        });

        app.MapPost("/admin/report/{id}/status", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var form = await ctx.Request.ReadFormAsync();
            if (runtime.GuardAdminForm(form) is { } forged)
            {
                return forged;
            }

            // The whole pair (#1380): the list shows the two rows as one report, so "mark done" must not leave
            // the other half new.
            string status = form["status"].ToString();
            if (ReportPairActions.SetStatus(runtime.Store, id, status) == null)
            {
                return Results.NotFound();
            }

            return Results.Redirect($"/admin/report/{id}");
        });

        app.MapPost("/admin/report/{id}/delete", async (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var form = await ctx.Request.ReadFormAsync();
            if (runtime.GuardAdminForm(form) is { } forged)
            {
                return forged;
            }

            ReportPairActions.Delete(runtime.Store, id); // both halves, with screenshots and threads (#1380)
            return Results.Redirect("/admin");
        });
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The admin UI of the playtest catalogue (Basic Auth; every form carries the CSRF token, #1369): the task list, the
/// editing form, save / delete / switch, and the "confirm" button of a playtest report. What is saved here is what
/// the game clients fetch — this page is the catalogue's truth; the repository only mirrors it.
/// </summary>
public static class ReportHostPlaytestAdminEndpoints
{
    private const string Html = "text/html; charset=utf-8";

    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    public static void Map(WebApplication app, ReportHostRuntime runtime)
    {
        MapPages(app, runtime);
        MapTaskForms(app, runtime);
        MapSwitches(app, runtime);
        MapConfirm(app, runtime);
    }

    /// <summary>The review card of a playtest report for the report detail page — empty for every other report.</summary>
    public static string ReportCard(ReportHostRuntime runtime, BugReportRecord shown, IReadOnlyList<BugReportRecord> pair)
    {
        var playtest = pair.FirstOrDefault(r => r.Category == ReportIngest.PlaytestCategory);
        if (playtest == null)
        {
            return string.Empty;
        }

        var task = PlaytestReportData.Read(playtest) is { } data ? runtime.Playtests.Get(data.TaskId)?.Task() : null;
        return ReportHostPlaytestPages.ReportCard(shown, playtest, task, runtime.Store.Attachments(playtest.Id), runtime.Csrf);
    }

    private static void MapPages(WebApplication app, ReportHostRuntime runtime)
    {
        app.MapGet("/admin/playtests", (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            var stats = PlaytestStats.ByTask(runtime.Store.PlaytestReports());
            return Results.Content(
                ReportHostPlaytestPages.TaskList(runtime.Playtests.All(), stats, runtime.Playtests.CatalogEnabled, runtime.Csrf), Html);
        });

        app.MapGet("/admin/playtests/new", (HttpContext ctx) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            PlaytestCatalogJson.TryReadTask(ReportHostPlaytestPages.NewTaskTemplate, out var template, PlaytestReadMode.AsWritten);
            return Results.Content(ReportHostPlaytestPages.TaskEdit(string.Empty, ReportHostPlaytestPages.NewTaskTemplate, template,
                Array.Empty<PlaytestProblem>(), stats: null, runtime.Csrf, saved: false), Html);
        });

        app.MapGet("/admin/playtests/{id}", (HttpContext ctx, string id) =>
        {
            if (runtime.GuardAdmin(ctx) is { } denied)
            {
                return denied;
            }

            if (runtime.Playtests.Get(id) is not { } record)
            {
                return Results.NotFound();
            }

            var task = record.Task();
            var stats = PlaytestStats.ByTask(runtime.Store.PlaytestReports()).GetValueOrDefault(id) ?? new PlaytestStats();
            bool saved = ctx.Request.Query["saved"].ToString() == "1";
            return Results.Content(ReportHostPlaytestPages.TaskEdit(id, PlaytestCatalogJson.WriteTask(task, indented: true), task,
                PlaytestCatalogValidator.Validate(task), stats, runtime.Csrf, saved), Html);
        });
    }

    private static void MapTaskForms(WebApplication app, ReportHostRuntime runtime)
    {
        // Saves the editing form. A refused save shows the form again with the text as typed and every finding.
        app.MapPost("/admin/playtests/save", async (HttpContext ctx) =>
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

            return SaveForm(runtime, form["stored"].ToString(), form["json"].ToString());
        });

        app.MapPost("/admin/playtests/{id}/delete", async (HttpContext ctx, string id) =>
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

            runtime.Playtests.Delete(id);
            runtime.Log.LogInformation("Playtest task {Id} deleted.", id);
            return Results.Redirect("/admin/playtests");
        });
    }

    private static void MapSwitches(WebApplication app, ReportHostRuntime runtime)
    {
        // One task on or off. Not a change of its content: the revision stays.
        app.MapPost("/admin/playtests/{id}/toggle", async (HttpContext ctx, string id) =>
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

            if (runtime.Playtests.Get(id) is not { } record)
            {
                return Results.NotFound();
            }

            var task = record.Task();
            task.Enabled = !task.Enabled;
            runtime.Playtests.Save(task, ReportHostRuntime.NowUnix(), force: true);
            return Results.Redirect("/admin/playtests");
        });

        // The kill switch of the whole catalogue.
        app.MapPost("/admin/playtests/switch", async (HttpContext ctx) =>
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

            bool enabled = form["enabled"].ToString() == "1";
            runtime.Playtests.SetCatalogEnabled(enabled);
            runtime.Log.LogInformation("Playtest catalogue switched {State}.", enabled ? "on" : "off");
            return Results.Redirect("/admin/playtests");
        });
    }

    private static void MapConfirm(WebApplication app, ReportHostRuntime runtime)
    {
        // The confirm button of a playtest report's card: status done on the pair + the thank-you to the player.
        app.MapPost("/admin/report/{id}/confirm", async (HttpContext ctx, string id) =>
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

            var done = PlaytestActions.Confirm(runtime.Store, runtime.Playtests, id, form["text"].ToString(),
                form["fixed_in_version"].ToString(), ReportHostRuntime.NowUnix());
            return done == null ? Results.NotFound() : Results.Redirect($"/admin/report/{id}");
        });
    }

    /// <summary>
    /// Validates and stores the editing form's task. The revision is the page's business, not the author's: it counts
    /// up when what players read or do changed (switching a task on or off, or moving it in the list, is not such a
    /// change). The admin page is the catalogue's truth, so its save always wins over the stored revision.
    /// </summary>
    private static IResult SaveForm(ReportHostRuntime runtime, string storedId, string json)
    {
        IResult Again(PlaytestTask? task, IReadOnlyList<PlaytestProblem> problems)
            => Results.Content(ReportHostPlaytestPages.TaskEdit(storedId, json, task, problems, stats: null, runtime.Csrf, saved: false), Html);

        if (!PlaytestCatalogJson.TryReadTask(json, out var task, PlaytestReadMode.AsWritten))
        {
            return Again(null, new[] { new PlaytestProblem(true, "json", "this is not a JSON object") });
        }

        var problems = PlaytestCatalogValidator.Validate(task);
        if (PlaytestCatalogValidator.HasErrors(problems))
        {
            return Again(task, problems);
        }

        if (runtime.Playtests.Get(task.Id) is { } stored)
        {
            bool changed = ContentOf(task) != ContentOf(stored.Task());
            task.Rev = changed ? Math.Max(task.Rev, stored.Rev + 1) : stored.Rev;
        }

        switch (runtime.Playtests.Save(task, ReportHostRuntime.NowUnix(), force: true))
        {
            case PlaytestSaveResult.ReservedId:
                return Again(task, new[] { new PlaytestProblem(true, "id", $"'{task.Id}' is a word of the inbox's routes — pick another id") });
            case PlaytestSaveResult.TooMany:
                return Again(task, new[] { new PlaytestProblem(true, "id", $"the store is full ({PlaytestStore.MaxStoredTasks} tasks) — delete one first") });
        }

        runtime.Log.LogInformation("Playtest task {Id} saved from the admin page (rev {Rev}).", task.Id, task.Rev);
        return Results.Redirect($"/admin/playtests/{task.Id}?saved=1");
    }

    /// <summary>The task as text without the fields that are bookkeeping (revision, on/off, list position) — two
    /// tasks with the same content read alike here.</summary>
    private static string ContentOf(PlaytestTask task)
    {
        int rev = task.Rev, sort = task.Sort;
        bool enabled = task.Enabled;
        task.Rev = 1;
        task.Sort = 0;
        task.Enabled = true;
        string content = PlaytestCatalogJson.WriteTask(task);
        task.Rev = rev;
        task.Sort = sort;
        task.Enabled = enabled;
        return content;
    }
}

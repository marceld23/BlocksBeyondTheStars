// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The operator's browser UI under <c>/admin</c> (#2902): the fleet page, per-world detail, the server-health JSON
/// and the form POSTs behind its buttons. Every route sits behind <see cref="WorldHostApi.GuardAdminUi"/> (Basic Auth).
/// </summary>
internal static class AdminUiEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        // ---------------- Operator admin UI (Basic Auth; the browser front-end to the API above) ----------------

        var admin = app.MapGroup("/admin");

        // Task<IResult> spelled out: see PortalEndpoints — an untyped lambda over HttpContext alone would bind to the
        // RequestDelegate overload and drop the result.
        admin.MapGet("", Task<IResult> (HttpContext ctx) => IndexAsync(s, ctx));

        admin.MapGet("/worlds/{id}", (HttpContext ctx, string id) => WorldDetail(s, ctx, id));

        admin.MapGet("/stats.json", Task<IResult> (HttpContext ctx) => StatsJsonAsync(s, ctx));

        admin.MapPost("/reports/{id:long}/close", Task<IResult> (HttpContext ctx, long id) => CloseReportAsync(s, ctx, id));

        admin.MapPost("/reset-password", Task<IResult> (HttpContext ctx) => ResetPasswordAsync(s, log, ctx));

        admin.MapPost("/ban", Task<IResult> (HttpContext ctx) => BanAsync(s, log, ctx));

        admin.MapPost("/worlds/{id}/stop", (HttpContext ctx, string id) => StopWorld(s, log, ctx, id));

        admin.MapPost("/worlds/{id}/kill", (HttpContext ctx, string id) => KillWorld(s, log, ctx, id));

        admin.MapPost("/glitch/ban", Task<IResult> (HttpContext ctx) => GlitchBanAsync(s, log, ctx));

        admin.MapPost("/announce", Task<IResult> (HttpContext ctx) => AnnounceAsync(s, log, ctx));

        admin.MapPost("/worlds/{id}/restart", Task<IResult> (HttpContext ctx, string id) => RestartWorldAsync(s, log, ctx, id));

        admin.MapPost("/worlds/{id}/wake", Task<IResult> (HttpContext ctx, string id) => WakeWorldAsync(s, ctx, id));

        admin.MapPost("/worlds/{id}/delete", Task<IResult> (HttpContext ctx, string id) => DeleteWorldAsync(s, log, ctx, id));
    }

    internal static async Task<IResult> IndexAsync(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        return await RenderAdminAsync(s, ctx);
    }

    // Gathers everything the admin page shows; live joined counts are probed in parallel for running
    // instances only (3 s HTTP cap keeps a dead instance from stalling the page).
    private static async Task<IResult> RenderAdminAsync(WorldHostServices s, HttpContext ctx)
    {
        string? lookupQuery = ctx.Request.Query["acct"].ToString() is { Length: > 0 } q ? q : null;
        var all = s.Registry.ListAllWorldsAdmin();
        var rows = await Task.WhenAll(all.Select(async entry =>
        {
            int? joined = entry.World.Status == WorldStatus.Running
                && await s.Orchestrator.ReadInstanceStatusAsync(entry.World) is { } json
                ? WorldHostApi.ParseJoinedPlayers(json)
                : null;
            return new AdminWorldRow(entry.World, entry.OwnerName, joined);
        }));

        return Results.Content(WorldHostAdminPages.Index(
            s.Config, rows, s.Registry.ListOpenReports(), s.Registry.ListBannedAccounts(),
            lookupQuery is null ? null : s.Registry.FindAccountByName(lookupQuery), lookupQuery,
            s.Registry.ListGlitchGuests(), s.Registry.ListGlitchBans(),
            ctx.Request.Query["notice"].ToString()),
            "text/html; charset=utf-8");
    }

    // Per-world detail (#489): players, structures and build hotspots, read straight from the world save.
    // No instance call — the saves are bind-mounted where this process can read them (WorldInspector).
    internal static IResult WorldDetail(WorldHostServices s, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        string ownerName = s.Registry.ListAllWorldsAdmin()
            .FirstOrDefault(e => e.World.Id == world.Id).OwnerName ?? "—";
        var insight = WorldInspector.Read(s.Config, world.Id);
        return Results.Content(
            WorldHostAdminPages.WorldDetail(s.Config, world, ownerName, insight), "text/html; charset=utf-8");
    }

    // Server-health JSON behind the admin page's "Server health" card (closes #244). Fetched by the card
    // AFTER the page renders, because `docker stats` samples for ~1-2 s and must not stall the page.
    internal static async Task<IResult> StatsJsonAsync(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var host = HostStats.Read(s.Config.WorldsDir);
        var containers = await Task.Run(() => s.Launcher.ContainerStats());
        var counts = s.Registry.CountForMetrics();
        int online = await WorldHostApi.CountPlayersOnlineAsync(s.Registry, s.Orchestrator);
        return Results.Json(new
        {
            host = new
            {
                load1 = host.Load1,
                load5 = host.Load5,
                load15 = host.Load15,
                cores = host.Cores,
                memTotalKb = host.MemTotalKb,
                memAvailableKb = host.MemAvailableKb,
                diskTotalBytes = host.DiskTotalBytes,
                diskFreeBytes = host.DiskFreeBytes,
            },
            containers = containers.Select(c => new
            {
                name = c.Name,
                cpuPercent = c.CpuPercent,
                memUsedBytes = c.MemUsedBytes,
                memLimitBytes = c.MemLimitBytes,
            }),
            fleet = new
            {
                accounts = counts.Accounts,
                reportsOpen = counts.OpenReports,
                playersOnline = online,
                worlds = counts.WorldsByStatus.Select(s => new { status = s.Status, count = s.Count }),
            },
        });
    }

    internal static async Task<IResult> CloseReportAsync(WorldHostServices s, HttpContext ctx, long id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        s.Registry.CloseReport(id, form["status"].ToString() is "dismissed" ? "dismissed" : "reviewed");
        return Results.Redirect("/admin");
    }

    // Admin-UI twin of /api/admin/reset-password. Renders the outcome page directly instead of
    // redirecting — the one-time temp password must never travel in a URL (logs, browser history).
    internal static async Task<IResult> ResetPasswordAsync(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        string accountId = form["accountId"].ToString();
        var account = s.Registry.GetAccount(accountId);
        var (ok, temp) = account is null ? (false, string.Empty) : s.Registry.AdminResetPassword(accountId);
        if (!ok)
        {
            log.LogInformation("Admin UI: password reset refused (unknown or operator account).");
            return Results.Redirect("/admin?notice=operator_reset");
        }

        // Name, not id (CodeQL cs/cleartext-storage): account ids are stable registry references and
        // deliberately stay out of log files — same rule as the signup log line.
        log.LogInformation("Admin UI: password of account '{Name}' reset.", WorldHostApi.LogSafe(account!.Name));
        return Results.Content(WorldHostAdminPages.ResetPasswordResult(s.Config, account!.Name, temp), "text/html; charset=utf-8");
    }

    internal static async Task<IResult> BanAsync(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        string accountId = form["accountId"].ToString();
        bool banned = form["banned"].ToString() == "true";
        string reason = form["reason"].ToString();
        string reasonCode = form["reasonCode"].ToString();
        _ = int.TryParse(form["days"].ToString(), out int days); // empty/garbage = 0 = until an operator lifts it
        if (accountId.Length > 0)
        {
            if (!s.Registry.SetBanned(accountId, banned, reason, reasonCode, System.Math.Clamp(days, 0, 3650)))
            {
                log.LogInformation("Admin UI: ban refused — operator accounts cannot be banned.");
                return Results.Redirect("/admin?notice=operator");
            }

            log.LogInformation("Admin UI: account {Action} for {Days} day(s) ({Reason}).",
                banned ? "BANNED" : "unbanned", days, WorldHostApi.LogSafe(reason));
            if (banned)
            {
                // A ban that leaves the offender playing until they feel like logging off is not a ban.
                int kicked = await s.Orchestrator.KickAccountEverywhereAsync(accountId);
                log.LogInformation("Admin UI: ban kicked the account out of {Count} running world(s).", kicked);
            }
        }

        return Results.Redirect("/admin");
    }

    // Stop an instance. The `docker stop` runs OFF the request path (issue #519): it waits out the drain +
    // save and cannot be allowed to hold the admin request for two minutes — the old inline version returned
    // so late that the browser had already given up, which read as "stop does nothing".
    internal static IResult StopWorld(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        if (HostRegistry.IsValidWorldId(id) && s.Registry.GetWorld(id) is { } world)
        {
            s.Orchestrator.StopWorldInBackground(world);
            log.LogInformation("Admin UI: world {Id} stopping (draining in the background).", world.Id);
        }

        return Results.Redirect("/admin?notice=stopping");
    }

    // Emergency hard kill: SIGKILL, no drain, no save. For an instance that will not go down on its own —
    // everything since the last autosave is lost, so the UI states that plainly and asks for a confirmation.
    internal static IResult KillWorld(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        if (HostRegistry.IsValidWorldId(id) && s.Registry.GetWorld(id) is { } world)
        {
            s.Orchestrator.KillWorld(world);
            log.LogWarning("Admin UI: world {Id} HARD KILLED (no save).", world.Id);
        }

        return Results.Redirect("/admin?notice=killed");
    }

    // Ban/unban a glitch.fun install id (the arcade channel's ban lever — guests have no account). A ban
    // takes effect on the guest's NEXT session grant and next heartbeat (the relay answers 403, which the
    // client treats as "stop the game"), so no instance restart is involved.
    internal static async Task<IResult> GlitchBanAsync(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        string installId = form["installId"].ToString().Trim();
        bool banned = form["banned"].ToString() == "true";
        if (installId.Length > 0)
        {
            s.Registry.SetGlitchBanned(installId, banned, form["reason"].ToString(), form["playerName"].ToString());
            log.LogInformation("Admin UI: glitch install {Action} ({Reason}).", banned ? "BANNED" : "unbanned", WorldHostApi.LogSafe(form["reason"].ToString()));
        }

        return Results.Redirect("/admin");
    }

    // The /admin "Announce" card (#249): info message, scheduled restart or cancel — per world or fleet-wide.
    internal static async Task<IResult> AnnounceAsync(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        string message = form["message"].ToString().Trim();
        string worldIdRaw = form["worldId"].ToString().Trim();
        string? worldId = worldIdRaw.Length > 0 && HostRegistry.IsValidWorldId(worldIdRaw) ? worldIdRaw : null;
        string action = form["action"].ToString();

        byte kind;
        int seconds = -1;
        if (action == "cancel")
        {
            kind = 2;
        }
        else if (int.TryParse(form["minutes"].ToString(), out int minutes) && minutes > 0)
        {
            kind = 1;
            seconds = minutes * 60;
        }
        else
        {
            kind = 0;
            if (message.Length == 0)
            {
                return Results.Redirect("/admin"); // nothing to say — an info announce needs text
            }
        }

        var (reached, targets) = await s.Orchestrator.AnnounceAsync(kind, message, seconds, worldId);
        log.LogInformation("Admin UI: announce kind {Kind} to {Target} — reached {Reached}/{Targets}.",
            kind, worldId is null ? "fleet" : WorldHostApi.LogSafe(worldId), reached, targets);
        return Results.Redirect("/admin");
    }

    // Graceful per-world restart (#249): warn the players (10-minute countdown banner), then the instance
    // stops itself through the same drain+save path — the humane sibling of the immediate Stop button.
    internal static async Task<IResult> RestartWorldAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        if (HostRegistry.IsValidWorldId(id) && s.Registry.GetWorld(id) is { } world)
        {
            bool reached = await s.Orchestrator.AnnounceInstanceAsync(world, kind: 1, text: null, seconds: 600);
            log.LogInformation("Admin UI: world {Id} graceful restart {Result}.", world.Id, reached ? "scheduled (10 min)" : "NOT reachable");
        }

        return Results.Redirect("/admin");
    }

    internal static async Task<IResult> WakeWorldAsync(WorldHostServices s, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        if (HostRegistry.IsValidWorldId(id))
        {
            // errors surface as status on the page; the operator's wake also retries a world whose save the current
            // image refused as too new (#2464) — e.g. after re-pulling a floating image tag
            await s.Orchestrator.EnsureRunningAsync(id, retryRefusedSave: true);
        }

        return Results.Redirect("/admin");
    }

    // Delete a world from the fleet overview. Guarded by a typed confirmation (the world's display name)
    // checked HERE rather than in the browser — the delete button sits next to `stop` in a narrow table cell
    // and there is no undo. `purge=true` erases the saves as well; the plain delete keeps them on disk.
    // Deleting a RUNNING world is allowed and — unlike the stop button — deliberately still BLOCKS while
    // `docker stop` drains it: the container object is removed and the registry row dropped right after, so
    // the stop must be finished before we get there. On a mismatch nothing happens and the page says so.
    internal static async Task<IResult> DeleteWorldAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.GuardAdminUi(s.Config, ctx) is { } denied)
        {
            return denied;
        }

        var form = await ctx.Request.ReadFormAsync();
        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.Redirect("/admin");
        }

        if (!string.Equals(form["confirm"].ToString().Trim(), world.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("Admin UI: delete of world {Id} refused — confirmation name did not match.", world.Id);
            return Results.Redirect("/admin?notice=confirm");
        }

        bool purge = form["purge"].ToString() == "true";
        WorldHostApi.NotifyWorldDeleted(s.Registry, world, form["reason"].ToString());
        s.Orchestrator.DeleteWorld(world, purge);
        log.LogInformation("Admin UI: world '{Name}' ({Id}) deleted — saves {Fate}.",
            WorldHostApi.LogSafe(world.DisplayName), world.Id, purge ? "PURGED" : "retained");
        return Results.Redirect($"/admin?notice={(purge ? "purged" : "deleted")}");
    }
}

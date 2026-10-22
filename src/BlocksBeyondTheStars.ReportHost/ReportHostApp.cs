// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Notifications;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The inbox's HTTP surface — <see cref="Create"/> wires the <see cref="WebApplication"/> the entry point runs
/// and the tests start in-process on a loopback port (#1352): limiters, CSRF token, start-up store maintenance,
/// the player-route CORS middleware, <c>/healthz</c> and <c>/</c>, then the three route groups
/// (<see cref="ReportHostPlayerEndpoints"/>, <see cref="ReportHostApiEndpoints"/>,
/// <see cref="ReportHostAdminEndpoints"/>) over one <see cref="ReportHostRuntime"/>. Program.cs only reads the
/// environment, opens the store and calls <see cref="Create"/>.
/// </summary>
public static class ReportHostApp
{
    /// <summary>Builds the configured app (not yet started). <paramref name="store"/> stays owned by the
    /// caller; <paramref name="args"/> are the command-line arguments handed to the host builder.</summary>
    public static WebApplication Create(ReportHostConfig config, ReportStore store, AdminNotifier notifier, string[] args)
    {
        // Per-IP fixed-window limiter for report submission only. The reply routes have their own
        // limiter keyed by reply key (see ReportHostRuntime.GuardReplyKey) so a LAN class behind one NAT
        // polling for answers cannot exhaust the budget a real F1 report from the same network needs (#1352).
        var limiter = new IngestRateLimiter(config.IngestPerMinute);
        var replyLimiter = new IngestRateLimiter(config.ReplyPerMinute);

        // One random token per process, rendered into every admin form and required on every admin form
        // POST (#1369): Basic credentials ride along on any cross-site request the browser makes, and since
        // #1327 a forged form submit could put text in front of a player.
        var csrf = new AdminCsrf();

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = config.MaxBodyBytes);

        var app = builder.Build();
        var log = app.Logger;

        int prunedAtStart = store.Prune(config.RetentionDays, ReportHostRuntime.NowUnix());
        prunedAtStart += store.PruneTextureSubmissions(config.TextureRetentionDays, ReportHostRuntime.NowUnix());
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

        // Pairs triaged apart before actions covered both halves (#1380) settle on their most advanced
        // state. Idempotent — a no-op once repaired, and every route's action (ReportPairActions) keeps pairs in
        // step from here on.
        int synced = store.SyncPairStatuses(ReportHostPages.GroupDuplicates);
        if (synced > 0)
        {
            log.LogInformation("Aligned the status of {Count} report row(s) with their paired half.", synced);
        }

        log.LogInformation(
            "ReportHost on {Bind}:{Port} — ingest {Ingest}, read API {Read}, admin UI {Admin}, retention {Retention}, pruned {Pruned} at start.",
            config.BindAddress, config.Port,
            config.WriteKey.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_WRITE_KEY)",
            config.ReadKey.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_READ_KEY)",
            config.AdminUser.Length > 0 && config.AdminPassword.Length > 0 ? "ON" : "OFF (no BBS_REPORTS_ADMIN_USER/PASSWORD)",
            config.RetentionDays > 0 ? config.RetentionDays + "d" : "forever",
            prunedAtStart);

        var runtime = new ReportHostRuntime(config, store, notifier, log, limiter, replyLimiter, csrf);

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

        ReportHostPlayerEndpoints.Map(app, runtime);
        ReportHostApiEndpoints.Map(app, runtime);
        ReportHostAdminEndpoints.Map(app, runtime);

        return app;
    }
}

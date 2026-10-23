// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Microsoft.AspNetCore.Http.Features;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The world routes under <c>/api/worlds</c> (#2902), all for a signed-in caller: the own, public and operator world
/// lists, creation, the join grant, the owner's settings and moderation (password, listing, bans, kick, stop,
/// delete) and the save upload/export round trip.
/// </summary>
internal static class WorldEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        // ---------------- Worlds ----------------

        var worlds = app.MapGroup("/api/worlds");

        worlds.MapGet("", (HttpContext ctx) => List(s, ctx));

        worlds.MapGet("/public", (HttpContext ctx) => ListPublic(s, ctx));

        worlds.MapGet("/all", (HttpContext ctx) => ListAll(s, ctx));

        worlds.MapPost("", (HttpContext ctx, CreateWorldRequest req) => Create(s, log, ctx, req));

        worlds.MapPost("/{id}/join", Task<IResult> (HttpContext ctx, string id, JoinRequestDto req) => JoinAsync(s, log, ctx, id, req));

        worlds.MapPost("/{id}/password", (HttpContext ctx, string id, WorldPasswordRequest req) => SetPassword(s, log, ctx, id, req));

        worlds.MapPost("/{id}/visibility", (HttpContext ctx, string id, WorldVisibilityRequest req) => SetVisibility(s, log, ctx, id, req));

        // ---- Owner moderation (#497): every world owner gets the lever the fleet operator has, for their own
        // world only. The list is enforced at the join grant (WorldOrchestrator), so it holds for every client. ----

        worlds.MapGet("/{id}/bans", (HttpContext ctx, string id) => ListBans(s, ctx, id));

        worlds.MapPost("/{id}/bans", Task<IResult> (HttpContext ctx, string id, WorldBanRequest req) => AddBanAsync(s, log, ctx, id, req));

        worlds.MapDelete("/{id}/bans/{banId:long}", (HttpContext ctx, string id, long banId) => RemoveBan(s, ctx, id, banId));

        worlds.MapPost("/{id}/kick", Task<IResult> (HttpContext ctx, string id, WorldKickRequest req) => KickAsync(s, log, ctx, id, req));

        worlds.MapPost("/{id}/stop", (HttpContext ctx, string id) => Stop(s, ctx, id));

        worlds.MapDelete("/{id}", (HttpContext ctx, string id) => Delete(s, log, ctx, id));

        // ---------------- Save upload / export (the SP↔hosted round-trip) ----------------

        worlds.MapPost("/{id}/save", Task<IResult> (HttpContext ctx, string id) => UploadSaveAsync(s, log, ctx, id));

        worlds.MapGet("/{id}/save", (HttpContext ctx, string id) => DownloadSave(s, ctx, id));
    }

    internal static IResult List(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        // needsNewerServer (#2464): the current server image refused the world's save as written by a newer build.
        var worlds = s.Registry.ListWorlds(account.Id)
            .Select(w => new { id = w.Id, name = w.DisplayName, status = w.Status, subdomain = w.Subdomain, hasPassword = w.HasPassword, isPublic = w.IsPublic, needsNewerServer = s.Orchestrator.NeedsNewerServer(w) });
        return Results.Json(new { worlds });
    }

    // Public world browser: worlds their owners opted into listing. Requires a signed-in account (joining
    // needs one anyway) but is NOT owner-scoped — this is the one cross-account world listing players can see.
    // Every listed world is password-gated by construction (see HostRegistry.SetWorldVisibility), so the join
    // still requires the owner-shared password; we surface only name + status, never owner identity.
    internal static IResult ListPublic(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        var worlds = s.Registry.ListPublicWorlds()
            .Select(w => new { id = w.Id, name = w.DisplayName, status = w.Status, hasPassword = w.HasPassword, needsNewerServer = s.Orchestrator.NeedsNewerServer(w) });
        return Results.Json(new { worlds });
    }

    // Operator world browser (issue #495): EVERY world on the fleet, including private ones — the reach the
    // invisible observer needs, since kids mostly play on private/password worlds. Developer accounts only
    // (claimable solely with the secret ReservedClaimCode), and unlike /api/worlds/public this one may name the
    // owner: the operator moderates, and "whose world is this" is the first question moderation asks. Regular
    // accounts get 403 — the client probes this endpoint and simply hides the operator section on failure.
    internal static IResult ListAll(WorldHostServices s, HttpContext ctx)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!account.IsDeveloper)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var worlds = s.Registry.ListAllWorldsAdmin()
            .Select(e => new
            {
                id = e.World.Id,
                name = e.World.DisplayName,
                status = e.World.Status,
                owner = e.OwnerName,
                isPublic = e.World.IsPublic,
                hasPassword = e.World.HasPassword,
                needsNewerServer = s.Orchestrator.NeedsNewerServer(e.World),
            });
        return Results.Json(new { worlds });
    }

    internal static IResult Create(WorldHostServices s, ILogger log, HttpContext ctx, CreateWorldRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (WorldHostApi.GuardAccount(s.Config, account) is { } blocked)
        {
            return blocked;
        }

        // Same screening as signup (#938): a blocked world name pings the operator, a watch hit flags it.
        var nameScreen = s.Registry.ScreenName(req.Name);
        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Block)
        {
            s.Metrics.NameBlocked();
            log.LogWarning("World name blocked for account {Account} (matched '{Term}').", WorldHostApi.LogSafe(account.Name), nameScreen.MatchedTerm);
            s.Notifier.Post("Blocked world name", $"Account '{account.Name}' tried to create a world whose name matched blocked term '{nameScreen.MatchedTerm}'.", "no_entry");
            return WorldHostApi.ApiError("Please choose a different world name.");
        }

        var (ok, error, world) = s.Registry.CreateWorld(account.Id, req.Name, req.Password);
        if (!ok)
        {
            return WorldHostApi.ApiError(error);
        }

        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Watch)
        {
            s.Metrics.NameFlagged();
            log.LogWarning("World name '{Name}' flagged (matched watch term '{Term}'); creation allowed.", WorldHostApi.LogSafe(world!.DisplayName), nameScreen.MatchedTerm);
            s.Notifier.Post("World name flagged", $"New world '{req.Name}' (owner '{account.Name}') matched watch-list term '{nameScreen.MatchedTerm}'. Creation was allowed — review manually.", "triangular_flag_on_post");
        }

        log.LogInformation("World '{Name}' ({Id}) created by {Account}.", WorldHostApi.LogSafe(world!.DisplayName), world.Id, WorldHostApi.LogSafe(account.Name));
        return Results.Json(new { id = world.Id, name = world.DisplayName, status = world.Status, subdomain = world.Subdomain, hasPassword = world.HasPassword });
    }

    internal static async Task<IResult> JoinAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id, JoinRequestDto req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is null)
        {
            return Results.NotFound();
        }

        // Name screening visibility (#938): the ORCHESTRATOR stays the enforcement point (unchanged player
        // responses); this only gives the operator metrics + a ping for what used to happen silently.
        var nameScreen = s.Registry.ScreenName(req.PlayerName);
        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Block)
        {
            s.Metrics.NameBlocked();
            log.LogWarning("Join name blocked on world {Id} for account {Account} (matched '{Term}').", WorldHostApi.LogSafe(id), WorldHostApi.LogSafe(account.Name), nameScreen.MatchedTerm);
            s.Notifier.Post("Blocked player name", $"Account '{account.Name}' tried to join world {id} under a name matching blocked term '{nameScreen.MatchedTerm}'.", "no_entry");
        }

        // The join grant is the access-control choke point: the orchestrator enforces ban/terms AND the
        // creator-set world password (#250/#251) before minting a token; the instance only admits valid
        // tokens. The grant names the caller's account, so the instance can attribute every admitted player.
        var (grant, error) = await s.Orchestrator.JoinAsync(id, account, req.PlayerName, req.Password);
        if (grant is null)
        {
            int status = WorldHostApi.CodeFor(error) switch
            {
                "password_required" or "wrong_password" => StatusCodes.Status403Forbidden,
                "too_many_attempts" => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
            return WorldHostApi.ApiError(error, status);
        }

        if (nameScreen.Verdict == BlocksBeyondTheStars.Shared.Moderation.NameVerdict.Watch)
        {
            s.Metrics.NameFlagged();
            log.LogWarning("Player name '{Name}' flagged on world {Id} (matched watch term '{Term}'); join allowed.", WorldHostApi.LogSafe(req.PlayerName), WorldHostApi.LogSafe(id), nameScreen.MatchedTerm);
            s.Notifier.Post("Player name flagged", $"Player '{req.PlayerName}' (account '{account.Name}') joined world {id} under a name matching watch-list term '{nameScreen.MatchedTerm}'. Review manually.", "triangular_flag_on_post");
        }

        return Results.Json(grant);
    }

    // Owner-only: set/change (4-24 chars) or remove (empty) the world's join password (#250). Applies to
    // the NEXT join grant immediately — no instance restart involved (the gate lives at token issuance).
    internal static IResult SetPassword(WorldHostServices s, ILogger log, HttpContext ctx, string id, WorldPasswordRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        var (ok, error) = s.Registry.SetWorldPassword(world.Id, req.Password);
        if (!ok)
        {
            return WorldHostApi.ApiError(error);
        }

        bool hasPassword = !string.IsNullOrEmpty(req.Password);
        log.LogInformation("World {Id}: join password {Action} by its owner.", world.Id, hasPassword ? "set" : "removed");
        // Removing the password also un-lists a public world (SetWorldPassword enforces it) — tell the client
        // so its toggle reflects the new state without a reload.
        return Results.Json(new { hasPassword, isPublic = hasPassword && s.Registry.GetWorld(world.Id)?.IsPublic == true });
    }

    // Owner-only: opt a world in/out of the public browser. Listing requires a join password first (enforced
    // in the registry) — public worlds are always password-gated.
    internal static IResult SetVisibility(WorldHostServices s, ILogger log, HttpContext ctx, string id, WorldVisibilityRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        var (ok, error) = s.Registry.SetWorldVisibility(world.Id, req.Public);
        if (!ok)
        {
            return WorldHostApi.ApiError(error);
        }

        log.LogInformation("World {Id}: public listing {Action} by its owner.", world.Id, req.Public ? "enabled" : "disabled");
        return Results.Json(new { isPublic = req.Public });
    }

    // Owner-only: the world's ban list plus the recent visitors to pick from (nobody remembers account ids).
    internal static IResult ListBans(WorldHostServices s, HttpContext ctx, string id)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        return Results.Json(new
        {
            bans = s.Registry.ListWorldBans(world.Id)
                .Select(b => new { id = b.Id, playerName = b.PlayerName, accountId = b.AccountId, reason = b.Reason, created = b.CreatedUnix }),
            // The owner's own account is never offered as a ban target — locking yourself out of your world
            // would be a support case, not a feature.
            visitors = s.Registry.ListWorldVisitors(world.Id)
                .Where(v => v.AccountId != account.Id)
                .Select(v => new { playerName = v.PlayerName, accountId = v.AccountId, lastSeen = v.LastSeenUnix }),
        });
    }

    // Owner-only: bar a player from this world (and, unless asked otherwise, end their session right now).
    internal static async Task<IResult> AddBanAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id, WorldBanRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        string playerName = (req.PlayerName ?? string.Empty).Trim();
        string targetAccount = (req.AccountId ?? string.Empty).Trim();
        if (playerName.Length > 24 || playerName.Any(char.IsControl))
        {
            return WorldHostApi.ApiError("Player name must be 1-24 printable characters.");
        }

        if (targetAccount == account.Id)
        {
            return WorldHostApi.ApiError("You cannot block yourself from your own world.");
        }

        // The fleet operator is not blockable — a world owner must not be able to switch off oversight of
        // their own world (#495). Answered as "reserved", which is what the fleet-admin names already are
        // everywhere else (signup, join), so this leaks nothing that isn't public behaviour anyway.
        if (WorldHostApi.IsOperatorTarget(s, targetAccount, playerName))
        {
            return WorldHostApi.ApiError("This player name is reserved.", StatusCodes.Status403Forbidden);
        }

        if (!s.Registry.AddWorldBan(world.Id, targetAccount, playerName, req.Reason ?? string.Empty))
        {
            return WorldHostApi.ApiError("Player name must be 1-24 printable characters.");
        }

        log.LogInformation("World {Id}: owner blocked a player.", world.Id);
        bool kicked = false;
        if (req.Kick && playerName.Length > 0)
        {
            // A block only decides the next join — without this the blocked player keeps playing.
            kicked = await s.Orchestrator.KickInstanceAsync(world, playerName,
                string.IsNullOrWhiteSpace(req.Reason) ? "@ui.kick.world_ban" : req.Reason);
        }

        return Results.Json(new { ok = true, kicked });
    }

    // Owner-only: lift a block. The world id is part of the lookup, so a foreign ban id cannot be guessed away.
    internal static IResult RemoveBan(WorldHostServices s, HttpContext ctx, string id, long banId)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        s.Registry.RemoveWorldBan(world.Id, banId);
        return Results.Ok();
    }

    // Owner-only: end one player's session on this world without a lasting block ("go cool off").
    internal static async Task<IResult> KickAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id, WorldKickRequest req)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        string playerName = (req.PlayerName ?? string.Empty).Trim();
        if (playerName.Length is < 1 or > 24 || playerName.Any(char.IsControl))
        {
            return WorldHostApi.ApiError("Player name must be 1-24 printable characters.");
        }

        if (WorldHostApi.IsOperatorTarget(s, accountId: string.Empty, playerName))
        {
            return WorldHostApi.ApiError("This player name is reserved.", StatusCodes.Status403Forbidden);
        }

        bool kicked = await s.Orchestrator.KickInstanceAsync(world, playerName,
            string.IsNullOrWhiteSpace(req.Reason) ? "@ui.kick.by_owner" : req.Reason);
        log.LogInformation("World {Id}: owner kicked a player (delivered: {Delivered}).", world.Id, kicked);
        return Results.Json(new { kicked });
    }

    internal static IResult Stop(WorldHostServices s, HttpContext ctx, string id)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        // Off the request path for the same reason as the admin button (issue #519): the owner's portal call
        // must not sit through the instance's drain + save.
        s.Orchestrator.StopWorldInBackground(world);
        return Results.Ok();
    }

    internal static IResult Delete(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        // The saves directory is intentionally NOT removed — an operator can still recover/export it (and
        // purge it from /admin); automated retention is Phase 3.
        s.Orchestrator.DeleteWorld(world, purgeSaves: false);
        log.LogInformation("World '{Name}' ({Id}) deleted by {Account} (saves directory retained).", WorldHostApi.LogSafe(world.DisplayName), world.Id, WorldHostApi.LogSafe(account.Name));
        return Results.Ok();
    }

    internal static async Task<IResult> UploadSaveAsync(WorldHostServices s, ILogger log, HttpContext ctx, string id)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (WorldHostApi.GuardAccount(s.Config, account) is { } blocked)
        {
            return blocked;
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        if (!s.UploadLimit.TryPass(account.Id))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        // Only while stopped: the instance owns the file when it runs, and a mid-write copy would corrupt.
        if (s.Registry.GetWorld(id)!.Status != WorldStatus.Stopped || s.Launcher.IsRunning(world.ContainerId))
        {
            return WorldHostApi.ApiError("Stop the world before uploading a save.");
        }

        // Stream to a temp file with a hard size cap, then validate BEFORE it replaces anything. The cap is the portal's
        // own (UploadMaxBytes): Kestrel's default request-body limit of 30,000,000 bytes sits below the 50 MB default and
        // cut every save between the two with a bare 413 before this code saw it (#2922), so this route sets Kestrel's
        // limit to the cap. A body Kestrel still refuses — declared or streamed past the cap — gets the portal's answer.
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = s.Config.UploadMaxBytes;
        }

        string tmp = Path.Combine(Path.GetTempPath(), $"bbs-upload-{world.Id}-{Guid.NewGuid():N}.db");
        try
        {
            await using (var file = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                try
                {
                    while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
                    {
                        total += read;
                        if (total > s.Config.UploadMaxBytes)
                        {
                            return UploadTooLarge(s);
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read));
                    }
                }
                catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    return UploadTooLarge(s);
                }

                if (total == 0)
                {
                    return WorldHostApi.ApiError("Empty upload.");
                }
            }

            var (ok, error) = SavePaths.ValidateUploadedSave(tmp);
            if (!ok)
            {
                return WorldHostApi.ApiError(error);
            }

            // Keep exactly one previous generation as a manual-recovery net, then move the upload into place.
            string target = SavePaths.WorldDbPath(s.Config, world.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.Copy(target, target + ".bak", overwrite: true);
            }

            File.Move(tmp, target, overwrite: true);
            s.Registry.ClearSaveTooNew(world.Id); // #2464: a different save — whether the image opens it is a new question
            log.LogInformation("World '{Name}' ({Id}): save uploaded by {Account}.", WorldHostApi.LogSafe(world.DisplayName), world.Id, WorldHostApi.LogSafe(account.Name));
            return Results.Ok();
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    // The answer to a save over UploadMaxBytes, whichever noticed first: Kestrel's body limit or the counted copy.
    private static IResult UploadTooLarge(WorldHostServices s)
        => WorldHostApi.ApiError($"Save exceeds the {s.Config.UploadMaxBytes / (1024 * 1024)} MB upload limit.");

    internal static IResult DownloadSave(WorldHostServices s, HttpContext ctx, string id)
    {
        if (WorldHostApi.Caller(s.Registry, ctx) is not { } account)
        {
            return Results.Unauthorized();
        }

        if (!HostRegistry.IsValidWorldId(id) || s.Registry.GetWorld(id) is not { } world)
        {
            return Results.NotFound();
        }

        if (world.OwnerAccountId != account.Id)
        {
            return WorldHostApi.NotWorldOwner();
        }

        if (s.Registry.GetWorld(id)!.Status != WorldStatus.Stopped || s.Launcher.IsRunning(world.ContainerId))
        {
            return WorldHostApi.ApiError("Stop the world before downloading its save.");
        }

        string path = SavePaths.WorldDbPath(s.Config, world.Id);
        if (!File.Exists(path))
        {
            return WorldHostApi.ApiError("This world has no save yet (it was never started).");
        }

        return Results.File(path, "application/octet-stream", $"{world.Id}-world.db");
    }
}

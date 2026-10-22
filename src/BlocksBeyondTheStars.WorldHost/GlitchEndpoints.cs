// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

// ---------------- glitch.fun arcade gateway ----------------
// The Glitch-hosted WebGL build calls these two endpoints cross-origin, so they are the ONLY /api
// routes with CORS — echoing exactly the configured Glitch origins, never *. Everything else about
// the arcade lives in GlitchGateway; disabled deployments answer 404 (and never leak why).

/// <summary>The <c>/api/glitch/*</c> routes (#2902): the arcade session grant, the heartbeat relay and the
/// browser-singleplayer cloud-save relay, all behind <see cref="ApplyGlitchCors"/>.</summary>
internal static class GlitchEndpoints
{
    /// <summary>Maps this group's routes onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        var group = app.MapGroup("/api/glitch");

        // The JSON POSTs from the Glitch origin always preflight — answer it for every /api/glitch route.
        group.MapMethods("/{**rest}", new[] { "OPTIONS" }, (HttpContext ctx) => Preflight(s, ctx));

        group.MapPost("/session", Task<IResult> (HttpContext ctx, GlitchSessionRequest req) => SessionAsync(s, log, ctx, req));

        group.MapPost("/heartbeat", Task<IResult> (HttpContext ctx, GlitchHeartbeatRequest req) => HeartbeatAsync(s, ctx, req));

        // Browser-singleplayer cloud saves, relayed to Glitch Cloud Save slot 0 (title token stays here;
        // checksum computed server-side over the decoded bytes; 10 MB + rate caps enforced before Glitch).
        group.MapGet("/save", Task<IResult> (HttpContext ctx, string? installId) => LoadSaveAsync(s, ctx, installId));

        group.MapPost("/save", Task<IResult> (HttpContext ctx, GlitchSaveStoreRequest req) => StoreSaveAsync(s, ctx, req));

        group.MapPost("/save/resolve", Task<IResult> (HttpContext ctx, GlitchSaveResolveRequest req) => ResolveSaveAsync(s, ctx, req));
    }

    internal static void ApplyGlitchCors(GlitchGateway glitch, HttpContext ctx)
    {
        if (glitch.ResolveCorsOrigin(ctx.Request.Headers.Origin) is { } origin)
        {
            ctx.Response.Headers.AccessControlAllowOrigin = origin;
            ctx.Response.Headers.Vary = "Origin";
            ctx.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
            ctx.Response.Headers.AccessControlAllowHeaders = "Content-Type";
            ctx.Response.Headers.AccessControlMaxAge = "3600";
        }
    }

    internal static IResult Preflight(WorldHostServices s, HttpContext ctx)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        return Results.NoContent();
    }

    internal static async Task<IResult> SessionAsync(WorldHostServices s, ILogger log, HttpContext ctx, GlitchSessionRequest req)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        if (!s.Glitch.Enabled)
        {
            return Results.NotFound();
        }

        if (!s.GlitchSessionLimit.TryPass(WorldHostApi.CallerIp(ctx)))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        var result = await s.Glitch.SessionAsync(req.InstallId, req.PlayerName);
        if (!result.Ok)
        {
            int status = WorldHostApi.CodeFor(result.Error) switch
            {
                "banned" or "glitch_invalid_install" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status503ServiceUnavailable, // glitch_full, no_capacity, wake failures
            };
            return WorldHostApi.ApiError(result.Error, status);
        }

        log.LogInformation("glitch.fun session: {Player} → arcade world {World}.", WorldHostApi.LogSafe(result.PlayerName), result.WorldId);
        return Results.Json(new
        {
            worldId = result.WorldId,
            worldName = result.WorldName,
            playerName = result.PlayerName,
            wssUrl = result.WssUrl,
            joinToken = result.JoinToken,
            tokenExpiresUnix = result.TokenExpiresUnix,
            nameToken = result.NameToken,
        });
    }

    internal static async Task<IResult> HeartbeatAsync(WorldHostServices s, HttpContext ctx, GlitchHeartbeatRequest req)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        var (status, body) = await s.Glitch.RelayHeartbeatAsync(req.InstallId, req.SessionId, req.Platform, req.GameVersion);
        return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
    }

    internal static async Task<IResult> LoadSaveAsync(WorldHostServices s, HttpContext ctx, string? installId)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        var (status, body) = await s.Glitch.LoadSaveAsync(installId);
        return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
    }

    internal static async Task<IResult> StoreSaveAsync(WorldHostServices s, HttpContext ctx, GlitchSaveStoreRequest req)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        var (status, body) = await s.Glitch.StoreSaveAsync(req.InstallId, req.Payload, req.BaseVersion);
        return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
    }

    internal static async Task<IResult> ResolveSaveAsync(WorldHostServices s, HttpContext ctx, GlitchSaveResolveRequest req)
    {
        ApplyGlitchCors(s.Glitch, ctx);
        var (status, body) = await s.Glitch.ResolveSaveAsync(req.InstallId, req.SaveId, req.ConflictId, req.Choice);
        return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
    }
}

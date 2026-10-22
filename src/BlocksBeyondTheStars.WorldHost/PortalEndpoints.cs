// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The root routes (#2902): health and metrics, the public stats, the server-rendered portal pages, the browser-play
/// entry (<c>/play</c> plus its static files) and the Caddy on-demand TLS gate.
/// </summary>
internal static class PortalEndpoints
{
    /// <summary>Maps this group's routes (and the /play static-files middleware) onto <paramref name="app"/>.</summary>
    internal static void Map(WebApplication app, WorldHostServices s, ILogger log)
    {
        app.MapGet("/healthz", () => Results.Text("ok\n"));

        // Prometheus scrape (Phase 3). Reachable only on the loopback bind — Caddy deliberately does not
        // route /metrics, so fleet numbers never leak publicly.
        app.MapGet("/metrics", () => Results.Text(s.Metrics.Render(s.Registry), "text/plain; version=0.0.4; charset=utf-8"));

        // Task-returning handlers spell out the lambda's return type: a lambda over HttpContext alone that returns a
        // Task binds to the RequestDelegate overload, which throws the IResult away and answers an empty 200.
        app.MapGet("/api/stats", Task<IResult> (HttpContext ctx) => StatsAsync(s, log, ctx));

        // ---------------- Portal pages (server-rendered shells; the JS talks to /api with a Bearer session) ----------------

        app.MapGet("/", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Landing(s.Config, WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8"));
        app.MapGet("/worlds", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Worlds(s.Config, WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8"));
        app.MapGet("/rules", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Rules(s.Config, WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8"));
        app.MapGet("/impressum", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Impressum(s.Config, WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8"));
        app.MapGet("/datenschutz", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Privacy(s.Config, WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8"));

        // The official game favicon, embedded in the binary (the portal ships no asset files). Safe to cache
        // long: it changes at most with a deployment.
        app.MapGet("/favicon.ico", (HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(PortalFavicon.Bytes, "image/x-icon");
        });

        MapPlay(app, s);

        app.MapGet("/ask", (string? domain) => Ask(s, domain));
    }

    // Public aggregate stats (closes #245): four numbers for the website/client — no names, no ids, so
    // nothing personal leaves the service. Doubly guarded because it is unauthenticated: the cached
    // single-flight snapshot bounds the work, the per-IP limit bounds the traffic. CORS-open on purpose
    // so the marketing site can fetch it client-side.
    internal static async Task<IResult> StatsAsync(WorldHostServices s, ILogger log, HttpContext ctx)
    {
        if (!s.StatsLimit.TryPass(WorldHostApi.CallerIp(ctx)))
        {
            return WorldHostApi.RateLimited(s.Metrics);
        }

        ctx.Response.Headers.AccessControlAllowOrigin = "*";
        ctx.Response.Headers.CacheControl = $"public, max-age={Math.Max(1, s.Config.StatsCacheSeconds)}";
        try
        {
            return Results.Text(await s.PublicStats.GetAsync(), "application/json; charset=utf-8");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Public stats snapshot failed.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    // ---------------- Browser play (/play): the Unity WebGL client, deep-linked by the Play button ----------------
    // The build itself is injected out-of-band (fleet: a bind-mounted host folder — see deploy/worldhost);
    // the page connects to the world's wss URL from the deep-link query, so ONE central build serves every
    // world. Public like the portal pages: joining still requires the short-lived HMAC join token.
    private static void MapPlay(WebApplication app, WorldHostServices s)
    {
        string webglDir = Path.GetFullPath(s.Config.WebGlDir);
        Directory.CreateDirectory(webglDir);

        app.MapGet("/play", (HttpContext ctx) => Play(webglDir, ctx));

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webglDir),
            RequestPath = "/play",
            ServeUnknownFileTypes = true, // Unity emits .data/.wasm/.symbols.json without standard MIME types
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                var (encoding, contentType) = PlayPage.EncodingFor(ctx.File.Name);
                if (encoding != null)
                {
                    headers.ContentEncoding = encoding;
                }

                if (contentType != null)
                {
                    headers.ContentType = contentType;
                }

                headers.CacheControl = PlayPage.CacheControlFor(ctx.File.Name, ctx.Context.Request.Query.ContainsKey("v"));
            },
        });
    }

    internal static IResult Play(string webglDir, HttpContext ctx)
    {
        // The Unity index.html references its assets RELATIVELY (TemplateData/…, Build/…). Served at the
        // slashless "/play" those resolve against "/" and 404 (#218) — canonicalize to "/play/" and keep
        // the query string (the WebGL client reads server_host/hosted_token/… from the page URL).
        if (!ctx.Request.Path.Value!.EndsWith('/'))
        {
            return Results.Redirect("/play/" + ctx.Request.QueryString);
        }

        if (PlayPage.StampIndexHtml(webglDir) is { } html)
        {
            ctx.Response.Headers.CacheControl = "no-cache"; // always revalidate the entry page
            return Results.Content(html, "text/html; charset=utf-8");
        }

        return Results.Content(PlayPage.NotInstalledHtml(WorldHostApi.PageLang(ctx)), "text/html; charset=utf-8");
    }

    // Caddy on-demand TLS gate: before issuing a certificate for a requested hostname, Caddy asks this
    // endpoint. 200 only for the portal host itself and subdomains of real worlds — so nobody can make us
    // mint certificates (and burn rate limits) for arbitrary names pointed at our IP.
    internal static IResult Ask(WorldHostServices s, string? domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return Results.NotFound();
        }

        if (string.Equals(domain, s.Config.BaseDomain, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok();
        }

        string suffix = "." + s.Config.BaseDomain;
        if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && s.Registry.FindBySubdomain(domain.Substring(0, domain.Length - suffix.Length).ToLowerInvariant()) != null)
        {
            return Results.Ok();
        }

        return Results.NotFound();
    }
}

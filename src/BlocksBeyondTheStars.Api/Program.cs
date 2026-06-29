// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using BlocksBeyondTheStars.Shared.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

// BlocksBeyondTheStars admin web UI / meta backend (technical requirements §13). Lightweight
// ASP.NET Core app over a server installation directory. Bound to localhost/LAN by
// default so it is not publicly reachable without explicit configuration (§13.3).

string installDir = AppContext.BaseDirectory;
var admin = new AdminService(installDir);
var startupConfig = admin.LoadConfig();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton(admin);
builder.WebHost.UseUrls($"http://{startupConfig.AdminBindAddress}:{startupConfig.AdminPort}");

// Honor X-Forwarded-* so that, behind a TLS reverse proxy (the optional Caddy profile for public browser
// play, issue #121), req.Scheme/req.Host reflect the public https host rather than the internal container
// address — the portal's "Play in the browser" deep-link and the WebGL client's ws/wss choice depend on it.
// The proxy is a trusted sibling container on an arbitrary IP, so clear the default loopback-only allow-list.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Admin authentication: when an admin password is configured, every /api request must
// present it via the X-Admin-Password header. With no password set we rely on the bind
// address (localhost/LAN) and surface a warning in the status.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var password = app.Services.GetRequiredService<AdminService>().LoadConfig().AdminPassword;
        if (!string.IsNullOrEmpty(password))
        {
            var provided = context.Request.Headers["X-Admin-Password"].ToString();
            if (provided != password)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }
    }

    await next();
});

// Client distribution (anf_webclient.md §7): the Velopack installer + auto-update feed live in
// <install>/clients, produced by scripts/publish-client-installer.ps1. Serve that folder at /updates
// (the feed the in-game updater reads) — public, no admin password. .nupkg/RELEASES have no standard
// MIME type, so serve unknown types too.
string clientsDir = Path.Combine(installDir, "clients");
Directory.CreateDirectory(clientsDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(clientsDir),
    RequestPath = "/updates",
    ServeUnknownFileTypes = true,
});

// Browser client (issue #121): serve a Unity WebGL build at /play, same-origin with the portal so the
// content fetch needs no CORS. The image can't build WebGL (no Unity), so the folder is filled out-of-band:
// a mounted volume in phase 1, an entrypoint GitHub-Release fetch in phase 2. Public (no admin password),
// like /portal and /updates — the gameplay server still enforces its own join password.
string webglDir = Path.Combine(installDir, "webgl");
Directory.CreateDirectory(webglDir);

// Friendly page when no build is installed yet (volume not mounted / fetch disabled), so /play never
// 404s blankly. Registered BEFORE UseStaticFiles so it only fires when index.html is absent.
app.MapGet("/play", () =>
{
    if (File.Exists(Path.Combine(webglDir, "index.html")))
    {
        return Results.File(Path.Combine(webglDir, "index.html"), "text/html; charset=utf-8");
    }

    return Results.Content(
        "<!DOCTYPE html><meta charset='utf-8'><title>Browser client not installed</title>" +
        "<body style='font-family:system-ui;background:#070a12;color:#dfe9f7;padding:40px;line-height:1.5'>" +
        "<h2>No browser build is installed on this server yet.</h2>" +
        "<p>Mount a Unity <code>Build/WebGL/</code> folder at <code>/app/webgl</code>, " +
        "or set <code>BBS_FETCH_WEBGL=1</code> once a release ships a <code>webgl*.zip</code> asset.</p>" +
        "<p><a style='color:#5fd7ff' href='/portal'>Back to the server portal</a></p></body>",
        "text/html; charset=utf-8");
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webglDir),
    RequestPath = "/play",
    ServeUnknownFileTypes = true, // Unity emits .data/.wasm/.symbols.json without standard MIME types
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var name = ctx.File.Name;

        // Unity Brotli builds emit *.br files (e.g. build.wasm.br). UseStaticFiles serves them as opaque
        // bytes; announce the encoding + the decoded MIME so the browser inflates them natively instead of
        // falling back to Unity's slow JS decompressor.
        if (name.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
        {
            headers[HeaderNames.ContentEncoding] = "br";
            if (name.EndsWith(".wasm.br", StringComparison.OrdinalIgnoreCase))
            {
                headers[HeaderNames.ContentType] = "application/wasm";
            }
            else if (name.EndsWith(".js.br", StringComparison.OrdinalIgnoreCase))
            {
                headers[HeaderNames.ContentType] = "application/javascript";
            }
            else
            {
                headers[HeaderNames.ContentType] = "application/octet-stream";
            }
        }
        else if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            headers[HeaderNames.ContentEncoding] = "gzip";
        }

        // index.html references hash-named build files, so it must never be cached; everything else under
        // the Build/ folder is content-addressed and safe to cache immutably.
        headers[HeaderNames.CacheControl] = string.Equals(name, "index.html", StringComparison.OrdinalIgnoreCase)
            ? "no-cache"
            : "public, max-age=31536000, immutable";
    },
});

app.MapGet("/", () => Results.Content(AdminDashboard.Html, "text/html"));

// Public server portal (anf_webclient.md §7): the studio + game logo landing page with the client
// download and the in-app update URL. baseUrl is derived from the request so the page shows the exact
// address players reached it on.
app.MapGet("/portal", (HttpRequest req, AdminService a) =>
{
    var s = a.GetStatus();
    var baseUrl = $"{req.Scheme}://{req.Host}";
    return Results.Content(PortalPage.Render(s.ServerName, s.WorldName, s.GameplayPort, baseUrl), "text/html; charset=utf-8");
});

// One-click download: hand out the newest published Windows installer (Setup.exe) from <install>/clients.
// GET + HEAD (some download managers probe with HEAD) and range processing so the big self-contained
// installer download is resumable.
app.MapMethods("/download", new[] { "GET", "HEAD" }, () =>
{
    var setup = Directory.Exists(clientsDir)
        ? Directory.GetFiles(clientsDir, "*Setup.exe").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;
    return setup is null
        ? Results.NotFound("No client installer has been published yet. Run scripts/publish-client-installer.ps1 -ServeDir <this install>.")
        : Results.File(setup, "application/octet-stream", Path.GetFileName(setup), enableRangeProcessing: true);
});

// One-click Linux download: hand out the newest published Linux AppImage from <install>/clients. Mirrors
// /download above (GET + HEAD + range processing for a resumable, large self-contained download). The Docker
// entrypoint best-effort fetches this asset from the latest GitHub Release alongside the Windows Setup.exe.
app.MapMethods("/download-linux", new[] { "GET", "HEAD" }, () =>
{
    var appImage = Directory.Exists(clientsDir)
        ? Directory.GetFiles(clientsDir, "*.AppImage").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;
    return appImage is null
        ? Results.NotFound("No Linux client has been published yet. The latest GitHub Release ships a *.AppImage; drop it into the clients folder.")
        : Results.File(appImage, "application/octet-stream", Path.GetFileName(appImage), enableRangeProcessing: true);
});

// One-click macOS download: hand out the newest published macOS portable zip from <install>/clients.
// Mirrors /download-linux. The glob is anchored on "osx" so it never picks the Windows/Linux *Portable.zip.
// EXPERIMENTAL: the .app inside is unsigned/un-notarized (the portal page labels it as such). The Docker
// entrypoint best-effort fetches this asset from the latest GitHub Release alongside the other clients.
app.MapMethods("/download-mac", new[] { "GET", "HEAD" }, () =>
{
    var macZip = Directory.Exists(clientsDir)
        ? Directory.GetFiles(clientsDir, "*osx*Portable.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;
    return macZip is null
        ? Results.NotFound("No macOS client has been published yet. The latest GitHub Release ships a *-osx-*-Portable.zip; drop it into the clients folder.")
        : Results.File(macZip, "application/octet-stream", Path.GetFileName(macZip), enableRangeProcessing: true);
});

app.MapGet("/api/status", (AdminService a) => Results.Json(a.GetStatus()));

app.MapGet("/api/config", (AdminService a) => Results.Json(a.LoadConfig()));

app.MapPut("/api/config", async (HttpRequest req, AdminService a) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync();
    try
    {
        var config = ServerConfig.FromJson(json);
        a.SaveConfig(config);
        return Results.Ok(new { saved = true });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/backups", (AdminService a) => Results.Json(a.ListBackups()));

app.MapPost("/api/backups", (AdminService a) =>
{
    try
    {
        return Results.Json(new { name = a.CreateBackup() });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/logs", (AdminService a, int lines = 200) => Results.Json(a.TailLog(lines)));

// --- Admin extension editor: missions & content packs ---

app.MapGet("/api/missions", (AdminService a) => Results.Json(a.ListAdminMissions()));

app.MapPost("/api/missions", async (HttpRequest req, AdminService a) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync();
    try
    {
        var mission = System.Text.Json.JsonSerializer.Deserialize<BlocksBeyondTheStars.Shared.Missions.MissionDefinition>(json);
        if (mission is null)
        {
            return Results.BadRequest(new { error = "empty mission" });
        }

        var result = a.SaveAdminMission(mission);
        return result.Success ? Results.Json(result) : Results.BadRequest(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/missions/{id}", (string id, AdminService a) =>
{
    a.DeleteAdminMission(id);
    return Results.Ok(new { deleted = id });
});

app.MapGet("/api/content-pack", (AdminService a) => Results.Content(a.ExportContentPack(), "application/json"));

app.MapPost("/api/content-pack", async (HttpRequest req, AdminService a) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync();
    var result = a.ImportContentPack(json);
    return result.Success ? Results.Json(result) : Results.BadRequest(result);
});

app.Run();

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using BlocksBeyondTheStars.Shared.Configuration;
using Microsoft.Extensions.FileProviders;

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

var app = builder.Build();

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

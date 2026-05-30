using Spacecraft.Api;
using Spacecraft.Shared.Configuration;

// Spacecraft admin web UI / meta backend (technical requirements §13). Lightweight
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

app.MapGet("/", () => Results.Content(AdminDashboard.Html, "text/html"));

// Public server portal (anf_webclient.md §7): landing page + browser-play placeholder.
app.MapGet("/portal", (AdminService a) =>
{
    var s = a.GetStatus();
    var html = $@"<!DOCTYPE html><html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1""><title>{s.ServerName}</title>
<style>body{{font-family:system-ui,sans-serif;background:#0b0f1a;color:#dfe6f3;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0}}
.card{{background:#131a2b;border:1px solid #243049;border-radius:12px;padding:32px;max-width:480px}}
a.btn{{display:block;background:#2b6cff;color:#fff;text-decoration:none;text-align:center;padding:12px;border-radius:8px;margin:10px 0}}
.muted{{color:#8a96ad;font-size:13px}}</style></head>
<body><div class=""card""><h1>🚀 {s.ServerName}</h1>
<p class=""muted"">World: {s.WorldName} · Gameplay port: {s.GameplayPort}</p>
<a class=""btn"" href=""https://example.invalid/spacecraft/windows"">Download Windows client</a>
<a class=""btn"" href=""/play"">Play in the browser</a>
<p class=""muted"">Native clients connect to this server's IP on UDP {s.GameplayPort}.</p>
</div></body></html>";
    return Results.Content(html, "text/html");
});

app.MapGet("/play", () => Results.Content(
    "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Spacecraft - Browser</title></head>" +
    "<body style=\"font-family:system-ui;background:#0b0f1a;color:#dfe6f3;padding:40px\">" +
    "<h1>Browser client</h1><p>The WebGL build will be served here. The server exposes a WebSocket " +
    "gateway on the gameplay port so the browser client uses the same protocol. " +
    "See docs/WEBCLIENT_FEASIBILITY.md.</p></body></html>", "text/html"));

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
        var mission = System.Text.Json.JsonSerializer.Deserialize<Spacecraft.Shared.Missions.MissionDefinition>(json);
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

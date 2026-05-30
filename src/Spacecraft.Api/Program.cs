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

app.Run();

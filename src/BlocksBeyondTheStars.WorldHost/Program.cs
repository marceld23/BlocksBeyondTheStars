// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Microsoft.AspNetCore.HttpOverrides;

// Hosted-worlds control plane ("WorldHost"): accounts, world registry, wake-on-demand allocation and
// join-token issuing for a fleet of one-container-per-world dedicated servers. See
// docs/developer/HOSTED_WORLDS.md for the architecture (routing, DNS, certificates, lifecycle).
//
// Deliberately NOT part of the per-instance admin Api: this service owns MANY worlds and the Docker
// socket; the Api serves ONE installation. Bound to loopback by default — the public portal domain is
// proxied onto it by Caddy.

var config = WorldHostConfig.FromEnvironment();
var registry = new HostRegistry(config);
IInstanceLauncher launcher = new DockerCliLauncher(config);
var metrics = new WorldHostMetrics();
var orchestrator = new WorldOrchestrator(config, registry, launcher, metrics: metrics);
var glitch = new GlitchGateway(config, registry, orchestrator);

// Abuse limits (Phase 3). Signup/login key on the caller IP (real one via X-Forwarded-For — Caddy
// fronts this service), uploads/reports on the account. See WorldHostConfig for the operator knobs.
var signupLimit = new RateLimiter(config.SignupPerHourPerIp, TimeSpan.FromHours(1));
var loginLimit = new RateLimiter(config.LoginPerMinutePerIp, TimeSpan.FromMinutes(1));
var loginFailLimit = new RateLimiter(config.LoginFailsPer15MinPerAccount, TimeSpan.FromMinutes(15));
var uploadLimit = new RateLimiter(config.UploadsPerHourPerAccount, TimeSpan.FromHours(1));
var reportLimit = new RateLimiter(config.ReportsPerHourPerAccount, TimeSpan.FromHours(1));
var statsLimit = new RateLimiter(config.StatsPerMinutePerIp, TimeSpan.FromMinutes(1));
var glitchSessionLimit = new RateLimiter(config.GlitchSessionsPerMinutePerIp, TimeSpan.FromMinutes(1));

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");

// Honor X-Forwarded-* from the fronting Caddy so rate limits key on the real client IP, not the proxy —
// but ONLY from the configured trusted proxies (#418). The lists were previously cleared, which in this
// middleware means "trust ANY peer": whoever could reach the bind directly could rotate a fabricated
// X-Forwarded-For per request and mint a fresh rate-limit bucket every time. ForwardLimit stays at its
// default 1, so even a proxy that appends to an attacker-supplied header only ever advances one hop —
// the one the trusted proxy itself wrote. An empty trusted list (BBS_WH_TRUSTED_PROXIES=none) skips the
// middleware entirely: limits then key on the immediate peer, which is at least never attacker-chosen.
var (trustedNetworks, trustedProxies) = WorldHostConfig.ParseTrustedProxies(config.TrustedProxies);
bool honorForwardedHeaders = trustedNetworks.Count + trustedProxies.Count > 0;
if (honorForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        trustedNetworks.ForEach(options.KnownIPNetworks.Add);
        trustedProxies.ForEach(options.KnownProxies.Add);
    });
}

var app = builder.Build();
if (honorForwardedHeaders)
{
    app.UseForwardedHeaders();
}
var log = app.Logger;

string CallerIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// Every error response carries a stable machine `code` alongside the (English) `error` text, so the
// game client and the portal can show properly localized messages (DE/EN) instead of raw API English.
// The mapping lives here — the single place all player-safe error TEXTS of this assembly pass through.
static string? CodeFor(string error) => error switch
{
    _ when error.StartsWith("This account is banned", StringComparison.Ordinal) => "banned",
    _ when error.StartsWith("World limit reached", StringComparison.Ordinal) => "world_limit",
    _ when error.StartsWith("Save exceeds", StringComparison.Ordinal) => "upload_too_large",
    "Please accept the community rules to create an account." => "accept_rules",
    "Name must be 3-24 characters: letters, digits, '-' or '_'." => "name_invalid",
    "Password must be at least 8 characters." => "password_short",
    "This name is already taken." => "name_taken",
    "This name is reserved." or "This player name is reserved." => "name_reserved",
    "Please choose a different name." or "Please choose a different world name." or "Please choose a different player name." => "name_blocked",
    "World name must be 1-40 printable characters." => "world_name_invalid",
    "No capacity available right now — please try again later." => "no_capacity",
    "Player name must be 1-24 printable characters." => "player_name_invalid",
    "The community rules have changed — please accept them on the portal first."
        or "The community rules have changed — please accept them first." => "terms_outdated",
    "World not found." => "world_not_found",
    "The world could not be started — please try again in a moment." => "world_start_failed",
    "The world did not come up in time — please try again." => "world_wake_failed",
    "Stop the world before uploading a save." or "Stop the world before downloading its save." => "stop_first",
    "This world needs a password." => "password_required",
    "Wrong world password." => "wrong_password",
    "Too many password attempts — please wait a few minutes." => "too_many_attempts",
    "World password must be 4-24 printable characters." => "world_password_invalid",
    "Empty upload." => "upload_empty",
    "This file is not a Blocks Beyond the Stars save (world.db)."
        or "The save file is damaged (integrity check failed)."
        or "This database is not a Blocks Beyond the Stars world save."
        or "The save file could not be read." => "save_invalid",
    "This world has no save yet (it was never started)." => "save_missing",
    _ when error.StartsWith("This player is banned", StringComparison.Ordinal) => "banned",
    "The glitch.fun gateway is disabled." => "glitch_disabled",
    "This install could not be verified with glitch.fun." => "glitch_invalid_install",
    "All arcade worlds are full right now — please try again in a few minutes." => "glitch_full",
    _ => null,
};

IResult ApiError(string error, int status = StatusCodes.Status400BadRequest)
    => Results.Json(new { error, code = CodeFor(error) }, statusCode: status);

IResult RateLimited()
{
    metrics.RateLimited();
    return Results.Json(new { error = "Too many requests — please wait a bit and try again.", code = "rate_limited" },
        statusCode: StatusCodes.Status429TooManyRequests);
}

// Resolves the caller's account from the Authorization: Bearer <session> header; null = not signed in.
AccountRecord? Caller(HttpContext ctx)
{
    string header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? registry.ResolveSession(header.Substring(prefix.Length).Trim())
        : null;
}

// Strips CR/LF so a player-supplied string (name, reason) can never forge extra log lines. Account and
// world names are charset-validated anyway; this covers free-text fields and satisfies defense in depth.
static string LogSafe(string? value)
    => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

// Uniform account-state gate for world actions: banned accounts and accounts that haven't accepted the
// CURRENT rules version are refused (the join path re-checks inside the orchestrator as well — that is
// the choke point native clients will use directly).
IResult? GuardAccount(AccountRecord account)
{
    if (account.IsBanned)
    {
        return Results.Json(new
        {
            error = string.IsNullOrEmpty(account.BanReason) ? "This account is banned." : $"This account is banned: {account.BanReason}",
            code = "banned",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (account.AcceptedTermsVersion < config.TermsVersion)
    {
        return Results.Json(new
        {
            error = "The community rules have changed — please accept them first.",
            code = "terms_outdated",
            termsOutdated = true,
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    return null;
}

bool IsAdmin(HttpContext ctx)
    => BasicAuth.TokenEquals(ctx.Request.Headers["X-Admin-Token"].ToString(), config.AdminToken);

// Browser admin UI gate (Basic Auth — browsers can't send X-Admin-Token). Returns the 401 challenge
// to send, or null when authorized. Off until BBS_WH_ADMIN_USER + _PASSWORD are configured.
IResult? GuardAdminUi(HttpContext ctx)
{
    if (BasicAuth.IsAuthorized(ctx.Request.Headers.Authorization.ToString(), config.AdminUser, config.AdminPassword))
    {
        return null;
    }

    ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"BBS fleet admin\", charset=\"UTF-8\"";
    return Results.Text("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
}

// Extracts joinedPlayers from an instance's /status JSON (shared with the glitch gateway's world
// pick); null when unreadable — callers show "?" (admin page) or count 0 (aggregates) rather than fail.
static int? ParseJoinedPlayers(string statusJson) => GlitchGateway.ParseJoinedPlayers(statusJson);

// Sum of players currently on running instances, probed in parallel. Callers are throttled (the
// admin page and the CACHED public snapshot) — never wire this to an uncached public path.
async Task<int> CountPlayersOnlineAsync()
{
    var running = registry.ListAllWorldsAdmin().Where(e => e.World.Status == WorldStatus.Running);
    var counts = await Task.WhenAll(running.Select(async e =>
        await orchestrator.ReadInstanceStatusAsync(e.World) is { } json ? ParseJoinedPlayers(json) ?? 0 : 0));
    return counts.Sum();
}

// Public aggregate snapshot (/api/stats): rebuilt at most once per TTL no matter the request rate —
// the instance probes behind `online` are the expensive part this cache exists to protect.
using var publicStats = new CachedJson(TimeSpan.FromSeconds(Math.Max(1, config.StatsCacheSeconds)), async () =>
{
    var counts = registry.CountForMetrics();
    long created = counts.WorldsByStatus.Sum(s => s.Count);
    long active = counts.WorldsByStatus
        .Where(s => s.Status is WorldStatus.Running or WorldStatus.Starting)
        .Sum(s => s.Count);
    int online = await CountPlayersOnlineAsync();
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        worlds = new { created, active },
        players = new { registered = counts.Accounts, online },
        updatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    });
});

// Gathers everything the admin page shows; live joined counts are probed in parallel for running
// instances only (3 s HTTP cap keeps a dead instance from stalling the page).
async Task<IResult> RenderAdminAsync(HttpContext ctx)
{
    string? lookupQuery = ctx.Request.Query["acct"].ToString() is { Length: > 0 } q ? q : null;
    var all = registry.ListAllWorldsAdmin();
    var rows = await Task.WhenAll(all.Select(async entry =>
    {
        int? joined = entry.World.Status == WorldStatus.Running
            && await orchestrator.ReadInstanceStatusAsync(entry.World) is { } json
            ? ParseJoinedPlayers(json)
            : null;
        return new AdminWorldRow(entry.World, entry.OwnerName, joined);
    }));

    return Results.Content(WorldHostAdminPages.Index(
        config, rows, registry.ListOpenReports(), registry.ListBannedAccounts(),
        lookupQuery is null ? null : registry.FindAccountByName(lookupQuery), lookupQuery,
        registry.ListGlitchGuests(), registry.ListGlitchBans(),
        ctx.Request.Query["notice"].ToString()),
        "text/html; charset=utf-8");
}

app.MapGet("/healthz", () => Results.Text("ok\n"));

// Prometheus scrape (Phase 3). Reachable only on the loopback bind — Caddy deliberately does not
// route /metrics, so fleet numbers never leak publicly.
app.MapGet("/metrics", () => Results.Text(metrics.Render(registry), "text/plain; version=0.0.4; charset=utf-8"));

// Public aggregate stats (closes #245): four numbers for the website/client — no names, no ids, so
// nothing personal leaves the service. Doubly guarded because it is unauthenticated: the cached
// single-flight snapshot bounds the work, the per-IP limit bounds the traffic. CORS-open on purpose
// so the marketing site can fetch it client-side.
app.MapGet("/api/stats", async (HttpContext ctx) =>
{
    if (!statsLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    ctx.Response.Headers.AccessControlAllowOrigin = "*";
    ctx.Response.Headers.CacheControl = $"public, max-age={Math.Max(1, config.StatsCacheSeconds)}";
    try
    {
        return Results.Text(await publicStats.GetAsync(), "application/json; charset=utf-8");
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Public stats snapshot failed.");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

// ---------------- Portal pages (server-rendered shells; the JS talks to /api with a Bearer session) ----------------

// Page language: explicit ?lang= wins (and is remembered in a cookie so plain links keep the choice);
// otherwise the cookie; otherwise the browser's Accept-Language (first visit — auto-detection sets NO
// cookie, only an explicit switch persists); otherwise German — the portal's primary audience. Only
// "en"/"de" are honored.
string PageLang(HttpContext ctx)
{
    string? q = ctx.Request.Query["lang"];
    if (q is "en" or "de")
    {
        ctx.Response.Cookies.Append("bbs_lang", q, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            // Server-side fallback only (the JS carries ?lang= itself), so it can be locked down fully.
            Secure = true,
            HttpOnly = true,
        });
        return q;
    }

    string? cookie = ctx.Request.Cookies["bbs_lang"];
    return cookie is "en" or "de"
        ? cookie
        : WorldHostPortalPages.LangFromAcceptHeader(ctx.Request.Headers.AcceptLanguage);
}

app.MapGet("/", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Landing(config, PageLang(ctx)), "text/html; charset=utf-8"));
app.MapGet("/worlds", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Worlds(config, PageLang(ctx)), "text/html; charset=utf-8"));
app.MapGet("/rules", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Rules(config, PageLang(ctx)), "text/html; charset=utf-8"));
app.MapGet("/impressum", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Impressum(config, PageLang(ctx)), "text/html; charset=utf-8"));
app.MapGet("/datenschutz", (HttpContext ctx) => Results.Content(WorldHostPortalPages.Privacy(config, PageLang(ctx)), "text/html; charset=utf-8"));

// The official game favicon, embedded in the binary (the portal ships no asset files). Safe to cache
// long: it changes at most with a deployment.
app.MapGet("/favicon.ico", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(PortalFavicon.Bytes, "image/x-icon");
});

// ---------------- Browser play (/play): the Unity WebGL client, deep-linked by the Play button ----------------
// The build itself is injected out-of-band (fleet: a bind-mounted host folder — see deploy/worldhost);
// the page connects to the world's wss URL from the deep-link query, so ONE central build serves every
// world. Public like the portal pages: joining still requires the short-lived HMAC join token.

string webglDir = Path.GetFullPath(config.WebGlDir);
Directory.CreateDirectory(webglDir);

app.MapGet("/play", (HttpContext ctx) =>
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

    return Results.Content(PlayPage.NotInstalledHtml(PageLang(ctx)), "text/html; charset=utf-8");
});

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

// Caddy on-demand TLS gate: before issuing a certificate for a requested hostname, Caddy asks this
// endpoint. 200 only for the portal host itself and subdomains of real worlds — so nobody can make us
// mint certificates (and burn rate limits) for arbitrary names pointed at our IP.
app.MapGet("/ask", (string? domain) =>
{
    if (string.IsNullOrEmpty(domain))
    {
        return Results.NotFound();
    }

    if (string.Equals(domain, config.BaseDomain, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok();
    }

    string suffix = "." + config.BaseDomain;
    if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && registry.FindBySubdomain(domain.Substring(0, domain.Length - suffix.Length).ToLowerInvariant()) != null)
    {
        return Results.Ok();
    }

    return Results.NotFound();
});

// ---------------- glitch.fun arcade gateway ----------------
// The Glitch-hosted WebGL build calls these two endpoints cross-origin, so they are the ONLY /api
// routes with CORS — echoing exactly the configured Glitch origins, never *. Everything else about
// the arcade lives in GlitchGateway; disabled deployments answer 404 (and never leak why).

void ApplyGlitchCors(HttpContext ctx)
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

// The JSON POSTs from the Glitch origin always preflight — answer it for every /api/glitch route.
app.MapMethods("/api/glitch/{**rest}", new[] { "OPTIONS" }, (HttpContext ctx) =>
{
    ApplyGlitchCors(ctx);
    return Results.NoContent();
});

app.MapPost("/api/glitch/session", async (HttpContext ctx, GlitchSessionRequest req) =>
{
    ApplyGlitchCors(ctx);
    if (!glitch.Enabled)
    {
        return Results.NotFound();
    }

    if (!glitchSessionLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    var result = await glitch.SessionAsync(req.InstallId, req.PlayerName);
    if (!result.Ok)
    {
        int status = CodeFor(result.Error) switch
        {
            "banned" or "glitch_invalid_install" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status503ServiceUnavailable, // glitch_full, no_capacity, wake failures
        };
        return ApiError(result.Error, status);
    }

    log.LogInformation("glitch.fun session: {Player} → arcade world {World}.", LogSafe(result.PlayerName), result.WorldId);
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
});

app.MapPost("/api/glitch/heartbeat", async (HttpContext ctx, GlitchHeartbeatRequest req) =>
{
    ApplyGlitchCors(ctx);
    var (status, body) = await glitch.RelayHeartbeatAsync(req.InstallId, req.SessionId, req.Platform, req.GameVersion);
    return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
});

// Browser-singleplayer cloud saves, relayed to Glitch Cloud Save slot 0 (title token stays here;
// checksum computed server-side over the decoded bytes; 10 MB + rate caps enforced before Glitch).
app.MapGet("/api/glitch/save", async (HttpContext ctx, string? installId) =>
{
    ApplyGlitchCors(ctx);
    var (status, body) = await glitch.LoadSaveAsync(installId);
    return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
});

app.MapPost("/api/glitch/save", async (HttpContext ctx, GlitchSaveStoreRequest req) =>
{
    ApplyGlitchCors(ctx);
    var (status, body) = await glitch.StoreSaveAsync(req.InstallId, req.Payload, req.BaseVersion);
    return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
});

app.MapPost("/api/glitch/save/resolve", async (HttpContext ctx, GlitchSaveResolveRequest req) =>
{
    ApplyGlitchCors(ctx);
    var (status, body) = await glitch.ResolveSaveAsync(req.InstallId, req.SaveId, req.ConflictId, req.Choice);
    return Results.Text(body, "application/json; charset=utf-8", statusCode: status);
});

// ---------------- Accounts ----------------

// Current community-rules version + plain text (DE/EN), anonymous: the desktop client shows the rules
// in-game and needs the version number for signup (login only reports a boolean termsOutdated). Static
// per deployment, so browsers/clients may cache it briefly.
app.MapGet("/api/terms", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Json(new
    {
        version = config.TermsVersion,
        textDe = CommunityRules.PlainText("de"),
        textEn = CommunityRules.PlainText("en"),
    });
});

app.MapPost("/api/signup", (HttpContext ctx, SignupRequest req) =>
{
    if (!signupLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    var (ok, error, accountId, session) = registry.CreateAccount(req.Name, req.Password, req.ClaimCode, req.AcceptedTermsVersion);
    if (!ok)
    {
        return ApiError(error);
    }

    // Deliberately no account id in the log: ids act as stable references in the registry and appearing
    // in log files would let anyone with log access correlate them (CodeQL cs/cleartext-storage).
    log.LogInformation("Account created: {Name}.", LogSafe(req.Name));
    return Results.Json(new { accountId, sessionToken = session });
});

app.MapPost("/api/login", (HttpContext ctx, SignupRequest req) =>
{
    if (!loginLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    // Per-account failed-login backoff (#418), the world-password pattern: the per-IP window keys on a
    // value a distributed attacker multiplies at will, this one keys on the TARGET name — lowercased so
    // case rotation doesn't mint fresh windows. Only failures consume budget (IsExhausted checks without
    // spending), so the account owner with the right password sails through even mid-attack.
    string name = req.Name ?? string.Empty; // declared non-nullable, but JSON binding can deliver null
    string accountKey = name.Trim().ToLowerInvariant();
    if (loginFailLimit.IsExhausted(accountKey))
    {
        return ApiError("Too many password attempts — please wait a few minutes.", StatusCodes.Status429TooManyRequests);
    }

    if (registry.Login(name, req.Password) is not { } login)
    {
        loginFailLimit.TryPass(accountKey);
        return Results.Unauthorized();
    }

    // termsOutdated tells the portal/client to show the re-acceptance screen before world actions.
    var account = registry.ResolveSession(login.SessionToken)!;
    return Results.Json(new
    {
        accountId = login.AccountId,
        sessionToken = login.SessionToken,
        termsOutdated = account.AcceptedTermsVersion < config.TermsVersion,
    });
});

app.MapPost("/api/accept-terms", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    registry.AcceptTerms(account.Id, config.TermsVersion);
    return Results.Ok();
});

// Account self-deletion (DSGVO Art. 17): erases the account, its sessions, its reports AND all of its
// worlds including their saves on disk (live + archive). Deliberately available to banned accounts too.
app.MapDelete("/api/account", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    foreach (var world in registry.ListWorlds(account.Id))
    {
        orchestrator.DeleteWorld(world, purgeSaves: true);
    }

    registry.DeleteAccount(account.Id);
    log.LogInformation("Account '{Name}' deleted on request (worlds + saves removed).", LogSafe(account.Name));
    return Results.Ok();
});

// ---------------- Player reports ("Spieler melden") ----------------

app.MapPost("/api/reports", (HttpContext ctx, ReportRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!reportLimit.TryPass(account.Id))
    {
        return RateLimited();
    }

    // Banned players may still file reports (they can't play, but silencing them buys nothing);
    // reports are length-capped and reviewed manually — nobody is auto-punished by a report.
    var (ok, error) = registry.CreateReport(account.Id, req.WorldId ?? string.Empty, req.ReportedName, req.Category, req.Message ?? string.Empty);
    return ok ? Results.Ok() : ApiError(error);
});

// ---------------- Operator admin (X-Admin-Token; disabled when no token is configured) ----------------

app.MapGet("/api/admin/reports", (HttpContext ctx) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    return Results.Json(new { reports = registry.ListOpenReports() });
});

app.MapPost("/api/admin/reports/{id:long}/close", (HttpContext ctx, long id, CloseReportRequest req) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    string status = req.Status is "dismissed" ? "dismissed" : "reviewed";
    registry.CloseReport(id, status);
    return Results.Ok();
});

app.MapPost("/api/admin/ban", (HttpContext ctx, BanRequest req) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    registry.SetBanned(req.AccountId, req.Banned, req.Reason ?? string.Empty);
    log.LogInformation("Account {Id} {Action} ({Reason}).", LogSafe(req.AccountId), req.Banned ? "BANNED" : "unbanned", LogSafe(req.Reason));
    return Results.Ok();
});

// Maintenance announcement (#249), scriptable twin of the /admin form below: pushes an info message or a
// restart countdown into one world or the whole fleet. The message text is operator-authored free text —
// never logged raw (LogSafe), sanitized again instance-side before broadcasting.
app.MapPost("/api/admin/announce", async (HttpContext ctx, AnnounceRequest req) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    if (req.WorldId is { } wid && !HostRegistry.IsValidWorldId(wid))
    {
        return ApiError("World not found.", StatusCodes.Status404NotFound);
    }

    var (reached, targets) = await orchestrator.AnnounceAsync(req.Kind, req.Text, req.Seconds, req.WorldId);
    log.LogInformation("Admin API: announce kind {Kind} to {Target} — reached {Reached}/{Targets}.",
        req.Kind, req.WorldId is null ? "fleet" : LogSafe(req.WorldId), reached, targets);
    return Results.Json(new { reached, targets });
});

// Operator world deletion, scriptable twin of the /admin buttons below — the lever for bulk cleanup of
// dead worlds. `purge=true` also erases the saves (live + archive); without it they stay on disk exactly
// like an owner's own delete. Unlike the owner route this is NOT ownership-scoped: it deletes any world,
// including the account-less glitch.fun arcade pool (which the gateway then re-creates empty).
app.MapDelete("/api/admin/worlds/{id}", (HttpContext ctx, string id) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return ApiError("World not found.", StatusCodes.Status404NotFound);
    }

    bool purge = ctx.Request.Query["purge"].ToString() == "true";
    orchestrator.DeleteWorld(world, purge);
    log.LogInformation("Admin API: world '{Name}' ({Id}) deleted — saves {Fate}.",
        LogSafe(world.DisplayName), world.Id, purge ? "PURGED" : "retained");
    return Results.Ok();
});

// ---------------- Operator admin UI (Basic Auth; the browser front-end to the API above) ----------------

app.MapGet("/admin", async (HttpContext ctx) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    return await RenderAdminAsync(ctx);
});

// Per-world detail (#489): players, structures and build hotspots, read straight from the world save.
// No instance call — the saves are bind-mounted where this process can read them (WorldInspector).
app.MapGet("/admin/worlds/{id}", (HttpContext ctx, string id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    string ownerName = registry.ListAllWorldsAdmin()
        .FirstOrDefault(e => e.World.Id == world.Id).OwnerName ?? "—";
    var insight = WorldInspector.Read(config, world.Id);
    return Results.Content(
        WorldHostAdminPages.WorldDetail(config, world, ownerName, insight), "text/html; charset=utf-8");
});

// Server-health JSON behind the admin page's "Server health" card (closes #244). Fetched by the card
// AFTER the page renders, because `docker stats` samples for ~1-2 s and must not stall the page.
app.MapGet("/admin/stats.json", async (HttpContext ctx) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    var host = HostStats.Read(config.WorldsDir);
    var containers = await Task.Run(() => launcher.ContainerStats());
    var counts = registry.CountForMetrics();
    int online = await CountPlayersOnlineAsync();
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
});

app.MapPost("/admin/reports/{id:long}/close", async (HttpContext ctx, long id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    var form = await ctx.Request.ReadFormAsync();
    registry.CloseReport(id, form["status"].ToString() is "dismissed" ? "dismissed" : "reviewed");
    return Results.Redirect("/admin");
});

app.MapPost("/admin/ban", async (HttpContext ctx) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    var form = await ctx.Request.ReadFormAsync();
    string accountId = form["accountId"].ToString();
    bool banned = form["banned"].ToString() == "true";
    string reason = form["reason"].ToString();
    if (accountId.Length > 0)
    {
        registry.SetBanned(accountId, banned, reason);
        log.LogInformation("Admin UI: account {Action} ({Reason}).", banned ? "BANNED" : "unbanned", LogSafe(reason));
    }

    return Results.Redirect("/admin");
});

app.MapPost("/admin/worlds/{id}/stop", (HttpContext ctx, string id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    if (HostRegistry.IsValidWorldId(id) && registry.GetWorld(id) is { } world)
    {
        orchestrator.StopWorld(world);
        log.LogInformation("Admin UI: world {Id} stopped.", world.Id);
    }

    return Results.Redirect("/admin");
});

// Ban/unban a glitch.fun install id (the arcade channel's ban lever — guests have no account). A ban
// takes effect on the guest's NEXT session grant and next heartbeat (the relay answers 403, which the
// client treats as "stop the game"), so no instance restart is involved.
app.MapPost("/admin/glitch/ban", async (HttpContext ctx) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    var form = await ctx.Request.ReadFormAsync();
    string installId = form["installId"].ToString().Trim();
    bool banned = form["banned"].ToString() == "true";
    if (installId.Length > 0)
    {
        registry.SetGlitchBanned(installId, banned, form["reason"].ToString(), form["playerName"].ToString());
        log.LogInformation("Admin UI: glitch install {Action} ({Reason}).", banned ? "BANNED" : "unbanned", LogSafe(form["reason"].ToString()));
    }

    return Results.Redirect("/admin");
});

// The /admin "Announce" card (#249): info message, scheduled restart or cancel — per world or fleet-wide.
app.MapPost("/admin/announce", async (HttpContext ctx) =>
{
    if (GuardAdminUi(ctx) is { } denied)
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

    var (reached, targets) = await orchestrator.AnnounceAsync(kind, message, seconds, worldId);
    log.LogInformation("Admin UI: announce kind {Kind} to {Target} — reached {Reached}/{Targets}.",
        kind, worldId is null ? "fleet" : LogSafe(worldId), reached, targets);
    return Results.Redirect("/admin");
});

// Graceful per-world restart (#249): warn the players (10-minute countdown banner), then the instance
// stops itself through the same drain+save path — the humane sibling of the immediate Stop button.
app.MapPost("/admin/worlds/{id}/restart", async (HttpContext ctx, string id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    if (HostRegistry.IsValidWorldId(id) && registry.GetWorld(id) is { } world)
    {
        bool reached = await orchestrator.AnnounceInstanceAsync(world, kind: 1, text: null, seconds: 600);
        log.LogInformation("Admin UI: world {Id} graceful restart {Result}.", world.Id, reached ? "scheduled (10 min)" : "NOT reachable");
    }

    return Results.Redirect("/admin");
});

app.MapPost("/admin/worlds/{id}/wake", async (HttpContext ctx, string id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    if (HostRegistry.IsValidWorldId(id))
    {
        await orchestrator.EnsureRunningAsync(id); // errors surface as status on the page
    }

    return Results.Redirect("/admin");
});

// Delete a world from the fleet overview. Guarded by a typed confirmation (the world's display name)
// checked HERE rather than in the browser — the delete button sits next to `stop` in a narrow table cell
// and there is no undo. `purge=true` erases the saves as well; the plain delete keeps them on disk.
// Deleting a RUNNING world is allowed and blocks while `docker stop` drains it, exactly like the stop
// button. On a mismatch nothing happens and the page says so.
app.MapPost("/admin/worlds/{id}/delete", async (HttpContext ctx, string id) =>
{
    if (GuardAdminUi(ctx) is { } denied)
    {
        return denied;
    }

    var form = await ctx.Request.ReadFormAsync();
    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.Redirect("/admin");
    }

    if (!string.Equals(form["confirm"].ToString().Trim(), world.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        log.LogInformation("Admin UI: delete of world {Id} refused — confirmation name did not match.", world.Id);
        return Results.Redirect("/admin?notice=confirm");
    }

    bool purge = form["purge"].ToString() == "true";
    orchestrator.DeleteWorld(world, purge);
    log.LogInformation("Admin UI: world '{Name}' ({Id}) deleted — saves {Fate}.",
        LogSafe(world.DisplayName), world.Id, purge ? "PURGED" : "retained");
    return Results.Redirect($"/admin?notice={(purge ? "purged" : "deleted")}");
});

// ---------------- Worlds ----------------

app.MapGet("/api/worlds", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    var worlds = registry.ListWorlds(account.Id)
        .Select(w => new { id = w.Id, name = w.DisplayName, status = w.Status, subdomain = w.Subdomain, hasPassword = w.HasPassword, isPublic = w.IsPublic });
    return Results.Json(new { worlds });
});

// Public world browser: worlds their owners opted into listing. Requires a signed-in account (joining
// needs one anyway) but is NOT owner-scoped — this is the one cross-account world listing players can see.
// Every listed world is password-gated by construction (see HostRegistry.SetWorldVisibility), so the join
// still requires the owner-shared password; we surface only name + status, never owner identity.
app.MapGet("/api/worlds/public", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    var worlds = registry.ListPublicWorlds()
        .Select(w => new { id = w.Id, name = w.DisplayName, status = w.Status, hasPassword = w.HasPassword });
    return Results.Json(new { worlds });
});

// Operator world browser (issue #495): EVERY world on the fleet, including private ones — the reach the
// invisible observer needs, since kids mostly play on private/password worlds. Developer accounts only
// (claimable solely with the secret ReservedClaimCode), and unlike /api/worlds/public this one may name the
// owner: the operator moderates, and "whose world is this" is the first question moderation asks. Regular
// accounts get 403 — the client probes this endpoint and simply hides the operator section on failure.
app.MapGet("/api/worlds/all", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!account.IsDeveloper)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var worlds = registry.ListAllWorldsAdmin()
        .Select(e => new
        {
            id = e.World.Id,
            name = e.World.DisplayName,
            status = e.World.Status,
            owner = e.OwnerName,
            isPublic = e.World.IsPublic,
            hasPassword = e.World.HasPassword,
        });
    return Results.Json(new { worlds });
});

app.MapPost("/api/worlds", (HttpContext ctx, CreateWorldRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (GuardAccount(account) is { } blocked)
    {
        return blocked;
    }

    var (ok, error, world) = registry.CreateWorld(account.Id, req.Name, req.Password);
    if (!ok)
    {
        return ApiError(error);
    }

    log.LogInformation("World '{Name}' ({Id}) created by {Account}.", LogSafe(world!.DisplayName), world.Id, LogSafe(account.Name));
    return Results.Json(new { id = world.Id, name = world.DisplayName, status = world.Status, subdomain = world.Subdomain, hasPassword = world.HasPassword });
});

app.MapPost("/api/worlds/{id}/join", async (HttpContext ctx, string id, JoinRequestDto req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is null)
    {
        return Results.NotFound();
    }

    // The join grant is the access-control choke point: the orchestrator enforces ban/terms AND the
    // creator-set world password (#250/#251) before minting a token; the instance only admits valid
    // tokens. The grant names the caller's account, so the instance can attribute every admitted player.
    var (grant, error) = await orchestrator.JoinAsync(id, account, req.PlayerName, req.Password);
    if (grant is null)
    {
        int status = CodeFor(error) switch
        {
            "password_required" or "wrong_password" => StatusCodes.Status403Forbidden,
            "too_many_attempts" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        return ApiError(error, status);
    }

    return Results.Json(grant);
});

// Owner-only: set/change (4-24 chars) or remove (empty) the world's join password (#250). Applies to
// the NEXT join grant immediately — no instance restart involved (the gate lives at token issuance).
app.MapPost("/api/worlds/{id}/password", (HttpContext ctx, string id, WorldPasswordRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    var (ok, error) = registry.SetWorldPassword(world.Id, req.Password);
    if (!ok)
    {
        return ApiError(error);
    }

    bool hasPassword = !string.IsNullOrEmpty(req.Password);
    log.LogInformation("World {Id}: join password {Action} by its owner.", world.Id, hasPassword ? "set" : "removed");
    // Removing the password also un-lists a public world (SetWorldPassword enforces it) — tell the client
    // so its toggle reflects the new state without a reload.
    return Results.Json(new { hasPassword, isPublic = hasPassword && registry.GetWorld(world.Id)?.IsPublic == true });
});

// Owner-only: opt a world in/out of the public browser. Listing requires a join password first (enforced
// in the registry) — public worlds are always password-gated.
app.MapPost("/api/worlds/{id}/visibility", (HttpContext ctx, string id, WorldVisibilityRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    var (ok, error) = registry.SetWorldVisibility(world.Id, req.Public);
    if (!ok)
    {
        return ApiError(error);
    }

    log.LogInformation("World {Id}: public listing {Action} by its owner.", world.Id, req.Public ? "enabled" : "disabled");
    return Results.Json(new { isPublic = req.Public });
});

app.MapPost("/api/worlds/{id}/stop", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    orchestrator.StopWorld(world);
    return Results.Ok();
});

app.MapDelete("/api/worlds/{id}", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    // The saves directory is intentionally NOT removed — an operator can still recover/export it (and
    // purge it from /admin); automated retention is Phase 3.
    orchestrator.DeleteWorld(world, purgeSaves: false);
    log.LogInformation("World '{Name}' ({Id}) deleted by {Account} (saves directory retained).", LogSafe(world.DisplayName), world.Id, LogSafe(account.Name));
    return Results.Ok();
});

// ---------------- Save upload / export (the SP↔hosted round-trip) ----------------

app.MapPost("/api/worlds/{id}/save", async (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (GuardAccount(account) is { } blocked)
    {
        return blocked;
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    if (!uploadLimit.TryPass(account.Id))
    {
        return RateLimited();
    }

    // Only while stopped: the instance owns the file when it runs, and a mid-write copy would corrupt.
    if (registry.GetWorld(id)!.Status != WorldStatus.Stopped || launcher.IsRunning(world.ContainerId))
    {
        return ApiError("Stop the world before uploading a save.");
    }

    // Stream to a temp file with a hard size cap, then validate BEFORE it replaces anything.
    string tmp = Path.Combine(Path.GetTempPath(), $"bbs-upload-{world.Id}-{Guid.NewGuid():N}.db");
    try
    {
        await using (var file = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > config.UploadMaxBytes)
                {
                    return ApiError($"Save exceeds the {config.UploadMaxBytes / (1024 * 1024)} MB upload limit.");
                }

                await file.WriteAsync(buffer.AsMemory(0, read));
            }

            if (total == 0)
            {
                return ApiError("Empty upload.");
            }
        }

        var (ok, error) = SavePaths.ValidateUploadedSave(tmp);
        if (!ok)
        {
            return ApiError(error);
        }

        // Keep exactly one previous generation as a manual-recovery net, then move the upload into place.
        string target = SavePaths.WorldDbPath(config, world.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            File.Copy(target, target + ".bak", overwrite: true);
        }

        File.Move(tmp, target, overwrite: true);
        log.LogInformation("World '{Name}' ({Id}): save uploaded by {Account}.", LogSafe(world.DisplayName), world.Id, LogSafe(account.Name));
        return Results.Ok();
    }
    finally
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
    }
});

app.MapGet("/api/worlds/{id}/save", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    if (registry.GetWorld(id)!.Status != WorldStatus.Stopped || launcher.IsRunning(world.ContainerId))
    {
        return ApiError("Stop the world before downloading its save.");
    }

    string path = SavePaths.WorldDbPath(config, world.Id);
    if (!File.Exists(path))
    {
        return ApiError("This world has no save yet (it was never started).");
    }

    return Results.File(path, "application/octet-stream", $"{world.Id}-world.db");
});

// ---------------- Background reaper ----------------

// Reconcile registry vs Docker every 30 s: instances that exited themselves (idle shutdown — the normal
// sleep path) get marked stopped so the next join wakes them and world lists stay truthful.
// Keep-awake arcade worlds: wake the pool once at startup (fire-and-forget — worldgen on a fresh
// pool can take a minute per world) so the first glitch.fun visitor lands in a RUNNING world.
if (config.GlitchKeepAwake && glitch.Enabled)
{
    _ = Task.Run(async () =>
    {
        try
        {
            int woken = await glitch.WakePoolAsync();
            log.LogInformation("glitch.fun keep-awake: {Count} arcade world(s) woken at startup.", woken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "glitch.fun keep-awake startup pass failed (the reaper retries).");
        }
    });
}

var reaper = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    int ticks = 0;
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            int reaped = orchestrator.Reap();
            if (reaped > 0)
            {
                log.LogInformation("Reaper: {Count} idle-stopped world(s) marked stopped.", reaped);
            }

            // Keep-awake arcade worlds: re-wake anything the reaper just found dead (crash/OOM/host
            // reboot) — with idle shutdown off these should never stop on their own.
            int rewoken = await glitch.WakePoolAsync();
            if (rewoken > 0)
            {
                log.LogInformation("glitch.fun keep-awake: {Count} arcade world(s) re-woken.", rewoken);
            }

            // Archive sweep once an hour (120 × 30 s): long-inactive stopped worlds move to the archive.
            if (++ticks % 120 == 0)
            {
                int archived = orchestrator.ArchiveSweep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (archived > 0)
                {
                    log.LogInformation("Archive sweep: {Count} world(s) archived after {Months} months of inactivity.",
                        archived, config.ArchiveAfterMonths);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Reaper pass failed (will retry).");
        }
    }
});

log.LogInformation(
    "WorldHost up on {Bind}:{Port} — domain {Domain}, image {Image}, quotas: {Worlds} worlds/account, {Players} players, idle {Idle} min.",
    config.BindAddress, config.Port, config.BaseDomain, config.ServerImage,
    config.MaxWorldsPerAccount, config.MaxPlayersPerWorld, config.IdleShutdownMinutes);

if (config.GlitchEnabled && !config.GlitchConfigured)
{
    log.LogWarning("BBS_WH_GLITCH_ENABLED is set but the title id/token are missing — the glitch.fun gateway stays OFF.");
}
else if (glitch.Enabled)
{
    log.LogInformation("glitch.fun arcade gateway ON — {Worlds} pool world(s), {Players} players each.",
        config.GlitchWorldCount, config.GlitchMaxPlayers);
}

app.Run();

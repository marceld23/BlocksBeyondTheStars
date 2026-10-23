// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Notifications;
using BlocksBeyondTheStars.WorldHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// One in-process WorldHost for the whole <see cref="WorldHostHttpTests"/> class (#2902): the app
/// <see cref="WorldHostApp.Create"/> builds — the one the container runs — over a temp registry, with
/// <see cref="FakeLauncher"/> standing in for Docker, started on a loopback port Kestrel picks. Building and starting
/// the host is the expensive step, so it is paid once per class.
/// </summary>
public sealed class WorldHostHttpFixture : IAsyncLifetime
{
    public const string AdminToken = "admin-token-for-tests";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_whh_" + Guid.NewGuid().ToString("N"));

    public WebApplication App { get; private set; } = null!;

    public HostRegistry Registry { get; private set; } = null!;

    public WorldHostServices Services { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public WorldHostConfig Config { get; private set; } = null!;

    /// <summary>Every route the app maps, group routes included.</summary>
    public IReadOnlyList<RouteEndpoint> Endpoints
        => ((IEndpointRouteBuilder)App).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Config = new WorldHostConfig
        {
            BindAddress = "127.0.0.1",
            Port = 0, // Kestrel picks a free port; the bound address is read back from App.Urls
            DataDir = _root,
            WorldsDir = Path.Combine(_root, "worlds"),
            WebGlDir = Path.Combine(_root, "webgl"),
            AdminToken = AdminToken,
        };
        Registry = new HostRegistry(Config, Path.Combine(_root, "registry.db"));
        IInstanceLauncher launcher = new FakeLauncher();
        var metrics = new WorldHostMetrics();
        var orchestrator = new WorldOrchestrator(Config, Registry, launcher, metrics: metrics);
        var glitch = new GlitchGateway(Config, Registry, orchestrator);
        Services = new WorldHostServices(Config, Registry, launcher, metrics, orchestrator, glitch, new AdminNotifier(string.Empty, "worldhost"));
        App = WorldHostApp.Create(Services, Array.Empty<string>());
        await App.StartAsync();

        // No auto-redirect: the /play canonicalization is asserted as the 302 it is.
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(App.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose(); // close the keep-alive connection before the host stops, so the stop never waits for it
        await App.StopAsync();
        await App.DisposeAsync();
        Services.Dispose();
        Registry.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }
}

/// <summary>
/// The WorldHost over real HTTP (#2902): the same app the container runs, started in-process on a loopback port. Pins
/// the route table (exactly the 62 routes the portal, the game client, the glitch.fun build and the operator scripts
/// call), checks that every handler's result reaches the response, sends every route one request without credentials
/// to see its gate answer, and walks the owner's side of the world routes and a stranger's refusal on them — so a
/// refactor of the route wiring cannot silently drop, rename or un-guard a route.
///
/// <para>The class belongs in <see cref="RealTimeSensitiveCollection"/> (the #1362 rule): its loopback round trips
/// are a millisecond of work whose continuation, in the parallel suite, waits behind every collection that has not
/// started yet.</para>
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // loopback HTTP round trips starve in the parallel queue (#1362)
public sealed class WorldHostHttpTests : IClassFixture<WorldHostHttpFixture>
{
    /// <summary>Every route of the old single-file Program.cs, as <c>METHOD /pattern</c>.</summary>
    private static readonly string[] ExpectedRoutes =
    {
        "DELETE /api/account",
        "DELETE /api/admin/worlds/{id}",
        "DELETE /api/worlds/{id}",
        "DELETE /api/worlds/{id}/bans/{banId:long}",
        "GET /",
        "GET /admin",
        "GET /admin/stats.json",
        "GET /admin/worlds/{id}",
        "GET /api/admin/reports",
        "GET /api/glitch/save",
        "GET /api/notices",
        "GET /api/stats",
        "GET /api/terms",
        "GET /api/worlds",
        "GET /api/worlds/all",
        "GET /api/worlds/public",
        "GET /api/worlds/{id}/bans",
        "GET /api/worlds/{id}/save",
        "GET /ask",
        "GET /datenschutz",
        "GET /favicon.ico",
        "GET /healthz",
        "GET /impressum",
        "GET /metrics",
        "GET /play",
        "GET /rules",
        "GET /worlds",
        "OPTIONS /api/glitch/{**rest}",
        "POST /admin/announce",
        "POST /admin/ban",
        "POST /admin/glitch/ban",
        "POST /admin/reports/{id:long}/close",
        "POST /admin/reset-password",
        "POST /admin/worlds/{id}/delete",
        "POST /admin/worlds/{id}/kill",
        "POST /admin/worlds/{id}/restart",
        "POST /admin/worlds/{id}/stop",
        "POST /admin/worlds/{id}/wake",
        "POST /api/accept-terms",
        "POST /api/account/password",
        "POST /api/account/recovery-codes",
        "POST /api/admin/announce",
        "POST /api/admin/ban",
        "POST /api/admin/reports/{id:long}/close",
        "POST /api/admin/reset-password",
        "POST /api/glitch/heartbeat",
        "POST /api/glitch/save",
        "POST /api/glitch/save/resolve",
        "POST /api/glitch/session",
        "POST /api/login",
        "POST /api/notices/ack",
        "POST /api/recover",
        "POST /api/reports",
        "POST /api/signup",
        "POST /api/worlds",
        "POST /api/worlds/{id}/bans",
        "POST /api/worlds/{id}/join",
        "POST /api/worlds/{id}/kick",
        "POST /api/worlds/{id}/password",
        "POST /api/worlds/{id}/save",
        "POST /api/worlds/{id}/stop",
        "POST /api/worlds/{id}/visibility",
    };

    private readonly WorldHostHttpFixture _host;

    public WorldHostHttpTests(WorldHostHttpFixture host) => _host = host;

    [Fact]
    public void RouteTable_IsExactlyTheSixtyTwoRoutesOfTheOldProgramCs()
    {
        var actual = _host.Endpoints
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(method => $"{method} {Normalize(e.RoutePattern.RawText)}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(62, ExpectedRoutes.Length);
        Assert.Equal(ExpectedRoutes.OrderBy(x => x, StringComparer.Ordinal), actual);
    }

    /// <summary>Every handler hands its <see cref="IResult"/> to the framework. A lambda over <c>HttpContext</c> alone
    /// that returns a <c>Task</c> binds to the <c>RequestDelegate</c> overload instead, whose result is discarded — the
    /// route then answers an empty 200 whatever the handler decided (a 401 included).</summary>
    [Fact]
    public void EveryRouteHandler_ReturnsItsResult_InsteadOfDiscardingIt()
    {
        var discarding = _host.Endpoints
            .Where(e => e.Metadata.GetMetadata<MethodInfo>() is not { } handler
                        || (handler.ReturnType != typeof(IResult) && handler.ReturnType != typeof(Task<IResult>)))
            .Select(e => e.DisplayName)
            .ToList();

        Assert.Empty(discarding);
    }

    [Fact]
    public async Task Stats_AnswerTheCachedJsonSnapshotAsync()
    {
        using var response = await _host.Client.GetAsync("/api/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("worlds").TryGetProperty("created", out _));
        Assert.True(json.RootElement.GetProperty("players").TryGetProperty("registered", out _));
    }

    [Fact]
    public async Task Healthz_AnswersOkAsync()
    {
        using var response = await _host.Client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Terms_CarryTheConfiguredVersionAsync()
    {
        using var response = await _host.Client.GetAsync("/api/terms?lang=de");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_host.Config.TermsVersion, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("de", json.RootElement.GetProperty("lang").GetString());
    }

    [Fact]
    public async Task Signup_ThenTheOwnWorldList_IsEmpty_AndNeedsTheBearerAsync()
    {
        string token = await SignUpAsync("HttpTester");

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/worlds"))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, json.RootElement.GetProperty("worlds").GetArrayLength());
        }

        using var anonymous = await _host.Client.GetAsync("/api/worlds");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task AdminApi_RequiresTheAdminTokenAsync()
    {
        using (var anonymous = await _host.Client.GetAsync("/api/admin/reports"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/reports");
        request.Headers.Add("X-Admin-Token", WorldHostHttpFixture.AdminToken);
        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("reports").GetArrayLength());
    }

    [Fact]
    public async Task AdminUi_IsOffUntilCredentialsAreConfiguredAsync()
    {
        using var response = await _host.Client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Basic", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlitchGateway_Disabled_Answers404_ButPreflightsAsync()
    {
        using (var session = await _host.Client.PostAsync("/api/glitch/session", Json("""{"installId":"x"}""")))
        {
            Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
        }

        using var preflight = await _host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/glitch/session"));
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
    }

    /// <summary>One request per route WITHOUT any credential: every gated route answers its gate (401 for the
    /// bearer, admin-token and admin-UI groups), every public one its anonymous answer.</summary>
    [Theory]
    [InlineData("GET", "/healthz", null, 200)]
    [InlineData("GET", "/metrics", null, 200)]
    [InlineData("GET", "/api/stats", null, 200)]
    [InlineData("GET", "/", null, 200)]
    [InlineData("GET", "/worlds", null, 200)]
    [InlineData("GET", "/rules", null, 200)]
    [InlineData("GET", "/impressum", null, 200)]
    [InlineData("GET", "/datenschutz", null, 200)]
    [InlineData("GET", "/favicon.ico", null, 200)]
    [InlineData("GET", "/play", null, 302)]
    [InlineData("GET", "/play/", null, 200)]
    [InlineData("GET", "/ask", null, 404)]
    [InlineData("GET", "/ask?domain=localhost", null, 200)]
    [InlineData("GET", "/ask?domain=w-nothere.localhost", null, 404)]
    [InlineData("OPTIONS", "/api/glitch/save", null, 204)]
    [InlineData("POST", "/api/glitch/heartbeat", """{"installId":"x"}""", 404)]
    [InlineData("GET", "/api/glitch/save?installId=x", null, 404)]
    [InlineData("POST", "/api/glitch/save", """{"installId":"x","payload":""}""", 404)]
    [InlineData("POST", "/api/glitch/save/resolve", """{"installId":"x","saveId":"s","conflictId":"c","choice":"keep_server"}""", 404)]
    [InlineData("GET", "/api/terms", null, 200)]
    [InlineData("POST", "/api/login", """{"name":"nobody-here","password":"wrong-password"}""", 401)]
    [InlineData("GET", "/api/notices", null, 401)]
    [InlineData("POST", "/api/notices/ack", "{}", 401)]
    [InlineData("POST", "/api/accept-terms", null, 401)]
    [InlineData("POST", "/api/account/password", """{"oldPassword":"a","newPassword":"b"}""", 401)]
    [InlineData("POST", "/api/recover", """{"name":"nobody-here","code":"x","newPassword":"longenough1"}""", 400)]
    [InlineData("POST", "/api/account/recovery-codes", """{"password":"x"}""", 401)]
    [InlineData("DELETE", "/api/account", null, 401)]
    [InlineData("POST", "/api/reports", """{"reportedName":"x","category":"chat"}""", 401)]
    [InlineData("POST", "/api/admin/reset-password", """{"accountId":"x"}""", 401)]
    [InlineData("GET", "/api/admin/reports", null, 401)]
    [InlineData("POST", "/api/admin/reports/1/close", """{"status":"reviewed"}""", 401)]
    [InlineData("POST", "/api/admin/ban", """{"accountId":"x","banned":true}""", 401)]
    [InlineData("POST", "/api/admin/announce", """{"kind":0}""", 401)]
    [InlineData("DELETE", "/api/admin/worlds/abc", null, 401)]
    [InlineData("GET", "/admin", null, 401)]
    [InlineData("GET", "/admin/worlds/abc", null, 401)]
    [InlineData("GET", "/admin/stats.json", null, 401)]
    [InlineData("POST", "/admin/reports/1/close", null, 401)]
    [InlineData("POST", "/admin/reset-password", null, 401)]
    [InlineData("POST", "/admin/ban", null, 401)]
    [InlineData("POST", "/admin/worlds/abc/stop", null, 401)]
    [InlineData("POST", "/admin/worlds/abc/kill", null, 401)]
    [InlineData("POST", "/admin/glitch/ban", null, 401)]
    [InlineData("POST", "/admin/announce", null, 401)]
    [InlineData("POST", "/admin/worlds/abc/restart", null, 401)]
    [InlineData("POST", "/admin/worlds/abc/wake", null, 401)]
    [InlineData("POST", "/admin/worlds/abc/delete", null, 401)]
    [InlineData("GET", "/api/worlds", null, 401)]
    [InlineData("GET", "/api/worlds/public", null, 401)]
    [InlineData("GET", "/api/worlds/all", null, 401)]
    [InlineData("POST", "/api/worlds", """{"name":"w"}""", 401)]
    [InlineData("POST", "/api/worlds/abc/join", """{"playerName":"p"}""", 401)]
    [InlineData("POST", "/api/worlds/abc/password", """{"password":null}""", 401)]
    [InlineData("POST", "/api/worlds/abc/visibility", """{"public":true}""", 401)]
    [InlineData("GET", "/api/worlds/abc/bans", null, 401)]
    [InlineData("POST", "/api/worlds/abc/bans", "{}", 401)]
    [InlineData("DELETE", "/api/worlds/abc/bans/1", null, 401)]
    [InlineData("POST", "/api/worlds/abc/kick", """{"playerName":"p"}""", 401)]
    [InlineData("POST", "/api/worlds/abc/stop", null, 401)]
    [InlineData("DELETE", "/api/worlds/abc", null, 401)]
    [InlineData("POST", "/api/worlds/abc/save", null, 401)]
    [InlineData("GET", "/api/worlds/abc/save", null, 401)]
    public async Task EveryRoute_AnswersItsGate_WithoutCredentialsAsync(string method, string path, string? body, int expectedStatus)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body != null)
        {
            request.Content = Json(body);
        }

        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    /// <summary>The owner's side of the world routes, end to end over the bearer session: create, list, password,
    /// visibility, ban list, unban, the kick and save guards, and an unknown world id.</summary>
    [Fact]
    public async Task OwnerRoutes_ServeTheOwnersOwnWorldAsync()
    {
        string token = await SignUpAsync("HttpOwner");

        using var created = await SendAsync(HttpMethod.Post, "/api/worlds", token, """{"name":"Owner World"}""");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var world = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        string id = world.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("Owner World", world.RootElement.GetProperty("name").GetString());
        Assert.False(world.RootElement.GetProperty("hasPassword").GetBoolean());

        using (var list = await SendAsync(HttpMethod.Get, "/api/worlds", token))
        {
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal(id, json.RootElement.GetProperty("worlds")[0].GetProperty("id").GetString());
        }

        using (var password = await SendAsync(HttpMethod.Post, $"/api/worlds/{id}/password", token, """{"password":"secret1"}"""))
        {
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            Assert.Equal("""{"hasPassword":true,"isPublic":false}""", await password.Content.ReadAsStringAsync());
        }

        using (var visibility = await SendAsync(HttpMethod.Post, $"/api/worlds/{id}/visibility", token, """{"public":true}"""))
        {
            Assert.Equal(HttpStatusCode.OK, visibility.StatusCode);
            Assert.Equal("""{"isPublic":true}""", await visibility.Content.ReadAsStringAsync());
        }

        using (var bans = await SendAsync(HttpMethod.Get, $"/api/worlds/{id}/bans", token))
        {
            Assert.Equal(HttpStatusCode.OK, bans.StatusCode);
            Assert.Equal("""{"bans":[],"visitors":[]}""", await bans.Content.ReadAsStringAsync());
        }

        using (var unban = await SendAsync(HttpMethod.Delete, $"/api/worlds/{id}/bans/1", token))
        {
            Assert.Equal(HttpStatusCode.OK, unban.StatusCode);
        }

        using (var kick = await SendAsync(HttpMethod.Post, $"/api/worlds/{id}/kick", token, """{"playerName":""}"""))
        {
            Assert.Equal(HttpStatusCode.BadRequest, kick.StatusCode);
            Assert.Contains("\"code\":\"player_name_invalid\"", await kick.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var download = await SendAsync(HttpMethod.Get, $"/api/worlds/{id}/save", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, download.StatusCode);
            Assert.Contains("\"code\":\"save_missing\"", await download.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var upload = await SendAsync(HttpMethod.Post, $"/api/worlds/{id}/save", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
            Assert.Contains("\"code\":\"upload_empty\"", await upload.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using var unknown = await SendAsync(HttpMethod.Post, "/api/worlds/000000000000/join", token, """{"playerName":"Owner"}""");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    /// <summary>A signed-in account that does not own the world gets 403 with the usual JSON error body on every
    /// owner-only route, and the world stays as it was (#2919). These used to answer <c>Results.Forbid()</c>, which
    /// needs an authentication scheme the WorldHost never registers — the request ended as a 500.</summary>
    [Fact]
    public async Task OwnerRoutes_RefuseAStranger_With403AndTheJsonErrorAsync()
    {
        string owner = await SignUpAsync("HttpWorldOwner");
        string stranger = await SignUpAsync("HttpStranger");
        using var created = await SendAsync(HttpMethod.Post, "/api/worlds", owner, """{"name":"Not Yours"}""");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var world = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        string id = world.RootElement.GetProperty("id").GetString()!;

        var ownerOnly = new (HttpMethod Method, string Path, string? Body)[]
        {
            (HttpMethod.Post, $"/api/worlds/{id}/password", """{"password":"secret1"}"""),
            (HttpMethod.Post, $"/api/worlds/{id}/visibility", """{"public":true}"""),
            (HttpMethod.Get, $"/api/worlds/{id}/bans", null),
            (HttpMethod.Post, $"/api/worlds/{id}/bans", """{"playerName":"HttpWorldOwner"}"""),
            (HttpMethod.Delete, $"/api/worlds/{id}/bans/1", null),
            (HttpMethod.Post, $"/api/worlds/{id}/kick", """{"playerName":"HttpWorldOwner"}"""),
            (HttpMethod.Post, $"/api/worlds/{id}/stop", null),
            (HttpMethod.Delete, $"/api/worlds/{id}", null),
            (HttpMethod.Post, $"/api/worlds/{id}/save", null),
            (HttpMethod.Get, $"/api/worlds/{id}/save", null),
        };
        foreach (var (method, path, body) in ownerOnly)
        {
            using var response = await SendAsync(method, path, stranger, body);
            string route = $"{method} {path}";
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route} answered {(int)response.StatusCode}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Only the world's owner can do this.", json.RootElement.GetProperty("error").GetString());
            Assert.Equal("not_world_owner", json.RootElement.GetProperty("code").GetString());
        }

        using var list = await SendAsync(HttpMethod.Get, "/api/worlds", owner);
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var mine = listed.RootElement.GetProperty("worlds")[0];
        Assert.Equal(id, mine.GetProperty("id").GetString());
        Assert.False(mine.GetProperty("hasPassword").GetBoolean());
        Assert.False(mine.GetProperty("isPublic").GetBoolean());
    }

    /// <summary>MapGroup joins patterns textually, so a group's "" route reads "/admin/" — the pin compares paths
    /// without their outer slashes.</summary>
    private static string Normalize(string? raw) => "/" + (raw ?? string.Empty).Trim('/');

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private async Task<string> SignUpAsync(string name)
    {
        string body = $$"""{"name":"{{name}}","password":"longenough1","acceptedTermsVersion":{{_host.Config.TermsVersion}}}""";
        using var response = await _host.Client.PostAsync("/api/signup", Json(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HostRegistry.RecoveryCodeCount, json.RootElement.GetProperty("recoveryCodes").GetArrayLength());
        string token = json.RootElement.GetProperty("sessionToken").GetString()!;
        Assert.False(string.IsNullOrEmpty(token));
        return token;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body != null)
        {
            request.Content = Json(body);
        }

        return await _host.Client.SendAsync(request);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Text;
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.WorldHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The glitch.fun arcade gateway: install validation against a faked Glitch API, stable suffixed guest
/// names, lazy pool creation, capacity-aware world picking, install bans, the heartbeat relay (title
/// token stays server-side; bans answer 403) and the CORS origin gate.
/// </summary>
public sealed class GlitchGatewayTests : IDisposable
{
    private const string Install = "17d0c5b6-d1e4-4cdf-ab2a-ae9854da9339";

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public GlitchGatewayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_glitch_gw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private static WorldHostConfig NewConfig() => new()
    {
        GlitchEnabled = true,
        GlitchTitleId = "title-1",
        GlitchTitleToken = "token-1",
        GlitchWorldCount = 2,
        GlitchMaxPlayers = 2,
        WakeTimeoutSeconds = 5,
    };

    private HostRegistry NewRegistry(WorldHostConfig config)
    {
        var registry = new HostRegistry(config, Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    private sealed class FakeLauncher : IInstanceLauncher
    {
        public int StartCount;
        public readonly HashSet<string> Running = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            StartCount++;
            string id = "container-" + StartCount;
            Running.Add(id);
            return id;
        }

        public void Stop(string containerId) => Running.Remove(containerId);

        public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    /// <summary>Canned Glitch API: answers /validate per install id (default: valid with a user name),
    /// the cloud-save routes (list/store/resolve) and the install heartbeat; records every request
    /// incl. the Authorization header.</summary>
    private sealed class FakeGlitchApi : HttpMessageHandler
    {
        public readonly List<(string Url, string? Auth, string Body)> Requests = new();
        public readonly Dictionary<string, (HttpStatusCode Status, string Body)> ValidateByInstall = new();
        public (HttpStatusCode Status, string Body) HeartbeatAnswer = (HttpStatusCode.OK, """{"data":{"id":"x"}}""");
        public (HttpStatusCode Status, string Body) SavesListAnswer = (HttpStatusCode.OK, """{"data":[]}""");
        public (HttpStatusCode Status, string Body) SaveStoreAnswer = (HttpStatusCode.Created, """{"data":{"version":1}}""");
        public (HttpStatusCode Status, string Body) SaveResolveAnswer = (HttpStatusCode.OK, """{"data":{"version":2}}""");
        public bool ThrowOnSend;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            if (ThrowOnSend)
            {
                throw new HttpRequestException("no route to glitch");
            }

            string path = request.RequestUri!.AbsolutePath;
            (HttpStatusCode Status, string Body) answer;
            if (path.EndsWith("/validate", StringComparison.Ordinal))
            {
                string installId = request.RequestUri.Segments[^2].TrimEnd('/');
                answer = ValidateByInstall.TryGetValue(installId, out var configured)
                    ? configured
                    : (HttpStatusCode.OK, """{"valid":true,"user_name":"Gamer123"}""");
            }
            else if (path.EndsWith("/resolve", StringComparison.Ordinal))
            {
                answer = SaveResolveAnswer;
            }
            else if (path.Contains("/saves", StringComparison.Ordinal))
            {
                answer = request.Method == HttpMethod.Get ? SavesListAnswer : SaveStoreAnswer;
            }
            else
            {
                answer = HeartbeatAnswer;
            }

            return new HttpResponseMessage(answer.Status)
            {
                Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private (GlitchGateway Gateway, HostRegistry Registry, FakeGlitchApi Api, WorldHostConfig Config) NewGateway(
        WorldHostConfig? config = null, Func<WorldRecord, Task<string?>>? statusReader = null)
    {
        config ??= NewConfig();
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = new WorldOrchestrator(config, registry, launcher,
            w => Task.FromResult(launcher.IsRunning(w.ContainerId)));
        var api = new FakeGlitchApi();
        var gateway = new GlitchGateway(config, registry, orchestrator, api,
            statusReader: statusReader ?? (_ => Task.FromResult<string?>(null)));
        return (gateway, registry, api, config);
    }

    // ---------------- Session grants ----------------

    [Fact]
    public async Task Session_HappyPath_CreatesPool_MintsGuestToken_AndRecordsTheGuestAsync()
    {
        var (gateway, registry, api, _) = NewGateway();

        var result = await gateway.SessionAsync(Install);

        Assert.True(result.Ok, result.Error);
        Assert.StartsWith("Gamer123-", result.PlayerName, StringComparison.Ordinal); // Glitch user_name + stable suffix
        Assert.NotEmpty(result.JoinToken);
        Assert.StartsWith("wss://w-", result.WssUrl, StringComparison.Ordinal);

        // The pool was lazily created; the world the guest landed on is an arcade world.
        var pool = registry.ListWorldsByChannel(WorldChannel.Glitch);
        Assert.Equal(2, pool.Count);
        var world = pool.Single(w => w.Id == result.WorldId);

        // The token names the synthetic guest identity — the instance-side gate accepts it as-is.
        Assert.True(HostedJoinToken.TryValidate(world.JoinSecret, world.Id, result.JoinToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out string accountId, out string playerName, out string error), error);
        Assert.Equal("glitch:" + Install, accountId);
        Assert.Equal(result.PlayerName, playerName);

        // The validate call went out with the server-side title token; the guest is on the ban-target list.
        Assert.Contains(api.Requests, r => r.Url.Contains("/validate") && r.Auth == "Bearer token-1");
        Assert.Equal(Install, Assert.Single(registry.ListGlitchGuests()).InstallId);
    }

    [Fact]
    public async Task Session_CreatesTheInstallBeforeValidating_GlitchRequiredCallOrderAsync()
    {
        // Glitch's documented call order: create/resume the install FIRST, then validate — validating
        // an install the platform has never seen answers 403 (the launch-day arcade join failure).
        var (gateway, _, api, _) = NewGateway();

        var result = await gateway.SessionAsync(Install);

        Assert.True(result.Ok, result.Error);
        int createIndex = api.Requests.FindIndex(r => r.Url.EndsWith("/installs", StringComparison.Ordinal));
        int validateIndex = api.Requests.FindIndex(r => r.Url.EndsWith("/validate", StringComparison.Ordinal));
        Assert.True(createIndex >= 0, "the install create/resume call must happen");
        Assert.True(validateIndex > createIndex, "create/resume must precede validate");
        Assert.Contains("\"user_install_id\":\"" + Install + "\"", api.Requests[createIndex].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_StableIdentity_SameInstallGetsTheSameNameEveryVisitAsync()
    {
        var (gateway, _, _, _) = NewGateway();

        var first = await gateway.SessionAsync(Install);
        var second = await gateway.SessionAsync(Install);

        Assert.True(first.Ok && second.Ok);
        Assert.Equal(first.PlayerName, second.PlayerName);
    }

    [Fact]
    public async Task Session_DisabledGateway_RefusesAsync()
    {
        var config = NewConfig();
        config.GlitchTitleToken = string.Empty; // enabled flag without credentials = off
        var (gateway, _, _, _) = NewGateway(config);

        var result = await gateway.SessionAsync(Install);

        Assert.False(result.Ok);
        Assert.Equal("The glitch.fun gateway is disabled.", result.Error);
    }

    [Fact]
    public async Task Session_InvalidInstall_RefusedWithoutTouchingThePoolAsync()
    {
        var (gateway, registry, api, _) = NewGateway();
        api.ValidateByInstall[Install] = (HttpStatusCode.Forbidden, """{"valid":false,"reason":"LICENSE_EXPIRED"}""");

        var result = await gateway.SessionAsync(Install);

        Assert.False(result.Ok);
        Assert.Equal("This install could not be verified with glitch.fun.", result.Error);
        Assert.Empty(registry.ListWorldsByChannel(WorldChannel.Glitch)); // no pool for invalid installs
        Assert.Empty(registry.ListGlitchGuests());
    }

    [Fact]
    public async Task Session_MalformedInstallId_RefusedWithoutCallingGlitchAsync()
    {
        var (gateway, _, api, _) = NewGateway();

        var result = await gateway.SessionAsync("nope !"); // spaces/short — not an install id

        Assert.False(result.Ok);
        Assert.Equal("This install could not be verified with glitch.fun.", result.Error);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Session_BannedInstall_RefusedWithTheReasonAsync()
    {
        var (gateway, registry, _, _) = NewGateway();
        registry.SetGlitchBanned(Install, banned: true, reason: "griefing");

        var result = await gateway.SessionAsync(Install);

        Assert.False(result.Ok);
        Assert.Equal("This player is banned: griefing", result.Error);
    }

    [Fact]
    public async Task Session_PoolCreationIsIdempotentAsync()
    {
        var (gateway, registry, _, _) = NewGateway();

        Assert.True((await gateway.SessionAsync(Install)).Ok);
        Assert.True((await gateway.SessionAsync("5f9dd262-840c-4df8-a30b-366fc0c7e1d8")).Ok);

        Assert.Equal(2, registry.ListWorldsByChannel(WorldChannel.Glitch).Count);
    }

    [Fact]
    public async Task Session_PicksRunningWorldWithHeadroom_ThenWakesSleeping_ThenReportsFullAsync()
    {
        // Status reader script: world with headroom → joined 1/2; full world → 2/2.
        var full = new HashSet<string>(StringComparer.Ordinal);
        Task<string?> StatusAsync(WorldRecord w) =>
            Task.FromResult<string?>(full.Contains(w.Id) ? """{"joinedPlayers":2}""" : """{"joinedPlayers":1}""");

        var (gateway, registry, _, _) = NewGateway(statusReader: StatusAsync);

        // First guest wakes Arcade 1 (pool created on demand, nothing running yet).
        var first = await gateway.SessionAsync(Install);
        Assert.True(first.Ok, first.Error);

        // Second guest: Arcade 1 is running with headroom (1/2) → reuse, no second wake.
        var second = await gateway.SessionAsync("5f9dd262-840c-4df8-a30b-366fc0c7e1d8");
        Assert.True(second.Ok, second.Error);
        Assert.Equal(first.WorldId, second.WorldId);

        // Arcade 1 fills up → the next guest wakes Arcade 2.
        full.Add(first.WorldId);
        var third = await gateway.SessionAsync("11111111-2222-3333-4444-555555555555");
        Assert.True(third.Ok, third.Error);
        Assert.NotEqual(first.WorldId, third.WorldId);

        // Both running and full → the friendly arcade-full answer.
        full.Add(third.WorldId);
        var fourth = await gateway.SessionAsync("66666666-7777-8888-9999-000000000000");
        Assert.False(fourth.Ok);
        Assert.Equal("All arcade worlds are full right now — please try again in a few minutes.", fourth.Error);
    }

    // ---------------- Player names ----------------

    [Theory]
    [InlineData("Justus")]      // developer-reserved
    [InlineData("hurensohn99")] // blocked word
    [InlineData("")]            // nothing usable
    public void ResolvePlayerName_FallsBackToExplorer(string requested)
    {
        var (gateway, _, _, _) = NewGateway();

        string name = gateway.ResolvePlayerName(requested, glitchUserName: null, Install);

        Assert.StartsWith("Explorer-", name, StringComparison.Ordinal);
        Assert.Equal("Explorer-".Length + 3, name.Length); // stable 3-hex-char suffix
    }

    [Fact]
    public void ResolvePlayerName_PrefersRequested_ThenGlitchName_AndStaysStablePerInstall()
    {
        var (gateway, _, _, _) = NewGateway();

        Assert.StartsWith("Maxi-", gateway.ResolvePlayerName("Maxi", "Gamer123", Install), StringComparison.Ordinal);
        Assert.StartsWith("Gamer123-", gateway.ResolvePlayerName(null, "Gamer123", Install), StringComparison.Ordinal);
        Assert.Equal(
            gateway.ResolvePlayerName(null, "Gamer123", Install),
            gateway.ResolvePlayerName(null, "Gamer123", Install));

        // Overlong/dirty Glitch names are trimmed so the suffix still fits the 24-char instance limit.
        string longName = gateway.ResolvePlayerName(null, new string('x', 60) + "\n", Install);
        Assert.True(longName.Length <= 24, longName);
    }

    // ---------------- Heartbeat relay ----------------

    [Fact]
    public async Task Heartbeat_RelaysWithTheServerSideTokenAsync()
    {
        var (gateway, _, api, _) = NewGateway();

        var (status, body) = await gateway.RelayHeartbeatAsync(Install, "sess-1", "web", "0.7.8");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("\"ok\":true", body, StringComparison.Ordinal);
        var sent = Assert.Single(api.Requests);
        Assert.EndsWith("/installs", sent.Url, StringComparison.Ordinal);
        Assert.Equal("Bearer token-1", sent.Auth);
        Assert.Contains("\"user_install_id\":\"" + Install + "\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains("\"session_id\":\"sess-1\"", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Heartbeat_BannedInstall_Answers403WithoutRelayingAsync()
    {
        var (gateway, registry, api, _) = NewGateway();
        registry.SetGlitchBanned(Install, banned: true, reason: "griefing");

        var (status, body) = await gateway.RelayHeartbeatAsync(Install, null, "web", "0.7.8");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Contains("banned", body, StringComparison.Ordinal);
        Assert.Empty(api.Requests); // never even talks to Glitch for a banned install
    }

    [Fact]
    public async Task Heartbeat_GlitchRefusal_PassesTheStatusThrough_ButUnreachableIs503Async()
    {
        var (gateway, _, api, _) = NewGateway();

        api.HeartbeatAnswer = (HttpStatusCode.Forbidden, """{"error":"license expired"}""");
        Assert.Equal(StatusCodes.Status403Forbidden, (await gateway.RelayHeartbeatAsync(Install, null, "web", "1")).Status);

        // Glitch being down must not read as a revoked license.
        api.ThrowOnSend = true;
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, (await gateway.RelayHeartbeatAsync(Install, null, "web", "1")).Status);
    }

    [Fact]
    public async Task Heartbeat_RateLimit_KeysOnTheInstallAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = new WorldOrchestrator(config, registry, launcher, w => Task.FromResult(true));
        var api = new FakeGlitchApi();
        var gateway = new GlitchGateway(config, registry, orchestrator, api,
            heartbeatLimit: new RateLimiter(1, TimeSpan.FromMinutes(1)), statusReader: _ => Task.FromResult<string?>(null));

        Assert.Equal(StatusCodes.Status200OK, (await gateway.RelayHeartbeatAsync(Install, null, "web", "1")).Status);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await gateway.RelayHeartbeatAsync(Install, null, "web", "1")).Status);
        Assert.Equal(StatusCodes.Status200OK, (await gateway.RelayHeartbeatAsync("5f9dd262-840c-4df8-a30b-366fc0c7e1d8", null, "web", "1")).Status);
    }

    // ---------------- Cloud-save relay (browser singleplayer) ----------------

    [Fact]
    public async Task LoadSave_ReturnsSlotZeroVersionAndPayload_Or404WhenEmptyAsync()
    {
        var (gateway, _, api, _) = NewGateway();

        // No saves yet → the friendly 404.
        Assert.Equal(StatusCodes.Status404NotFound, (await gateway.LoadSaveAsync(Install)).Status);

        // Slot 0 exists (a slot-3 decoy proves the slot filter) → 200 with version + payload.
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("world-bytes"));
        api.SavesListAnswer = (HttpStatusCode.OK,
            $$"""{"data":[{"slot_index":3,"version":9,"payload":"zzzz"},{"slot_index":0,"version":5,"payload":"{{payload}}"}]}""");
        var (status, body) = await gateway.LoadSaveAsync(Install);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("\"version\":5", body, StringComparison.Ordinal);
        Assert.Contains(payload, body, StringComparison.Ordinal);
        Assert.Contains(api.Requests, r => r.Url.Contains("include_payload=1") && r.Auth == "Bearer token-1");
    }

    [Fact]
    public async Task LoadSave_GuestInstall_PassesThe403ThroughAsync()
    {
        var (gateway, _, api, _) = NewGateway();
        api.SavesListAnswer = (HttpStatusCode.Forbidden, """{"error":"GUEST_NOT_ALLOWED"}""");

        var (status, body) = await gateway.LoadSaveAsync(Install);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Contains("glitch_guest_saves", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreSave_ComputesTheChecksumOverDecodedBytes_AndReturnsTheNewVersionAsync()
    {
        var (gateway, _, api, _) = NewGateway();
        byte[] raw = Encoding.UTF8.GetBytes("world-bytes");
        string expectedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant();
        api.SaveStoreAnswer = (HttpStatusCode.Created, """{"data":{"version":7}}""");

        var (status, body) = await gateway.StoreSaveAsync(Install, Convert.ToBase64String(raw), baseVersion: 6);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("\"version\":7", body, StringComparison.Ordinal);
        var sent = Assert.Single(api.Requests, r => r.Url.EndsWith("/saves", StringComparison.Ordinal));
        Assert.Contains($"\"checksum\":\"{expectedChecksum}\"", sent.Body, StringComparison.Ordinal); // over DECODED bytes
        Assert.Contains("\"slot_index\":0", sent.Body, StringComparison.Ordinal);
        Assert.Contains("\"base_version\":6", sent.Body, StringComparison.Ordinal);
        Assert.Contains("\"save_type\":\"auto\"", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreSave_Conflict409_SurfacesTheConflictIdsForTheExplicitResolveAsync()
    {
        var (gateway, _, api, _) = NewGateway();
        api.SaveStoreAnswer = ((HttpStatusCode)409,
            """{"status":"conflict","save_id":"SAVE-1","conflict_id":"CONF-1","server_version":9,"your_base_version":5}""");

        var (status, body) = await gateway.StoreSaveAsync(Install, Convert.ToBase64String(new byte[] { 1 }), baseVersion: 5);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("\"saveId\":\"SAVE-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"conflictId\":\"CONF-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"serverVersion\":9", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreSave_RejectsGarbageAndOversizeBeforeTouchingGlitchAsync()
    {
        var (gateway, registry, api, _) = NewGateway();

        Assert.Equal(StatusCodes.Status400BadRequest, (await gateway.StoreSaveAsync(Install, "not base64!!", 0)).Status);
        Assert.Equal(StatusCodes.Status400BadRequest, (await gateway.StoreSaveAsync(Install, string.Empty, 0)).Status);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge,
            (await gateway.StoreSaveAsync(Install, Convert.ToBase64String(new byte[10 * 1024 * 1024 + 1]), 0)).Status);
        Assert.Empty(api.Requests);

        // Banned installs can't push saves either.
        registry.SetGlitchBanned(Install, banned: true, reason: "griefing");
        Assert.Equal(StatusCodes.Status403Forbidden,
            (await gateway.StoreSaveAsync(Install, Convert.ToBase64String(new byte[] { 1 }), 0)).Status);
    }

    [Fact]
    public async Task StoreSave_RateLimit_KeysOnTheInstallAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var orchestrator = new WorldOrchestrator(config, registry, new FakeLauncher(), w => Task.FromResult(true));
        var gateway = new GlitchGateway(config, registry, orchestrator, new FakeGlitchApi(),
            statusReader: _ => Task.FromResult<string?>(null), saveLimit: new RateLimiter(1, TimeSpan.FromHours(1)));
        string payload = Convert.ToBase64String(new byte[] { 1 });

        Assert.Equal(StatusCodes.Status200OK, (await gateway.StoreSaveAsync(Install, payload, 0)).Status);
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await gateway.StoreSaveAsync(Install, payload, 0)).Status);
        Assert.Equal(StatusCodes.Status200OK,
            (await gateway.StoreSaveAsync("5f9dd262-840c-4df8-a30b-366fc0c7e1d8", payload, 0)).Status);
    }

    [Fact]
    public async Task ResolveSave_ForwardsTheChoice_AndRejectsMalformedInputAsync()
    {
        var (gateway, _, api, _) = NewGateway();

        var (status, body) = await gateway.ResolveSaveAsync(Install, "SAVE-1", "CONF-1", "use_client");

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Contains("\"version\":2", body, StringComparison.Ordinal);
        var sent = Assert.Single(api.Requests, r => r.Url.EndsWith("/saves/SAVE-1/resolve", StringComparison.Ordinal));
        Assert.Contains("\"choice\":\"use_client\"", sent.Body, StringComparison.Ordinal);
        Assert.Contains("\"conflict_id\":\"CONF-1\"", sent.Body, StringComparison.Ordinal);

        Assert.Equal(StatusCodes.Status400BadRequest, (await gateway.ResolveSaveAsync(Install, "SAVE-1", "CONF-1", "wipe_everything")).Status);
        Assert.Equal(StatusCodes.Status400BadRequest, (await gateway.ResolveSaveAsync(Install, "", "CONF-1", "keep_server")).Status);
    }

    // ---------------- CORS + container shape ----------------

    [Fact]
    public void ResolveCorsOrigin_EchoesOnlyConfiguredGlitchOrigins()
    {
        var (gateway, _, _, _) = NewGateway();

        Assert.Equal("https://play.glitch.fun", gateway.ResolveCorsOrigin("https://play.glitch.fun"));
        Assert.Equal("https://glitch.fun/", gateway.ResolveCorsOrigin("https://glitch.fun/")); // trailing slash tolerated
        Assert.Null(gateway.ResolveCorsOrigin("https://evil.example"));
        Assert.Null(gateway.ResolveCorsOrigin(null));
    }

    [Fact]
    public void BuildRunArgs_GlitchWorldsGetTheArcadePlayerCap_AndKeepAwakeDisablesIdleExit()
    {
        var config = NewConfig();
        config.MaxPlayersPerWorld = 12;
        config.IdleShutdownMinutes = 20;
        var registry = NewRegistry(config);
        var arcade = registry.CreateGlitchWorld("Glitch Arcade 1").World!;
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var portal = registry.CreateWorld(accountId, "Family World").World!;

        var arcadeArgs = DockerCliLauncher.BuildRunArgs(config, arcade, "saves");
        var portalArgs = DockerCliLauncher.BuildRunArgs(config, portal, "saves");

        Assert.Contains("BBS_MAX_PLAYERS=2", arcadeArgs);
        Assert.Contains("BBS_MAX_PLAYERS=12", portalArgs);
        Assert.Contains("BBS_IDLE_SHUTDOWN_MINUTES=0", arcadeArgs);  // kept awake: never self-exits
        Assert.Contains("BBS_IDLE_SHUTDOWN_MINUTES=20", portalArgs); // portal worlds keep sleeping

        // Tight hosts can opt out — arcade worlds then idle-exit like everyone else.
        config.GlitchKeepAwake = false;
        Assert.Contains("BBS_IDLE_SHUTDOWN_MINUTES=20", DockerCliLauncher.BuildRunArgs(config, arcade, "saves"));
    }

    [Fact]
    public async Task WakePool_CreatesAndWakesEveryArcadeWorld_AndIsIdempotentAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = new WorldOrchestrator(config, registry, launcher,
            w => Task.FromResult(launcher.IsRunning(w.ContainerId)));
        var gateway = new GlitchGateway(config, registry, orchestrator, new FakeGlitchApi(),
            statusReader: _ => Task.FromResult<string?>(null));

        Assert.Equal(2, await gateway.WakePoolAsync()); // fresh pool: both created + woken
        Assert.Equal(2, launcher.StartCount);
        Assert.All(registry.ListWorldsByChannel(WorldChannel.Glitch), w => Assert.Equal(WorldStatus.Running, w.Status));

        Assert.Equal(0, await gateway.WakePoolAsync()); // already running: nothing to do
        Assert.Equal(2, launcher.StartCount);

        // Keep-awake off → the pass is a no-op even with stopped worlds.
        config.GlitchKeepAwake = false;
        launcher.Running.Clear();
        orchestrator.Reap();
        Assert.Equal(0, await gateway.WakePoolAsync());
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — temp cleanup only
        }
    }
}

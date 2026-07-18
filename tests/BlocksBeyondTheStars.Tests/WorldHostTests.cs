// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hosted-worlds control plane (fleet phase 1): accounts (privacy-minimal, PBKDF2), the world registry
/// with its operator quotas and stable port allocation, and the orchestrator's route-or-wake allocation —
/// driven against a fake launcher, so the logic is covered without Docker.
/// </summary>
public sealed class WorldHostTests : IDisposable
{
    private readonly string _root;

    public WorldHostTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_wh_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private HostRegistry NewRegistry(WorldHostConfig? config = null)
    {
        var registry = new HostRegistry(
            config ?? new WorldHostConfig(),
            System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    private readonly List<HostRegistry> _registries = new();

    /// <summary>In-memory stand-in for Docker: containers "run" until stopped (or told to die).</summary>
    private sealed class FakeLauncher : IInstanceLauncher
    {
        public int StartCount;
        public bool FailStart;
        public readonly HashSet<string> Running = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            if (FailStart)
            {
                throw new InvalidOperationException("docker run failed (simulated)");
            }

            StartCount++;
            string id = "container-" + StartCount;
            Running.Add(id);
            return id;
        }

        public void Stop(string containerId) => Running.Remove(containerId);

        public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    /// <summary>Orchestrator whose "instance is healthy" probe is simply "its fake container runs".</summary>
    private static WorldOrchestrator NewOrchestrator(HostRegistry registry, FakeLauncher launcher, WorldHostConfig config)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)));

    // ---------------- Password hashing ----------------

    [Fact]
    public void PasswordHash_Verifies_AndRejectsWrongPassword()
    {
        string stored = PasswordHasher.Hash("correct horse battery");
        Assert.True(PasswordHasher.Verify("correct horse battery", stored));
        Assert.False(PasswordHasher.Verify("wrong", stored));
        Assert.False(PasswordHasher.Verify("correct horse battery", "garbage-record"));
    }

    // ---------------- Accounts & sessions ----------------

    [Fact]
    public void Signup_Login_And_SessionResolution_Work()
    {
        var registry = NewRegistry();

        var (ok, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(ok);
        Assert.Equal("Pilot", registry.ResolveSession(session)!.Name);

        var login = registry.Login("Pilot", "super-secret-1");
        Assert.NotNull(login);
        Assert.Equal(accountId, login!.Value.AccountId);
        Assert.Equal(accountId, registry.ResolveSession(login.Value.SessionToken)!.Id);

        Assert.Null(registry.Login("Pilot", "wrong-password"));
        Assert.Null(registry.Login("Nobody", "super-secret-1"));
        Assert.Null(registry.ResolveSession("not-a-token"));
        Assert.Null(registry.ResolveSession(null));
    }

    [Fact]
    public void Signup_Rejects_TakenNames_CaseInsensitive_AndInvalidInput()
    {
        var registry = NewRegistry();
        Assert.True(registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms).Ok);

        Assert.False(registry.CreateAccount("pilot", "super-secret-1", acceptedTermsVersion: Terms).Ok);    // taken (NOCASE)
        Assert.False(registry.CreateAccount("ab", "super-secret-1", acceptedTermsVersion: Terms).Ok);       // too short
        Assert.False(registry.CreateAccount("has space", "super-secret-1", acceptedTermsVersion: Terms).Ok); // bad charset
        Assert.False(registry.CreateAccount("Fine", "short", acceptedTermsVersion: Terms).Ok);              // weak password
    }

    // ---------------- World registry ----------------

    [Fact]
    public void CreateWorld_EnforcesQuota_AndAllocatesUniqueStablePorts()
    {
        var config = new WorldHostConfig { MaxWorldsPerAccount = 2, PortRangeStart = 32000 };
        var registry = NewRegistry(config);
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);

        var w1 = registry.CreateWorld(accountId, "First World");
        var w2 = registry.CreateWorld(accountId, "Second World");
        Assert.True(w1.Ok && w2.Ok);
        Assert.NotEqual(w1.World!.Id, w2.World!.Id);
        Assert.Equal(new[] { 32000, 32001 }, new[] { w1.World.HostPort, w2.World.HostPort });
        Assert.Equal(WorldStatus.Stopped, w1.World.Status);

        var w3 = registry.CreateWorld(accountId, "One Too Many");
        Assert.False(w3.Ok); // quota (2) reached
        Assert.Contains("limit", w3.Error, StringComparison.OrdinalIgnoreCase);

        // A deleted world's port returns to the pool (it is the world's stable native endpoint otherwise).
        registry.DeleteWorld(w1.World.Id);
        var w4 = registry.CreateWorld(accountId, "Replacement");
        Assert.True(w4.Ok);
        Assert.Equal(32000, w4.World!.HostPort);
    }

    [Fact]
    public void CreateWorld_ValidatesDisplayName()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);

        Assert.False(registry.CreateWorld(accountId, "").Ok);
        Assert.False(registry.CreateWorld(accountId, "   ").Ok);
        Assert.False(registry.CreateWorld(accountId, new string('x', 41)).Ok);
        Assert.False(registry.CreateWorld(accountId, "evil\nname").Ok);
        Assert.True(registry.CreateWorld(accountId, "Justus' Welt 🚀").Ok); // spaces/unicode are fine — it's only an env VALUE
    }

    [Fact]
    public void FindBySubdomain_ResolvesRealWorlds_AndRejectsGarbage()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: Terms);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.Equal(world.Id, registry.FindBySubdomain(world.Subdomain)!.Id);
        Assert.Null(registry.FindBySubdomain("w-000000000000"));      // well-formed but unknown
        Assert.Null(registry.FindBySubdomain("evil"));                // no prefix
        Assert.Null(registry.FindBySubdomain("w-NOTHEX!"));           // invalid id
    }

    // ---------------- Orchestrator: route-or-wake ----------------

    /// <summary>Signups in tests accept the default rules version 1 — the terms gate has its own tests.</summary>
    private const int Terms = 1;

    private static (string AccountId, AccountRecord Account) NewAccount(HostRegistry registry, string name = "Owner")
    {
        var (ok, error, accountId, session) = registry.CreateAccount(name, "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(ok, error);
        return (accountId, registry.ResolveSession(session)!);
    }

    [Fact]
    public async Task Join_WakesAStoppedWorld_AndIssuesAValidTokenAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5, BaseDomain = "play.example.de", PublicHost = "play.example.de" };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Pilot");

        Assert.Equal(string.Empty, error);
        Assert.NotNull(grant);
        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
        Assert.Equal($"wss://w-{world.Id}.play.example.de", grant!.WssUrl);
        Assert.Equal(world.HostPort, grant.NativePort);

        // The grant's token must satisfy exactly the check the game server runs (Phase 0):
        Assert.True(HostedJoinToken.TryValidate(world.JoinSecret, world.Id, grant.JoinToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var tokenAccount, out var tokenPlayer, out _));
        Assert.Equal(accountId, tokenAccount);
        Assert.Equal("Pilot", tokenPlayer);

        // The token must survive the browser deep-link's cold path — the FIRST WebGL download can take
        // minutes, and the token is only verified once the engine joins (10-minute TTL, was 2).
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(grant.TokenExpiresUnix, now + 540, now + 660);
    }

    [Fact]
    public async Task Join_ReusesTheRunningInstanceAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P1")).Grant);
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P2")).Grant);

        Assert.Equal(1, launcher.StartCount); // second join routed to the live instance, no second container
    }

    [Fact]
    public async Task Reap_MarksIdleExitedWorldsStopped_AndNextJoinRewakesAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        await orchestrator.JoinAsync(world.Id, account, "P1");
        string containerId = registry.GetWorld(world.Id)!.ContainerId;

        // The instance idle-shuts-down (Phase 0) — its container exits on its own.
        launcher.Stop(containerId);
        Assert.Equal(1, orchestrator.Reap());
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);

        // The next join wakes a fresh container.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P1")).Grant);
        Assert.Equal(2, launcher.StartCount);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
    }

    [Fact]
    public async Task Join_FailedStart_LeavesTheWorldStopped_WithAPlayerSafeErrorAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 1 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher { FailStart = true };
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "P1");

        Assert.Null(grant);
        Assert.NotEqual(string.Empty, error);
        Assert.DoesNotContain("docker", error, StringComparison.OrdinalIgnoreCase); // no internals leak to players
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);
    }

    [Fact]
    public async Task Join_RejectsUnknownWorlds_AndBadPlayerNamesAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 1 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.Null((await orchestrator.JoinAsync("000000000000", account, "P1")).Grant);
        Assert.Null((await orchestrator.JoinAsync(world.Id, account, "")).Grant);
        Assert.Null((await orchestrator.JoinAsync(world.Id, account, new string('x', 25))).Grant);
    }

    // ---------------- Reserved developer names ----------------

    [Theory]
    [InlineData("Justus")]      // exact
    [InlineData("justus")]      // case
    [InlineData("Flash-Miner")] // separator trick vs "FlashMiner"
    [InlineData("Ju_Ju")]       // separator trick vs "juju"
    [InlineData("JuMaVeGames")] // "JuMaVe Games" (space is stripped in normalization)
    public void Signup_RejectsReservedNames_WhenNoClaimCodeIsConfigured(string name)
    {
        var registry = NewRegistry(); // default config: claim code empty ⇒ reserved names unclaimable
        var result = registry.CreateAccount(name, "super-secret-1", acceptedTermsVersion: Terms);
        Assert.False(result.Ok);
        Assert.Contains("reserved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signup_WithTheClaimCode_CreatesADeveloperAccount()
    {
        var config = new WorldHostConfig { ReservedClaimCode = "dev-code-123" };
        var registry = NewRegistry(config);

        Assert.False(registry.CreateAccount("Justus", "super-secret-1", "wrong-code", Terms).Ok);

        var (ok, error, _, session) = registry.CreateAccount("Justus", "super-secret-1", "dev-code-123", Terms);
        Assert.True(ok, error);
        Assert.True(registry.ResolveSession(session)!.IsDeveloper);

        // Ordinary names don't need (and don't get) developer status, code or not.
        var (okPlain, _, _, plainSession) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(okPlain);
        Assert.False(registry.ResolveSession(plainSession)!.IsDeveloper);
    }

    [Fact]
    public async Task Join_ReservedInGameName_OnlyForDeveloperAccountsAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5, ReservedClaimCode = "dev-code-123" };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);

        var (accountId, account) = NewAccount(registry, "Pilot");
        var world = registry.CreateWorld(accountId, "My World").World!;

        // A normal account cannot impersonate a developer in-game — on ANY hosted world.
        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Justus");
        Assert.Null(grant);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);

        // The claimed developer account may play under the reserved name.
        var (_, _, _, devSession) = registry.CreateAccount("FlashMiner", "super-secret-1", "dev-code-123", Terms);
        var dev = registry.ResolveSession(devSession)!;
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, dev, "Justus")).Grant);
    }

    // ---------------- Community rules acceptance, bans, reports ----------------

    [Fact]
    public void Signup_RequiresAcceptingTheCurrentRulesVersion()
    {
        var registry = NewRegistry(); // default TermsVersion = 1

        var without = registry.CreateAccount("Pilot", "super-secret-1"); // acceptedTermsVersion defaults to 0
        Assert.False(without.Ok);
        Assert.Contains("rules", without.Error, StringComparison.OrdinalIgnoreCase);

        Assert.False(registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: 99).Ok); // wrong version
        Assert.True(registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: 1).Ok);
    }

    [Fact]
    public async Task RulesChange_BlocksJoins_UntilReacceptedAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        // The operator bumps the rules version: existing accounts must re-accept before playing.
        config.TermsVersion = 2;
        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "P1");
        Assert.Null(grant);
        Assert.Contains("rules", error, StringComparison.OrdinalIgnoreCase);

        registry.AcceptTerms(accountId, 2);
        var refreshed = account with { AcceptedTermsVersion = 2 }; // what a fresh ResolveSession would carry
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, refreshed, "P1")).Grant);
    }

    [Fact]
    public async Task BannedAccount_CannotJoin_UntilUnbannedAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        registry.SetBanned(accountId, true, "Hate speech in chat");
        var banned = registry.ResolveSession(registry.Login("Owner", "super-secret-1")!.Value.SessionToken)!;
        Assert.True(banned.IsBanned);

        var (grant, error) = await orchestrator.JoinAsync(world.Id, banned, "P1");
        Assert.Null(grant);
        Assert.Contains("banned", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hate speech", error, StringComparison.Ordinal); // the reason is shown to the player

        registry.SetBanned(accountId, false, string.Empty);
        var unbanned = registry.ResolveSession(registry.Login("Owner", "super-secret-1")!.Value.SessionToken)!;
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, unbanned, "P1")).Grant);
    }

    [Fact]
    public void Reports_FileListAndClose()
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);

        Assert.False(registry.CreateReport(accountId, "w1", "Meanie", "nonsense-category", "msg").Ok);
        Assert.False(registry.CreateReport(accountId, "w1", "", "chat", "msg").Ok);

        Assert.True(registry.CreateReport(accountId, "w1", "Meanie", "chat", new string('x', 600)).Ok);
        var open = registry.ListOpenReports();
        var report = Assert.Single(open);
        Assert.Equal("Meanie", report.ReportedName);
        Assert.Equal(500, report.Message.Length); // free text is length-capped server-side
        Assert.Equal(string.Empty, report.WorldId); // "w1" is not a well-formed world id → stored empty, not rejected

        registry.CloseReport(report.Id, "reviewed");
        Assert.Empty(registry.ListOpenReports());
    }

    [Fact]
    public void Reports_KeepAWellFormedWorldId()
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);

        // A proper 12-hex world id (sent by the in-game /report command and the portal form) survives.
        Assert.True(registry.CreateReport(accountId, "aabbccddee11", "Meanie", "chat", "insults in chat").Ok);
        var report = Assert.Single(registry.ListOpenReports());
        Assert.Equal("aabbccddee11", report.WorldId);
    }

    [Fact]
    public void Reports_FeedbackCategoryNeedsNoNameButAMessage()
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);

        // Game feedback ("Feedback & Ideen") is the only category without a reported player — but an
        // empty idea is worthless, so the message becomes mandatory instead.
        Assert.False(registry.CreateReport(accountId, "", "", "feedback", "   ").Ok);
        Assert.True(registry.CreateReport(accountId, "", "", "feedback", "please add space whales!").Ok);

        var open = Assert.Single(registry.ListOpenReports());
        Assert.Equal("feedback", open.Category);
        Assert.Equal(string.Empty, open.ReportedName);
        Assert.Equal("please add space whales!", open.Message);

        // All other categories still require the reported player's name.
        Assert.False(registry.CreateReport(accountId, "", "", "chat", "some message").Ok);

        registry.CloseReport(open.Id, "reviewed");
        Assert.Empty(registry.ListOpenReports());
    }

    // ---------------- Save upload validation ----------------

    [Fact]
    public void UploadValidation_AcceptsOnlyRealWorldSaves()
    {
        string garbage = System.IO.Path.Combine(_root, "garbage.db");
        System.IO.File.WriteAllText(garbage, "definitely not sqlite");
        Assert.False(SavePaths.ValidateUploadedSave(garbage).Ok);

        // A real SQLite file, but not one of our saves (no world_meta table).
        string foreign = System.IO.Path.Combine(_root, "foreign.db");
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={foreign}"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE something(id INTEGER)";
            cmd.ExecuteNonQuery();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.False(SavePaths.ValidateUploadedSave(foreign).Ok);

        // The shape a genuine world.db has (world_meta anchor table) passes.
        string save = System.IO.Path.Combine(_root, "save.db");
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={save}"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE world_meta(key TEXT PRIMARY KEY, value TEXT)";
            cmd.ExecuteNonQuery();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var result = SavePaths.ValidateUploadedSave(save);
        Assert.True(result.Ok, result.Error);
    }

    // ---------------- Instance resource fences + AI passthrough (docker run argv) ----------------

    [Fact]
    public void BuildRunArgs_AppliesResourceFences_AndAiPassthrough()
    {
        var config = new WorldHostConfig
        {
            AiBackendUrl = "http://ai:8077",
            AiLevel = "TextOnly",
            InstanceMemory = "768m",
            InstanceCpus = "2",
        };
        var world = new WorldRecord("abc123abc123", "acct", "My World", "secret", 32001, WorldStatus.Stopped, "", 0, 0);

        var args = DockerCliLauncher.BuildRunArgs(config, world, "/opt/bbs/worldhost/worlds/abc123abc123/saves");

        string joined = string.Join(" ", args);
        Assert.Contains("--memory 768m", joined);
        Assert.Contains("--memory-swap 768m", joined); // same value: a capped world must not swap-thrash the host
        Assert.Contains("--cpus 2", joined);
        Assert.Contains("--pids-limit 256", joined);
        Assert.Contains("BBS_AI_BACKEND_URL=http://ai:8077", joined);
        Assert.Contains("BBS_AI_LEVEL=TextOnly", joined);
        Assert.Equal(config.ServerImage, args[^1]); // image stays the last argv entry
    }

    [Fact]
    public void BuildRunArgs_OmitsFencesAndAi_WhenUnconfigured()
    {
        var config = new WorldHostConfig { AiBackendUrl = "", InstanceMemory = "", InstanceCpus = "" };
        var world = new WorldRecord("abc123abc123", "acct", "My World", "secret", 32001, WorldStatus.Stopped, "", 0, 0);

        var args = DockerCliLauncher.BuildRunArgs(config, world, "/tmp/saves");

        string joined = string.Join(" ", args);
        Assert.DoesNotContain("--memory", joined);
        Assert.DoesNotContain("--cpus", joined);
        Assert.DoesNotContain("BBS_AI_BACKEND_URL", joined);
        Assert.DoesNotContain("BBS_CRASH_REPORT", joined); // no key configured = no crash-report env at all
        Assert.Contains("--pids-limit 256", joined); // the pids fence is unconditional
    }

    [Fact]
    public void BuildRunArgs_ForwardsCrashReportKey_AndOptionalEndpointOverride()
    {
        var world = new WorldRecord("abc123abc123", "acct", "My World", "secret", 32001, WorldStatus.Stopped, "", 0, 0);

        // Key only (the fleet case): the endpoint stays the server's built-in default.
        var keyOnly = new WorldHostConfig { CrashReportKey = "write-key" };
        string joined = string.Join(" ", DockerCliLauncher.BuildRunArgs(keyOnly, world, "/tmp/saves"));
        Assert.Contains("BBS_CRASH_REPORT_KEY=write-key", joined);
        Assert.DoesNotContain("BBS_CRASH_REPORT_ENDPOINT", joined);

        // Key + endpoint override (self-hosted fleet with its own ReportHost).
        var selfHosted = new WorldHostConfig
        {
            CrashReportKey = "write-key",
            CrashReportEndpoint = "https://reports.example.com/api/bugreport",
        };
        joined = string.Join(" ", DockerCliLauncher.BuildRunArgs(selfHosted, world, "/tmp/saves"));
        Assert.Contains("BBS_CRASH_REPORT_KEY=write-key", joined);
        Assert.Contains("BBS_CRASH_REPORT_ENDPOINT=https://reports.example.com/api/bugreport", joined);

        // An endpoint WITHOUT a key must not leak through — upload is off without the key anyway.
        var endpointOnly = new WorldHostConfig { CrashReportEndpoint = "https://reports.example.com/api/bugreport" };
        Assert.DoesNotContain("BBS_CRASH_REPORT", string.Join(" ", DockerCliLauncher.BuildRunArgs(endpointOnly, world, "/tmp/saves")));
    }

    [Fact]
    public void BuildRunArgs_ForwardsChunkStreamBudget_AndOmitsWhenOff()
    {
        var world = new WorldRecord("abc123abc123", "acct", "My World", "secret", 32001, WorldStatus.Stopped, "", 0, 0);

        // Default (25 ms): bounds cold-worldgen bursts so one player's fresh join can't stall the world's tick.
        var on = new WorldHostConfig();
        Assert.Contains("BBS_CHUNK_STREAM_BUDGET_MS=25", string.Join(" ", DockerCliLauncher.BuildRunArgs(on, world, "/tmp/saves")));

        // Explicitly off (0): no env forwarded — the instance keeps the unbounded historical behaviour.
        var off = new WorldHostConfig { ChunkStreamBudgetMs = 0 };
        Assert.DoesNotContain("BBS_CHUNK_STREAM_BUDGET_MS", string.Join(" ", DockerCliLauncher.BuildRunArgs(off, world, "/tmp/saves")));
    }

    // ---------------- Fleet capacity gate ----------------

    [Fact]
    public async Task Wake_RefusesBeyondMaxActiveInstances_AndRunningWorldsAreUnaffectedAsync()
    {
        var config = new WorldHostConfig { MaxActiveInstances = 1 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);

        var (_, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        var account = registry.ResolveSession(session)!;
        var w1 = registry.CreateWorld(accountId, "First").World!;
        var w2 = registry.CreateWorld(accountId, "Second").World!;

        Assert.NotNull((await orchestrator.JoinAsync(w1.Id, account, "Pilot")).Grant);

        // Fleet is full: waking the second world is refused with the localizable no-capacity error…
        var (grant, error) = await orchestrator.JoinAsync(w2.Id, account, "Pilot");
        Assert.Null(grant);
        Assert.StartsWith("No capacity", error);

        // …but joining the ALREADY-RUNNING world still works (routing, not waking).
        Assert.NotNull((await orchestrator.JoinAsync(w1.Id, account, "Pilot")).Grant);

        // Once the first world stops, the second may wake.
        orchestrator.StopWorld(registry.GetWorld(w1.Id)!);
        Assert.NotNull((await orchestrator.JoinAsync(w2.Id, account, "Pilot")).Grant);
    }

    // ---------------- Admin lookups + Basic Auth ----------------

    [Fact]
    public void AdminLookups_FindByName_BannedList_AndFleetOverview()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Pilot", "super-secret-1", acceptedTermsVersion: Terms);
        registry.CreateWorld(accountId, "My World");

        Assert.Equal(accountId, registry.FindAccountByName("pilot")!.Id); // case-insensitive
        Assert.Null(registry.FindAccountByName("nobody"));
        Assert.Null(registry.FindAccountByName("  "));

        Assert.Empty(registry.ListBannedAccounts());
        registry.SetBanned(accountId, banned: true, reason: "be kind");
        var banned = Assert.Single(registry.ListBannedAccounts());
        Assert.Equal("Pilot", banned.Name);
        Assert.Equal("be kind", banned.BanReason);

        var rows = registry.ListAllWorldsAdmin();
        var row = Assert.Single(rows);
        Assert.Equal("My World", row.World.DisplayName);
        Assert.Equal("Pilot", row.OwnerName);
    }

    [Fact]
    public void AdminUiBasicAuth_FailsClosed_AndAcceptsOnlyExactCredentials()
    {
        static string Header(string user, string pass)
            => "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user + ":" + pass));

        // Unconfigured credentials = admin UI off, no header ever matches.
        Assert.False(BasicAuth.IsAuthorized(Header("admin", "pw"), "", ""));
        Assert.False(BasicAuth.IsAuthorized(Header("admin", "pw"), "admin", ""));

        Assert.True(BasicAuth.IsAuthorized(Header("admin", "pw"), "admin", "pw"));
        Assert.False(BasicAuth.IsAuthorized(Header("admin", "wrong"), "admin", "pw"));
        Assert.False(BasicAuth.IsAuthorized(Header("other", "pw"), "admin", "pw"));
        Assert.False(BasicAuth.IsAuthorized(null, "admin", "pw"));
        Assert.False(BasicAuth.IsAuthorized("Bearer xyz", "admin", "pw"));
        Assert.False(BasicAuth.IsAuthorized("Basic not-base64!", "admin", "pw"));
    }

    public void Dispose()
    {
        try
        {
            foreach (var r in _registries)
            {
                r.Dispose();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

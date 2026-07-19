// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hosted-worlds Phase 3 hardening: rate limiting, blocked-name hygiene, archive-after-inactivity with
/// transparent restore-on-join, and the Prometheus metrics rendering.
/// </summary>
public sealed class WorldHostPhase3Tests : IDisposable
{
    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldHostPhase3Tests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_wh3_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private HostRegistry NewRegistry(WorldHostConfig config)
    {
        var registry = new HostRegistry(config, System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
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

    private static WorldOrchestrator NewOrchestrator(HostRegistry registry, FakeLauncher launcher, WorldHostConfig config, WorldHostMetrics? metrics = null)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)), metrics);

    private static (string AccountId, AccountRecord Account) NewAccount(HostRegistry registry, string name = "Owner")
    {
        var (ok, error, accountId, session) = registry.CreateAccount(name, "super-secret-1", acceptedTermsVersion: 1);
        Assert.True(ok, error);
        return (accountId, registry.ResolveSession(session)!);
    }

    // ---------------- Rate limiter ----------------

    [Fact]
    public void RateLimiter_EnforcesTheWindow_AndResets()
    {
        long now = 1000;
        var limiter = new RateLimiter(2, TimeSpan.FromSeconds(60), () => now);

        Assert.True(limiter.TryPass("ip-1"));
        Assert.True(limiter.TryPass("ip-1"));
        Assert.False(limiter.TryPass("ip-1"));   // budget spent
        Assert.True(limiter.TryPass("ip-2"));    // other keys are independent

        now += 61;                                // window rolls over
        Assert.True(limiter.TryPass("ip-1"));
    }

    [Fact]
    public void RateLimiter_NonPositiveLimit_DisablesIt()
    {
        var limiter = new RateLimiter(0, TimeSpan.FromSeconds(1), () => 0);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(limiter.TryPass("ip"));
        }
    }

    [Fact]
    public void RateLimiter_IsExhausted_ChecksWithoutConsuming()
    {
        long now = 1000;
        var limiter = new RateLimiter(2, TimeSpan.FromSeconds(60), () => now);

        // Repeated checks never spend budget — the login backoff relies on metering only FAILURES.
        for (int i = 0; i < 10; i++)
        {
            Assert.False(limiter.IsExhausted("acct"));
        }

        Assert.True(limiter.TryPass("acct"));
        Assert.True(limiter.TryPass("acct"));
        Assert.True(limiter.IsExhausted("acct"));
        Assert.False(limiter.IsExhausted("other"));

        now += 61;                                // window rolls over → cooldown ends
        Assert.False(limiter.IsExhausted("acct"));
    }

    // ---------------- Trusted-proxy parsing (#418) ----------------

    [Fact]
    public void ParseTrustedProxies_SplitsCidrsFromBareAddresses()
    {
        var (networks, proxies) = WorldHostConfig.ParseTrustedProxies(
            new[] { "172.16.0.0/12", "::1", "127.0.0.0/8", "203.0.113.7" });

        Assert.Equal(2, networks.Count);
        Assert.Equal(2, proxies.Count);
        Assert.Contains(proxies, p => p.ToString() == "203.0.113.7");
    }

    [Fact]
    public void ParseTrustedProxies_DefaultListParses()
    {
        var (networks, proxies) = WorldHostConfig.ParseTrustedProxies(new WorldHostConfig().TrustedProxies);
        Assert.True(networks.Count + proxies.Count > 0);
    }

    [Fact]
    public void ParseTrustedProxies_InvalidEntry_FailsLoudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => WorldHostConfig.ParseTrustedProxies(new[] { "not-an-ip" }));
        Assert.Contains("not-an-ip", ex.Message);
    }

    // ---------------- Blocked names (kid-facing hygiene) ----------------

    [Fact]
    public async Task BlockedWords_AreRejected_ForAccounts_Worlds_AndPlayerNamesAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);

        // Account names: exact, cased and separator-tricked variants are all caught.
        Assert.False(registry.CreateAccount("hitler88", "super-secret-1", acceptedTermsVersion: 1).Ok);
        Assert.False(registry.CreateAccount("H-i-t-l-e-r", "super-secret-1", acceptedTermsVersion: 1).Ok);
        Assert.True(registry.CreateAccount("Hilda", "super-secret-1", acceptedTermsVersion: 1).Ok); // no false positive

        var (accountId, account) = NewAccount(registry);

        // World display names.
        Assert.False(registry.CreateWorld(accountId, "xX fuck Xx").Ok);
        var world = registry.CreateWorld(accountId, "Friendly World").World!;

        // In-game player names at the join grant.
        Assert.Null((await orchestrator.JoinAsync(world.Id, account, "N_a_z_i")).Grant);
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "NicePilot")).Grant);
    }

    [Fact]
    public void BlockedWords_OperatorExtension_IsHonored()
    {
        var config = new WorldHostConfig();
        config.BlockedNameWords.Add("kackwurst");
        var registry = NewRegistry(config);
        Assert.False(registry.CreateAccount("Kack-Wurst", "super-secret-1", acceptedTermsVersion: 1).Ok);
    }

    // ---------------- Archive after inactivity ----------------

    [Fact]
    public async Task ArchiveSweep_ArchivesInactiveWorlds_AndJoinRestoresThemTransparentlyAsync()
    {
        var config = new WorldHostConfig
        {
            WakeTimeoutSeconds = 5,
            ArchiveAfterMonths = 6,
            WorldsDir = System.IO.Path.Combine(_root, "worlds"),
        };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Sleepy World").World!;

        // The world has been played once: saves exist on disk.
        string dbPath = SavePaths.WorldDbPath(config, world.Id);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        System.IO.File.WriteAllText(dbPath, "save-bytes");

        // Fresh worlds are safe: a sweep "today" archives nothing.
        Assert.Equal(0, orchestrator.ArchiveSweep(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        // Seven months later the world is a candidate and gets archived: saves moved, status flipped.
        long sevenMonthsLater = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7L * 30 * 86400;
        Assert.Equal(1, orchestrator.ArchiveSweep(sevenMonthsLater));
        Assert.Equal(WorldStatus.Archived, registry.GetWorld(world.Id)!.Status);
        Assert.False(System.IO.File.Exists(dbPath));
        Assert.True(System.IO.File.Exists(
            System.IO.Path.Combine(SavePaths.ArchivedSavesDir(config, world.Id), world.Id, "world.db")));

        // Joining an archived world restores the saves and wakes it — transparent to the player.
        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.NotNull(grant);
        Assert.Equal(string.Empty, error);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
        Assert.True(System.IO.File.Exists(dbPath)); // saves are back in the live location
        Assert.False(System.IO.Directory.Exists(SavePaths.ArchivedSavesDir(config, world.Id)));
    }

    [Fact]
    public async Task ArchiveSweep_SparesRecentlyActiveWorldsAsync()
    {
        var config = new WorldHostConfig
        {
            WakeTimeoutSeconds = 5,
            ArchiveAfterMonths = 6,
            WorldsDir = System.IO.Path.Combine(_root, "worlds-active"),
        };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Busy World").World!;

        // A join stamps activity; the instance then idle-exits and the reaper re-stamps it.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "Pilot")).Grant);
        launcher.Stop(registry.GetWorld(world.Id)!.ContainerId);
        Assert.Equal(1, orchestrator.Reap());

        // Five months of silence: still spared (threshold is six).
        long fiveMonthsLater = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5L * 30 * 86400;
        Assert.Equal(0, orchestrator.ArchiveSweep(fiveMonthsLater));
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);
    }

    // ---------------- Reaper/sweep vs. wake races (#415/#416) ----------------

    /// <summary>Builds an orchestrator whose health probe blocks: it signals <paramref name="probeEntered"/>
    /// on entry (the wake now holds the per-world lock) and waits for <paramref name="probeGate"/> before
    /// answering — the deterministic stand-in for "a wake is in flight while a background pass runs".</summary>
    private static WorldOrchestrator NewBlockingProbeOrchestrator(
        HostRegistry registry, FakeLauncher launcher, WorldHostConfig config,
        SemaphoreSlim probeEntered, SemaphoreSlim probeGate)
        => new(config, registry, launcher, async w =>
        {
            probeEntered.Release();
            await probeGate.WaitAsync();
            return launcher.IsRunning(w.ContainerId);
        });

    [Fact]
    public async Task Reap_SkipsAWorld_WhoseWakeIsInFlightAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        using var probeEntered = new SemaphoreSlim(0);
        using var probeGate = new SemaphoreSlim(0);
        var orchestrator = NewBlockingProbeOrchestrator(registry, launcher, config, probeEntered, probeGate);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Waking World").World!;

        var joinTask = orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.True(await probeEntered.WaitAsync(TimeSpan.FromSeconds(10)));

        // The reaper's container probe races the wake: simulate it observing "not running" for the
        // container the wake just started. It must NOT write a stale Stopped/"" over the in-flight wake —
        // that row would make the next join `docker rm -f` the live container (#415).
        string containerId = registry.GetWorld(world.Id)!.ContainerId;
        launcher.Running.Clear();
        Assert.Equal(0, orchestrator.Reap());
        Assert.Equal(WorldStatus.Starting, registry.GetWorld(world.Id)!.Status);

        launcher.Running.Add(containerId);
        probeGate.Release();
        Assert.NotNull((await joinTask).Grant);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
    }

    [Fact]
    public async Task ArchiveSweep_SparesAWorld_WhoseWakeIsInFlightAsync()
    {
        var config = new WorldHostConfig
        {
            WakeTimeoutSeconds = 5,
            ArchiveAfterMonths = 6,
            WorldsDir = System.IO.Path.Combine(_root, "worlds-waking"),
        };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        using var probeEntered = new SemaphoreSlim(0);
        using var probeGate = new SemaphoreSlim(0);
        var orchestrator = NewBlockingProbeOrchestrator(registry, launcher, config, probeEntered, probeGate);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Night Owl World").World!;

        string dbPath = SavePaths.WorldDbPath(config, world.Id);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        System.IO.File.WriteAllText(dbPath, "save-bytes");

        // The hourly sweep fires while the world is mid-wake (wake lock held, probe pending). It must
        // not move the saves out from under the starting container (#416) — even with a cutoff that
        // would have archived the world a moment earlier.
        var joinTask = orchestrator.JoinAsync(world.Id, account, "Pilot");
        Assert.True(await probeEntered.WaitAsync(TimeSpan.FromSeconds(10)));
        long sevenMonthsLater = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7L * 30 * 86400;
        Assert.Equal(0, orchestrator.ArchiveSweep(sevenMonthsLater));
        Assert.True(System.IO.File.Exists(dbPath));

        probeGate.Release();
        Assert.NotNull((await joinTask).Grant);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
        Assert.True(System.IO.File.Exists(dbPath));
    }

    [Fact]
    public void ArchiveSweep_Disabled_WhenMonthsIsZero()
    {
        var config = new WorldHostConfig { ArchiveAfterMonths = 0, WorldsDir = System.IO.Path.Combine(_root, "worlds-off") };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (accountId, _) = NewAccount(registry);
        registry.CreateWorld(accountId, "Kept World");

        Assert.Equal(0, orchestrator.ArchiveSweep(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 100L * 30 * 86400));
    }

    // ---------------- Metrics ----------------

    [Fact]
    public void Metrics_RenderCarriesGaugesAndCounters()
    {
        var config = new WorldHostConfig();
        var registry = NewRegistry(config);
        var (accountId, _) = NewAccount(registry);
        registry.CreateWorld(accountId, "My World");

        var metrics = new WorldHostMetrics();
        metrics.JoinGranted();
        metrics.Archived(2);

        string text = metrics.Render(registry);
        Assert.Contains("bbs_accounts_total 1", text, StringComparison.Ordinal);
        Assert.Contains("bbs_worlds{status=\"stopped\"} 1", text, StringComparison.Ordinal);
        Assert.Contains("bbs_joins_granted_total 1", text, StringComparison.Ordinal);
        Assert.Contains("bbs_worlds_archived_total 2", text, StringComparison.Ordinal);
        Assert.Contains("bbs_reports_open 0", text, StringComparison.Ordinal);
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

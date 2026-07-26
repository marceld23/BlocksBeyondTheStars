// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Fleet-operator access to any world (issue #495): the operator must be able to enter private and
/// password-protected worlds — that is where kids actually play, and oversight is the point of observer
/// mode. What these tests pin down is the SECURITY BOUNDARY around that power: it must require the
/// developer account (claimable only with the secret code) AND a configured fleet-admin name, and the
/// name itself must be unclaimable by anyone else.
/// </summary>
public sealed class FleetOperatorAccessTests : IDisposable
{
    private const int Terms = 1;
    private const string ClaimCode = "family-secret";

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public FleetOperatorAccessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fleetop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>A config with "Operator" as the fleet admin, the claim code set, and the fleet-admin
    /// names reserved — exactly what <see cref="WorldHostConfig.FromEnvironment"/> produces in production.</summary>
    private static WorldHostConfig NewConfig()
    {
        var config = new WorldHostConfig
        {
            FleetAdmins = "Operator",
            ReservedClaimCode = ClaimCode,
            TermsVersion = Terms,
        };
        config.ReserveFleetAdminNames();
        return config;
    }

    private HostRegistry NewRegistry(WorldHostConfig config)
    {
        var registry = new HostRegistry(config, Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    private sealed class FakeLauncher : IInstanceLauncher
    {
        private int _startCount;
        private readonly HashSet<string> _running = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            string id = "container-" + ++_startCount;
            _running.Add(id);
            return id;
        }

        public void Stop(string containerId) => _running.Remove(containerId);
        public void Remove(string worldId) { }
        public bool IsRunning(string containerId) => containerId != null && _running.Contains(containerId);
        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    private static WorldOrchestrator NewOrchestrator(HostRegistry registry, WorldHostConfig config)
    {
        var launcher = new FakeLauncher();
        return new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)));
    }

    private static AccountRecord NewAccount(HostRegistry registry, string name, string? claimCode = null)
    {
        var (ok, error, _, session) = registry.CreateAccount(name, "super-secret-1", claimCode, Terms);
        Assert.True(ok, error);
        return registry.ResolveSession(session)!;
    }

    // ---------------- The password bypass ----------------

    [Fact]
    public async Task Operator_EntersPasswordProtectedWorld_WithoutThePasswordAsync()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, config);

        var owner = NewAccount(registry, "KidOwner");
        var world = registry.CreateWorld(owner.Id, "Kids World", "geheim").World!;

        // The operator: a developer account (claimed with the secret code) joining under the fleet-admin name.
        var op = NewAccount(registry, "Operator", ClaimCode);
        Assert.True(op.IsDeveloper);

        var (grant, error) = await orchestrator.JoinAsync(world.Id, op, "Operator");
        Assert.Null(error.Length == 0 ? null : error); // surface the reason on failure
        Assert.NotNull(grant);
    }

    [Fact]
    public async Task DeveloperAccount_UnderANormalName_StillNeedsThePasswordAsync()
    {
        // Both halves of the gate are required: a developer account joining under a non-fleet-admin name
        // is a family member playing normally, not the operator observing — no bypass.
        var config = NewConfig();
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, config);

        var owner = NewAccount(registry, "KidOwner");
        var world = registry.CreateWorld(owner.Id, "Kids World", "geheim").World!;
        var dev = NewAccount(registry, "Operator", ClaimCode);

        var (grant, error) = await orchestrator.JoinAsync(world.Id, dev, "JustPlaying");
        Assert.Null(grant);
        Assert.Contains("password", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalAccount_CannotUseTheFleetAdminName_AtAllAsync()
    {
        // The other half: without the developer flag the fleet-admin name is a RESERVED name (auto-added
        // by config load), so a regular account cannot even join under it — let alone bypass a password.
        var config = NewConfig();
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, config);

        var owner = NewAccount(registry, "KidOwner");
        var world = registry.CreateWorld(owner.Id, "Kids World", "geheim").World!;
        var kid = NewAccount(registry, "SomeKid");

        var (grant, error) = await orchestrator.JoinAsync(world.Id, kid, "Operator");
        Assert.Null(grant);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operator_EntersPrivateWorlds_TheSameWayAsync()
    {
        // Private = never made public, no password. The join API itself has no visibility gate (players
        // just cannot SEE unlisted worlds); the operator finds them via /api/worlds/all and joins by id.
        var config = NewConfig();
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, config);

        var owner = NewAccount(registry, "KidOwner");
        var world = registry.CreateWorld(owner.Id, "Private World").World!;
        Assert.False(world.IsPublic);

        var op = NewAccount(registry, "Operator", ClaimCode);
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, op, "Operator")).Grant);
    }

    // ---------------- The name lock (child-safety hardening) ----------------

    [Fact]
    public void ConfigLoad_AutoReservesFleetAdminNames()
    {
        var config = new WorldHostConfig { FleetAdmins = "Operator, Aufsicht" };
        Assert.DoesNotContain(config.ReservedNames, r => r == "Aufsicht");

        config.ReserveFleetAdminNames();

        Assert.Contains(config.ReservedNames, r => r == "Operator");
        Assert.Contains(config.ReservedNames, r => r == "Aufsicht");

        // Idempotent — a second load must not stack duplicates.
        int count = config.ReservedNames.Count;
        config.ReserveFleetAdminNames();
        Assert.Equal(count, config.ReservedNames.Count);
    }

    [Fact]
    public void AutoReservedName_CannotBeRegistered_WithoutTheClaimCode()
    {
        var config = NewConfig();
        var registry = NewRegistry(config);

        var (ok, error, _, _) = registry.CreateAccount("Operator", "super-secret-1", null, Terms);

        Assert.False(ok);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FleetAdminNameMatch_IsCaseInsensitive()
    {
        var config = new WorldHostConfig { FleetAdmins = "Marcel" };

        Assert.True(config.IsFleetAdminName("Marcel"));
        Assert.True(config.IsFleetAdminName("marcel"));
        Assert.True(config.IsFleetAdminName(" MARCEL "));
        Assert.False(config.IsFleetAdminName("Marcel2"));
        Assert.False(config.IsFleetAdminName(""));
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

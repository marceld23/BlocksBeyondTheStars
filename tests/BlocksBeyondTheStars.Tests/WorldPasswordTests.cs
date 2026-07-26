// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.WorldHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Creator-set world join passwords (#250, closes the open-join gap #251): stored PBKDF2-hashed on the
/// world row, authored at creation or later by the owner, and enforced at the join-grant choke point in
/// the orchestrator — before the world would even wake. Wrong guesses burn a rate-limit budget that ends
/// in a cooldown answer; the owner always bypasses.
/// </summary>
public sealed class WorldPasswordTests : IDisposable
{
    private const int Terms = 1;

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldPasswordTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wpw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string NewDbPath() => Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");

    private HostRegistry NewRegistry(WorldHostConfig? config = null, string? dbPath = null)
    {
        var registry = new HostRegistry(config ?? new WorldHostConfig(), dbPath ?? NewDbPath());
        _registries.Add(registry);
        return registry;
    }

    private static (string AccountId, AccountRecord Account) NewAccount(HostRegistry registry, string name = "Owner")
    {
        var (ok, error, accountId, session) = registry.CreateAccount(name, "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(ok, error);
        return (accountId, registry.ResolveSession(session)!);
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

        public void Remove(string worldId) => Removed.Add(worldId);

        public readonly List<string> Removed = new();

        public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);

        public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
    }

    private static WorldOrchestrator NewOrchestrator(
        HostRegistry registry, FakeLauncher launcher, WorldHostConfig config, RateLimiter? passwordAttempts = null)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)),
            passwordAttempts: passwordAttempts);

    // ---------------- Registry: create / set / remove ----------------

    [Fact]
    public void CreateWorld_WithPassword_StoresAHash_NeverThePlaintext()
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);

        var open = registry.CreateWorld(accountId, "Open World").World!;
        Assert.False(open.HasPassword);

        var locked = registry.CreateWorld(accountId, "Locked World", "geheim").World!;
        Assert.True(locked.HasPassword);
        Assert.DoesNotContain("geheim", locked.PasswordHash, StringComparison.OrdinalIgnoreCase);
        Assert.True(PasswordHasher.Verify("geheim", registry.GetWorld(locked.Id)!.PasswordHash));
        Assert.False(PasswordHasher.Verify("falsch", registry.GetWorld(locked.Id)!.PasswordHash));
    }

    [Theory]
    [InlineData("abc")]                        // too short
    [InlineData("abcdefghijklmnopqrstuvwxy")]  // 25 chars — too long
    [InlineData("ab\ncd")]                     // control chars
    public void CreateWorld_RejectsInvalidPasswords(string password)
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);

        var (ok, error, _) = registry.CreateWorld(accountId, "My World", password);

        Assert.False(ok);
        Assert.Equal("World password must be 4-24 printable characters.", error);
    }

    [Fact]
    public void SetWorldPassword_SetsChangesAndRemoves()
    {
        var registry = NewRegistry();
        var (accountId, _) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.True(registry.SetWorldPassword(world.Id, "erstes").Ok);
        Assert.True(PasswordHasher.Verify("erstes", registry.GetWorld(world.Id)!.PasswordHash));

        Assert.True(registry.SetWorldPassword(world.Id, "zweites").Ok);
        Assert.True(PasswordHasher.Verify("zweites", registry.GetWorld(world.Id)!.PasswordHash));
        Assert.False(PasswordHasher.Verify("erstes", registry.GetWorld(world.Id)!.PasswordHash));

        Assert.True(registry.SetWorldPassword(world.Id, string.Empty).Ok); // empty = open again
        Assert.False(registry.GetWorld(world.Id)!.HasPassword);

        Assert.False(registry.SetWorldPassword(world.Id, "abc").Ok);          // validation shared with create
        Assert.False(registry.SetWorldPassword("000000000000", "valid-pw").Ok); // unknown world
    }

    // ---------------- Orchestrator: the join-grant gate ----------------

    [Fact]
    public async Task Join_ProtectedWorld_RequiresThePassword_AndOwnerBypassesAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (ownerId, owner) = NewAccount(registry);
        var (_, guest) = NewAccount(registry, "Guest");
        var world = registry.CreateWorld(ownerId, "Family World", "geheim").World!;

        // No password → the structured "needs a password" answer, and the world was NOT woken.
        var (grant, error) = await orchestrator.JoinAsync(world.Id, guest, "G1");
        Assert.Null(grant);
        Assert.Equal("This world needs a password.", error);
        Assert.Equal(0, launcher.StartCount);

        // Wrong password → refused, still not woken.
        (grant, error) = await orchestrator.JoinAsync(world.Id, guest, "G1", "falsch");
        Assert.Null(grant);
        Assert.Equal("Wrong world password.", error);
        Assert.Equal(0, launcher.StartCount);

        // Correct password → grant.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, guest, "G1", "geheim")).Grant);

        // The owner never needs the password (any device, any session).
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, owner, "Owner")).Grant);
    }

    [Fact]
    public async Task Join_OpenWorld_IgnoresAnySuppliedPasswordAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (ownerId, _) = NewAccount(registry);
        var (_, guest) = NewAccount(registry, "Guest");
        var world = registry.CreateWorld(ownerId, "Open World").World!;

        Assert.NotNull((await orchestrator.JoinAsync(world.Id, guest, "G1")).Grant);
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, guest, "G1", "whatever")).Grant);
    }

    [Fact]
    public async Task Join_TooManyWrongGuesses_HitsTheCooldown_EvenWithTheRightPasswordAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config,
            passwordAttempts: new RateLimiter(2, TimeSpan.FromMinutes(15)));
        var (ownerId, owner) = NewAccount(registry);
        var (_, guest) = NewAccount(registry, "Guest");
        var world = registry.CreateWorld(ownerId, "Family World", "geheim").World!;

        Assert.Equal("Wrong world password.", (await orchestrator.JoinAsync(world.Id, guest, "G1", "falsch1")).Error);
        Assert.Equal("Wrong world password.", (await orchestrator.JoinAsync(world.Id, guest, "G1", "falsch2")).Error);

        // Budget exhausted: even the CORRECT password now gets the cooldown answer.
        var (grant, error) = await orchestrator.JoinAsync(world.Id, guest, "G1", "geheim");
        Assert.Null(grant);
        Assert.Equal("Too many password attempts — please wait a few minutes.", error);

        // The cooldown is per account+world: the owner (and other accounts) are unaffected.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, owner, "Owner")).Grant);
    }

    [Fact]
    public async Task Join_EmptyProbes_DoNotBurnTheAttemptBudgetAsync()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config,
            passwordAttempts: new RateLimiter(1, TimeSpan.FromMinutes(15)));
        var (ownerId, _) = NewAccount(registry);
        var (_, guest) = NewAccount(registry, "Guest");
        var world = registry.CreateWorld(ownerId, "Family World", "geheim").World!;

        // The passwordless first contact is the NORMAL prompt trigger — repeat it freely.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal("This world needs a password.", (await orchestrator.JoinAsync(world.Id, guest, "G1")).Error);
        }

        // Budget still intact: the correct password joins despite the limit of 1.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, guest, "G1", "geheim")).Grant);
    }

    // ---------------- Migration ----------------

    [Fact]
    public void OldRegistry_WithoutThePasswordColumn_IsUpgradedInPlace()
    {
        // A pre-#250 registry: the world table has no password_hash column.
        string dbPath = NewDbPath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE world(
                    id TEXT PRIMARY KEY,
                    owner_account_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    join_secret TEXT NOT NULL,
                    host_port INTEGER NOT NULL UNIQUE,
                    status TEXT NOT NULL,
                    container_id TEXT NOT NULL DEFAULT '',
                    created_unix INTEGER NOT NULL,
                    last_started_unix INTEGER NOT NULL DEFAULT 0,
                    last_active_unix INTEGER NOT NULL DEFAULT 0);
                INSERT INTO world VALUES('abcdefabcdef', 'acc-1', 'Old World', 'secret', 32000, 'stopped', '', 1, 0, 0);
                """;
            cmd.ExecuteNonQuery();
        }

        var registry = NewRegistry(dbPath: dbPath);

        // The pre-existing world reads back open (NULL→'' hash) and can be protected from now on.
        var world = registry.GetWorld("abcdefabcdef");
        Assert.NotNull(world);
        Assert.False(world!.HasPassword);
        Assert.True(registry.SetWorldPassword(world.Id, "geheim").Ok);
        Assert.True(registry.GetWorld(world.Id)!.HasPassword);
    }

    public void Dispose()
    {
        foreach (var registry in _registries)
        {
            registry.Dispose();
        }

        SqliteConnection.ClearAllPools(); // release the migration test's raw connection file handle
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

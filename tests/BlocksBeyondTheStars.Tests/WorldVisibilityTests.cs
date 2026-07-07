// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The public world browser (opt-in discovery): a world can be listed publicly ONLY when it has a join
/// password, so every listed world stays password-gated — strangers who find it still need the
/// owner-shared password to join. Removing the password un-lists the world automatically.
/// </summary>
public sealed class WorldVisibilityTests : IDisposable
{
    private const int Terms = 1;

    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldVisibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string NewDbPath() => Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");

    private HostRegistry NewRegistry(string? dbPath = null)
    {
        var registry = new HostRegistry(new WorldHostConfig(), dbPath ?? NewDbPath());
        _registries.Add(registry);
        return registry;
    }

    private static string NewAccount(HostRegistry registry, string name = "Owner")
    {
        var (ok, error, accountId, _) = registry.CreateAccount(name, "super-secret-1", acceptedTermsVersion: Terms);
        Assert.True(ok, error);
        return accountId;
    }

    [Fact]
    public void NewWorld_IsPrivateByDefault()
    {
        var registry = NewRegistry();
        var accountId = NewAccount(registry);

        var world = registry.CreateWorld(accountId, "My World", "geheim").World!;

        Assert.False(world.IsPublic);
        Assert.False(registry.GetWorld(world.Id)!.IsPublic);
        Assert.Empty(registry.ListPublicWorlds());
    }

    [Fact]
    public void SetVisibility_Public_RequiresAPassword()
    {
        var registry = NewRegistry();
        var accountId = NewAccount(registry);
        var open = registry.CreateWorld(accountId, "Open World").World!;

        // No password → listing is refused, world stays private.
        var (ok, error) = registry.SetWorldVisibility(open.Id, isPublic: true);
        Assert.False(ok);
        Assert.Equal("A public world needs a join password first.", error);
        Assert.False(registry.GetWorld(open.Id)!.IsPublic);

        // Add a password → now it can be listed.
        Assert.True(registry.SetWorldPassword(open.Id, "geheim").Ok);
        Assert.True(registry.SetWorldVisibility(open.Id, isPublic: true).Ok);
        Assert.True(registry.GetWorld(open.Id)!.IsPublic);
    }

    [Fact]
    public void SetVisibility_UnknownWorld_Fails()
    {
        var registry = NewRegistry();
        Assert.False(registry.SetWorldVisibility("000000000000", isPublic: true).Ok);
        Assert.False(registry.SetWorldVisibility("000000000000", isPublic: false).Ok);
    }

    [Fact]
    public void RemovingThePassword_AlsoUnlistsThePublicWorld()
    {
        var registry = NewRegistry();
        var accountId = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Family World", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(world.Id, isPublic: true).Ok);
        Assert.True(registry.GetWorld(world.Id)!.IsPublic);

        // Removing the password (empty) must drop the public listing too — a public world without a
        // password would be joinable by anyone with no gate at all.
        Assert.True(registry.SetWorldPassword(world.Id, string.Empty).Ok);
        Assert.False(registry.GetWorld(world.Id)!.HasPassword);
        Assert.False(registry.GetWorld(world.Id)!.IsPublic);
        Assert.Empty(registry.ListPublicWorlds());
    }

    [Fact]
    public void ChangingThePassword_KeepsThePublicListing()
    {
        var registry = NewRegistry();
        var accountId = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Family World", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(world.Id, isPublic: true).Ok);

        // A non-empty change is not a removal → the world stays listed.
        Assert.True(registry.SetWorldPassword(world.Id, "neues-passwort").Ok);
        Assert.True(registry.GetWorld(world.Id)!.IsPublic);
    }

    [Fact]
    public void ListPublicWorlds_ReturnsOnlyListedWorlds_AcrossOwners()
    {
        var registry = NewRegistry();
        var alice = NewAccount(registry, "Alice");
        var bob = NewAccount(registry, "Bob");

        var alicePublic = registry.CreateWorld(alice, "Alice Public", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(alicePublic.Id, true).Ok);
        registry.CreateWorld(alice, "Alice Private", "geheim"); // has a password but not listed
        var bobPublic = registry.CreateWorld(bob, "Bob Public", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(bobPublic.Id, true).Ok);

        var listed = registry.ListPublicWorlds();

        Assert.Equal(2, listed.Count); // cross-owner, only the two listed worlds
        Assert.Contains(listed, w => w.Id == alicePublic.Id);
        Assert.Contains(listed, w => w.Id == bobPublic.Id);
        Assert.All(listed, w => Assert.True(w.IsPublic));
        Assert.All(listed, w => Assert.True(w.HasPassword)); // gated by construction
    }

    [Fact]
    public void SetVisibility_False_Unlists()
    {
        var registry = NewRegistry();
        var accountId = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "Family World", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(world.Id, true).Ok);
        Assert.Single(registry.ListPublicWorlds());

        Assert.True(registry.SetWorldVisibility(world.Id, false).Ok);
        Assert.False(registry.GetWorld(world.Id)!.IsPublic);
        Assert.Empty(registry.ListPublicWorlds());
    }

    [Fact]
    public void OldRegistry_WithoutTheIsPublicColumn_IsUpgradedInPlace()
    {
        // A pre-feature registry: the world table has no is_public column (but does have password_hash).
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
                    last_active_unix INTEGER NOT NULL DEFAULT 0,
                    password_hash TEXT NOT NULL DEFAULT '');
                INSERT INTO world VALUES('abcdefabcdef', 'acc-1', 'Old World', 'secret', 32000, 'stopped', '', 1, 0, 0, '');
                """;
            cmd.ExecuteNonQuery();
        }

        var registry = NewRegistry(dbPath);

        // The pre-existing world reads back private and can be listed once it has a password.
        var world = registry.GetWorld("abcdefabcdef");
        Assert.NotNull(world);
        Assert.False(world!.IsPublic);
        Assert.True(registry.SetWorldPassword(world.Id, "geheim").Ok);
        Assert.True(registry.SetWorldVisibility(world.Id, true).Ok);
        Assert.True(registry.GetWorld(world.Id)!.IsPublic);
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

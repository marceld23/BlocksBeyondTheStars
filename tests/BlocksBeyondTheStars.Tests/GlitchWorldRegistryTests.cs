// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Registry side of the glitch.fun arcade channel: arcade worlds live on channel 'glitch' with a
/// synthetic owner and no quota, are invisible to every portal listing (public browser AND per-account
/// lists), and the guest/install-ban bookkeeping keys on Glitch's pseudonymous install id. Existing
/// registries gain the channel column through the tolerant in-place upgrade.
/// </summary>
public sealed class GlitchWorldRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public GlitchWorldRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_glitch_reg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string NewDbPath() => Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");

    private HostRegistry NewRegistry(WorldHostConfig? config = null, string? dbPath = null)
    {
        var registry = new HostRegistry(config ?? new WorldHostConfig(), dbPath ?? NewDbPath());
        _registries.Add(registry);
        return registry;
    }

    [Fact]
    public void CreateGlitchWorld_SetsChannelAndSyntheticOwner_NoAccountQuota()
    {
        var registry = NewRegistry(new WorldHostConfig { MaxWorldsPerAccount = 2 });

        // Three arcade worlds despite the 2-per-account portal quota — the pool is operator policy.
        for (int i = 1; i <= 3; i++)
        {
            var (ok, error, world) = registry.CreateGlitchWorld($"Glitch Arcade {i}");
            Assert.True(ok, error);
            Assert.Equal(WorldChannel.Glitch, world!.Channel);
            Assert.Equal(WorldChannel.Glitch, world.OwnerAccountId);
            Assert.False(world.HasPassword);
        }

        Assert.Equal(3, registry.ListWorldsByChannel(WorldChannel.Glitch).Count);
    }

    [Fact]
    public void GlitchWorlds_NeverSurfaceInPortalListings()
    {
        var registry = NewRegistry();
        var arcade = registry.CreateGlitchWorld("Glitch Arcade 1").World!;

        // Even if an arcade world somehow ends up password-protected AND publicly flagged, the public
        // browser filters by channel — belt and braces against a future admin/API slip.
        Assert.True(registry.SetWorldPassword(arcade.Id, "geheim").Ok);
        Assert.True(registry.SetWorldVisibility(arcade.Id, true).Ok);

        Assert.Empty(registry.ListPublicWorlds());
        Assert.Empty(registry.ListWorlds(WorldChannel.Glitch)); // the synthetic owner id lists nothing

        // Portal worlds are unaffected by the channel filter.
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var portal = registry.CreateWorld(accountId, "Family World", "geheim").World!;
        Assert.True(registry.SetWorldVisibility(portal.Id, true).Ok);
        Assert.Single(registry.ListPublicWorlds());
        Assert.Single(registry.ListWorlds(accountId));
    }

    [Fact]
    public void ListAllWorldsAdmin_IncludesGlitchWorlds_WithChannelAndOwnerLabel()
    {
        var registry = NewRegistry();
        registry.CreateGlitchWorld("Glitch Arcade 1");

        var rows = registry.ListAllWorldsAdmin();
        var entry = Assert.Single(rows);
        Assert.Equal(WorldChannel.Glitch, entry.World.Channel);
        Assert.Equal("glitch.fun", entry.OwnerName);
    }

    [Fact]
    public void OldRegistry_WithoutTheChannelColumn_IsUpgradedInPlace()
    {
        // A pre-arcade registry: the world table has no channel column yet.
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
                    password_hash TEXT NOT NULL DEFAULT '',
                    is_public INTEGER NOT NULL DEFAULT 0);
                INSERT INTO world VALUES('abcdefabcdef', 'acc-1', 'Old World', 'secret', 32000, 'stopped', '', 1, 0, 0, '', 0);
                """;
            cmd.ExecuteNonQuery();
        }

        var registry = NewRegistry(dbPath: dbPath);

        // The pre-existing world reads back as a portal world; arcade creation works alongside it.
        var world = registry.GetWorld("abcdefabcdef");
        Assert.NotNull(world);
        Assert.Equal(WorldChannel.Portal, world!.Channel);
        Assert.True(registry.CreateGlitchWorld("Glitch Arcade 1").Ok);
        Assert.Single(registry.ListWorldsByChannel(WorldChannel.Glitch));
    }

    [Fact]
    public void GlitchGuests_UpsertOnTouch_AndListMostRecentFirst()
    {
        var registry = NewRegistry();
        registry.TouchGlitchGuest("install-aaaa", "Max-a1b");
        registry.TouchGlitchGuest("install-aaaa", "Maxine-a1b"); // renamed on Glitch — name follows
        registry.TouchGlitchGuest("install-bbbb", "Ida-c2d");

        var guests = registry.ListGlitchGuests();
        Assert.Equal(2, guests.Count);
        var a = guests.Single(g => g.InstallId == "install-aaaa");
        Assert.Equal("Maxine-a1b", a.PlayerName);
        Assert.Equal(2, a.Sessions);
    }

    [Fact]
    public void GlitchBans_SetLookupListAndUnban()
    {
        var registry = NewRegistry();
        Assert.Null(registry.GetGlitchBan("install-aaaa"));

        registry.SetGlitchBanned("install-aaaa", banned: true, reason: "griefing", playerName: "Max-a1b");
        var ban = registry.GetGlitchBan("install-aaaa");
        Assert.NotNull(ban);
        Assert.Equal("griefing", ban!.Reason);
        Assert.Single(registry.ListGlitchBans());

        // Re-banning updates the reason instead of failing on the primary key.
        registry.SetGlitchBanned("install-aaaa", banned: true, reason: "worse griefing");
        Assert.Equal("worse griefing", registry.GetGlitchBan("install-aaaa")!.Reason);

        registry.SetGlitchBanned("install-aaaa", banned: false, reason: string.Empty);
        Assert.Null(registry.GetGlitchBan("install-aaaa"));
        Assert.Empty(registry.ListGlitchBans());
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

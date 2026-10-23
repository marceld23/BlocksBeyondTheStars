// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Legal / account-lifecycle additions: DSGVO account self-deletion (cascade over every registry row keyed by the
/// account and its worlds incl. on-disk saves) and the legal-config plumbing that drives the Impressum/Datenschutz pages.
/// </summary>
public sealed class WorldHostLegalTests : IDisposable
{
    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldHostLegalTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_whl_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Registry tables allowed to keep a deleted account's id after <see cref="HostRegistry.DeleteAccount"/>. Empty on
    /// purpose: nothing in the registry keeps it. What other players recorded about the player stays — another
    /// account's report naming the player, a world owner's ban by name — but it carries the in-game name, never the
    /// account id, because it is that other account's data. A table goes here only with a comment saying why it keeps
    /// the id.
    /// </summary>
    private static readonly string[] TablesThatMayKeepADeletedAccountId = Array.Empty<string>();

    private HostRegistry NewRegistry(WorldHostConfig config)
        => NewRegistry(config, System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));

    private HostRegistry NewRegistry(WorldHostConfig config, string dbPath)
    {
        var registry = new HostRegistry(config, dbPath);
        _registries.Add(registry);
        return registry;
    }

    [Fact]
    public void DeleteAccount_RemovesAccount_Sessions_AndReports()
    {
        var config = new WorldHostConfig();
        var registry = NewRegistry(config);

        var (_, _, accountId, session) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var (_, _, otherId, _) = registry.CreateAccount("Someone", "super-secret-1", acceptedTermsVersion: 1);
        registry.CreateReport(accountId, "w1", "Meanie", "chat", "was mean");
        registry.CreateReport(otherId, "w1", "Owner", "chat", "counter-report");

        registry.DeleteAccount(accountId);

        Assert.Null(registry.ResolveSession(session));                 // session gone
        Assert.Null(registry.Login("Owner", "super-secret-1"));        // account gone (name reusable)
        var open = registry.ListOpenReports();
        Assert.Single(open);                                           // only the OTHER account's report survives
        Assert.Equal("Owner", open[0].ReportedName);
    }

    [Fact]
    public void DeleteAccount_LeavesNoRowWithTheAccountId_InAnyTable()
    {
        // #2920: the erasure left the rescue codes behind. Instead of a hand list of tables, every column of every
        // table in the registry schema is searched for the account id, so a table added later is covered too.
        string dbPath = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");
        var registry = NewRegistry(new WorldHostConfig { WorldsDir = System.IO.Path.Combine(_root, "worlds") }, dbPath);

        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var (_, _, otherId, _) = registry.CreateAccount("Someone", "super-secret-1", acceptedTermsVersion: 1);
        var ownWorld = registry.CreateWorld(accountId, "My World").World!;
        var otherWorld = registry.CreateWorld(otherId, "Their World").World!;

        // The account's own rows: rescue codes, a filed report, a notice, a visit to and a ban on someone else's world.
        Assert.NotEmpty(registry.CreateRecoveryCodes(accountId));
        Assert.True(registry.CreateReport(accountId, otherWorld.Id, "Meanie", "chat", "was mean").Ok);
        registry.AddNotice(accountId, NoticeRecord.KindWorldDeleted, "Gone", string.Empty);
        registry.RecordWorldVisitor(otherWorld.Id, accountId, "Owner");
        Assert.True(registry.AddWorldBan(otherWorld.Id, accountId, "Owner", "kept blowing up the base"));

        // The other account's rows about this player, which carry the name or the world id but never the account id.
        Assert.True(registry.CreateReport(otherId, ownWorld.Id, "Owner", "chat", "counter-report").Ok);
        registry.RecordWorldVisitor(ownWorld.Id, otherId, "Someone");

        // The fixture must reach every table keyed by an account, or the final check passes vacuously for a table it
        // never filled.
        var seeded = TablesHolding(dbPath, accountId);
        foreach (string table in AccountKeyedTables(dbPath))
        {
            Assert.Contains(table, seeded);
        }

        // DELETE /api/account first deletes the account's worlds through the orchestrator (whose registry step is
        // DeleteWorld), then the account.
        registry.DeleteWorld(ownWorld.Id);
        registry.DeleteAccount(accountId);

        Assert.Empty(TablesHolding(dbPath, accountId).Except(TablesThatMayKeepADeletedAccountId, StringComparer.Ordinal));
        Assert.Contains("Owner", registry.ListOpenReports().Select(r => r.ReportedName)); // the other account's report stays
    }

    [Fact]
    public void DeleteWorldData_ErasesLiveAndArchivedSaves()
    {
        var config = new WorldHostConfig { WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var world = registry.CreateWorld(accountId, "My World").World!;

        // Simulate both a live save and an archived copy on disk.
        foreach (var dir in new[] { SavePaths.HostSavesDir(config, world.Id), SavePaths.ArchivedSavesDir(config, world.Id) })
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "world.db"), "bytes");
        }

        SavePaths.DeleteWorldData(config, world.Id);

        Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(config.WorldsDir, world.Id)));
        Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(config.WorldsDir, "_archive", world.Id)));
    }

    [Fact]
    public void LegalConfig_LoadsFromEnvironment()
    {
        // The Impressum/Datenschutz pages read these operator-set values; verify the env plumbing.
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_NAME", "Marcel Dütscher");
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_ADDRESS", "Bresslauer Str. 20, 54295 Trier");
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_EMAIL", "info@blocksbeyondthestars.de");
        try
        {
            var c = WorldHostConfig.FromEnvironment();
            Assert.Equal("Marcel Dütscher", c.LegalName);
            Assert.Equal("Bresslauer Str. 20, 54295 Trier", c.LegalAddress);
            Assert.Equal("info@blocksbeyondthestars.de", c.LegalEmail);

            // The pages render without throwing and carry the configured data / no placeholder.
            // (Non-ASCII like "ü" is HTML-encoded, so assert on ASCII-safe tokens.)
            string impressum = WorldHostPortalPages.Impressum(c);
            Assert.Contains("Marcel", impressum, StringComparison.Ordinal);
            Assert.Contains("Bresslauer Str. 20", impressum, StringComparison.Ordinal);
            Assert.Contains("info@blocksbeyondthestars.de", impressum, StringComparison.Ordinal);
            Assert.DoesNotContain("noch nicht", impressum, StringComparison.Ordinal); // no unconfigured notice
            Assert.Contains("§ 5 DDG", impressum, StringComparison.Ordinal);

            string privacy = WorldHostPortalPages.Privacy(c);
            Assert.Contains("Datenschutz", privacy, StringComparison.Ordinal);
            Assert.Contains("kein Tracking", privacy, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_NAME", null);
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_ADDRESS", null);
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_EMAIL", null);
        }
    }

    [Fact]
    public void Impressum_UnconfiguredOperator_ShowsNotice_NotWrongData()
    {
        // A self-hosted WorldHost with no legal config must NOT serve the project authors' identity.
        string impressum = WorldHostPortalPages.Impressum(new WorldHostConfig());
        Assert.Contains("noch nicht", impressum, StringComparison.Ordinal); // the "not configured" notice
        Assert.DoesNotContain("Breslauer", impressum, StringComparison.Ordinal);
    }

    /// <summary>Every registry table with at least one row holding <paramref name="value"/> in any column. Tables come
    /// from <c>sqlite_master</c> and columns from <c>table_info</c>, so the search follows the schema by itself.</summary>
    private static HashSet<string> TablesHolding(string dbPath, string value)
    {
        var hits = new HashSet<string>(StringComparer.Ordinal);
        using var db = OpenRegistryFile(dbPath);
        foreach (string table in UserTables(db))
        {
            foreach (string column in Columns(db, table))
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"{column}\" = $v";
                cmd.Parameters.AddWithValue("$v", value);
                if (Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
                {
                    hits.Add(table);
                    break;
                }
            }
        }

        return hits;
    }

    /// <summary>The tables whose schema keys rows by an account: <c>account</c> itself and every table with an
    /// <c>account_id</c> or <c>…_account_id</c> column.</summary>
    private static List<string> AccountKeyedTables(string dbPath)
    {
        using var db = OpenRegistryFile(dbPath);
        return UserTables(db)
            .Where(t => t == "account"
                || Columns(db, t).Any(c => c == "account_id" || c.EndsWith("_account_id", StringComparison.Ordinal)))
            .ToList();
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenRegistryFile(string dbPath)
    {
        // Unpooled like the registry's own connection, so the file is released when the test is done with it.
        var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
        db.Open();
        return db;
    }

    private static List<string> UserTables(Microsoft.Data.Sqlite.SqliteConnection db)
        => Query(db, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name", null);

    private static List<string> Columns(Microsoft.Data.Sqlite.SqliteConnection db, string table)
        => Query(db, "SELECT name FROM pragma_table_info($t)", table);

    private static List<string> Query(Microsoft.Data.Sqlite.SqliteConnection db, string sql, string? table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        if (table != null)
        {
            cmd.Parameters.AddWithValue("$t", table);
        }

        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
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

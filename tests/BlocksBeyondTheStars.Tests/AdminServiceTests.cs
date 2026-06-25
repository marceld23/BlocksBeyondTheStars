// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class AdminServiceTests : IDisposable
{
    private readonly string _install;

    public AdminServiceTests()
    {
        _install = Path.Combine(Path.GetTempPath(), "bbts_admin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_install);
    }

    [Fact]
    public void Config_RoundTripsThroughService()
    {
        var admin = new AdminService(_install);

        var config = admin.LoadConfig(); // creates a default config file
        config.ServerName = "My Server";
        config.MaxPlayers = 8;
        admin.SaveConfig(config);

        var reloaded = new AdminService(_install).LoadConfig();
        Assert.Equal("My Server", reloaded.ServerName);
        Assert.Equal(8, reloaded.MaxPlayers);
    }

    [Fact]
    public void Status_ReportsWorldAndBackup_AfterWorldCreated()
    {
        var admin = new AdminService(_install);
        var config = admin.LoadConfig();
        admin.SaveConfig(config);

        // Create a world via the repository in the same install layout.
        var paths = new SaveGamePaths(Path.Combine(_install, config.SavesRoot), config.WorldName);
        using (var repo = new SqliteWorldRepository(paths))
        {
            repo.Initialize();
            repo.SaveMetadata(new WorldMetadata { WorldName = config.WorldName, Seed = 5 });
            repo.SavePlayer(new PlayerState { PlayerId = "p", Name = "p" });
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var before = admin.GetStatus();
        Assert.True(before.WorldExists);
        Assert.Equal(1, before.RegisteredPlayers);

        var backupName = admin.CreateBackup();
        Assert.EndsWith(".db", backupName);
        Assert.True(admin.GetStatus().BackupCount >= 1);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_install))
            {
                Directory.Delete(_install, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup races
        }
    }
}

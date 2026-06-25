// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Api;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class AdminContentTests : IDisposable
{
    private readonly string _install;

    public AdminContentTests()
    {
        _install = Path.Combine(Path.GetTempPath(), "bbts_admincontent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_install);
    }

    private AdminService NewAdmin()
    {
        var admin = new AdminService(_install);
        var cfg = admin.LoadConfig();
        cfg.DataDir = TestPaths.DataDir();                  // absolute → content loads in tests
        cfg.SavesRoot = Path.Combine(_install, "saves");
        admin.SaveConfig(cfg);
        return admin;
    }

    private static MissionDefinition ValidMission(string id) => new()
    {
        Id = id,
        Title = "Admin Ore Run",
        Objectives = { new MissionObjective { Type = MissionObjectiveType.Mine, Target = "iron_ore", Required = 5 } },
        Rewards = { new BlocksBeyondTheStars.Shared.Definitions.ItemAmount("iron_plate", 2) },
    };

    [Fact]
    public void SaveAdminMission_Valid_PersistsAsAdminSource()
    {
        var admin = NewAdmin();
        var result = admin.SaveAdminMission(ValidMission("am_test"));
        Assert.True(result.Success);

        var missions = admin.ListAdminMissions();
        var m = Assert.Single(missions);
        Assert.Equal(MissionSource.Admin, m.Source);
        Assert.Equal("am_test", m.Id);
    }

    [Fact]
    public void SaveAdminMission_Invalid_IsRejectedWithProblems()
    {
        var admin = NewAdmin();
        var bad = new MissionDefinition
        {
            Id = "am_bad",
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Mine, Target = "does_not_exist", Required = 1 } },
        };

        var result = admin.SaveAdminMission(bad);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Problems);
        Assert.Empty(admin.ListAdminMissions());
    }

    [Fact]
    public void ContentPack_ExportThenImport_RoundTrips()
    {
        var admin = NewAdmin();
        admin.SaveAdminMission(ValidMission("am_roundtrip"));

        var json = admin.ExportContentPack();
        admin.DeleteAdminMission("am_roundtrip");
        Assert.Empty(admin.ListAdminMissions());

        var import = admin.ImportContentPack(json);
        Assert.True(import.Success);
        Assert.Single(admin.ListAdminMissions());
    }

    [Fact]
    public void ImportContentPack_RejectsInvalidMissions()
    {
        var admin = NewAdmin();
        var pack = new ContentPack
        {
            Missions =
            {
                new MissionDefinition
                {
                    Id = "am_bad",
                    Objectives = { new MissionObjective { Type = MissionObjectiveType.Deliver, Target = "ghost_item", Required = 1 } },
                },
            },
        };
        var json = System.Text.Json.JsonSerializer.Serialize(pack);

        var result = admin.ImportContentPack(json);
        Assert.False(result.Success);
        Assert.Empty(admin.ListAdminMissions());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
        }
        catch { }
    }
}

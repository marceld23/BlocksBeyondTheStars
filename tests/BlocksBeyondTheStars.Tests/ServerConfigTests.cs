// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Command-line overrides used to embed/launch the server (e.g. local singleplayer host).</summary>
public sealed class ServerConfigTests
{
    [Fact]
    public void ApplyCommandLine_OverridesKnownKeys()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--port", "31550",
            "--name", "Singleplayer",
            "--world", "singleplayer",
            "--saves", @"C:\sp\saves",
            "--data", @"C:\sp\data",
            "--database-provider", "postgresql",
            "--postgres-connection-string", "Host=db;Database=bbs;Username=bbs;Password=secret",
            "--max-players", "1",
            "--view-distance", "3",
        });

        Assert.Equal(31550, config.GameplayPort);
        Assert.Equal("Singleplayer", config.ServerName);
        Assert.Equal("singleplayer", config.WorldName);
        Assert.Equal(@"C:\sp\saves", config.SavesRoot);
        Assert.Equal(@"C:\sp\data", config.DataDir);
        Assert.Equal("postgresql", config.DatabaseProvider);
        Assert.Equal("Host=db;Database=bbs;Username=bbs;Password=secret", config.PostgresConnectionString);
        Assert.Equal(1, config.MaxPlayers);
        Assert.Equal(3, config.ViewDistanceChunks);
        Assert.Contains("port", applied);
        Assert.Contains("max-players", applied);
        Assert.Contains("view-distance", applied);
    }

    [Fact]
    public void ApplyCommandLine_MapsChunkStreamBudgetAndPerTick()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--chunk-stream-per-tick", "24",
            "--chunk-stream-budget-ms", "12.5", // invariant decimal point, must parse regardless of host locale
        });

        Assert.Equal(24, config.ChunkStreamPerTick);
        Assert.Equal(12.5, config.ChunkStreamBudgetMs);
        Assert.Contains("chunk-stream-per-tick", applied);
        Assert.Contains("chunk-stream-budget-ms", applied);
    }

    [Fact]
    public void ApplyCommandLine_RejectsInvalidChunkStreamValues()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--chunk-stream-per-tick", "0",     // must be >= 1
            "--chunk-stream-budget-ms", "-3",   // must be >= 0
        });

        Assert.Equal(16, config.ChunkStreamPerTick);     // untouched default
        Assert.Equal(0.0, config.ChunkStreamBudgetMs);   // untouched default (off)
        Assert.DoesNotContain("chunk-stream-per-tick", applied);
        Assert.DoesNotContain("chunk-stream-budget-ms", applied);
    }

    [Fact]
    public void ApplyEnvironment_MapsChunkStreamBudgetAndPerTick()
    {
        var vars = new Dictionary<string, string?>
        {
            ["BBS_CHUNK_STREAM_PER_TICK"] = "20",
            ["BBS_CHUNK_STREAM_BUDGET_MS"] = "25",
        };

        WithEnvironment(vars, () =>
        {
            var config = new ServerConfig();
            var applied = config.ApplyEnvironment();

            Assert.Equal(20, config.ChunkStreamPerTick);
            Assert.Equal(25.0, config.ChunkStreamBudgetMs);
            Assert.Contains("BBS_CHUNK_STREAM_PER_TICK", applied);
            Assert.Contains("BBS_CHUNK_STREAM_BUDGET_MS", applied);
        });
    }

    [Fact]
    public void ApplyCommandLine_OverridesSpaceRules()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--free-flight", "true", "--space-combat", "PvE", "--space-npcs", "Normal" });

        Assert.True(config.Rules.FreeSpaceFlight);
        Assert.Equal(SpaceCombatMode.PvE, config.Rules.SpaceCombat);
        Assert.Equal(AlienActivity.Normal, config.Rules.SpaceNpcEnemies);
        Assert.Contains("free-flight", applied);
    }

    [Fact]
    public void ApplyCommandLine_AdminCheatsFlagEnablesCheatsInEveryMode()
    {
        // #642: the bundled singleplayer/host launcher passes --admin-cheats true so /tp, /give etc.
        // work out of the box. The flag must satisfy CheatsAllowed in survival (the singleplayer
        // default mode), which needs BOTH AdminCheats and AllowCheatsInSurvival.
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--admin-cheats", "true" });

        Assert.True(config.Rules.AdminCheats);
        Assert.True(config.Rules.AllowCheatsInSurvival);
        Assert.True(config.Rules.CheatsAllowed);
        Assert.Contains("admin-cheats", applied);

        // Without the flag the gate stays closed — dedicated servers keep the off default.
        Assert.False(new ServerConfig().Rules.CheatsAllowed);
    }

    [Fact]
    public void ApplyCommandLine_GameModeFlagSelectsSandbox()
    {
        // #662: the launcher passes --game-mode Creative when the player picks Sandbox at world
        // creation. Case-insensitive; an unknown value leaves the Survival default untouched.
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--game-mode", "creative" });

        Assert.Equal(GameMode.Creative, config.Rules.GameMode);
        Assert.False(config.Rules.CraftingCostsMaterials);
        Assert.False(config.Rules.OxygenEnabled);
        Assert.False(config.Rules.HungerEnabled);
        Assert.Contains("game-mode", applied);

        var bad = new ServerConfig();
        bad.ApplyCommandLine(new[] { "--game-mode", "nonsense" });
        Assert.Equal(GameMode.Survival, bad.Rules.GameMode);
    }

    [Fact]
    public void ApplyCommandLine_OverridesShipWeaponsAndKeepRules()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--ship-weapons", "NpcsOnly", "--keep-ship", "false", "--keep-inventory", "true",
        });

        Assert.Equal(ShipWeaponMode.NpcsOnly, config.Rules.ShipWeapons);
        Assert.False(config.Rules.KeepShipOnDeath);
        Assert.True(config.Rules.KeepInventoryOnDeath);
        Assert.Contains("ship-weapons", applied);
        Assert.Contains("keep-ship", applied);
        Assert.Contains("keep-inventory", applied);
    }

    [Fact]
    public void ApplyCommandLine_OverridesAutoAim()
    {
        // #693: auto-aim is a world rule — ON by default, --auto-aim false mandates manual aiming.
        Assert.True(new ServerConfig().Rules.AutoAim);

        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--auto-aim", "false" });
        Assert.False(config.Rules.AutoAim);
        Assert.Contains("auto-aim", applied);
    }

    [Fact]
    public void ApplyCommandLine_OverridesStructureTemplateOptions()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[]
        {
            "--station-templates", "Frequent",
            "--settlement-templates", "Off",
            "--structure-packs", "vanilla,mybuilds",
            "--usercontent", @"C:\sp\usercontent",
        });

        Assert.Equal(Frequency.Frequent, config.World.StationTemplateUse);
        Assert.Equal(Frequency.Off, config.World.SettlementTemplateUse);
        Assert.Equal(new[] { "vanilla", "mybuilds" }, config.World.EnabledStructurePacks);
        Assert.Equal(@"C:\sp\usercontent", config.UserContentDir);
        Assert.Contains("station-templates", applied);
        Assert.Contains("structure-packs", applied);
        Assert.Contains("usercontent", applied);
    }

    [Fact]
    public void ApplyCommandLine_IgnoresUnknownKeysAndKeepsDefaults()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        var applied = config.ApplyCommandLine(new[] { "--unknown", "x", "--also-unknown", "y" });

        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);
    }

    [Fact]
    public void ApplyCommandLine_HandlesMissingValueAndNullSafely()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        // Trailing flag with no value must not throw or change anything.
        var applied = config.ApplyCommandLine(new[] { "--port" });
        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);

        Assert.Empty(config.ApplyCommandLine(null));
    }

    [Fact]
    public void ApplyCommandLine_RejectsNonNumericPort()
    {
        var config = new ServerConfig();
        int defaultPort = config.GameplayPort;

        var applied = config.ApplyCommandLine(new[] { "--port", "notaport" });

        Assert.Empty(applied);
        Assert.Equal(defaultPort, config.GameplayPort);
    }

    [Fact]
    public void ApplyEnvironment_OverridesKnownKeys()
    {
        var vars = new Dictionary<string, string?>
        {
            ["BBS_PORT"] = "32000",
            ["BBS_ADMIN_PORT"] = "32001",
            ["BBS_MAX_PLAYERS"] = "8",
            ["BBS_ADMIN_BIND"] = "0.0.0.0",
            ["BBS_ADMIN_PASSWORD"] = "secret",
            ["BBS_ENABLE_WEBSOCKET"] = "true",
            ["BBS_ADMINS"] = "Alice, Bob",
            ["BBS_WORLD"] = "dockerworld",
            ["BBS_FREE_FLIGHT"] = "false",
            ["BBS_SPACE_COMBAT"] = "PvE",
            ["BBS_SHIP_WEAPONS"] = "All",
            ["BBS_SPACE_NPCS"] = "Normal",
            ["BBS_AI_LEVEL"] = "Suggest",
            ["BBS_DATABASE_PROVIDER"] = "postgresql",
            ["BBS_POSTGRES_CONNECTION_STRING"] = "Host=db;Database=bbs;Username=bbs;Password=secret",
        };

        WithEnvironment(vars, () =>
        {
            var config = new ServerConfig();
            var applied = config.ApplyEnvironment();

            Assert.Equal(32000, config.GameplayPort);
            Assert.Equal(32001, config.AdminPort);
            Assert.Equal(8, config.MaxPlayers);
            Assert.Equal("0.0.0.0", config.AdminBindAddress);
            Assert.Equal("secret", config.AdminPassword);
            Assert.True(config.EnableWebSocket);
            Assert.Equal(new[] { "Alice", "Bob" }, config.AdminPlayers);
            Assert.Equal("dockerworld", config.WorldName);
            Assert.False(config.Rules.FreeSpaceFlight);
            Assert.Equal(SpaceCombatMode.PvE, config.Rules.SpaceCombat);
            Assert.Equal(ShipWeaponMode.All, config.Rules.ShipWeapons);
            Assert.Equal(AlienActivity.Normal, config.Rules.SpaceNpcEnemies);
            Assert.Equal(AiLevel.Suggest, config.AiLevel);
            Assert.Equal("postgresql", config.DatabaseProvider);
            Assert.Equal("Host=db;Database=bbs;Username=bbs;Password=secret", config.PostgresConnectionString);
            Assert.Contains("BBS_PORT", applied);
            Assert.Contains("BBS_ADMIN_BIND", applied);
            Assert.Contains("BBS_FREE_FLIGHT", applied);
            Assert.Contains("BBS_AI_LEVEL", applied);
        });
    }

    [Fact]
    public void RepositoryFactory_SelectsPostgreSqlFromConfig()
    {
        var config = new ServerConfig
        {
            DatabaseProvider = "postgres",
            PostgresConnectionString = "Host=db;Database=bbs;Username=bbs;Password=secret",
        };

        using var repo = WorldRepositoryFactory.Create(config, new SaveGamePaths(Path.GetTempPath(), "factory"));

        Assert.IsType<PostgreSqlWorldRepository>(repo);
        Assert.True(WorldRepositoryFactory.IsPostgreSql(config));
        Assert.Equal("PostgreSQL", WorldRepositoryFactory.DisplayName(config));
    }

    [Fact]
    public void ApplyEnvironment_IgnoresUnsetAndUnparseableValues()
    {
        var vars = new Dictionary<string, string?>
        {
            ["BBS_PORT"] = "notaport",
            ["BBS_MAX_PLAYERS"] = "",
        };

        WithEnvironment(vars, () =>
        {
            var config = new ServerConfig();
            int defaultPort = config.GameplayPort;
            int defaultMax = config.MaxPlayers;

            var applied = config.ApplyEnvironment();

            Assert.Empty(applied);
            Assert.Equal(defaultPort, config.GameplayPort);
            Assert.Equal(defaultMax, config.MaxPlayers);
        });
    }

    [Fact]
    public void CommandLine_TakesPrecedenceOverEnvironment()
    {
        WithEnvironment(new Dictionary<string, string?> { ["BBS_PORT"] = "32000" }, () =>
        {
            var config = new ServerConfig();
            config.ApplyEnvironment();          // env layer: 32000
            config.ApplyCommandLine(new[] { "--port", "33000" }); // CLI layer wins

            Assert.Equal(33000, config.GameplayPort);
        });
    }

    // --- Startup config resolution (--no-config): the bundled singleplayer host must ignore any stale
    // config/server.json next to its exe so it always starts from current code defaults (e.g. StartPlanet). ---

    [Fact]
    public void LoadForStartup_WithNoConfigFlag_IgnoresExistingFileAndUsesDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "bbts_noconfig_" + Guid.NewGuid().ToString("N"), "server.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A stale file pinning the OLD start planet — exactly the situation that made a fresh build spawn "rocky".
        new ServerConfig { StartPlanet = "rocky" }.Save(path);
        var fileBefore = File.ReadAllText(path);

        try
        {
            var config = ServerConfig.LoadForStartup(new[] { "--no-config", "true" }, path);

            Assert.Equal("varied", config.StartPlanet); // code default, NOT the file's "rocky"
            Assert.Equal(fileBefore, File.ReadAllText(path)); // the file is neither read into effect nor rewritten
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void LoadForStartup_WithNoConfigFlag_DoesNotCreateAFileWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "bbts_noconfig_" + Guid.NewGuid().ToString("N"), "server.json");

        var config = ServerConfig.LoadForStartup(new[] { "--no-config", "true" }, path);

        Assert.Equal("varied", config.StartPlanet);
        Assert.False(File.Exists(path)); // unlike Load(), --no-config never writes a default file
    }

    [Fact]
    public void LoadForStartup_WithoutFlag_ReadsTheConfigFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "bbts_cfg_" + Guid.NewGuid().ToString("N"), "server.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new ServerConfig { StartPlanet = "ice" }.Save(path);

        try
        {
            var config = ServerConfig.LoadForStartup(new[] { "--port", "31550" }, path); // no --no-config
            Assert.Equal("ice", config.StartPlanet); // the dedicated-server flow still honours the file
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ApplyCommandLine_NoConfigFlagConsumesItsValueWithoutShadowingTheNextFlag()
    {
        // --no-config is a valued flag (" --no-config true") so it must not swallow the following --port.
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--no-config", "true", "--port", "31550" });

        Assert.Equal(31550, config.GameplayPort);
        Assert.Contains("port", applied);
        Assert.DoesNotContain("no-config", applied); // recognized no-op, nothing applied to the config
    }

    /// <summary>Sets the given environment variables for the duration of <paramref name="body"/>, then restores them.</summary>
    private static void WithEnvironment(Dictionary<string, string?> vars, Action body)
    {
        var previous = new Dictionary<string, string?>();
        foreach (var (key, value) in vars)
        {
            previous[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

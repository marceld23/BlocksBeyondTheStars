// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Pins every accepted <c>--switch</c> and <c>BBS_*</c> variable of <see cref="ServerConfig"/>: the canonical name each one
/// reports, the parse rules, range checks, clamps and companion setters, and the order <see cref="ServerConfig.ApplyEnvironment"/>
/// applies and reports variables in (Program.cs logs that list). Shares the <see cref="ServerConfigEnvironmentCollection"/>
/// with <see cref="ServerConfigTests"/> and the admin tests so their process-global environment-variable edits and reads
/// never interleave.
/// </summary>
[Collection(ServerConfigEnvironmentCollection.Name)]
public sealed class ServerConfigOptionsTests
{
    /// <summary>The 52 environment variables <see cref="ServerConfig.ApplyEnvironment"/> reads, in the order it applies and
    /// reports them.</summary>
    private static readonly string[] HistoricalEnvironmentOrder =
    {
        "BBS_SERVER_NAME", "BBS_WORLD", "BBS_PORT", "BBS_ADMIN_PORT", "BBS_MAX_PLAYERS", "BBS_PASSWORD", "BBS_ADMINS",
        "BBS_FLEET_ADMINS", "BBS_ADMIN_PASSWORD", "BBS_ADMIN_BIND", "BBS_ENABLE_WEBSOCKET", "BBS_WEBSOCKET_BIND", "BBS_SAVES",
        "BBS_DATA", "BBS_USERCONTENT", "BBS_DATABASE_PROVIDER", "BBS_POSTGRES_CONNECTION_STRING", "BBS_SEED",
        "BBS_START_PLANET", "BBS_TICK_RATE", "BBS_VIEW_DISTANCE", "BBS_CHUNK_STREAM_PER_TICK", "BBS_CHUNK_STREAM_BUDGET_MS",
        "BBS_CHUNK_GEN_WORKERS", "BBS_BACKUP_INTERVAL_MINUTES", "BBS_BACKUP_KEEP", "BBS_TICK_TIMING_LOG_SECONDS",
        "BBS_FREE_FLIGHT", "BBS_SPACE_COMBAT", "BBS_SHIP_WEAPONS", "BBS_SPACE_NPCS", "BBS_BANDITS", "BBS_AI_LEVEL",
        "BBS_AI_BACKEND_URL", "BBS_AI_TIMEOUT_SECONDS", "BBS_AI_LINES_PER_MINUTE", "BBS_CRASH_REPORT_ENDPOINT",
        "BBS_CRASH_REPORT_KEY", "BBS_NOTIFY_URL", "BBS_BLOCKED_WORDS", "BBS_WATCH_WORDS", "BBS_CHAT_FILTER",
        "BBS_CHAT_BLOCKED_WORDS", "BBS_CHAT_MASKED_WORDS", "BBS_CHAT_WATCH_WORDS", "BBS_CHAT_ALLOW_WORDS", "BBS_VOICE",
        "BBS_IDLE_SHUTDOWN_MINUTES", "BBS_JOIN_TOKEN_SECRET", "BBS_WORLD_OWNER", "BBS_NO_PLAYER_ADMINS", "BBS_ANNOUNCE_TOKEN",
    };

    /// <summary>The fallback variables, each consulted only while its canonical variable is unset or empty.</summary>
    private static readonly Dictionary<string, string> HistoricalFallbacks = new(StringComparer.Ordinal)
    {
        ["BBS_PORT"] = "BBS_GAMEPLAY_PORT",
        ["BBS_PASSWORD"] = "BBS_SERVER_PASSWORD",
        ["BBS_DATABASE_PROVIDER"] = "BBS_DATABASE",
        ["BBS_POSTGRES_CONNECTION_STRING"] = "DATABASE_URL",
    };

    /// <summary>Every switch spelling the command line accepts (canonical names and aliases, lower-case, without <c>--</c>).</summary>
    private static readonly string[] HistoricalSwitches95 =
    {
        "port", "gameplay-port", "admin-port", "saves", "saves-root", "data", "data-dir", "usercontent", "user-content",
        "no-config", "stdin-stop", "database", "database-provider", "postgres", "postgres-connection",
        "postgres-connection-string", "world", "world-name", "name", "server-name", "max-players", "view-distance",
        "view-distance-chunks", "chunk-stream-per-tick", "chunk-stream-budget-ms", "chunk-budget-ms", "chunk-gen-workers",
        "backup-interval-minutes", "backup-keep", "free-flight", "space-combat", "ship-weapons", "space-npcs", "password",
        "admins", "admin-players", "fleet-admins", "no-player-admins", "seed", "start-planet", "admin-bind", "game-mode",
        "creative-flight", "admin-cheats", "voice", "voice-chat", "chat-filter", "chat-mode", "unlock-all-blueprints",
        "start-all-ships", "creative-kit", "guarantee-start-cube", "creatures", "planet-enemies", "ufos", "bandits", "oxygen",
        "hunger", "hazards", "death-penalty", "keep-inventory", "keep-ship", "auto-aim", "world-textures",
        "starter-teleporter", "return-to-ship", "friendly-visitors", "machine-catchup", "story", "story-density", "tutorial",
        "flora", "ore", "settlements", "planet-wrecks", "vaults", "stations", "wormholes", "debris-fields", "derelicts",
        "exotic", "station-templates", "settlement-templates", "structure-packs", "systems", "planets-min", "planets-max",
        "moons-max", "variance", "belts", "continents", "terrain-generation", "galaxy-growth", "danger", "planet-types",
    };

    /// <summary>(switch, valid sample value, canonical name it reports) for every accepted spelling except the two no-op
    /// switches <c>no-config</c> and <c>stdin-stop</c>.</summary>
    public static TheoryData<string, string, string> HistoricalSwitches() => new()
    {
        { "port", "31550", "port" },
        { "gameplay-port", "31550", "port" },
        { "admin-port", "31551", "admin-port" },
        { "saves", "x", "saves" },
        { "saves-root", "x", "saves" },
        { "data", "x", "data" },
        { "data-dir", "x", "data" },
        { "usercontent", "x", "usercontent" },
        { "user-content", "x", "usercontent" },
        { "database", "x", "database-provider" },
        { "database-provider", "x", "database-provider" },
        { "postgres", "x", "postgres-connection-string" },
        { "postgres-connection", "x", "postgres-connection-string" },
        { "postgres-connection-string", "x", "postgres-connection-string" },
        { "world", "x", "world" },
        { "world-name", "x", "world" },
        { "name", "x", "name" },
        { "server-name", "x", "name" },
        { "max-players", "7", "max-players" },
        { "view-distance", "7", "view-distance" },
        { "view-distance-chunks", "7", "view-distance" },
        { "chunk-stream-per-tick", "7", "chunk-stream-per-tick" },
        { "chunk-stream-budget-ms", "2.5", "chunk-stream-budget-ms" },
        { "chunk-budget-ms", "2.5", "chunk-stream-budget-ms" },
        { "chunk-gen-workers", "7", "chunk-gen-workers" },
        { "backup-interval-minutes", "7", "backup-interval-minutes" },
        { "backup-keep", "7", "backup-keep" },
        { "free-flight", "true", "free-flight" },
        { "space-combat", "PvE", "space-combat" },
        { "ship-weapons", "All", "ship-weapons" },
        { "space-npcs", "Normal", "space-npcs" },
        { "password", "x", "password" },
        { "admins", "A, B", "admins" },
        { "admin-players", "A, B", "admins" },
        { "fleet-admins", "A, B", "fleet-admins" },
        { "no-player-admins", "true", "no-player-admins" },
        { "seed", "42", "seed" },
        { "start-planet", "lava", "start-planet" },
        { "admin-bind", "x", "admin-bind" },
        { "game-mode", "Creative", "game-mode" },
        { "creative-flight", "true", "creative-flight" },
        { "admin-cheats", "true", "admin-cheats" },
        { "voice", "true", "voice" },
        { "voice-chat", "true", "voice" },
        { "chat-filter", "Off", "chat-filter" },
        { "chat-mode", "Safe", "chat-mode" },
        { "unlock-all-blueprints", "true", "unlock-all-blueprints" },
        { "start-all-ships", "true", "start-all-ships" },
        { "creative-kit", "true", "creative-kit" },
        { "guarantee-start-cube", "true", "guarantee-start-cube" },
        { "creatures", "Normal", "creatures" },
        { "planet-enemies", "Normal", "planet-enemies" },
        { "ufos", "Normal", "ufos" },
        { "bandits", "Normal", "bandits" },
        { "oxygen", "Slow", "oxygen" },
        { "hunger", "Fast", "hunger" },
        { "hazards", "Hard", "hazards" },
        { "death-penalty", "None", "death-penalty" },
        { "keep-inventory", "true", "keep-inventory" },
        { "keep-ship", "true", "keep-ship" },
        { "auto-aim", "true", "auto-aim" },
        { "world-textures", "true", "world-textures" },
        { "starter-teleporter", "true", "starter-teleporter" },
        { "return-to-ship", "true", "return-to-ship" },
        { "friendly-visitors", "true", "friendly-visitors" },
        { "machine-catchup", "7", "machine-catchup" },
        { "story", "none", "story" },
        { "story-density", "Dense", "story-density" },
        { "tutorial", "optin", "tutorial" },
        { "flora", "Rare", "flora" },
        { "ore", "Rare", "ore" },
        { "settlements", "Rare", "settlements" },
        { "planet-wrecks", "Rare", "planet-wrecks" },
        { "vaults", "Rare", "vaults" },
        { "stations", "Rare", "stations" },
        { "wormholes", "Rare", "wormholes" },
        { "debris-fields", "Rare", "debris-fields" },
        { "derelicts", "Rare", "derelicts" },
        { "exotic", "Rare", "exotic" },
        { "station-templates", "Rare", "station-templates" },
        { "settlement-templates", "Rare", "settlement-templates" },
        { "structure-packs", "a,b", "structure-packs" },
        { "systems", "7", "systems" },
        { "planets-min", "3", "planets-min" },
        { "planets-max", "8", "planets-max" },
        { "moons-max", "4", "moons-max" },
        { "variance", "off", "variance" },
        { "belts", "off", "belts" },
        { "continents", "off", "continents" },
        { "terrain-generation", "0", "terrain-generation" },
        { "galaxy-growth", "off", "galaxy-growth" },
        { "danger", "Rare", "danger" },
        { "planet-types", "ocean=Rare", "planet-types" },
    };

    [Theory]
    [MemberData(nameof(HistoricalSwitches))]
    public void CommandLine_AcceptsEveryHistoricalSwitchUnderItsCanonicalName(string sw, string value, string canonical)
    {
        Assert.Equal(new[] { canonical }, new ServerConfig().ApplyCommandLine(new[] { "--" + sw, value }));
        // Switch names are matched after ToLowerInvariant(), so an upper-case spelling is the same switch.
        Assert.Equal(new[] { canonical }, new ServerConfig().ApplyCommandLine(new[] { "--" + sw.ToUpperInvariant(), value }));
    }

    [Fact]
    public void CommandLine_HistoricalSwitchDataCoversEverySpellingButTheNoOps()
    {
        var covered = HistoricalSwitches().Select(row => (string)row[0]).ToList();

        Assert.Equal(93, covered.Count);
        Assert.Equal(HistoricalSwitches95.Except(new[] { "no-config", "stdin-stop" }).OrderBy(s => s, StringComparer.Ordinal),
            covered.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void CommandLine_NoOpSwitchesConsumeTheirValueAndReportNothing()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--stdin-stop", "true", "--no-config", "true", "--port", "31550" });

        Assert.Equal(new[] { "port" }, applied);
        Assert.Equal(31550, config.GameplayPort);
    }

    [Fact]
    public void CommandLine_UnknownSwitchConsumesItsValue_StrayTokenDoesNot()
    {
        var unknownFirst = new ServerConfig();
        Assert.Equal(new[] { "port" }, unknownFirst.ApplyCommandLine(new[] { "--bogus", "--port", "--port", "31550" }));
        Assert.Equal(31550, unknownFirst.GameplayPort);

        var valueIsASwitch = new ServerConfig();
        Assert.Empty(valueIsASwitch.ApplyCommandLine(new[] { "--port", "--bogus" }));
        Assert.Equal(ServerConfig.DefaultGameplayPort, valueIsASwitch.GameplayPort);

        var stray = new ServerConfig();
        Assert.Equal(new[] { "port" }, stray.ApplyCommandLine(new[] { "stray", "--port", "31550" }));
        Assert.Equal(31550, stray.GameplayPort);

        // A bare "--" is an (empty-named, unknown) switch: it consumes the next token like any unknown switch.
        var bareDashes = new ServerConfig();
        Assert.Empty(bareDashes.ApplyCommandLine(new[] { "--", "--port", "31550" }));
        Assert.Equal(ServerConfig.DefaultGameplayPort, bareDashes.GameplayPort);

        // A trailing switch without a value is skipped; empty and null tokens are skipped without consuming anything.
        var trailing = new ServerConfig();
        Assert.Equal(new[] { "port" }, trailing.ApplyCommandLine(new[] { string.Empty, null!, "--port", "31550", "--name" }));
        Assert.Equal(31550, trailing.GameplayPort);

        Assert.Empty(new ServerConfig().ApplyCommandLine(null));
    }

    [Fact]
    public void CommandLine_ReportsRepeatsInArgumentOrder()
    {
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--port", "1", "--name", "n", "--gameplay-port", "2" });

        Assert.Equal(new[] { "port", "name", "port" }, applied);
        Assert.Equal(2, config.GameplayPort);
        Assert.Equal("n", config.ServerName);
    }

    [Fact]
    public void CommandLine_HungerAcceptsTierAndLegacyBoolean()
    {
        static (IReadOnlyList<string> Applied, HungerConsumption Hunger) Apply(string value)
        {
            var config = new ServerConfig();
            config.Rules.HungerConsumption = HungerConsumption.Slow;
            var applied = config.ApplyCommandLine(new[] { "--hunger", value });
            return (applied, config.Rules.HungerConsumption);
        }

        Assert.Equal(HungerConsumption.Fast, Apply("Fast").Hunger);
        Assert.Equal(new[] { "hunger" }, Apply("Fast").Applied);
        Assert.Equal(HungerConsumption.Normal, Apply("true").Hunger);
        Assert.Equal(new[] { "hunger" }, Apply("true").Applied);
        Assert.Equal(HungerConsumption.Off, Apply("false").Hunger);
        Assert.Equal(new[] { "hunger" }, Apply("false").Applied);
        Assert.Empty(Apply("maybe").Applied);
        Assert.Equal(HungerConsumption.Slow, Apply("maybe").Hunger);
    }

    [Fact]
    public void CommandLine_WorldRegimeSwitchesAcceptOnOffWords()
    {
        var regimes = new (string Switch, Func<ServerConfig, bool> Get, Action<ServerConfig, bool> Set)[]
        {
            ("variance", c => c.World.SystemVariance, (c, v) => c.World.SystemVariance = v),
            ("belts", c => c.World.AsteroidBelts, (c, v) => c.World.AsteroidBelts = v),
            ("continents", c => c.World.TerrainContinents, (c, v) => c.World.TerrainContinents = v),
            ("galaxy-growth", c => c.World.GalaxyGrowth, (c, v) => c.World.GalaxyGrowth = v),
        };

        foreach (var (sw, get, set) in regimes)
        {
            foreach (var (value, expected) in new[] { ("off", false), ("on", true), ("False", false), ("TRUE", true), ("OFF", false) })
            {
                var config = new ServerConfig();
                set(config, !expected);
                Assert.Equal(new[] { sw }, config.ApplyCommandLine(new[] { "--" + sw, value }));
                Assert.Equal(expected, get(config));
            }

            foreach (bool start in new[] { false, true })
            {
                var config = new ServerConfig();
                set(config, start);
                Assert.Empty(config.ApplyCommandLine(new[] { "--" + sw, "nope" }));
                Assert.Equal(start, get(config));
            }
        }
    }

    [Fact]
    public void CommandLine_ClampsAndRangeChecksAsBefore()
    {
        var planetsMin = new ServerConfig();
        Assert.Equal(new[] { "planets-min" }, planetsMin.ApplyCommandLine(new[] { "--planets-min", "11" }));
        Assert.Equal(10, planetsMin.World.PlanetsPerSystemMin);
        Assert.True(planetsMin.World.PlanetsPerSystemMax >= 10);

        var planetsMax = new ServerConfig();
        Assert.Equal(new[] { "planets-max" }, planetsMax.ApplyCommandLine(new[] { "--planets-max", "0" }));
        Assert.Equal(1, planetsMax.World.PlanetsPerSystemMax);
        Assert.Equal(1, planetsMax.World.PlanetsPerSystemMin);

        var bothOrders = new ServerConfig();
        bothOrders.ApplyCommandLine(new[] { "--planets-max", "4", "--planets-min", "6" });
        Assert.Equal((6, 6), (bothOrders.World.PlanetsPerSystemMin, bothOrders.World.PlanetsPerSystemMax));

        var systems = new ServerConfig();
        Assert.Equal(new[] { "systems" }, systems.ApplyCommandLine(new[] { "--systems", "99" }));
        Assert.Equal(32, systems.World.StarSystemCount);
        systems.ApplyCommandLine(new[] { "--systems", "-3" });
        Assert.Equal(1, systems.World.StarSystemCount);

        var moons = new ServerConfig();
        Assert.Equal(new[] { "moons-max" }, moons.ApplyCommandLine(new[] { "--moons-max", "9" }));
        Assert.Equal(8, moons.World.MoonsPerPlanetMax);
        moons.ApplyCommandLine(new[] { "--moons-max", "-1" });
        Assert.Equal(0, moons.World.MoonsPerPlanetMax);

        var terrain = new ServerConfig();
        int defaultGeneration = terrain.World.TerrainGeneration;
        Assert.Empty(terrain.ApplyCommandLine(new[] { "--terrain-generation", "-1" }));
        Assert.Empty(terrain.ApplyCommandLine(new[] { "--terrain-generation", (WorldDescription.CurrentTerrainGeneration + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        Assert.Equal(defaultGeneration, terrain.World.TerrainGeneration);
        Assert.Equal(new[] { "terrain-generation" }, terrain.ApplyCommandLine(new[] { "--terrain-generation", WorldDescription.CurrentTerrainGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture) }));

        var catchUp = new ServerConfig();
        Assert.Equal(new[] { "machine-catchup" }, catchUp.ApplyCommandLine(new[] { "--machine-catchup", "100000" }));
        Assert.Equal(24 * 60, catchUp.Rules.MachineCatchUpMinutes);
        int before = catchUp.Rules.MachineCatchUpMinutes;
        Assert.Empty(catchUp.ApplyCommandLine(new[] { "--machine-catchup", "-1" }));
        Assert.Equal(before, catchUp.Rules.MachineCatchUpMinutes);

        var workers = new ServerConfig();
        Assert.Equal(new[] { "chunk-gen-workers" }, workers.ApplyCommandLine(new[] { "--chunk-gen-workers", "99" }));
        Assert.Equal(ServerConfig.ChunkGenWorkersCeiling, workers.ChunkGenWorkers);
        Assert.Empty(workers.ApplyCommandLine(new[] { "--chunk-gen-workers", "-1" }));

        var perTick = new ServerConfig();
        Assert.Empty(perTick.ApplyCommandLine(new[] { "--chunk-stream-per-tick", "0" }));
        Assert.Empty(perTick.ApplyCommandLine(new[] { "--chunk-stream-budget-ms", "-1" }));
        Assert.Empty(perTick.ApplyCommandLine(new[] { "--backup-keep", "0" }));
        Assert.Empty(perTick.ApplyCommandLine(new[] { "--backup-interval-minutes", "-1" }));
        Assert.Equal(new[] { "backup-interval-minutes" }, perTick.ApplyCommandLine(new[] { "--backup-interval-minutes", "0" }));
    }

    [Fact]
    public void CommandLine_EnumSwitchesAcceptAnyNumeralAsBefore()
    {
        // Enum.TryParse takes any integer, also an undefined one; the switches never added an IsDefined check.
        var config = new ServerConfig();
        var applied = config.ApplyCommandLine(new[] { "--story-density", "7", "--hazards", "99", "--flora", "1", "--tutorial", "1" });

        Assert.Equal(new[] { "story-density", "hazards", "flora" }, applied); // --tutorial refuses numbers
        Assert.Equal((StoryDensity)7, config.Rules.StoryDensity);
        Assert.Equal((HazardLevel)99, config.Rules.EnvironmentalHazards);
        Assert.Equal(Frequency.VeryRare, config.World.FloraDensity);
    }

    [Fact]
    public void CommandLine_PlanetTypesAndStructurePacksAlwaysReport()
    {
        var garbage = new ServerConfig();
        Assert.Empty(garbage.World.PlanetTypeFrequencies);
        Assert.Equal(new[] { "planet-types" }, garbage.ApplyCommandLine(new[] { "--planet-types", "garbage" }));
        Assert.Empty(garbage.World.PlanetTypeFrequencies);

        var types = new ServerConfig();
        types.ApplyCommandLine(new[] { "--planet-types", " ocean = Rare ,lava=nope,,ice=Frequent=x,desert=frequent" });
        Assert.Equal(Frequency.Rare, types.World.PlanetTypeFrequencies["ocean"]);
        Assert.Equal(Frequency.Frequent, types.World.PlanetTypeFrequencies["desert"]);
        Assert.False(types.World.PlanetTypeFrequencies.ContainsKey("lava"));
        Assert.False(types.World.PlanetTypeFrequencies.ContainsKey("ice"));

        var emptyPacks = new ServerConfig();
        Assert.Equal(new[] { "structure-packs" }, emptyPacks.ApplyCommandLine(new[] { "--structure-packs", string.Empty }));
        Assert.Empty(emptyPacks.World.EnabledStructurePacks);

        var packs = new ServerConfig();
        Assert.Equal(new[] { "structure-packs" }, packs.ApplyCommandLine(new[] { "--structure-packs", " a , ,b " }));
        Assert.Equal(new[] { "a", "b" }, packs.World.EnabledStructurePacks);

        var story = new ServerConfig();
        Assert.Equal(new[] { "story" }, story.ApplyCommandLine(new[] { "--story", string.Empty }));
        Assert.Equal(string.Empty, story.Rules.StoryId);
    }

    [Fact]
    public void CommandLine_GameModeAndAdminCheatsSetCompanionRules()
    {
        var modeLast = new ServerConfig();
        modeLast.ApplyCommandLine(new[] { "--creative-flight", "true", "--game-mode", "survival" });
        Assert.Equal(GameMode.Survival, modeLast.Rules.GameMode);
        Assert.False(modeLast.Rules.CreativeFlight);

        var flightLast = new ServerConfig();
        flightLast.ApplyCommandLine(new[] { "--game-mode", "survival", "--creative-flight", "true" });
        Assert.True(flightLast.Rules.CreativeFlight);

        var creative = new ServerConfig();
        creative.ApplyCommandLine(new[] { "--game-mode", "creative" });
        Assert.Equal(GameMode.Creative, creative.Rules.GameMode);
        Assert.True(creative.Rules.CreativeFlight);

        var cheats = new ServerConfig();
        Assert.Equal(new[] { "admin-cheats" }, cheats.ApplyCommandLine(new[] { "--admin-cheats", "true" }));
        Assert.True(cheats.Rules.AdminCheats);
        Assert.True(cheats.Rules.AllowCheatsInSurvival);
        cheats.ApplyCommandLine(new[] { "--admin-cheats", "false" });
        Assert.False(cheats.Rules.AdminCheats);
        Assert.False(cheats.Rules.AllowCheatsInSurvival);
    }

    [Fact]
    public void StartPlanet_CommandLineRejectsBlank_EnvironmentOnlyTrims()
    {
        var cli = new ServerConfig();
        Assert.Empty(cli.ApplyCommandLine(new[] { "--start-planet", "  " }));
        Assert.Equal("varied", cli.StartPlanet);
        Assert.Equal(new[] { "start-planet" }, cli.ApplyCommandLine(new[] { "--start-planet", " lava " }));
        Assert.Equal("lava", cli.StartPlanet);

        ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { ["BBS_START_PLANET"] = "  " }, () =>
        {
            var env = new ServerConfig();
            Assert.Contains("BBS_START_PLANET", env.ApplyEnvironment());
            Assert.Equal(string.Empty, env.StartPlanet);
        });
    }

    [Fact]
    public void Environment_AppliesEveryHistoricalVariableInTheHistoricalOrder()
    {
        Assert.Equal(52, HistoricalEnvironmentOrder.Length);
        Assert.Equal(52, HistoricalEnvironmentOrder.Distinct(StringComparer.Ordinal).Count());

        // Sample values: valid for each parser. Any non-empty value counts as applied, so the order assertion is
        // unaffected. The classes that reload the config through AdminService.LoadConfig (AdminServiceTests,
        // AdminContentTests) share this class's collection since #2923, so they never run inside the window in which
        // these are set process-wide.
        var samples = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["BBS_SERVER_NAME"] = "My Server",
            ["BBS_WORLD"] = "world_001",
            ["BBS_PORT"] = "32000",
            ["BBS_ADMIN_PORT"] = "32001",
            ["BBS_MAX_PLAYERS"] = "8",
            ["BBS_PASSWORD"] = "x",
            ["BBS_ADMINS"] = "A, B",
            ["BBS_FLEET_ADMINS"] = "A, B",
            ["BBS_ADMIN_PASSWORD"] = "x",
            ["BBS_ADMIN_BIND"] = "127.0.0.1",
            ["BBS_ENABLE_WEBSOCKET"] = "true",
            ["BBS_WEBSOCKET_BIND"] = "localhost",
            ["BBS_SAVES"] = "saves",
            ["BBS_DATA"] = TestPaths.DataDir(),
            ["BBS_USERCONTENT"] = "usercontent",
            ["BBS_DATABASE_PROVIDER"] = "sqlite",
            ["BBS_POSTGRES_CONNECTION_STRING"] = "x",
            ["BBS_SEED"] = "42",
            ["BBS_START_PLANET"] = "lava",
            ["BBS_TICK_RATE"] = "7",
            ["BBS_VIEW_DISTANCE"] = "7",
            ["BBS_CHUNK_STREAM_PER_TICK"] = "7",
            ["BBS_CHUNK_STREAM_BUDGET_MS"] = "2.5",
            ["BBS_CHUNK_GEN_WORKERS"] = "7",
            ["BBS_BACKUP_INTERVAL_MINUTES"] = "7",
            ["BBS_BACKUP_KEEP"] = "7",
            ["BBS_TICK_TIMING_LOG_SECONDS"] = "2.5",
            ["BBS_FREE_FLIGHT"] = "true",
            ["BBS_SPACE_COMBAT"] = "PvE",
            ["BBS_SHIP_WEAPONS"] = "All",
            ["BBS_SPACE_NPCS"] = "Normal",
            ["BBS_BANDITS"] = "Normal",
            ["BBS_AI_LEVEL"] = "Suggest",
            ["BBS_AI_BACKEND_URL"] = "x",
            ["BBS_AI_TIMEOUT_SECONDS"] = "7",
            ["BBS_AI_LINES_PER_MINUTE"] = "2.5",
            ["BBS_CRASH_REPORT_ENDPOINT"] = "x",
            ["BBS_CRASH_REPORT_KEY"] = "x",
            ["BBS_NOTIFY_URL"] = "x",
            ["BBS_BLOCKED_WORDS"] = "A, B",
            ["BBS_WATCH_WORDS"] = "A, B",
            ["BBS_CHAT_FILTER"] = "Off",
            ["BBS_CHAT_BLOCKED_WORDS"] = "A, B",
            ["BBS_CHAT_MASKED_WORDS"] = "A, B",
            ["BBS_CHAT_WATCH_WORDS"] = "A, B",
            ["BBS_CHAT_ALLOW_WORDS"] = "A, B",
            ["BBS_VOICE"] = "true",
            ["BBS_IDLE_SHUTDOWN_MINUTES"] = "7",
            ["BBS_JOIN_TOKEN_SECRET"] = "x",
            ["BBS_WORLD_OWNER"] = "x",
            ["BBS_NO_PLAYER_ADMINS"] = "1",
            ["BBS_ANNOUNCE_TOKEN"] = "x",
        };
        Assert.Equal(HistoricalEnvironmentOrder.OrderBy(n => n, StringComparer.Ordinal), samples.Keys.OrderBy(n => n, StringComparer.Ordinal));
        foreach (string fallback in HistoricalFallbacks.Values)
        {
            samples[fallback] = null; // unset for the duration: only the canonical names may be read
        }

        ServerConfigTests.WithEnvironment(samples, () =>
        {
            var config = new ServerConfig();
            var applied = config.ApplyEnvironment();

            Assert.Equal(HistoricalEnvironmentOrder, applied);
            Assert.Equal(32000, config.GameplayPort);
            Assert.Equal(new[] { "A", "B" }, config.AdminPlayers);
            Assert.Equal("lava", config.StartPlanet);
            Assert.Equal(2.5, config.TickTimingLogSeconds);
            Assert.Equal(AiLevel.Suggest, config.AiLevel);
            Assert.Equal(ChatFilterLevel.Off, config.ChatFilter);
            Assert.True(config.NoPlayerAdmins);
            Assert.Equal(7, config.IdleShutdownMinutes);
            Assert.Contains("A", config.ChatBlockedWords);   // the word lists EXTEND the defaults ...
            Assert.Contains("hitler", config.ChatBlockedWords); // ... instead of replacing them
            Assert.Contains("B", config.BlockedNameWords);
            Assert.Equal(new[] { "A", "B" }, config.ChatAllowWords);
        });
    }

    [Fact]
    public void Environment_FallbacksCountOnlyWhenTheCanonicalIsUnset()
    {
        var cases = new (string Fallback, string Value, Func<ServerConfig, object> Read, object Expected)[]
        {
            ("BBS_GAMEPLAY_PORT", "31500", c => c.GameplayPort, 31500),
            ("BBS_SERVER_PASSWORD", "pw", c => c.ServerPassword, "pw"),
            // "SQLite", not "postgresql": the parallel AdminServiceTests reload the config and assert the SQLite backend.
            // It still differs from the default "sqlite", so the read-back proves the fallback was applied.
            ("BBS_DATABASE", "SQLite", c => c.DatabaseProvider, "SQLite"),
            ("DATABASE_URL", "Host=db", c => c.PostgresConnectionString, "Host=db"),
        };

        foreach (var (fallback, value, read, expected) in cases)
        {
            string canonical = HistoricalFallbacks.Single(kv => kv.Value == fallback).Key;
            ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { [canonical] = null, [fallback] = value }, () =>
            {
                var config = new ServerConfig();
                var applied = config.ApplyEnvironment();

                Assert.Equal(new[] { canonical }, applied); // reported under the CANONICAL name
                Assert.Equal(expected, read(config));
            });

            // An empty canonical variable counts as unset.
            ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { [canonical] = string.Empty, [fallback] = value }, () =>
            {
                var config = new ServerConfig();
                Assert.Equal(new[] { canonical }, config.ApplyEnvironment());
                Assert.Equal(expected, read(config));
            });
        }

        // The canonical wins when both are set.
        ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { ["BBS_PORT"] = "31600", ["BBS_GAMEPLAY_PORT"] = "31500" }, () =>
        {
            var config = new ServerConfig();
            Assert.Equal(new[] { "BBS_PORT" }, config.ApplyEnvironment());
            Assert.Equal(31600, config.GameplayPort);
        });

        // A set-but-unparseable canonical still hides a valid fallback (it is only consulted while the canonical is unset).
        ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { ["BBS_PORT"] = "notaport", ["BBS_GAMEPLAY_PORT"] = "31500" }, () =>
        {
            var config = new ServerConfig();
            Assert.Empty(config.ApplyEnvironment());
            Assert.Equal(ServerConfig.DefaultGameplayPort, config.GameplayPort);
        });
    }

    [Fact]
    public void Environment_RangeChecksAsBefore()
    {
        var rejected = new Dictionary<string, string>[]
        {
            new() { ["BBS_AI_TIMEOUT_SECONDS"] = "0" },
            new() { ["BBS_AI_LINES_PER_MINUTE"] = "Infinity" },
            new() { ["BBS_AI_LINES_PER_MINUTE"] = "-1" },
            new() { ["BBS_TICK_TIMING_LOG_SECONDS"] = "-1" },
            new() { ["BBS_BACKUP_KEEP"] = "0" },
            new() { ["BBS_CHUNK_STREAM_PER_TICK"] = "0" },
            new() { ["BBS_CHUNK_GEN_WORKERS"] = "-1" },
            new() { ["BBS_BACKUP_INTERVAL_MINUTES"] = "-1" },
            new() { ["BBS_CHUNK_STREAM_BUDGET_MS"] = "-0.5" },
            new() { ["BBS_ENABLE_WEBSOCKET"] = "1" },
            new() { ["BBS_SEED"] = "1.5" },
        };

        foreach (var vars in rejected)
        {
            ServerConfigTests.WithEnvironment(vars.ToDictionary(kv => kv.Key, kv => (string?)kv.Value, StringComparer.Ordinal), () =>
            {
                Assert.Empty(new ServerConfig().ApplyEnvironment());
            });
        }

        ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { ["BBS_IDLE_SHUTDOWN_MINUTES"] = "-5" }, () =>
        {
            var config = new ServerConfig();
            Assert.Equal(new[] { "BBS_IDLE_SHUTDOWN_MINUTES" }, config.ApplyEnvironment()); // no range check
            Assert.Equal(-5, config.IdleShutdownMinutes);
        });

        ServerConfigTests.WithEnvironment(new Dictionary<string, string?> { ["BBS_CHUNK_GEN_WORKERS"] = "99", ["BBS_AI_LINES_PER_MINUTE"] = "0" }, () =>
        {
            var config = new ServerConfig();
            Assert.Equal(new[] { "BBS_CHUNK_GEN_WORKERS", "BBS_AI_LINES_PER_MINUTE" }, config.ApplyEnvironment());
            Assert.Equal(ServerConfig.ChunkGenWorkersCeiling, config.ChunkGenWorkers); // the setter clamps
            Assert.Equal(0.0, config.AiLinesPerMinute);
        });
    }

    // --- The option table itself (ServerConfigOptions.All) ---

    [Fact]
    public void Table_HasExactly105Rows_EachWithANameAndHelp()
    {
        Assert.Equal(105, ServerConfigOptions.All.Count);
        foreach (var option in ServerConfigOptions.All)
        {
            Assert.True(option.CommandLineName is not null || option.EnvironmentName is not null, "a row needs a switch or a variable");
            Assert.False(string.IsNullOrWhiteSpace(option.Help), $"row {option.CommandLineName ?? option.EnvironmentName} has no help line");
            Assert.NotNull(option.Apply);
            Assert.All(option.CommandLineAliases, alias => Assert.False(string.IsNullOrEmpty(alias)));
            Assert.All(option.EnvironmentFallbacks, fallback => Assert.False(string.IsNullOrEmpty(fallback)));
            if (option.CommandLineName is null)
            {
                Assert.Empty(option.CommandLineAliases);
            }

            if (option.EnvironmentName is null)
            {
                Assert.Empty(option.EnvironmentFallbacks);
            }
        }
    }

    [Fact]
    public void Table_SwitchSpellingsAreUniqueAndMatchTheHistoricalSet()
    {
        var spellings = ServerConfigOptions.All
            .Where(option => option.CommandLineName is not null)
            .SelectMany(option => option.CommandLineAliases.Prepend(option.CommandLineName!))
            .ToList();

        Assert.Equal(95, HistoricalSwitches95.Length);
        Assert.Equal(95, spellings.Count);
        Assert.Equal(spellings.Count, spellings.Distinct(StringComparer.Ordinal).Count());
        Assert.All(spellings, name => Assert.Equal(name.ToLowerInvariant(), name));
        Assert.Equal(HistoricalSwitches95.OrderBy(s => s, StringComparer.Ordinal), spellings.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(82, ServerConfigOptions.All.Count(option => option.CommandLineName is not null));
    }

    [Fact]
    public void Table_EnvironmentNamesAreUniqueAndKeepTheHistoricalSequence()
    {
        var rows = ServerConfigOptions.All.Where(option => option.EnvironmentName is not null).ToList();

        Assert.Equal(HistoricalEnvironmentOrder, rows.Select(option => option.EnvironmentName!));
        // The 52 rows carrying a variable come first; the rest are command-line-only.
        Assert.Equal(rows, ServerConfigOptions.All.Take(rows.Count));

        var fallbacks = rows
            .Where(option => option.EnvironmentFallbacks.Count > 0)
            .ToDictionary(option => option.EnvironmentName!, option => string.Join(",", option.EnvironmentFallbacks), StringComparer.Ordinal);
        Assert.Equal(HistoricalFallbacks.OrderBy(kv => kv.Key, StringComparer.Ordinal), fallbacks.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        Assert.DoesNotContain(HistoricalFallbacks.Values, fallback => HistoricalEnvironmentOrder.Contains(fallback, StringComparer.Ordinal));

        // 56 distinct variable names in all: 52 canonical + 4 fallbacks.
        Assert.Equal(56, rows.SelectMany(option => option.EnvironmentFallbacks.Prepend(option.EnvironmentName!)).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Table_NoOpSwitchesNeverReport()
    {
        foreach (string name in new[] { "no-config", "stdin-stop" })
        {
            var option = Assert.Single(ServerConfigOptions.All, row => row.CommandLineName == name);
            var config = new ServerConfig();
            string before = config.ToJson();

            Assert.False(option.Apply(config, "true"));
            Assert.Null(option.EnvironmentName);
            Assert.Equal(before, config.ToJson());
        }
    }
}

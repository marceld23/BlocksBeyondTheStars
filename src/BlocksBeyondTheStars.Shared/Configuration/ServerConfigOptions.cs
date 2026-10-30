// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Configuration;

/// <summary>
/// One server option: a <c>--switch</c>, a <c>BBS_*</c> environment variable, or both, sharing one parser.
/// </summary>
/// <param name="CommandLineName">The canonical switch without <c>--</c> — the name <see cref="ServerConfig.ApplyCommandLine"/>
/// reports when the switch is applied. <c>null</c> = environment-only.</param>
/// <param name="CommandLineAliases">Other accepted spellings of the switch, lower-case, without <c>--</c>; they report the
/// canonical name.</param>
/// <param name="EnvironmentName">The canonical variable — the name <see cref="ServerConfig.ApplyEnvironment"/> reports when
/// the variable is applied. <c>null</c> = command-line-only.</param>
/// <param name="EnvironmentFallbacks">Other variables, consulted in order only while the canonical variable and the earlier
/// fallbacks are unset or empty. A set but unparseable canonical variable is not skipped: its fallbacks stay unread.</param>
/// <param name="Apply">Parses the raw value and sets it on the config; returns <c>true</c> when it was applied (<c>false</c>
/// = ignored, nothing reported).</param>
/// <param name="Help">One English line describing the option.</param>
public sealed record ServerConfigOption(
    string? CommandLineName,
    IReadOnlyList<string> CommandLineAliases,
    string? EnvironmentName,
    IReadOnlyList<string> EnvironmentFallbacks,
    Func<ServerConfig, string, bool> Apply,
    string Help);

/// <summary>
/// Every command-line switch and <c>BBS_*</c> environment variable <see cref="ServerConfig"/> accepts, as one declarative
/// table (<see cref="All"/>), plus the two appliers behind <see cref="ServerConfig.ApplyCommandLine"/> and
/// <see cref="ServerConfig.ApplyEnvironment"/>. Adding an option = adding one row.
/// </summary>
public static class ServerConfigOptions
{
    // --- Parser factories: each turns a typed setter into a row's Apply. Every Apply takes the config as a parameter and
    // captures no config instance, so the static table is safe to share. ---

    /// <summary>A switch that is recognised (its value token is consumed) but applies nothing and reports nothing.</summary>
    private static readonly Func<ServerConfig, string, bool> Ignore = (_, _) => false;

    /// <summary>The raw value, always applied (also an empty one on the command line).</summary>
    private static Func<ServerConfig, string, bool> Text(Action<ServerConfig, string> set)
        => (config, value) =>
        {
            set(config, value);
            return true;
        };

    /// <summary>An integer via the culture-default <c>int.TryParse(value, out …)</c>, optionally range-checked.</summary>
    private static Func<ServerConfig, string, bool> Int(Action<ServerConfig, int> set, Func<int, bool>? accept = null)
        => (config, value) =>
        {
            if (!int.TryParse(value, out var parsed) || (accept is not null && !accept(parsed)))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>A long via the culture-default <c>long.TryParse(value, out …)</c>.</summary>
    private static Func<ServerConfig, string, bool> Long(Action<ServerConfig, long> set)
        => (config, value) =>
        {
            if (!long.TryParse(value, out var parsed))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>A boolean via <c>bool.TryParse</c> (<c>true</c>/<c>false</c>, case-insensitive).</summary>
    private static Func<ServerConfig, string, bool> Bool(Action<ServerConfig, bool> set)
        => (config, value) =>
        {
            if (!bool.TryParse(value, out var parsed))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>A boolean via <c>bool.TryParse</c>, else the words <c>on</c>/<c>off</c> (case-insensitive).</summary>
    private static Func<ServerConfig, string, bool> OnOff(Action<ServerConfig, bool> set)
        => (config, value) =>
        {
            if (bool.TryParse(value, out var parsed))
            {
                set(config, parsed);
                return true;
            }

            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            {
                set(config, true);
                return true;
            }

            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                set(config, false);
                return true;
            }

            return false;
        };

    /// <summary>A shell-style on/off flag via <see cref="ServerConfig.TryParseFlag"/>.</summary>
    private static Func<ServerConfig, string, bool> Flag(Action<ServerConfig, bool> set)
        => (config, value) =>
        {
            if (!ServerConfig.TryParseFlag(value, out var parsed))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>A double via <c>double.TryParse</c> with <see cref="NumberStyles.Float"/> and the invariant culture,
    /// range-checked.</summary>
    private static Func<ServerConfig, string, bool> InvariantDouble(Action<ServerConfig, double> set, Func<double, bool> accept)
        => (config, value) =>
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !accept(parsed))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>An enum value via <c>Enum.TryParse(value, ignoreCase: true, out …)</c>. Deliberately no
    /// <c>Enum.IsDefined</c> check: like the pre-table parser, any integer is accepted, also an undefined one.</summary>
    private static Func<ServerConfig, string, bool> Choice<TEnum>(Action<ServerConfig, TEnum> set)
        where TEnum : struct, Enum
        => (config, value) =>
        {
            if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            {
                return false;
            }

            set(config, parsed);
            return true;
        };

    /// <summary>A comma-separated name list that REPLACES the configured list; always applied.</summary>
    private static Func<ServerConfig, string, bool> Names(Action<ServerConfig, List<string>> set)
        => (config, value) =>
        {
            set(config, SplitNames(value));
            return true;
        };

    /// <summary>A comma-separated name list that EXTENDS the configured list (the defaults stay); always applied.</summary>
    private static Func<ServerConfig, string, bool> ExtendNames(Func<ServerConfig, List<string>> list)
        => (config, value) =>
        {
            list(config).AddRange(SplitNames(value));
            return true;
        };

    /// <summary>Splits a comma-separated name list, trimming entries and dropping empties.</summary>
    private static List<string> SplitNames(string value)
        => value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

    // --- Row factories ---

    private static ServerConfigOption Switch(string name, Func<ServerConfig, string, bool> apply, string help, params string[] aliases)
        => new(name, aliases, null, Array.Empty<string>(), apply, help);

    private static ServerConfigOption Variable(string name, Func<ServerConfig, string, bool> apply, string help, params string[] fallbacks)
        => new(null, Array.Empty<string>(), name, fallbacks, apply, help);

    private static ServerConfigOption Both(
        string commandLineName,
        string environmentName,
        Func<ServerConfig, string, bool> apply,
        string help,
        string[]? aliases = null,
        string[]? fallbacks = null)
        => new(commandLineName, aliases ?? Array.Empty<string>(), environmentName, fallbacks ?? Array.Empty<string>(), apply, help);

    /// <summary>
    /// The option table. The row order IS the order <see cref="ApplyEnvironment"/> applies and reports variables — kept
    /// from the pre-table code, because the server logs that list (<c>Applied environment overrides: …</c>): first the 52
    /// rows carrying a variable, then the command-line-only rows. Command-line lookup is order-independent (one switch per
    /// argument, matched by name).
    /// </summary>
    public static readonly IReadOnlyList<ServerConfigOption> All = new ServerConfigOption[]
    {
        // --- Rows with an environment variable, in the historical ApplyEnvironment order ---
        Both("name", "BBS_SERVER_NAME", Text((c, v) => c.ServerName = v), "Server name shown to players.", aliases: new[] { "server-name" }),
        Both("world", "BBS_WORLD", Text((c, v) => c.WorldName = v), "World (save) name under the saves root.", aliases: new[] { "world-name" }),
        Both("port", "BBS_PORT", Int((c, v) => c.GameplayPort = v), "Gameplay port (UDP; also the WebSocket TCP port).", aliases: new[] { "gameplay-port" }, fallbacks: new[] { "BBS_GAMEPLAY_PORT" }),
        Both("admin-port", "BBS_ADMIN_PORT", Int((c, v) => c.AdminPort = v), "Admin web UI port."),
        Both("max-players", "BBS_MAX_PLAYERS", Int((c, v) => c.MaxPlayers = v), "Maximum number of players online at once."),
        Both("password", "BBS_PASSWORD", Text((c, v) => c.ServerPassword = v), "Join password; empty = open server.", fallbacks: new[] { "BBS_SERVER_PASSWORD" }),
        Both("admins", "BBS_ADMINS", Names((c, v) => c.AdminPlayers = v), "Comma-separated player names granted the Admin role on join (in-game hosting passes the host's name).", aliases: new[] { "admin-players" }),
        Both("fleet-admins", "BBS_FLEET_ADMINS", Names((c, v) => c.FleetAdminPlayers = v), "Comma-separated player names granted fleet admin (observer mode, #487)."),
        Variable("BBS_ADMIN_PASSWORD", Text((c, v) => c.AdminPassword = v), "Admin web UI password."),
        Both("admin-bind", "BBS_ADMIN_BIND", Text((c, v) => c.AdminBindAddress = v), "Admin web UI bind address (loopback by default)."),
        Variable("BBS_ENABLE_WEBSOCKET", Bool((c, v) => c.EnableWebSocket = v), "Also accept browser clients over WebSocket on the gameplay port."),
        Variable("BBS_WEBSOCKET_BIND", Text((c, v) => c.WebSocketBindAddress = v), "WebSocket bind host (\"localhost\", a LAN ip, or \"+\" for all interfaces)."),
        Both("saves", "BBS_SAVES", Text((c, v) => c.SavesRoot = v), "Saves root folder.", aliases: new[] { "saves-root" }),
        Both("data", "BBS_DATA", Text((c, v) => c.DataDir = v), "Game data folder (the data/*.json definitions).", aliases: new[] { "data-dir" }),
        Both("usercontent", "BBS_USERCONTENT", Text((c, v) => c.UserContentDir = v), "Writable folder with in-game-editor structure templates.", aliases: new[] { "user-content" }),
        Both("database-provider", "BBS_DATABASE_PROVIDER", Text((c, v) => c.DatabaseProvider = v), "Persistence backend: \"sqlite\" (default) or \"postgresql\".", aliases: new[] { "database" }, fallbacks: new[] { "BBS_DATABASE" }),
        Both("postgres-connection-string", "BBS_POSTGRES_CONNECTION_STRING", Text((c, v) => c.PostgresConnectionString = v), "PostgreSQL connection string.", aliases: new[] { "postgres", "postgres-connection" }, fallbacks: new[] { "DATABASE_URL" }),
        Both("seed", "BBS_SEED", Long((c, v) => c.Seed = v), "World seed; 0 derives one from the world name."),
        Variable("BBS_START_PLANET", ApplyStartPlanetVariable, "Planet type the world starts on (trimmed; unlike --start-planet, a blank value is applied)."),
        Variable("BBS_TICK_RATE", Int((c, v) => c.TickRate = v), "Simulation tick rate in Hz."),
        Both("view-distance", "BBS_VIEW_DISTANCE", Int((c, v) => c.ViewDistanceChunks = v), "View distance in chunks.", aliases: new[] { "view-distance-chunks" }),
        Both("chunk-stream-per-tick", "BBS_CHUNK_STREAM_PER_TICK", Int((c, v) => c.ChunkStreamPerTick = v, v => v >= 1), "Chunks streamed to each player per tick (>= 1; the setter caps it, #999)."),
        Both("chunk-stream-budget-ms", "BBS_CHUNK_STREAM_BUDGET_MS", InvariantDouble((c, v) => c.ChunkStreamBudgetMs = v, v => v >= 0), "Wall-clock budget for chunk streaming per tick in ms (>= 0; 0 = off).", aliases: new[] { "chunk-budget-ms" }),
        Both("chunk-gen-workers", "BBS_CHUNK_GEN_WORKERS", Int((c, v) => c.ChunkGenWorkers = v, v => v >= 0), "Background chunk-generation threads (>= 0; 0 = inline; the setter caps it, #1817)."),
        Both("backup-interval-minutes", "BBS_BACKUP_INTERVAL_MINUTES", Int((c, v) => c.BackupIntervalMinutes = v, v => v >= 0), "Minutes of play between two rotating backups (>= 0; 0 = off, #2223)."),
        Both("backup-keep", "BBS_BACKUP_KEEP", Int((c, v) => c.BackupKeepCount = v, v => v >= 1), "Rotating backups kept per kind (>= 1; the setter caps it, #2223)."),
        Variable("BBS_TICK_TIMING_LOG_SECONDS", InvariantDouble((c, v) => c.TickTimingLogSeconds = v, v => v >= 0), "Tick-profiling summary interval in seconds (>= 0; 0 = off, #1504)."),
        Both("free-flight", "BBS_FREE_FLIGHT", Bool((c, v) => c.Rules.FreeSpaceFlight = v), "Free space flight."),
        Both("space-combat", "BBS_SPACE_COMBAT", Choice<SpaceCombatMode>((c, v) => c.Rules.SpaceCombat = v), "Space combat mode (Off, PvE, Pvp, Both)."),
        Both("ship-weapons", "BBS_SHIP_WEAPONS", Choice<ShipWeaponMode>((c, v) => c.Rules.ShipWeapons = v), "Ship weapon mode (Off, MiningOnly, NpcsOnly, PvpAllowed, All)."),
        Both("space-npcs", "BBS_SPACE_NPCS", Choice<AlienActivity>((c, v) => c.Rules.SpaceNpcEnemies = v), "Hostile space NPC activity."),
        Both("bandits", "BBS_BANDITS", Choice<AlienActivity>((c, v) => c.Rules.Bandits = v), "Bandit activity (world option)."),
        Variable("BBS_AI_LEVEL", Choice<AiLevel>((c, v) => c.AiLevel = v), "Optional AI backend level (Off keeps the game AI-free)."),
        Variable("BBS_AI_BACKEND_URL", Text((c, v) => c.AiBackendUrl = v), "Base URL of the optional Python AI backend."),
        Variable("BBS_AI_TIMEOUT_SECONDS", Int((c, v) => c.AiTimeoutSeconds = v, v => v > 0), "HTTP timeout for AI-backend calls in seconds (> 0)."),
        Variable("BBS_AI_LINES_PER_MINUTE", InvariantDouble((c, v) => c.AiLinesPerMinute = v, v => v >= 0 && !double.IsInfinity(v)), "AI-personalised NPC/VEGA lines per minute (>= 0 and finite; 0 = none, #2591)."),
        Variable("BBS_CRASH_REPORT_ENDPOINT", Text((c, v) => c.CrashReportEndpoint = v), "Endpoint automatic crash reports are uploaded to."),
        Variable("BBS_CRASH_REPORT_KEY", Text((c, v) => c.CrashReportApiKey = v), "Spam-gate key sent with crash uploads; empty keeps uploading off."),
        Variable("BBS_NOTIFY_URL", Text((c, v) => c.NotifyUrl = v), "Operator push-notification URL (an ntfy topic or a webhook, #938)."),
        Variable("BBS_BLOCKED_WORDS", ExtendNames(c => c.BlockedNameWords), "Comma-separated words added to the name block list."),
        Variable("BBS_WATCH_WORDS", ExtendNames(c => c.WatchNameWords), "Comma-separated words added to the name watch list."),
        Both("chat-filter", "BBS_CHAT_FILTER", Choice<ChatFilterLevel>((c, v) => c.ChatFilter = v), "Operator chat screening level: Off, Mask or Strict (#1207)."),
        Both("festivals", "BBS_FESTIVALS", Text((c, v) => c.FestivalsOverride = v.Trim().ToLowerInvariant()), "Operator festival override (#3001): empty = each world's rule and the calendar decide, off = none on this server, a festival key = that one everywhere. Never saved into a world."),
        Variable("BBS_CHAT_BLOCKED_WORDS", ExtendNames(c => c.ChatBlockedWords), "Comma-separated words added to the chat block list."),
        Variable("BBS_CHAT_MASKED_WORDS", ExtendNames(c => c.ChatMaskedWords), "Comma-separated words added to the chat mask list."),
        Variable("BBS_CHAT_WATCH_WORDS", ExtendNames(c => c.ChatWatchWords), "Comma-separated words added to the chat watch list."),
        Variable("BBS_CHAT_ALLOW_WORDS", ExtendNames(c => c.ChatAllowWords), "Comma-separated words added to the chat allow list."),
        Both("voice", "BBS_VOICE", Bool((c, v) => c.VoiceChatEnabled = v), "Opt-in live voice chat.", aliases: new[] { "voice-chat" }),
        Variable("BBS_IDLE_SHUTDOWN_MINUTES", Int((c, v) => c.IdleShutdownMinutes = v), "Shut down after this many minutes without a joined player (0 = never; not range-checked)."),
        Variable("BBS_JOIN_TOKEN_SECRET", Text((c, v) => c.JoinTokenSecret = v), "HMAC secret for hosted join tokens; empty = joins work as before."),
        Variable("BBS_WORLD_OWNER", Text((c, v) => c.WorldOwnerAccountId = v), "Control-plane account id of the world's owner."),
        Both("no-player-admins", "BBS_NO_PLAYER_ADMINS", Flag((c, v) => c.NoPlayerAdmins = v), "No saved player role grants admin on this instance (#2486; true/false, 1/0, yes/no, on/off)."),
        Variable("BBS_ANNOUNCE_TOKEN", Text((c, v) => c.AnnounceToken = v), "Shared secret for the WebSocket gateway's POST /announce; empty = off."),

        // --- Command-line-only switches ---
        Switch("no-config", Ignore, "Start from in-memory defaults without config/server.json; read from the raw args by ServerConfig.LoadForStartup, recognised here so its value token is consumed."),
        Switch("stdin-stop", Ignore, "Arm the stdin graceful-shutdown watcher (bundled singleplayer host); read from the raw args by Program.cs, recognised here so its value token is consumed."),
        Switch("start-planet", ApplyStartPlanetSwitch, "Planet type the world spawns on (an unknown key falls back to the worldgen default; a blank value is ignored)."),
        Switch("game-mode", ApplyGameMode, "Game mode (Survival, Creative; #662); also sets creative flight to match — a later --creative-flight still wins."),
        Switch("creative-flight", Bool((c, v) => c.Rules.CreativeFlight = v), "Free flight for everyone in this world, independent of the game mode."),
        Switch("admin-cheats", ApplyAdminCheats, "Allow admin cheat commands in every game mode (#642); sets both AdminCheats and AllowCheatsInSurvival."),
        Switch("chat-mode", Choice<ChatMode>((c, v) => c.Rules.ChatMode = v), "World chat mode (Open, Filtered, Safe)."),
        Switch("unlock-all-blueprints", Bool((c, v) => c.CreativeUnlockAllBlueprints = v), "Creative world option: start with every blueprint unlocked."),
        Switch("start-all-ships", Bool((c, v) => c.CreativeStartAllShips = v), "Creative world option: own every ship type from the start."),
        Switch("creative-kit", Bool((c, v) => c.CreativeStarterKit = v), "Creative world option: grant the curated starter kit at first spawn."),
        Switch("guarantee-start-cube", Bool((c, v) => c.GuaranteeStartDataCube = v), "Guarantee one data cube next to the start landing pad (singleplayer launcher)."),
        Switch("creatures", Choice<AlienActivity>((c, v) => c.Rules.CreatureAbundance = v), "Creature abundance (world option)."),
        Switch("planet-enemies", Choice<AlienActivity>((c, v) => c.Rules.PlanetEnemies = v), "Planet enemy activity (world option)."),
        Switch("ufos", Choice<AlienActivity>((c, v) => c.Rules.AlienUfos = v), "Alien UFO activity (world option)."),
        Switch("oxygen", Choice<OxygenConsumption>((c, v) => c.Rules.OxygenConsumption = v), "Oxygen consumption (world option)."),
        Switch("hunger", ApplyHunger, "Hunger tier (Off, Slow, Normal, Fast) or the legacy boolean (true = Normal, false = Off)."),
        Switch("hazards", Choice<HazardLevel>((c, v) => c.Rules.EnvironmentalHazards = v), "Environmental hazard level (world option)."),
        Switch("death-penalty", Choice<DeathPenalty>((c, v) => c.Rules.DeathPenalty = v), "Death penalty (world option)."),
        Switch("keep-inventory", Bool((c, v) => c.Rules.KeepInventoryOnDeath = v), "Keep the inventory on death."),
        Switch("keep-ship", Bool((c, v) => c.Rules.KeepShipOnDeath = v), "Keep the ship on death."),
        Switch("auto-aim", Bool((c, v) => c.Rules.AutoAim = v), "Auto-aim (#693); off = weapons only hit what is under the crosshair."),
        Switch("world-textures", Bool((c, v) => c.Rules.WorldTextures = v), "Allow the world's admins to publish textures for everyone in the save (#1958)."),
        Switch("starter-teleporter", Bool((c, v) => c.Rules.StarterTeleporter = v), "Hand every joining player a suit teleporter (#1056)."),
        Switch("return-to-ship", Bool((c, v) => c.Rules.ReturnToShip = v), "The pause menu's \"Back to my ship\" rescue teleport (#2286; default on)."),
        Switch("friendly-visitors", Bool((c, v) => c.Rules.FriendlyVisitors = v), "A neighbour, a travelling merchant or a lost traveller now and then visits a base (#2602; default on)."),
        Switch("machine-catchup", ApplyMachineCatchUp, "Minutes a world's running machines catch up when it runs again (#2269; >= 0, capped at 24 h; 0 = off)."),
        Switch("story", Text((c, v) => c.Rules.StoryId = v), "Story pack id, \"none\" for sandbox, or \"default\"/empty."),
        Switch("story-density", Choice<StoryDensity>((c, v) => c.Rules.StoryDensity = v), "Story pace (Sparse, Normal, Dense)."),
        Switch("tutorial", ApplyTutorial, "Tutorial policy of a fresh world (#2466): auto, courses, optin or off."),
        Switch("flora", Choice<Frequency>((c, v) => c.World.FloraDensity = v), "Flora density (world option)."),
        Switch("ore", Choice<Frequency>((c, v) => c.World.RareResources = v), "Rare-resource frequency (world option)."),
        Switch("settlements", Choice<Frequency>((c, v) => c.World.Settlements = v), "Settlement frequency (world option)."),
        Switch("planet-wrecks", Choice<Frequency>((c, v) => c.World.PlanetWrecks = v), "Planet wreck frequency (world option)."),
        Switch("vaults", Choice<Frequency>((c, v) => c.World.Vaults = v), "Vault frequency (world option)."),
        Switch("stations", Choice<Frequency>((c, v) => c.World.SpaceStations = v), "Space station frequency (world option)."),
        Switch("wormholes", Choice<Frequency>((c, v) => c.World.Wormholes = v), "How many wormhole pairs the galaxy has (#2242)."),
        Switch("debris-fields", Choice<Frequency>((c, v) => c.World.DebrisFields = v), "How many systems hold a debris field (#2353)."),
        Switch("derelicts", Choice<Frequency>((c, v) => c.World.Derelicts = v), "How often a system holds broken old stations (#2632; Off removes them all)."),
        Switch("exotic", Choice<Frequency>((c, v) => c.World.ExoticWorlds = v), "Exotic world frequency (world option)."),
        Switch("station-templates", Choice<Frequency>((c, v) => c.World.StationTemplateUse = v), "How often stations use hand-designed templates (world option)."),
        Switch("settlement-templates", Choice<Frequency>((c, v) => c.World.SettlementTemplateUse = v), "How often settlements use hand-designed templates (world option)."),
        Switch("structure-packs", ApplyStructurePacks, "Enabled structure-template packs (\"a,b\"); empty = all packs, \"__none__\" = none."),
        Switch("systems", Int((c, v) => c.World.StarSystemCount = Math.Clamp(v, 1, 32)), "Star systems in the galaxy (clamped to 1..32)."),
        Switch("planets-min", ApplyPlanetsMin, "Minimum planets per system (clamped to 1..10; raises the maximum to match)."),
        Switch("planets-max", ApplyPlanetsMax, "Maximum planets per system (clamped to 1..12; lowers the minimum to match)."),
        Switch("moons-max", Int((c, v) => c.World.MoonsPerPlanetMax = Math.Clamp(v, 0, 8)), "Maximum moons per planet (clamped to 0..8, #546)."),
        Switch("variance", OnOff((c, v) => c.World.SystemVariance = v), "System archetype variance (#546; true/false or on/off)."),
        Switch("belts", OnOff((c, v) => c.World.AsteroidBelts = v), "Asteroid-belt layout (#683; true/false or on/off)."),
        Switch("continents", OnOff((c, v) => c.World.TerrainContinents = v), "Continents and real oceans (#704; true/false or on/off)."),
        Switch("terrain-generation", Int((c, v) => c.World.TerrainGeneration = v, v => v >= 0 && v <= WorldDescription.CurrentTerrainGeneration), "Terrain generation a new world is created with (#1644; 0 = classic, up to the current one)."),
        Switch("galaxy-growth", OnOff((c, v) => c.World.GalaxyGrowth = v), "Growing galaxy (#1123; true/false or on/off)."),
        Switch("danger", Choice<Frequency>((c, v) => c.World.Danger = v), "Global hostility multiplier (#547)."),
        Switch("planet-types", ApplyPlanetTypes, "Per-type planet frequencies (\"corrupted=Rare,ocean=Frequent\"); unparseable pairs are skipped."),
    };

    /// <summary>Every accepted switch spelling (canonical names and aliases, lower-case) → its row.</summary>
    private static readonly Dictionary<string, ServerConfigOption> BySwitch = BuildSwitchLookup();

    /// <summary>
    /// Applies <c>--key value</c> pairs onto <paramref name="config"/> and returns the canonical names of the applied
    /// switches, once per occurrence, in argument order. A token is a switch when it starts with <c>--</c> and a value
    /// token follows; the value is always consumed, also for an unknown or no-op switch. Switch names are matched after
    /// <c>ToLowerInvariant()</c>. Other tokens are skipped without consuming anything.
    /// </summary>
    public static IReadOnlyList<string> ApplyCommandLine(ServerConfig config, string[]? args)
    {
        var applied = new List<string>();
        if (args is null)
        {
            return applied;
        }

        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (string.IsNullOrEmpty(key) || !key.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                continue;
            }

            var value = args[++i]; // consume the value
            if (BySwitch.TryGetValue(key.Substring(2).ToLowerInvariant(), out var option) && option.Apply(config, value))
            {
                applied.Add(option.CommandLineName!);
            }
        }

        return applied;
    }

    /// <summary>
    /// Applies the environment variables of <see cref="All"/>, in table order, onto <paramref name="config"/> and returns
    /// the canonical names of the applied ones. Unset or empty variables are skipped; a fallback is read only while the
    /// canonical variable (and every earlier fallback) is unset or empty.
    /// </summary>
    public static IReadOnlyList<string> ApplyEnvironment(ServerConfig config)
    {
        var applied = new List<string>();
        foreach (var option in All)
        {
            if (option.EnvironmentName is null)
            {
                continue;
            }

            var raw = ReadVariable(option.EnvironmentName);
            foreach (var fallback in option.EnvironmentFallbacks)
            {
                if (raw is not null)
                {
                    break;
                }

                raw = ReadVariable(fallback);
            }

            if (raw is not null && option.Apply(config, raw))
            {
                applied.Add(option.EnvironmentName);
            }
        }

        return applied;
    }

    /// <summary>An environment variable's value; unset and empty both read as <c>null</c>.</summary>
    private static string? ReadVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static Dictionary<string, ServerConfigOption> BuildSwitchLookup()
    {
        var lookup = new Dictionary<string, ServerConfigOption>(StringComparer.Ordinal);
        foreach (var option in All)
        {
            if (option.CommandLineName is null)
            {
                continue;
            }

            foreach (var name in option.CommandLineAliases.Prepend(option.CommandLineName))
            {
                if (!lookup.TryAdd(name, option))
                {
                    throw new InvalidOperationException($"Duplicate command-line switch '{name}'");
                }
            }
        }

        return lookup;
    }

    // --- Composite and special-case parsers, transcribed from the pre-table switch ---

    /// <summary><c>BBS_START_PLANET</c>: trimmed and always applied — a blank value yields an empty start planet.</summary>
    private static bool ApplyStartPlanetVariable(ServerConfig config, string value)
    {
        config.StartPlanet = value.Trim();
        return true;
    }

    /// <summary><c>--start-planet</c>: a blank value is ignored, anything else is trimmed.</summary>
    private static bool ApplyStartPlanetSwitch(ServerConfig config, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        config.StartPlanet = value.Trim();
        return true;
    }

    /// <summary><c>--game-mode</c> (issue #662): "Creative" makes crafting free, disables oxygen/hunger and keeps planet
    /// enemies/bandits off (both gate on Survival). Passed by the launcher only at world creation — thereafter the save's
    /// baked RulesOverride carries the mode.</summary>
    private static bool ApplyGameMode(ServerConfig config, string value)
    {
        if (!Enum.TryParse<GameMode>(value, ignoreCase: true, out var mode))
        {
            return false;
        }

        config.Rules.GameMode = mode;
        // A Creative/Sandbox world flies — that is what the mode name promises to anyone arriving from Minecraft. An
        // explicit --creative-flight after this still wins.
        config.Rules.CreativeFlight = mode == GameMode.Creative;
        return true;
    }

    /// <summary><c>--admin-cheats</c>: allow admin cheat commands (/tp, /give, /fly …) in every game mode. Passed by the
    /// bundled singleplayer/host launcher, where the solo player is the WorldAdmin anyway; guests on a hosted world are
    /// still blocked by the admin role. Dedicated servers keep the off default unless the operator opts in (#642).</summary>
    private static bool ApplyAdminCheats(ServerConfig config, string value)
    {
        if (!bool.TryParse(value, out var allow))
        {
            return false;
        }

        config.Rules.AdminCheats = allow;
        config.Rules.AllowCheatsInSurvival = allow;
        return true;
    }

    /// <summary><c>--hunger</c>: the difficulty tier (Off/Slow/Normal/Fast) and, for backward compatibility with existing
    /// configs/saves, the legacy boolean (true → Normal, false → Off).</summary>
    private static bool ApplyHunger(ServerConfig config, string value)
    {
        if (Enum.TryParse<HungerConsumption>(value, ignoreCase: true, out var tier))
        {
            config.Rules.HungerConsumption = tier;
            return true;
        }

        if (bool.TryParse(value, out var legacy))
        {
            config.Rules.HungerConsumption = legacy ? HungerConsumption.Normal : HungerConsumption.Off;
            return true;
        }

        return false;
    }

    /// <summary><c>--machine-catchup</c> (#2269): an invariant-culture integer >= 0, capped at one day.</summary>
    private static bool ApplyMachineCatchUp(ServerConfig config, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 0)
        {
            return false;
        }

        config.Rules.MachineCatchUpMinutes = Math.Min(minutes, 24 * 60);
        return true;
    }

    /// <summary><c>--tutorial</c> (#2466): the tutorial pack's world rule — a fresh world only, like every world option: the
    /// save's baked rules win on later launches.</summary>
    private static bool ApplyTutorial(ServerConfig config, string value)
    {
        if (!ServerConfig.TryParseTutorialPolicy(value, out var policy))
        {
            return false;
        }

        config.Rules.TutorialPolicy = policy;
        return true;
    }

    /// <summary><c>--structure-packs</c>: the enabled structure-template packs ("a,b"); an empty value ⇒ all packs (the
    /// default). The "__none__" sentinel means "no packs" (the picker turned everything off), kept as a single
    /// non-matching entry so no template is ever rolled. Always applied.</summary>
    private static bool ApplyStructurePacks(ServerConfig config, string value)
    {
        var packList = new List<string>();
        foreach (var pack in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = pack.Trim();
            if (trimmed.Length > 0)
            {
                packList.Add(trimmed);
            }
        }

        config.World.EnabledStructurePacks = packList;
        return true;
    }

    /// <summary><c>--planets-min</c>: clamped to 1..10, and the maximum is raised to match.</summary>
    private static bool ApplyPlanetsMin(ServerConfig config, string value)
    {
        if (!int.TryParse(value, out var min))
        {
            return false;
        }

        config.World.PlanetsPerSystemMin = Math.Clamp(min, 1, 10);
        // Keep min ≤ max no matter the arg order: DeterministicRandom.Range silently returns min when max < min, which
        // would bypass the max clamp entirely.
        config.World.PlanetsPerSystemMax = Math.Max(config.World.PlanetsPerSystemMax, config.World.PlanetsPerSystemMin);
        return true;
    }

    /// <summary><c>--planets-max</c>: clamped to 1..12, and the minimum is lowered to match.</summary>
    private static bool ApplyPlanetsMax(ServerConfig config, string value)
    {
        if (!int.TryParse(value, out var max))
        {
            return false;
        }

        config.World.PlanetsPerSystemMax = Math.Clamp(max, 1, 12);
        config.World.PlanetsPerSystemMin = Math.Min(config.World.PlanetsPerSystemMin, config.World.PlanetsPerSystemMax);
        return true;
    }

    /// <summary><c>--planet-types</c>: the advanced per-type page, "corrupted=Rare,ocean=Frequent,..." (unknown keys are
    /// ignored by the universe generator; an empty dict keeps the data-driven spawn weights). Always applied, also when no
    /// pair parsed.</summary>
    private static bool ApplyPlanetTypes(ServerConfig config, string value)
    {
        foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && Enum.TryParse<Frequency>(kv[1].Trim(), ignoreCase: true, out var frequency))
            {
                config.World.PlanetTypeFrequencies[kv[0].Trim()] = frequency;
            }
        }

        return true;
    }
}

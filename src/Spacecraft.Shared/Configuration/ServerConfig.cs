using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spacecraft.Shared.Configuration;

/// <summary>
/// Self-hosting server configuration (technical requirements §14). Loaded from
/// <c>config/server.json</c>; shared by the game server and the admin web UI, and
/// editable through the admin UI.
/// </summary>
public sealed class ServerConfig
{
    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;

    public string ServerName { get; set; } = "Spacecraft Server";
    public string WorldName { get; set; } = "world_001";

    public int GameplayPort { get; set; } = DefaultGameplayPort;
    public int AdminPort { get; set; } = DefaultAdminPort;

    /// <summary>Also accept browser clients over WebSocket (on the gameplay port, TCP).</summary>
    public bool EnableWebSocket { get; set; }

    /// <summary>WebSocket bind host ("localhost"/LAN ip for safety, "+" for all interfaces).</summary>
    public string WebSocketBindAddress { get; set; } = "localhost";

    public int MaxPlayers { get; set; } = 4;
    public string ServerPassword { get; set; } = string.Empty;
    public bool WhitelistEnabled { get; set; }
    public List<string> Whitelist { get; set; } = new();
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>Player names granted the Admin role on join (the world creator becomes WorldAdmin).</summary>
    public List<string> AdminPlayers { get; set; } = new();

    public int AutoSaveIntervalMinutes { get; set; } = 5;
    public int BackupIntervalMinutes { get; set; } = 60;

    public int ViewDistanceChunks { get; set; } = 4;
    public int MaxLoadedChunksPerPlayer { get; set; } = 256;

    public string Difficulty { get; set; } = "normal";
    public bool AllowGuests { get; set; } = true;

    /// <summary>Bind address for the admin UI; defaults to loopback so it is not public (§13.3).</summary>
    public string AdminBindAddress { get; set; } = "127.0.0.1";

    /// <summary>Server simulation tick rate in Hz (10–20 recommended, §7.2).</summary>
    public int TickRate { get; set; } = 15;

    /// <summary>Long world seed; 0 means "derive one from the world name".</summary>
    public long Seed { get; set; }

    /// <summary>Planet type the world starts on.</summary>
    public string StartPlanet { get; set; } = "rocky";

    /// <summary>Authoritative world rules (mode, PvP, hazards, death penalty, cheats, ...).</summary>
    public GameRules Rules { get; set; } = new();

    /// <summary>Universe description used when first creating the world.</summary>
    public Spacecraft.Shared.World.WorldDescription World { get; set; } = new();

    /// <summary>Optional AI mission backend level (Off keeps the game fully AI-free).</summary>
    public AiLevel AiLevel { get; set; } = AiLevel.Off;

    /// <summary>Base URL of the optional Python AI backend (used when <see cref="AiLevel"/> is not Off).</summary>
    public string AiBackendUrl { get; set; } = "http://127.0.0.1:8077";

    // --- Filesystem locations (resolved relative to the server install dir) ---

    public string SavesRoot { get; set; } = "saves";
    public string DataDir { get; set; } = "data";

    /// <summary>Whether to stamp the enterable starter-ship hull at the start landing zone (M23a).</summary>
    public bool PlaceStarterShip { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp a procedural settlement on the start planet's surface (away
    /// from the landing zone) when the planet + seed call for one.
    /// </summary>
    public bool PlaceSettlements { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new ServerConfig();
            fresh.Save(path);
            return fresh;
        }

        return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Options) ?? new ServerConfig();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ServerConfig FromJson(string json)
        => JsonSerializer.Deserialize<ServerConfig>(json, Options) ?? new ServerConfig();

    /// <summary>
    /// Applies <c>--key value</c> command-line overrides onto this config. Used to embed/launch
    /// the server programmatically — e.g. the Unity client's local singleplayer host passes a
    /// port, a private saves dir and the data dir. Unknown keys are ignored; missing trailing
    /// values are safe. Returns the canonical names of the keys that were applied.
    /// </summary>
    public IReadOnlyList<string> ApplyCommandLine(string[]? args)
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
            switch (key.Substring(2).ToLowerInvariant())
            {
                case "port":
                case "gameplay-port":
                    if (int.TryParse(value, out var gp)) { GameplayPort = gp; applied.Add("port"); }
                    break;
                case "admin-port":
                    if (int.TryParse(value, out var ap)) { AdminPort = ap; applied.Add("admin-port"); }
                    break;
                case "saves":
                case "saves-root":
                    SavesRoot = value; applied.Add("saves");
                    break;
                case "data":
                case "data-dir":
                    DataDir = value; applied.Add("data");
                    break;
                case "world":
                case "world-name":
                    WorldName = value; applied.Add("world");
                    break;
                case "name":
                case "server-name":
                    ServerName = value; applied.Add("name");
                    break;
                case "max-players":
                    if (int.TryParse(value, out var mp)) { MaxPlayers = mp; applied.Add("max-players"); }
                    break;
                case "view-distance":
                case "view-distance-chunks":
                    if (int.TryParse(value, out var vd)) { ViewDistanceChunks = vd; applied.Add("view-distance"); }
                    break;
                case "free-flight":
                    if (bool.TryParse(value, out var ff)) { Rules.FreeSpaceFlight = ff; applied.Add("free-flight"); }
                    break;
                case "space-combat":
                    if (Enum.TryParse<SpaceCombatMode>(value, ignoreCase: true, out var sc)) { Rules.SpaceCombat = sc; applied.Add("space-combat"); }
                    break;
                case "space-npcs":
                    if (Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var sn)) { Rules.SpaceNpcEnemies = sn; applied.Add("space-npcs"); }
                    break;
                case "password":
                    ServerPassword = value; applied.Add("password");
                    break;
                case "seed":
                    if (long.TryParse(value, out var sd)) { Seed = sd; applied.Add("seed"); }
                    break;
                case "admin-bind":
                    AdminBindAddress = value; applied.Add("admin-bind");
                    break;
            }
        }

        return applied;
    }
}

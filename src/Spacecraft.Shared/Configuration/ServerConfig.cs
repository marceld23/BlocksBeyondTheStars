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

    // --- Filesystem locations (resolved relative to the server install dir) ---

    public string SavesRoot { get; set; } = "saves";
    public string DataDir { get; set; } = "data";

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
}

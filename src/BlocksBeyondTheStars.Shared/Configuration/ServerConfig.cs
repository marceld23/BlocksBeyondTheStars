using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Configuration;

/// <summary>
/// Self-hosting server configuration (technical requirements §14). Loaded from
/// <c>config/server.json</c>; shared by the game server and the admin web UI, and
/// editable through the admin UI.
/// </summary>
public sealed class ServerConfig
{
    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;

    public string ServerName { get; set; } = "Blocks Beyond the Stars Server";
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
    public BlocksBeyondTheStars.Shared.World.WorldDescription World { get; set; } = new();

    /// <summary>Optional AI mission backend level (Off keeps the game fully AI-free).</summary>
    public AiLevel AiLevel { get; set; } = AiLevel.Off;

    /// <summary>Base URL of the optional Python AI backend (used when <see cref="AiLevel"/> is not Off).</summary>
    public string AiBackendUrl { get; set; } = "http://127.0.0.1:8077";

    // --- Filesystem locations (resolved relative to the server install dir) ---

    public string SavesRoot { get; set; } = "saves";
    public string DataDir { get; set; } = "data";

    /// <summary>Optional writable folder holding in-game-editor structure templates
    /// (<c>station_templates/*.json</c>, <c>settlement_templates/*.json</c>). When set, they are merged
    /// into the template pools at load so player-authored structures appear in new worlds without a
    /// rebuild. Empty ⇒ only the shipped <c>data/</c> pools are used.</summary>
    public string UserContentDir { get; set; } = string.Empty;

    /// <summary>Whether to stamp the enterable starter-ship hull at the start landing zone (M23a).</summary>
    public bool PlaceStarterShip { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp a procedural settlement on the start planet's surface (away
    /// from the landing zone) when the planet + seed call for one.
    /// </summary>
    public bool PlaceSettlements { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp a rare crashed-ship wreck on the start planet's surface (away
    /// from the landing zone). Wrecks are uncommon and left scavengeable (not protected).
    /// </summary>
    public bool PlaceWrecks { get; set; } = true;

    /// <summary>
    /// Whether the server may stamp buried vault ruins ("Welten reicher" W-R3) — 0–2 per world: a surface
    /// pillar ring over a shaft down to a stone chamber with data caches + lootable containers.
    /// </summary>
    public bool PlaceVaults { get; set; } = true;

    /// <summary>
    /// Whether the server may scatter "data cubes" on a body's surface — 0–N per world (some bodies get
    /// none): glowing download terminals that grant the player a small bundled minigame for their personal
    /// arcade collection. Deterministic from the world seed; carry no gameplay effect.
    /// </summary>
    public bool PlaceDataCubes { get; set; } = true;

    /// <summary>
    /// Singleplayer/admin convenience: guarantee one data cube right next to the start world's landing pad, so
    /// a solo player can always reach a minigame near spawn. Set only by the bundled singleplayer launcher;
    /// left off on shared/dedicated servers (where the random scatter applies as normal).
    /// </summary>
    public bool GuaranteeStartDataCube { get; set; }

    // --- Singleplayer "Creative" world options (the player picks these at world creation). They are a
    // head-start sandbox: everything available + a starter set, while survival mechanics stay ON. Default
    // false = the normal "Explorer" experience. Persisted per world in WorldMetadata so they reapply on load. ---

    /// <summary>Start with every blueprint unlocked (re-applied each join; idempotent).</summary>
    public bool CreativeUnlockAllBlueprints { get; set; }

    /// <summary>Own every ship type from the start (re-applied each join; idempotent).</summary>
    public bool CreativeStartAllShips { get; set; }

    /// <summary>Grant a curated kit (all tools + generous stacks of key materials) once, at first spawn.</summary>
    public bool CreativeStarterKit { get; set; }

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
                case "usercontent":
                case "user-content":
                    UserContentDir = value; applied.Add("usercontent");
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
                case "ship-weapons":
                    if (Enum.TryParse<ShipWeaponMode>(value, ignoreCase: true, out var sw)) { Rules.ShipWeapons = sw; applied.Add("ship-weapons"); }
                    break;
                case "space-npcs":
                    if (Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var sn)) { Rules.SpaceNpcEnemies = sn; applied.Add("space-npcs"); }
                    break;
                case "password":
                    ServerPassword = value; applied.Add("password");
                    break;
                case "admins":
                case "admin-players":
                    // Comma-separated player names granted the Admin role on join (in-game hosting
                    // passes the host's name so the host is always admin, even on older saves).
                    AdminPlayers = SplitNames(value); applied.Add("admins");
                    break;
                case "seed":
                    if (long.TryParse(value, out var sd)) { Seed = sd; applied.Add("seed"); }
                    break;
                case "start-planet":
                    // Planet type the world spawns on (an unknown key falls back to the worldgen default).
                    if (!string.IsNullOrWhiteSpace(value)) { StartPlanet = value.Trim(); applied.Add("start-planet"); }
                    break;
                case "admin-bind":
                    AdminBindAddress = value; applied.Add("admin-bind");
                    break;
                case "unlock-all-blueprints":
                    if (bool.TryParse(value, out var uab)) { CreativeUnlockAllBlueprints = uab; applied.Add("unlock-all-blueprints"); }
                    break;
                case "start-all-ships":
                    if (bool.TryParse(value, out var sas)) { CreativeStartAllShips = sas; applied.Add("start-all-ships"); }
                    break;
                case "creative-kit":
                    if (bool.TryParse(value, out var ck)) { CreativeStarterKit = ck; applied.Add("creative-kit"); }
                    break;
                case "guarantee-start-cube":
                    if (bool.TryParse(value, out var gsc)) { GuaranteeStartDataCube = gsc; applied.Add("guarantee-start-cube"); }
                    break;

                // --- World options (creation-time; the server bakes them into the save's metadata) ---
                case "creatures":
                    if (Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var ca)) { Rules.CreatureAbundance = ca; applied.Add("creatures"); }
                    break;
                case "planet-enemies":
                    if (Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var pe)) { Rules.PlanetEnemies = pe; applied.Add("planet-enemies"); }
                    break;
                case "ufos":
                    if (Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var uf)) { Rules.AlienUfos = uf; applied.Add("ufos"); }
                    break;
                case "oxygen":
                    if (Enum.TryParse<OxygenConsumption>(value, ignoreCase: true, out var ox)) { Rules.OxygenConsumption = ox; applied.Add("oxygen"); }
                    break;
                case "hunger":
                    if (bool.TryParse(value, out var hg)) { Rules.Hunger = hg; applied.Add("hunger"); }
                    break;
                case "hazards":
                    if (Enum.TryParse<HazardLevel>(value, ignoreCase: true, out var hz)) { Rules.EnvironmentalHazards = hz; applied.Add("hazards"); }
                    break;
                case "death-penalty":
                    if (Enum.TryParse<DeathPenalty>(value, ignoreCase: true, out var dp)) { Rules.DeathPenalty = dp; applied.Add("death-penalty"); }
                    break;
                case "keep-inventory":
                    if (bool.TryParse(value, out var ki)) { Rules.KeepInventoryOnDeath = ki; applied.Add("keep-inventory"); }
                    break;
                case "keep-ship":
                    if (bool.TryParse(value, out var ks)) { Rules.KeepShipOnDeath = ks; applied.Add("keep-ship"); }
                    break;
                case "story":
                    Rules.StoryId = value; applied.Add("story"); // pack id, "none" for sandbox, or "default"/empty
                    break;
                case "story-density":
                    if (Enum.TryParse<StoryDensity>(value, ignoreCase: true, out var storyDens)) { Rules.StoryDensity = storyDens; applied.Add("story-density"); }
                    break;
                case "flora":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var fl)) { World.FloraDensity = fl; applied.Add("flora"); }
                    break;
                case "ore":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var or)) { World.RareResources = or; applied.Add("ore"); }
                    break;
                case "settlements":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var se)) { World.Settlements = se; applied.Add("settlements"); }
                    break;
                case "planet-wrecks":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var pw)) { World.PlanetWrecks = pw; applied.Add("planet-wrecks"); }
                    break;
                case "vaults":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var va)) { World.Vaults = va; applied.Add("vaults"); }
                    break;
                case "stations":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var sf)) { World.SpaceStations = sf; applied.Add("stations"); }
                    break;
                case "exotic":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var ex)) { World.ExoticWorlds = ex; applied.Add("exotic"); }
                    break;
                case "station-templates":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var st)) { World.StationTemplateUse = st; applied.Add("station-templates"); }
                    break;
                case "settlement-templates":
                    if (Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(value, ignoreCase: true, out var set)) { World.SettlementTemplateUse = set; applied.Add("settlement-templates"); }
                    break;
                case "structure-packs":
                    // The enabled structure-template packs ("a,b"); empty arg ⇒ all packs (the default).
                    // The "__none__" sentinel means "no packs" (the picker turned everything off), which we
                    // keep as a single non-matching entry so no template is ever rolled.
                    var packList = new System.Collections.Generic.List<string>();
                    foreach (var p in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = p.Trim();
                        if (trimmed.Length > 0)
                        {
                            packList.Add(trimmed);
                        }
                    }

                    World.EnabledStructurePacks = packList;
                    applied.Add("structure-packs");
                    break;
                case "systems":
                    if (int.TryParse(value, out var sy)) { World.StarSystemCount = Math.Clamp(sy, 1, 32); applied.Add("systems"); }
                    break;
                case "planets-min":
                    if (int.TryParse(value, out var pmin)) { World.PlanetsPerSystemMin = Math.Clamp(pmin, 1, 10); applied.Add("planets-min"); }
                    break;
                case "planets-max":
                    if (int.TryParse(value, out var pmax)) { World.PlanetsPerSystemMax = Math.Clamp(pmax, 1, 12); applied.Add("planets-max"); }
                    break;
                case "moons-max":
                    if (int.TryParse(value, out var mm)) { World.MoonsPerPlanetMax = Math.Clamp(mm, 0, 5); applied.Add("moons-max"); }
                    break;
                case "planet-types":
                    // Advanced per-type page: "corrupted=Rare,ocean=Frequent,..." (unknown keys are ignored
                    // by the universe generator; an empty dict keeps the data-driven spawn weights).
                    foreach (var pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = pair.Split('=', 2);
                        if (kv.Length == 2 && Enum.TryParse<BlocksBeyondTheStars.Shared.World.Frequency>(kv[1].Trim(), ignoreCase: true, out var tf))
                        {
                            World.PlanetTypeFrequencies[kv[0].Trim()] = tf;
                        }
                    }

                    applied.Add("planet-types");
                    break;
            }
        }

        return applied;
    }

    /// <summary>Splits a comma-separated name list, trimming entries and dropping empties.</summary>
    private static List<string> SplitNames(string value)
        => value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
}

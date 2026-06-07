using System.Collections.Generic;
using Spacecraft.Persistence;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// One loaded voxel world for a celestial body: the <see cref="ServerWorld"/>, the ids that identify it,
/// and its per-world runtime state (fauna, enemies, NPCs, flora, fluids, containers, stamped structures,
/// landing zones). GameServer reaches this state through forwarding properties pointing at the active
/// world, so several bodies can be resident at once (one per occupied location) with isolated content.
/// </summary>
internal sealed class LoadedWorld
{
    public required ServerWorld World { get; init; }
    public required string LocationId { get; init; }
    public required string PlanetType { get; init; }

    // Per-world runtime state (was scattered across the GameServer partials).
    public List<CombatEntity> Creatures { get; } = new();
    public List<CombatEntity> PlanetEnemies { get; } = new();
    public List<GameServer.ServerNpc> Npcs { get; } = new();
    public List<GameServer.ServerDoor> Doors { get; } = new();
    public List<(string Type, Vector3f Pos)> SettlementMarkers { get; } = new();
    public HashSet<string> SettlementMissionIds { get; } = new();
    public List<(string Type, Vector3f Pos)> WreckMarkers { get; } = new();
    public Dictionary<Vector3i, (ushort FloraId, double Timer)> FloraRegrow { get; } = new();
    public Dictionary<Vector3i, byte> FluidLevel { get; } = new();
    public HashSet<Vector3i> ActiveFluid { get; } = new();
    public Dictionary<Vector3i, float> FireTimer { get; } = new(); // burning cells → remaining burn time (item 30)
    public HashSet<Vector3i> ActiveFire { get; } = new();
    public Dictionary<string, LandingZone> LandingZones { get; } = new();
    public List<StoredContainer> Containers { get; } = new();

    // Per-player ship structures stamped into THIS world (one per player present, each at their own
    // landing zone). Block protection + interior checks cover every player's ship; respawn/stations use
    // the player's own. Keyed by player id.
    public Dictionary<string, ShipStamp> ShipStamps { get; } = new();

    /// <summary>The ship-stamp record for a player in this world, created empty on first access.</summary>
    public ShipStamp StampFor(string playerId)
    {
        if (!ShipStamps.TryGetValue(playerId, out var stamp))
        {
            stamp = new ShipStamp();
            ShipStamps[playerId] = stamp;
        }

        return stamp;
    }

    // Settlement stamp state.
    public bool SettlementStamped { get; set; }
    public Vector3i SettlementMin { get; set; }
    public Vector3i SettlementMax { get; set; }
    public bool SettlementRuined { get; set; }
    public string SettlementName { get; set; } = string.Empty;
    public string SettlementInhabitant { get; set; } = string.Empty;

    // Wreck stamp state.
    public bool WreckStamped { get; set; }
    public Vector3i WreckOrigin { get; set; }
    public WreckStructure? Wreck { get; set; }
    public string WreckName { get; set; } = string.Empty;
    public bool WreckClaimed { get; set; }

    // Per-world simulation timers/counters (so each resident world ticks independently). Weather + time
    // stay global for now (all resident worlds share the sky — a temporary limitation, refined in P7).
    public double CreatureSpawnTimer { get; set; }
    public double CreatureClock { get; set; }
    public double CreatureBroadcastTimer { get; set; }
    public int CreatureSpawnRotor { get; set; }
    public double EnemySpawnTimer { get; set; }
    public double SinceFluid { get; set; }
    public double SinceFire { get; set; }
    public double NpcBroadcastTimer { get; set; }
    public int NextNpcId { get; set; } = 1;
    public int NextDoorId { get; set; } = 1;

    // Per-world weather/environment (TickWeather + BroadcastEnvironment are per-planet: day length, storm
    // chance, atmosphere/oxygen, clouds, weather RNG). time-of-day fraction included; admin time/weather
    // strings stay global on GameServer for now.
    public double DayFraction { get; set; } = 0.35;
    public double DayLength { get; set; } = 600.0;
    public double StormChance { get; set; } = 0.35;
    public string PlanetWeatherMode { get; set; } = "dynamic";
    public string WeatherState { get; set; } = "clear";
    public float WeatherIntensity { get; set; }
    public double WeatherTimer { get; set; }
    public double SinceEnvBroadcast { get; set; }
    public int SunColor { get; set; } = 0xFFF6E8;
    public int CloudColor { get; set; } = 0xEDEFF2;
    public int FloraTint { get; set; } = 0xFFFFFF;
    public float CloudDensity { get; set; } = 0.45f;
    public bool Breathable { get; set; }
    public bool SpaceSky { get; set; }
    public string Biome { get; set; } = "rock";
    public double OxygenExtractability { get; set; }
    public double AtmosphereHeight { get; set; }
    public System.Random EnvRng { get; set; } = new(1);
}

/// <summary>One player's ship stamped into a world (its hull anchor + size, heal-tank, station markers and
/// protected blocks). Each player gets their own at their own landing zone, so two players on one planet
/// never share a start point or ship.</summary>
internal sealed class ShipStamp
{
    public bool Stamped { get; set; }
    public bool IsLayout { get; set; }           // stamped from a designed voxel layout (irregular shape)
    public Vector3i Anchor { get; set; }          // hull floor-centre block
    public int HalfX { get; set; } = 2;           // hull half-extents (from the ship design)
    public int Height { get; set; } = 4;
    public int HalfZ { get; set; } = 3;
    public Vector3f HealTank { get; set; }        // medbay respawn point
    public HashSet<Vector3i> Extra { get; } = new(); // exterior/silhouette + layout cells (protected)
    public List<(string Type, Vector3f Pos)> Stations { get; } = new();
    public List<Vector3f> Doors { get; } = new(); // designed-ship doorway markers (sci-fi slide doors)
}

/// <summary>
/// Owns the voxel world(s) the server has resident. Today it tracks a single <see cref="Active"/> world and
/// (re)builds it on activation — behaviour-identical to the previous single-<c>_world</c> field — but it is
/// the seam the multi-world work grows from: a dictionary of loaded worlds keyed by location, loaded on
/// demand and unloaded when empty, with each player tracking which one they're in.
/// </summary>
internal sealed class WorldManager
{
    private readonly GameContent _content;
    private readonly WorldGenerator _generator;
    private readonly IWorldRepository _repo;

    public WorldManager(GameContent content, WorldGenerator generator, IWorldRepository repo)
    {
        _content = content;
        _generator = generator;
        _repo = repo;
    }

    private readonly Dictionary<string, LoadedWorld> _loaded = new();

    /// <summary>The world the server is currently operating on (the "Active cursor"). In the multi-world
    /// tick it is set to each occupied world in turn, and to a player's world before handling their
    /// messages. Settable within the assembly so the tick can move the cursor.</summary>
    public LoadedWorld Active { get; set; } = null!;

    /// <summary>All currently resident worlds (one per occupied body once multi-world is on).</summary>
    public IEnumerable<LoadedWorld> Loaded => _loaded.Values;

    /// <summary>True if a world for this body is resident in memory.</summary>
    public bool IsLoaded(string locationId) => _loaded.ContainsKey(locationId);

    /// <summary>The resident world for a body without changing the Active cursor, or null if not loaded.</summary>
    public LoadedWorld? Find(string locationId) => _loaded.TryGetValue(locationId, out var w) ? w : null;

    /// <summary>How many worlds are currently resident.</summary>
    public int Count => _loaded.Count;

    /// <summary>(Re)builds the world for a body and makes it active, NOT caching it — the single-world
    /// path (Start + travel today). Multi-world uses <see cref="GetOrCreate"/> instead.</summary>
    public LoadedWorld Activate(PlanetType planet, string locationId, int circumference)
    {
        Active = new LoadedWorld
        {
            World = new ServerWorld(_content, _generator, _repo, planet, locationId, circumference),
            LocationId = locationId,
            PlanetType = planet.Key,
        };
        return Active;
    }

    /// <summary>Returns the resident world for a body, creating (and caching) a fresh one if absent. Sets
    /// it active. <paramref name="isNew"/> tells the caller to run the one-time world init/stamp.</summary>
    public LoadedWorld GetOrCreate(PlanetType planet, string locationId, int circumference, out bool isNew)
    {
        if (_loaded.TryGetValue(locationId, out var w))
        {
            isNew = false;
            Active = w;
            return w;
        }

        w = new LoadedWorld
        {
            World = new ServerWorld(_content, _generator, _repo, planet, locationId, circumference),
            LocationId = locationId,
            PlanetType = planet.Key,
        };
        _loaded[locationId] = w;
        isNew = true;
        Active = w;
        return w;
    }

    /// <summary>Points the Active cursor at an already-resident world. Returns false if it isn't loaded.</summary>
    public bool SetActive(string locationId)
    {
        if (_loaded.TryGetValue(locationId, out var w))
        {
            Active = w;
            return true;
        }

        return false;
    }

    /// <summary>Drops a resident world from memory (its chunk edits are already persisted by the repo).
    /// No-op if it isn't loaded or is still the active cursor.</summary>
    public void Unload(string locationId)
    {
        if (_loaded.TryGetValue(locationId, out var w) && !ReferenceEquals(w, Active))
        {
            _loaded.Remove(locationId);
        }
    }
}

using System.Collections.Generic;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

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
    public List<GameServer.ServerDataCube> DataCubes { get; } = new(); // minigame download cubes scattered on the surface
    public List<GameServer.ServerBeacon> Beacons { get; } = new(); // placed radio beacons (item 37)
    public List<GameServer.ServerBeam> Beams { get; } = new(); // placed beam blocks (teleporter pads)
    public List<GameServer.ServerSpeeder> Speeders { get; } = new(); // deployed hover speeders (materialised per present owner)
    public List<GameServer.ServerNetFragment> NetFragments { get; } = new(); // story net fragments scattered on the surface (P2)
    public List<(string Type, Vector3f Pos)> SettlementMarkers { get; } = new();
    public HashSet<string> SettlementMissionIds { get; } = new();
    public List<(string Type, Vector3f Pos)> WreckMarkers { get; } = new();
    public Dictionary<Vector3i, (ushort FloraId, double Timer)> FloraRegrow { get; } = new();
    public Dictionary<Vector3i, byte> FluidLevel { get; } = new();
    public HashSet<Vector3i> ActiveFluid { get; } = new();
    public HashSet<Vector3i> FallingFluid { get; } = new(); // flowing cells filled from above (feed a waterfall)
    public Dictionary<Vector3i, float> FireTimer { get; } = new(); // burning cells → remaining burn time (item 30)
    public HashSet<Vector3i> ActiveFire { get; } = new();
    public List<GameServer.LandingPad> LandingPads { get; } = new(); // fixed, map-planned landing pads (item 38)
    public List<StoredContainer> Containers { get; } = new();

    // Per-player ships PARKED on THIS world as placed structure objects (one per player present, each at
    // their own landing pad). Interior/keep-out checks cover every player's ship; respawn/stations use
    // the player's own. Keyed by player id.
    public Dictionary<string, LandedShip> LandedShips { get; } = new();

    /// <summary>The landed-ship record for a player in this world, created empty on first access.</summary>
    public LandedShip LandedFor(string playerId)
    {
        if (!LandedShips.TryGetValue(playerId, out var ship))
        {
            ship = new LandedShip();
            LandedShips[playerId] = ship;
        }

        return ship;
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

    // Buried-vault stamp state ("Welten reicher" W-R3): surface entrances of this world's vault ruins.
    public bool VaultsStamped { get; set; }
    public List<Vector3i> VaultEntrances { get; } = new();

    // Finale (P6 Stage 2): the buried Guardian-core chamber on the guardian_finale-core body — stamped once,
    // its terminal centre is where the breach hack is gated (reach it by digging or down the aperture shaft).
    public bool GuardianCoreStamped { get; set; }
    public Vector3i CoreChamberCenter { get; set; }
    public bool HasCoreChamber { get; set; }

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
    public int NextDataCubeId { get; set; } = 1;
    public int NextBeaconId { get; set; } = 1;
    public int NextBeamId { get; set; } = 1;
    public int NextNetFragmentId { get; set; } = 1;

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
    public int SkyColor { get; set; } = 0x8CBFF2;
    public int FloraTint { get; set; } = 0xFFFFFF;
    public float CloudDensity { get; set; } = 0.45f;
    public bool Breathable { get; set; }
    public bool SpaceSky { get; set; }
    public string Biome { get; set; } = "rock";
    public double OxygenExtractability { get; set; }
    public double AtmosphereHeight { get; set; }
    public double AtmosphereDensity { get; set; }
    public System.Random EnvRng { get; set; } = new(1);
}

/// <summary>One player's ship PARKED on a world as a placed voxel structure OBJECT (ship-as-object — the
/// hull is no longer stamped into the world grid). Each player gets their own at their own landing pad, so
/// two players on one planet never share a start point or ship. Gameplay anchors (heal-tank, stations,
/// doors) are pre-resolved to world coordinates from the structure-local cells + the origin.</summary>
internal sealed class LandedShip
{
    public bool Placed { get; set; }
    public Vector3i Origin { get; set; }              // world cell of the structure-local origin (0,0,0)
    public SpaceStructure Structure { get; set; } = new();
    public Vector3f HealTank { get; set; }            // medbay respawn point (world coords)
    public List<(string Type, Vector3f Pos)> Stations { get; } = new(); // world coords
    public List<Vector3f> Doors { get; } = new();     // doorway base centres (world coords)

    /// <summary>Maps a world cell into the structure-local grid (longitude wrap-aware on X).</summary>
    public Vector3i ToLocal(Vector3i world, int circumference)
        => new(WorldConstants.WrapDeltaX(world.X - Origin.X, circumference), world.Y - Origin.Y, world.Z - Origin.Z);
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

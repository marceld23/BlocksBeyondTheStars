// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The authoritative game server: it owns the world, players and ship, validates every
/// client intent and broadcasts the resulting state. The client never decides outcomes
/// (technical requirements §7, §15). Drive it by calling <see cref="Tick"/> at the
/// configured rate, or use <see cref="Run"/> for a blocking loop.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Process-lifetime singleton; its ManualResetEventSlim is released when the host process exits. Making it IDisposable would cascade CA2213 onto the long-lived owned services (transport, persistence) that the host tears down explicitly.")]
public sealed partial class GameServer
{
    private const string ShipId = "default";
    private const float MaxReach = 8f;
    private const int HotbarSlots = 9;
    private const int MaxPlayerNameLength = 24; // client-supplied names are capped to this on join

    // Vertical build band: client-driven block edits and chunk streaming are clamped to this Y range so a
    // spoofed position can't make the server generate/persist chunks at arbitrary heights — otherwise a cheat
    // client placing/mining at ever-increasing Y grows RAM + disk without bound (DoS). The band is far wider
    // than any legitimate build: terrain sits near Y≈64, the highest planet atmosphere line is ~320 (above
    // which a player floats in space on foot), so towers to space and deep mines stay well inside it.
    private const int MinBuildY = -512;
    private const int MaxBuildY = 1024;

    /// <summary>True when a client-supplied block Y is inside the legal vertical build band (see MinBuildY).</summary>
    private static bool WithinBuildHeight(int y) => y >= MinBuildY && y <= MaxBuildY;

    private readonly ServerConfig _config;
    private readonly GameContent _content;
    private readonly IServerTransport _transport;
    private readonly IWorldRepository _repo;
    private readonly IGameLogger _log;
    private readonly IAiMissionProvider _ai;

    private readonly Dictionary<int, PlayerSession> _sessions = new();

    // Synthetic connection ids for local (non-networked) sessions count down from -1 so they
    // never collide with transport-assigned ids (which are positive).
    private int _nextLocalConnectionId = -1;

    private WorldMetadata _meta = new();
    private WorldGenerator _generator = null!;
    private WorldManager _worlds = null!;
    private Galaxy _galaxy = new();

    /// <summary>The session the server is currently serving (the "ship cursor"). Per-player ship state
    /// (`_ship`/`_ships`/`_activeShipId`) resolves through this, set before each player's messages + ticks
    /// (mirrors the world Active cursor; single-threaded). Falls back to the first joined player.</summary>
    private PlayerSession? _current;

    /// <summary>Empty placeholder returned when no player is being served (avoids null ship access).</summary>
    private readonly ShipState _noShip = new();

    private PlayerSession? CurrentOrFirst()
    {
        if (_current is { Joined: true })
        {
            return _current;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                return s;
            }
        }

        return _current;
    }

    /// <summary>The active ship of the player currently being served (the ship cursor).</summary>
    private ShipState _ship
    {
        get
        {
            var s = CurrentOrFirst();
            return s != null && s.Ships.TryGetValue(s.ActiveShipId, out var ship) ? ship : _noShip;
        }
    }

    /// <summary>Points the ship cursor at a session, refreshing the derived combat stats for its ship.</summary>
    private void SetCurrent(PlayerSession session)
    {
        if (!ReferenceEquals(_current, session))
        {
            _current = session;
            RecomputeShipCombatStats();
        }
    }

    /// <summary>Points BOTH cursors (world + ship) at a player before serving them — used by the public
    /// test/util entry methods that bypass the OnPayload dispatch (which already does this).</summary>
    private void Serve(PlayerSession session)
    {
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);
    }

    /// <summary>The active voxel world. Routed through <see cref="WorldManager"/> so multi-world can hold
    /// several resident worlds; today there is exactly one active world (behaviour unchanged).</summary>
    private ServerWorld _world => _worlds.Active.World;

    private double _sinceAutoSave;
    // Fractional playtime carry: whole seconds are flushed into _meta.CumulativePlaytimeSeconds, the
    // sub-second remainder lives here between ticks. Only advanced while a player is joined.
    private double _playtimeCarry;
    private volatile bool _running;
    // True while the Run() loop owns the tick thread. Lets Stop() (possibly called from another thread, e.g. a
    // Ctrl-C handler) hand the save off to the run loop instead of saving concurrently with a live Tick().
    private volatile bool _runLoopActive;
    private readonly System.Threading.ManualResetEventSlim _stopped = new(true);
    private string _timeOfDay = "day";
    private string _weather = "clear";

    public GameServer(
        ServerConfig config,
        GameContent content,
        IServerTransport transport,
        IWorldRepository repo,
        IGameLogger? logger = null,
        IAiMissionProvider? aiProvider = null)
    {
        _config = config;
        _content = content;
        _transport = transport;
        _repo = repo;
        _log = logger ?? new NullGameLogger();
        _ai = aiProvider
              ?? (config.AiLevel != AiLevel.Off ? new HttpAiMissionProvider(config.AiBackendUrl) : new NullAiMissionProvider());
    }

    private GameRules Rules => _config.Rules;

    public ServerWorld World => _world;
    public ShipState Ship => _ship;
    public Galaxy Galaxy => _galaxy;
    /// <summary>The location of the active cursor world (the world being operated on). With one player/
    /// world this is simply where they are; <c>_meta.ActiveLocationId</c> remains the default join body.</summary>
    public string ActiveLocationId => _worlds.Active?.LocationId ?? _meta.ActiveLocationId;
    public IReadOnlyDictionary<int, PlayerSession> Sessions => _sessions;
    public WorldMetadata Metadata => _meta;

    /// <summary>Number of worlds currently resident in memory (test/inspection — one per occupied body).</summary>
    public int ResidentWorldCount => _worlds.Count;

    /// <summary>The resident voxel world for a body without moving the active cursor, or null (test/inspection).</summary>
    public ServerWorld? WorldAt(string locationId) => _worlds.Find(locationId)?.World;

    // ---------------- Lifecycle ----------------

    public void Start()
    {
        _repo.Initialize();

        _meta = _repo.LoadMetadata() ?? CreateInitialMetadata();

        // World options: once created, the WORLD owns its rules — the save's override replaces the launch
        // config's rules (singleplayer passes creation options only once; dedicated restarts keep the set).
        // Saves from before world options existed have no override and keep using the config's rules.
        if (_meta.RulesOverride is not null)
        {
            _config.Rules = _meta.RulesOverride;
        }

        _repo.SaveMetadata(_meta);

        _generator = new WorldGenerator(_meta.Seed, _content);
        // World options: flora/ore factors are part of the save's description — set BEFORE any chunk
        // generates so worldgen stays deterministic across reloads.
        _generator.SetWorldOptionFactors(
            _meta.Description.FloraDensity.FloraFactor(),
            _meta.Description.RareResources.OreFactor());
        _worlds = new WorldManager(_content, _generator, _repo);
        BuildGalaxy(); // resolves _meta.ActiveLocationId to a concrete celestial body id
        LoadPlayerStations(); // item 20 S4: restore persisted player stations onto the star map + registry
        LoadAllBases();       // restore player-founded planet bases (Grundstein) server-wide for the travel screen
        LoadAllAlliances();   // restore the player alliance graph server-wide (shared station/base access)
        LoadStoryState();     // restore the per-save story progress + active story pack (server-wide, P0)

        // Ships are per-player now: each player loads/creates their own on join (no global ship at start).
        BuildMissions();

        // Builds the active world for the start body plus all its per-world state (weather, fauna,
        // flora, fluids, landing zones, containers, stamped ship/settlement/wreck). Reused by travel.
        SwitchActiveWorld(_meta.DefaultPlanetType, _meta.ActiveLocationId);

        // Persist any newly generated structure-loot guard keys so caches don't respawn on reload.
        _repo.SaveMetadata(_meta);

        _transport.ClientConnected += OnClientConnected;
        _transport.ClientDisconnected += OnClientDisconnected;
        _transport.PayloadReceived += OnPayload;
        _transport.Start(_config.GameplayPort);

        _log.Info($"Server '{_config.ServerName}' started on port {_config.GameplayPort}, world '{_meta.WorldName}' (seed {_meta.Seed}, planet {_meta.DefaultPlanetType}).");
    }

    private WorldMetadata CreateInitialMetadata()
    {
        long seed = _config.Seed != 0 ? _config.Seed : WorldGenerator.StableHash(_config.WorldName);
        return new WorldMetadata
        {
            WorldName = _config.WorldName,
            Seed = seed,
            DefaultPlanetType = _config.StartPlanet,
            ActiveLocationId = _config.StartPlanet,
            Description = _config.World,
            // Bake the chosen singleplayer "Creative" world options into the save so they persist + reapply.
            CreativeUnlockAllBlueprints = _config.CreativeUnlockAllBlueprints,
            CreativeStartAllShips = _config.CreativeStartAllShips,
            CreativeStarterKit = _config.CreativeStarterKit,
            // World options: the rules chosen at creation become the world's own (live admin edits update them).
            RulesOverride = _config.Rules.Clone(),
        };
    }

    /// <summary>
    /// Builds the deterministic galaxy from the seed + world description, applies persisted
    /// generation status, and marks the start location as visited.
    /// </summary>
    private void BuildGalaxy()
    {
        _galaxy = new UniverseGenerator(_meta.Seed, _meta.Description, _content).Generate();

        var stored = _repo.LoadLocationStatuses();
        foreach (var body in _galaxy.AllBodies())
        {
            if (stored.TryGetValue(body.Id, out var s) && Enum.TryParse<GenerationStatus>(s, out var status))
            {
                body.Status = status;
            }
        }

        // Choose a start body: first planet matching the configured start planet type, else any planet.
        CelestialBody? start = null;
        foreach (var body in _galaxy.AllBodies())
        {
            if (body.Kind == CelestialKind.Planet)
            {
                start ??= body;
                if (body.PlanetType == _meta.DefaultPlanetType)
                {
                    start = body;
                    break;
                }
            }
        }

        if (start is not null)
        {
            _meta.ActiveLocationId = start.Id;
            if (start.Status != GenerationStatus.Visited)
            {
                start.Status = GenerationStatus.Visited;
                _repo.SetLocationStatus(start.Id, start.Status.ToString());
            }
        }

        // Finale (P6): the galaxy is regenerated from seed each start, so re-append the Guardian system for an
        // already-revealed save (after start-body selection, so it never affects the spawn world). A fresh
        // reveal adds it live via RevealGuardianSystemIfReady.
        if (_storyState.GuardianSystemRevealed)
        {
            EnsureGuardianSystemInGalaxy();
        }
    }

    /// <summary>
    /// Makes <paramref name="locationId"/> (a celestial body of type <paramref name="planetTypeKey"/>)
    /// the active world: rebuilds <see cref="_world"/> (its edits load from that body's persistence key),
    /// resets + re-initialises all per-world runtime state (weather, fauna, flora, fluids, landing zones,
    /// containers) and re-stamps the ship/settlement/wreck. Used at startup and on travel.
    /// </summary>
    /// <summary>Sets the world-wide default/active body (used by new joins + the star map) and ensures its
    /// world is resident. Called once at startup.</summary>
    private void SwitchActiveWorld(string planetTypeKey, string locationId)
    {
        _meta.DefaultPlanetType = planetTypeKey;
        _meta.ActiveLocationId = locationId;
        if (_ship is not null)
        {
            _ship.CurrentLocationId = locationId;
        }

        LoadWorld(planetTypeKey, locationId);
        _repo.SaveMetadata(_meta);
    }

    /// <summary>Ensures the world for a body is resident (creating + initialising it the first time) and
    /// makes it the active cursor. Cached: a revisited world keeps its in-memory state. Returns the world.</summary>
    private LoadedWorld LoadWorld(string planetTypeKey, string locationId)
    {
        var planet = _content.GetPlanet(planetTypeKey)
                     ?? throw new InvalidOperationException($"Unknown planet type '{planetTypeKey}'.");

        // The walkable circumference varies by body: asteroids are tiny, moons small, planets large
        // (deterministic from the body id + its size class), and the noise/wrap/chunk keys all use it.
        var worldBody = _galaxy?.FindBody(locationId);
        var sizeClass = WorldConstants.SizeClassFor(worldBody?.Kind ?? CelestialKind.Planet, planet.Key);
        int circumference = WorldConstants.CircumferenceFor(locationId, sizeClass);

        var world = _worlds.GetOrCreate(planet, locationId, circumference, out bool isNew);
        world.SizeClass = sizeClass; // remembered for the per-world gravity band seeded in InitWeather
        _generator.SetCircumference(world.World.Circumference); // active world's size for direct gen queries
        // Airless MOONS get cratered regolith too (item 33) — even when their planet type normally has air on a
        // full planet. The asteroid type carries Cratered in data, so it's handled by the planet type itself.
        bool airlessMoon = worldBody?.Kind == CelestialKind.Moon
            && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        _generator.SetCratered(airlessMoon);
        if (!isNew)
        {
            return world; // already resident — keep its fauna/structures/edits
        }

        // Fresh world: GetOrCreate set it active. Build its per-world state + structures. The player's own
        // ship is stamped per-player on join/travel (not here), so each player gets their ship in their world.
        ResetWorldRuntimeState();
        InitWeather();

        // A void world (an orbital station) has no terrain, so it gets none of the planet-surface content —
        // no fauna/flora/fluids, no settlements/wrecks/landing zones. Only its stamped structure lives there
        // (the caller stamps it). Weather is initialised above so the env reads its clear/space-sky settings.
        if (!planet.Void)
        {
            BuildLandingPads(); // FIRST: the pads must reach worldgen before any pad-area chunk generates
            InitFluids();
            InitFire();
            InitFlora();
            InitCreatures();
            LoadContainers();

            if (locationId == GuardianCoreBodyId)
            {
                // The finale body is special: ONLY the Guardian-core chamber + its aperture are placed here.
                // No random settlements / wrecks / vaults / data cubes / net fragments — the procedural
                // structure generator never touches the finale area (by design), so nothing collides with it.
                StampGuardianCoreChamber();
            }
            else
            {
                if (_config.PlaceSettlements)
                {
                    StampSettlement();
                }

                if (_config.PlaceWrecks)
                {
                    StampWreck();
                }

                if (_config.PlaceVaults)
                {
                    StampVaults(); // buried vault ruins ("Welten reicher" W-R3) — 0-2 per world, loot via containers
                }

                if (_config.PlaceDataCubes)
                {
                    StampDataCubes(); // minigame download cubes — 0-N per world (many bodies get none)
                }

                StampNetFragments(); // story net fragments scattered on the surface (P2; self-skips when story off / Void)
            }
        }

        LoadPlayerDoors(); // persisted player-built doors load on every world (void or not, settlement or not)
        LoadBeacons();     // placed radio beacons restore their label/owner entities (the blocks come back via edits)
        LoadBeams();       // placed beam blocks restore their name/owner entities (the blocks come back via edits)

        var body = _galaxy?.FindBody(locationId);
        if (body is not null && body.Status != GenerationStatus.Visited)
        {
            body.Status = GenerationStatus.Visited;
            _repo.SetLocationStatus(body.Id, body.Status.ToString());
        }

        // P3: if a peaceful trader landed on this body while its world was unloaded, re-create its parked ship
        // + pilot now that the world is resident again (the registry is the source of truth, not world state).
        MaterializeLandedTraderHere();

        return world;
    }

    /// <summary>Clears all per-world runtime state so a freshly switched world doesn't keep the old
    /// planet's entities/structures. Persistent collections (landing zones, containers) are reloaded by
    /// their Load* methods; fauna/enemies/NPCs/fluids/flora re-populate from the new world.</summary>
    private void ResetWorldRuntimeState()
    {
        _creatures.Clear();
        _speciesRoster = System.Array.Empty<Shared.Definitions.CreatureSpecies>();
        _planetEnemies.Clear();
        _npcs.Clear();
        _doors.Clear();
        _dataCubes.Clear();
        _settlements.Clear();
        _settlementMarkers.Clear();
        _wreckMarkers.Clear();
        _floraRegrow.Clear();
        _fluidLevel.Clear();
        _activeFluid.Clear();
        _fallingFluid.Clear();
        _worlds.Active.LandedShips.Clear(); // parked-ship objects are per-world; a fresh world starts empty
    }

    /// <summary>
    /// Travels to (and lands on) another celestial body picked from the star map: switches the active
    /// world to the destination, then relocates every player to its landing zone/ship and tells the
    /// client to reload the world. Each body keeps its own edits (persistence is keyed by body id).
    /// </summary>
    /// <summary>Travels the given player to a celestial body by id (also the test/util entrypoint). This is
    /// the unconditional "go there" path — it bypasses the Instant Travel quick-travel gate (it stands in for
    /// flying there + landing), so it always lands the player and marks the body visited.</summary>
    public void Travel(string playerId, string destinationBodyId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            HandleTravel(session, new TravelIntent { DestinationBodyId = destinationBodyId }, quickTravel: false);
        }
    }

    /// <summary>Test hook: toggle the Instant Travel world rule.</summary>
    public void SetInstantTravelForTest(bool on) => Rules.InstantTravel = on;

    /// <summary>Test hook for the travel-screen quick-travel path (gated by the Instant Travel rule). Returns
    /// whether the player ended up at the destination (i.e. the travel was allowed).</summary>
    public bool QuickTravelForTest(string playerId, string destinationBodyId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return false;
        }

        HandleTravel(session, new TravelIntent { DestinationBodyId = destinationBodyId }, quickTravel: true);
        return session.CurrentLocationId == destinationBodyId;
    }

    /// <summary>Travels (instantly) to a body. <paramref name="quickTravel"/> = true is the travel-screen
    /// shortcut: it is gated by the Instant Travel world rule — when that rule is off you may only quick-travel
    /// to bodies you've already landed on. <paramref name="quickTravel"/> = false is a manual flight landing
    /// (you flew there and chose to set down), which is always allowed.</summary>
    private void HandleTravel(PlayerSession session, TravelIntent intent, bool quickTravel = true, bool adminBypass = false)
    {
        Serve(session); // act on the traveller's own world + ship (the jump-drive check below needs it)

        if (!Rules.FreeSpaceFlight)
        {
            Reject(session, "travel", "Space flight is disabled on this server.");
            return;
        }

        var body = _galaxy?.FindBody(intent.DestinationBodyId);
        if (body is null)
        {
            Reject(session, "travel", "No such destination.");
            return;
        }

        // A space station is BOARDED straight from the travel screen (Q1: "board directly"), gated by having
        // visited it before — not landed on like a surface.
        if (body.Kind == CelestialKind.SpaceStation)
        {
            TravelToStation(session, body.Id, quickTravel);
            return;
        }

        if ((body.Kind != CelestialKind.Planet && body.Kind != CelestialKind.Moon && body.Kind != CelestialKind.AsteroidField)
            || string.IsNullOrEmpty(body.PlanetType))
        {
            // Planets, moons AND landable asteroids are surfaces you land on (B45); belts/wrecks are not
            // "travel" destinations (you visit those differently).
            Reject(session, "travel", "You can only land on a planet, moon or asteroid.");
            return;
        }

        if (body.Id == session.CurrentLocationId)
        {
            Reject(session, "travel", "You are already there.");
            return;
        }

        // Instant Travel gate (world option, default off): the travel-screen shortcut may only reach bodies
        // you've already landed on. To reach a new world, fly there and land manually (which marks it). A
        // manual flight landing (quickTravel=false) bypasses this — you physically flew there.
        if (quickTravel && !Rules.InstantTravel && !session.State.LandedBodies.Contains(body.Id))
        {
            Reject(session, "travel", "You haven't been there yet — fly there and land manually first (or enable Instant Travel).");
            return;
        }

        // A jump to a different star system is a hyperspace jump — it needs a jump generator fitted.
        var origin = _galaxy?.FindBody(session.CurrentLocationId);
        bool hyperjump = origin is null || origin.SystemId != body.SystemId;
        if (hyperjump && !adminBypass && (_ship is null || !_ship.HasModule("jump_generator")))
        {
            Reject(session, "travel", "Your ship has no jump generator — fit one to jump between star systems.");
            return;
        }

        // Fixed landing pads (item 38): claim the player's chosen (or first free) pad before tearing down the
        // flight state. A full body (every pad occupied) refuses the landing here, leaving the player in flight.
        if (!ClaimPadOrReject(session, body.Id, intent.PadIndex))
        {
            return;
        }

        // Per-player travel: only THIS player moves. Other players stay on their own worlds.
        string oldLoc = session.CurrentLocationId;
        LeaveSpace(session.State.PlayerId);

        LoadWorld(body.PlanetType, body.Id); // loads/initialises the destination + sets the Active cursor
        session.CurrentLocationId = body.Id;
        MarkArrivedOnBody(session, body.Id); // landed here → a quick-travel target + its system now known

        // Park this player's own ship object on the destination world before placing them.
        SetCurrent(session);
        if (_ship is not null)
        {
            _ship.CurrentLocationId = body.Id; // keep the ship's body in sync so a later launch rises off THIS body (B48)
        }

        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip();
        }

        var (systemName, planetName) = ActiveLocationNames();
        OnPlayerTravelled(session, body.Id, body.Name); // complete any "travel to a place" mission objective (item 31)
        ShipAiOnTravelled(session); // VEGA onboarding: a landing after the first launch + world-type flavour
        var pad = PlayerPad(session); // the pad claimed above (item 38)
        int surfaceY = PadGroundY(pad.CenterX, pad.CenterZ); // matches the ship placement's median footprint height
        var spawn = _shipPlaced ? _healTank : new Vector3f(pad.CenterX + 0.5f, surfaceY + 2f, pad.CenterZ + 0.5f);
        session.State.Position = spawn;
        session.State.RespawnPoint = _shipPlaced ? _healTank : spawn;
        session.State.AboardShip = true;
        session.SentChunks.Clear();
        BroadcastShipTransit(session, body.Id, pad.CenterX + 0.5f, surfaceY, pad.CenterZ + 0.5f, landing: true); // others see the descent (item 38)

        Send(session, new WorldReset { PlanetType = body.PlanetType, PlanetName = planetName, SystemName = systemName, Hyperjump = hyperjump });
        SendPlayerState(session);
        SendShipCombatStatus(session);
        SendLandedShips(session); // every parked ship object on this world (incl. the player's own)
        SendShipPlacement(session);
        SendShipStations(session);
        SendPlanetPois(session);
        SendEnvironment(session);
        PopulateCreaturesNear(session.State, CreatureCapPerPlayer); // arrive to a living world, not an empty one
        SpawnCompanionsForSession(session); // re-materialise the player's pets if this is their companions' home world
        SendCreatures(session);
        SendCompanions(session); // the player's full companion roster (for the Companions menu tab)
        SendDoors(session);
        SendDataCubes(session); // minigame download cubes on this body
        SendNetFragments(session); // story net fragments on this body (P2)
        SendBeacons(session);
        SendBeams(session); // placed beam blocks (teleporter pads) on this body
        SendBases(session); // player-founded bases on this body (Grundstein markers)
        SendLandingPads(session);
        SendContainers(session);
        SendStarMap(session);
        Send(session, new ServerMessage { Text = hyperjump ? $"Hyperjumped to {systemName} — {planetName}." : $"Arrived at {planetName}." });
        CheckpointSave($"landed on {planetName}"); // auto-save when landing on a body

        // Drop the old world from memory if this was the last player there (edits are already persisted).
        if (!string.IsNullOrEmpty(oldLoc) && oldLoc != body.Id && !OccupiedLocations().Contains(oldLoc))
        {
            _worlds.Unload(oldLoc);
        }
    }

    /// <summary>Persistence key for a player's saved ship (one persisted ship per player; extra owned
    /// ships in the fleet live in-memory per session for now).</summary>
    private static string ShipSaveKey(string playerId) => "ship_" + playerId;

    /// <summary>Sets up a freshly-joined player's ship: points the cursor at them, loads or creates their
    /// own ship, registers it as their active ship, and parks it on their (active) world. A player owns
    /// their own fleet (multiple ships possible via crafting/wreck-claim) with exactly one active ship.</summary>
    private void SetupPlayerShip(PlayerSession session)
    {
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);
        MarkArrivedOnBody(session, session.CurrentLocationId); // the home body is a quick-travel target from the start
        var ship = _repo.LoadShip(ShipSaveKey(session.State.PlayerId)) ?? CreateStarterShip();
        RegisterActiveShip(session, ship);
        RecomputeShipCombatStats();
        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip(); // park this player's ship object on their world
            session.State.RespawnPoint = _healTank;
        }

        _repo.SaveShip(ShipSaveKey(session.State.PlayerId), ship);
    }

    private ShipState CreateStarterShip()
    {
        // Prefer the data-driven "starter" ship design; fall back to a built-in module list.
        if (_content.GetShip("starter") is { } def)
        {
            return BuildShipFromDefinition(def);
        }

        var ship = new ShipState { CurrentLocationId = _meta.DefaultPlanetType };
        foreach (var key in new[] { "cockpit", "reactor", "life_support", "workshop", "medbay", "quarters", "cargo_hold_basic", "ship_laser_basic", "tractor_beam" })
        {
            if (_content.GetShipModule(key) is not null)
            {
                ship.Modules.Add(key);
            }
        }

        ResizeCargo(ship);
        return ship;
    }

    /// <summary>Recomputes cargo capacity from built modules, preserving existing contents.</summary>
    private void ResizeCargo(ShipState ship)
    {
        int slots = 0;
        foreach (var moduleKey in ship.Modules)
        {
            if (_content.GetShipModule(moduleKey) is { } m && m.Stats.TryGetValue("cargo_slots", out var s))
            {
                slots += (int)s;
            }
        }

        slots = System.Math.Max(slots, 1);
        if (ship.Cargo.SlotCount == slots)
        {
            return;
        }

        var resized = new Inventory(slots);
        for (int i = 0; i < ship.Cargo.SlotCount; i++)
        {
            if (ship.Cargo.Slots[i] is { } stack && !stack.IsEmpty)
            {
                resized.Add(stack.Item, stack.Count, _content.MaxStackOf(stack.Item));
            }
        }

        ship.Cargo = resized;
    }

    /// <summary>Blocking loop; runs until <see cref="Stop"/> is called.</summary>
    public void Run()
    {
        _running = true;
        _runLoopActive = true;
        _stopped.Reset();
        double tickSeconds = 1.0 / System.Math.Max(1, _config.TickRate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds;

        try
        {
            while (_running)
            {
                double now = sw.Elapsed.TotalSeconds;
                double dt = now - last;
                last = now;

                Tick(dt);

                double sleep = tickSeconds - (sw.Elapsed.TotalSeconds - now);
                if (sleep > 0)
                {
                    System.Threading.Thread.Sleep((int)(sleep * 1000));
                }
            }

            // Shutdown was requested (RequestStop): persist + close down HERE, on the tick thread, so the
            // save never races a concurrent Tick. This is the only place that touches _sessions/_repo at
            // shutdown when a run loop is active.
            Shutdown();
        }
        finally
        {
            _runLoopActive = false;
            _stopped.Set(); // wake any thread blocked in Stop()
        }
    }

    /// <summary>Signals the <see cref="Run"/> loop to stop after the current tick. Safe to call from any
    /// thread (e.g. a Ctrl-C handler): it does NOT save — the run loop drains + saves on the tick thread.</summary>
    public void RequestStop() => _running = false;

    public void Stop()
    {
        if (_runLoopActive)
        {
            // A run loop owns the tick thread: ask it to stop and let IT do the save (no cross-thread save
            // race). Block until it has drained, with a timeout so a wedged tick can't hang shutdown forever.
            RequestStop();
            _stopped.Wait(TimeSpan.FromSeconds(10));
            return;
        }

        // No run loop (tests / manual TickForTest drivers): save synchronously inline on the caller's thread.
        Shutdown();
    }

    /// <summary>Persists everything and closes the transport. Always runs on the thread that owns ticking
    /// (the run loop, or the test driver) so it never races a concurrent <see cref="Tick"/>.</summary>
    private void Shutdown()
    {
        SaveAll();
        _repo.Flush();
        _transport.Stop();
        _log.Info("Server stopped and world saved.");
    }

    // ---------------- Tick ----------------

    public void Tick(double deltaSeconds)
    {
        _transport.Poll();
        TickSpace(deltaSeconds); // space instances are keyed by location and handle their own players

        // Tick each occupied world with the Active cursor set to it, so its environment/fauna/fluids/
        // weather/presence/chunk-streaming only touch that world's players. With a single occupied world
        // this runs once and is identical to the old flat tick. When no world is occupied we still tick the
        // active one (so its weather/fluids advance — and so headless tests with no players still simulate).
        var ticking = OccupiedLocations();
        if (ticking.Count == 0 && _worlds.Active != null)
        {
            ticking.Add(_worlds.Active.LocationId);
        }

        foreach (var locId in ticking)
        {
            if (!SetActiveWorld(locId))
            {
                continue;
            }

            TickEnvironment(deltaSeconds);
            TickEnemies(deltaSeconds);
            TickPresence(deltaSeconds);
            TickFluids(deltaSeconds);
            TickFire(deltaSeconds);
            TickWeather(deltaSeconds);
            TickFlora(deltaSeconds);
            TickCreatures(deltaSeconds);
            TickNpcs(deltaSeconds);
            TickLandedTraders(deltaSeconds); // P3: materialize/lift-off a peaceful trader parked on this surface
            TickDoors(deltaSeconds);
            TickVoidRescue(deltaSeconds);
            TickShipAi(deltaSeconds); // VEGA advisor hints + memory-fragment redemption
            StreamChunks();
        }

        SampleHistories(deltaSeconds);
        SweepExpiredLandedTraders(); // P3: free pads of traders whose dwell ended on bodies nobody is on
        TickGreetings(); // push any LLM NPC greetings finished off-thread (item 15)
        TickMissionTexts(); // push mission-list refreshes when L3 board texts arrive
        TickVegaBanter(); // push VEGA's LLM banter lines finished off-thread

        AccumulatePlaytime(deltaSeconds);

        _sinceAutoSave += deltaSeconds;
        if (_sinceAutoSave >= _config.AutoSaveIntervalMinutes * 60.0)
        {
            _sinceAutoSave = 0;
            SaveAll();
            _log.Info("Autosave complete.");
        }
    }

    /// <summary>Advances the world's cumulative playtime — but only while at least one player is joined, so an
    /// idle dedicated server (or a headless test with no players) never inflates it. Whole elapsed seconds are
    /// committed to <see cref="WorldMetadata.CumulativePlaytimeSeconds"/>; the sub-second remainder carries over.
    /// The value is persisted by the next <see cref="SaveAll"/> (it rides along in the metadata blob).</summary>
    private void AccumulatePlaytime(double deltaSeconds)
    {
        bool anyJoined = false;
        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                anyJoined = true;
                break;
            }
        }

        if (!anyJoined)
        {
            return;
        }

        _playtimeCarry += deltaSeconds;
        if (_playtimeCarry >= 1.0)
        {
            long whole = (long)_playtimeCarry;
            _meta.CumulativePlaytimeSeconds += whole;
            _playtimeCarry -= whole;
        }
    }

    /// <summary>Test helper kept explicit so tests can drive one authoritative server tick.</summary>
    public void TickForTest(double deltaSeconds) => Tick(deltaSeconds);

    private void TickEnvironment(double dt)
    {
        if (ReconcileSpeeders()) // materialise present owners' speeders / despawn departed owners' (hover vehicles)
        {
            BroadcastSpeeders();
        }

        foreach (var session in JoinedInActiveWorld())
        {
            SetCurrent(session); // per-player ship cursor: own heal-tank/aboard/stamp resolve correctly
            UpdateAboard(session);

            var p = session.State;

            // Walk out of the ship's hatch while it floats in space → step straight onto an EVA spacewalk
            // (rather than falling into the void around the interior). The door you already have IS the airlock.
            if (InShipInterior(p.PlayerId) && SteppedOutOfShipHull(p.Position))
            {
                StartEvaFromShip(p.PlayerId);
                continue; // transitioned out of this world — skip the rest of the on-foot tick for this player
            }

            // Built/climbed a tower above the atmosphere → float in space on foot (item 10).
            UpdateAboveAtmosphere(session);

            DecayTeleportCooldown(p.PlayerId, dt);
            DecayBeamCooldown(p.PlayerId, dt);
            TickStealth(session, dt);
            TickJetpack(session, dt);
            float maxOxygen = MaxOxygen(p);
            if (p.GodMode)
            {
                p.Health = 100f;
                p.Oxygen = maxOxygen;
                p.Hunger = 100f;
                continue; // invulnerable: no drain, no death
            }

            // On an EVA spacewalk there is no atmosphere and no ship/station life support: always drain,
            // regardless of the body you launched from being breathable. InEva overrides everything below.
            // Standing physically inside the landed ship's cabin counts as life support too — a sealed cabin
            // gives air even on an airless planet, so you never suffocate inside your own ship (B41b).
            bool insideShip = !p.InEva && ShipInteriorContains(p.Position);
            bool lifeSupport = !p.InEva && (p.AboardShip || insideShip || InStation(p.PlayerId) || !Rules.OxygenEnabled);
            // Submerged underwater the suit runs on its own air, even on a breathable world — diving spends
            // the oxygen tank just like a toxic/airless atmosphere does (the extractor can't pull from water).
            bool submerged = !lifeSupport && !p.InEva && HeadUnderwater(p);
            // Above the atmosphere (built a tower up into space) the air runs out too, even on a breathable
            // world — the suit tank drains until the player descends back below the line.
            if (!submerged && !p.AboveAtmosphere && (lifeSupport || (!p.InEva && AtmosphereBreathable)))
            {
                // Aboard the ship (life support), boarded on a station (its life support), oxygen disabled
                // by rules, or a breathable atmosphere: regenerate, no drain (up to the tank capacity).
                p.Oxygen = System.Math.Min(maxOxygen, p.Oxygen + (float)(dt * 25));
                p.Health = System.Math.Min(100f, p.Health + (float)(dt * 2));

                // Aboard the ship the suit recharges (powers the jetpack / stealth / suit tools); outside it
                // only refills at a heal-tank. Don't recharge while actively spending it.
                if (p.AboardShip && !p.Stealthed && !p.Jetpacking)
                {
                    p.SuitEnergy = System.Math.Min(100f, p.SuitEnergy + (float)(dt * 20));
                }
            }
            else
            {
                // Outside without breathable air (toxic / airless) or submerged underwater: drain the tank.
                float drain = (float)(dt * Rules.OxygenDrainPerSecond);
                if (!submerged && !p.InEva && !p.AboveAtmosphere && _oxygenExtractability > 0 && p.Inventory.Has("oxygen_extractor", 1))
                {
                    // The suit extracts some oxygen from a toxic atmosphere — reduces (never refills)
                    // the drain, scaled by how breathable-ish this world is. Airless worlds (0) don't help.
                    drain *= 1f - OxygenExtractorEffectiveness * (float)_oxygenExtractability;
                }

                p.Oxygen = System.Math.Max(0f, p.Oxygen - drain);
                if (p.Oxygen <= 0f)
                {
                    p.Health = System.Math.Max(0f, p.Health - (float)(dt * 5));
                }
            }

            // Lava burns (reduced by armor).
            if (InLava(p.Position))
            {
                p.Health = System.Math.Max(0f, p.Health - Mitigate(p, (float)(dt * 15)));
            }

            // Standing in fire burns too (item 30) — a little less than lava.
            if (InFire(p.Position))
            {
                p.Health = System.Math.Max(0f, p.Health - Mitigate(p, (float)(dt * 10)));
            }

            // Hunger (survival): aboard the ship, boarded on a station (both have life support), or when
            // disabled, sate; otherwise drain and, once empty, starve (health loss until the player eats).
            if (p.AboardShip || InStation(p.PlayerId) || !Rules.HungerEnabled)
            {
                p.Hunger = System.Math.Min(100f, p.Hunger + (float)(dt * 10));
            }
            else
            {
                p.Hunger = System.Math.Max(0f, p.Hunger - (float)(dt * Rules.HungerDrainPerSecond));
                if (p.Hunger <= EmergencyRationThreshold)
                {
                    TryAutoEatRation(session); // suit auto-feeds a stored ration before starvation
                }

                if (p.Hunger <= 0f)
                {
                    p.Health = System.Math.Max(0f, p.Health - (float)(dt * 3));
                }
            }

            if (p.Health <= 0f)
            {
                RespawnPlayer(session, "Critical condition — emergency recovery to the Medbay heal-tank.");
                continue;
            }

            // Periodic vitals sync: oxygen/hunger/energy/health drain + regen every tick SERVER-side, but
            // PlayerStateUpdate used to go out only on discrete events — the HUD bars froze in between.
            // Push the state twice a second whenever a vital has visibly moved since the last send.
            session.VitalsSyncTimer += dt;
            if (session.VitalsSyncTimer >= 0.5)
            {
                session.VitalsSyncTimer = 0;
                bool changed = System.Math.Abs(p.Health - session.LastSentHealth) > 0.4f
                    || System.Math.Abs(p.Oxygen - session.LastSentOxygen) > 0.4f
                    || System.Math.Abs(p.SuitEnergy - session.LastSentEnergy) > 0.4f
                    || System.Math.Abs(p.Hunger - session.LastSentHunger) > 0.4f;
                if (changed)
                {
                    session.LastSentHealth = p.Health;
                    session.LastSentOxygen = p.Oxygen;
                    session.LastSentEnergy = p.SuitEnergy;
                    session.LastSentHunger = p.Hunger;
                    SendPlayerState(session);
                }
            }
        }
    }

    /// <summary>Blocks of overlap below the atmosphere line before the in-space state drops, so a player
    /// hovering right at the boundary doesn't flicker in and out of zero-g.</summary>
    private const float AtmosphereHysteresis = 4f;

    /// <summary>Flips <see cref="Shared.State.PlayerState.AboveAtmosphere"/> when an on-foot player crosses
    /// the planet's atmosphere line (item 10), broadcasting the change. Only an on-foot player on a real
    /// planet qualifies (not aboard / EVA / ship interior / station; only worlds with an atmosphere line).</summary>
    private void UpdateAboveAtmosphere(PlayerSession session)
    {
        var p = session.State;
        bool eligible = _atmosphereHeight > 0
            && !p.AboardShip && !p.InEva
            && !InShipInterior(p.PlayerId) && !InStation(p.PlayerId);

        // Hysteresis: cross up at the line, drop only once a few blocks back below it.
        bool above = eligible && (p.AboveAtmosphere
            ? p.Position.Y > _atmosphereHeight - AtmosphereHysteresis
            : p.Position.Y > _atmosphereHeight);

        if (above != p.AboveAtmosphere)
        {
            p.AboveAtmosphere = above;
            SendPlayerState(session);
        }
    }

    /// <summary>True when the player's head is inside a water block — diving spends the suit's oxygen tank.</summary>
    private bool HeadUnderwater(Shared.State.PlayerState p)
    {
        if (_waterId == 0)
        {
            return false;
        }

        var head = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(
            (int)System.Math.Floor(p.Position.X), (int)System.Math.Floor(p.Position.Y + 1.5f), (int)System.Math.Floor(p.Position.Z));
        return _world.GetBlock(head).Value == _waterId;
    }

    /// <summary>Hunger level at or below which the suit auto-consumes a stored emergency ration.</summary>
    private const float EmergencyRationThreshold = 15f;

    /// <summary>Base fraction of oxygen drain the suit extractor can offset (× the planet's extractability).</summary>
    private const float OxygenExtractorEffectiveness = 0.6f;

    /// <summary>
    /// Auto-feed when hungry: the suit's ration dispenser dispenses stored food first; failing that
    /// a loose emergency ration in the inventory is eaten. Applies the food's hunger restore.
    /// </summary>
    private void TryAutoEatRation(PlayerSession session)
    {
        var p = session.State;

        // 1) The ration dispenser — eat the first stored food (any consumable that sates hunger).
        for (int i = 0; i < p.RationStore.SlotCount; i++)
        {
            if (p.RationStore.Slots[i] is { } stack && !stack.IsEmpty
                && _content.GetItem(stack.Item) is { Category: ItemCategory.Consumable } food && food.ConsumeHunger > 0f)
            {
                p.RationStore.Remove(stack.Item, 1);
                p.Hunger = System.Math.Min(100f, p.Hunger + food.ConsumeHunger);
                SendInventory(session);
                return;
            }
        }

        // 2) Fallback: a loose emergency ration carried in the inventory.
        if (p.Inventory.Has("emergency_ration", 1))
        {
            p.Inventory.Remove("emergency_ration", 1);
            float restore = _content.GetItem("emergency_ration")?.ConsumeHunger ?? 40f;
            p.Hunger = System.Math.Min(100f, p.Hunger + restore);
            SendInventory(session);
        }
    }

    /// <summary>Loads food from the player's inventory into the suit ration dispenser (food only, up to capacity).</summary>
    public void LoadRation(string playerId, string itemKey, int count)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var def = _content.GetItem(itemKey);
        if (def is not { Category: ItemCategory.Consumable } || def.ConsumeHunger <= 0f)
        {
            Reject(session, "ration", "Only food can go in the ration dispenser.");
            return;
        }

        var p = session.State;
        int want = System.Math.Min(System.Math.Max(1, count), p.Inventory.CountOf(itemKey));
        if (want <= 0)
        {
            Reject(session, "ration", "You don't have that food.");
            return;
        }

        int leftover = p.RationStore.Add(itemKey, want, def.MaxStack); // capped by the dispenser's slots
        int stored = want - leftover;
        if (stored > 0)
        {
            p.Inventory.Remove(itemKey, stored);
            SendInventory(session);
        }
        else
        {
            Reject(session, "ration", "The ration dispenser is full.");
        }
    }

    private void HandleLoadRation(PlayerSession session, LoadRationIntent intent)
        => LoadRation(session.State.PlayerId, intent.ItemKey, intent.Count);

    /// <summary>
    /// Returns the player to the heal-tank in their ship's Medbay and restores vitals. Per
    /// the active rules, non-tool items may be left behind in a salvage capsule at the
    /// death site (`anf_admin_blueprinf.md` §2–3).
    /// </summary>
    private void RespawnPlayer(PlayerSession session, string reason)
    {
        var p = session.State;
        bool dropSalvage = !Rules.KeepInventoryOnDeath &&
                           Rules.DeathPenalty is DeathPenalty.Normal or DeathPenalty.Hard;

        bool salvaged = false;
        if (dropSalvage)
        {
            var capsule = new StoredContainer
            {
                Id = "salvage_" + Guid.NewGuid().ToString("N"),
                Planet = _world.LocationId,
                Kind = "salvage_capsule",
                Position = p.Position.ToBlock(),
            };

            for (int i = 0; i < p.Inventory.SlotCount; i++)
            {
                if (p.Inventory.Slots[i] is { } stack && !stack.IsEmpty)
                {
                    var def = _content.GetItem(stack.Item);
                    if (def is { Category: ItemCategory.Tool })
                    {
                        continue; // tools are never lost
                    }

                    capsule.Items.Add(stack.Clone());
                    p.Inventory.SetSlot(i, null);
                }
            }

            if (capsule.Items.Count > 0)
            {
                AddContainer(capsule); // persists + tracks + broadcasts (now lootable)
                salvaged = true;
            }
        }

        // Capture where the player died BEFORE resetting state — it decides whether a full world transition
        // is needed (you died away from the ship's world) or just a snap to the heal-tank.
        bool wasInFlightView = InSpace(p.PlayerId);
        bool wasInShipInterior = _inShipInterior.ContainsKey(p.PlayerId);

        p.Health = 100f;
        p.Oxygen = MaxOxygen(p);
        p.SuitEnergy = 100f;
        p.Hunger = 100f;
        p.Stealthed = false;
        p.InEva = false; // a death ends any spacewalk
        _inShipInterior.Remove(p.PlayerId); // and any in-ship walkabout
        _dockedFromEva.Remove(p.PlayerId);  // and any "ship floating while docked" memory

        // On foot on a planet your ship is already there (you land with it) — a plain heal-tank snap. You only
        // need a world transition if you died away from your ship's world: in the flight view, on a spacewalk,
        // inside the ship, or boarded on a station.
        bool sameWorld = !wasInFlightView && !wasInShipInterior && !InStation(p.PlayerId);

        if (sameWorld)
        {
            // Died on the ship's own world on foot — snap to the heal-tank, no loading screen.
            p.Position = p.RespawnPoint;
            p.AboardShip = true;
            Send(session, new RespawnNotice
            {
                X = p.RespawnPoint.X,
                Y = p.RespawnPoint.Y,
                Z = p.RespawnPoint.Z,
                Reason = reason,
                SalvageCapsuleDropped = salvaged,
                Died = true, // an actual death → client plays the red death flash + sound
            });
            SendInventory(session);
            SendPlayerState(session);
        }
        else
        {
            // Died on a spacewalk, in the flight view, inside the ship, or on another body — recover with a
            // proper world transition to the ship's planet + heal-tank, so you always come back WITH the ship
            // and are never left stuck in the flight view or a stale world.
            RecoverToShip(session, reason, salvaged);
        }

        _repo.SavePlayer(p);
        _log.Info($"Player '{p.Name}' respawned (salvage={salvaged}, transition={!sameWorld}).");
    }

    /// <summary>Death recovery with a world transition: lands the player at their ship's heal-tank on the
    /// ship's planet, leaving any space instance first so the client drops out of the flight view.</summary>
    private void RecoverToShip(PlayerSession session, string reason, bool salvaged)
    {
        var p = session.State;
        // The cursor is already on this session (set by the per-player tick / Serve), so _ship is this
        // player's ship — recover to the world it's parked on.
        string shipHome = !string.IsNullOrEmpty(_ship?.CurrentLocationId) ? _ship.CurrentLocationId : _meta.ActiveLocationId;

        // Finale rule (P6): a death inside the Guardian system must not respawn the clone in the boss arena —
        // send it back to the world it launched into the finale from (re-homing the ship there), so there is no
        // death-loop and the finale has to be re-approached.
        string homeLoc = ResolveRespawnHome(p.PlayerId, shipHome);
        if (homeLoc != shipHome && _ship is not null)
        {
            _ship.CurrentLocationId = homeLoc; // the ship follows the clone back to the prior world
        }

        var homeBody = _galaxy?.FindBody(homeLoc);
        string homeType = !string.IsNullOrEmpty(homeBody?.PlanetType) ? homeBody.PlanetType : _meta.DefaultPlanetType;

        LeaveSpace(p.PlayerId); // exit any flight view (sends SpaceClosed if in one)

        LoadWorld(homeType, homeLoc);
        SetCurrent(session);
        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip();
        }

        session.CurrentLocationId = homeLoc;
        MarkArrivedOnBody(session, homeLoc); // respawned onto this body → keep it a quick-travel target
        p.Position = _shipPlaced ? _healTank : p.RespawnPoint;
        p.RespawnPoint = _shipPlaced ? _healTank : p.RespawnPoint;
        p.AboardShip = true;
        session.SentChunks.Clear();

        var (systemName, planetName) = ActiveLocationNames();
        Send(session, new WorldReset { PlanetType = homeType, PlanetName = planetName, SystemName = systemName, Hyperjump = false });
        Send(session, new RespawnNotice
        {
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Reason = reason,
            SalvageCapsuleDropped = salvaged,
            Died = true,
        });
        SendPlayerState(session);
        SendEnvironment(session);
        SendInventory(session);
        SendLandedShips(session); // the respawn world's parked ship objects
        SendPlanetPois(session);
        SendCreatures(session);
        SendContainers(session);
        SendNpcs(session);
    }

    private void StreamChunks()
    {
        int radius = System.Math.Max(1, _config.ViewDistanceChunks);
        const int perTickBudget = 12;

        // Chunk band the build height maps to — the streamed column is clamped into it so a spoofed player
        // position can't make the server generate/cache chunks at arbitrary heights (memory DoS). See MinBuildY.
        int minChunkY = WorldConstants.WorldToChunk(MinBuildY);
        int maxChunkY = WorldConstants.WorldToChunk(MaxBuildY);

        foreach (var session in JoinedInActiveWorld())
        {
            var center = WorldConstants.WorldToChunk(session.State.Position.ToBlock());
            center = new ChunkCoord(center.X, System.Math.Clamp(center.Y, minChunkY, maxChunkY), center.Z);

            // Collect the not-yet-sent chunks in the view column and stream them NEAREST-FIRST. The player's
            // own chunk (its floor) then loads before everything else, so a freshly spawned/teleported player
            // gets solid ground under them immediately instead of falling through while a fixed bottom-up
            // order slowly works up toward the surface (which, on a fresh world's slow first-gen + a large
            // view distance, could outlast the client's settle-freeze and drop them below the terrain). A
            // taller vertical span (esp. below) is still covered so digging down never outruns the terrain.
            var pending = new List<(ChunkCoord Coord, int DistSq)>();
            for (int dy = -3; dy <= 2; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        // Canonicalize longitude so chunks just west of the seam (center.X+dx < 0) stream as the
                        // wrapped chunk from the far side — the player can see across X = 0 ≡ X = Circumference.
                        var coord = WorldConstants.CanonicalChunk(new ChunkCoord(center.X + dx, center.Y + dy, center.Z + dz), _world.Circumference);
                        if (session.SentChunks.Contains(coord))
                        {
                            continue;
                        }

                        pending.Add((coord, dx * dx + dy * dy + dz * dz));
                    }

            pending.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

            int sent = 0;
            foreach (var (coord, _) in pending)
            {
                if (sent >= perTickBudget)
                {
                    break;
                }

                if (session.SentChunks.Contains(coord))
                {
                    continue; // two view offsets can map to the same wrapped chunk — send it once
                }

                var chunk = _world.GetOrLoadChunk(coord);
                var msg = new ChunkDataMessage
                {
                    Cx = coord.X,
                    Cy = coord.Y,
                    Cz = coord.Z,
                    Blocks = chunk.ToArray(),
                };
                PackChunkModifiers(chunk, msg); // dyed-block / coloured-light cells, if any
                Send(session, msg);
                session.SentChunks.Add(coord);
                sent++;
            }
        }
    }

    /// <summary>Fills a chunk message's sparse colour-modifier + shape arrays from the chunk's dyed/glowing/
    /// shaped cells (no-op for the overwhelming majority of chunks, which carry none).</summary>
    private static void PackChunkModifiers(BlocksBeyondTheStars.Shared.World.ChunkData chunk, ChunkDataMessage msg)
    {
        var mods = chunk.Modifiers;
        if (mods is not null && mods.Count > 0)
        {
            int n = mods.Count;
            var idx = new int[n];
            var tint = new int[n];
            var glow = new int[n];
            int i = 0;
            foreach (var kv in mods)
            {
                idx[i] = kv.Key;
                tint[i] = kv.Value.Tint;
                glow[i] = kv.Value.Glow;
                i++;
            }

            msg.ModIndex = idx;
            msg.ModTint = tint;
            msg.ModGlow = glow;
        }

        var shapes = chunk.Shapes;
        if (shapes is not null && shapes.Count > 0)
        {
            int n = shapes.Count;
            var sIdx = new int[n];
            var sData = new int[n];
            int i = 0;
            foreach (var kv in shapes)
            {
                sIdx[i] = kv.Key;
                sData[i] = kv.Value;
                i++;
            }

            msg.ShapeIndex = sIdx;
            msg.ShapeData = sData;
        }
    }

    /// <summary>Heals a stale client chunk view (a "ghost" block the server no longer has): confirms the cell's
    /// authoritative block immediately and forgets the chunk on this session so <see cref="StreamChunks"/> re-sends
    /// the current authoritative chunk next tick — clearing every ghost in it at once.</summary>
    private void ResyncStaleChunk(PlayerSession session, Vector3i pos)
    {
        var (rsTint, rsGlow) = _world.GetModifier(pos);
        Send(session, new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = _world.GetBlock(pos).Value, Tint = rsTint, Glow = rsGlow, Shape = _world.GetShape(pos) });
        var coord = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(pos), _world.Circumference);
        session.SentChunks.Remove(coord); // not-sent again → StreamChunks re-streams it on the next tick
    }

    // ---------------- Connection handling ----------------

    private void OnClientConnected(int connectionId)
    {
        // Session is created on a successful JoinRequest; just note the pending connection.
        _log.Info($"Connection {connectionId} opened; awaiting join.");
    }

    private void OnClientDisconnected(int connectionId)
    {
        if (_sessions.TryGetValue(connectionId, out var session) && session.Joined)
        {
            ClearDocking(session.State.PlayerId);
            LeaveSpace(session.State.PlayerId);
            LeaveStation(session.State.PlayerId);
            CancelTradesFor(session.State.PlayerId);
            _repo.SavePlayer(session.State);
            SetCurrent(session);
            if (session.Ships.TryGetValue(session.ActiveShipId, out var theirShip))
            {
                _repo.SaveShip(ShipSaveKey(session.State.PlayerId), theirShip);
            }

            string loc = session.CurrentLocationId;
            _sessions.Remove(connectionId);
            ClearAlliancePending(session.State.PlayerId); // drop transient requests; refresh online allies' rosters
            SetActiveWorld(loc);
            RemoveLandedShip(session); // the parked ship object leaves with its owner (ship-as-object)
            BroadcastToWorld(new PlayerLeft { PlayerId = session.State.PlayerId }); // remove their avatar in-world
            if (!string.IsNullOrEmpty(loc) && loc != _meta.ActiveLocationId && !OccupiedLocations().Contains(loc))
            {
                _worlds.Unload(loc); // last player left this body — drop it from memory (edits persisted)
            }
        }
        else
        {
            _sessions.Remove(connectionId);
        }

        _log.Info($"Connection {connectionId} closed.");
    }

    private void OnPayload(int connectionId, byte[] payload)
    {
        var message = NetCodec.Decode(payload);
        if (message is null)
        {
            return;
        }

        if (message is JoinRequest join)
        {
            HandleJoin(connectionId, join);
            return;
        }

        if (!_sessions.TryGetValue(connectionId, out var session) || !session.Joined)
        {
            return; // ignore gameplay intents before joining
        }

        // Operate on the sender's world + ship: block edits, broadcasts, ship state and lookups in the
        // handlers below all go through the Active world cursor + the ship cursor.
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);

        // A handler throwing must never take down the single-threaded tick (whole-server DoS). Log it with
        // the offending message type + connection and drop the message; the world keeps simulating.
        try
        {
            Dispatch(session, message);
        }
        catch (Exception ex)
        {
            _log.Error($"Handler for {message.GetType().Name} from connection {connectionId} threw: {ex}");
        }
    }

    private void Dispatch(PlayerSession session, object message)
    {
        switch (message)
        {
            case MoveIntent move: HandleMove(session, move); break;
            case SelectHotbarIntent hotbar: session.State.SelectedHotbarSlot = System.Math.Clamp(hotbar.Slot, 0, HotbarSlots - 1); break;
            case MoveItemIntent moveItem: HandleMoveItem(session, moveItem); break;
            case MineBlockIntent mine: HandleMine(session, mine); break;
            case PlaceBlockIntent place: HandlePlace(session, place); break;
            case CraftIntent craft: HandleCraft(session, craft); break;
            case TintCraftIntent tint: HandleTintCraft(session, tint); break;
            case ShapeCraftIntent shapeIntent: HandleShapeCraft(session, shapeIntent); break;
            case UnlockBlueprintIntent unlock: HandleUnlock(session, unlock); break;
            case ChatIntent chat: HandleChat(session, chat); break;
            case VoiceFrame voice: HandleVoice(session, voice); break;
            case BumpReport bump: HandleBumpReport(session, bump); break;
            case RequestStarMap: SendStarMap(session); break;
            case SaveGameIntent: SaveAll(); _log.Info($"Explicit save requested by '{session.State.Name}'."); break;
            case TractorPullIntent pull: HandleTractorPull(session, pull); break;
            case DoorInteractIntent door: HandleDoorInteract(session, door); break;
            case UnlockGameIntent unlockGame: HandleUnlockGame(session, unlockGame); break;
            case MinigameResultIntent miniResult: HandleMinigameResult(session, miniResult); break;
            case FallDamageIntent fall: HandleFallDamage(session, fall); break;
            case AdminCommandIntent admin: HandleAdminCommand(session, admin); break;
            case RequestMissions: SendMissionList(session); break;
            case AcceptMissionIntent accept: HandleAcceptMission(session, accept.MissionId); break;
            case TurnInMissionIntent turnIn: HandleTurnInMission(session, turnIn.MissionId); break;
            case CreateMissionIntent create: HandleCreateMission(session, create); break;
            case DockRequestIntent dock: HandleDockRequest(session, dock); break;
            case DockResponseIntent response: HandleDockResponse(session, response); break;
            case UndockIntent: HandleUndock(session); break;
            case BuildShipModuleIntent build: HandleBuildModule(session, build); break;
            case EnterSpaceIntent: HandleEnterSpace(session); break;
            case HyperjumpSystemIntent hyperjump: HandleHyperjumpSystem(session, hyperjump); break;
            case EnterShipIntent: EnterShipInterior(session.State.PlayerId); break;
            case ExitShipIntent: ExitShipToFlight(session.State.PlayerId); break;
            case LeaveSpaceIntent leaveSpace: HandleLeaveSpace(session, leaveSpace); break;
            case FireWeaponIntent fire: HandleFireWeapon(session, fire); break;
            case AttackEntityIntent attack: HandleAttackEntity(session, attack); break;
            case UseStationIntent use: HandleUseStation(session, use); break;
            case SetAppearanceIntent appearance: HandleSetAppearance(session, appearance); break;
            case SetFaceIntent face: HandleSetFace(session, face); break;
            case CraftShipIntent craftShip: HandleCraftShip(session, craftShip); break;
            case SwitchShipIntent switchShip: HandleSwitchShip(session, switchShip); break;
            case ConsumeItemIntent consume: HandleConsume(session, consume); break;
            case UseGadgetIntent gadget: HandleUseGadget(session, gadget); break;
            case TameRespondIntent tameResp: HandleTameRespond(session, tameResp); break;
            case RequestCompanionsIntent: HandleRequestCompanions(session); break;
            case SetCompanionNameIntent compName: HandleSetCompanionName(session, compName); break;
            case ReleaseCompanionIntent release: HandleReleaseCompanion(session, release); break;
            case EnterSpeederIntent enterSpeeder: HandleEnterSpeeder(session, enterSpeeder); break;
            case ExitSpeederIntent: HandleExitSpeeder(session); break;
            case StowSpeederIntent stowSpeeder: HandleStowSpeeder(session, stowSpeeder); break;
            case RefuelSpeederIntent refuelSpeeder: HandleRefuelSpeeder(session, refuelSpeeder); break;
            case SpeederImpactIntent speederImpact: HandleSpeederImpact(session, speederImpact); break;
            case SetBeaconLabelIntent beacon: HandleSetBeaconLabel(session, beacon); break;
            case SetBeamNameIntent beamName: HandleSetBeamName(session, beamName); break;
            case BeamTeleportIntent beamJump: HandleBeamTeleport(session, beamJump); break;
            case SetBaseNameIntent baseName: HandleSetBaseName(session, baseName); break;
            case SetStationNameIntent stationName: HandleSetStationName(session, stationName); break;
            case RequestLandingPadsIntent reqPads: HandleRequestLandingPads(session, reqPads); break;
            case LootContainerIntent loot: HandleLootContainer(session, loot); break;
            case DepositContainerIntent dep: HandleDepositContainer(session, dep); break;
            case ShipMoveIntent shipMove: HandleShipMove(session, shipMove); break;
            case DisassembleIntent disassemble: HandleDisassemble(session, disassemble); break;
            case TradeRequestIntent tradeReq: HandleTradeRequest(session, tradeReq); break;
            case TradeRespondIntent tradeResp: HandleTradeRespond(session, tradeResp); break;
            case TradeOfferIntent tradeOffer: HandleTradeOffer(session, tradeOffer); break;
            case TradeKnowledgeIntent tradeKnow: HandleTradeKnowledge(session, tradeKnow); break;
            case TradeConfirmIntent: HandleTradeConfirm(session); break;
            case TradeCancelIntent: HandleTradeCancel(session); break;
            case ScanIntent scan: HandleScan(session, scan); break;
            case ScanEntityIntent scanEntity: HandleScanEntity(session, scanEntity); break;
            case LoadRationIntent loadRation: HandleLoadRation(session, loadRation); break;
            case TeleportToShipIntent: HandleTeleportToShip(session); break;
            case ToggleStealthIntent: HandleToggleStealth(session); break;
            case SetJetpackIntent sj: HandleSetJetpack(session, sj); break;
            case SetEvaIntent eva: HandleSetEva(session, eva); break;
            case StructureEditIntent structureEdit: HandleStructureEdit(session, structureEdit); break;
            case DeployStationCoreIntent: HandleDeployStationCore(session); break;
            case BoardStationIntent boardStation: HandleBoardStation(session, boardStation); break;
            case LeaveStationIntent: HandleLeaveStation(session); break;
            case RepairWreckIntent repairWreck: HandleRepairWreck(session, repairWreck); break;
            case ClaimWreckIntent: HandleClaimWreck(session); break;
            case RepairShipIntent repairShip: HandleRepairShip(session, repairShip); break;
            case TravelIntent travel: HandleTravel(session, travel); break;
            case NpcGreetIntent greet: HandleNpcGreet(session, greet); break;
            case SkipOnboardingIntent skipOnboarding: HandleSkipOnboarding(session, skipOnboarding); break;
            case SetWorldRulesIntent worldRules: HandleSetWorldRules(session, worldRules); break;
            case RequestAllianceListIntent: HandleRequestAllianceList(session); break;
            case RequestAllianceIntent allianceReq: HandleRequestAlliance(session, allianceReq); break;
            case AllianceResponseIntent allianceResp: HandleAllianceResponse(session, allianceResp); break;
            case DissolveAllianceIntent allianceDis: HandleDissolveAlliance(session, allianceDis); break;
            case StorySelectIntent storySelect: HandleStorySelect(session, storySelect); break;
            case NetFragmentFoundIntent netFrag: HandleNetFragmentFound(session, netFrag); break;
            case CoreHackIntent coreHack: HandleCoreHack(session, coreHack); break;
            case CoreDialogueChoiceIntent coreChoice: HandleCoreDialogueChoice(session, coreChoice); break;
        }
    }

    /// <summary>The body to place a (re)joining player on: the one they were last on (persisted per-player)
    /// if it is a real landable body, otherwise the home/default body — for a first join, or a transient
    /// save location like a station / in space.</summary>
    private (string Body, string Type) RestoreJoinBody(Shared.State.PlayerState state)
    {
        if (_galaxy?.FindBody(state.CurrentLocationId) is { } b
            && b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
            && !string.IsNullOrEmpty(b.PlanetType))
        {
            return (b.Id, b.PlanetType);
        }

        return (_meta.ActiveLocationId, _meta.DefaultPlanetType);
    }

    private void HandleJoin(int connectionId, JoinRequest join)
    {
        if (join.ProtocolVersion != Protocol.Version)
        {
            SendTo(connectionId, new JoinRejected { Reason = $"Protocol mismatch (server {Protocol.Version}, client {join.ProtocolVersion})." });
            return;
        }

        if (!string.IsNullOrEmpty(_config.ServerPassword) && join.Password != _config.ServerPassword)
        {
            SendTo(connectionId, new JoinRejected { Reason = "Invalid server password." });
            return;
        }

        var name = string.IsNullOrWhiteSpace(join.PlayerName) ? $"player_{connectionId}" : join.PlayerName.Trim();
        if (name.Length > MaxPlayerNameLength)
        {
            name = name.Substring(0, MaxPlayerNameLength); // cap a client-supplied name so it can't be a multi-KB blob (persisted + broadcast in presence)
        }

        if (_config.WhitelistEnabled && !_config.Whitelist.Contains(name))
        {
            SendTo(connectionId, new JoinRejected { Reason = "You are not on the whitelist." });
            return;
        }

        int joinedCount = _sessions.Values.Count(s => s.Joined);
        if (joinedCount >= _config.MaxPlayers)
        {
            SendTo(connectionId, new JoinRejected { Reason = "Server is full." });
            return;
        }

        // Name reservation: one live session per name — PlayerId == name, so a second client under
        // the same name would alias (and corrupt) the same player state.
        bool de = NormalizeLocale(join.Locale) == "de";
        if (_sessions.Values.Any(s => s.Joined && string.Equals(s.State.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SendTo(connectionId, new JoinRejected
            {
                Reason = de
                    ? $"Der Name '{name}' ist auf diesem Server gerade online."
                    : $"The name '{name}' is already online on this server.",
            });
            return;
        }

        var state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);

        // Name verification: the first join under a name claims it with the client's per-install token;
        // later joins must present the matching token (protects the host/admin identity from spoofing).
        // Unclaimed records (legacy saves / tokenless clients) adopt the first token they see.
        string tokenHash = HashNameToken(join.Token);
        if (!string.IsNullOrEmpty(state.NameTokenHash) && state.NameTokenHash != tokenHash)
        {
            SendTo(connectionId, new JoinRejected
            {
                Reason = de
                    ? $"Der Name '{name}' gehört einem anderen Spieler (Namens-Verifikation)."
                    : $"The name '{name}' belongs to another player (name verification).",
            });
            return;
        }

        if (string.IsNullOrEmpty(state.NameTokenHash) && !string.IsNullOrEmpty(tokenHash))
        {
            state.NameTokenHash = tokenHash;
            _repo.SavePlayer(state); // persist the claim immediately, not only on the next save cycle
        }

        // A configured admin name is granted the Admin role (the world creator keeps WorldAdmin).
        if (state.Role != PlayerRole.WorldAdmin && _config.AdminPlayers.Contains(name))
        {
            state.Role = PlayerRole.Admin;
        }

        // Return the player to the body they were last on (persisted per-player), not always the home world.
        // Ensure that body's world is resident + the active cursor before placing them + sending world data.
        var (joinBody, joinBodyType) = RestoreJoinBody(state);
        LoadWorld(joinBodyType, joinBody);

        var session = new PlayerSession(connectionId, state) { Joined = true, CurrentLocationId = joinBody, Locale = NormalizeLocale(join.Locale) };
        _sessions[connectionId] = session;
        SetupPlayerShip(session); // give the player their own ship, stamped into their world
        EnsureSafeSpawn(session); // self-heal a position persisted mid-fall (don't load them into the void)
        ApplyCreativeGrants(session); // singleplayer "Creative" world: unlock-all / all-ships / starter kit

        var (systemName, planetName) = ActiveLocationNames();
        SendTo(connectionId, new JoinAccepted
        {
            PlayerId = state.PlayerId,
            WorldSeed = _meta.Seed,
            PlanetType = joinBodyType,
            PlanetName = planetName,
            SystemName = systemName,
            CumulativePlaytimeSeconds = _meta.CumulativePlaytimeSeconds,
        });
        SendInventory(session);
        SendPlayerState(session);
        SendRules(session);
        SendShipCombatStatus(session);
        SendLandedShips(session); // every parked ship object on the join world
        SendShipPlacement(session);
        SendShipStations(session);
        SendPlanetPois(session);
        SendOwnedShips(session);
        SendEnvironment(session);
        PopulateCreaturesNear(state, CreatureCapPerPlayer); // seed fauna so the world feels alive on entry
        SpawnCompanionsForSession(session); // re-materialise the player's pets if they joined onto their companions' home world
        SpawnSpeedersForSession(session); // re-materialise the player's deployed hover speeders on the join world
        SendCreatures(session);
        SendCompanions(session); // the player's full companion roster (for the Companions menu tab)
        SendDoors(session);
        SendDataCubes(session);   // minigame download cubes on the join world
        SendNetFragments(session); // story net fragments on the join world (P2)
        SendGameUnlocks(session); // the player's downloaded-games collection (per-player, persisted)
        SendBeacons(session);
        SendBeams(session); // placed beam blocks (teleporter pads) on the join world
        SendBases(session); // player-founded bases on the join world (Grundstein markers)
        SendAllianceList(session); // the player's alliance roster (shared station/base access + Funk tab)
        SendStoryStateOnJoin(session); // story meter + per-player beat catch-up (P0)
        SendLandingPads(session);
        SendContainers(session);
        SendExistingPresences(session); // show already-online players to the newcomer
        SendExistingFaces(session);     // custom pixel faces of already-online players
        ShipAiOnJoin(session); // boot VEGA: onboarding intro / veteran skip / resume objective

        _log.Info($"Player '{name}' joined (connection {connectionId}).");
    }

    /// <summary>SHA-256 hex of a join token; empty/missing token → empty hash (name stays unclaimed).</summary>
    private static string HashNameToken(string? token)
        => string.IsNullOrEmpty(token)
            ? string.Empty
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private PlayerState CreateNewPlayer(string name)
    {
        int spawnX = 0, spawnZ = 0;
        if (Rules.PersonalLandingZones && _landingPads.Count > 0)
        {
            // First spawn: drop the new player on the first free landing pad of the home body (item 38).
            int idx = FirstFreePadIndex(_world.LocationId, _landingPads.Count, name);
            var pad = _landingPads[idx >= 0 ? idx : 0];
            spawnX = pad.CenterX;
            spawnZ = pad.CenterZ;
        }

        int surfaceY = PadGroundY(spawnX, spawnZ); // median footprint height — same level the ship stamps at
        var spawn = new Vector3f(spawnX + 0.5f, surfaceY + 2f, spawnZ + 0.5f);
        var state = new PlayerState
        {
            PlayerId = name,
            Name = name,
            Position = spawn,
            RespawnPoint = _shipPlaced ? _healTank : spawn, // the heal-tank in the ship's Medbay
            AboardShip = true,
            // The very first player to join becomes the world admin (world creator).
            Role = _repo.ListPlayerIds().Count == 0
                ? PlayerRole.WorldAdmin
                : (_config.AdminPlayers.Contains(name) ? PlayerRole.Admin : PlayerRole.Player),
        };

        // Starter kit: a basic drill and a hand scanner in the first hotbar slots, plus a suit lamp so the
        // player can light up caves / the ship at night (toggle with L). Blocks are placed directly — select
        // a block item and right-click — so there is no separate "block placer" tool.
        state.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        state.Inventory.SetSlot(1, new ItemStack("hand_scanner", 1));
        state.Inventory.SetSlot(2, new ItemStack("suit_lamp", 1));
        state.Inventory.SetSlot(3, new ItemStack("machete", 1));       // a simple starter melee weapon
        state.Inventory.SetSlot(4, new ItemStack("scrap_pistol", 1));   // ...and a weak ranged sidearm so a fresh
                                                                        // player can fight back from a distance, not
                                                                        // only by walking into a hostile's bite range

        // Starter food so a fresh pilot can't starve before discovering the food loop: a few berries to eat by
        // hand straight away (VEGA's "eat" lesson points here), plus emergency rations pre-loaded into the suit
        // dispenser so the low-hunger auto-feed safety net works from the first minute, not only once they craft one.
        state.Inventory.SetSlot(6, new ItemStack("berries", 5));
        state.RationStore.SetSlot(0, new ItemStack("emergency_ration", 2));
        _repo.SavePlayer(state);
        return state;
    }

    /// <summary>A curated "Creative" starter set (singleplayer): a couple of better tools + generous stacks of
    /// the key materials/ores/components so you can build right away. Survival mechanics still apply, so this is a
    /// head start, not infinite resources. Unknown keys are skipped. (Inventory + ship cargo absorb the stacks.)</summary>
    private static readonly (string Item, int Count)[] CreativeKit =
    {
        ("titanium_drill", 1), ("advanced_scanner", 1),
        ("iron_ore", 99), ("copper_ore", 99), ("titanium_ore", 99), ("silicate", 99), ("carbon", 99),
        ("iron_ingot", 99), ("iron_plate", 99), ("titanium_plate", 99), ("steel", 99), ("light_alloy", 99),
        ("metal_panel", 99), ("copper_wire", 99), ("cable", 99), ("circuit_board", 99), ("carbon_composite", 99),
        ("energy_cell_1", 99), ("glass", 99), ("data_fragment", 99),
        ("iron_wall", 99), ("stone", 99), ("station_core", 8),
    };

    /// <summary>Applies the world's chosen singleplayer "Creative" options to a (re)joining player: unlock every
    /// blueprint, own every ship type, and — once — grant the curated starter kit. Blueprints + ships are
    /// idempotent so they reapply cleanly on every load (which also rebuilds the in-memory fleet). Survival rules
    /// are untouched (the player chose "head start", not no-mechanics).</summary>
    private void ApplyCreativeGrants(PlayerSession session)
    {
        if (!_meta.CreativeUnlockAllBlueprints && !_meta.CreativeStartAllShips && !_meta.CreativeStarterKit)
        {
            return; // an Explorer world — nothing to grant
        }

        Serve(session); // point the ship/world cursors at this player before granting
        var p = session.State;

        if (_meta.CreativeUnlockAllBlueprints)
        {
            bool changed = false;
            foreach (var key in _content.Blueprints.Keys)
            {
                changed |= p.UnlockedBlueprints.Add(key);
            }

            if (changed)
            {
                _repo.SavePlayer(p);
            }

            UnlockAllGames(session); // Creative: also recover every data fragment (minigame) so they can be tested
        }

        if (_meta.CreativeStartAllShips)
        {
            var owned = new HashSet<string>(session.Ships.Values.Select(s => s.ShipType));
            foreach (var def in _content.Ships.Values)
            {
                if (def.Key != "starter" && owned.Add(def.Key))
                {
                    AddOwnedShipFromDefinition(def, "creative");
                }
            }
        }

        if (_meta.CreativeStarterKit && !_meta.CreativeKitGranted)
        {
            var pool = new MaterialPool(_content, p, _ship);
            foreach (var (item, count) in CreativeKit)
            {
                if (_content.GetItem(item) is not null)
                {
                    pool.Add(item, count);
                }
            }

            _meta.CreativeKitGranted = true;
            _repo.SaveMetadata(_meta);
            _repo.SavePlayer(p); // persist the granted kit so a reload keeps it (and the one-time flag holds)
            SendInventory(session);
        }
    }

    /// <summary>
    /// Adds a fully-joined player session without a network handshake, using a synthetic
    /// (negative) connection id. Used by singleplayer/local co-op and by multi-player server
    /// tests, since the loopback transport only models a single networked client. The caller
    /// drives this player's actions through the authoritative server methods directly.
    /// </summary>
    public PlayerSession AddLocalPlayer(string name, string locale = "en")
    {
        var state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);

        if (state.Role != PlayerRole.WorldAdmin && _config.AdminPlayers.Contains(name))
        {
            state.Role = PlayerRole.Admin;
        }

        int connectionId = _nextLocalConnectionId--;

        // Return the player to the body they were last on (persisted); home/default for a fresh player.
        var (joinBody, joinBodyType) = RestoreJoinBody(state);
        LoadWorld(joinBodyType, joinBody);

        var session = new PlayerSession(connectionId, state) { Joined = true, CurrentLocationId = joinBody, Locale = NormalizeLocale(locale) };
        _sessions[connectionId] = session;
        SetupPlayerShip(session); // local/test players get their own ship too
        EnsureSafeSpawn(session); // self-heal a position persisted mid-fall (don't load them into the void)
        ApplyCreativeGrants(session); // singleplayer "Creative" world: unlock-all / all-ships / starter kit
        return session;
    }

    /// <summary>Runs the authoritative mine validator for a player until the block breaks (used by local
    /// play / tests). Hard blocks now need several drill hits, so this applies hits up to a safe cap.</summary>
    public void MineBlock(string playerId, int x, int y, int z)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return;
        }

        var pos = new Vector3i(x, y, z);
        for (int i = 0; i < 32 && !_world.GetBlock(pos).IsAir; i++)
        {
            HandleMine(session, new MineBlockIntent { X = x, Y = y, Z = z });
        }
    }

    /// <summary>Places a block from a held item for a player (test/util entrypoint). An optional label rides
    /// along for labelled blocks (a radio beacon).</summary>
    public void PlaceBlock(string playerId, int x, int y, int z, string itemKey, string? label = null)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandlePlace(session, new PlaceBlockIntent { X = x, Y = y, Z = z, ItemKey = itemKey, Label = label ?? string.Empty });
        }
    }

    /// <summary>Applies a single mining hit (for tests that need to observe per-hit progress).</summary>
    public void MineBlockOnce(string playerId, int x, int y, int z)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleMine(session, new MineBlockIntent { X = x, Y = y, Z = z });
        }
    }

    /// <summary>Runs the authoritative craft validator for a player (used by local play / tests).</summary>
    public void Craft(string playerId, string recipeKey, int count = 1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            HandleCraft(session, new CraftIntent { RecipeKey = recipeKey, Count = count });
        }
    }

    /// <summary>Runs the always-available "Shape" action for a player (used by local play / tests): re-forms a
    /// held building material into another geometric shape, like <see cref="Craft"/> for the dye/glow action.</summary>
    public void ShapeCraft(string playerId, string sourceItemKey, int shape, int count = 1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
            HandleShapeCraft(session, new ShapeCraftIntent { SourceItemKey = sourceItemKey, Shape = shape, Count = count });
        }
    }

    /// <summary>Runs the authoritative blueprint-unlock validator for a player (used by local play / tests).</summary>
    public void UnlockBlueprint(string playerId, string blueprintKey)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleUnlock(session, new UnlockBlueprintIntent { BlueprintKey = blueprintKey });
        }
    }

    /// <summary>Sends a chat line as a player through the real handler (used by local play / tests): exercises
    /// the radio gate, rate limit and tiered reach.</summary>
    public void Chat(string playerId, string text)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleChat(session, new ChatIntent { Text = text });
        }
    }

    /// <summary>Relays a voice frame as a player through the real handler (used by local play / tests).</summary>
    public void SendVoice(string playerId, byte[] opus, int sequence)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleVoice(session, new VoiceFrame { Opus = opus, Sequence = sequence });
        }
    }

    // ---------------- Authoritative validators ----------------

    private void HandleMove(PlayerSession session, MoveIntent move)
    {
        // MVP: trust position but clamp to sane finite values. (Full movement validation later.)
        if (float.IsFinite(move.X) && float.IsFinite(move.Y) && float.IsFinite(move.Z))
        {
            // ROUND WORLDS: the client transform runs unbounded as it laps the world in any direction; the
            // authoritative position is canonical — X in [0, Circumference), Z in the latitude domain
            // (±period/2, period ≈ circumference/2). The old pole clamp is gone: north–south wraps seamlessly
            // like east–west. Stations/space keep their own small coordinate space (no wrap there).
            int circ = _world.Circumference; // this world's size (asteroids small, planets large)
            float z = move.Z;
            if (!InStation(session.State.PlayerId) && !InSpace(session.State.PlayerId))
            {
                z = (float)WorldConstants.WrapZ((double)move.Z, circ);
            }

            session.State.Position = new Vector3f((float)WorldConstants.WrapX(move.X, circ), move.Y, z);
            session.State.Yaw = move.Yaw;
            session.State.Pitch = move.Pitch;
            UpdateDrivingSpeeder(session); // if driving a speeder, slave it to this pose + drain its energy cell
        }
    }

    // Accumulated mining effort per block (a hard block needs several hits before it breaks).
    // Mining progress per cell, tagged with the block it belongs to — so if a cell's block changes (flora
    // regrowth, fluid flow, a structure stamp, a placed block) the leftover progress doesn't carry to the NEW
    // block and one-shot it. A block of a given hardness then always takes the same number of hits (B52).
    private readonly Dictionary<Vector3i, (ushort Block, float Progress)> _miningProgress = new();

    private const float FallSafeImpactSpeed = 14f;  // matches the client; below this a landing is harmless
    private const float FallDamagePerSpeed = 4.5f;  // health lost per unit of impact speed over the safe cap

    /// <summary>Applies fall damage from a hard landing the client reported (it owns on-foot movement),
    /// scaled by how far over a safe impact speed it was and reduced by armor. A lethal fall respawns the
    /// player at the heal-tank (with the death flag → the client's death flash).</summary>
    private void HandleFallDamage(PlayerSession session, FallDamageIntent intent)
    {
        var p = session.State;
        if (InSpace(p.PlayerId) || !float.IsFinite(intent.ImpactSpeed))
        {
            return; // piloting in space — there is no on-foot fall to take
        }

        float over = intent.ImpactSpeed - FallSafeImpactSpeed;
        if (over <= 0f)
        {
            return;
        }

        float damage = Mitigate(p, System.Math.Min(120f, over * FallDamagePerSpeed));
        if (damage <= 0f)
        {
            return;
        }

        p.Health = System.Math.Max(0f, p.Health - damage);
        if (p.Health <= 0f)
        {
            RespawnPlayer(session, "You did not survive the fall.");
        }
        else
        {
            SendPlayerState(session);
        }
    }

    private void HandleMine(PlayerSession session, MineBlockIntent mine)
    {
        // Longitude wraps: canonicalize X up front so reach, protection, mining progress and the broadcast
        // all agree, whatever lap the client's unbounded transform reported the block from. MUST use THIS
        // world's circumference: the no-arg default (6000) silently mapped every block intent beyond
        // X=6000 onto a column thousands of blocks away on bigger worlds — "cannot mine anything".
        var pos = WorldConstants.CanonicalBlock(new Vector3i(mine.X, mine.Y, mine.Z), _world.Circumference);

        // Outside the legal build band there is no world to touch — drop it without loading/caching a chunk
        // there (a spoofed-Y mining spam would otherwise generate chunks at arbitrary heights). See MinBuildY.
        if (!WithinBuildHeight(pos.Y))
        {
            return;
        }

        // A player-built door fills an air cell as an entity — mining it removes the door + returns the item.
        if (RemovePlayerDoorAt(session, pos))
        {
            return;
        }

        var current = _world.GetBlock(pos);
        if (current.IsAir)
        {
            // The client aimed at a block here but the server has air — its chunk view is STALE (a ghost block).
            // Heal SILENTLY: the resync sends the corrective BlockChanged + re-streams the chunk, the client's
            // voxel world fixes itself and the held drill simply hits the real block on its next tick. The old
            // "Block is already empty." reject read as "mining is broken" to players and added nothing — the
            // heal is the fix either way. Log the spot so the actual ghost SOURCE can be identified from
            // reports (a SetBlock somewhere that skipped its broadcast).
            ResyncStaleChunk(session, pos);
            _log.Warn($"Ghost block healed at {pos.X},{pos.Y},{pos.Z} for '{session.State.Name}' (client saw a block, server has air).");
            return;
        }

        if (IsShipBlock(pos))
        {
            Reject(session, "mine", "The ship hull cannot be mined.");
            return;
        }

        if (IsSettlementBlock(pos))
        {
            Reject(session, "mine", "This settlement is protected.");
            return;
        }

        if (IsStationBlock(pos))
        {
            Reject(session, "mine", "This station is protected.");
            return;
        }

        if (IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "mine", "This base is protected — ally with its owner to build here.");
            return;
        }

        var def = _world.Definition(current);
        if (def is null || !def.Mineable)
        {
            Reject(session, "mine", "Block cannot be mined.");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "mine", "Out of reach.");
            return;
        }

        var tool = ActiveTool(session.State);
        if (!ToolCanMine(tool, def))
        {
            Reject(session, "mine", "Your current tool cannot mine this block.");
            return;
        }

        // Harder blocks need more drill effort; stronger drills apply more per hit. Soft blocks
        // (mud/dirt) break in one hit; hard ones (stone/metal/ore) take several. Accumulate until break.
        float hardness = System.Math.Max(0.2f, def.Hardness);
        float power = tool.MiningPower > 0f ? tool.MiningPower : 1f;
        // Only keep prior progress if it was for THIS same block (else a replaced block starts fresh — B52).
        float prior = _miningProgress.TryGetValue(pos, out var prev) && prev.Block == current.Value ? prev.Progress : 0f;
        float progress = prior + power;

        if (progress + 0.0001f < hardness)
        {
            _miningProgress[pos] = (current.Value, progress);
            Send(session, new MiningProgress { X = pos.X, Y = pos.Y, Z = pos.Z, Fraction = progress / hardness });
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        BreakBlockAt(session, pos, def, pool);

        // Powerful drills clear a small area at once.
        if (tool.MiningRadius > 0)
        {
            BreakArea(session, pos, tool.MiningRadius, pool);
        }

        SendInventory(session);
    }

    /// <summary>Breaks one block: clears it, banks its drops in the pool, broadcasts the change,
    /// schedules flora regrowth and advances mining missions. Clears any accumulated mining progress.</summary>
    private void BreakBlockAt(PlayerSession session, Vector3i pos, BlockDefinition def, MaterialPool pool)
    {
        var current = _world.GetBlock(pos);
        var (dropTint, dropGlow) = _world.GetModifier(pos); // read the dye/glow BEFORE clearing, to recover it into the drop
        int dropShape = ShapeCode.ShapeOf(_world.GetShape(pos)); // recover the FORM (orientation is re-derived on re-place)
        _world.SetBlock(pos, BlockId.Air);
        _miningProgress.Remove(pos);

        if (def.Key == "crate")
        {
            RemoveCrateContainer(pos, pool); // mining a crate returns its stored contents (Task 5 Stage 3b)
        }
        else if (def.Key == "radio_beacon")
        {
            RemoveBeaconAt(pos); // mining a beacon forgets its label/marker (item 37)
        }
        else if (def.Key == "base_core")
        {
            RemoveBaseAt(pos); // mining a base core removes the founded base (Grundstein)
        }
        else if (def.Key == "beam_block")
        {
            RemoveBeamAt(pos); // mining a beam block forgets its name/owner + map marker (teleporter pad)
        }

        // A toxic flora species yields poisonous berries instead of edible ones (the scan warns which is which).
        bool toxicFlora = IsFlora(current.Value)
            && _floraSpeciesByBlock.TryGetValue(current.Value, out var fsp) && fsp.Toxic;
        foreach (var drop in def.Drops)
        {
            string item = toxicFlora && drop.Item == "berries" ? "toxic_berries" : drop.Item;
            // If this cell was dyed/glowing/shaped and the drop is the block itself, return it still coloured + formed.
            if ((dropTint != 0 || dropGlow != 0 || dropShape != 0) && _content.GetItem(item)?.PlacesBlock == def.Key)
            {
                item = ItemKey.Compose(item, dropTint, dropGlow, dropShape);
            }

            pool.Add(item, drop.Count);
        }

        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue });
        if (IsFlora(current.Value))
        {
            ScheduleFloraRegrow(pos, current.Value); // regrows if the host stays intact
        }

        // Wake adjacent fluid so a hole opened in or under a body of water/lava refills — whether the mined
        // block was the fluid itself or a rock/kelp surrounded by it (a finite pool still drains to its last cells).
        if (IsFluid(current.Value) || HasFluidNeighbor(pos))
        {
            OnFluidRemoved(pos);
        }

        OnBlockMined(session, def.Key);
        ShipAiOnMine(session); // VEGA onboarding: the "mine a few blocks" stage counts every break
    }

    /// <summary>Area mining for powerful drills: breaks the mineable, unprotected blocks around a centre.</summary>
    private void BreakArea(PlayerSession session, Vector3i center, int radius, MaterialPool pool)
    {
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    var p = new Vector3i(center.X + dx, center.Y + dy, center.Z + dz);
                    var b = _world.GetBlock(p);
                    if (b.IsAir || IsShipBlock(p) || IsSettlementBlock(p) || IsStationBlock(p)
                        || IsBaseProtected(p, session.State.PlayerId, session.State.IsAdmin))
                    {
                        continue;
                    }

                    var d = _world.Definition(b);
                    if (d is null || !d.Mineable)
                    {
                        continue;
                    }

                    BreakBlockAt(session, p, d, pool);
                }
    }

    private void HandlePlace(PlayerSession session, PlaceBlockIntent place)
    {
        var item = _content.GetItem(place.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "place", "Item cannot be placed.");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "place", "Unknown block for item.");
            return;
        }

        var pos = WorldConstants.CanonicalBlock(new Vector3i(place.X, place.Y, place.Z), _world.Circumference); // wraps at THIS world's seam

        // Reject before touching the world: a block edit outside the build band would generate + persist a
        // chunk at an arbitrary height (unbounded RAM/disk DoS from a spoofed-position place spam). See MinBuildY.
        if (!WithinBuildHeight(pos.Y))
        {
            Reject(session, "place", "Out of the buildable height range.");
            return;
        }

        if (!_world.GetBlock(pos).IsAir)
        {
            Reject(session, "place", "Target is not empty.");
            return;
        }

        // Don't let the player wall themselves in: refuse a block at HEAD height in their own column. The FEET
        // cell is allowed so you can pillar-jump (place under yourself while jumping) — the client collider just
        // lifts you onto the new block (B3); only the head cell would trap you.
        var feet = session.State.Position;
        int fx = (int)System.Math.Floor(feet.X), fy = (int)System.Math.Floor(feet.Y), fz = (int)System.Math.Floor(feet.Z);
        if (pos.X == fx && pos.Z == fz && pos.Y == fy + 1)
        {
            Reject(session, "place", "You can't place a block right above your head.");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "place", "Out of reach.");
            return;
        }

        if (!session.State.IsAdmin && IsOnLandingPad(pos))
        {
            Reject(session, "place", "Landing pads are reserved — you can't build on one.");
            return;
        }

        if (IsStationBlock(pos))
        {
            Reject(session, "place", "This station is protected.");
            return;
        }

        if (IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "place", "This base is protected — ally with its owner to build here.");
            return;
        }

        // No building inside the ship — the cabin is a fixed structure.
        if (ShipInteriorContains(new Vector3f(pos.X, pos.Y, pos.Z)))
        {
            Reject(session, "place", "You can't build inside the ship.");
            return;
        }

        // Seeds / flora only take on a suitable host block (mud, grass, crystal, ...).
        if (IsFlora(blockDef.NumericId.Value))
        {
            if (!IsValidFloraHost(blockDef.NumericId.Value, pos))
            {
                Reject(session, "place", "This plant needs suitable ground beneath it.");
                return;
            }

            // On a space station (void world) a plant must sit fully inside the hull — solid block below and no
            // side open to space — so it can't be seen or walked through into the void.
            if (!IsFloraEnclosedForVoidWorld(pos))
            {
                Reject(session, "place", "Plants can only be placed inside a station, never against open space.");
                return;
            }
        }

        // A base core founds a player base (Grundstein) — only on a real surface, and only one per body per player.
        // Checked before any material is consumed so a refused founding costs nothing.
        if (blockDef.Key == "base_core")
        {
            var hereBody = _galaxy?.FindBody(_world.LocationId);
            if (hereBody is null
                || (hereBody.Kind != CelestialKind.Planet && hereBody.Kind != CelestialKind.Moon && hereBody.Kind != CelestialKind.AsteroidField))
            {
                Reject(session, "place", "You can only found a base on a planet, moon or asteroid.");
                return;
            }

            if (PlayerHasBaseOn(session.State.PlayerId, _world.LocationId))
            {
                Reject(session, "place", "You already have a base on this body — mine its core to move it.");
                return;
            }
        }

        // Creative mode and admin instant-build place without consuming materials.
        bool free = !Rules.CraftingCostsMaterials || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(place.ItemKey) < 1)
            {
                Reject(session, "place", "You do not have that block.");
                return;
            }

            pool.Remove(new[] { new ItemAmount(place.ItemKey, 1) });
        }

        // A door isn't a voxel block — it fills the (air) cell as a server door entity (Task 5 Stage 3c).
        if (blockDef.Key == "door_hinge" || blockDef.Key == "door_slide")
        {
            PlaceDoor(session, pos, blockDef.Key == "door_slide" ? "slide" : "hinge");
            SendInventory(session);
            return;
        }

        // A dyed/glowing block carries its colour in the item key; stamp it on the placed cell. Only honour
        // it for tintable building materials (the colour came from the always-available dye/glow action).
        int placeTint = 0, placeGlow = 0;
        if (blockDef.Tintable && ItemKey.HasModifier(place.ItemKey))
        {
            placeTint = ItemKey.Tint(place.ItemKey);
            placeGlow = ItemKey.Glow(place.ItemKey);
        }

        // A shaped block carries its FORM in the item key; the placement ORIENTATION is derived from the
        // player's facing (yaw quantized to one of the four cardinal directions). Together they pack into the
        // per-voxel shape descriptor. Only shapeable building materials honour a shape.
        int placeShape = 0;
        if (blockDef.Shapeable)
        {
            int shapeIndex = ItemKey.Shape(place.ItemKey);
            if (ShapeCode.IsValidShape(shapeIndex))
            {
                int facing = ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;
                placeShape = ShapeCode.Pack(shapeIndex, facing);
            }
        }

        _world.SetBlock(pos, blockDef.NumericId, placeTint, placeGlow, placeShape);

        if (blockDef.Key == "crate")
        {
            PlaceCrate(pos); // a placed storage crate becomes a lootable/stash-able container (Task 5 Stage 3b)
        }
        else if (blockDef.Key == "radio_beacon")
        {
            PlaceBeacon(session, pos, place.Label); // a placed beacon becomes a labelled map/compass waypoint (item 37)
        }
        else if (blockDef.Key == "base_core")
        {
            PlaceBase(session, pos); // a placed base core founds a named planet base (Grundstein)
        }
        else if (blockDef.Key == "beam_block")
        {
            PlaceBeam(session, pos, place.Label); // a placed beam block becomes a named teleporter pad
        }

        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = blockDef.NumericId.Value, Tint = placeTint, Glow = placeGlow, Shape = placeShape });
        if (IsFluid(blockDef.NumericId.Value))
        {
            RegisterFluidSource(pos); // placed water/lava starts flowing
        }

        SendInventory(session);
    }

    private void HandleCraft(PlayerSession session, CraftIntent craft)
    {
        var recipe = _content.GetRecipe(craft.RecipeKey);
        if (recipe is null)
        {
            Reject(session, "craft", "Unknown recipe.");
            return;
        }

        int count = System.Math.Clamp(craft.Count, 1, 999); // bound the batch size (avoid input*count overflow)

        // Creative mode: no material/blueprint/station cost — just produce the output.
        if (!Rules.CraftingCostsMaterials)
        {
            var freePool = new MaterialPool(_content, session.State, _ship);
            foreach (var output in recipe.Outputs)
            {
                freePool.Add(output.Item, output.Count * count);
            }

            Send(session, new CraftResult { Success = true, RecipeKey = recipe.Key });
            SendInventory(session);
            return;
        }

        if (!string.IsNullOrEmpty(recipe.RequiredBlueprint) &&
            !session.State.UnlockedBlueprints.Contains(recipe.RequiredBlueprint!))
        {
            CraftFail(session, recipe.Key, "Blueprint not unlocked.");
            return;
        }

        if (!StationAvailable(session.State, recipe.Station))
        {
            CraftFail(session, recipe.Key, "Required crafting station is not available here.");
            return;
        }

        // Market barter is themed per VENDOR (B55): each vendor posts the goods of its own profession, so different
        // vendors at one settlement/station offer different deals (and station vendors can post themed goods, not
        // just the themeless ones). Themeless market recipes trade anywhere (every vendor + the ship's own console).
        if (recipe.Station == CraftingStation.Market && !string.IsNullOrEmpty(recipe.MarketTheme))
        {
            string vendorTheme = VendorThemeAt(session.State);
            if (!string.Equals(vendorTheme, recipe.MarketTheme, System.StringComparison.OrdinalIgnoreCase))
            {
                CraftFail(session, recipe.Key, "This trade isn't offered here.");
                return;
            }
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        var scaledInputs = recipe.Inputs.Select(i => new ItemAmount(i.Item, i.Count * count)).ToList();
        if (!pool.Has(scaledInputs))
        {
            CraftFail(session, recipe.Key, "Missing materials.");
            return;
        }

        pool.Remove(scaledInputs);
        foreach (var output in recipe.Outputs)
        {
            pool.Add(output.Item, output.Count * count);
        }

        // Bartering at a settlement/station market stall is a trade with that vendor NPC — remembered (item 14).
        if (recipe.Station == Shared.Definitions.CraftingStation.Market && !session.State.AboardShip)
        {
            RecordVendorTrade(session.State);
            ShipAiOnTradeOrMission(session); // VEGA onboarding: a vendor barter counts as the first trade
        }

        Send(session, new CraftResult { Success = true, RecipeKey = recipe.Key });
        SendInventory(session);
        ShipAiOnCraft(session); // VEGA onboarding: first successful craft
    }

    /// <summary>
    /// The always-available "Dye"/"Glow" action: turn a held building material into a coloured (and/or
    /// glowing) variant of itself. The output is the same item with the colour encoded in its key
    /// (<see cref="ItemKey"/>), so it stacks separately and, when placed/mined, carries the colour through.
    /// Dyeing is a free 1:1 recolour (no station, no dye item); a glow variant additionally consumes a
    /// luminescent <c>crystal</c> per unit. Only tintable materials qualify.
    /// </summary>
    private void HandleTintCraft(PlayerSession session, TintCraftIntent intent)
    {
        string baseKey = ItemKey.Base(intent.SourceItemKey);
        var item = _content.GetItem(baseKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            CraftFail(session, "tint", "That item can't be coloured.");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null || !blockDef.Tintable)
        {
            CraftFail(session, "tint", "That material can't be coloured.");
            return;
        }

        int tint = intent.Tint & 0xFFFFFF;
        int glow = intent.Glow & 0xFFFFFF;
        if (tint == 0 && glow == 0)
        {
            CraftFail(session, "tint", "No colour chosen.");
            return;
        }

        int count = System.Math.Clamp(intent.Count, 1, 999);
        // Preserve any shape the source already carried — colouring a shaped block keeps its form.
        string output = ItemKey.Compose(baseKey, tint, glow, ItemKey.Shape(intent.SourceItemKey));

        // Creative mode: no material cost — just produce the coloured material.
        if (!Rules.CraftingCostsMaterials)
        {
            new MaterialPool(_content, session.State, _ship).Add(output, count);
            Send(session, new CraftResult { Success = true, RecipeKey = "tint" });
            SendInventory(session);
            return;
        }

        // Consume the chosen source stack (its exact key — recolouring an already-dyed item works too) plus,
        // for a glowing variant, one crystal per unit as the luminescent core.
        var pool = new MaterialPool(_content, session.State, _ship);
        var inputs = new List<ItemAmount> { new ItemAmount(intent.SourceItemKey, count) };
        if (glow != 0)
        {
            inputs.Add(new ItemAmount("crystal", count));
        }

        if (!pool.Has(inputs))
        {
            CraftFail(session, "tint", glow != 0 ? "Need the material and a crystal." : "Missing material.");
            return;
        }

        pool.Remove(inputs);
        pool.Add(output, count);
        Send(session, new CraftResult { Success = true, RecipeKey = "tint" });
        SendInventory(session);
        ShipAiOnCraft(session);
    }

    /// <summary>
    /// The always-available "Shape" action: re-form a held building material into another geometric shape
    /// (sphere, dome, pyramid, ramp, …) that still behaves like a block. The output is the same item with the
    /// shape index encoded in its key (<see cref="ItemKey"/>), preserving any colour the source already
    /// carried, so it stacks separately and carries the form through place/mine. Free 1:1 (no station, no extra
    /// item), like dyeing. Only shapeable materials qualify; <c>Shape == 0</c> re-forms back to a plain cube.
    /// The placement ORIENTATION isn't chosen here — it's derived from the player's facing when the block is set.
    /// </summary>
    private void HandleShapeCraft(PlayerSession session, ShapeCraftIntent intent)
    {
        string baseKey = ItemKey.Base(intent.SourceItemKey);
        var item = _content.GetItem(baseKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            CraftFail(session, "shape", "That item can't be shaped.");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null || !blockDef.Shapeable)
        {
            CraftFail(session, "shape", "That material can't be shaped.");
            return;
        }

        int shape = intent.Shape;
        if (shape != 0 && !ShapeCode.IsValidShape(shape))
        {
            CraftFail(session, "shape", "Unknown shape.");
            return;
        }

        // Re-forming to the shape the source already has (incl. "cube" on a plain block) is a no-op.
        if (shape == ItemKey.Shape(intent.SourceItemKey))
        {
            CraftFail(session, "shape", "Already that shape.");
            return;
        }

        int count = System.Math.Clamp(intent.Count, 1, 999);
        // Only the form changes — keep whatever colour the source carried.
        string output = ItemKey.Compose(baseKey, ItemKey.Tint(intent.SourceItemKey), ItemKey.Glow(intent.SourceItemKey), shape);

        // Creative mode: no material cost — just produce the shaped material.
        if (!Rules.CraftingCostsMaterials)
        {
            new MaterialPool(_content, session.State, _ship).Add(output, count);
            Send(session, new CraftResult { Success = true, RecipeKey = "shape" });
            SendInventory(session);
            if (shape != 0) RevealShapeAnomalyMemory(session); // forming a non-cube → VEGA's "why we built blocky" memory
            return;
        }

        // Free 1:1: consume the exact source stack (re-shaping an already coloured/shaped item works too).
        var pool = new MaterialPool(_content, session.State, _ship);
        var inputs = new List<ItemAmount> { new ItemAmount(intent.SourceItemKey, count) };
        if (!pool.Has(inputs))
        {
            CraftFail(session, "shape", "Missing material.");
            return;
        }

        pool.Remove(inputs);
        pool.Add(output, count);
        Send(session, new CraftResult { Success = true, RecipeKey = "shape" });
        SendInventory(session);
        ShipAiOnCraft(session);
        if (shape != 0) RevealShapeAnomalyMemory(session); // forming a non-cube → VEGA's "why we built blocky" memory
    }

    /// <summary>Fraction of a crafted item's recipe inputs recovered when it is disassembled.</summary>
    private const float DisassemblyRecoveryRate = 0.5f;

    /// <summary>Dismantles one crafted item at a workshop, returning a portion of its recipe components.</summary>
    public void Disassemble(string playerId, string itemKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        // Find the crafting recipe that produces this item (so we know what it's made of).
        // Market (barter) recipes are trades, not construction — they must not make raw resources
        // look "disassemblable".
        RecipeDefinition? recipe = null;
        int perCraft = 1;
        foreach (var r in _content.Recipes.Values)
        {
            if (r.Station == CraftingStation.Market)
            {
                continue;
            }

            var output = r.Outputs.FirstOrDefault(o => o.Item == itemKey);
            if (output is not null && r.Inputs.Count > 0)
            {
                recipe = r;
                perCraft = System.Math.Max(1, output.Count);
                break;
            }
        }

        if (recipe is null)
        {
            Reject(session, "disassemble", "This item cannot be disassembled.");
            return;
        }

        if (!StationAvailable(session.State, CraftingStation.Workshop))
        {
            Reject(session, "disassemble", "A workshop is required to disassemble.");
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        if (pool.Count(itemKey) < 1)
        {
            Reject(session, "disassemble", "You don't have that item.");
            return;
        }

        pool.Remove(new[] { new ItemAmount(itemKey, 1) });
        foreach (var input in recipe.Inputs)
        {
            int recovered = (int)System.Math.Floor(input.Count * DisassemblyRecoveryRate / perCraft);
            if (recovered > 0)
            {
                pool.Add(input.Item, recovered);
            }
        }

        SendInventory(session);
    }

    private void HandleDisassemble(PlayerSession session, DisassembleIntent intent)
        => Disassemble(session.State.PlayerId, intent.ItemKey);

    private void HandleUnlock(PlayerSession session, UnlockBlueprintIntent unlock)
    {
        var bp = _content.GetBlueprint(unlock.BlueprintKey);
        if (bp is null)
        {
            Reject(session, "unlock", "Unknown blueprint.");
            return;
        }

        if (session.State.UnlockedBlueprints.Contains(bp.Key))
        {
            Reject(session, "unlock", "Already unlocked.");
            return;
        }

        foreach (var pre in bp.Prerequisites)
        {
            if (!session.State.UnlockedBlueprints.Contains(pre))
            {
                Reject(session, "unlock", "Prerequisite blueprint missing.");
                return;
            }
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.Has(bp.UnlockCost))
        {
            Reject(session, "unlock", "Missing research materials.");
            return;
        }

        if (session.State.KnowledgePoints < bp.KnowledgeCost)
        {
            Reject(session, "unlock", "Not enough knowledge — scan more to research this.");
            return;
        }

        // Knowledge is a permanent threshold (item 11): it gates the unlock but is never spent — only the
        // research materials are consumed. (Knowledge can also be taught to others without losing any.)
        pool.Remove(bp.UnlockCost);
        session.State.UnlockedBlueprints.Add(bp.Key);

        Send(session, new ServerMessage { Text = $"Blueprint unlocked: {bp.Key}" });
        SendInventory(session);
        ShipAiOnBlueprint(session); // VEGA onboarding: first blueprint researched
    }

    private void HandleAdminCommand(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;
        if (!p.IsAdmin)
        {
            Reject(session, "admin", "Only the world admin or admins may use cheats.");
            return;
        }

        // Admin content tooling (not a cheat): AI mission generation.
        if (string.Equals(cmd.Command, "ai_mission", StringComparison.OrdinalIgnoreCase))
        {
            var (ok, message) = TryGenerateAiMission(cmd.StringArg ?? string.Empty);
            Send(session, new ServerMessage { Text = message });
            CheatLog(p, ok ? $"generated an AI mission" : $"AI mission request: {message}");
            return;
        }

        if (!Rules.CheatsAllowed)
        {
            Reject(session, "admin", "Admin cheats are disabled on this server.");
            return;
        }

        switch (cmd.Command?.ToLowerInvariant())
        {
            case "give_item":
                {
                    if (_content.GetItem(cmd.StringArg ?? string.Empty) is null)
                    {
                        Reject(session, "admin", "Unknown item.");
                        return;
                    }

                    var target = FindSessionByName(cmd.TargetPlayer) ?? session;
                    int amount = System.Math.Max(1, cmd.IntArg);
                    new MaterialPool(_content, target.State, _ship).Add(cmd.StringArg!, amount);
                    SendInventory(target);
                    CheatLog(p, $"gave {amount} {cmd.StringArg} to {target.State.Name}");
                    break;
                }

            case "teleport_to_location":
                p.Position = new Vector3f(cmd.X, cmd.Y, cmd.Z);
                SendPlayerState(session);
                CheatLog(p, $"teleported to ({cmd.X:0.#}, {cmd.Y:0.#}, {cmd.Z:0.#})");
                break;

            case "teleport_to_player":
                {
                    var target = FindSessionByName(cmd.TargetPlayer);
                    if (target is null)
                    {
                        Reject(session, "admin", "Target player not found.");
                        return;
                    }

                    p.Position = target.State.Position;
                    SendPlayerState(session);
                    CheatLog(p, $"teleported to player {target.State.Name}");
                    break;
                }

            case "set_time":
                _timeOfDay = cmd.StringArg ?? _timeOfDay;
                Broadcast(new ServerMessage { Text = $"The world admin set the time to {_timeOfDay}." });
                CheatLog(p, $"set time to {_timeOfDay}");
                break;

            case "set_weather":
                _weather = cmd.StringArg ?? _weather;
                Broadcast(new ServerMessage { Text = $"The world admin set the weather to {_weather}." });
                CheatLog(p, $"set weather to {_weather}");
                break;

            case "fly":
                p.Fly = !p.Fly;
                Send(session, new ServerMessage { Text = $"Fly mode: {(p.Fly ? "on" : "off")}" });
                CheatLog(p, $"toggled fly to {p.Fly}");
                break;

            case "godmode":
                p.GodMode = !p.GodMode;
                Send(session, new ServerMessage { Text = $"God mode: {(p.GodMode ? "on" : "off")}" });
                CheatLog(p, $"toggled god mode to {p.GodMode}");
                break;

            case "instant_build":
                p.InstantBuild = !p.InstantBuild;
                Send(session, new ServerMessage { Text = $"Instant build: {(p.InstantBuild ? "on" : "off")}" });
                CheatLog(p, $"toggled instant build to {p.InstantBuild}");
                break;

            // ---- Story QA (P8 telemetry): jump around the arc for testing ----
            case "advance_story":
                {
                    int beats = AdminAdvanceStory(cmd.IntArg);
                    Send(session, new ServerMessage { Text = $"Story advanced — beats revealed: {beats}." });
                    CheatLog(p, $"advanced story by {System.Math.Max(1, cmd.IntArg)} (beats {beats})");
                    break;
                }

            case "reveal_finale":
                AdminRevealFinale();
                Send(session, new ServerMessage
                {
                    Text = _storyState.GuardianSystemRevealed
                        ? "Guardian finale system revealed on the star map."
                        : "No active story to reveal a finale for.",
                });
                CheatLog(p, "revealed the Guardian finale system");
                break;

            case "story_status":
                {
                    var snap = StorySnapshot;
                    Send(session, new ServerMessage
                    {
                        Text = $"Story '{snap.StoryId}': fragments={snap.Fragments}, kills={snap.Kills}, " +
                               $"milestones={snap.Milestones}, beats={snap.BeatsRevealed}/{(_story?.Beats.Count ?? 0)}, " +
                               $"finaleRevealed={_storyState.GuardianSystemRevealed}, defeated={snap.Defeated}",
                    });
                    break;
                }

            // ---- Finale QA: fit a ship module (e.g. the jump generator), reveal all lore, or drop into the core ----
            case "grant_module":
                {
                    var key = cmd.StringArg ?? string.Empty;
                    if (_ship is null)
                    {
                        Reject(session, "admin", "You have no active ship to fit a module to.");
                        return;
                    }

                    if (_content.GetShipModule(key) is null)
                    {
                        Reject(session, "admin", "Unknown ship module.");
                        return;
                    }

                    if (!_ship.HasModule(key))
                    {
                        _ship.Modules.Add(key);
                        ResizeCargo(_ship);
                        RecomputeShipCombatStats();
                        _repo.SaveShip(ShipSaveKey(p.PlayerId), _ship);
                        SendShipCombatStatus(session);
                        SendPlayerState(session);
                    }

                    Send(session, new ServerMessage { Text = $"Ship module fitted: {key}" });
                    CheatLog(p, $"fitted ship module {key}");
                    break;
                }

            case "reveal_lore":
                {
                    int n = AdminRevealAllLore(session);
                    Send(session, new ServerMessage { Text = $"Revealed all story lore ({n} fragments + every memory)." });
                    CheatLog(p, "revealed all story lore");
                    break;
                }

            case "goto_core":
                AdminGotoCore(session);
                Send(session, new ServerMessage
                {
                    Text = _storyState.GuardianSystemRevealed
                        ? "Dropped into the Guardian core chamber."
                        : "No active story to drop into a finale for.",
                });
                CheatLog(p, "teleported to the Guardian core chamber");
                break;

            default:
                Reject(session, "admin", "Unknown admin command.");
                break;
        }
    }

    private PlayerSession? FindSessionByName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.State.Name == name)
            {
                return s;
            }
        }

        return null;
    }

    private void CheatLog(PlayerState admin, string message)
        => _log.Info($"[CHEAT] Admin {admin.Name} {message}.");

    // ---------------- Helpers ----------------

    private bool WithinReach(PlayerState player, Vector3i block)
    {
        // The client aims an 8 m ray FROM THE CAMERA at a block FACE, while this check used to measure the
        // BODY position to the block CENTRE off a move stream that only updates at 10 Hz (unreliable) — three
        // stacked discrepancies (eye offset ~0.8, centre-vs-face up to ~0.87, movement lag ~1) that made
        // legitimate mines bounce with "Out of reach" (2026-06-10 bug). Measure to the nearest point of the
        // block instead — vertically against the player's body segment (anchor-agnostic), X the short way
        // round the longitude seam — with a small slack for the move-stream lag. HandleMove fully trusts the
        // reported position anyway, so this stays a sanity bound, not an anti-cheat wall.
        double dx = System.Math.Abs(WorldConstants.WrapDeltaX((block.X + 0.5) - player.Position.X, _world.Circumference));
        dx = System.Math.Max(0.0, dx - 0.5); // to the near face, not the centre

        double by = block.Y + 0.5;
        double lo = player.Position.Y - 0.5, hi = player.Position.Y + 1.8; // body segment (feet-or-centre anchored)
        double dy = by < lo ? lo - by : by > hi ? by - hi : 0.0;
        dy = System.Math.Max(0.0, dy - 0.5);

        double dz = System.Math.Abs((block.Z + 0.5) - player.Position.Z);
        dz = System.Math.Max(0.0, dz - 0.5);

        const double slack = 1.0; // covers the 10 Hz move-stream trailing the true position while walking
        double max = MaxReach + slack;
        return dx * dx + dy * dy + dz * dz <= max * max;
    }

    /// <summary>Squared distance between two on-planet positions measured the short way round the longitude
    /// seam — every surface proximity check uses this so a creature/door/vendor/container just across X = 0 is
    /// adjacent, not a world away, at this world's size. (Space combat keeps plain distances.)</summary>
    private double WrapDistSq(Vector3f a, Vector3f b) => WorldConstants.WrapDistanceSquared(a, b, _world.Circumference);

    /// <summary>Wrap-aware squared distance from a position to a block cell (the cell's min corner).</summary>
    private double WrapDistSq(Vector3f a, Vector3i b) => WorldConstants.WrapDistanceSquared(a, new Vector3f(b.X, b.Y, b.Z), _world.Circumference);

    private ToolProperties ActiveTool(PlayerState player)
    {
        int slot = player.SelectedHotbarSlot;
        if (slot >= 0 && slot < player.Inventory.SlotCount && player.Inventory.Slots[slot] is { } stack && !stack.IsEmpty)
        {
            var def = _content.GetItem(stack.Item);
            if (def is { Category: ItemCategory.Tool, Tool: { } tool })
            {
                return tool;
            }
        }

        return new ToolProperties { Kind = ToolKind.None, Tier = 0 };
    }

    private static bool ToolCanMine(ToolProperties tool, BlockDefinition block)
    {
        if (block.RequiredTool != ToolKind.None && tool.Kind != block.RequiredTool)
        {
            return false;
        }

        return tool.Tier >= block.MinToolTier;
    }

    private bool StationAvailable(PlayerState player, CraftingStation station)
    {
        if (station == CraftingStation.Hand)
        {
            return true;
        }

        if (station == CraftingStation.Market)
        {
            return MarketAvailable(player); // barter trade console — no module needed
        }

        // Off the ship, a placed workbench/forge enables crafting on a world — base-building (Task 5 Stage 3).
        if (!player.AboardShip)
        {
            return station switch
            {
                CraftingStation.Workshop => NearStationBlock(player, "workbench"),
                CraftingStation.Refinery => NearStationBlock(player, "forge"),
                CraftingStation.Detoxifier => NearStationBlock(player, "detoxifier"),
                _ => false,
            };
        }

        var moduleKey = station switch
        {
            CraftingStation.Workshop => "workshop",
            CraftingStation.Refinery => "refinery",
            CraftingStation.Detoxifier => "detoxifier",
            _ => string.Empty,
        };

        return moduleKey.Length > 0 && _ship.HasModule(moduleKey);
    }

    /// <summary>True when a placed crafting-station block (workbench/forge) sits within reach of the player,
    /// so they can craft at a base on a world without being aboard the ship (Task 5 Stage 3).</summary>
    private bool NearStationBlock(Shared.State.PlayerState player, string blockKey)
    {
        if (_content.GetBlock(blockKey) is not { } def || def.NumericId.Value == 0)
        {
            return false;
        }

        ushort id = def.NumericId.Value;
        int px = (int)System.Math.Floor(player.Position.X);
        int py = (int)System.Math.Floor(player.Position.Y);
        int pz = (int)System.Math.Floor(player.Position.Z);
        const int reach = 3;
        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dz = -reach; dz <= reach; dz++)
                {
                    if (_world.GetBlock(new Shared.Geometry.Vector3i(px + dx, py + dy, pz + dz)).Value == id)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the player can use a market (barter) trade station — either the ship's trade console
    /// (aboard) or standing next to a settlement vendor.
    /// </summary>
    private bool MarketAvailable(PlayerState player)
        => player.AboardShip || NearSettlementVendor(player) || NearSpaceStationVendor(player)
           || NearLandedTraderPilot(player); // P3: barter with a peaceful trader landed on a planet surface

    private void SaveAll()
    {
        // One transaction for the whole save: every player + ship + metadata commits once instead of paying a
        // separate WAL commit per row (which scales with the player count and stalls the tick thread).
        _repo.RunInTransaction(() =>
        {
            foreach (var session in _sessions.Values)
            {
                if (!session.Joined)
                {
                    continue;
                }

                _repo.SavePlayer(session.State);
                if (session.Ships.TryGetValue(session.ActiveShipId, out var ship))
                {
                    _repo.SaveShip(ShipSaveKey(session.State.PlayerId), ship); // each player's own ship
                }
            }

            _repo.SaveMetadata(_meta);
        });
    }

    /// <summary>Auto-saves at a natural checkpoint (landing on a body, docking a station) so the player's
    /// per-planet position is captured there, not only on the autosave timer / an explicit save.</summary>
    private void CheckpointSave(string reason)
    {
        SaveAll();
        _log.Info($"Checkpoint save ({reason}).");
    }

    /// <summary>Player chat (requires a radio; length-capped + rate-limited). Reach depends on the best radio
    /// tier held: comm_radio = same world, system_radio = same star system, galaxy_radio = the whole game.</summary>
    private void HandleChat(PlayerSession session, ChatIntent chat)
    {
        string text = (chat.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        // Debug snapshot command — captured + persisted for the dev; works without a comm radio. Rate-limit it
        // like chat so it can't be spammed to write a dev snapshot per packet (disk/log growth).
        if (text.StartsWith("/bump", System.StringComparison.OrdinalIgnoreCase))
        {
            int bumpNow = System.Environment.TickCount;
            if (bumpNow - session.LastChatTick < 700)
            {
                return; // rate limit
            }

            session.LastChatTick = bumpNow;
            HandleBump(session, text.Length > 5 ? text.Substring(5).Trim() : string.Empty);
            return;
        }

        if (text.Length > 200)
        {
            text = text.Substring(0, 200);
        }

        if (!HasAnyRadio(session))
        {
            Reject(session, "chat", "You need a comm radio to use comms.");
            return;
        }

        int now = System.Environment.TickCount;
        if (now - session.LastChatTick < 700)
        {
            return; // rate limit
        }

        session.LastChatTick = now;
        string sender = string.IsNullOrEmpty(session.State.Name) ? "Pilot" : session.State.Name;
        // Reach follows the sender's best radio tier (world / system / galaxy), not a flat game-wide broadcast.
        SendToRadioAudience(session, new ChatMessage { Sender = sender, Text = text }, DeliveryMode.ReliableOrdered);
    }

    /// <summary>Live voice relay (opt-in). A thin, opaque forwarder: the server never decodes the Opus payload —
    /// it stamps the speaker's id and relays the frame to the same tiered radio audience as text chat (world /
    /// system / galaxy by the best radio held), sent Unreliable for lowest latency. Gated on the same radio
    /// requirement as chat; silently dropped when voice is disabled or the player holds no radio (the client is
    /// told voice is available via <see cref="ServerRules.VoiceChatEnabled"/>, so it should not be sending).</summary>
    private void HandleVoice(PlayerSession session, VoiceFrame frame)
    {
        if (!_config.VoiceChatEnabled || frame.Opus is not { Length: > 0 } || !HasAnyRadio(session))
        {
            return;
        }

        // Cap a single frame so a malicious client can't relay huge payloads to the whole audience. ~20 ms of
        // Opus is well under 1 KB even at high bitrate; 4 KB is a generous ceiling.
        if (frame.Opus.Length > 4096)
        {
            return;
        }

        frame.FromPlayerId = session.State.PlayerId; // authoritative sender id (don't trust the client's field)
        SendToRadioAudienceExcept(session, frame, DeliveryMode.Unreliable);
    }

    private void Reject(PlayerSession session, string action, string reason)
        => Send(session, new ActionRejected { Action = action, Reason = reason });

    private void CraftFail(PlayerSession session, string recipeKey, string reason)
        => Send(session, new CraftResult { Success = false, RecipeKey = recipeKey, Reason = reason });

    private void SendPlayerState(PlayerSession session)
    {
        var p = session.State;
        Send(session, new PlayerStateUpdate
        {
            PlayerId = p.PlayerId,
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Yaw = p.Yaw,
            Pitch = p.Pitch,
            Health = p.Health,
            Oxygen = p.Oxygen,
            SuitEnergy = p.SuitEnergy,
            Hunger = p.Hunger,
            AboardShip = p.AboardShip,
            InEva = p.InEva,
            AboveAtmosphere = p.AboveAtmosphere,
            StationName = CurrentStationName(p.PlayerId),
            AiCoreTier = VegaCoreTier(session),
            InSpeeder = p.InSpeeder,
        });
    }

    /// <summary>Resolves the friendly (system, planet) names for the currently active world (the Active
    /// cursor's location), so per-world init/tick label the right body even with several worlds resident.</summary>
    private (string System, string Planet) ActiveLocationNames()
    {
        string activeId = _worlds.Active?.LocationId ?? _meta.ActiveLocationId;
        foreach (var sys in _galaxy.Systems)
        {
            foreach (var body in sys.Bodies)
            {
                if (body.Id == activeId)
                {
                    return (sys.Name, body.Name);
                }
            }
        }

        return (string.Empty, _worlds.Active?.PlanetType ?? _meta.DefaultPlanetType);
    }

    private void SendStarMap(PlayerSession session)
    {
        var systems = _galaxy.Systems.Select(sys => new NetStarSystem
        {
            Id = sys.Id,
            Name = sys.Name,
            MapX = sys.MapX,
            MapY = sys.MapY,
            Bodies = sys.Bodies.Select(ToNetBody).ToArray(),
        }).ToArray();

        var players = _sessions.Values
            .Where(s => s.Joined)
            .Select(s => new NetPlayerLocation { Name = s.State.Name, LocationId = s.CurrentLocationId })
            .ToArray();

        // This player's own progression: bodies landed on + systems entered. The body/system the player is
        // currently on always counts (covers legacy saves + the very first spawn before anything was marked).
        var landed = new HashSet<string>(session.State.LandedBodies);
        var known = new HashSet<string>(session.State.KnownSystems);
        if (_galaxy?.FindBody(session.CurrentLocationId) is { } hereBody)
        {
            landed.Add(hereBody.Id);
            if (!string.IsNullOrEmpty(hereBody.SystemId))
            {
                known.Add(hereBody.SystemId);
            }
        }

        Send(session, new StarMapData
        {
            Systems = systems,
            ActiveLocationId = session.CurrentLocationId,
            Players = players,
            LandedBodyIds = landed.ToArray(),
            KnownSystemIds = known.ToArray(),
            MyStationBodyIds = MyStationBodyIds(session.State.PlayerId), // bodies the player has a station orbiting
            MyBases = MyBaseList(session.State.PlayerId),                // bodies the player has founded a base on
        });
    }

    /// <summary>Refreshes the shared star map for every joined player (e.g. after a station is renamed).</summary>
    private void BroadcastStarMap()
    {
        foreach (var s in _sessions.Values.Where(s => s.Joined))
        {
            SendStarMap(s);
        }
    }

    /// <summary>Records that a player has physically arrived ON a body — marks it landed (a quick-travel
    /// target) and its system known (its bodies + mini map revealed on the travel screen). Persisted.</summary>
    private void MarkArrivedOnBody(PlayerSession session, string bodyId)
    {
        var body = _galaxy?.FindBody(bodyId);
        if (body == null)
        {
            return;
        }

        session.State.LandedBodies.Add(body.Id);
        if (!string.IsNullOrEmpty(body.SystemId) && session.State.KnownSystems.Add(body.SystemId))
        {
            RecordStoryMilestone(); // a new star system mapped → story milestone (P3)
        }
    }

    /// <summary>Records that a player has entered a star system in flight (a hyperjump arrival) — reveals
    /// the system's bodies + mini map on the travel screen, without marking any body landed. Persisted.</summary>
    private void MarkSystemKnown(PlayerSession session, string systemId)
    {
        if (!string.IsNullOrEmpty(systemId) && session.State.KnownSystems.Add(systemId))
        {
            RecordStoryMilestone(); // a new star system mapped → story milestone (P3)
        }
    }

    /// <summary>Projects a galaxy body to its network form, including its fixed-landing-pad capacity + how many
    /// pads are currently free (item 38) so the star map can flag a full body. Non-surface bodies have 0 pads.</summary>
    private NetBody ToNetBody(BlocksBeyondTheStars.Shared.World.CelestialBody b)
    {
        int total = string.IsNullOrEmpty(b.PlanetType) ? 0 : PadCountFor(b.Id, b.PlanetType!, b.Kind);
        return new NetBody
        {
            Id = b.Id,
            Name = b.Name,
            Kind = b.Kind.ToString(),
            PlanetType = b.PlanetType,
            Status = b.Status.ToString(),
            OwnerName = b.Kind == CelestialKind.SpaceStation ? StationOwnerName(b.Id) : string.Empty,
            SystemX = b.SystemX,
            SystemY = b.SystemY,
            SystemZ = b.SystemZ,
            OrbitPeriodDays = b.OrbitPeriodDays,
            ParentId = b.ParentId,
            PadsTotal = total,
            PadsFree = total > 0 ? FreePadCount(b.Id, total) : 0,
        };
    }

    private void SendRules(PlayerSession session)
    {
        var r = Rules;
        Send(session, new ServerRules
        {
            GameMode = r.GameMode.ToString(),
            Pvp = r.Pvp.ToString(),
            WeaponMode = r.WeaponMode.ToString(),
            AggressiveAliens = r.AggressiveAliens.ToString(),
            EnvironmentalHazards = r.EnvironmentalHazards.ToString(),
            DeathPenalty = r.DeathPenalty.ToString(),
            KeepInventoryOnDeath = r.KeepInventoryOnDeath,
            KeepShipOnDeath = r.KeepShipOnDeath,
            OxygenEnabled = r.OxygenEnabled,
            AdminCheatsActive = r.CheatsAllowed,
            CreatureAbundance = r.CreatureAbundance.ToString(),
            PlanetEnemies = r.PlanetEnemies.ToString(),
            SpaceNpcEnemies = r.SpaceNpcEnemies.ToString(),
            AlienUfos = r.AlienUfos.ToString(),
            InstantTravel = r.InstantTravel,
            VoiceChatEnabled = _config.VoiceChatEnabled,
        });
    }

    /// <summary>World options, live edit (world admin only): applies the sent gameplay activities to the
    /// running rules, persists them into the save's rules override and re-broadcasts the rule set, so the
    /// change survives reloads and every client's settings view updates.</summary>
    private void HandleSetWorldRules(PlayerSession session, SetWorldRulesIntent intent)
    {
        if (!session.State.IsAdmin)
        {
            Reject(session, "world_rules", "Only the world admin may change world rules.");
            return;
        }

        static void Apply(string value, System.Action<AlienActivity> set)
        {
            if (!string.IsNullOrEmpty(value) && System.Enum.TryParse<AlienActivity>(value, ignoreCase: true, out var v))
            {
                set(v);
            }
        }

        Apply(intent.CreatureAbundance, v => Rules.CreatureAbundance = v);
        Apply(intent.PlanetEnemies, v => Rules.PlanetEnemies = v);
        Apply(intent.SpaceNpcEnemies, v => Rules.SpaceNpcEnemies = v);
        Apply(intent.AlienUfos, v => Rules.AlienUfos = v);
        if (!string.IsNullOrEmpty(intent.InstantTravel))
        {
            Rules.InstantTravel = intent.InstantTravel.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(intent.KeepInventoryOnDeath))
        {
            Rules.KeepInventoryOnDeath = intent.KeepInventoryOnDeath.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(intent.KeepShipOnDeath))
        {
            Rules.KeepShipOnDeath = intent.KeepShipOnDeath.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        _meta.RulesOverride = Rules.Clone(); // the world owns its rules — persist the edit
        _repo.SaveMetadata(_meta);

        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                SendRules(s);
            }
        }

        _log.Info($"World rules updated by '{session.State.Name}': creatures={Rules.CreatureAbundance}, " +
                  $"planet={Rules.PlanetEnemies}, space={Rules.SpaceNpcEnemies}, ufos={Rules.AlienUfos}, instantTravel={Rules.InstantTravel}.");
    }

    /// <summary>Rearranges the player's personal inventory by swapping two slots (B58 — customising the quick-bar,
    /// slots 0..HotbarSlots-1). <c>ToSlot == -1</c> stows the item out of the quick-bar into the first free
    /// backpack slot. Server-authoritative: validates indices, then swaps and re-syncs.</summary>
    private void HandleMoveItem(PlayerSession session, MoveItemIntent intent)
    {
        var inv = session.State.Inventory;
        int from = intent.FromSlot;
        if (from < 0 || from >= inv.SlotCount || inv.Slots[from] is null)
        {
            return; // nothing to move
        }

        int to = intent.ToSlot;
        if (to == -1)
        {
            to = inv.FirstEmptySlot(HotbarSlots); // stow into the backpack (past the quick-bar)
            if (to < 0)
            {
                to = inv.FirstEmptySlot(0); // backpack full → any free slot
            }

            if (to < 0 || to == from)
            {
                return; // inventory full / nowhere to stow
            }
        }
        else if (to < 0 || to >= inv.SlotCount || to == from)
        {
            return;
        }

        inv.Swap(from, to);
        SendInventory(session);
    }

    /// <summary>Test seam: drives a quick-bar move/swap for a player (B58).</summary>
    public void MoveItemForTest(string playerId, int fromSlot, int toSlot)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleMoveItem(session, new MoveItemIntent { FromSlot = fromSlot, ToSlot = toSlot });
        }
    }

    private void SendInventory(PlayerSession session)
    {
        Send(session, new InventoryUpdate
        {
            Personal = DumpInventory(session.State.Inventory),
            Cargo = session.State.AboardShip ? DumpInventory(_ship.Cargo) : Array.Empty<NetItemStack>(),
            UnlockedBlueprints = session.State.UnlockedBlueprints.ToArray(),
            KnowledgePoints = session.State.KnowledgePoints,
        });
    }

    private static NetItemStack[] DumpInventory(Inventory inv)
    {
        var list = new List<NetItemStack>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { } s && !s.IsEmpty)
            {
                list.Add(new NetItemStack { Slot = i, Item = s.Item, Count = s.Count });
            }
        }

        return list.ToArray();
    }

    private void Send(PlayerSession session, object message)
        => _transport.Send(session.ConnectionId, NetCodec.Encode(message), DeliveryMode.ReliableOrdered);

    /// <summary>Sends an already-encoded payload (so a broadcast encodes the message once and reuses the same
    /// bytes for every recipient instead of re-serializing per send). The payload is read-only after encoding.</summary>
    private void SendEncoded(int connectionId, byte[] payload)
        => _transport.Send(connectionId, payload, DeliveryMode.ReliableOrdered);

    private void SendTo(int connectionId, object message)
        => _transport.Send(connectionId, NetCodec.Encode(message), DeliveryMode.ReliableOrdered);

    private void Broadcast(object message)
        => _transport.Broadcast(NetCodec.Encode(message), DeliveryMode.ReliableOrdered);

    // ---------------- Radio reach (tiered comms: text chat + voice) ----------------

    /// <summary>Whether a player can transmit on comms at all (holds any radio tier).</summary>
    private static bool HasAnyRadio(PlayerSession s)
        => s.State.Inventory.Has("comm_radio", 1)
        || s.State.Inventory.Has("system_radio", 1)
        || s.State.Inventory.Has("galaxy_radio", 1);

    /// <summary>The players who can hear <paramref name="sender"/>'s comms, by the widest radio tier they hold
    /// (the tiers stack as upgrades). <c>galaxy_radio</c> = everyone joined; <c>system_radio</c> = everyone on a
    /// body in the same star system; <c>comm_radio</c> = everyone on the same world. The sender is included (so
    /// text chat echoes locally, exactly as the prior game-wide broadcast did). When a player's location has no
    /// resolvable star system (station/void worlds), the system tier falls back to same-world reach.</summary>
    private IEnumerable<PlayerSession> RadioAudience(PlayerSession sender)
    {
        var inv = sender.State.Inventory;

        if (inv.Has("galaxy_radio", 1))
        {
            return _sessions.Values.Where(s => s.Joined);
        }

        if (inv.Has("system_radio", 1))
        {
            string sysId = _galaxy?.FindBody(sender.CurrentLocationId)?.SystemId ?? string.Empty;
            if (!string.IsNullOrEmpty(sysId))
            {
                return _sessions.Values.Where(s => s.Joined
                    && (_galaxy?.FindBody(s.CurrentLocationId)?.SystemId ?? string.Empty) == sysId);
            }
            // No star system here (e.g. a station interior) → behave like a local radio.
        }

        string loc = sender.CurrentLocationId;
        return _sessions.Values.Where(s => s.Joined && s.CurrentLocationId == loc);
    }

    /// <summary>Sends a comms message to the sender's tiered radio audience, encoding once and reusing the bytes
    /// for every recipient. Text chat uses <see cref="DeliveryMode.ReliableOrdered"/>; voice frames use
    /// <see cref="DeliveryMode.Unreliable"/> (latency over delivery — a dropped 20 ms frame is inaudible).</summary>
    private void SendToRadioAudience(PlayerSession sender, object message, DeliveryMode mode)
    {
        var payload = NetCodec.Encode(message);
        foreach (var s in RadioAudience(sender))
        {
            _transport.Send(s.ConnectionId, payload, mode);
        }
    }

    /// <summary>As <see cref="SendToRadioAudience"/> but skips the sender — used for voice, where a speaker must
    /// not hear their own relayed frames (text chat, by contrast, echoes the sender's own line into their log).</summary>
    private void SendToRadioAudienceExcept(PlayerSession sender, object message, DeliveryMode mode)
    {
        var payload = NetCodec.Encode(message);
        foreach (var s in RadioAudience(sender))
        {
            if (s.ConnectionId == sender.ConnectionId)
            {
                continue;
            }

            _transport.Send(s.ConnectionId, payload, mode);
        }
    }

    // ---------------- Multi-world routing (Active cursor) ----------------

    /// <summary>Joined players currently in the active cursor world. With one world this is every joined
    /// player; with several resident worlds it is just that world's occupants.</summary>
    private IEnumerable<PlayerSession> JoinedInActiveWorld()
    {
        string loc = _worlds.Active?.LocationId ?? string.Empty;
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.CurrentLocationId == loc)
            {
                yield return s;
            }
        }
    }

    /// <summary>Sends a world-local message (block change, entity list, environment, presence) only to the
    /// players in the active cursor world, so a player on planet A never receives planet B's events.</summary>
    private void BroadcastToWorld(object message)
    {
        // Encode ONCE and reuse the bytes for every recipient — re-encoding per send made a 4-player world
        // serialize the same block-change / entity / environment message 4× (the biggest steady-state GC cost).
        var payload = NetCodec.Encode(message);
        foreach (var s in JoinedInActiveWorld())
        {
            SendEncoded(s.ConnectionId, payload);
        }
    }

    /// <summary>Points the Active cursor at the resident world for a body. True if it is the current world
    /// or a cached one; false if not loaded (an occupied world is always loaded, so it normally succeeds).</summary>
    private bool SetActiveWorld(string locationId)
    {
        if (_worlds.Active != null && _worlds.Active.LocationId == locationId)
        {
            return true;
        }

        return _worlds.SetActive(locationId);
    }

    /// <summary>The distinct bodies that currently have at least one joined player (the worlds to tick).</summary>
    private List<string> OccupiedLocations()
    {
        var seen = new List<string>();
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !string.IsNullOrEmpty(s.CurrentLocationId) && !seen.Contains(s.CurrentLocationId))
            {
                seen.Add(s.CurrentLocationId);
            }
        }

        return seen;
    }
}

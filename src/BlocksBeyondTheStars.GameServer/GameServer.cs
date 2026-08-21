// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Reflection;
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
    // which a player floats in space on foot). The floor covers the DEEPEST world foundation roll
    // (surface − 2048 → bedrock near Y −1990, #580) so "dig to the bedrock" works on every world — the old
    // −512 silently walled off the bottom kilometre on deep-rolled worlds. Widening the band grows the
    // worst-case chunk volume a spoofed client could force by ~2×; still hard-bounded, and the streaming
    // LOD never sends deep chunks far from a player, so normal-play cost is unchanged.
    private const int MinBuildY = -2100;
    private const int MaxBuildY = 1024;

    /// <summary>True when a client-supplied block Y is inside the legal vertical build band (see MinBuildY).</summary>
    private static bool WithinBuildHeight(int y) => y >= MinBuildY && y <= MaxBuildY;

    /// <summary>Replaces every control character (CR/LF/tab/ANSI/NUL) in a client-supplied string with a
    /// space. Used before any free text is broadcast to other players or written to a log/file — control
    /// chars would otherwise corrupt chat UIs, forge log lines or break persisted JSON.</summary>
    internal static string StripControlChars(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(char.IsControl(c) ? ' ' : c);
        }

        return sb.ToString().Trim();
    }

    private readonly ServerConfig _config;
    private readonly GameContent _content;
    private readonly IServerTransport _transport;
    private readonly IWorldRepository _repo;
    private readonly IGameLogger _log;
    private readonly IAiMissionProvider _ai;

    private readonly Lazy<CrashReportWriter> _crashWriter;

    /// <summary>The durable, endpoint-independent sink for contained tick faults and process-wide crashes.
    /// Lazily created (after <see cref="Start"/> has resolved the world directory) and shared with the host's
    /// <c>AppDomain</c>/<c>TaskScheduler</c> handlers so every server fault is written to one place.
    /// <see cref="Lazy{T}"/> instead of <c>??=</c> (#426 S18): the first accesses can race — a crash handler
    /// on any thread vs. the tick thread — and a torn double-init would split reports over two writers.</summary>
    public CrashReportWriter CrashWriter => _crashWriter.Value;

    /// <summary>Build string baked into each crash report (informational version if the build set one, else
    /// the assembly version) so a report identifies the binary that produced it.</summary>
    private static string ServerVersionString =>
        typeof(GameServer).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(GameServer).Assembly.GetName().Version?.ToString()
        ?? "unknown";

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
    // Far-chunk unload throttle: sweeping every loaded chunk against every player each tick would be wasteful,
    // so the server only evicts out-of-range cached chunks on this cadence (seconds). Bounds server memory on
    // long exploration — without it _loaded grows unbounded as a player crosses the world (the cache never shrank).
    private double _sinceChunkSweep;
    // Fractional playtime carry: whole seconds are flushed into _meta.CumulativePlaytimeSeconds, the
    // sub-second remainder lives here between ticks. Only advanced while a player is joined.
    private double _playtimeCarry;
    private volatile bool _running;
    // Latches a stop request permanently, unlike _running which Run() re-arms on entry. Needed because the
    // SIGINT handler is registered BEFORE Start() (issue #243): a stop requested while startup worldgen is
    // still running must survive until Run() begins, which then drains + saves immediately instead of looping.
    private volatile bool _stopRequested;
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
              ?? (config.AiLevel != AiLevel.Off
                  ? new HttpAiMissionProvider(config.AiBackendUrl, timeoutSeconds: config.AiTimeoutSeconds)
                  : new NullAiMissionProvider());
        _crashWriter = new Lazy<CrashReportWriter>(
            () => new CrashReportWriter(
                BugReportPaths.ResolveCrashes(Path.Combine(_repo.WorldDirectory, "crashes")),
                _config.WorldName,
                ServerVersionString),
            LazyThreadSafetyMode.ExecutionAndPublication);
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
        try
        {
            _repo.Initialize();
        }
        catch (InvalidDataException ex)
        {
            _log.Error($"Failed to initialize persistence: database is corrupted. " +
                       $"The database was left untouched. Error: {ex.Message}");

            throw;
        }
        // Record the current block-id palette and remap any save written under a different block set BEFORE
        // world load. Block ids are assigned by key sort order, so adding a block shifts them; without this a
        // content update would silently decode every stored edit to the wrong block.
        _repo.EnsureBlockPalette(_content.BlockPalette());

        var launchRules = _config.Rules.Clone();
        _meta = _repo.LoadMetadata() ?? CreateInitialMetadata();

        // World options: once created, the WORLD owns its rules — the save's override replaces the launch
        // config's rules (singleplayer passes creation options only once; dedicated restarts keep the set).
        // Saves from before world options existed have no override and keep using the config's rules.
        if (_meta.RulesOverride is not null)
        {
            _config.Rules = _meta.RulesOverride;
        }

        // Hosted servers can now opt everyone into free space flight from launch config/env. If an
        // older world baked the previous default (off), preserve other saved world rules but lift this one.
        if (launchRules.FreeSpaceFlight && !_config.Rules.FreeSpaceFlight)
        {
            _config.Rules.FreeSpaceFlight = true;
            _meta.RulesOverride = _config.Rules.Clone();
            _log.Info("Free space flight enabled for this world by server launch rules.");
        }

        // Same lift for admin cheats (#642): every existing singleplayer save baked AdminCheats=false
        // (the flag predates --admin-cheats), so without this the launcher's opt-in would only ever
        // reach freshly created worlds. Launch-config cheats are an explicit operator choice — the
        // bundled host always passes the flag; dedicated servers only when started with it.
        if (launchRules.AdminCheats && !_config.Rules.AdminCheats)
        {
            _config.Rules.AdminCheats = true;
            _config.Rules.AllowCheatsInSurvival |= launchRules.AllowCheatsInSurvival;
            _meta.RulesOverride = _config.Rules.Clone();
            _log.Info("Admin cheats enabled for this world by server launch rules.");
        }

        // Creative/Sandbox worlds fly. The rule is new, so every existing Creative save baked the `false`
        // default — without this lift the fix would only ever reach worlds created after the update, and the
        // player who asked for it would still be walking in the world he already has. A Creative world that
        // deliberately turned flight OFF keeps that choice, because the launch rules carry it explicitly.
        if (_config.Rules.GameMode == GameMode.Creative && !_config.Rules.CreativeFlight && launchRules.CreativeFlight)
        {
            _config.Rules.CreativeFlight = true;
            _meta.RulesOverride = _config.Rules.Clone();
            _log.Info("Free flight enabled for this creative world.");
        }

        _repo.SaveMetadata(_meta);

        _generator = new WorldGenerator(_meta.Seed, _content);
        // World options: flora/ore factors are part of the save's description — set BEFORE any chunk
        // generates so worldgen stays deterministic across reloads. Continents (#704) ride the same
        // path: baked at creation, re-applied on every load, never flipped on an existing save.
        _generator.SetWorldOptionFactors(
            _meta.Description.FloraDensity.FloraFactor(),
            _meta.Description.RareResources.OreFactor());
        _generator.SetContinentsEnabled(_meta.Description.TerrainContinents);
        _worlds = new WorldManager(_content, _generator, _repo);
        BuildGalaxy(); // resolves _meta.ActiveLocationId to a concrete celestial body id
        LoadPlayerStations(); // item 20 S4: restore persisted player stations onto the star map + registry
        RecomputeRelayLanes(); // #1125: jump lanes re-derive from the completed relays (never persisted)
        RegisterUniqueDerelict(); // #1129: "The Long Quiet" — the galaxy's one boardable derelict (derived)
        LoadAllBases();       // restore player-founded planet bases (Grundstein) server-wide for the travel screen
        LoadPaintDesigns();   // restore the save-global paint-design registry (painted blocks reference it by id)
        LoadCustomShapes();   // …and the player-designed form registry (shaped blocks/items reference it by index)
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
            // Born after ship-as-object: no stamped hulls can exist in this save, so the legacy
            // stamp-residue cleanup (#870) must never touch it.
            CreatedWithShipObjects = true,
        };
    }

    /// <summary>
    /// Builds the deterministic galaxy from the seed + world description, applies persisted
    /// generation status, and marks the start location as visited.
    /// </summary>
    private void BuildGalaxy()
    {
        // #1123: a grown save regenerates with the persisted extra count — system N is a pure function
        // of (seed, N), so the grown systems come back byte-identical, in the same pass as the fixed ones.
        int systemCount = _meta.Description.StarSystemCount + Math.Max(0, _meta.GalaxyGrownSystems);
        _galaxy = new UniverseGenerator(_meta.Seed, _meta.Description, _content).Generate(systemCount);

        var stored = _repo.LoadLocationStatuses();
        foreach (var body in _galaxy.AllBodies())
        {
            if (stored.TryGetValue(body.Id, out var s) && Enum.TryParse<GenerationStatus>(s, out var status))
            {
                body.Status = status;
            }

            // #468: the persisted type map is the authority — a body keeps the type it was first seen with,
            // no matter how data/planets.json changed since. Unknown pinned types (a removed data entry)
            // fall back to the fresh derivation rather than crashing LoadWorld.
            if (_meta.BodyPlanetTypes.TryGetValue(body.Id, out var pinned)
                && _content.GetPlanet(pinned) is not null)
            {
                body.PlanetType = pinned;
            }
        }

        // Choose a start body: the first planet matching the configured start planet type. When no body
        // matches, two fallbacks keep the start experience intact (air + food + materials):
        //  - Unknown type (a --start-planet typo would otherwise crash LoadWorld): adopt the first
        //    breathable planet that grows flora, else any planet, as the world default.
        //  - Known type without a body in this galaxy (per-type frequency overrides removed it, or a
        //    forced --start-planet for the marketing captures): RETYPE the first planet to the configured
        //    type. The start terrain is always generated from DefaultPlanetType, so retyping keeps the
        //    star map — and a later travel-back, which regenerates by body type — consistent with the
        //    surface the player actually spawned on.
        CelestialBody? start = null, firstPlanet = null, firstBreathable = null;
        foreach (var body in _galaxy.AllBodies())
        {
            if (body.Kind != CelestialKind.Planet)
            {
                continue;
            }

            firstPlanet ??= body;
            if (firstBreathable is null
                && _content.GetPlanet(body.PlanetType ?? string.Empty) is { } def
                && string.Equals(def.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase)
                && def.FloraDensity > 0)
            {
                firstBreathable = body;
            }

            if (body.PlanetType == _meta.DefaultPlanetType)
            {
                start = body;
                break;
            }
        }

        if (start is null && firstPlanet is not null)
        {
            if (_content.GetPlanet(_meta.DefaultPlanetType) is null)
            {
                start = firstBreathable ?? firstPlanet;
                _meta.DefaultPlanetType = start.PlanetType ?? string.Empty;
            }
            else
            {
                start = firstPlanet;
                start.PlanetType = _meta.DefaultPlanetType;
            }
        }

        if (start is not null)
        {
            _meta.ActiveLocationId = start.Id;
            // #596: the start planet always carries rings — the sky band is the feature's shop window.
            // Deterministic from the body id, so every restart re-derives the same ring; cosmetic only.
            UniverseGenerator.EnsureStartPlanetRings(start);
            // #678: the start planet is a landmark — it trades its designation for a coined proper name.
            // Runs after the retype above so the name's biome flavor matches the FINAL planet type.
            var startSystem = _galaxy.Systems.FirstOrDefault(s => s.Id == start.SystemId);
            if (startSystem is not null)
            {
                UniverseGenerator.EnsureStartPlanetProperName(startSystem, start);
            }
            if (start.Status != GenerationStatus.Visited)
            {
                start.Status = GenerationStatus.Visited;
                _repo.SetLocationStatus(start.Id, start.Status.ToString());
            }
        }

        // #468 (decision #1: freeze): pin every body's FINAL type — including the start-planet retype
        // above — so future planets.json edits can never re-roll them. On an existing save this adopts
        // whatever the player currently sees; new systems get pinned the first time they appear.
        bool typesDirty = false;
        foreach (var body in _galaxy.AllBodies())
        {
            if (string.IsNullOrEmpty(body.PlanetType))
            {
                continue;
            }

            if (!_meta.BodyPlanetTypes.TryGetValue(body.Id, out var known) || known != body.PlanetType)
            {
                _meta.BodyPlanetTypes[body.Id] = body.PlanetType;
                typesDirty = true;
            }
        }

        if (typesDirty)
        {
            _repo.SaveMetadata(_meta);
        }

        // Finale (P6): the galaxy is regenerated from seed each start, so re-append the Guardian system for an
        // already-revealed save (after start-body selection, so it never affects the spawn world). A fresh
        // reveal adds it live via RevealGuardianSystemIfReady.
        if (_storyState.GuardianSystemRevealed)
        {
            EnsureGuardianSystemInGalaxy();
        }
    }

    /// <summary>The archetype a system rolled (#546) — Standard for every system of a pre-variance save.
    /// Recomputed from the seed on demand (the trader-traffic pattern), so nothing is persisted; all
    /// inhabitant systems (stations, traders, bandits, camps, drones) consult THIS one resolver.</summary>
    private SystemArchetype SystemArchetypeOf(string? systemId)
        => SystemArchetypes.For(_meta.Seed, systemId, _meta.Description);

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
        // #549: the archetype's size bias stretches the band (lone giant up to 16000, swarm dwarf down to
        // 4000); bodies outside the galaxy (station interiors, ship worlds) carry no bias.
        int circumference = WorldConstants.CircumferenceFor(locationId, sizeClass, worldBody?.SizeBias ?? 0f);

        var world = _worlds.GetOrCreate(planet, locationId, circumference, out bool isNew);
        world.SizeClass = sizeClass; // remembered for the per-world gravity band seeded in InitWeather
        // Airless MOONS get cratered regolith too (item 33) — even when their planet type normally has air on a
        // full planet. The asteroid type carries Cratered in data, so it's handled by the planet type itself.
        bool airlessMoon = worldBody?.Kind == CelestialKind.Moon
            && string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        world.World.Cratered = airlessMoon; // stamped on the world so chunk gen re-configures fully (#424 S13)
        // Frontier scaling (#1122): outer systems generate richer rare-tier veins. Stamped like Cratered,
        // so every chunk generation re-configures the shared generator with it.
        world.World.FrontierOreBoost = FrontierOreBoostFor(FrontierTierForBody(locationId));
        // Configure the shared generator for this body's direct gen queries (size, cratering, pads —
        // LandingPadFlats is still empty for a brand-new world; BuildLandingPads below refills it).
        // The location id salts the per-body identity (#478): terrain character, rosters, structures.
        _generator.SetWorldMode(world.World.Circumference, airlessMoon, world.World.LandingPadFlats, locationId,
            world.World.FrontierOreBoost);
        if (!isNew)
        {
            return world; // already resident — keep its fauna/structures/edits
        }

        // Fresh world: GetOrCreate set it active. Build its per-world state + structures. The player's own
        // ship is stamped per-player on join/travel (not here), so each player gets their ship in their world.
        ResetWorldRuntimeState();
        InitWeather();

        // #586: decide the placement mode BEFORE anything writes blocks. No persisted edits at all ⇒ the
        // world was never materialised ⇒ the guaranteed (escalating) placement search may run; otherwise the
        // frozen legacy search re-derives positions so structures stay attached to their stamped blocks.
        world.VirginAtLoad = !_repo.HasAnyBlockEdits(locationId);
        world.StampReport.Clear();

        // The flora registry is built on EVERY world, void or not (#628). A station grows nothing of its own,
        // but its hydroponics bay holds real crops — and without the registry the server would not recognise
        // them as flora at all, so harvesting one would not schedule a regrow and no seed could be planted
        // aboard. What keeps plants out of open space is the enclosure test in the regrow/plant paths, not
        // this registry. On a void world the species roster comes out empty, which is exactly right: crops
        // are cultivated, so they carry no world identity anyway.
        InitFlora();
        LoadFloraRegrow(); // restore persisted harvest regrowths so a restart doesn't strand bare cells
        LoadWeatherDeposits(); // #900: restore settled snow so a restart doesn't strand cells that can never melt

        // A void world (an orbital station) has no terrain, so it gets none of the OTHER planet-surface
        // content — no fauna/fluids, no settlements/wrecks/landing zones. Only its stamped structure lives
        // there (the caller stamps it). Weather is initialised above so the env reads its space-sky settings.
        if (!planet.Void)
        {
            BuildLandingPads(); // FIRST: the pads must reach worldgen before any pad-area chunk generates
            InitFluids();
            LoadFluidState(); // #657: restore flowing cells so a restart doesn't promote them to sources
            InitFire();
            LoadFireState(); // #784: restore burn timers so a restart doesn't strand permanent, inert flames
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

                if (_config.PlaceRuins)
                {
                    StampRuins(); // standalone fallen-city ruins (unprotected) — after settlements so they avoid them
                }

                StampBanditCamps(); // small hostile outposts (unprotected; self-skips per config + Bandits rule)

                if (_config.PlaceMonuments)
                {
                    StampMonuments(); // eroded rune relics (unprotected) — the only surface feature airless bodies get
                }

                if (_config.PlaceFactories)
                {
                    StampFactories(); // rare industrial factories (protected until claimed) — avoid settlements
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

                if (_config.PlaceChests)
                {
                    StampChests(); // rare standalone treasure caches (0-N per body)
                }

                StampUniqueSites(); // #1129: this body may carry one of the galaxy's one-of-a-kind places
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
        _bandits.Clear();
        _banditCamps.Clear();
        _monuments.Clear();
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

    /// <summary>Test hook: flips the AutoAim world rule (#693) without a session/admin round-trip.</summary>
    public void SetAutoAimForTest(bool on) => Rules.AutoAim = on;

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
            Reject(session, "travel", "@srv.travel.flight_disabled");
            return;
        }

        var body = _galaxy?.FindBody(intent.DestinationBodyId);
        if (body is null)
        {
            Reject(session, "travel", "@srv.travel.no_destination");
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
            Reject(session, "travel", "@srv.travel.surface_only");
            return;
        }

        if (body.Id == session.CurrentLocationId)
        {
            Reject(session, "travel", "@srv.travel.already_there");
            return;
        }

        // Instant Travel gate (world option, default off): the travel-screen shortcut may only reach bodies
        // you've already landed on. To reach a new world, fly there and land manually (which marks it). A
        // manual flight landing (quickTravel=false) bypasses this — you physically flew there.
        if (quickTravel && !Rules.InstantTravel && !session.State.LandedBodies.Contains(body.Id))
        {
            Reject(session, "travel", "@srv.travel.not_visited");
            return;
        }

        // A jump to a different star system is a hyperspace jump — it needs a jump generator fitted,
        // UNLESS an SPS jump lane links the two systems (#1125): the relay network carries you.
        var origin = _galaxy?.FindBody(session.CurrentLocationId);
        bool hyperjump = origin is null || origin.SystemId != body.SystemId;
        if (hyperjump && !adminBypass && (_ship is null || !_ship.HasModule("jump_generator"))
            && !HasJumpLane(origin?.SystemId, body.SystemId))
        {
            Reject(session, "travel", "@srv.travel.no_jump_generator");
            return;
        }

        // Fixed landing pads (item 38): claim the player's chosen (or first free) pad before tearing down the
        // flight state. A full body (every pad occupied) refuses the landing here, leaving the player in flight.
        // An observer takes no pad (issue #487): pads are finite and communal, and being refused entry to a busy
        // world — the world most likely to need an operator's eyes — would be exactly backwards.
        if (!session.Spectating && !ClaimPadOrReject(session, body.Id, intent.PadIndex))
        {
            return;
        }

        // Per-player travel: only THIS player moves. Other players stay on their own worlds.
        string oldLoc = session.CurrentLocationId;
        LeaveSpace(session.State.PlayerId);

        LoadWorld(body.PlanetType, body.Id); // loads/initialises the destination + sets the Active cursor
        session.CurrentLocationId = body.Id;
        if (hyperjump && !session.Spectating)
        {
            OnAchievementHyperjump(session);        // "Jump Pilot" (#1102)
            RecordStoryMilestone("hyperjump:first"); // the save's first jump between stars advances the arc (#1105)
        }

        MarkArrivedOnBody(session, body.Id); // landed here → a quick-travel target + its system now known

        // Park this player's own ship object on the destination world before placing them.
        SetCurrent(session);
        if (_ship is not null)
        {
            _ship.CurrentLocationId = body.Id; // keep the ship's body in sync so a later launch rises off THIS body (B48)
        }

        // An observer arrives with no ship and no announcement (issue #487).
        if (_config.PlaceStarterShip && !session.Spectating)
        {
            PlaceLandedShip();
        }

        var (systemName, planetName) = ActiveLocationNames();
        OnPlayerTravelled(session, body.Id, body.Name); // complete any "travel to a place" mission objective (item 31)
        if (!session.Spectating)
        {
            ShipAiOnTravelled(session); // VEGA onboarding: a landing after the first launch + world-type flavour
        }

        var pad = PlayerPad(session); // the pad claimed above (item 38)
        int surfaceY = PadGroundY(pad.CenterX, pad.CenterZ); // matches the ship placement's median footprint height
        var spawn = _shipPlaced ? _healTank : new Vector3f(pad.CenterX + 0.5f, surfaceY + 2f, pad.CenterZ + 0.5f);
        session.State.Position = spawn;
        session.AwaitingSpawnAdopt = true; // #865: the client still streams its pre-landing pose for a beat
        session.SentChunks.Clear();
        if (!session.Spectating)
        {
            session.State.RespawnPoint = _shipPlaced ? _healTank : spawn;
            session.State.AboardShip = true;
            BroadcastShipTransit(session, body.Id, pad.CenterX + 0.5f, surfaceY, pad.CenterZ + 0.5f, landing: true); // others see the descent (item 38)
        }

        Send(session, new WorldReset { PlanetType = body.PlanetType, PlanetName = planetName, SystemName = systemName, Hyperjump = hyperjump });
        SendPlayerState(session);
        SendShipCombatStatus(session);
        SendLandedShips(session); // every parked ship object on this world (incl. the player's own)
        SendShipPlacement(session);
        SendShipStations(session);
        SendStationsInReach(session); // #1070: the Tab-menu gates start from the server truth, not a guess
        SendPlanetPois(session);
        SendEnvironment(session);
        if (!session.Spectating)
        {
            // Observers watch the world as it is: they neither seed fauna around themselves nor bring pets
            // along, both of which would be visible changes made by an invisible person (issue #487).
            PopulateCreaturesNear(session.State, CreatureCapPerPlayer); // arrive to a living world, not an empty one
            SpawnCompanionsForSession(session); // re-materialise the player's pets if this is their companions' home world
        }

        SendCreatures(session);
        SendCompanions(session); // the player's full companion roster (for the Companions menu tab)
        SendDoors(session);
        SendDataCubes(session); // minigame download cubes on this body
        SendNetFragments(session); // story net fragments on this body (P2)
        SendVegaObjective(session); // the story objective is per body — "a fragment is HERE" (#1110)
        SendFactories(session); // factories on this body (animated machines + production terminals)
        SendBeacons(session);
        SendBeams(session); // placed beam blocks (teleporter pads) on this body
        SendBases(session); // player-founded bases on this body (Grundstein markers)
        BroadcastLandingPads(session); // the arrival claimed a pad — everyone's map must show it (#1020)
        SendContainers(session);
        SendStarMap(session);
        SyncAppearance(session); // faces + body paintings both ways — appearance is per-world state (#982)
        Send(session, new ServerMessage
        {
            Text = hyperjump
                ? Localize(session.Locale, "srv.travel.hyperjumped").Replace("{system}", systemName).Replace("{planet}", planetName)
                : Localize(session.Locale, "srv.travel.arrived").Replace("{planet}", planetName),
        });
        CheckpointSave($"landed on {planetName}"); // auto-save when landing on a body

        // Drop the old world from memory if this was the last player there (edits are already persisted).
        if (!string.IsNullOrEmpty(oldLoc) && oldLoc != body.Id && !OccupiedLocations().Contains(oldLoc))
        {
            _worlds.Unload(oldLoc);
        }
    }

    /// <summary>Persistence key for a player's ACTIVE ship. Kept as the legacy single-ship key (#848): every
    /// save still mirrors the active ship here, so a save written by this build stays loadable by an older one
    /// and the pre-fleet write sites need no change. The other ships use <see cref="FleetShipSaveKey"/>.</summary>
    private static string ShipSaveKey(string playerId) => "ship_" + playerId;

    /// <summary>Persistence key for one ship of a player's fleet (#848). The `ship` table is a generic
    /// key→JSON store, so per-ship rows need no schema change; <c>PlayerState.FleetShipIds</c> is the index.</summary>
    private static string FleetShipSaveKey(string playerId, string shipId) => "ship_" + playerId + "#" + shipId;

    /// <summary>Sets up a freshly-joined player's ship: points the cursor at them, restores their whole fleet
    /// and the ship they were flying, and parks it on their (active) world. A player owns their own fleet
    /// (multiple ships via crafting/wreck-claim) with exactly one active ship.</summary>
    private void SetupPlayerShip(PlayerSession session)
    {
        SetActiveWorld(session.CurrentLocationId);
        SetCurrent(session);
        MarkArrivedOnBody(session, session.CurrentLocationId); // the home body is a quick-travel target from the start
        RestoreFleet(session);
        RestoreLandingPad(session);
        RecomputeShipCombatStats();
        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip(); // park this player's ship object on their world
            session.State.RespawnPoint = _healTank;
        }

        PersistFleet(session);
    }

    /// <summary>Restores a joining player's fleet from the save (#848). Every owned ship is loaded from its own
    /// row and the active ship is the one they were last flying. A save from before per-ship persistence has no
    /// fleet index — it migrates through the legacy single-ship key, so an existing ship is never lost — and a
    /// brand-new player gets a starter ship.</summary>
    private void RestoreFleet(PlayerSession session)
    {
        var p = session.State;
        session.Ships.Clear();
        foreach (var id in p.FleetShipIds)
        {
            if (!string.IsNullOrEmpty(id) && !session.Ships.ContainsKey(id)
                && _repo.LoadShip(FleetShipSaveKey(p.PlayerId, id)) is { } stored)
            {
                session.Ships[id] = stored;
            }
        }

        if (session.Ships.Count == 0)
        {
            // Pre-#848 save (or a first join): the single persisted ship becomes the fleet's starter entry.
            session.Ships[ShipId] = _repo.LoadShip(ShipSaveKey(p.PlayerId)) ?? CreateStarterShip();
        }

        if (!session.Ships.ContainsKey(session.ActiveShipId))
        {
            session.ActiveShipId = session.Ships.Keys.First(); // a dropped/unknown active id falls back to ship one
        }
    }

    /// <summary>Revalidates the landing pad restored from the save (#848). Pads are communal and finite, so a
    /// persisted pad that is out of range for this body, or already held by another player standing on it, is
    /// released — the next <c>PlayerPad</c> call then hands out the first free one, as before this existed.</summary>
    private void RestoreLandingPad(PlayerSession session)
    {
        int idx = session.AssignedPadIndex;
        if (idx < 0)
        {
            return;
        }

        if (_landingPads.Count == 0)
        {
            BuildLandingPads(); // the pad set is recomputed per world load; make sure it's there to validate against
        }

        if (idx >= _landingPads.Count || PadOccupiedByOther(session.CurrentLocationId, idx, session.State.PlayerId))
        {
            session.AssignedPadIndex = -1;
        }
    }

    /// <summary>Writes a player's whole fleet to the save (#848): every owned ship under its own key, the
    /// active one additionally under the legacy key, and the fleet index onto the player record. The caller
    /// persists the player state itself (or calls <see cref="PersistFleet"/>, which does both).</summary>
    private void SaveFleet(PlayerSession session)
    {
        var p = session.State;
        p.FleetShipIds = session.Ships.Keys.ToList();
        foreach (var (id, ship) in session.Ships)
        {
            _repo.SaveShip(FleetShipSaveKey(p.PlayerId, id), ship);
        }

        if (session.Ships.TryGetValue(session.ActiveShipId, out var active))
        {
            _repo.SaveShip(ShipSaveKey(p.PlayerId), active); // legacy key: still the active ship
        }
    }

    /// <summary>Persists a fleet change (craft, wreck claim, ship switch) immediately, rather than leaving a
    /// bought-and-paid-for ship riding on the next autosave.</summary>
    private void PersistFleet(PlayerSession session)
    {
        SaveFleet(session);
        _repo.SavePlayer(session.State); // carries the fleet index + active ship id
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
        _running = !_stopRequested; // a stop requested before the loop started (SIGINT during startup worldgen) skips straight to the drain+save below
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

                // Backstop: the per-system Guards inside Tick contain the simulation, but anything OUTSIDE
                // them (transport.Poll, the world-loop scaffolding, an autosave path) must not be able to
                // crash the loop either — a crash here would drop every player AND skip the shutdown save
                // below. Contain it, log it (throttled) and keep the loop alive.
                try
                {
                    Tick(dt);
                }
                catch (Exception ex)
                {
                    RecordTickFault("Tick", ex);
                }

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
    /// thread (e.g. a Ctrl-C handler) and at any time — even BEFORE <see cref="Run"/> starts (a stop during
    /// startup worldgen is latched and honored on loop entry). It does NOT save — the run loop drains +
    /// saves on the tick thread.</summary>
    public void RequestStop()
    {
        _stopRequested = true;
        _running = false;
    }

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

    // --- The world pause --------------------------------------------------------------------------------
    // "Im Einzelspieler sollte das Spiel pausiert werden, wenn man in das Menü geht." The Esc dialog was
    // already titled "Pause" with a "Resume" button but nothing ever stopped (#612), and the client was not
    // told about the hold either (#908). Both only ever served a LONE player: a second joined player had the
    // request refused outright, so two friends taking a break both watched hunger drain behind their menus.
    //
    // #973 makes it a group decision instead. The intent lives on each session; the world holds once EVERY
    // joined player is asking for it, and runs again the moment one of them resumes. Nobody can freeze a
    // world for anybody else, because everybody has to agree — which is also why no rule switches this off.

    /// <summary>True while the world is held. DERIVED from the players' intents by
    /// <see cref="RecomputePause"/> — never assign it anywhere else. Only the simulation stops: the transport
    /// keeps being polled, or the unpause could never arrive.</summary>
    private bool _paused;

    /// <summary>Seconds the world has been held, in real time. A client that dies with its menu open must not
    /// leave the world frozen forever (it would also never save), so the hold expires.</summary>
    private double _pausedFor;

    /// <summary>What the last <see cref="PauseState"/> broadcast said. The pause dialog shows who is still
    /// missing, so the message has to go out whenever the tally moves — but only then, not every tick.</summary>
    private (bool Paused, int Holding, int Joined, string Waiting) _pauseBroadcast = (false, -1, -1, string.Empty);

    /// <summary>Longest a lone player may hold the world before it resumes on its own.</summary>
    private const double MaxPauseSeconds = 30 * 60;

    /// <summary>Longest a GROUP hold may last. Shorter than a solo hold on purpose: it suspends everyone
    /// else's evening too, and a hold nobody is left to end costs more the more players are waiting on it.</summary>
    private const double MaxGroupPauseSeconds = 10 * 60;

    /// <summary>True while the world is holding — for tests and the /status snapshot.</summary>
    public bool IsPaused => _paused;

    /// <summary>Drives the pause intent for tests (the client sends it when the Esc menu opens/closes, and
    /// repeats it as a keep-alive while the menu stays open). Stamps the heartbeat the way the wire path
    /// does — this stands in for a real payload, and a test client must not look silent for sending one.</summary>
    public void PauseForTest(PlayerSession session, bool paused)
    {
        session.LastPayloadAt = _uptime;
        HandlePause(session, new PauseIntent { Paused = paused });
    }

    /// <summary>
    /// Records a player's pause intent. It is always accepted — what it means for the WORLD is decided by
    /// <see cref="RecomputePause"/>, which holds only when everybody agrees. (Before #973 a request was
    /// refused outright whenever a second player was joined.)
    /// </summary>
    private void HandlePause(PlayerSession session, PauseIntent intent)
    {
        session.PausedSilentSeconds = 0; // hearing from this client at all is what the keep-alive is for

        if (!intent.Paused)
        {
            session.PauseHoldExpired = false; // closing the menu ends the hold — and any lockout on it
        }
        else
        {
            // A repeat while this session already wants the hold is the client's keep-alive: behind an open
            // menu it is the only payload it sends, and the only proof that it is still alive (see
            // SweepSilentPausedSessions). Seeing one also tells us this client is new enough to send them.
            if (session.WantsPause)
            {
                session.SendsPauseKeepAlive = true;
            }

            if (session.PauseHoldExpired)
            {
                // The hold already ran out under this open menu. The keep-alives still prove the client is
                // alive (stamped above), but they must not put the world straight back to sleep — that would
                // make the ceiling meaningless. Closing and reopening the menu asks again.
                return;
            }
        }

        session.WantsPause = intent.Paused;
        RecomputePause();
    }

    /// <summary>
    /// Derives the hold from the players' intents and broadcasts it whenever the tally moves. The world holds
    /// while at least one player is joined and EVERY joined non-spectator wants it held.
    /// <para>
    /// Spectators are excluded exactly as in #908: an invisible admin observing a world is not someone whose
    /// game a pause could interrupt — counting them silently denied the actual players their pause, and would
    /// now block it forever (an observer never opens a pause menu). Everywhere else in the server draws the
    /// same line (see <c>GameServerObserver</c>).
    /// </para>
    /// </summary>
    private void RecomputePause()
    {
        int joined = 0;
        int holding = 0;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || s.Spectating)
            {
                continue;
            }

            joined++;
            if (s.WantsPause)
            {
                holding++;
            }
        }

        // Only worth naming names when somebody is actually waiting on somebody: this runs every tick, and
        // while everyone is simply playing (holding == 0) nobody has a pause dialog to read them in.
        List<string>? waitingFor = null;
        if (holding > 0 && holding < joined)
        {
            foreach (var s in _sessions.Values)
            {
                if (s.Joined && !s.Spectating && !s.WantsPause)
                {
                    (waitingFor ??= new List<string>()).Add(s.State.Name);
                }
            }
        }

        // "Nobody joined" must not read as "everybody agrees": an empty world would hold forever, never save,
        // and on a hosted server never idle out.
        bool hold = joined > 0 && holding == joined;
        if (hold != _paused)
        {
            if (hold)
            {
                SaveAll(); // a held world is a natural, safe save point — and covers a client that never comes back
            }

            _paused = hold;
            _pausedFor = 0;
            foreach (var s in _sessions.Values)
            {
                s.PausedSilentSeconds = 0; // the paused-silence clock only runs while the world stands still
            }

            _log.Info(hold
                ? $"World held — all {joined} player(s) are in the pause menu."
                : "World resumed.");
        }

        string waiting = waitingFor is null ? string.Empty : string.Join(", ", waitingFor);
        var tally = (_paused, holding, joined, waiting);
        if (tally != _pauseBroadcast)
        {
            _pauseBroadcast = tally;

            // Broadcast, not a reply to the asker: every client stops its OWN world clock from this message,
            // and the pause dialog shows the tally to everyone waiting in it.
            Broadcast(new PauseState
            {
                Paused = _paused,
                Allowed = true, // the intent is always recorded now; only the world's answer can be "not yet"
                HoldingPlayers = holding,
                JoinedPlayers = joined,
                WaitingFor = waiting,
            });
        }
    }

    /// <summary>Clears every player's pause intent when the hold expires on its own, and latches the menus
    /// that were holding it (see <see cref="PlayerSession.PauseHoldExpired"/>) — otherwise the world would
    /// re-enter the hold on the very next recompute, with everybody still sitting in their menus.</summary>
    private void ClearPauseIntents()
    {
        foreach (var s in _sessions.Values)
        {
            s.PauseHoldExpired |= s.WantsPause;
            s.WantsPause = false;
        }
    }

    /// <summary>Advances the hold and releases it when it must not continue: the last holder left, a client
    /// died behind its menu, or the hold outlived its ceiling. Returns true while the world is (still) held.
    /// <para>Also runs while the world is NOT held — the tally it broadcasts has to follow players joining and
    /// leaving, not just the pause itself.</para></summary>
    private bool HoldingPause(double deltaSeconds)
    {
        if (!_paused)
        {
            RecomputePause(); // a join/leave changes what the pause dialogs are waiting for
            return false;
        }

        _pausedFor += deltaSeconds;
        double ceiling = _pauseBroadcast.Holding > 1 ? MaxGroupPauseSeconds : MaxPauseSeconds;
        if (_pausedFor >= ceiling)
        {
            _log.Info($"Pause expired after {ceiling / 60:0} min — resuming the world.");
            ClearPauseIntents();
            RecomputePause();
            return false;
        }

        SweepSilentPausedSessions(deltaSeconds); // a client that died mid-pause must not hold the world hostage
        RecomputePause(); // a swept session takes its intent with it — with nobody left the hold ends here
        return _paused;
    }

    public void Tick(double deltaSeconds)
    {
        _transport.Poll();

        // A held world still pumps the network (the unpause has to get through) and still runs the moderation /
        // maintenance intake, but no simulation advances: no hunger, no creatures, no weather, no clock.
        if (HoldingPause(deltaSeconds))
        {
            Guard("Moderation", deltaSeconds, TickModeration);
            Guard("Maintenance", deltaSeconds, TickMaintenance);

            // The control plane must keep seeing a held world (#973): the /status snapshot is what the hosted
            // fleet polls, and freezing it would report a stale player count for as long as the pause lasts.
            // Safe to run here — with players joined it only republishes; the idle timer stays at zero.
            Guard("HostedLifecycle", deltaSeconds, TickHostedLifecycle);

            // #996: an observer neither holds nor counts toward the pause (#973) and keeps flying — without
            // streaming they run off the already-sent chunks into void until somebody resumes the world.
            Guard("SpectatorChunks", StreamChunksToSpectators);
            return;
        }
        Guard("TickSpace", deltaSeconds, TickSpace); // space instances are keyed by location and handle their own players

        // Tick each occupied world with the Active cursor set to it, so its environment/fauna/fluids/
        // weather/presence/chunk-streaming only touch that world's players. With a single occupied world
        // this runs once and is identical to the old flat tick. When no world is occupied we still tick the
        // active one (so its weather/fluids advance — and so headless tests with no players still simulate).
        var ticking = OccupiedLocations();
        if (ticking.Count == 0 && _worlds.Active != null)
        {
            ticking.Add(_worlds.Active.LocationId);
        }

        // Decide once per tick whether this is a chunk-sweep tick (throttled), then run the eviction per active
        // world inside the loop so each world's anchors are its own players. See SweepFarChunks.
        _sinceChunkSweep += deltaSeconds;
        bool sweepDue = _sinceChunkSweep >= ChunkSweepIntervalSeconds;
        if (sweepDue)
        {
            _sinceChunkSweep = 0;
        }

        foreach (var locId in ticking)
        {
            if (!SetActiveWorld(locId))
            {
                continue;
            }

            // Each simulation system is contained on its own (see GameServerResilience.Guard): a system
            // that throws on edge-case data is logged + skipped for this tick, while every OTHER system in
            // this world — and every other occupied world — keeps simulating. A throw here can never reach
            // Run() and crash the process.
            Guard("TickEnvironment", deltaSeconds, TickEnvironment);
            Guard("TickEnemies", deltaSeconds, TickEnemies);
            Guard("TickBandits", deltaSeconds, TickBandits);
            Guard("TickPresence", deltaSeconds, TickPresence);
            Guard("TickFluids", deltaSeconds, TickFluids);
            Guard("TickFire", deltaSeconds, TickFire);
            Guard("TickWeather", deltaSeconds, TickWeather);
            Guard("TickFlora", deltaSeconds, TickFlora);
            Guard("TickCreatures", deltaSeconds, TickCreatures);
            Guard("TickNpcs", deltaSeconds, TickNpcs);
            Guard("TickLandedTraders", deltaSeconds, TickLandedTraders); // P3: materialize/lift-off a peaceful trader parked on this surface
            Guard("TickDoors", deltaSeconds, TickDoors);
            Guard("TickDropPackets", deltaSeconds, TickDropPackets); // #853: ground packets flow back into whoever walks over them
            Guard("TickHealTanks", deltaSeconds, TickHealTanks); // base/station regen field: heal + feed + suit recharge
            Guard("TickStationsInReach", deltaSeconds, TickStationsInReach); // #1070: Tab-menu station gates follow the player
            Guard("TickVoidRescue", deltaSeconds, TickVoidRescue);
            Guard("TickShipAi", deltaSeconds, TickShipAi); // VEGA advisor hints + memory-fragment redemption
            Guard("StreamChunks", StreamChunks);
            if (sweepDue)
            {
                Guard("SweepFarChunks", SweepFarChunks);
            }
        }

        Guard("SampleHistories", deltaSeconds, SampleHistories); // also advances _uptime
        Guard("SilentSessions", SweepSilentSessions); // release names/slots held by dead clients (#964)
        Guard("SweepExpiredLandedTraders", SweepExpiredLandedTraders); // P3: free pads of traders whose dwell ended on bodies nobody is on
        Guard("TickGreetings", TickGreetings); // push any LLM NPC greetings finished off-thread (item 15)
        Guard("TickDialogRadio", TickDialogRadio); // due "they said they'd call" dialogue consequences (#1127)
        Guard("TickNpcRadio", TickNpcRadio);   // NPC radio calls (#1119): per-player 30 s trigger scans
        Guard("TickBaseLife", TickBaseLife);   // the world notices your base (#1120): settlers move in
        Guard("TickMissionTexts", TickMissionTexts); // push mission-list refreshes when L3 board texts arrive
        Guard("TickAiMissions", TickAiMissions); // publish /ai_mission generations finished off-thread
        Guard("TickVegaBanter", TickVegaBanter); // push VEGA's LLM banter lines finished off-thread

        Guard("AccumulatePlaytime", deltaSeconds, AccumulatePlaytime);
        Guard("HostedLifecycle", deltaSeconds, TickHostedLifecycle); // idle shutdown + /status snapshot (hosted worlds)
        Guard("Maintenance", deltaSeconds, TickMaintenance); // announcement intake + restart countdown broadcasts
        Guard("Moderation", deltaSeconds, TickModeration); // kick intake + the delayed close behind it
        Guard("CrashReportFlush", deltaSeconds, MaybeFlushCrashReports); // best-effort background upload of queued reports

        _sinceAutoSave += deltaSeconds;
        if (_sinceAutoSave >= _config.AutoSaveIntervalMinutes * 60.0)
        {
            _sinceAutoSave = 0;
            if (Guard("Autosave", SaveAll))
            {
                _log.Info("Autosave complete.");
            }
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

    /// <summary>Test entrypoint mirroring the AI damage ticks (creatures/bandits/machines/speeders): a direct
    /// <see cref="RespawnPlayer"/> call, deliberately WITHOUT serving the victim first — those ticks run with
    /// the ship cursor on whoever was served last, which is exactly the #1020 death-in-a-foreign-ship setup.</summary>
    public void KillPlayerForTest(PlayerSession session, string reason) => RespawnPlayer(session, reason);

    /// <summary>Saves everything durably NOW, outside the autosave cadence — the same guarded path the
    /// periodic autosave takes. The browser singleplayer host calls this when the tab loses focus
    /// (visibility change): a WebGL page gets no reliable shutdown callback, so waiting out the autosave
    /// interval would risk losing up to that many minutes on a tab close.</summary>
    public void SaveNow()
    {
        _sinceAutoSave = 0;
        if (Guard("SaveNow", SaveAll))
        {
            _repo.Flush(); // durable now — the browser host persists its snapshot blob on this signal
            _log.Info("On-demand save complete.");
        }
    }

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
            // A deferred respawn choice is pending (issue #462): the player lies at the death spot at 0 HP.
            // No drains, no regen, no re-death — just enforce the timeout fallback to the ship.
            if (session.RespawnChoiceDeadline > 0)
            {
                if (_uptime >= session.RespawnChoiceDeadline)
                {
                    session.RespawnChoiceDeadline = 0;
                    CompleteRespawn(session, session.PendingRespawnReason, session.PendingRespawnSalvaged,
                        session.PendingRespawnSameWorld, useCustomSpawn: false);
                }

                continue;
            }

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
            // A founded base's zone is a life-support field (issue #782): the Grundstein projects air over
            // its whole protection cube — the same hand-wave station interiors already use. Any base counts,
            // not just your own: visitors breathe too, the protection rules still keep them from editing.
            // Beyond the cube, SEALED rooms connected to the core breathe too (issue #794): walls of
            // airtight full-cube blocks, energy doors in the doorways (#793) — mechanical doors leak.
            var playerCell = new Vector3i(
                (int)System.Math.Floor(p.Position.X), (int)System.Math.Floor(p.Position.Y), (int)System.Math.Floor(p.Position.Z));
            bool atBase = !p.InEva && (InAnyBaseZone(playerCell) || InSealedBaseRoom(playerCell));
            bool lifeSupport = !p.InEva && (p.AboardShip || insideShip || atBase || InStation(p.PlayerId)
                || !Rules.OxygenEnabledFor(p.ModeOverride));
            // Which source keeps this player breathing — sent to the client so the HUD can name it
            // (0 none, 1 ship cabin/aboard, 2 station, 3 base zone or sealed room). Base ranks last so
            // the label only claims the base when nothing closer (ship/station) already covers you.
            p.LifeSupportSource = (byte)(!lifeSupport ? 0
                : p.AboardShip || insideShip ? 1
                : InStation(p.PlayerId) ? 2
                : atBase ? 3 : 0);
            // Submerged underwater the suit runs on its own air, even on a breathable world — diving spends
            // the oxygen tank just like a toxic/airless atmosphere does (the extractor can't pull from water).
            // Life support overrides this (ship cabin, station, base zone): an underwater base is a dome.
            bool submerged = !lifeSupport && !p.InEva && HeadUnderwater(p);
            // Above the atmosphere (built a tower up into space) the air runs out too, even on a breathable
            // world — the suit tank drains until the player descends back below the line. Life support wins
            // over the altitude line as well, so a base founded on a peak above it still breathes.
            if (!submerged && (lifeSupport || (!p.AboveAtmosphere && !p.InEva && AtmosphereBreathable)))
            {
                // Aboard the ship (life support), boarded on a station (its life support), oxygen disabled
                // by rules, or a breathable atmosphere: regenerate, no drain (up to the tank capacity).
                // Health regen never revives a dead player (0 HP) — that would outrun the death check
                // below and quietly skip the respawn on breathable worlds.
                p.Oxygen = System.Math.Min(maxOxygen, p.Oxygen + (float)(dt * 25));
                if (p.Health > 0f)
                {
                    p.Health = System.Math.Min(100f, p.Health + (float)(dt * 2));
                }

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

            // Extreme heat / cold / vacuum stress the suit (#666): climate control drains suit energy
            // first (insulation gear slows it), an empty suit means slow exposure damage.
            TickTemperature(session, dt);

            // Hunger (survival): aboard the ship, boarded on a station (both have life support), or when
            // disabled, sate; otherwise drain and, once empty, starve (health loss until the player eats).
            if (p.AboardShip || InStation(p.PlayerId) || !Rules.HungerEnabledFor(p.ModeOverride))
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
                RespawnPlayer(session, "@srv.death.critical");
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
            Reject(session, "ration", "@srv.ration.food_only");
            return;
        }

        var p = session.State;
        int want = System.Math.Min(System.Math.Max(1, count), p.Inventory.CountOf(itemKey));
        if (want <= 0)
        {
            Reject(session, "ration", "@srv.ration.no_food");
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
            Reject(session, "ration", "@srv.ration.full");
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
        // Deaths dealt by AI ticks (creatures, guardians, bandits, speeder crashes) arrive here with the
        // ship cursor still on whoever the server served last — everything downstream (_ship/_shipPlaced/
        // _healTank) would resolve to THAT player's ship, respawning the victim inside someone else's hull
        // (#1020, same class as #997). Pin the cursor to the dying player before any of it is read.
        SetCurrent(session);

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

        // On foot on a planet your ship is already there (you land with it) — a plain heal-tank snap. You only
        // need a world transition if you died away from your ship's world: in the flight view, on a spacewalk,
        // inside the ship, or boarded on a station.
        bool sameWorld = !wasInFlightView && !wasInShipInterior && !InStation(p.PlayerId);

        // Respawn choice (issue #462): with a home spawn set (heal tank in a base/station, issue #461) the
        // relocation is DEFERRED — the death screen offers ship vs home and answers with RespawnChoiceIntent.
        // Until then the player lies at the death spot at 0 HP: the environment tick skips pending players
        // (no drains, no re-death) and enforces the timeout fallback to the ship.
        if (!string.IsNullOrEmpty(p.CustomSpawnBodyId))
        {
            p.Health = 0f;
            session.RespawnChoiceDeadline = _uptime + RespawnChoiceTimeout;
            session.PendingRespawnSalvaged = salvaged;
            session.PendingRespawnSameWorld = sameWorld;
            session.PendingRespawnReason = reason;
            Send(session, new RespawnOptions
            {
                Reason = reason,
                SalvageCapsuleDropped = salvaged,
                CustomLabel = p.CustomSpawnLabel,
            });
            SendInventory(session); // the salvage capsule already emptied the pockets — reflect it under the modal
            _repo.SavePlayer(p);    // a disconnect mid-choice re-offers the choice on the next join (health 0)
            _log.Info($"Player '{p.Name}' died — respawn choice offered (home '{p.CustomSpawnLabel}').");
            return;
        }

        CompleteRespawn(session, reason, salvaged, sameWorld, useCustomSpawn: false);
    }

    /// <summary>Seconds a deferred respawn choice may stay unanswered before the ship respawn runs.</summary>
    private const double RespawnChoiceTimeout = 30.0;

    /// <summary>The relocation half of a death: resets vitals and places the player at the ship heal-tank
    /// (classic behaviour) or, on request, at their home spawn (issue #462) with the ship as fallback.</summary>
    private void CompleteRespawn(PlayerSession session, string reason, bool salvaged, bool sameWorld, bool useCustomSpawn)
    {
        var p = session.State;
        p.Health = 100f;
        p.Oxygen = MaxOxygen(p);
        p.SuitEnergy = 100f;
        p.Hunger = 100f;
        p.Stealthed = false;
        p.Seated = false; // death stands you up (#806)
        p.InEva = false; // a death ends any spacewalk
        _inShipInterior.Remove(p.PlayerId); // and any in-ship walkabout
        _dockedFromEva.Remove(p.PlayerId);  // and any "ship floating while docked" memory

        if (useCustomSpawn && TryCustomRespawn(session, reason, salvaged, sameWorld))
        {
            _repo.SavePlayer(p);
            _log.Info($"Player '{p.Name}' respawned at their home spawn '{p.CustomSpawnLabel}' (salvage={salvaged}).");
            return;
        }

        if (useCustomSpawn)
        {
            // The home attempt may have loaded another world before failing — the ship transition below
            // reloads the ship's own world, so the plain same-world snap is no longer safe.
            sameWorld = false;
        }

        // Dying while boarded used to leave the station membership behind (InStation stayed true after the
        // recovery to the ship — permanent free life support). Always drop it here: every non-station respawn
        // target below leaves the station, and the station home spawn re-registers it itself.
        _boardedStation.Remove(p.PlayerId);

        if (sameWorld)
        {
            // Died on the ship's own world on foot — snap to the heal-tank, no loading screen.
            p.Position = p.RespawnPoint;
            session.AwaitingSpawnAdopt = true; // #865: ignore death-spot reports until the client snaps
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

    /// <summary>The player's answer to a deferred respawn choice (no-op without one pending, so a duplicate
    /// or late packet can't double-respawn).</summary>
    private void HandleRespawnChoice(PlayerSession session, RespawnChoiceIntent choice)
    {
        if (session.RespawnChoiceDeadline <= 0)
        {
            return;
        }

        session.RespawnChoiceDeadline = 0;
        CompleteRespawn(session, session.PendingRespawnReason, session.PendingRespawnSalvaged,
            session.PendingRespawnSameWorld, choice.UseCustomSpawn);
    }

    /// <summary>Death recovery with a world transition: lands the player at their ship's heal-tank on the
    /// ship's planet, leaving any space instance first so the client drops out of the flight view.</summary>
    private void RecoverToShip(PlayerSession session, string reason, bool salvaged)
    {
        var p = session.State;
        // Pin the ship cursor BEFORE the first _ship read: this runs from death paths where the cursor may
        // still point at another player (#1020) — reading (or re-homing, below) _ship then targets the
        // wrong player's ship and recovers the victim to the world THAT ship is parked on.
        SetCurrent(session);
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
        session.AwaitingSpawnAdopt = true; // #865: ignore death-spot reports until the client snaps
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

    /// <summary>Upper bound on a client-requested render distance (matches the in-game slider's max), so a
    /// spoofed JoinRequest can't make the server stream/generate an enormous column (memory/CPU DoS).</summary>
    private const int MaxClientViewDistanceChunks = 8;

    /// <summary>Horizontal radius (chunks, Chebyshev) within which the FULL vertical span streams — so caves,
    /// overhangs and digging straight down near the player are always covered. Beyond it, only the surface band
    /// (below) streams. Small view distances (≤ this) are therefore unaffected by the vertical LOD.</summary>
    private const int NearFullColumnRadius = 3;

    /// <summary>For far columns, how many chunks below / above the column's surface chunk still stream — the
    /// visible shell of distant terrain (cliffs just under the surface, trees/features just above). Kept small;
    /// fog hides the far edge, so a tall distant cliff cropping a chunk low is acceptable for the perf win.</summary>
    private const int FarSurfaceBandBelow = 1;
    private const int FarSurfaceBandAbove = 1;

    /// <summary>Total height cap (chunks) for a far column's band. A SUBMERGED column stretches its band from the
    /// seabed up to the sea SURFACE (see <see cref="StreamChunks"/>), which a flooded rift could make hundreds of
    /// blocks tall; this caps that. Trimming happens at the BOTTOM — the surface is what the player actually sees,
    /// and the deep seabed under it is hidden by the underwater haze anyway.</summary>
    private const int FarColumnMaxChunks = 6;

    /// <summary>The chunk-Y band a FAR column streams: from just below its terrain surface up to just above its
    /// VISIBLE top — the waterline on a submerged column, the ground itself on a dry one. Pure arithmetic, so the
    /// rule is unit-testable without spinning up a world. <paramref name="seaLevel"/> is int.MinValue on a dry
    /// world (and below the terrain on any column that stands above the sea), which leaves the band exactly where
    /// it was before #987.</summary>
    internal static (int LoCy, int HiCy) FarColumnBand(int surfaceY, int seaLevel)
    {
        int surfCy = WorldConstants.WorldToChunk(surfaceY);
        int hiCy = (seaLevel > surfaceY ? WorldConstants.WorldToChunk(seaLevel) : surfCy) + FarSurfaceBandAbove;
        // Trim at the BOTTOM when a very deep body would blow the cap: the waterline is what the player sees, the
        // seabed far below it is lost in the underwater haze (and the client culls fluid faces toward the chunks
        // we never sent, so the cut stays invisible).
        return (System.Math.Max(surfCy - FarSurfaceBandBelow, hiCy - (FarColumnMaxChunks - 1)), hiCy);
    }

    /// <summary>Extra chunk rings streamed BEYOND the player's view radius (and beyond the client's fog edge, which
    /// sits at the view radius). This terrain loads while it is still fully hazed, so as the player walks forward the
    /// newly-revealed edge is already meshed and simply fades in through the fog instead of popping in from nothing
    /// (#388). The extra ring is always past <see cref="NearFullColumnRadius"/>, so it streams only the cheap far
    /// surface band, and stays within the sweep's keepRadius (maxViewRadius + 4) so it is not immediately evicted.</summary>
    private const int LoadAheadRings = 1;

    /// <summary>This player's streaming radius in chunks: their requested view distance (clamped to the slider
    /// range) when they sent one, otherwise the host's configured default.</summary>
    private int EffectiveViewRadius(PlayerSession session)
        => session.ViewDistance > 0
            ? System.Math.Clamp(session.ViewDistance, 1, MaxClientViewDistanceChunks)
            : System.Math.Max(1, _config.ViewDistanceChunks);

    private void StreamChunks() => StreamChunks(spectatorsOnly: false);

    /// <summary>Chunk streaming for observers while the world is held paused (#996): spectators don't hold
    /// the pause and keep moving, but the paused tick skips the per-world simulation loop (and with it
    /// <see cref="StreamChunks()"/>) entirely. Non-spectators sit in their pause menus and have no use for
    /// chunks until the world resumes.</summary>
    private void StreamChunksToSpectators()
    {
        if (!_sessions.Values.Any(s => s.Joined && s.Spectating))
        {
            return; // the common case — nobody is observing
        }

        foreach (var locId in OccupiedLocations())
        {
            if (SetActiveWorld(locId))
            {
                StreamChunks(spectatorsOnly: true);
            }
        }
    }

    private void StreamChunks(bool spectatorsOnly)
    {
        int perTickBudget = System.Math.Max(1, _config.ChunkStreamPerTick);

        // Optional wall-clock budget (browser singleplayer: the tick shares the render thread). Started
        // before any generation so a burst of expensive first-visit gens is cut off mid-loop; at least one
        // chunk per tick always goes out so streaming can never starve entirely.
        var streamTimer = _config.ChunkStreamBudgetMs > 0
            ? System.Diagnostics.Stopwatch.StartNew()
            : null;

        // Chunk band the build height maps to — the streamed column is clamped into it so a spoofed player
        // position can't make the server generate/cache chunks at arbitrary heights (memory DoS). See MinBuildY.
        int minChunkY = WorldConstants.WorldToChunk(MinBuildY);
        int maxChunkY = WorldConstants.WorldToChunk(MaxBuildY);

        foreach (var session in JoinedInActiveWorld())
        {
            if (spectatorsOnly && !session.Spectating)
            {
                continue; // paused-world streaming (#996) serves only the observers
            }

            int radius = EffectiveViewRadius(session); // per-player: honour the client's View Distance slider
            int streamRadius = radius + LoadAheadRings; // load one hazed ring past the fog edge so it fades in, not pops (#388)
            var center = WorldConstants.WorldToChunk(session.State.Position.ToBlock());
            center = new ChunkCoord(center.X, System.Math.Clamp(center.Y, minChunkY, maxChunkY), center.Z);

            // Collect the not-yet-sent chunks in the view column and stream them NEAREST-FIRST. The player's
            // own chunk (its floor) then loads before everything else, so a freshly spawned/teleported player
            // gets solid ground under them immediately instead of falling through while a fixed bottom-up
            // order slowly works up toward the surface (which, on a fresh world's slow first-gen + a large
            // view distance, could outlast the client's settle-freeze and drop them below the terrain).
            //
            // Distance-based vertical LOD: near the player (Chebyshev ≤ NearFullColumnRadius) the FULL vertical
            // span streams so digging down / walking into caves never outruns the terrain. Beyond that, only the
            // band around THAT column's actual surface streams — the deep underground + high air far away are
            // never seen, so skipping them roughly halves the chunk count at a large view distance (faster fill,
            // lighter client). Surface-relative (not player-relative) so a distant valley or peak well off the
            // player's own altitude still streams its visible shell.
            var planet = _world.Planet;
            int seaLevel = _generator.SeaLevel(planet); // int.MinValue on a dry world; cached per world
            var pending = new List<(ChunkCoord Coord, int DistSq)>();
            for (int dx = -streamRadius; dx <= streamRadius; dx++)
                for (int dz = -streamRadius; dz <= streamRadius; dz++)
                {
                    int loDy, hiDy;
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) <= NearFullColumnRadius)
                    {
                        loDy = -3;
                        hiDy = 2; // full near column (unchanged behaviour)
                    }
                    else
                    {
                        int worldX = (center.X + dx) * WorldConstants.ChunkSize + WorldConstants.ChunkSize / 2;
                        int worldZ = (center.Z + dz) * WorldConstants.ChunkSize + WorldConstants.ChunkSize / 2;
                        // SurfaceHeight is the TERRAIN top — under a sea that is the seabed, and the generator
                        // fills everything from there up to the sea level with the sea fluid. Anchoring the band
                        // at the seabed therefore cut a deep ocean off mid-water, and the client (which reads a
                        // missing chunk as air) rendered that cut as a wavy water SURFACE hanging in mid-water,
                        // with fake waterfall streaks down the band's side edges (#987). FarColumnBand stretches
                        // a submerged column up to its real waterline instead.
                        var band = FarColumnBand(_generator.SurfaceHeight(planet, worldX, worldZ), seaLevel);
                        loDy = band.LoCy - center.Y;
                        hiDy = band.HiCy - center.Y;
                    }

                    for (int dy = loDy; dy <= hiDy; dy++)
                    {
                        int cy = center.Y + dy;
                        if (cy < minChunkY || cy > maxChunkY)
                        {
                            continue; // never stream/generate outside the build-height band
                        }

                        // Canonicalize longitude so chunks just west of the seam (center.X+dx < 0) stream as the
                        // wrapped chunk from the far side — the player can see across X = 0 ≡ X = Circumference.
                        var coord = WorldConstants.CanonicalChunk(new ChunkCoord(center.X + dx, cy, center.Z + dz), _world.Circumference);
                        if (session.SentChunks.Contains(coord))
                        {
                            continue;
                        }

                        pending.Add((coord, dx * dx + dy * dy + dz * dz));
                    }
                }

            pending.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

            int sent = 0;
            foreach (var (coord, _) in pending)
            {
                if (sent >= perTickBudget)
                {
                    break;
                }

                // Time budget spent (see above) — but only after at least one send, so progress is guaranteed.
                if (streamTimer != null && sent > 0 && streamTimer.Elapsed.TotalMilliseconds >= _config.ChunkStreamBudgetMs)
                {
                    break;
                }

                if (session.SentChunks.Contains(coord))
                {
                    continue; // two view offsets can map to the same wrapped chunk — send it once
                }

                var chunk = _world.GetOrLoadChunk(coord);
                var dense = chunk.ToArray();
                // Ship the run-length-encoded payload when it is smaller (terrain almost always is; a rare
                // unrunnable chunk goes dense). Decisive on the browser JSON path — ~15-25 KB of JSON
                // numbers per chunk shrink to usually a few hundred bytes — and it trims native payloads
                // and VPS egress too. The client accepts both representations (ChunkDataMessage.DecodeBlocks).
                var rle = ChunkBlocksRle.Encode(dense);
                var msg = new ChunkDataMessage
                {
                    Cx = coord.X,
                    Cy = coord.Y,
                    Cz = coord.Z,
                };
                if (rle.Length < dense.Length)
                {
                    msg.BlocksRle = rle;
                }
                else
                {
                    msg.Blocks = dense;
                }
                PackChunkModifiers(chunk, msg); // dyed-block / coloured-light cells, if any
                Send(session, msg);
                session.SentChunks.Add(coord);
                MarkExploredCell(session, coord); // #1113: the planet map remembers this across sessions
                sent++;
            }
        }
    }

    /// <summary>How often (seconds) the server evicts cached chunks that drifted out of every player's keep-range.
    /// Coarse on purpose: chunk caching is cheap and re-loading is on-demand, so a slow sweep is plenty to keep
    /// memory bounded without scanning the whole cache every tick.</summary>
    private const double ChunkSweepIntervalSeconds = 10.0;

    /// <summary>Evicts cached chunks in the active world that fall outside the keep-range of every joined player,
    /// bounding server memory on long exploration (the cache otherwise only ever grew). The keep radius sits a
    /// few chunks beyond the streaming radius so a chunk the player can currently see is never dropped; chunks
    /// regenerate on demand (with persisted edits re-applied) if the player returns. The client unloads its own
    /// far chunks too (~384 blocks, #966), so each session's sent-set is also pruned by that session's OWN
    /// distance below — the cache eviction alone only forgets chunks far from EVERY player, which left a
    /// returning player's sent-set stale wherever another player kept the area alive (#1030). Honours <see cref="ServerConfig.MaxLoadedChunksPerPlayer"/>
    /// in spirit by keeping the resident set proportional to the view, not the distance travelled.</summary>
    private void SweepFarChunks()
    {
        var anchors = new List<ChunkCoord>();
        int maxViewRadius = 1;
        foreach (var session in JoinedInActiveWorld())
        {
            anchors.Add(WorldConstants.WorldToChunk(session.State.Position.ToBlock()));
            maxViewRadius = System.Math.Max(maxViewRadius, EffectiveViewRadius(session));
        }

        if (anchors.Count == 0)
        {
            return; // nobody here — leave the cache as-is (an idle world isn't growing it)
        }

        // Keep a margin beyond the widest player's horizontal streaming radius so the diagonal/vertical fringe of
        // the streamed column (dy -3..+2) is never evicted while still in view. A single shared keep radius (the
        // max across players) is safe: it can only keep MORE than any one player needs, never less.
        int keepRadius = maxViewRadius + 4;
        var removed = _world.UnloadFarChunks(anchors, keepRadius);

        // Also drop the evicted coords from every player's sent-set. A swept chunk is far from EVERY anchor (that
        // is the sweep's condition), so this is safe for all sessions — and it lets the client unload the same far
        // chunks (bounding its own memory) and still get them re-streamed fresh if it walks back into range.
        if (removed.Count > 0)
        {
            foreach (var session in JoinedInActiveWorld())
            {
                foreach (var coord in removed)
                {
                    session.SentChunks.Remove(coord);
                }
            }
        }

        // The eviction above only forgets chunks that are far from EVERY player — but the client unloads by its
        // own distance alone (RepositionChunks, ~384 blocks = 24 chunks). So while another player camped in an
        // area, its chunks stayed cached AND stayed in a departed player's sent-set even though that player's
        // client had long discarded them; on return, StreamChunks skipped them as "already sent" and the
        // returner stood in void terrain the server actually had ("/tpp … I only see space", #1030). Prune each
        // sent-set by ITS OWN session's anchor too. The prune radius must stay below the client's 24-chunk
        // unload distance (or a client-unloaded chunk could survive in the sent-set); a chunk pruned while the
        // client still holds it merely re-streams when it re-enters the view, which is idempotent.
        foreach (var session in JoinedInActiveWorld())
        {
            var anchor = WorldConstants.WorldToChunk(session.State.Position.ToBlock());
            int pruneRadius = System.Math.Min(EffectiveViewRadius(session) + 4, 20);
            int pruneSq = pruneRadius * pruneRadius;
            int circumference = _world.Circumference;
            session.SentChunks.RemoveWhere(c => WrappedChunkDistanceSquared(c, anchor, circumference) > pruneSq);
        }
    }

    /// <summary>Squared chunk-grid distance measured the short way round BOTH seams (X wraps at the chunk
    /// circumference, Z at the latitude chunk band; Y is linear). The sent-set prune must not read a chunk just
    /// across a seam as "far", or a player standing near a seam would re-stream half their view every sweep.</summary>
    private static int WrappedChunkDistanceSquared(ChunkCoord a, ChunkCoord b, int circumference)
    {
        int dx = WrapChunkDelta(a.X - b.X, WorldConstants.ChunksAroundOf(circumference));
        int dy = a.Y - b.Y;
        int dz = WrapChunkDelta(a.Z - b.Z, WorldConstants.LatitudePeriodFor(circumference) / WorldConstants.ChunkSize);
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Shortest signed delta on a wrapping chunk axis with the given period (chunk-unit twin of
    /// <see cref="WorldConstants.WrapDeltaX(int,int)"/>, whose parameter is a BLOCK circumference).</summary>
    private static int WrapChunkDelta(int delta, int period)
    {
        int m = ((delta % period) + period) % period;
        return m > period / 2 ? m - period : m;
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

    /// <summary>Seconds a chunk is exempt from a full ghost re-stream after it just had one (#965).</summary>
    private const double GhostRestreamCooldown = 10.0;

    /// <summary>Heals a stale client chunk view (a "ghost" block the server no longer has): confirms the cell's
    /// authoritative block immediately, and — only if the same chunk ghosts REPEATEDLY — forgets the chunk on
    /// this session so <see cref="StreamChunks"/> re-sends the whole authoritative chunk.
    /// <para>The corrective <see cref="BlockChanged"/> fixes the normal single-cell case on its own. Re-streaming
    /// the full chunk on EVERY ghost was a bandwidth/CPU amplifier: one ghost cost a whole chunk on the wire plus
    /// seven chunk remeshes on the client, and a client that double-sent its mine intents (#965) produced one per
    /// mined block. Returns whether the caller should log the ghost — the log is rate-limited with the
    /// re-stream so a mining session can no longer spam hundreds of warnings.</para></summary>
    private bool ResyncStaleChunk(PlayerSession session, Vector3i pos)
    {
        var (rsTint, rsGlow) = _world.GetModifier(pos);
        Send(session, new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = _world.GetBlock(pos).Value, Tint = rsTint, Glow = rsGlow, Shape = _world.GetShape(pos) });

        var coord = WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(pos), _world.Circumference);
        if (session.GhostChunkSeen.TryGetValue(coord, out double lastAt) && _uptime - lastAt < GhostRestreamCooldown)
        {
            return false; // already re-streamed this chunk moments ago — the BlockChanged above is enough
        }

        session.GhostChunkSeen[coord] = _uptime;
        session.SentChunks.Remove(coord); // not-sent again → StreamChunks re-streams it on the next tick
        return true;
    }

    // ---------------- Connection handling ----------------

    private void OnClientConnected(int connectionId)
    {
        // Session is created on a successful JoinRequest; just note the pending connection.
        _log.Info($"Connection {connectionId} opened; awaiting join.");
    }

    /// <summary>The live session holding a player name (case-insensitive), or null. One session per name:
    /// PlayerId == name, so two clients under one name would alias the same player state.</summary>
    private PlayerSession? FindJoinedSessionByName(string name)
    {
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && string.Equals(s.State.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Seconds without a single payload after which a joined session is considered dead (#964).
    /// The transport cannot see this case: a client whose game froze or whose machine died mid-frame can keep
    /// answering pings from its network thread, so only the absence of INTENTS proves nobody is playing.
    /// Generously above any legitimate quiet period — a playing client sends movement/pose updates
    /// continuously, and a paused one sends the pause keep-alive (see <see cref="SweepSilentPausedSessions"/>,
    /// which applies the same budget on a clock that keeps running while the world does not).</summary>
    private const double SessionHeartbeatTimeout = 90.0;

    /// <summary>Drops joined sessions that have gone silent (see <see cref="SessionHeartbeatTimeout"/>), so a
    /// crashed player's name and slot are released long before the transport notices.</summary>
    private void SweepSilentSessions()
    {
        List<int>? dead = null;
        foreach (var (connectionId, session) in _sessions)
        {
            if (session.Joined && session.HeartbeatTracked && _uptime - session.LastPayloadAt > SessionHeartbeatTimeout)
            {
                (dead ??= new List<int>()).Add(connectionId);
            }
        }

        DropSilentSessions(dead);
    }

    /// <summary>
    /// Drops clients that fell silent WHILE THE WORLD STOOD STILL (#973). The normal sweep above cannot see
    /// them: it ages sessions against <c>_uptime</c>, which a simulation system advances — and a held world
    /// runs no simulation, so every heartbeat freezes along with the clock.
    /// <para>
    /// Two things go wrong without this pass. A player whose client crashes behind its pause menu squats
    /// their name and slot for the whole hold — up to <see cref="MaxGroupPauseSeconds"/> — which is exactly
    /// the rejoin lockout #964 removed for a running world. And if EVERY paused client dies (a host machine
    /// going to sleep), nobody is left to resume: the world sits frozen, saving nothing, until the ceiling.
    /// </para>
    /// <para>
    /// Only clients that have shown they send the pause keep-alive are swept. One from before #973 sends
    /// nothing at all while its menu is open — dropping it for that would be a regression, not a fix — so a
    /// mixed-version world simply keeps the old behaviour and waits out the ceiling.
    /// </para>
    /// </summary>
    private void SweepSilentPausedSessions(double deltaSeconds)
    {
        List<int>? dead = null;
        foreach (var (connectionId, session) in _sessions)
        {
            if (!session.Joined || !session.HeartbeatTracked || !session.SendsPauseKeepAlive)
            {
                continue;
            }

            session.PausedSilentSeconds += deltaSeconds;
            if (session.PausedSilentSeconds > SessionHeartbeatTimeout)
            {
                (dead ??= new List<int>()).Add(connectionId);
            }
        }

        DropSilentSessions(dead);
    }

    /// <summary>Disconnects the sessions a heartbeat sweep found dead, logging each one.</summary>
    private void DropSilentSessions(List<int>? dead)
    {
        if (dead is null)
        {
            return;
        }

        foreach (int connectionId in dead)
        {
            string who = _sessions.TryGetValue(connectionId, out var s) ? s.State.Name : "?";
            _log.Warn($"Player '{who}' sent nothing for {SessionHeartbeatTimeout:0}s — dropping the session (connection {connectionId}).");
            _transport.DisconnectClient(connectionId);
            OnClientDisconnected(connectionId);
        }
    }

    private void OnClientDisconnected(int connectionId)
    {
        _joinGates.Remove(connectionId); // transport connection ids may be reused — a new connection starts fresh
        if (_sessions.TryGetValue(connectionId, out var session) && session.Joined)
        {
            ClearDocking(session.State.PlayerId);
            LeaveSpace(session.State.PlayerId);
            LeaveStation(session.State.PlayerId);
            CancelTradesFor(session.State.PlayerId);
            SetCurrent(session);
            SaveFleet(session); // the whole fleet + the fleet index on the state, before it is written below
            _repo.SavePlayer(session.State);

            string loc = session.CurrentLocationId;
            _sessions.Remove(connectionId);
            ClearAlliancePending(session.State.PlayerId); // drop transient requests; refresh online allies' rosters
            SetActiveWorld(loc);
            RemoveLandedShip(session); // the parked ship object leaves with its owner (ship-as-object)
            RemoveConstructionSite(session); // the half-built hull despawns too — it lives on in the fleet save
            BroadcastToWorld(new PlayerLeft { PlayerId = session.State.PlayerId }); // remove their avatar in-world
            BroadcastLandingPads(); // the leaver's pad is free again — everyone's map must show it (#1020)
            foreach (var other in _sessions.Values)
            {
                if (other.Joined && other.State.IsAdmin)
                {
                    SendRules(other); // the admins' player-mode roster (#1121) loses this player
                }
            }

            if (!string.IsNullOrEmpty(loc) && loc != _meta.ActiveLocationId && !OccupiedLocations().Contains(loc))
            {
                // Move the cursor off the world we're about to drop, back to the (always-resident) default
                // body. Without this the Unload would target the world the disconnect just made Active and
                // silently no-op, leaking that world (and leaving the empty server ticking the orphan).
                SetActiveWorld(_meta.ActiveLocationId);
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
            // Re-join guard (#424 S8): a joined connection re-sending JoinRequest would reload the player
            // from the DB (rolling back progress since the last autosave) and re-run the full ~40-message
            // join burst — an asymmetric amplifier. A legitimate client never re-joins on a live
            // connection, so drop it without a reply (an answer would feed the amplifier).
            if (_sessions.TryGetValue(connectionId, out var existing) && existing.Joined)
            {
                _log.Warn($"Connection {connectionId} sent a JoinRequest while already joined — dropped.");
                return;
            }

            // Flood gate for the join path (#424 S8): joins arrive before a session (and its token bucket)
            // exists, so they get their own per-connection bucket. Even a rejected join does DB/crypto work,
            // and an accepted one is the most expensive message the server has.
            if (!AllowJoinAttempt(connectionId))
            {
                return;
            }

            // Guarded like every other handler (#964): HandleJoin registers the session BEFORE its ~40-message
            // burst, so an exception midway used to leave a half-built session that held the player's name
            // forever — with no way for them to get back in.
            try
            {
                HandleJoin(connectionId, join);
            }
            catch (Exception ex)
            {
                _log.Error($"Join from connection {connectionId} threw: {ex}");
                try
                {
                    // #998: the join burst may already have parked the player's ship object
                    // (SetupPlayerShip) — plain session removal left it orphaned in the world with no
                    // owner to ever clean it up. Tear the world half down, but deliberately do NOT
                    // save: the session may be half-restored, and persisting partial state could
                    // clobber the real save the retry-join is about to load.
                    if (_sessions.TryGetValue(connectionId, out var half))
                    {
                        LeaveSpace(half.State.PlayerId);
                        if (SetActiveWorld(half.CurrentLocationId))
                        {
                            RemoveLandedShip(half);
                            RemoveConstructionSite(half);
                            BroadcastToWorld(new PlayerLeft { PlayerId = half.State.PlayerId });
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    _log.Error($"Join-failure cleanup for connection {connectionId} threw: {cleanupEx}");
                }

                _sessions.Remove(connectionId); // never leave a half-joined session holding the name
                SendTo(connectionId, new JoinRejected { Reason = "@srv.join.failed" });
            }

            return;
        }

        if (!_sessions.TryGetValue(connectionId, out var session) || !session.Joined)
        {
            return; // ignore gameplay intents before joining
        }

        session.LastPayloadAt = _uptime; // app-level heartbeat (#964) — see SweepSilentSessions
        session.PausedSilentSeconds = 0; // the same signal on a clock that runs while the world is held (#973)

        // Per-connection flood gate: a token bucket refilled at MsgRatePerSecond, capped at MsgBurst.
        // Every joined intent costs one token; when the bucket is empty the packet is dropped. This bounds
        // the single-threaded tick against a client that spams cheap-to-send intents (Move/Mine/Place/
        // SetFace/star-map requests) far faster than a human ever could (audit 2026-07-05). Generous enough
        // that legitimate bursts (movement + block edits) never notice.
        if (!AllowMessage(session))
        {
            return;
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

    // Token-bucket flood gate constants: a human client peaks well under 100 intents/s; 200/s sustained
    // with a 400-token burst never throttles real play but caps a scripted flood cheaply.
    private const double MsgRatePerSecond = 200.0;
    private const double MsgBurst = 400.0;

    // Join-attempt gate (#424 S8): a client joins once per connection; retries after a rejection are
    // human-paced. 1/s with a burst of 5 never throttles a real client but caps a scripted join flood.
    private const double JoinRatePerSecond = 1.0;
    private const double JoinBurst = 5.0;

    private sealed class JoinGate
    {
        public double Budget = JoinBurst;
        public int LastRefillTick;
    }

    // Per-connection pre-join token buckets; entries are dropped when the connection closes.
    private readonly Dictionary<int, JoinGate> _joinGates = new();

    /// <summary>Same token-bucket scheme as <see cref="AllowMessage"/> but for pre-session join attempts,
    /// keyed by connection id. Returns false when the connection's join budget is exhausted (drop it).</summary>
    private bool AllowJoinAttempt(int connectionId)
    {
        if (!_joinGates.TryGetValue(connectionId, out var gate))
        {
            _joinGates[connectionId] = gate = new JoinGate { LastRefillTick = Environment.TickCount };
        }

        int now = Environment.TickCount;
        int elapsedMs = unchecked(now - gate.LastRefillTick);
        if (elapsedMs is < 0 or > 60_000)
        {
            elapsedMs = 0; // clock wrap or a long gap — just don't over-refill
        }

        gate.LastRefillTick = now;
        gate.Budget = System.Math.Min(JoinBurst, gate.Budget + (elapsedMs / 1000.0 * JoinRatePerSecond));
        if (gate.Budget < 1.0)
        {
            return false;
        }

        gate.Budget -= 1.0;
        return true;
    }

    /// <summary>Refills the session's token bucket by elapsed wall time and consumes one token. Returns
    /// false when the bucket is empty (drop the message).</summary>
    private static bool AllowMessage(PlayerSession session)
    {
        int now = Environment.TickCount;
        int elapsedMs = unchecked(now - session.LastMsgRefillTick);
        if (elapsedMs is < 0 or > 60_000)
        {
            elapsedMs = 0; // clock wrap or a long gap — just don't over-refill
        }

        session.LastMsgRefillTick = now;
        session.MsgBudget = System.Math.Min(MsgBurst, session.MsgBudget + (elapsedMs / 1000.0 * MsgRatePerSecond));
        if (session.MsgBudget < 1.0)
        {
            return false;
        }

        session.MsgBudget -= 1.0;
        return true;
    }

    /// <summary>Messages still served while the world is held paused (#995): the resume path itself, chat
    /// and voice (players coordinating the resume), diagnostics, explicit saves, harmless UI state,
    /// read-only requests and admin commands. Everything else — movement, mining, building, crafting,
    /// combat, trading — would mutate a world whose simulation (threats, hunger, clock) is frozen.</summary>
    private static bool PausedMayHandle(object message) => message switch
    {
        PauseIntent or ChatIntent or VoiceFrame or BumpReport or SaveGameIntent
            or SelectHotbarIntent or AdminCommandIntent => true,
        RequestStarMap or RequestMissions or RequestCompanionsIntent
            or RequestAllianceListIntent or RequestLandingPadsIntent => true,
        _ => false,
    };

    private void Dispatch(PlayerSession session, object message)
    {
        // Observer mode is read-only apart from block removal (issue #487): an invisible admin who could
        // craft, loot, trade or shoot would change a world nobody can see them in. Dropped silently — the
        // client already hides the affordances, so a rejection toast would just be noise.
        if (session.Spectating && !SpectatorMayHandle(message))
        {
            return;
        }

        // #995: while every player holds the world paused, the simulation is frozen — so gameplay intents
        // must not mutate the frozen world either. A stock client sends nothing from its pause menu, so this
        // only stops a modified client from mining/building/moving while everyone else's clock stands still.
        // Control-plane traffic stays live (the resume path, chat, saves, read-only requests, admin
        // commands), and spectators are exempt: they never hold the pause and keep moving (#996).
        if (_paused && !session.Spectating && !PausedMayHandle(message))
        {
            return;
        }

        switch (message)
        {
            case MoveIntent move: HandleMove(session, move); break;
            case SelectHotbarIntent hotbar: session.State.SelectedHotbarSlot = System.Math.Clamp(hotbar.Slot, 0, HotbarSlots - 1); break;
            case MoveItemIntent moveItem: HandleMoveItem(session, moveItem); break;
            case DiscardItemIntent discard: HandleDiscardItem(session, discard); break;
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
            case SetSpawnPointIntent spawnPoint: HandleSetSpawnPoint(session, spawnPoint); break;
            case PauseIntent pause: HandlePause(session, pause); break;
            case RespawnChoiceIntent respawnChoice: HandleRespawnChoice(session, respawnChoice); break;
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
            case ContributeRelayIntent relay: HandleContributeRelay(session, relay); break; // #1125
            case TalkToNpcIntent talk: HandleTalkToNpc(session, talk); break; // #1127
            case NpcDialogChoiceIntent dialogChoice: HandleNpcDialogChoice(session, dialogChoice); break; // #1127
            case EnterShipIntent: EnterShipInterior(session.State.PlayerId); break;
            case ExitShipIntent: ExitShipToFlight(session.State.PlayerId); break;
            case LeaveSpaceIntent leaveSpace: HandleLeaveSpace(session, leaveSpace); break;
            case FireWeaponIntent fire: HandleFireWeapon(session, fire); break;
            case AttackEntityIntent attack: HandleAttackEntity(session, attack); break;
            case ShootBlockIntent shot: HandleShootBlock(session, shot); break;
            case UseStationIntent use: HandleUseStation(session, use); break;
            case LocateStationIntent locate: HandleLocateStation(session, locate); break; // #1072
            case SetAppearanceIntent appearance: HandleSetAppearance(session, appearance); break;
            case SetFaceIntent face: HandleSetFace(session, face); break;
            case SetBodyPaintIntent bodyPaint: HandleSetBodyPaint(session, bodyPaint); break;
            case PaintBlockIntent paint: HandlePaintBlock(session, paint); break;
            case PaintCraftIntent paintCraft: HandlePaintCraft(session, paintCraft); break;
            case CustomShapeCraftIntent form: HandleCustomShapeCraft(session, form); break;
            case CraftShipIntent craftShip: HandleCraftShip(session, craftShip); break;
            case SwitchShipIntent switchShip: HandleSwitchShip(session, switchShip); break;
            case ConsumeItemIntent consume: HandleConsume(session, consume); break;
            case UseGadgetIntent gadget: HandleUseGadget(session, gadget); break;
            case TameRespondIntent tameResp: HandleTameRespond(session, tameResp); break;
            case BanditResponseIntent banditResp: HandleBanditResponse(session, banditResp); break;
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
            case SetContainerFilterIntent filter: HandleSetContainerFilter(session, filter); break;
            case MoveCargoItemIntent moveCargo: HandleMoveCargoItem(session, moveCargo); break;
            case ShipMoveIntent shipMove: HandleShipMove(session, shipMove); break;
            case DisassembleIntent disassemble: HandleDisassemble(session, disassemble); break;
            case ClaimStructureIntent claim: HandleClaimStructure(session, claim); break;
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
            case TeleportToPlayerIntent tpp: HandleTeleportToPlayer(session, tpp); break;
            case ToggleStealthIntent: HandleToggleStealth(session); break;
            case SetJetpackIntent sj: HandleSetJetpack(session, sj); break;
            case SetLampIntent sl: HandleSetLamp(session, sl); break;
            case CopyBuildIntent cb: HandleCopyBuild(session, cb); break;    // #1117: region → share code
            case PasteBuildIntent pb: HandlePasteBuild(session, pb); break;  // #1117: share code → blocks
            case RequestKnownNpcsIntent: HandleRequestKnownNpcs(session); break;  // #1118: "People you know"
            case SetNpcCallsIntent nc: HandleSetNpcCalls(session, nc); break;     // #1119: call preference
            case SetSeatedIntent seat: HandleSetSeated(session, seat); break;
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
            case RequestStoryResolutionIntent: HandleRequestStoryResolution(session); break; // #1124: watch the ending again
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
            SendTo(connectionId, new JoinRejected
            {
                Reason = Localize(join.Locale, "srv.join.protocol_mismatch")
                    .Replace("{server}", Protocol.Version.ToString())
                    .Replace("{client}", join.ProtocolVersion.ToString()),
            });
            return;
        }

        if (!string.IsNullOrEmpty(_config.ServerPassword)
            && !Shared.Security.SecretCompare.FixedTimeEquals(join.Password, _config.ServerPassword))
        {
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.bad_password" });
            return;
        }

        var name = string.IsNullOrWhiteSpace(join.PlayerName) ? $"player_{connectionId}" : join.PlayerName.Trim();
        if (name.Length > MaxPlayerNameLength)
        {
            name = name.Substring(0, MaxPlayerNameLength); // cap a client-supplied name so it can't be a multi-KB blob (persisted + broadcast in presence)
        }

        // Chat text is control-char-stripped (HandleChat); the join name must be too — it is persisted
        // and broadcast in presence, and a smuggled newline would corrupt logs and list UIs (#938).
        name = StripControlChars(name).Trim();
        if (name.Length == 0)
        {
            name = $"player_{connectionId}";
        }

        // Name screening at the join itself (#938): the WorldHost gates only cover HOSTED worlds — on a
        // self-hosted or direct-connect server this is the only gate there is. Blocked names are turned
        // away; watch-list names join normally but leave a log line + optional operator ping (a human
        // reviews ambiguous terms, the filter never guesses).
        var nameScreenResult = JoinNameScreen.Screen(name);
        if (nameScreenResult.Verdict == Shared.Moderation.NameVerdict.Block)
        {
            _log.Warn($"Join of '{name}' rejected: name matches blocked term '{nameScreenResult.MatchedTerm}'.");
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.name_blocked" });
            return;
        }

        if (nameScreenResult.Verdict == Shared.Moderation.NameVerdict.Watch)
        {
            _log.Warn($"NAME FLAG: join name '{name}' matches watch-list term '{nameScreenResult.MatchedTerm}' (allowed; review manually).");
            NotifyOperator("Name flagged", $"Player name '{name}' on world '{_config.WorldName}' matches watch-list term '{nameScreenResult.MatchedTerm}'. The join was allowed — review manually.", "triangular_flag_on_post");
        }

        // Hosted-worlds gate: with a JoinTokenSecret configured, only joins the control plane vouched for
        // (a valid HMAC token, bound to THIS world and THIS name) get in. Validated offline — no network
        // dependency on the control plane. Local sessions (AddLocalPlayer) never pass through here, so the
        // bundled singleplayer host is unaffected even if a secret were set.
        string hostedAccountId = string.Empty;
        if (!string.IsNullOrEmpty(_config.JoinTokenSecret))
        {
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!Shared.Security.HostedJoinToken.TryValidate(
                    _config.JoinTokenSecret, _config.WorldName, join.HostedToken, nowUnix,
                    out hostedAccountId, out string tokenName, out string tokenError)
                || !string.Equals(tokenName, name, StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn($"Join of '{name}' rejected: hosted token check failed ({(tokenError.Length > 0 ? tokenError : "name mismatch")}).");
                SendTo(connectionId, new JoinRejected { Reason = "@srv.join.token_required" });
                return;
            }
        }

        if (_config.WhitelistEnabled && !_config.Whitelist.Contains(name))
        {
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.not_whitelisted" });
            return;
        }

        // Fleet admin (issue #487): the operator of this installation, config-only and never persisted —
        // see ServerConfig.FleetAdminPlayers for why this must not become a PlayerRole. Case-insensitive
        // (#495): the hosted token check above compares names OrdinalIgnoreCase too, and a silent
        // `marcel` ≠ `Marcel` mismatch would grant nothing with no error anywhere.
        bool fleetAdmin = IsFleetAdminName(name);

        PlayerState state;
        try
        {
            state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);
        }
        catch (InvalidDataException ex)
        {
            _log.Error($"Failed to load player '{name}' (connection {connectionId}): persisted data is corrupted. " +
                       $"The database record was kept untouched for manual recovery. Error: {ex.Message} -> {ex.InnerException?.Message}\n{ex.StackTrace}");

            SendTo(connectionId, new JoinRejected
            {
                Reason = "@srv.join.data_corrupted"
            });
            return;
        }
        string tokenHash = HashNameToken(join.Token);

        // Reconnect eviction (#964). A client that dies without closing its socket cleanly — PC crash, hard
        // kill, a frozen game whose transport thread keeps answering pings — leaves a session that still
        // looks joined. It holds the name and a player slot, so the player's OWN reconnect is refused, and
        // nothing frees it until the transport finally gives up (22 minutes in the 2026-08-12 playtest).
        // Whoever proves ownership of the name with the matching token is the rightful owner of that
        // session: drop the old one and let them back in. This is exactly what the name token is for.
        if (FindJoinedSessionByName(name) is { } stale
            && !string.IsNullOrEmpty(state.NameTokenHash) && state.NameTokenHash == tokenHash)
        {
            _log.Info($"Player '{name}' reconnected — dropping their previous session (connection {stale.ConnectionId}).");
            _transport.DisconnectClient(stale.ConnectionId);
            OnClientDisconnected(stale.ConnectionId); // saves + tears the old session down synchronously
        }

        // A fleet admin gets a reserved slot on top of MaxPlayers: they come to observe a world, and a full
        // world is exactly when moderation is most likely to be needed. Their observer session also does not
        // count toward the cap for anyone else (see JoinedPlayerCount).
        int joinedCount = JoinedPlayerCount();
        if (joinedCount >= _config.MaxPlayers && !fleetAdmin)
        {
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.server_full" });
            return;
        }

        // Name reservation: one live session per name — PlayerId == name, so a second client under
        // the same name would alias (and corrupt) the same player state.
        if (FindJoinedSessionByName(name) != null)
        {
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.name_online:" + name });
            return;
        }

        // Name verification: the first join under a name claims it with the client's per-install token;
        // later joins must present the matching token (protects the host/admin identity from spoofing).
        // Unclaimed records (legacy saves / tokenless clients) adopt the first token they see.
        if (!string.IsNullOrEmpty(state.NameTokenHash) && state.NameTokenHash != tokenHash)
        {
            SendTo(connectionId, new JoinRejected { Reason = "@srv.join.name_taken:" + name });
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

        // Owner bootstrap (hosted worlds): the token-verified owner account is granted WorldAdmin even when
        // someone else already holds that role — an uploaded singleplayer save carries its old first-joiner
        // WorldAdmin, and without this the uploader could be locked out of administering their own world.
        if (!string.IsNullOrEmpty(_config.WorldOwnerAccountId)
            && hostedAccountId == _config.WorldOwnerAccountId
            && state.Role != PlayerRole.WorldAdmin)
        {
            state.Role = PlayerRole.WorldAdmin;
            _repo.SavePlayer(state); // persist immediately, like the name claim above
            // Deliberately no account id in the log line — ids are registry references and don't belong
            // in log files (CodeQL cs/cleartext-storage); the name identifies the event well enough.
            _log.Info($"Player '{name}' recognised as the world owner — granted WorldAdmin.");
        }

        // Return the player to the body they were last on (persisted per-player), not always the home world.
        // Ensure that body's world is resident + the active cursor before placing them + sending world data.
        var (joinBody, joinBodyType) = RestoreJoinBody(state);
        LoadWorld(joinBodyType, joinBody);

        var session = new PlayerSession(connectionId, state)
        {
            Joined = true,
            CurrentLocationId = joinBody,
            Locale = NormalizeLocale(join.Locale),
            ViewDistance = join.ViewDistanceChunks,
            IsFleetAdmin = fleetAdmin,
            HeartbeatTracked = true, // joined over the wire → silence is meaningful (#964)
        };
        session.LastPayloadAt = _uptime; // start the heartbeat clock now (#964) — a client that freezes
                                         // right after joining must age out like any other silent session
        _sessions[connectionId] = session;
        state.LastSeenUtc = UtcNowIso(); // "last seen" for the admin player list (issue #488)
        SetupPlayerShip(session); // give the player their own ship, stamped into their world
        EnsureSafeSpawn(session); // self-heal a position persisted mid-fall (don't load them into the void)
        session.AwaitingSpawnAdopt = true; // #865: drop pre-snap position reports until the client is here
        ApplyCreativeGrants(session); // singleplayer "Creative" world: unlock-all / all-ships / starter kit
        GrantStarterTeleporter(session); // StarterTeleporter world rule (#1056): hand out the device on join

        var (systemName, planetName) = ActiveLocationNames();
        SendTo(connectionId, new JoinAccepted
        {
            PlayerId = state.PlayerId,
            WorldSeed = _meta.Seed,
            PlanetType = joinBodyType,
            PlanetName = planetName,
            SystemName = systemName,
            CumulativePlaytimeSeconds = _meta.CumulativePlaytimeSeconds,
            TerrainContinents = _meta.Description.TerrainContinents,
        });
        SendInventory(session);
        SendPlayerState(session);
        SendRules(session);
        // Online admins carry a player-mode roster in their rule set (#1121) — refresh it now that this
        // player is on it.
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other != session && other.State.IsAdmin)
            {
                SendRules(other);
            }
        }

        SendShipCombatStatus(session);
        SendLandedShips(session); // every parked ship object on the join world
        SendShipPlacement(session);
        SendShipStations(session);
        SendStationsInReach(session); // #1070
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
        SendFactories(session);   // factories on the join world (animated machines + production terminals)
        SendGameUnlocks(session); // the player's downloaded-games collection (per-player, persisted)
        BackfillPlaceDiscoveries(session); // pre-#1113 saves: mirror already-landed bodies into "Places" first
        SendDiscoveryLog(session); // the first-scan ledger, for the Codex "Discoveries" chapter (#484)

        // Achievements: settle anything that came due while a reward had nowhere to go, retro-award entries that
        // were added to the data file since this save was made, and send the list with live progress.
        SettleAchievements(session);
        SendBeacons(session);
        SendBeams(session); // placed beam blocks (teleporter pads) on the join world
        SendBases(session); // player-founded bases on the join world (Grundstein markers)
        SendAllianceList(session); // the player's alliance roster (shared station/base access + Funk tab)
        SendStoryStateOnJoin(session); // story meter + per-player beat catch-up (P0)
        SendRelayNetwork(session); // SPS relay meters + jump lanes (#1125)
        ArmNpcRadioOnJoin(session); // NPC calls (#1119): quiet period first; the join scan then catches up
        BroadcastLandingPads(session); // the join claimed a pad — everyone's map must show it (#1020)
        SendContainers(session);
        SendExistingPresences(session); // show already-online players to the newcomer
        SyncAppearance(session);        // custom faces + body paintings, BOTH ways (#982)
        SendPaintDesigns(session);      // paint-design registry — before any chunk with painted blocks can arrive
        SendCustomShapes(session);      // …and the form registry, for the same reason (#843)
        ShipAiOnJoin(session); // boot VEGA: onboarding intro / veteran skip / resume objective

        // Hosted worlds: one-time welcome (community rules + beta notice) on the player's FIRST join of
        // this world — the acceptance screen lives on the portal; this is the friendly in-game reminder.
        // Keyed on the join-token gate (only official hosted instances run with a secret).
        if (!string.IsNullOrEmpty(_config.JoinTokenSecret) && !state.HostedWelcomeShown)
        {
            // Server-side localized (not a token): the copy is long-form prose, and join.Locale is at hand.
            Send(session, new ServerMessage { Text = Localize(session.Locale, "srv.join.welcome") });
            state.HostedWelcomeShown = true; // persists with the next save cycle
        }

        // A restart countdown may already be running — the newcomer needs the banner too.
        if (BuildActiveMaintenanceNotice() is { } maintenance)
        {
            Send(session, maintenance);
        }

        _log.Info($"Player '{name}' joined (connection {connectionId}).");
    }

    /// <summary>SHA-256 hex of a join token; empty/missing token → empty hash (name stays unclaimed).
    /// Instance-based SHA256 + manual hex: works on net10 AND the netstandard2.1 (Unity/WebGL) flavor,
    /// where the static HashData/Convert.ToHexString helpers don't exist.</summary>
    private static string HashNameToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }

    private PlayerState CreateNewPlayer(string name)
    {
        int spawnX = 0, spawnZ = 0;
        if (Rules.PersonalLandingZones && _landingPads.Count > 0)
        {
            // First spawn: drop the new player on the first free landing pad of the home body (item 38). The pad
            // is NOT claimed here — occupancy is live, and a pad is only held once the player's ship is parked
            // on it (PlayerPad), which is also where the claim starts being persisted (#848).
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
            // #997: this runs BEFORE the new session exists, so the per-player ship cursor (_shipPlaced /
            // _healTank) still points at whoever the server processed last — with PlaceStarterShip=false
            // the HOST's heal tank persisted as a brand-new player's respawn anchor. The pad spawn is the
            // only anchor that is truly theirs here; SetupPlayerShip re-anchors to their own heal tank.
            RespawnPoint = spawn,
            AboardShip = true,
            // The very first player to join becomes the world admin (world creator).
            Role = _repo.ListPlayerIds().Count == 0
                ? PlayerRole.WorldAdmin
                : (_config.AdminPlayers.Contains(name) ? PlayerRole.Admin : PlayerRole.Player),
        };

        // Starter kit: a basic drill and a hand scanner in the first hotbar slots, plus a suit lamp so the
        // player can light up caves / the ship at night (toggle with L), a simple melee weapon and a weak
        // ranged sidearm so a fresh player can fight back from a distance, not only by walking into a
        // hostile's bite range. Blocks are placed directly — select a block item and right-click — so there
        // is no separate "block placer" tool.
        // Stocked FROM StarterKit.Items so the list the discard guard protects (#599) is the same list the
        // player is actually handed — one array, no drift between the two.
        for (int i = 0; i < StarterKit.Items.Length; i++)
        {
            state.Inventory.SetSlot(i, new ItemStack(StarterKit.Items[i], 1));
        }

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
    /// head start, not infinite resources. Unknown keys are skipped. (Tools go to the backpack; the material
    /// stacks go to the ship's cargo hold so the backpack keeps free slots for mining — #677.)</summary>
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
            // Only the kit's TOOLS go into the backpack; the material stacks land in the ship's cargo hold.
            // The backpack has 24 slots and the starter gear already occupies five — stuffing the ~21 material
            // stacks in there left it 24/24 full, and a full backpack refuses every on-foot mine since #600
            // ("inventory full" on each swing), which players read as "mining is broken in Sandbox" (#677).
            // The starter hold (48 slots) absorbs the whole kit; leftovers are dropped with a log rather than
            // spilled back into the backpack, because free backpack slots ARE the fix.
            int overflow = 0;
            foreach (var (item, count) in CreativeKit)
            {
                if (_content.GetItem(item) is not { } idef)
                {
                    continue;
                }

                int maxStack = _content.MaxStackOf(item);
                if (idef.Category == ItemCategory.Tool)
                {
                    int left = p.Inventory.Add(item, count, maxStack);
                    overflow += left > 0 ? _ship.Cargo.Add(item, left, maxStack) : 0;
                }
                else
                {
                    overflow += _ship.Cargo.Add(item, count, maxStack);
                }
            }

            if (overflow > 0)
            {
                _log.Warn($"Creative kit: {overflow} item(s) did not fit the cargo hold and were dropped.");
            }

            _meta.CreativeKitGranted = true;
            _repo.SaveMetadata(_meta);
            _repo.SavePlayer(p); // persist the granted kit so a reload keeps it (and the one-time flag holds)
            SaveFleet(session); // the kit lives in the hold now — persist the fleet with the flag
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
        PlayerState state;
        try
        {
            state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);
        }
        catch (InvalidDataException ex)
        {
            _log.Error($"Failed to load local player '{name}': save data is corrupted. " +
                       $"Kept data untouched for manual recovery. Error: {ex.Message} -> {ex.InnerException?.Message}");
            throw; // Preserve the contract: surface the corruption to UI/caller without overwriting
        }

        if (state.Role != PlayerRole.WorldAdmin && _config.AdminPlayers.Contains(name))
        {
            state.Role = PlayerRole.Admin;
        }

        int connectionId = _nextLocalConnectionId--;

        // Return the player to the body they were last on (persisted); home/default for a fresh player.
        var (joinBody, joinBodyType) = RestoreJoinBody(state);
        LoadWorld(joinBodyType, joinBody);

        var session = new PlayerSession(connectionId, state)
        {
            Joined = true,
            CurrentLocationId = joinBody,
            Locale = NormalizeLocale(locale),
            IsFleetAdmin = IsFleetAdminName(name), // config-only, like the network join path
            // NOT heartbeat-tracked (#964): a locally-added player drives the server through direct calls
            // and never sends a payload, so silence is normal rather than a sign of a dead client.
        };
        _sessions[connectionId] = session;
        state.LastSeenUtc = UtcNowIso();
        SetupPlayerShip(session); // local/test players get their own ship too
        EnsureSafeSpawn(session); // self-heal a position persisted mid-fall (don't load them into the void)
        session.AwaitingSpawnAdopt = true; // #865: drop pre-snap position reports until the client is here
        ApplyCreativeGrants(session); // singleplayer "Creative" world: unlock-all / all-ships / starter kit
        GrantStarterTeleporter(session); // StarterTeleporter world rule (#1056): hand out the device on join
        return session;
    }

    /// <summary>Test seam: feeds a raw payload through the REAL receive path (join gate, flood gate, heartbeat
    /// stamp, dispatch) — the only way to exercise joins and rejoins without a live socket (#964).</summary>
    public void HandlePayloadForTest(int connectionId, byte[] payload) => OnPayload(connectionId, payload);

    /// <summary>Test hook: players currently counted against the player cap.</summary>
    public int JoinedPlayerCountForTest => JoinedPlayerCount();

    /// <summary>Test hook: how many chunks this session has had fully re-streamed by the ghost heal (#965).</summary>
    public int GhostReStreamsForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.GhostChunkSeen.Count ?? 0;

    /// <summary>Test hook: an air cell just above the player, for driving the ghost-block path.</summary>
    public Vector3i? FindAirCellForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        Serve(session);
        var p = session.State.Position;
        for (int dy = 2; dy < 12; dy++)
        {
            var cell = new Vector3i((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y) + dy, (int)System.Math.Floor(p.Z));
            if (WithinBuildHeight(cell.Y) && _world.GetBlock(cell).IsAir)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>Test seam: simulates a player's connection dropping, running the same disconnect handling a
    /// real transport close would (session cleanup, save, world unload) — so tests can assert that behaviour
    /// without a live socket. No-op if the player isn't joined.</summary>
    public void DisconnectLocalPlayerForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            OnClientDisconnected(session.ConnectionId);
        }
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
    public void PlaceBlock(string playerId, int x, int y, int z, string itemKey, string? label = null,
        int upFace = -1, int yaw = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandlePlace(session, new PlaceBlockIntent
            {
                X = x,
                Y = y,
                Z = z,
                ItemKey = itemKey,
                Label = label ?? string.Empty,
                UpFace = upFace,
                Yaw = yaw,
            });
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

    /// <summary>How far (blocks) a position report may sit from a just-placed spawn and still count as the
    /// client adopting it. A snapped client reports from the spawn itself; the pre-snap ghost pose is the
    /// world origin or the pre-teleport spot — typically thousands of blocks out. Generous, so a slow first
    /// settle (falling a few blocks onto the pad) can never wedge the gate shut.</summary>
    private const float SpawnAdoptRadius = 64f;

    private void HandleMove(PlayerSession session, MoveIntent move)
    {
        if (session.RespawnChoiceDeadline > 0)
        {
            return; // lying dead awaiting the respawn choice — the corpse doesn't walk
        }

        // MVP: trust position but clamp to sane finite values. (Full movement validation later.)
        if (float.IsFinite(move.X) && float.IsFinite(move.Y) && float.IsFinite(move.Z))
        {
            // ROUND WORLDS: the client transform runs unbounded as it laps the world in any direction; the
            // authoritative position is canonical — X in [0, Circumference), Z in the latitude domain
            // (±period/2, period ≈ circumference/2). The old pole clamp is gone: north–south wraps seamlessly
            // like east–west. Stations/space keep their own small coordinate space (no wrap there).
            int circ = _world.Circumference; // this world's size (asteroids small, planets large)
            float z = move.Z;
            bool onSurface = !InStation(session.State.PlayerId) && !InSpace(session.State.PlayerId);
            if (onSurface)
            {
                z = (float)WorldConstants.WrapZ((double)move.Z, circ);
            }

            var reported = new Vector3f((float)WorldConstants.WrapX(move.X, circ), move.Y, z);

            // Spawn-adoption gate (#865): right after the server places this player (join, travel landing,
            // respawn), the client may still be streaming a stale pose from before it processed the snap —
            // most damagingly the scene-default transform near the world origin during the very first join.
            // Drop reports far from the placed position until one arrives close to it; that first nearby
            // report proves the client has adopted the spawn and normal trust resumes. Wrap-aware, so a
            // spawn next to a world seam never reads as "far". Stations/space keep their own small
            // coordinate spaces where a fresh join cannot occur — the gate only guards surface play.
            if (session.AwaitingSpawnAdopt && onSurface)
            {
                if (WorldConstants.WrapDistanceSquared(reported, session.State.Position, circ)
                    > SpawnAdoptRadius * SpawnAdoptRadius)
                {
                    return; // pre-snap ghost pose — the authoritative spawn stands
                }

                session.AwaitingSpawnAdopt = false;
            }

            session.State.Position = reported;
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
            RespawnPlayer(session, "@srv.death.fall");
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
            if (ResyncStaleChunk(session, pos))
            {
                _log.Warn($"Ghost block healed at {pos.X},{pos.Y},{pos.Z} for '{session.State.Name}' (client saw a block, server has air).");
            }

            return;
        }

        // Hitting a flame stamps it out (#790), and swinging a torch at something flammable sets it alight
        // (#786). Both come BEFORE the protection and mineability checks: fire is not a mineable block, and a
        // torch must be able to light plants it could never mine. Ignition runs its own protection chain.
        if (TryStampOutFire(session, pos, current.Value) || TryTorchIgnite(session, pos, current.Value))
        {
            return;
        }

        if (IsShipBlock(pos))
        {
            Reject(session, "mine", "@srv.mine.ship_hull");
            return;
        }

        // Picking a plant is not vandalism (#626/#628). Settlement and station protection exists so nobody
        // tears down the houses or opens the hull — but a greenhouse is FOOD, and the whole point of it is
        // that the player walks in and harvests. Flora is never structural, and a harvested plant regrows on
        // its bed, so this takes nothing permanent from the settlement. Everything else the greenhouse is made
        // of — glass, beds, frame — stays protected.
        bool harvestingPlant = IsFlora(current.Value);

        if (!harvestingPlant && IsSettlementBlock(pos))
        {
            Reject(session, "mine", "@srv.protect.settlement");
            return;
        }

        if (!harvestingPlant && IsStationBlock(pos))
        {
            Reject(session, "mine", "@srv.protect.station");
            return;
        }

        if (IsFactoryProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "mine", "@srv.protect.factory");
            return;
        }

        if (IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "mine", "@srv.protect.base");
            return;
        }

        var def = _world.Definition(current);
        if (def is null || !def.Mineable)
        {
            Reject(session, "mine", "@srv.mine.not_mineable");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "mine", "@out_of_reach");
            return;
        }

        var tool = ActiveTool(session.State);
        if (!ToolCanMine(tool, def))
        {
            Reject(session, "mine", "@srv.mine.wrong_tool");
            return;
        }

        // Powered drills draw suit energy per swing (#796) — the same rule energy weapons follow per shot.
        // An empty suit rejects the swing BEFORE any progress accrues; the basic and diamond drills declare
        // no cost and keep working, so a drained player is never locked out of mining entirely.
        if (tool.EnergyPerUse > 0f)
        {
            if (session.State.SuitEnergy < tool.EnergyPerUse)
            {
                Reject(session, "mine", "@no_energy");
                return;
            }

            session.State.SuitEnergy -= tool.EnergyPerUse;
            SendPlayerState(session);
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
            BreakArea(session, pos, tool.MiningRadius, tool, pool);
        }

        SendInventory(session);

        // #853: whatever found no room is now lying on the ground as a drop packet instead of blocking the
        // swing. One spill call for the whole burst, so an area drill leaves one bundle, not one per cell.
        SpillPoolOverflow(session, pool, pos);
    }

    /// <summary>Breaks one block: clears it, banks its drops in the pool, broadcasts the change,
    /// schedules flora regrowth and advances mining missions. Clears any accumulated mining progress.
    /// <para>
    /// Nothing is ever destroyed here, but nothing is refused either: what the player's inventory (and the
    /// cargo hold, when aboard) cannot take stays in the pool's leftovers, and the caller spills it onto the
    /// ground as a drop packet (#853). Mining used to be refused outright in that situation — correct while
    /// there was no world container to spill into (#600/#607), and pure frustration once there is one.
    /// </para></summary>
    private void BreakBlockAt(PlayerSession session, Vector3i pos, BlockDefinition def, MaterialPool pool)
    {
        var current = _world.GetBlock(pos);
        var (dropTint, dropGlow) = _world.GetModifier(pos); // read the dye/glow BEFORE clearing, to recover it into the drop
        int dropDescriptor = _world.GetShape(pos);
        int dropShape = ShapeCode.ShapeOf(dropDescriptor); // recover the FORM (orientation is re-derived on re-place)
        // Recover the paint design into the drop too — the same round trip dye and form make. Only a LIVE
        // design travels; a wiped one is dropped here so the tombstoned id never rides an item into a stack.
        int dropDesign = ShapeCode.DesignOf(dropDescriptor);
        if (!IsLivePaintDesign(dropDesign))
        {
            dropDesign = 0;
        }

        if (PropShapes.IsStampedForm(def.Key, dropShape))
        {
            // A prop's server-stamped form is not player data — dropping it plain keeps the mined item
            // stacking with crafted ones (a "bed#s01" would never merge with a "bed"). Asking the shared
            // helper rather than comparing against the ONE default matters for the ladder (#909), which is
            // stamped as a wall plate OR a free-standing pole: the pole form would otherwise drop as its own
            // item key, split the stack, and then place as a plate anyway (the ladder item is not shapeable,
            // so a shape suffix on it is ignored at place time).
            dropShape = 0;
        }

        // Work out exactly what this break yields. Nothing here can fail any more: what the player cannot
        // carry ends up on the ground (see SpillPoolOverflow at the call sites), so the block always falls.
        var yield = new List<ItemAmount>();
        bool toxicFloraDrop = IsFlora(current.Value)
            && _floraSpeciesByBlock.TryGetValue(current.Value, out var toxSp) && toxSp.Toxic;
        foreach (var drop in def.Drops)
        {
            string item = toxicFloraDrop && drop.Item == "berries" ? "toxic_berries" : drop.Item;
            if ((dropTint != 0 || dropGlow != 0 || dropShape != 0 || dropDesign != 0) && _content.GetItem(item)?.PlacesBlock == def.Key)
            {
                item = ItemKey.Compose(item, dropTint, dropGlow, dropShape, dropDesign);
            }

            yield.Add(new ItemAmount(item, drop.Count));
        }

        if (IsContainerBlock(def.Key))
        {
            yield.AddRange(CrateContentsAt(pos)); // a mined crate/wood box hands its stored stacks back too
        }

        // Attribution (issue #490): removing a block is an edit like any other, and it is the one that grief
        // reports are actually about ("someone tore my house down") — so the remover is recorded as the owner.
        _world.SetBlock(pos, BlockId.Air, owner: session.State.PlayerId);
        _miningProgress.Remove(pos);

        if (IsContainerBlock(def.Key))
        {
            RemoveCrateContainer(pos, pool); // mining a crate/wood box returns its stored contents (Task 5 Stage 3b)
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

        // Bank the yield computed above. The crate case already handed its own stacks over in
        // RemoveCrateContainer, so only the block's own drops are added here.
        bool floraHarvest = IsFlora(current.Value);
        // #900: a spore bloom fattens the harvest — the reason to head out INTO the strange weather.
        int bloomBonus = floraHarvest ? WeatherHarvestBonus() : 0;
        foreach (var drop in yield.Take(def.Drops.Count))
        {
            pool.Add(drop.Item, drop.Count + bloomBonus);
        }

        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue });
        if (floraHarvest)
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
        ShipAiOnBlockBroken(session, def.Key); // VEGA context tips (#1077): digging score, by-hand streak, rare-ore learned
    }

    /// <summary>Area mining for powerful drills: breaks the mineable, unprotected blocks around a centre.
    /// Neighbours the tool could not mine directly (kind/tier — #797) are left standing, so a future
    /// low-tier area drill cannot sweep up ore above its own tier.</summary>
    private void BreakArea(PlayerSession session, Vector3i center, int radius, ToolProperties tool, MaterialPool pool)
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
                    if (d is null || !d.Mineable || !ToolCanMine(tool, d))
                    {
                        continue;
                    }

                    // A full inventory no longer stops the sweep: every block falls and the pool's leftovers
                    // are spilled ONCE by the caller, so a burst leaves one ground packet, not one per cell.
                    BreakBlockAt(session, p, d, pool);
                }
    }

    /// <summary>
    /// Banks loot from an event that cannot be refused after the fact — a creature is already dead, a wreck
    /// already burst.
    /// <para>
    /// With a <paramref name="spillAt"/> cell the overflow lands on the ground as a drop packet (#853) and
    /// nothing is lost. Without one — in space, where there is no ground to spill onto — what does not fit is
    /// genuinely gone, so the player is at least TOLD instead of the drop vanishing in silence (the complaint
    /// that started this: "Items futsch"). Prefer a capacity check up front
    /// (<see cref="MaterialPool.CanFit"/>) wherever the action can still be refused.
    /// </para>
    /// </summary>
    private void BankLoot(PlayerSession session, MaterialPool pool, IEnumerable<ItemAmount> drops, Vector3i? spillAt = null)
    {
        foreach (var drop in drops)
        {
            pool.Add(drop.Item, drop.Count);
        }

        if (spillAt is { } cell)
        {
            SpillPoolOverflow(session, pool, cell);
            return;
        }

        // Reuses the same rate-limited "your pockets are full" hint as every other overflow site (#600), rather
        // than a second warning channel saying the same thing.
        WarnIfPoolOverflowed(session, pool);
    }

    /// <summary>Auto-orients a placed shape from the surface it was built against: the shape's base rests on
    /// the first solid neighbour, preferring the floor (→ +Y up = the original ground behaviour), then walls,
    /// then the ceiling. Returns a <see cref="ShapeCode"/> up-face index (0..5); no solid neighbour → +Y.</summary>
    private int DeriveShapeUpFace(Vector3i pos)
    {
        bool Solid(int dx, int dy, int dz)
        {
            var p = WorldConstants.CanonicalBlock(new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz), _world.Circumference);
            var b = _world.GetBlock(p);
            return !b.IsAir && !IsFluid(b.Value); // water/lava is no surface to build against (#851)
        }

        if (Solid(0, -1, 0)) return 0; // floor → +Y up (unchanged for ground placement)
        if (Solid(-1, 0, 0)) return 2; // wall → +X up
        if (Solid(1, 0, 0)) return 3;  // wall → -X up
        if (Solid(0, 0, -1)) return 4; // wall → +Z up
        if (Solid(0, 0, 1)) return 5;  // wall → -Z up
        if (Solid(0, 1, 0)) return 1;  // ceiling → -Y up
        return 0;
    }

    /// <summary>The packed shape descriptor a prop block is stamped with on placement (#909), or 0 when the
    /// key is no prop. Each <see cref="PropOrientation"/> honours exactly as much of the intent's orientation
    /// as its client-side rotate cycle offers — a pinned-back tip or an ignored quarter turn would make the
    /// placement ghost a liar.</summary>
    private int StampPropShape(PlayerSession session, PlaceBlockIntent place, string blockKey, Vector3i pos)
    {
        var cycle = PropShapes.OrientationOf(blockKey);
        if (cycle == PropOrientation.None)
        {
            return 0;
        }

        int facing = place.Yaw >= 0 && place.Yaw <= 3
            ? place.Yaw
            : ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;

        if (cycle == PropOrientation.LadderMount)
        {
            // The ladder's whole orientation IS its mount face, so the intent's up-face carries it: 2..5 hug
            // that wall, anything else means free-standing (the client's fifth cycle state sends +Y). Yaw is
            // dropped on purpose — both ladder forms are square about their own axis.
            int mount = ShapeCode.IsValidUpFace(place.UpFace) ? place.UpFace : DeriveLadderMount(pos);
            var (ladderShape, ladderUp) = PropShapes.LadderForm(mount);
            return ShapeCode.Pack(ladderShape, 0, ladderUp);
        }

        if (cycle == PropOrientation.Full)
        {
            // The crafted staircase is a directional form like any shaped block: it may tip onto walls and
            // ceilings, and auto-orients against the surface it was built on when nothing was chosen.
            int upFace = ShapeCode.IsValidUpFace(place.UpFace) ? place.UpFace : DeriveShapeUpFace(pos);
            return ShapeCode.Pack(PropShapes.DefaultPlaceShape(blockKey), facing, upFace);
        }

        // Furniture turns but never tips: a bed/campfire on a wall would break its sit/heal/warmth checks.
        return ShapeCode.Pack(PropShapes.DefaultPlaceShape(blockKey), facing, ShapeCode.UpPlusY);
    }

    /// <summary>Which wall a ladder hugs when the client sent no choice — an old client, a test, or one of the
    /// server's own internal placements. Mirrors the mesher heuristic #803 shipped with (first solid horizontal
    /// neighbour, in <see cref="ShapeCode.WallFaces"/> order), so those placements keep landing where they
    /// always did. The client normally decides this itself and sends the answer, because it can also honour
    /// the wall the player actually aimed at.</summary>
    private int DeriveLadderMount(Vector3i pos) => PropShapes.DeriveLadderMount(
        face =>
        {
            // The up-face points AWAY from the plate's support, so the wall sits at the opposite offset.
            var dir = ShapeCode.FaceDirection(face);
            var p = WorldConstants.CanonicalBlock(
                new Vector3i(pos.X - dir.X, pos.Y - dir.Y, pos.Z - dir.Z), _world.Circumference);
            return IsLadderMountWall(_world.GetBlock(p));
        },
        clickedFace: -1);

    /// <summary>A neighbour a ladder plate can hang on. The mesher additionally rules out see-through walls
    /// (glass, force fields), which the server has no flag for — the divergence only shows for a client old
    /// enough not to send its own mount face, and costs at most a plate where a pole was drawn before.</summary>
    private bool IsLadderMountWall(BlockId id)
    {
        if (id.IsAir || IsFluid(id.Value) || IsFlora(id.Value))
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def is not null && def.Solid && def.Key != "ladder";
    }

    private void HandlePlace(PlayerSession session, PlaceBlockIntent place)
    {
        var item = _content.GetItem(place.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "place", "@srv.place.not_placeable");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "place", "@srv.place.unknown_block");
            return;
        }

        var pos = WorldConstants.CanonicalBlock(new Vector3i(place.X, place.Y, place.Z), _world.Circumference); // wraps at THIS world's seam

        // Reject before touching the world: a block edit outside the build band would generate + persist a
        // chunk at an arbitrary height (unbounded RAM/disk DoS from a spoofed-position place spam). See MinBuildY.
        if (!WithinBuildHeight(pos.Y))
        {
            Reject(session, "place", "@srv.place.height_limit");
            return;
        }

        // A torch is an open flame: it needs air to burn. On an airless body (atmosphere "none" — asteroids and
        // the like) there is nothing to sustain it, so it is refused with a reason the player can act on instead
        // of being placed as a dud that mysteriously gives no light. A toxic atmosphere is fine — you need a
        // suit, the flame does not. Checked HERE, before the item is consumed further down, so a refused torch
        // stays in the pack.
        if (blockDef.Key == "torch" && !AtmospherePresent)
        {
            Reject(session, "place", "@no_air");
            return;
        }

        // Building under water (#851): the target cell is free when it is air OR holds a fluid — a placed block
        // DISPLACES water/lava, exactly like in every other voxel builder. Without this you can't build under
        // water at all: the client's aim march passes THROUGH fluids (they have no collider — you swim into
        // them), so the cell it offers while you're swimming always holds water, and water only yields to a
        // tier-3 mining beam, so there was no way to clear it first either.
        var existing = _world.GetBlock(pos);
        bool intoFluid = IsFluid(existing.Value);
        if (!existing.IsAir && !intoFluid)
        {
            Reject(session, "place", "@srv.place.not_empty");
            return;
        }

        if (intoFluid)
        {
            // Two placeables genuinely can't take a fluid cell, and both are refused before anything is
            // consumed: a door is an ENTITY living in an air cell (the fluid would just flow back around it,
            // leaving a door that holds nothing back), and a torch is an open flame — a submerged one would be
            // the same mysterious dud the airless-body check above exists to prevent.
            if (IsDoorBlock(blockDef.Key))
            {
                Reject(session, "place", "@srv.place.not_empty");
                return;
            }

            if (blockDef.Key == "torch" && existing.Value == _waterId)
            {
                Reject(session, "place", "@no_air");
                return;
            }
        }

        // Don't let the player wall themselves in: refuse a block at HEAD height in their own column. The FEET
        // cell is allowed so you can pillar-jump (place under yourself while jumping) — the client collider just
        // lifts you onto the new block (B3); only the head cell would trap you.
        var feet = session.State.Position;
        int fx = (int)System.Math.Floor(feet.X), fy = (int)System.Math.Floor(feet.Y), fz = (int)System.Math.Floor(feet.Z);
        if (pos.X == fx && pos.Z == fz && pos.Y == fy + 1)
        {
            Reject(session, "place", "@srv.place.above_head");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "place", "@out_of_reach");
            return;
        }

        if (!session.State.IsAdmin && IsOnLandingPad(pos))
        {
            Reject(session, "place", "@srv.place.pad_reserved");
            return;
        }

        if (IsStationBlock(pos))
        {
            Reject(session, "place", "@srv.protect.station");
            return;
        }

        if (IsFactoryProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "place", "@srv.protect.factory");
            return;
        }

        if (IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "place", "@srv.protect.base");
            return;
        }

        // No building inside the ship — the cabin is a fixed structure. The construction site (#948) is
        // guarded the same way: its cells are structure cells, never world blocks.
        if (ShipInteriorContains(new Vector3f(pos.X, pos.Y, pos.Z))
            || ConstructionContains(new Vector3f(pos.X, pos.Y, pos.Z)))
        {
            Reject(session, "place", "@srv.place.no_ship_interior");
            return;
        }

        // A ship keel founds a self-built ship (#948): it never becomes a world block — it seeds a new
        // construction-site structure OBJECT anchored at the cell. Fully handled (incl. material cost).
        if (blockDef.Key == ShipCoreBlock)
        {
            HandleShipCorePlace(session, pos, place.ItemKey);
            return;
        }

        // Seeds / flora only take on a suitable host block (mud, grass, crystal, ...).
        if (IsFlora(blockDef.NumericId.Value))
        {
            if (!IsValidFloraHost(blockDef.NumericId.Value, pos))
            {
                Reject(session, "place", "@srv.place.plant_ground");
                return;
            }

            // On a space station (void world) a plant must sit fully inside the hull — solid block below and no
            // side open to space — so it can't be seen or walked through into the void.
            if (!IsFloraEnclosedForVoidWorld(pos))
            {
                Reject(session, "place", "@srv.place.plant_enclosed");
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
                Reject(session, "place", "@srv.base.surface_only");
                return;
            }

            if (PlayerHasBaseOn(session.State.PlayerId, _world.LocationId))
            {
                Reject(session, "place", "@srv.base.already_here");
                return;
            }
        }

        // Creative mode and admin instant-build place without consuming materials.
        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(place.ItemKey) < 1)
            {
                Reject(session, "place", "@srv.place.no_block");
                return;
            }

            pool.Remove(new[] { new ItemAmount(place.ItemKey, 1) });
        }

        // A door isn't a voxel block — it fills the (air) cell as a server door entity (Task 5 Stage 3c).
        if (IsDoorBlock(blockDef.Key))
        {
            PlaceDoor(session, pos, DoorKindForBlock(blockDef.Key));
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
            // Built-in forms are always placeable; a player-designed form (#843) only if this save actually
            // has it registered — an item carrying a wiped index falls back to a plain cube rather than
            // stamping geometry nobody can mesh.
            if (ShapeCode.IsPlaceableShape(shapeIndex, HasCustomShape))
            {
                // Yaw: the client may send an explicit quarter-turn (rotate key); otherwise it follows where the
                // player is looking. An explicit turn is what lets you build stairs into a corner without having
                // to stand in a particular direction to get the angle you want.
                int facing = place.Yaw >= 0 && place.Yaw <= 3
                    ? place.Yaw
                    : ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;
                // Orientation: the client may send an explicit rotation override (rotate key); otherwise the
                // shape auto-orients so its base rests on the surface it was built against (floor → +Y, i.e.
                // unchanged; walls/ceiling tilt it). up-face × yaw give the full 24 orientations.
                int upFace = ShapeCode.IsValidUpFace(place.UpFace) ? place.UpFace : DeriveShapeUpFace(pos);
                placeShape = ShapeCode.Pack(shapeIndex, facing, upFace);
            }
        }

        // Prop blocks (furniture #804/#807/#809, ladder + crafted staircase #909) read as their real
        // silhouette out of the box: stamp their default form on placement — the same per-voxel shape channel
        // the Shape action writes, so the mesher, save and wire all treat it like any player-shaped cell. The
        // quarter-turn honours the rotate key like any shaped block (#863); without one it follows the
        // player's facing. How far the orientation may travel is per prop (PropOrientation), because the
        // cycle the client offers must promise exactly what is honoured here. BreakBlockAt strips the stamped
        // form again so the drop stacks with freshly crafted items.
        if (placeShape == 0)
        {
            placeShape = StampPropShape(session, place, blockDef.Key, pos);
        }

        // A painted item carries its design id in the key; stamp it into the descriptor's design bits so the
        // placed block shows the texture. Composes with any form above (built-in, custom or prop-stamped).
        // Only a LIVE design is honoured — an item holding a wiped/foreign id places as the plain material,
        // mirroring the custom-shape fallback. Tintable is the gate, like dye (paint is a surface cosmetic).
        if (blockDef.Tintable)
        {
            int placeDesign = ItemKey.Design(place.ItemKey);
            if (IsLivePaintDesign(placeDesign))
            {
                placeShape = ShapeCode.WithDesign(placeShape, placeDesign);
            }
        }

        _world.SetBlock(pos, blockDef.NumericId, placeTint, placeGlow, placeShape, session.State.PlayerId);

        if (IsContainerBlock(blockDef.Key))
        {
            PlaceCrate(pos); // a placed storage crate/wood box becomes a lootable/stash-able container (Task 5 Stage 3b)
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
        else if (intoFluid)
        {
            // The block displaced a fluid (#851): drop the cell's flowing state — memory AND the persisted level
            // row — or a reload would resurrect the tongue on top of the new block, and wake the neighbours so a
            // stream cut off here recedes (and the body around a new underwater wall settles again).
            UntrackFluid(pos);
            OnFluidRemoved(pos);
        }

        SendInventory(session);
        OnAchievementBuild(session);
        OnBlockPlaced(session, blockDef, pos); // #1116: advance any matching Build mission objectives
    }

    private void HandleCraft(PlayerSession session, CraftIntent craft)
    {
        var recipe = _content.GetRecipe(craft.RecipeKey);
        if (recipe is null)
        {
            Reject(session, "craft", "@srv.craft.unknown_recipe");
            return;
        }

        // Bound the batch size (avoid input*count overflow); a full stack in one order is the useful ceiling.
        int count = System.Math.Clamp(craft.Count, 1, ItemDefinition.DefaultMaxStack);

        // Creative mode: no material/blueprint/station cost — just produce the output.
        if (!Rules.CraftingCostsMaterialsFor(session.State.ModeOverride))
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
            CraftFail(session, recipe.Key, "@srv.craft.blueprint_locked");
            return;
        }

        if (!StationAvailable(session.State, recipe.Station))
        {
            // Send a machine-readable token so the client can localize it AND name the exact station
            // the recipe needs (Severin playtest #2: "crafting station not available" while standing at
            // a workbench — meat actually needs a detoxifier). The station enum name maps to the existing
            // "ui.craft.station_<name>" locale keys client-side.
            CraftFail(session, recipe.Key, "@need_station:" + recipe.Station.ToString().ToLowerInvariant());
            return;
        }

        // A factory only produces its own roster (a seeded subset of the factory recipes). The terminal the
        // player stands at decides what's on offer — never every factory recipe.
        if (recipe.Station == CraftingStation.Factory)
        {
            var factory = FactoryTerminalNear(session.State);
            if (factory is null || !factory.Roster.Contains(recipe.Key))
            {
                CraftFail(session, recipe.Key, "@srv.craft.factory_roster");
                return;
            }
        }

        // Market barter is themed per VENDOR (B55): each vendor posts the goods of its own profession, so different
        // vendors at one settlement/station offer different deals (and station vendors can post themed goods, not
        // just the themeless ones). Themeless market recipes trade anywhere (every vendor + the ship's own console).
        if (recipe.Station == CraftingStation.Market && !string.IsNullOrEmpty(recipe.MarketTheme))
        {
            string vendorTheme = VendorThemeAt(session.State);
            if (!string.Equals(vendorTheme, recipe.MarketTheme, System.StringComparison.OrdinalIgnoreCase))
            {
                CraftFail(session, recipe.Key, "@srv.craft.wrong_vendor");
                return;
            }
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        var scaledInputs = recipe.Inputs.Select(i => new ItemAmount(i.Item, i.Count * count)).ToList();
        if (!pool.Has(scaledInputs))
        {
            CraftFail(session, recipe.Key, "@srv.craft.missing_materials");
            return;
        }

        // Room for the RESULT before anything is consumed. Without this the inputs were removed, the
        // output silently dropped on the floor of a full inventory (MaterialPool.Add's leftover was
        // ignored) and the client was still told Success = true — a player lost crafted glass AND the
        // sand that went into it with 24/24 slots occupied and no ship cargo in reach. Checked against
        // the SCALED outputs so a batch craft that only partly fits is refused as a whole.
        var scaledOutputs = recipe.Outputs.Select(o => new ItemAmount(o.Item, o.Count * count)).ToList();
        if (!pool.CanFit(scaledOutputs))
        {
            // Machine-readable so the client can localize it (same convention as "@need_station:").
            CraftFail(session, recipe.Key, "@inventory_full");
            return;
        }

        pool.Remove(scaledInputs);
        foreach (var output in scaledOutputs)
        {
            pool.Add(output.Item, output.Count);
        }

        // Bartering at a settlement/station market stall is a trade with that vendor NPC — remembered (item 14).
        if (recipe.Station == Shared.Definitions.CraftingStation.Market && !session.State.AboardShip)
        {
            RecordVendorTrade(session.State);
            SendNpcStandings(session); // #1118: the vendor's nameplate stage may just have risen
            ShipAiOnTradeOrMission(session); // VEGA onboarding: a vendor barter counts as the first trade
        }

        Send(session, new CraftResult { Success = true, RecipeKey = recipe.Key });
        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: outputs that found no room are gone — say so
        ShipAiOnCraft(session); // VEGA onboarding: first successful craft
        OnAchievementCraft(session, recipe.Key);
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
            CraftFail(session, "tint", "@srv.craft.tint_item");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null || !blockDef.Tintable)
        {
            CraftFail(session, "tint", "@srv.craft.tint_material");
            return;
        }

        int tint = intent.Tint & 0xFFFFFF;
        int glow = intent.Glow & 0xFFFFFF;
        if (tint == 0 && glow == 0)
        {
            CraftFail(session, "tint", "@srv.craft.no_colour");
            return;
        }

        int count = System.Math.Clamp(intent.Count, 1, ItemDefinition.DefaultMaxStack);
        // Preserve any shape/design the source already carried — colouring a shaped or painted block keeps them.
        string output = ItemKey.Compose(baseKey, tint, glow,
            ItemKey.Shape(intent.SourceItemKey), ItemKey.Design(intent.SourceItemKey));

        // Creative mode: no material cost — just produce the coloured material.
        if (!Rules.CraftingCostsMaterialsFor(session.State.ModeOverride))
        {
            var freeTintPool = new MaterialPool(_content, session.State, _ship);
            AddCraftOutput(session, freeTintPool, output, count, intent.Slot);
            Send(session, new CraftResult { Success = true, RecipeKey = "tint" });
            SendInventory(session);
            WarnIfPoolOverflowed(session, freeTintPool); // #600
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
            CraftFail(session, "tint", glow != 0 ? "@srv.craft.need_crystal" : "@srv.craft.missing_material");
            return;
        }

        // The coloured result is a DIFFERENT item key than the source, so it needs room — but the room the
        // consumed source frees up counts (a whole-stack recolour in a full inventory is still a 1:1 swap).
        if (!pool.CanFitAfterRemoving(inputs, new[] { new ItemAmount(output, count) }))
        {
            CraftFail(session, "tint", "@inventory_full");
            return;
        }

        pool.Remove(inputs);
        AddCraftOutput(session, pool, output, count, intent.Slot);
        Send(session, new CraftResult { Success = true, RecipeKey = "tint" });
        SendInventory(session);
        // #600: dyeing PART of a stack needs a fresh slot for the new key, so a full inventory can still lose it.
        WarnIfPoolOverflowed(session, pool);
        ShipAiOnCraft(session);
    }

    /// <summary>
    /// Stores a craft output preferring an explicit personal-inventory slot — the hotbar slot the player
    /// invoked the action on, so a slot-local transform visibly lands in THAT slot instead of the first free
    /// one (or the cargo hold). Falls back to the ordinary pool add for whatever the slot cannot take: slot
    /// out of range (-1 = the legacy behaviour), occupied by a different item, or stack-capacity overflow.
    /// </summary>
    private void AddCraftOutput(PlayerSession session, MaterialPool pool, string output, int count, int slot)
    {
        var inv = session.State.Inventory;
        if (slot >= 0 && slot < inv.SlotCount && count > 0)
        {
            int maxStack = _content.MaxStackOf(output);
            var existing = inv.Slots[slot];
            if (existing is null || existing.IsEmpty)
            {
                int put = System.Math.Min(count, maxStack);
                inv.SetSlot(slot, new ItemStack(output, put));
                count -= put;
            }
            else if (existing.Item == output && existing.Count < maxStack)
            {
                int put = System.Math.Min(count, maxStack - existing.Count);
                existing.Count += put;
                count -= put;
            }
        }

        if (count > 0)
        {
            pool.Add(output, count);
        }
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
        if (!IsShapeableSource(session, intent.SourceItemKey, "shape"))
        {
            return;
        }

        int shape = intent.Shape;
        if (shape != 0 && !ShapeCode.IsValidShape(shape))
        {
            CraftFail(session, "shape", "@srv.craft.unknown_shape");
            return;
        }

        ApplyShapeExchange(session, intent.SourceItemKey, intent.Count, shape, "shape", intent.Slot);
    }

    /// <summary>Validates that a craft source is a building material that can be re-formed at all — the
    /// shared head of the built-in Shape action and the player-designed form craft (#843).</summary>
    private bool IsShapeableSource(PlayerSession session, string sourceItemKey, string tag)
    {
        var item = _content.GetItem(ItemKey.Base(sourceItemKey));
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            CraftFail(session, tag, "@srv.craft.shape_item");
            return false;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null || !blockDef.Shapeable)
        {
            CraftFail(session, tag, "@srv.craft.shape_material");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The free 1:1 exchange behind every form craft: consume the source stack, hand back the same material
    /// carrying <paramref name="shape"/> in its key, colour preserved. Shared by the built-in Shape action and
    /// the player-designed form craft so the two can never drift apart (inventory-full guard, creative path,
    /// overflow warning, VEGA hooks all live here once).
    /// </summary>
    private void ApplyShapeExchange(PlayerSession session, string sourceItemKey, int intentCount, int shape, string tag, int slot = -1)
    {
        string baseKey = ItemKey.Base(sourceItemKey);

        // Re-forming to the shape the source already has (incl. "cube" on a plain block) is a no-op.
        if (shape == ItemKey.Shape(sourceItemKey))
        {
            CraftFail(session, tag, "@srv.craft.same_shape");
            return;
        }

        int count = System.Math.Clamp(intentCount, 1, ItemDefinition.DefaultMaxStack);
        // Only the form changes — keep whatever colour/design the source carried.
        string output = ItemKey.Compose(baseKey, ItemKey.Tint(sourceItemKey), ItemKey.Glow(sourceItemKey),
            shape, ItemKey.Design(sourceItemKey));

        // Creative mode: no material cost — just produce the shaped material.
        if (!Rules.CraftingCostsMaterialsFor(session.State.ModeOverride))
        {
            var freeShapePool = new MaterialPool(_content, session.State, _ship);
            AddCraftOutput(session, freeShapePool, output, count, slot);
            Send(session, new CraftResult { Success = true, RecipeKey = tag });
            SendInventory(session);
            WarnIfPoolOverflowed(session, freeShapePool); // #600
            if (shape != 0) RevealShapeAnomalyMemory(session); // forming a non-cube → VEGA's "why we built blocky" memory
            return;
        }

        // Free 1:1: consume the exact source stack (re-shaping an already coloured/shaped item works too).
        var pool = new MaterialPool(_content, session.State, _ship);
        var inputs = new List<ItemAmount> { new ItemAmount(sourceItemKey, count) };
        if (!pool.Has(inputs))
        {
            CraftFail(session, tag, "@srv.craft.missing_material");
            return;
        }

        // Same as tinting: the re-formed result carries a different composed key, so it needs room — counting
        // the room the consumed source frees up. A 1:1 transform must never be able to destroy the material.
        if (!pool.CanFitAfterRemoving(inputs, new[] { new ItemAmount(output, count) }))
        {
            CraftFail(session, tag, "@inventory_full");
            return;
        }

        pool.Remove(inputs);
        AddCraftOutput(session, pool, output, count, slot);
        Send(session, new CraftResult { Success = true, RecipeKey = tag });
        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: same partial-stack trap as dyeing
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
        // Market (barter) recipes are trades, not construction; transmuter recipes synthesise raw ore
        // from matter dust — neither is a built item, so they must not make raw resources look
        // "disassemblable" (else mined ore could be reversed into matter dust + an energy cell).
        // Factory recipes deliberately consume MORE cheap raw than the base recipe, so disassembling
        // a factory-made item must never refund that surplus (craft cheap-bulk → disassemble for more).
        RecipeDefinition? recipe = null;
        int perCraft = 1;
        foreach (var r in _content.Recipes.Values)
        {
            if (r.Station is CraftingStation.Market or CraftingStation.Transmuter or CraftingStation.Factory)
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
            Reject(session, "disassemble", "@srv.craft.no_disassemble");
            return;
        }

        if (!StationAvailable(session.State, CraftingStation.Workshop))
        {
            Reject(session, "disassemble", "@srv.craft.need_workshop");
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        if (pool.Count(itemKey) < 1)
        {
            Reject(session, "disassemble", "@srv.misc.no_item");
            return;
        }

        // Work out the salvage first so it can be checked for room BEFORE the item is consumed — otherwise
        // disassembling with a full inventory destroys the item and returns nothing.
        var salvage = new List<ItemAmount>();
        foreach (var input in recipe.Inputs)
        {
            int recovered = (int)System.Math.Floor(input.Count * DisassemblyRecoveryRate / perCraft);
            if (recovered > 0)
            {
                salvage.Add(new ItemAmount(input.Item, recovered));
            }
        }

        if (!pool.CanFit(salvage))
        {
            Reject(session, "disassemble", "@inventory_full");
            return;
        }

        pool.Remove(new[] { new ItemAmount(itemKey, 1) });
        foreach (var part in salvage)
        {
            pool.Add(part.Item, part.Count);
        }

        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: recovered components that found no room are gone
    }

    private void HandleDisassemble(PlayerSession session, DisassembleIntent intent)
        => Disassemble(session.State.PlayerId, intent.ItemKey);

    private void HandleUnlock(PlayerSession session, UnlockBlueprintIntent unlock)
    {
        var bp = _content.GetBlueprint(unlock.BlueprintKey);
        if (bp is null)
        {
            Reject(session, "unlock", "@srv.unlock.unknown");
            return;
        }

        if (session.State.UnlockedBlueprints.Contains(bp.Key))
        {
            Reject(session, "unlock", "@srv.unlock.already");
            return;
        }

        // Research is location-bound to the cockpit (#1074) — the Tech tab used to claim a "lab" that no
        // ship ever had while this handler enforced nothing at all. Free-crafting (Creative) worlds skip it, like crafting does.
        if (Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) && !ResearchAvailable(session))
        {
            Reject(session, "unlock", "@srv.unlock.cockpit");
            return;
        }

        foreach (var pre in bp.Prerequisites)
        {
            if (!session.State.UnlockedBlueprints.Contains(pre))
            {
                Reject(session, "unlock", "@srv.unlock.prerequisite");
                return;
            }
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.Has(bp.UnlockCost))
        {
            Reject(session, "unlock", "@srv.unlock.materials");
            return;
        }

        if (session.State.KnowledgePoints < bp.KnowledgeCost)
        {
            Reject(session, "unlock", "@srv.unlock.knowledge");
            return;
        }

        // Knowledge is a permanent threshold (item 11): it gates the unlock but is never spent — only the
        // research materials are consumed. (Knowledge can also be taught to others without losing any.)
        pool.Remove(bp.UnlockCost);
        session.State.UnlockedBlueprints.Add(bp.Key);
        OnAchievementResearch(session); // "Researcher" ladder (#1102)

        // Localized frame + the blueprint's own localized display name (falls back to the raw key).
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.unlock.done")
                .Replace("{name}", LocalizedName(session.Locale, bp.NameKey, bp.Key)),
        });
        SendInventory(session);
        ShipAiOnBlueprint(session); // VEGA onboarding: first blueprint researched
    }

    private void HandleAdminCommand(PlayerSession session, AdminCommandIntent cmd)
    {
        var p = session.State;

        // A fleet admin is an admin everywhere by definition — they are the operator of the installation, not
        // a guest on someone's world. Checked as a session flag rather than by writing PlayerRole.Admin into
        // the save, so the elevation never travels with an exported world (see ServerConfig.FleetAdminPlayers).
        if (!p.IsAdmin && !session.IsFleetAdmin)
        {
            Reject(session, "admin", "@srv.admin.not_admin");
            return;
        }

        // Admin content tooling (not a cheat): AI mission generation.
        if (string.Equals(cmd.Command, "ai_mission", StringComparison.OrdinalIgnoreCase))
        {
            // Generation runs off the tick thread (the LLM call blocks up to the backend timeout); the
            // published/rejected result is pushed to the admin later by TickAiMissions. Acknowledge now.
            string ack = RequestAiMission(session, cmd.StringArg ?? string.Empty);
            Send(session, new ServerMessage { Text = ack });
            CheatLog(p, "requested an AI mission");
            return;
        }

        // Maintenance announcements (not cheats): the world admin may warn players before a restart even on
        // servers that have cheats disabled.
        if (string.Equals(cmd.Command, "announce", StringComparison.OrdinalIgnoreCase))
        {
            if (!EnqueueMaintenance(MaintenanceNotice.KindInfo, cmd.StringArg, -1))
            {
                Reject(session, "admin", "@srv.admin.usage_announce");
                return;
            }

            CheatLog(p, "posted a maintenance announcement");
            return;
        }

        // Kicking (not a cheat either): the world admin ends a session right now. Deliberately momentary —
        // a lasting block is the world owner's ban list on the portal, so there is exactly ONE ban store and
        // it lives where the world's identity does. Kicking yourself is refused: it reads as a bug report.
        if (string.Equals(cmd.Command, "kick", StringComparison.OrdinalIgnoreCase))
        {
            string target = (cmd.StringArg ?? string.Empty).Trim();
            if (string.Equals(target, p.Name, StringComparison.OrdinalIgnoreCase))
            {
                Reject(session, "admin", "@srv.admin.kick_self");
                return;
            }

            if (!EnqueueKick(target, "@ui.kick.by_admin"))
            {
                Reject(session, "admin", "@srv.admin.usage_kick");
                return;
            }

            Send(session, new ServerMessage { Text = "@srv.admin.kick_sent:" + target });
            CheatLog(p, $"kicked {target}");
            return;
        }

        if (string.Equals(cmd.Command, "schedule_restart", StringComparison.OrdinalIgnoreCase))
        {
            int minutes = cmd.IntArg;
            if (!EnqueueMaintenance(MaintenanceNotice.KindRestartCountdown, cmd.StringArg, minutes * 60))
            {
                Reject(session, "admin", Localize(session.Locale, "srv.admin.usage_restart")
                    .Replace("{max}", MaxMaintenanceRestartMinutes.ToString()));
                return;
            }

            CheatLog(p, $"scheduled a restart in {minutes} min");
            return;
        }

        if (string.Equals(cmd.Command, "cancel_restart", StringComparison.OrdinalIgnoreCase))
        {
            EnqueueMaintenance(MaintenanceNotice.KindCancelled, null, -1);
            CheatLog(p, "cancelled the scheduled restart");
            return;
        }

        // Inspection (issues #487/#488) — deliberately above the CheatsAllowed gate, like `announce`. That world
        // option defaults to OFF and hosted worlds never enable it, so gating oversight on it would make these
        // dead on arrival exactly where they matter. The role is the gate.
        switch (cmd.Command?.ToLowerInvariant())
        {
            case "players":
                AdminListPlayers(session);
                return;

            case "builds":
                AdminListBuilds(session, cmd.StringArg);
                return;

            // Paint moderation (#821) — like kick/announce, moderation is not a cheat: the role is the gate.
            case "shapewipe":
                AdminCustomShapeWipe(session, cmd.StringArg);
                break;

            case "paintwipe":
                AdminPaintWipe(session, cmd.StringArg);
                return;

            case "where":
                AdminWhere(session, cmd.StringArg ?? cmd.TargetPlayer);
                return;

            // Per-player mode override (#1121) — world management, not a cheat: family worlds keep
            // AdminCheats off, and exactly there a parent needs to hand the kid Creative. The role is the gate.
            case "set_mode":
                AdminSetPlayerMode(session, cmd.TargetPlayer, cmd.StringArg);
                return;

            // Observer mode + its jump command are fleet-admin only: they reach into worlds other people own,
            // so the owner of a single world must not be able to use them (issue #487).
            case "spectate":
            case "goto":
                if (!session.IsFleetAdmin)
                {
                    Reject(session, "admin", "@srv.admin.observer_reserved");
                    return;
                }

                if (string.Equals(cmd.Command, "spectate", StringComparison.OrdinalIgnoreCase))
                {
                    HandleSpectateCommand(session, cmd.StringArg);
                }
                else
                {
                    AdminGoto(session, cmd.StringArg);
                }

                return;
        }

        if (!Rules.CheatsAllowed)
        {
            Reject(session, "admin", "@srv.admin.cheats_disabled");
            return;
        }

        switch (cmd.Command?.ToLowerInvariant())
        {
            case "give_item":
                {
                    if (_content.GetItem(cmd.StringArg ?? string.Empty) is null)
                    {
                        Reject(session, "admin", "@srv.admin.unknown_item");
                        return;
                    }

                    var target = FindSessionByName(cmd.TargetPlayer) ?? session;
                    int amount = System.Math.Max(1, cmd.IntArg);
                    // Resolve the TARGET's own ship, not the admin's cursor ship (`_ship`): a give to an
                    // aboard-ship target must spill into that player's cargo, not the admin's.
                    var targetShip = target.Ships.TryGetValue(target.ActiveShipId, out var ts) ? ts : _noShip;
                    new MaterialPool(_content, target.State, targetShip).Add(cmd.StringArg!, amount);
                    SendInventory(target);
                    CheatLog(p, $"gave {amount} {cmd.StringArg} to {target.State.Name}");
                    break;
                }

            case "teleport_to_location":
                p.Position = new Vector3f(cmd.X, cmd.Y, cmd.Z);
                // A plain PlayerStateUpdate position is ignored by the client and then reverted by its
                // client-authoritative move stream — every server-side teleport must go through the
                // RespawnNotice snap channel or it silently does nothing (#414 M7).
                Send(session, new RespawnNotice { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Reason = "@srv.tp.done" });
                SendPlayerState(session);
                CheatLog(p, $"teleported to ({cmd.X:0.#}, {cmd.Y:0.#}, {cmd.Z:0.#})");
                break;

            // The named form of the same command ("/tp village2"). Same-body only and therefore the same
            // gate as the coordinate teleport it extends — cross-body jumping stays fleet-admin `/goto`.
            case "teleport_to_named":
                AdminTeleportNamed(session, cmd.StringArg);
                break;

            case "teleport_to_player":
                {
                    var target = FindSessionByName(cmd.TargetPlayer);
                    if (target is null)
                    {
                        Reject(session, "admin", "@srv.admin.no_target");
                        return;
                    }

                    // A position is only meaningful inside its own scene: while flying a space instance the
                    // snap channel would fight the flight scene (same guard as /tp), and a target who is in
                    // space or on another body has coordinates that mean nothing on the admin's body — copying
                    // them raw dropped the admin at a spot picked from the wrong scene (#1030).
                    if (InSpace(p.PlayerId))
                    {
                        Reject(session, "admin", "@srv.tp.no_surface_targets");
                        return;
                    }

                    if (InSpace(target.State.PlayerId)
                        || !string.Equals(target.CurrentLocationId, session.CurrentLocationId, System.StringComparison.Ordinal))
                    {
                        Reject(session, "admin", "@srv.tpp.not_here:" + target.State.Name);
                        return;
                    }

                    // Beside the target, not inside their capsule (#1055).
                    p.Position = LandingSpotNear(target.State.Position, target.State.Yaw);
                    // Same snap-channel rule as teleport_to_location (#414 M7).
                    Send(session, new RespawnNotice { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Reason = "@srv.tp.to:" + target.State.Name });
                    SendPlayerState(session);
                    UpdateAboard(session); // jumping onto/off a ship must flip the aboard state now, not on the next move (parity with /tp)
                    CheatLog(p, $"teleported to player {target.State.Name}");
                    break;
                }

            case "set_time":
                _timeOfDay = cmd.StringArg ?? _timeOfDay;
                Broadcast(new ServerMessage { Text = "@srv.admin.time_set:" + _timeOfDay });
                CheatLog(p, $"set time to {_timeOfDay}");
                break;

            case "set_weather":
                _weather = cmd.StringArg ?? _weather;
                Broadcast(new ServerMessage { Text = "@srv.admin.weather_set:" + _weather });
                CheatLog(p, $"set weather to {_weather}");
                break;

            case "fly":
                p.Fly = !p.Fly;
                Send(session, new ServerMessage { Text = p.Fly ? "@srv.admin.fly_on" : "@srv.admin.fly_off" });
                CheatLog(p, $"toggled fly to {p.Fly}");
                break;

            case "godmode":
                p.GodMode = !p.GodMode;
                Send(session, new ServerMessage { Text = p.GodMode ? "@srv.admin.god_on" : "@srv.admin.god_off" });
                CheatLog(p, $"toggled god mode to {p.GodMode}");
                break;

            case "instant_build":
                p.InstantBuild = !p.InstantBuild;
                Send(session, new ServerMessage { Text = p.InstantBuild ? "@srv.admin.build_on" : "@srv.admin.build_off" });
                CheatLog(p, $"toggled instant build to {p.InstantBuild}");
                break;

            // ---- Story QA (P8 telemetry): jump around the arc for testing ----
            case "advance_story":
                {
                    int beats = AdminAdvanceStory(cmd.IntArg);
                    Send(session, new ServerMessage { Text = "@srv.admin.story_advanced:" + beats });
                    CheatLog(p, $"advanced story by {System.Math.Max(1, cmd.IntArg)} (beats {beats})");
                    break;
                }

            case "reveal_finale":
                AdminRevealFinale();
                Send(session, new ServerMessage
                {
                    Text = _storyState.GuardianSystemRevealed
                        ? "@srv.admin.finale_revealed"
                        : "@srv.admin.no_story_finale",
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
                        Reject(session, "admin", "@srv.admin.no_ship");
                        return;
                    }

                    if (_content.GetShipModule(key) is null)
                    {
                        Reject(session, "admin", "@srv.module.unknown");
                        return;
                    }

                    if (!_ship.HasModule(key))
                    {
                        _ship.Modules.Add(key);
                        ResizeCargo(_ship);
                        RecomputeShipCombatStats();
                        SaveFleet(session); // through the fleet, so the per-ship row and the legacy key agree (#848)
                        SendShipCombatStatus(session);
                        SendPlayerState(session);
                    }

                    Send(session, new ServerMessage { Text = "@srv.admin.module_fitted:" + key });
                    CheatLog(p, $"fitted ship module {key}");
                    break;
                }

            case "reveal_lore":
                {
                    int n = AdminRevealAllLore(session);
                    Send(session, new ServerMessage { Text = "@srv.admin.lore_revealed:" + n });
                    CheatLog(p, "revealed all story lore");
                    break;
                }

            case "goto_core":
                AdminGotoCore(session);
                Send(session, new ServerMessage
                {
                    Text = _storyState.GuardianSystemRevealed
                        ? "@srv.admin.core_dropped"
                        : "@srv.admin.no_story_core",
                });
                CheatLog(p, "teleported to the Guardian core chamber");
                break;

            default:
                Reject(session, "admin", "@srv.admin.unknown_cmd");
                break;
        }
    }

    /// <summary>The joined session playing under <paramref name="name"/>. Matched case-insensitively and
    /// with surrounding whitespace/quotes ignored, like every other admin-side player lookup
    /// (<c>/where</c>, <c>/builds</c>, <c>/goto</c>, <c>/kick</c>) — an exact-case compare made
    /// <c>/tpp marcel</c> fail for <c>Marcel</c> with a message that read like the player did not exist
    /// (#980).</summary>
    private PlayerSession? FindSessionByName(string? name)
    {
        string wanted = (name ?? string.Empty).Trim().Trim('"').Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && string.Equals(s.State.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }
        }

        return null;
    }

    private void CheatLog(PlayerState admin, string message)
        => _log.Info($"[CHEAT] Admin {admin.Name} {message}.");

    /// <summary>Per-player mode override (#1121): <c>/mode &lt;player&gt; survival|creative|world</c> — the
    /// world admin lets one player play by another mode's rules than the world's (kid = creative flight +
    /// free crafting, parent unchanged). "world" clears the override. Online players only: the override is
    /// applied to the live session and persisted with it.</summary>
    private void AdminSetPlayerMode(PlayerSession session, string? targetName, string? modeArg)
    {
        var target = FindSessionByName(targetName);
        if (target is null)
        {
            Reject(session, "admin", "@srv.mode.usage");
            return;
        }

        PlayerModeOverride? over = (modeArg ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "survival" => PlayerModeOverride.Survival,
            "creative" => PlayerModeOverride.Creative,
            "world" or "none" or "clear" => PlayerModeOverride.None,
            _ => null,
        };
        if (over is null)
        {
            Reject(session, "admin", "@srv.mode.usage");
            return;
        }

        target.State.ModeOverride = over.Value;
        _repo.SavePlayer(target.State);
        CheatLog(session.State, $"set {target.State.Name}'s mode override to {over.Value}");

        // The target's client re-reads its effective rules (mode label, O2 bar, flight) right away; every
        // admin's Settings tab re-renders its player-mode rows from the re-broadcast roster.
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && (s == target || s.State.IsAdmin))
            {
                SendRules(s);
            }
        }

        SendPlayerState(target); // CanFly follows the override
        string modeName = Rules.ModeFor(over.Value).ToString();
        Send(target, new ServerMessage
        {
            Text = Localize(target.Locale, over.Value == PlayerModeOverride.None ? "srv.mode.cleared_you" : "srv.mode.set_you")
                .Replace("{mode}", modeName),
        });
        if (target != session)
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.mode.set_admin")
                    .Replace("{player}", target.State.Name)
                    .Replace("{mode}", over.Value == PlayerModeOverride.None ? Rules.GameMode.ToString() : modeName),
            });
        }
    }

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

    private bool StationAvailable(PlayerState player, CraftingStation station) => StationAvailable(player, _ship, station);

    /// <summary>The station check against an explicit ship (the per-player tick that publishes
    /// <see cref="StationsInReach"/> must not swing the ship cursor around, #1070).</summary>
    private bool StationAvailable(PlayerState player, ShipState ship, CraftingStation station)
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
                CraftingStation.Transmuter => NearStationBlock(player, "matter_forge"),
                CraftingStation.AlgaeTank => NearStationBlock(player, "algae_tank"),
                CraftingStation.Campfire => NearStationBlock(player, "campfire"),
                // A factory's production terminal — only present inside spawned factory structures
                // (players don't craft/place it), so factory recipes are only available at a factory.
                CraftingStation.Factory => NearStationBlock(player, "factory_terminal"),
                _ => false,
            };
        }

        var moduleKey = station switch
        {
            CraftingStation.Workshop => "workshop",
            CraftingStation.Refinery => "refinery",
            CraftingStation.Detoxifier => "detoxifier",
            CraftingStation.Transmuter => "transmuter",
            _ => string.Empty,
        };

        return moduleKey.Length > 0 && ship.HasModule(moduleKey);
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

                SaveFleet(session); // each player's own ships; also refreshes the fleet index on their state
                _repo.SavePlayer(session.State);
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

        // Paint moderation v1 (#821): "/reportpaint" flags the nearest painted block for operator review.
        // Like /bump it is intercepted before the radio gate — reporting must not require any equipment.
        // NOT "/report": that is the client-side PLAYER report to the worlds portal (ReportChatCommand).
        if (text.Equals("/reportpaint", System.StringComparison.OrdinalIgnoreCase))
        {
            int reportNow = System.Environment.TickCount;
            if (reportNow - session.LastChatTick < 700)
            {
                return; // rate limit
            }

            session.LastChatTick = reportNow;
            HandlePaintReport(session);
            return;
        }

        // The same for player-designed forms (#843) — geometry can be just as rude as a painting.
        if (text.Equals("/reportshape", System.StringComparison.OrdinalIgnoreCase))
        {
            int reportNow = System.Environment.TickCount;
            if (reportNow - session.LastChatTick < 700)
            {
                return; // rate limit
            }

            session.LastChatTick = reportNow;
            HandleCustomShapeReport(session);
            return;
        }

        // A pasted build share code would be silently cut to garbage by the chat cap — and a chat line
        // cannot be copied back out anyway. Refuse it with a pointer to the blueprint tool's clipboard
        // flow instead of truncating (#1154).
        if (text.Length > 200 && text.StartsWith("BBTS1-", System.StringComparison.Ordinal))
        {
            Send(session, new ServerMessage { Text = "@srv.chat.code_too_long" });
            return;
        }

        if (text.Length > 200)
        {
            text = text.Substring(0, 200);
        }

        // Strip control characters (CR/LF/tab/ANSI/NUL) before the line is broadcast verbatim to other
        // players — otherwise a client could inject newlines/escape sequences into everyone's chat UI.
        text = StripControlChars(text);
        if (text.Length == 0)
        {
            return;
        }

        // Observers are silent by default (issue #487): an invisible admin whose chat line pops up in the
        // channel is no longer invisible. "/say Text" is the deliberate way to speak.
        if (session.Spectating)
        {
            const string sayPrefix = "/say ";
            if (!text.StartsWith(sayPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                Send(session, new ServerMessage { Text = "@srv.obs.muted" });
                return;
            }

            text = text.Substring(sayPrefix.Length).Trim();
            if (text.Length == 0)
            {
                return;
            }
        }

        if (!HasAnyRadio(session) && !session.Spectating)
        {
            Reject(session, "chat", "@srv.misc.need_radio");
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

        // Per-speaker frame-rate cap before the 1→N audience fan-out. Real voice is ~50 frames/s (20 ms
        // each); 60/s is generous. Without it a client could flood frames as fast as the socket allows,
        // each amplified to the whole radio audience (audit 2026-07-05).
        if (_uptime < session.NextVoiceFrameAt)
        {
            return;
        }

        session.NextVoiceFrameAt = _uptime + (1.0 / 60.0);
        frame.FromPlayerId = session.State.PlayerId; // authoritative sender id (don't trust the client's field)
        SendToRadioAudienceExcept(session, frame, DeliveryMode.Unreliable);
    }

    private void Reject(PlayerSession session, string action, string reason)
        => Send(session, new ActionRejected { Action = action, Reason = reason });

    /// <summary>Seconds between two "backpack full" toasts for one player (#600).</summary>
    private const double InventoryFullHintCooldown = 8.0;

    /// <summary>
    /// Tells the player when a pool could not store everything it was handed (#600). Mined drops and craft
    /// outputs used to vanish without a word once both the 24 inventory slots and the cargo hold were full —
    /// the block broke, and nothing arrived. Throttled per player, because one drill swing can overflow on a
    /// dozen blocks at once. The token is localized client-side (<c>GameBootstrap.ServerMessageText</c>), so
    /// the player reads it in their own language rather than the server's.
    /// </summary>
    private void WarnIfPoolOverflowed(PlayerSession session, MaterialPool pool)
    {
        if (pool.Overflow <= 0 || _uptime < session.NextInventoryFullHintAt)
        {
            return;
        }

        session.NextInventoryFullHintAt = _uptime + InventoryFullHintCooldown;
        Send(session, new ServerMessage { Text = "@inventory_full" });
    }

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
            SuitClimateActive = p.SuitClimateActive,
            LifeSupportSource = p.LifeSupportSource,
            StationName = CurrentStationName(p.PlayerId),
            AiCoreTier = VegaCoreTier(session),
            InSpeeder = p.InSpeeder,
            Spectating = session.Spectating,
            // A creative world lets everybody fly; a per-player Creative override (#1121) grants it too;
            // /fly keeps working as the per-player admin cheat.
            CanFly = Rules.CreativeFlightFor(p.ModeOverride) || p.Fly,
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
            Bodies = sys.Bodies.Select(b => ToNetBody(b, session)).ToArray(),
            Tier = FrontierTierOf(sys.Id), // #1122: the star map tags frontier systems
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

        // Only the FIRST arrival on a body counts toward the explorer achievements — hopping back and forth
        // between two planets must not farm them.
        if (session.State.LandedBodies.Add(body.Id))
        {
            OnAchievementVisit(session);
            RecordPlaceDiscovery(session, body); // #1113: a "Places" Codex entry + the knowledge grant
        }

        if (!string.IsNullOrEmpty(body.SystemId) && session.State.KnownSystems.Add(body.SystemId))
        {
            OnAchievementVisitSystem(session); // "System Hopper" / "Starfarer" (#1102)
            RecordStoryMilestone(); // a new star system mapped → story milestone (P3)
            MaybeGrowGalaxy(session, body.SystemId); // #1123: reaching the edge pushes the frontier out
        }

        SendExploredMap(session, body.Id); // #1113: the remembered fog for this body's planet map
    }

    /// <summary>Records that a player has entered a star system in flight (a hyperjump arrival) — reveals
    /// the system's bodies + mini map on the travel screen, without marking any body landed. Persisted.</summary>
    private void MarkSystemKnown(PlayerSession session, string systemId)
    {
        if (!string.IsNullOrEmpty(systemId) && session.State.KnownSystems.Add(systemId))
        {
            OnAchievementVisitSystem(session); // "System Hopper" / "Starfarer" (#1102)
            RecordStoryMilestone(); // a new star system mapped → story milestone (P3)
            MaybeGrowGalaxy(session, systemId); // #1123: reaching the edge pushes the frontier out
        }
    }

    /// <summary>Projects a galaxy body to its network form, including its fixed-landing-pad capacity + how many
    /// pads are currently free (item 38) so the star map can flag a full body. Non-surface bodies have 0 pads.
    /// The free count is AS SEEN BY the receiver (#999): their own in-space reservation doesn't count against
    /// them, matching the pad chooser (#977) and the landing itself.</summary>
    private NetBody ToNetBody(BlocksBeyondTheStars.Shared.World.CelestialBody b, PlayerSession receiver)
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
            SizeBias = b.SizeBias, // #549: the client sizes this body with the same bias the server does
            RingSeed = b.RingSeed, // #596: 0 = no rings; the client renders the ring system from this
            PadsTotal = total,
            PadsFree = total > 0 ? FreePadCount(b.Id, total, receiver.State.PlayerId) : 0,
        };
    }

    private void SendRules(PlayerSession session)
    {
        var r = Rules;
        // The mode + its derived switches are the receiver's EFFECTIVE ones (#1121): a per-player override
        // makes the kid's client read "Creative" (and hide the O2 bar) while the world stays Survival.
        var over = session.State.ModeOverride;
        // World admins additionally get the online players' overrides, feeding the Settings-tab rows.
        string[] modeNames = System.Array.Empty<string>();
        string[] modeValues = System.Array.Empty<string>();
        if (session.State.IsAdmin)
        {
            var online = _sessions.Values.Where(s => s.Joined).OrderBy(s => s.State.Name).Take(24).ToList();
            modeNames = online.Select(s => s.State.Name).ToArray();
            modeValues = online.Select(s => s.State.ModeOverride.ToString()).ToArray();
        }

        Send(session, new ServerRules
        {
            GameMode = r.ModeFor(over).ToString(),
            Pvp = r.Pvp.ToString(),
            WeaponMode = r.WeaponMode.ToString(),
            AggressiveAliens = r.AggressiveAliens.ToString(),
            EnvironmentalHazards = r.EnvironmentalHazards.ToString(),
            DeathPenalty = r.DeathPenalty.ToString(),
            KeepInventoryOnDeath = r.KeepInventoryOnDeath,
            KeepShipOnDeath = r.KeepShipOnDeath,
            OxygenEnabled = r.OxygenEnabledFor(over),
            AdminCheatsActive = r.CheatsAllowed,
            CreatureAbundance = r.CreatureAbundance.ToString(),
            PlanetEnemies = r.PlanetEnemies.ToString(),
            SpaceNpcEnemies = r.SpaceNpcEnemies.ToString(),
            AlienUfos = r.AlienUfos.ToString(),
            Bandits = r.Bandits.ToString(),
            InstantTravel = r.InstantTravel,
            AutoAim = r.AutoAim,
            StarterTeleporter = r.StarterTeleporter,
            FrontierDanger = r.FrontierDanger,
            VoiceChatEnabled = _config.VoiceChatEnabled,
            PlayerModeNames = modeNames,
            PlayerModeValues = modeValues,
        });
    }

    /// <summary>World options, live edit (world admin only): applies the sent gameplay activities to the
    /// running rules, persists them into the save's rules override and re-broadcasts the rule set, so the
    /// change survives reloads and every client's settings view updates.</summary>
    private void HandleSetWorldRules(PlayerSession session, SetWorldRulesIntent intent)
    {
        if (!session.State.IsAdmin)
        {
            Reject(session, "world_rules", "@srv.admin.rules_admin_only");
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
        Apply(intent.Bandits, v => Rules.Bandits = v);
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

        // Environmental hazards (#670): the live switch for the temperature hazard — Off disables it
        // on a running world without CLI flags, Light/Hard soften/sharpen it.
        if (!string.IsNullOrEmpty(intent.EnvironmentalHazards)
            && System.Enum.TryParse<HazardLevel>(intent.EnvironmentalHazards, ignoreCase: true, out var hz))
        {
            Rules.EnvironmentalHazards = hz;
        }

        if (!string.IsNullOrEmpty(intent.AutoAim))
        {
            Rules.AutoAim = intent.AutoAim.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(intent.StarterTeleporter))
        {
            Rules.StarterTeleporter = intent.StarterTeleporter.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(intent.FrontierDanger))
        {
            Rules.FrontierDanger = intent.FrontierDanger.Equals("On", System.StringComparison.OrdinalIgnoreCase);
        }

        _meta.RulesOverride = Rules.Clone(); // the world owns its rules — persist the edit
        _repo.SaveMetadata(_meta);

        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                SendRules(s);
                if (GrantStarterTeleporter(s)) { SendInventory(s); } // #1056: flipping the rule on hands the device to everyone online now
            }
        }

        _log.Info($"World rules updated by '{session.State.Name}': creatures={Rules.CreatureAbundance}, " +
                  $"planet={Rules.PlanetEnemies}, space={Rules.SpaceNpcEnemies}, ufos={Rules.AlienUfos}, " +
                  $"bandits={Rules.Bandits}, instantTravel={Rules.InstantTravel}, hazards={Rules.EnvironmentalHazards}, " +
                  $"autoAim={Rules.AutoAim}, starterTeleporter={Rules.StarterTeleporter}.");
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

    /// <summary>
    /// Permanently destroys everything the player holds of one item (#599). Every other way to part with an
    /// item stores it somewhere (hold, crate, trade) or consumes it in a recipe, so unwanted loot used to be
    /// carried around forever. The slot picks the item — a dyed/shaped stack has a composite key — and then
    /// <b>all</b> stacks of that key go, because clearing "the 300 dirt" one stack at a time is busywork.
    /// The starter kit is refused so nobody can strand themselves without a drill or a light; observers never
    /// reach here (<c>SpectatorMayHandle</c> defaults to false). Client-side the button is hidden for those
    /// items too — this check exists because the server never trusts the client.
    /// </summary>
    private void HandleDiscardItem(PlayerSession session, DiscardItemIntent intent)
    {
        if (intent.FromCargo && !session.State.AboardShip)
        {
            Reject(session, "discard", "@srv.misc.aboard_for_cargo");
            return;
        }

        var inv = intent.FromCargo ? _ship.Cargo : session.State.Inventory;
        int slot = intent.Slot;
        if (slot < 0 || slot >= inv.SlotCount || inv.Slots[slot] is not { IsEmpty: false } stack)
        {
            return; // empty or out-of-range slot: nothing to discard
        }

        if (StarterKit.IsProtected(stack.Item))
        {
            Reject(session, "discard", "@srv.misc.starter_protected");
            return;
        }

        string item = stack.Item;
        int count = inv.CountOf(item);
        if (count <= 0 || !inv.Remove(item, count))
        {
            return;
        }

        SendInventory(session);
        _log.Info($"'{session.State.Name}' discarded {count}x {item} from the {(intent.FromCargo ? "hold" : "backpack")}.");
    }

    /// <summary>Test seam: discards the item in a player's slot through the real handler (#599).</summary>
    public void DiscardItemForTest(string playerId, int slot, bool fromCargo = false)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleDiscardItem(session, new DiscardItemIntent { Slot = slot, FromCargo = fromCargo });
        }
    }

    private void SendInventory(PlayerSession session)
    {
        Send(session, new InventoryUpdate
        {
            Personal = DumpInventory(session.State.Inventory),
            Cargo = session.State.AboardShip ? DumpInventory(_ship.Cargo) : Array.Empty<NetItemStack>(),
            CargoSlotCount = session.State.AboardShip ? _ship.Cargo.SlotCount : 0,
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

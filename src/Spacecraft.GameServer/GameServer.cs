using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.Shared.State;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// The authoritative game server: it owns the world, players and ship, validates every
/// client intent and broadcasts the resulting state. The client never decides outcomes
/// (technical requirements §7, §15). Drive it by calling <see cref="Tick"/> at the
/// configured rate, or use <see cref="Run"/> for a blocking loop.
/// </summary>
public sealed partial class GameServer
{
    private const string ShipId = "default";
    private const float MaxReach = 8f;
    private const int HotbarSlots = 9;

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
    private ServerWorld _world = null!;
    private ShipState _ship = new();
    private Galaxy _galaxy = new();

    private double _sinceAutoSave;
    private volatile bool _running;
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
    public string ActiveLocationId => _meta.ActiveLocationId;
    public IReadOnlyDictionary<int, PlayerSession> Sessions => _sessions;
    public WorldMetadata Metadata => _meta;

    // ---------------- Lifecycle ----------------

    public void Start()
    {
        _repo.Initialize();

        _meta = _repo.LoadMetadata() ?? CreateInitialMetadata();
        _repo.SaveMetadata(_meta);

        _generator = new WorldGenerator(_meta.Seed, _content);
        BuildGalaxy(); // resolves _meta.ActiveLocationId to a concrete celestial body id

        _ship = _repo.LoadShip(ShipId) ?? CreateStarterShip();
        RegisterActiveShip(_ship);
        RecomputeShipCombatStats();
        _repo.SaveShip(ShipId, _ship);

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
    }

    /// <summary>
    /// Makes <paramref name="locationId"/> (a celestial body of type <paramref name="planetTypeKey"/>)
    /// the active world: rebuilds <see cref="_world"/> (its edits load from that body's persistence key),
    /// resets + re-initialises all per-world runtime state (weather, fauna, flora, fluids, landing zones,
    /// containers) and re-stamps the ship/settlement/wreck. Used at startup and on travel.
    /// </summary>
    private void SwitchActiveWorld(string planetTypeKey, string locationId)
    {
        var planet = _content.GetPlanet(planetTypeKey)
                     ?? throw new InvalidOperationException($"Unknown planet type '{planetTypeKey}'.");

        _meta.DefaultPlanetType = planetTypeKey;
        _meta.ActiveLocationId = locationId;
        if (_ship is not null)
        {
            _ship.CurrentLocationId = locationId;
        }

        _world = new ServerWorld(_content, _generator, _repo, planet, locationId);

        ResetWorldRuntimeState();

        // Per-world systems read the new planet type / seed.
        InitWeather();
        InitFluids();
        InitFlora();
        InitCreatures();
        LoadLandingZones();
        LoadContainers();

        // (Re)stamp structures for this body (settlement/wreck are guarded against duplicate loot).
        if (_config.PlaceStarterShip)
        {
            StampShip();
        }

        if (_config.PlaceSettlements)
        {
            StampSettlement();
        }

        if (_config.PlaceWrecks)
        {
            StampWreck();
        }

        // Mark the destination visited.
        var body = _galaxy?.FindBody(locationId);
        if (body is not null && body.Status != GenerationStatus.Visited)
        {
            body.Status = GenerationStatus.Visited;
            _repo.SetLocationStatus(body.Id, body.Status.ToString());
        }

        _repo.SaveMetadata(_meta);
    }

    /// <summary>Clears all per-world runtime state so a freshly switched world doesn't keep the old
    /// planet's entities/structures. Persistent collections (landing zones, containers) are reloaded by
    /// their Load* methods; fauna/enemies/NPCs/fluids/flora re-populate from the new world.</summary>
    private void ResetWorldRuntimeState()
    {
        _creatures.Clear();
        _planetEnemies.Clear();
        _npcs.Clear();
        _settlementMarkers.Clear();
        _settlementMissionIds.Clear();
        _wreckMarkers.Clear();
        _floraRegrow.Clear();
        _fluidLevel.Clear();
        _activeFluid.Clear();
        _stations.Clear();
        _shipExtra.Clear();
        _shipStamped = false;
        _shipIsLayout = false;
        _healTank = default;
    }

    /// <summary>
    /// Travels to (and lands on) another celestial body picked from the star map: switches the active
    /// world to the destination, then relocates every player to its landing zone/ship and tells the
    /// client to reload the world. Each body keeps its own edits (persistence is keyed by body id).
    /// </summary>
    /// <summary>Travels the given player to a celestial body by id (also the test/util entrypoint).</summary>
    public void Travel(string playerId, string destinationBodyId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            HandleTravel(session, new TravelIntent { DestinationBodyId = destinationBodyId });
        }
    }

    private void HandleTravel(PlayerSession session, TravelIntent intent)
    {
        if (!Rules.FreeSpaceFlight)
        {
            Reject(session, "travel", "Space flight is disabled on this server.");
            return;
        }

        var body = _galaxy?.FindBody(intent.DestinationBodyId);
        if (body is null || body.Kind != CelestialKind.Planet || string.IsNullOrEmpty(body.PlanetType))
        {
            Reject(session, "travel", "You can only travel to a planet.");
            return;
        }

        if (body.Id == _meta.ActiveLocationId)
        {
            Reject(session, "travel", "You are already there.");
            return;
        }

        // A jump to a different star system is a hyperspace jump — it needs a jump generator fitted.
        var origin = _galaxy?.FindBody(_meta.ActiveLocationId);
        bool hyperjump = origin is null || origin.SystemId != body.SystemId;
        if (hyperjump && (_ship is null || !_ship.HasModule("jump_generator")))
        {
            Reject(session, "travel", "Your ship has no jump generator — fit one to jump between star systems.");
            return;
        }

        // Take everyone out of the (old world's) space instance before switching worlds.
        foreach (var pid in _playerInstance.Keys.ToList())
        {
            LeaveSpace(pid);
        }

        SwitchActiveWorld(body.PlanetType, body.Id);

        var (systemName, planetName) = ActiveLocationNames();

        // Relocate every joined player to the new world's landing zone / ship and reload their chunks.
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            var zone = EnsureLandingZone(s.State.PlayerId);
            int surfaceY = _generator.SurfaceHeight(_world.Planet, zone.CenterX, zone.CenterZ);
            var spawn = _shipStamped ? _healTank : new Vector3f(zone.CenterX + 0.5f, surfaceY + 2f, zone.CenterZ + 0.5f);
            s.State.Position = spawn;
            s.State.RespawnPoint = _shipStamped ? _healTank : spawn;
            s.State.AboardShip = true;
            s.SentChunks.Clear();

            Send(s, new WorldReset { PlanetType = _meta.DefaultPlanetType, PlanetName = planetName, SystemName = systemName, Hyperjump = hyperjump });
            SendPlayerState(s);
            SendShipCombatStatus(s);
            SendShipPlacement(s);
            SendShipStations(s);
            SendPlanetPois(s);
            SendEnvironment(s);
            PopulateCreaturesNear(s.State, CreatureCapPerPlayer); // arrive to a living world, not an empty one
            SendCreatures(s);
            SendContainers(s);
            SendStarMap(s);
            Send(s, new ServerMessage { Text = hyperjump ? $"Hyperjumped to {systemName} — {planetName}." : $"Arrived at {planetName}." });
        }
    }

    private ShipState CreateStarterShip()
    {
        // Prefer the data-driven "starter" ship design; fall back to a built-in module list.
        if (_content.GetShip("starter") is { } def)
        {
            return BuildShipFromDefinition(def);
        }

        var ship = new ShipState { CurrentLocationId = _meta.DefaultPlanetType };
        foreach (var key in new[] { "cockpit", "reactor", "life_support", "workshop", "medbay", "quarters", "cargo_hold_basic" })
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
        double tickSeconds = 1.0 / System.Math.Max(1, _config.TickRate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds;

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
    }

    public void Stop()
    {
        _running = false;
        SaveAll();
        _repo.Flush();
        _transport.Stop();
        _log.Info("Server stopped and world saved.");
    }

    // ---------------- Tick ----------------

    public void Tick(double deltaSeconds)
    {
        _transport.Poll();
        TickEnvironment(deltaSeconds);
        TickSpace(deltaSeconds);
        TickEnemies(deltaSeconds);
        TickPresence(deltaSeconds);
        TickFluids(deltaSeconds);
        TickWeather(deltaSeconds);
        TickFlora(deltaSeconds);
        TickCreatures(deltaSeconds);
        TickNpcs(deltaSeconds);
        StreamChunks();

        _sinceAutoSave += deltaSeconds;
        if (_sinceAutoSave >= _config.AutoSaveIntervalMinutes * 60.0)
        {
            _sinceAutoSave = 0;
            SaveAll();
            _log.Info("Autosave complete.");
        }
    }

    /// <summary>Test helper kept explicit so tests can drive one authoritative server tick.</summary>
    public void TickForTest(double deltaSeconds) => Tick(deltaSeconds);

    private void TickEnvironment(double dt)
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined)
            {
                continue;
            }

            UpdateAboard(session);

            var p = session.State;
            DecayTeleportCooldown(p.PlayerId, dt);
            TickStealth(session, dt);
            float maxOxygen = MaxOxygen(p);
            if (p.GodMode)
            {
                p.Health = 100f;
                p.Oxygen = maxOxygen;
                p.Hunger = 100f;
                continue; // invulnerable: no drain, no death
            }

            if (p.AboardShip || !Rules.OxygenEnabled || AtmosphereBreathable)
            {
                // Aboard the ship (life support), oxygen disabled by rules, or a breathable
                // atmosphere: regenerate, no drain (up to the equipped tank capacity).
                p.Oxygen = System.Math.Min(maxOxygen, p.Oxygen + (float)(dt * 25));
                p.Health = System.Math.Min(100f, p.Health + (float)(dt * 2));
            }
            else
            {
                // Outside without a breathable atmosphere (toxic / airless): drain at the configured rate.
                float drain = (float)(dt * Rules.OxygenDrainPerSecond);
                if (_oxygenExtractability > 0 && p.Inventory.Has("oxygen_extractor", 1))
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

            // Hunger (survival): aboard the ship or when disabled, sate; otherwise drain and,
            // once empty, starve (health loss until the player eats).
            if (p.AboardShip || !Rules.HungerEnabled)
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
            }
        }
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

        p.Health = 100f;
        p.Oxygen = MaxOxygen(p);
        p.SuitEnergy = 100f;
        p.Hunger = 100f;
        p.Stealthed = false;
        p.Position = p.RespawnPoint;
        p.AboardShip = true;

        Send(session, new RespawnNotice
        {
            X = p.RespawnPoint.X,
            Y = p.RespawnPoint.Y,
            Z = p.RespawnPoint.Z,
            Reason = reason,
            SalvageCapsuleDropped = salvaged,
        });
        SendInventory(session);
        SendPlayerState(session);
        _repo.SavePlayer(p);
        _log.Info($"Player '{p.Name}' respawned at heal-tank (salvage={salvaged}).");
    }

    private void StreamChunks()
    {
        int radius = System.Math.Max(1, _config.ViewDistanceChunks);
        const int perTickBudget = 8;

        foreach (var session in _sessions.Values)
        {
            if (!session.Joined)
            {
                continue;
            }

            var center = WorldConstants.WorldToChunk(session.State.Position.ToBlock());
            int sent = 0;
            for (int dy = -1; dy <= 1 && sent < perTickBudget; dy++)
            for (int dx = -radius; dx <= radius && sent < perTickBudget; dx++)
            for (int dz = -radius; dz <= radius && sent < perTickBudget; dz++)
            {
                var coord = new ChunkCoord(center.X + dx, center.Y + dy, center.Z + dz);
                if (session.SentChunks.Contains(coord))
                {
                    continue;
                }

                var chunk = _world.GetOrLoadChunk(coord);
                Send(session, new ChunkDataMessage
                {
                    Cx = coord.X,
                    Cy = coord.Y,
                    Cz = coord.Z,
                    Blocks = chunk.ToArray(),
                });
                session.SentChunks.Add(coord);
                sent++;
            }
        }
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
            _repo.SaveShip(ShipId, _ship);
            Broadcast(new PlayerLeft { PlayerId = session.State.PlayerId }); // remove their avatar elsewhere
        }

        _sessions.Remove(connectionId);
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

        switch (message)
        {
            case MoveIntent move: HandleMove(session, move); break;
            case SelectHotbarIntent hotbar: session.State.SelectedHotbarSlot = System.Math.Clamp(hotbar.Slot, 0, HotbarSlots - 1); break;
            case MineBlockIntent mine: HandleMine(session, mine); break;
            case PlaceBlockIntent place: HandlePlace(session, place); break;
            case CraftIntent craft: HandleCraft(session, craft); break;
            case UnlockBlueprintIntent unlock: HandleUnlock(session, unlock); break;
            case ChatIntent chat: HandleChat(session, chat); break;
            case RequestStarMap: SendStarMap(session); break;
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
            case LeaveSpaceIntent: HandleLeaveSpace(session); break;
            case FireWeaponIntent fire: HandleFireWeapon(session, fire); break;
            case AttackEntityIntent attack: HandleAttackEntity(session, attack); break;
            case UseStationIntent use: HandleUseStation(session, use); break;
            case SetAppearanceIntent appearance: HandleSetAppearance(session, appearance); break;
            case CraftShipIntent craftShip: HandleCraftShip(session, craftShip); break;
            case SwitchShipIntent switchShip: HandleSwitchShip(session, switchShip); break;
            case ConsumeItemIntent consume: HandleConsume(session, consume); break;
            case LootContainerIntent loot: HandleLootContainer(session, loot); break;
            case ShipMoveIntent shipMove: HandleShipMove(session, shipMove); break;
            case DisassembleIntent disassemble: HandleDisassemble(session, disassemble); break;
            case TradeRequestIntent tradeReq: HandleTradeRequest(session, tradeReq); break;
            case TradeRespondIntent tradeResp: HandleTradeRespond(session, tradeResp); break;
            case TradeOfferIntent tradeOffer: HandleTradeOffer(session, tradeOffer); break;
            case TradeConfirmIntent: HandleTradeConfirm(session); break;
            case TradeCancelIntent: HandleTradeCancel(session); break;
            case ScanIntent scan: HandleScan(session, scan); break;
            case ScanEntityIntent scanEntity: HandleScanEntity(session, scanEntity); break;
            case LoadRationIntent loadRation: HandleLoadRation(session, loadRation); break;
            case TeleportToShipIntent: HandleTeleportToShip(session); break;
            case ToggleStealthIntent: HandleToggleStealth(session); break;
            case BoardStationIntent boardStation: HandleBoardStation(session, boardStation); break;
            case LeaveStationIntent: HandleLeaveStation(session); break;
            case RepairWreckIntent repairWreck: HandleRepairWreck(session, repairWreck); break;
            case ClaimWreckIntent: HandleClaimWreck(session); break;
            case TravelIntent travel: HandleTravel(session, travel); break;
        }
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

        var state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);

        // A configured admin name is granted the Admin role (the world creator keeps WorldAdmin).
        if (state.Role != PlayerRole.WorldAdmin && _config.AdminPlayers.Contains(name))
        {
            state.Role = PlayerRole.Admin;
        }

        var session = new PlayerSession(connectionId, state) { Joined = true };
        _sessions[connectionId] = session;

        var (systemName, planetName) = ActiveLocationNames();
        SendTo(connectionId, new JoinAccepted
        {
            PlayerId = state.PlayerId,
            WorldSeed = _meta.Seed,
            PlanetType = _meta.DefaultPlanetType,
            PlanetName = planetName,
            SystemName = systemName,
        });
        SendInventory(session);
        SendPlayerState(session);
        SendRules(session);
        SendShipCombatStatus(session);
        SendShipPlacement(session);
        SendShipStations(session);
        SendPlanetPois(session);
        SendOwnedShips(session);
        SendEnvironment(session);
        PopulateCreaturesNear(state, CreatureCapPerPlayer); // seed fauna so the world feels alive on entry
        SendCreatures(session);
        SendContainers(session);
        SendExistingPresences(session); // show already-online players to the newcomer

        _log.Info($"Player '{name}' joined (connection {connectionId}).");
    }

    private PlayerState CreateNewPlayer(string name)
    {
        int spawnX = 0, spawnZ = 0;
        if (Rules.PersonalLandingZones)
        {
            var zone = EnsureLandingZone(name);
            spawnX = zone.CenterX;
            spawnZ = zone.CenterZ;
        }

        int surfaceY = _generator.SurfaceHeight(_world.Planet, spawnX, spawnZ);
        var spawn = new Vector3f(spawnX + 0.5f, surfaceY + 2f, spawnZ + 0.5f);
        var state = new PlayerState
        {
            PlayerId = name,
            Name = name,
            Position = spawn,
            RespawnPoint = _shipStamped ? _healTank : spawn, // the heal-tank in the ship's Medbay
            AboardShip = true,
            // The very first player to join becomes the world admin (world creator).
            Role = _repo.ListPlayerIds().Count == 0
                ? PlayerRole.WorldAdmin
                : (_config.AdminPlayers.Contains(name) ? PlayerRole.Admin : PlayerRole.Player),
        };

        // Starter kit: a basic drill, a block placer and a hand scanner in the first hotbar slots.
        state.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        state.Inventory.SetSlot(1, new ItemStack("block_placer", 1));
        state.Inventory.SetSlot(2, new ItemStack("hand_scanner", 1));
        _repo.SavePlayer(state);
        return state;
    }

    /// <summary>
    /// Adds a fully-joined player session without a network handshake, using a synthetic
    /// (negative) connection id. Used by singleplayer/local co-op and by multi-player server
    /// tests, since the loopback transport only models a single networked client. The caller
    /// drives this player's actions through the authoritative server methods directly.
    /// </summary>
    public PlayerSession AddLocalPlayer(string name)
    {
        var state = _repo.LoadPlayer(name) ?? CreateNewPlayer(name);

        if (state.Role != PlayerRole.WorldAdmin && _config.AdminPlayers.Contains(name))
        {
            state.Role = PlayerRole.Admin;
        }

        int connectionId = _nextLocalConnectionId--;
        var session = new PlayerSession(connectionId, state) { Joined = true };
        _sessions[connectionId] = session;
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
            HandleCraft(session, new CraftIntent { RecipeKey = recipeKey, Count = count });
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

    // ---------------- Authoritative validators ----------------

    private void HandleMove(PlayerSession session, MoveIntent move)
    {
        // MVP: trust position but clamp to sane finite values. (Full movement validation later.)
        if (float.IsFinite(move.X) && float.IsFinite(move.Y) && float.IsFinite(move.Z))
        {
            session.State.Position = new Vector3f(move.X, move.Y, move.Z);
            session.State.Yaw = move.Yaw;
            session.State.Pitch = move.Pitch;
        }
    }

    // Accumulated mining effort per block (a hard block needs several hits before it breaks).
    private readonly Dictionary<Vector3i, float> _miningProgress = new();

    private void HandleMine(PlayerSession session, MineBlockIntent mine)
    {
        var pos = new Vector3i(mine.X, mine.Y, mine.Z);
        var current = _world.GetBlock(pos);
        if (current.IsAir)
        {
            Reject(session, "mine", "Block is already empty.");
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

        if (!session.State.IsAdmin && IsLandingZoneBlockedForOther(session.State.PlayerId, pos))
        {
            Reject(session, "mine", "This is another player's protected landing zone.");
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
        float progress = (_miningProgress.TryGetValue(pos, out var prev) ? prev : 0f) + power;

        if (progress + 0.0001f < hardness)
        {
            _miningProgress[pos] = progress;
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
        _world.SetBlock(pos, BlockId.Air);
        _miningProgress.Remove(pos);

        foreach (var drop in def.Drops)
        {
            pool.Add(drop.Item, drop.Count);
        }

        Broadcast(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue });
        if (IsFlora(current.Value))
        {
            ScheduleFloraRegrow(pos, current.Value); // regrows if the host stays intact
        }

        OnBlockMined(session, def.Key);
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
            if (b.IsAir || IsShipBlock(p) || IsSettlementBlock(p) || IsStationBlock(p))
            {
                continue;
            }

            var d = _world.Definition(b);
            if (d is null || !d.Mineable)
            {
                continue;
            }

            if (!session.State.IsAdmin && IsLandingZoneBlockedForOther(session.State.PlayerId, p))
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

        var pos = new Vector3i(place.X, place.Y, place.Z);
        if (!_world.GetBlock(pos).IsAir)
        {
            Reject(session, "place", "Target is not empty.");
            return;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "place", "Out of reach.");
            return;
        }

        if (!session.State.IsAdmin && IsLandingZoneBlockedForOther(session.State.PlayerId, pos))
        {
            Reject(session, "place", "This is another player's protected landing zone.");
            return;
        }

        if (IsStationBlock(pos))
        {
            Reject(session, "place", "This station is protected.");
            return;
        }

        // Seeds / flora only take on a suitable host block (mud, grass, crystal, ...).
        if (IsFlora(blockDef.NumericId.Value) && !IsValidFloraHost(blockDef.NumericId.Value, pos))
        {
            Reject(session, "place", "This plant needs suitable ground beneath it.");
            return;
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

        _world.SetBlock(pos, blockDef.NumericId);

        Broadcast(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = blockDef.NumericId.Value });
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

        int count = System.Math.Max(1, craft.Count);

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

        Send(session, new CraftResult { Success = true, RecipeKey = recipe.Key });
        SendInventory(session);
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

        pool.Remove(bp.UnlockCost);
        session.State.KnowledgePoints -= bp.KnowledgeCost;
        session.State.UnlockedBlueprints.Add(bp.Key);

        Send(session, new ServerMessage { Text = $"Blueprint unlocked: {bp.Key}" });
        SendInventory(session);
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

    private static bool WithinReach(PlayerState player, Vector3i block)
    {
        var center = new Vector3f(block.X + 0.5f, block.Y + 0.5f, block.Z + 0.5f);
        return player.Position.DistanceSquared(center) <= MaxReach * MaxReach;
    }

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

        if (!player.AboardShip)
        {
            return false;
        }

        var moduleKey = station switch
        {
            CraftingStation.Workshop => "workshop",
            CraftingStation.Refinery => "refinery",
            CraftingStation.Lab => "lab",
            CraftingStation.MachineRoom => "machine_room",
            CraftingStation.Detoxifier => "detoxifier",
            _ => string.Empty,
        };

        return moduleKey.Length > 0 && _ship.HasModule(moduleKey);
    }

    /// <summary>
    /// Whether the player can use a market (barter) trade station — either the ship's trade console
    /// (aboard) or standing next to a settlement vendor.
    /// </summary>
    private bool MarketAvailable(PlayerState player)
        => player.AboardShip || NearSettlementVendor(player) || NearSpaceStationVendor(player);

    private void SaveAll()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Joined)
            {
                _repo.SavePlayer(session.State);
            }
        }

        _repo.SaveShip(ShipId, _ship);
        _repo.SaveMetadata(_meta);
    }

    /// <summary>Player chat (requires a comm radio; length-capped + rate-limited). Broadcast to all.</summary>
    private void HandleChat(PlayerSession session, ChatIntent chat)
    {
        string text = (chat.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > 200)
        {
            text = text.Substring(0, 200);
        }

        if (!session.State.Inventory.Has("comm_radio", 1))
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
        Broadcast(new ChatMessage { Sender = sender, Text = text });
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
            StationName = CurrentStationName(p.PlayerId),
        });
    }

    /// <summary>Resolves the friendly (system, planet) names for the world's active location.</summary>
    private (string System, string Planet) ActiveLocationNames()
    {
        foreach (var sys in _galaxy.Systems)
        {
            foreach (var body in sys.Bodies)
            {
                if (body.Id == _meta.ActiveLocationId)
                {
                    return (sys.Name, body.Name);
                }
            }
        }

        return (string.Empty, _meta.DefaultPlanetType);
    }

    private void SendStarMap(PlayerSession session)
    {
        var systems = _galaxy.Systems.Select(sys => new NetStarSystem
        {
            Id = sys.Id,
            Name = sys.Name,
            MapX = sys.MapX,
            MapY = sys.MapY,
            Bodies = sys.Bodies.Select(b => new NetBody
            {
                Id = b.Id,
                Name = b.Name,
                Kind = b.Kind.ToString(),
                PlanetType = b.PlanetType,
                Status = b.Status.ToString(),
            }).ToArray(),
        }).ToArray();

        Send(session, new StarMapData { Systems = systems, ActiveLocationId = _meta.ActiveLocationId });
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
            OxygenEnabled = r.OxygenEnabled,
            AdminCheatsActive = r.CheatsAllowed,
        });
    }

    private void SendInventory(PlayerSession session)
    {
        Send(session, new InventoryUpdate
        {
            Personal = DumpInventory(session.State.Inventory),
            Cargo = session.State.AboardShip ? DumpInventory(_ship.Cargo) : Array.Empty<NetItemStack>(),
            UnlockedBlueprints = session.State.UnlockedBlueprints.ToArray(),
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

    private void SendTo(int connectionId, object message)
        => _transport.Send(connectionId, NetCodec.Encode(message), DeliveryMode.ReliableOrdered);

    private void Broadcast(object message)
        => _transport.Broadcast(NetCodec.Encode(message), DeliveryMode.ReliableOrdered);
}

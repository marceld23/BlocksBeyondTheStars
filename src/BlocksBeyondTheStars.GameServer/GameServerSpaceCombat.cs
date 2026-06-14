using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Kind of combat entity in a space instance or on a planet (`anf_space_flight.md` §8, §10, §12).</summary>
public enum CombatEntityKind
{
    Asteroid,
    Drone,
    Ufo,
    Cruiser,
    SpaceStation,
    Creature,
    AlienMonster,
    ScanDrone,   // story P4: the black flying Guardian scan-drone (hovering planet enemy)
    ResourceDrop,
}

/// <summary>A server-authoritative combat entity (space object or planet enemy).</summary>
public sealed class CombatEntity
{
    public string Id { get; set; } = string.Empty;
    public CombatEntityKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Hostile { get; set; }

    /// <summary>Visual scale multiplier for the client's space model (stations: by size tier).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>For planet fauna: the procedural species id this entity is an instance of.</summary>
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Seconds a (territorial) creature stays provoked after being attacked (hunts + bites back).</summary>
    public double ProvokeTimer { get; set; }

    /// <summary>Seconds an aggressor has been actively chasing a player (drives the give-up leash).</summary>
    public double ChaseTimer { get; set; }

    /// <summary>Seconds an aggressor that gave up will ignore the player (wanders off, won't chase or attack).</summary>
    public double GiveUpTimer { get; set; }

    /// <summary>For asteroids: size tier (2 = large, 1 = medium, 0 = small). Large ones split when destroyed.</summary>
    public int AsteroidTier { get; set; }

    /// <summary>Per-individual COSMETIC size multiplier (a fauna instance's own size within its species, so a
    /// population reads as a mix of small + large animals). 1 = the species' normal size. Multiplied into the
    /// rendered creature size on the wire; does NOT affect health/damage/loot.</summary>
    public float SizeScale { get; set; } = 1f;

    public float Hull { get; set; }
    public float HullMax { get; set; }
    public Vector3f Position { get; set; }

    /// <summary>Damage this hostile deals to the ship/player per second while engaged.</summary>
    public float DamagePerSecond { get; set; }

    /// <summary>Seconds this creature is held in stasis (item 36): it can't move or attack while &gt; 0, so it
    /// can be scanned safely. Decays each tick; networked as <c>NetCreature.Frozen</c> for the blue tint.</summary>
    public double FrozenTimer { get; set; }

    /// <summary>What this entity drops when destroyed.</summary>
    public List<ItemAmount> Loot { get; set; } = new();

    // --- Hostile-NPC movement state (space drones/UFOs/cruisers patrol + chase; server-only) ---
    public bool PatrolInitialized { get; set; }
    public Vector3f PatrolCenter { get; set; }
    public double PatrolPhase { get; set; }

    /// <summary>True once this hostile has noticed the ship (entered aggro range) and the "spotted" warning has
    /// been raised. Cleared again when it loses the ship, so re-engaging warns afresh. Server-only.</summary>
    public bool Spotted { get; set; }
}

/// <summary>A loaded local space region (orbit / asteroid field) around a location.</summary>
public sealed class SpaceInstance
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "orbit";
    public List<CombatEntity> Entities { get; set; } = new();
    public HashSet<string> Players { get; set; } = new();

    /// <summary>The (shared) ship's authoritative position in this instance, and last tick's, for collision/speed.</summary>
    public Vector3f ShipPosition { get; set; }
    public Vector3f ShipLastPosition { get; set; }

    /// <summary>Each present player's pose (ship or floating EVA suit) so everyone in the instance can be drawn
    /// for the others. Visibility only — the shared <see cref="ShipPosition"/> still drives collision.</summary>
    public Dictionary<string, SpacePlayerPose> PlayerPoses { get; } = new();

    /// <summary>Seconds until the ship can take asteroid-collision damage again — a brief grace after a bump so a
    /// ram dents the shield/hull instead of stacking damage every tick and instantly destroying the ship (B56).</summary>
    public double CollisionCooldown { get; set; }

    /// <summary>Throttle for streaming hostile-movement updates (drones/UFOs patrol + chase now).</summary>
    public double HostileSyncTimer { get; set; }

    /// <summary>Uptime after which another "hostile spotted you" warning may be raised in this instance — so a
    /// pack arriving together raises one warning, not one per ship.</summary>
    public double SpottedReadyAt { get; set; }

    /// <summary>Counts up while the asteroid field is below its target so mined-out fields slowly replenish.</summary>
    public double AsteroidRespawnTimer { get; set; }

    /// <summary>Spreads successive respawned asteroids so they don't stack on one spot.</summary>
    public int AsteroidSpawnRotor { get; set; }

    /// <summary>Voxel structures floating in this instance (item 20). S1: each present player's own ship,
    /// keyed by player id, seeded from its ship-editor design. Later stages add stations + voxel asteroids.</summary>
    public Dictionary<string, SpaceStructure> Structures { get; } = new();
}

/// <summary>A player's pose in a space instance — where their ship (or EVA suit) is + which way it faces.</summary>
public readonly record struct SpacePlayerPose(Vector3f Pos, float Yaw, bool Eva);

/// <summary>
/// Free space flight and ship combat (technical requirements / `anf_space_flight.md` §6-11).
/// A small, fully server-authoritative PvE slice (see `docs/SPACE_COMBAT_CONCEPT.md`): local
/// space instances, shield/hull, rule-gated ship weapons, simple NPC drones and destructible
/// asteroids, and ship recovery (no permanent loss) when the hull is depleted. Also hosts the
/// ship-module build flow, since ship weapons are built modules.
/// </summary>
public sealed partial class GameServer
{
    private const float BaseHull = 100f;

    // Every ship carries a small baseline shield + slow regen even before fitting shield modules, so early-game
    // space combat isn't lethal (the ship used to take damage far too fast with 0 baseline shield). Flying clear
    // of the fight lets this baseline shield recharge. Shield modules add on top of this.
    private const float BaselineShipShield = 30f;
    private const float BaselineShipShieldRegen = 2f;

    private readonly Dictionary<string, SpaceInstance> _spaceInstances = new();
    private readonly Dictionary<string, string> _playerInstance = new(); // playerId -> instanceId
    private int _nextEntityId = 1;

    // Derived (from built modules); recomputed on ship load and on building a module.
    private const float BaseRadarRange = 130f;
    private float _shipHullMax = BaseHull;
    private float _shipShieldMax;
    private float _shipShieldRegen;
    private float _shipRadarRange = BaseRadarRange;

    /// <summary>The ship's current space-radar range in world units (base + radar-module bonus).</summary>
    public float ShipRadarRange => _shipRadarRange;

    // weapon_class: 0 = mining tool (breaks asteroids, can't hit hostiles), 1 = combat weapon (hits
    // hostiles; breaks asteroids only where AsteroidDestruction allows weapons), 2 = dual laser (does both —
    // the starter ship laser, so one weapon mines AND fights).
    private readonly record struct WeaponSpec(float Damage, float Range, double Cooldown, bool IsCombat, bool CanMine);

    /// <summary>True while the player is flying in a space instance.</summary>
    public bool InSpace(string playerId) => _playerInstance.ContainsKey(playerId);

    /// <summary>The combat entities in the player's current space instance (empty if not in space).</summary>
    public IReadOnlyList<CombatEntity> SpaceEntitiesFor(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var inst)
            ? inst.Entities
            : Array.Empty<CombatEntity>();

    // ---------------- Ship combat stats ----------------

    /// <summary>Recomputes hull/shield maxima from built modules and clamps current values.</summary>
    private void RecomputeShipCombatStats()
    {
        // Base stats come from the active ship's design (data/ships.json); modules add on top.
        var design = _content.GetShip(_ship.ShipType);
        float hull = design?.BaseHull ?? BaseHull;
        float shield = (design?.BaseShield ?? 0f) + BaselineShipShield;
        float regen = BaselineShipShieldRegen;
        float radar = BaseRadarRange;
        foreach (var key in _ship.Modules)
        {
            if (_content.GetShipModule(key) is not { } m)
            {
                continue;
            }

            hull += (float)m.Stats.GetValueOrDefault("hull", 0);
            shield += (float)m.Stats.GetValueOrDefault("shield", 0);
            regen += (float)m.Stats.GetValueOrDefault("shield_regen", 0);
            radar += (float)m.Stats.GetValueOrDefault("radar_bonus", 0);
        }

        _shipHullMax = hull;
        _shipShieldMax = shield;
        _shipShieldRegen = regen;
        _shipRadarRange = radar;

        // A freshly created ship starts at full hull; clamp persisted values into range.
        if (_ship.Hull <= 0f || _ship.Hull > _shipHullMax)
        {
            _ship.Hull = _shipHullMax;
        }

        _ship.Shield = System.Math.Min(_ship.Shield, _shipShieldMax);
    }

    private void SendShipCombatStatus(PlayerSession session)
        => Send(session, new ShipCombatStatus
        {
            Hull = _ship.Hull,
            HullMax = _shipHullMax,
            Shield = _ship.Shield,
            ShieldMax = _shipShieldMax,
            RadarRange = _shipRadarRange,
            Modules = _ship.Modules.ToArray(),
        });

    // ---------------- Build ship modules ----------------

    private void HandleBuildModule(PlayerSession session, BuildShipModuleIntent intent)
    {
        var p = session.State;
        var module = _content.GetShipModule(intent.ModuleKey);
        if (module is null)
        {
            Reject(session, "build_module", "Unknown ship module.");
            return;
        }

        if (_ship.HasModule(module.Key))
        {
            Reject(session, "build_module", "That module is already built.");
            return;
        }

        if (!p.AboardShip)
        {
            Reject(session, "build_module", "You must be aboard your ship to build modules.");
            return;
        }

        if (!_ship.HasModule("workshop"))
        {
            Reject(session, "build_module", "A workshop module is required to build ship modules.");
            return;
        }

        if (!string.IsNullOrEmpty(module.RequiredBlueprint) &&
            !p.UnlockedBlueprints.Contains(module.RequiredBlueprint!))
        {
            Reject(session, "build_module", "Blueprint not unlocked.");
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var pool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (!pool.Has(module.BuildCost))
            {
                Reject(session, "build_module", "Missing materials.");
                return;
            }

            pool.Remove(module.BuildCost);
        }

        _ship.Modules.Add(module.Key);
        ResizeCargo(_ship);
        RecomputeShipCombatStats();

        Send(session, new ServerMessage { Text = $"Ship module built: {module.Key}" });
        SendInventory(session);
        SendShipCombatStatus(session);
        SendPlayerState(session); // AiCoreTier may have changed (gates the client autopilot)
        ShipAiOnModuleBuilt(session, module.Key); // VEGA welcomes her new core
    }

    // ---------------- Enter / leave space ----------------

    /// <summary>Launches the player into a space instance around the ship's location.</summary>
    public void EnterSpace(string playerId, bool skipLaunch = false, bool hyperjump = false)
    {
        var session = FindSessionByPlayerId(playerId);

        if (!Rules.FreeSpaceFlight)
        {
            RejectSpace(session, "Free space flight is disabled on this server.");
            return;
        }

        if (session is not null && !session.State.AboardShip)
        {
            RejectSpace(session, "Board your ship before launching into space.");
            return;
        }

        if (_playerInstance.ContainsKey(playerId))
        {
            return; // already in space
        }

        string locationId = string.IsNullOrEmpty(_ship.CurrentLocationId) ? _meta.ActiveLocationId : _ship.CurrentLocationId;
        string instanceId = "space:" + locationId;
        if (!_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            instance = CreateSpaceInstance(instanceId);
            _spaceInstances[instanceId] = instance;
        }

        // Tell the players still on the body that this ship is launching — they see it rise off its pad
        // (item 38) — and remove the parked ship OBJECT from the pad: the ship is flying now, it can't
        // stand on the ground at the same time (ship-as-object).
        if (session is not null && !skipLaunch)
        {
            var p = session.State.Position;
            BroadcastShipTransit(session, locationId, p.X, p.Y - 1f, p.Z, landing: false);
        }

        if (session is not null && SetActiveWorld(session.CurrentLocationId))
        {
            RemoveLandedShip(session);
        }

        instance.Players.Add(playerId);
        _playerInstance[playerId] = instanceId;

        // Launch with the shields up (baseline + modules). The clamp in RecomputeShipCombatStats only ever lowers
        // the stored shield, so a fresh ship would otherwise start a flight at 0 shield and have to charge it.
        RecomputeShipCombatStats();
        _ship.Shield = _shipShieldMax;

        if (session is not null)
        {
            ShipAiOnEnterSpace(session); // VEGA onboarding: first launch into space
            SendSpaceState(session, instance, skipLaunch, hyperjump);
            SendShipCombatStatus(session);
            SendStarMap(session); // the space view needs the system's bodies to render + land on them

            // item 20 S1: carry the player's ship as a voxel structure in the instance + send it so the flight
            // view renders the real designed ship (1:1) instead of the hand-built cube model. Rebuilt fresh on
            // every entry: ALL ship edits persist as per-cell deltas now (EVA hull work, interior furnishing),
            // so the rebuild is lossless and picks up edits made while landed.
            var structureId = "ship:" + playerId;
            var structure = BuildShipStructure(playerId);
            instance.Structures[structure.Id] = structure; // keyed by structure id ("ship:<playerId>")

            SendShipDesign(session, structure);

            // item 20 S3: also send every voxel asteroid body so the flight view renders + can mine them.
            foreach (var st in instance.Structures.Values)
            {
                if (st.Kind == "asteroid")
                {
                    SendShipDesign(session, st);
                }
            }

            // Other pilots' ships show their REAL voxel designs too: hand the newcomer every other
            // ship already out here, and hand the newcomer's ship to everyone else in the instance.
            foreach (var st in instance.Structures.Values)
            {
                if (st.Kind == "ship" && st.Id != structureId)
                {
                    SendShipDesign(session, st, "ship_remote");
                }
            }

            foreach (var pid in instance.Players)
            {
                if (pid != playerId && FindSessionByPlayerId(pid) is { } other)
                {
                    SendShipDesign(other, structure, "ship_remote");
                }
            }
        }
    }

    /// <summary>Starts/ends an EVA spacewalk. Only honoured while the player is actually out in a space
    /// instance and free flight is allowed; on EVA the suit life support is off so oxygen drains.</summary>
    private void HandleSetEva(PlayerSession session, SetEvaIntent intent)
    {
        var p = session.State;
        if (intent.Active)
        {
            if (Rules.FreeSpaceFlight && InSpace(p.PlayerId))
            {
                p.InEva = true;
            }
        }
        else
        {
            p.InEva = false;
        }
    }

    /// <summary>Leaves the current space instance and returns to the surface/base.</summary>
    public void LeaveSpace(string playerId)
    {
        if (!_playerInstance.TryGetValue(playerId, out var instanceId))
        {
            return;
        }

        _playerInstance.Remove(playerId);
        if (FindSessionByPlayerId(playerId) is { } leaveSession)
        {
            leaveSession.State.InEva = false; // back on the surface — the spacewalk is over
        }
        if (_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            instance.Players.Remove(playerId);
            if (instance.Players.Count == 0)
            {
                _spaceInstances.Remove(instanceId);
            }
        }

        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            Send(session, new SpaceClosed { Reason = "Returned from space.", ShipDisabled = false });
        }
    }

    private SpaceInstance CreateSpaceInstance(string instanceId)
    {
        var instance = new SpaceInstance { Id = instanceId, Kind = "orbit" };

        // Asteroids are always present as scenery + mining targets; breaking them is gated at fire
        // time. They start large and split into smaller chunks when destroyed (§8.1).
        int asteroids = 3;
        for (int i = 0; i < asteroids; i++)
        {
            // B10: scatter them around the body (a golden-angle ring at varied radius/height) instead of a
            // tight line — but inside weapon range (asteroid_breaker reaches ~40) so they stay shootable.
            float ang = i * 2.39996f;
            float rad = 18f + i * 8f; // 18 / 26 / 34
            // item 20 S3: each asteroid is a voxel ore body (entity + structure) you can shoot AND EVA-mine.
            SpawnAsteroid(instance,
                new Vector3f(rad * (float)System.Math.Cos(ang), (i - 1) * 9f, rad * (float)System.Math.Sin(ang)),
                broadcast: false);
        }

        AddStationContacts(instance);
        AddPersistedStations(instance); // item 20 S4: re-create player-built stations floating in this instance

        // Hostile NPC drones only when space combat is enabled and NPC enemies are switched on — and never
        // once the Guardian core is destroyed (P6 pacification: the galaxy is at peace).
        bool combatEnabled = Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both;
        if (combatEnabled && !_storyState.GuardianDefeated)
        {
            // The finale system runs its own scripted ELITE gauntlet (P6 Stage 1) instead of the ambient
            // hostiles — strip the "space:" prefix to recover the anchor body id for the system check.
            string bodyId = instanceId.StartsWith("space:") ? instanceId.Substring("space:".Length) : instanceId;
            if (IsGuardianSystemLocation(bodyId))
            {
                SpawnGuardianGauntlet(instance);
            }
            else
            {
                // Spawn hostiles FAR from the launch point (well beyond ShipEngageRange) so launching/docking is
                // safe and combat is opt-in — you choose to fly out to them. They used to spawn ~25u away and
                // hammered the ship the instant it launched (continuous damage → destroyed → respawn at base).
                int drones = ActivityCount(Rules.SpaceNpcEnemies);
                for (int i = 0; i < drones; i++)
                {
                    instance.Entities.Add(new CombatEntity
                    {
                        Id = NextEntityId(),
                        Kind = CombatEntityKind.Drone,
                        Hostile = true,
                        Hull = 40f,
                        HullMax = 40f,
                        Position = new Vector3f(150f + i * 16f, 10f, -130f - i * 20f),
                        DamagePerSecond = 5f,
                        Loot = { new ItemAmount("data_fragment", 1) },
                    });
                }

                if (Rules.AlienUfos != AlienActivity.Off)
                {
                    instance.Entities.Add(new CombatEntity
                    {
                        Id = NextEntityId(),
                        Kind = CombatEntityKind.Ufo,
                        Hostile = true,
                        // Softened for a forgiving PvE feel: was 70 hull / 8 dps, which killed an unshielded ship in
                        // ~12s and took a long time to down. Now closer to a drone so UFOs read as a light threat.
                        Hull = 40f,
                        HullMax = 40f,
                        Position = new Vector3f(-170f, 14f, 150f),
                        DamagePerSecond = 4f,
                        Loot = { new ItemAmount("data_fragment", 3) },
                    });
                }
            }
        }

        return instance;
    }

    /// <summary>P6 Stage 1 — the Guardian system's elite gauntlet: the hardest space wave in the game, ringed
    /// around the dormant core. A heavy cruiser flanked by elite UFOs and a swarm of reinforced drones, all
    /// well beyond engage range so the approach stays opt-in. Reuses the normal ship-combat resolution +
    /// hostile AI; each kill still feeds the story like any Guardian machine.</summary>
    private void SpawnGuardianGauntlet(SpaceInstance instance)
    {
        // A reinforced drone swarm on a golden-angle ring.
        const int drones = 8;
        for (int i = 0; i < drones; i++)
        {
            float ang = i * 2.39996f;
            float rad = 150f + (i % 3) * 22f;
            instance.Entities.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.Drone,
                Hostile = true,
                Hull = 70f,
                HullMax = 70f,
                Position = new Vector3f(rad * (float)System.Math.Cos(ang), (i % 5 - 2) * 12f, rad * (float)System.Math.Sin(ang)),
                DamagePerSecond = 7f,
                Loot = { new ItemAmount("data_fragment", 2) },
            });
        }

        // Elite UFO escorts.
        for (int i = 0; i < 3; i++)
        {
            float ang = i * 2.094f + 0.7f;
            instance.Entities.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.Ufo,
                Hostile = true,
                Hull = 95f,
                HullMax = 95f,
                Position = new Vector3f(210f * (float)System.Math.Cos(ang), 18f, 210f * (float)System.Math.Sin(ang)),
                DamagePerSecond = 8f,
                Loot = { new ItemAmount("data_fragment", 4) },
            });
        }

        // The gauntlet's heavy cruiser — the toughest single ship the player will face before the core.
        instance.Entities.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Cruiser,
            Hostile = true,
            Hull = 260f,
            HullMax = 260f,
            Position = new Vector3f(0f, 26f, -240f),
            DamagePerSecond = 10f,
            Loot = { new ItemAmount("data_fragment", 8) },
        });
    }

    // ---------------- Weapons ----------------

    /// <summary>Fires a built ship weapon at a target entity. Server-authoritative: validates rules, range and resolves the hit.</summary>
    public void FireWeapon(string playerId, string weaponKey, string targetId)
    {
        var session = FindSessionByPlayerId(playerId);

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            RejectSpace(session, "You are not flying in space.");
            return;
        }

        if (session is not null)
        {
            SetCurrent(session); // pin the ship cursor to the firing player so _ship (tractor check / loot) is theirs
        }

        if (!TryGetWeapon(weaponKey, out var weapon))
        {
            RejectSpace(session, "That weapon is not built on your ship.");
            return;
        }

        var target = instance.Entities.FirstOrDefault(e => e.Id == targetId);
        if (target is null)
        {
            RejectSpace(session, "No such target.");
            return;
        }

        if (target.Position.DistanceSquared(instance.ShipPosition) > weapon.Range * weapon.Range)
        {
            RejectSpace(session, "Target is out of weapon range.");
            return;
        }

        if (!WeaponAllowedAgainst(weapon, target, out var reason))
        {
            RejectSpace(session, reason);
            return;
        }

        target.Hull -= weapon.Damage;

        // item 20 S3: a voxel ore asteroid carves down to match its hull as you shoot it (visible depletion).
        if (target.Kind == CombatEntityKind.Asteroid && instance.Structures.ContainsKey(target.Id))
        {
            CarveAsteroidToHull(instance, target);
        }

        if (target.Hull > 0f)
        {
            BroadcastSpaceState(instance);
            return;
        }

        // Destroyed. A large/medium asteroid splits into smaller chunks instead of dropping loot;
        // only the smallest asteroids (and other entities) yield resources.
        instance.Entities.Remove(target);
        if (target.Hostile)
        {
            RecordStoryMachineKill(); // space machine (drone/UFO) destroyed → advances the story (P4)
            if (session is not null)
            {
                TryDropPlayerMemory(session); // a chance to release a personal memory (P4)
            }
        }

        if (target.Kind == CombatEntityKind.Asteroid && instance.Structures.ContainsKey(target.Id))
        {
            RemoveAsteroidStructure(instance, target.Id); // S3: drop the voxel body too (loot handled below)
            // fall through to the loot branch (voxel asteroids are tier 0 → they yield ore)
        }

        if (target.Kind == CombatEntityKind.Asteroid && target.AsteroidTier > 0)
        {
            SplitAsteroid(instance, target);
        }
        else if (target.Loot.Count > 0 && _ship.HasModule(TractorModule))
        {
            // With a tractor beam fitted, loot floats as a salvage drop to be collected, instead of
            // teleporting into the inventory.
            instance.Entities.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.ResourceDrop,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = target.Position,
                Loot = new List<ItemAmount>(target.Loot),
            });
        }
        else if (session is not null)
        {
            var pool = new MaterialPool(_content, session.State, _ship);
            foreach (var drop in target.Loot)
            {
                pool.Add(drop.Item, drop.Count);
            }

            SendInventory(session);
        }

        BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = target.Id });
        BroadcastSpaceState(instance);
    }

    private const int LargeAsteroidTier = 2;
    private const int AsteroidSplitCount = 2;

    /// <summary>Hull of an asteroid by size tier (large is tougher; small breaks fast into resources).</summary>
    private static float AsteroidHull(int tier) => tier switch
    {
        2 => 40f,
        1 => 25f,
        _ => 15f,
    };

    private CombatEntity MakeAsteroid(int tier, Vector3f position) => new()
    {
        Id = NextEntityId(),
        Kind = CombatEntityKind.Asteroid,
        Hostile = false,
        Hull = AsteroidHull(tier),
        HullMax = AsteroidHull(tier),
        AsteroidTier = tier,
        Position = position,
        // Only the smallest chunks carry mineral drops; larger ones split first.
        Loot = tier == 0
            ? new List<ItemAmount> { new("iron_ore", 5), new("titanium_ore", 2) }
            : new List<ItemAmount>(),
    };

    /// <summary>Replaces a destroyed large/medium asteroid with a couple of smaller-tier chunks nearby.</summary>
    private void SplitAsteroid(SpaceInstance instance, CombatEntity parent)
    {
        int childTier = parent.AsteroidTier - 1;
        for (int i = 0; i < AsteroidSplitCount; i++)
        {
            float dx = i == 0 ? -2f : 2f;
            float dz = i == 0 ? -2f : 2f;
            var pos = new Vector3f(parent.Position.X + dx, parent.Position.Y, parent.Position.Z + dz);
            instance.Entities.Add(MakeAsteroid(childTier, pos));
        }
    }

    /// <summary>Applies the rule gating from §7.2 / §8.2 / §11 for a weapon firing at a target.</summary>
    private bool WeaponAllowedAgainst(WeaponSpec weapon, CombatEntity target, out string reason)
    {
        reason = string.Empty;

        if (target.Kind == CombatEntityKind.Asteroid)
        {
            // Asteroid mining/breaking is governed by AsteroidDestruction, independent of combat.
            if (Rules.AsteroidDestruction == AsteroidDestructionMode.Off)
            {
                reason = "Breaking asteroids is disabled on this server.";
                return false;
            }

            // Mining tools + dual lasers always break asteroids; a pure combat cannon only where the
            // server allows weapons against rocks.
            if (!weapon.CanMine && Rules.AsteroidDestruction != AsteroidDestructionMode.WeaponsAllowed)
            {
                reason = "Only mining tools may break asteroids on this server.";
                return false;
            }

            return true;
        }

        if (target.Kind == CombatEntityKind.SpaceStation)
        {
            reason = "Dock with the station instead of firing on it.";
            return false;
        }

        // Hostile NPC target: needs an actual combat weapon and combat-enabling rules.
        if (!weapon.IsCombat)
        {
            reason = "A mining tool cannot damage that target.";
            return false;
        }

        if (Rules.SpaceCombat is not (SpaceCombatMode.PvE or SpaceCombatMode.Both))
        {
            reason = "Space combat is disabled on this server.";
            return false;
        }

        if (Rules.ShipWeapons is ShipWeaponMode.Off or ShipWeaponMode.MiningOnly)
        {
            reason = "Ship weapons are not permitted on this server.";
            return false;
        }

        return true;
    }

    private bool TryGetWeapon(string moduleKey, out WeaponSpec spec)
    {
        spec = default;
        if (!_ship.HasModule(moduleKey) || _content.GetShipModule(moduleKey) is not { } def)
        {
            return false;
        }

        if (!def.Stats.ContainsKey("weapon_damage"))
        {
            return false;
        }

        int weaponClass = (int)def.Stats.GetValueOrDefault("weapon_class", 1);
        spec = new WeaponSpec(
            Damage: (float)def.Stats.GetValueOrDefault("weapon_damage", 10),
            Range: (float)def.Stats.GetValueOrDefault("weapon_range", 50),
            Cooldown: def.Stats.GetValueOrDefault("weapon_cooldown", 1.0),
            IsCombat: weaponClass >= 1,                  // combat weapons + dual lasers can hit hostiles
            CanMine: weaponClass == 0 || weaponClass == 2); // mining tools + dual lasers can break asteroids
        return true;
    }

    // ---------------- Ship flight (position in the instance) ----------------

    private const float ShipCollisionRadius = 3f;
    private const float ShipCollisionMinSpeed = 3f;
    private const float ShipCollisionDamageFactor = 0.8f;
    private const float ShipCollisionMaxDamage = 18f;       // a ram dents the shield/hull, never one-shots (B56)
    private const double ShipCollisionCooldown = 0.8;       // …and can't re-damage for this long, so it isn't per-tick
    // Hostiles only fire on the ship once they're within engagement range — so a distant drone can't plink
    // you forever (which read as the ship being shaken + flashing red with no visible attacker), and flying
    // clear of the fight actually stops the damage and lets the shield recharge.
    private const float ShipEngageRange = 70f;

    private const string TractorModule = "tractor_beam";
    // Passive auto-collect radius. Was 8 — too tight: salvage spawns at the destroyed rock's centre, so after a
    // mid-range kill you often couldn't get close enough to vacuum it (most noticeable on your very first kill,
    // before you've learned to nose right into the wreck). Widened so flying near the wreck reliably collects it.
    private const float TractorRange = 16f;

    /// <summary>Tractor beam: pulls salvage drops within <paramref name="range"/> into the ship's cargo hold
    /// (until full). The passive tick uses a short range; a manual pull (quick-bar) sweeps a wider one.</summary>
    private void CollectSalvage(SpaceInstance instance, float range)
    {
        if (!_ship.HasModule(TractorModule))
        {
            return;
        }

        bool changed = false;
        foreach (var drop in instance.Entities.Where(e => e.Kind == CombatEntityKind.ResourceDrop).ToList())
        {
            if (drop.Position.DistanceSquared(instance.ShipPosition) > range * range)
            {
                continue;
            }

            var leftover = new List<ItemAmount>();
            foreach (var item in drop.Loot)
            {
                int max = _content.GetItem(item.Item)?.MaxStack ?? 99;
                int notStowed = _ship.Cargo.Add(item.Item, item.Count, max); // cargo full → leave the rest floating
                if (notStowed < item.Count)
                {
                    changed = true;
                }

                if (notStowed > 0)
                {
                    leftover.Add(new ItemAmount(item.Item, notStowed));
                }
            }

            drop.Loot = leftover;
            if (drop.Loot.Count == 0)
            {
                instance.Entities.Remove(drop);
            }
        }

        if (changed)
        {
            BroadcastSpaceState(instance);
            foreach (var playerId in instance.Players)
            {
                if (FindSessionByPlayerId(playerId) is { } s)
                {
                    SendInventory(s); // cargo is part of the inventory update when aboard
                }
            }
        }
    }

    private const float TractorPullRange = 30f; // a manual quick-bar tractor sweep reaches further than the passive pull

    /// <summary>Manual tractor pull (quick-bar): sweeps salvage within a wider range into the cargo hold.</summary>
    public void TractorPull(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            return;
        }

        if (!_ship.HasModule(TractorModule))
        {
            RejectSpace(session, "No tractor beam fitted on this ship.");
            return;
        }

        CollectSalvage(instance, TractorPullRange);
    }

    private void HandleTractorPull(PlayerSession session) => TractorPull(session.State.PlayerId);

    /// <summary>Sets the player's ship position in its space instance (trusted + finite-clamped, like on-foot move).</summary>
    public void ShipMove(string playerId, float x, float y, float z, float yaw = 0f)
    {
        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            return;
        }

        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw))
        {
            return; // ignore garbage
        }

        var pos = new Vector3f(x, y, z);
        instance.ShipPosition = pos; // shared, for collision (the acting player's ship)

        // Per-player pose for visibility — so the others in this instance can render this ship / EVA suit.
        bool eva = FindSessionByPlayerId(playerId)?.State.InEva ?? false;
        instance.PlayerPoses[playerId] = new SpacePlayerPose(pos, yaw, eva);
    }

    private void HandleShipMove(PlayerSession session, ShipMoveIntent move)
        => ShipMove(session.State.PlayerId, move.X, move.Y, move.Z, move.Yaw);

    // ---------------- Space simulation tick ----------------

    private void TickSpace(double dt)
    {
        if (_spaceInstances.Count == 0)
        {
            return;
        }

        foreach (var instance in _spaceInstances.Values.ToList())
        {
            if (instance.Players.Count == 0)
            {
                continue;
            }

            // Tractor beam: pull nearby salvage drops into the cargo hold (before collision, so the
            // collision bounce doesn't move the ship away from the drop first).
            CollectSalvage(instance, TractorRange);

            // Collision: flying into an asteroid damages the ship (scaled by impact speed) and
            // stops it. Physical — independent of the combat rules.
            float speed = (float)(System.Math.Sqrt(instance.ShipPosition.DistanceSquared(instance.ShipLastPosition))
                                  / System.Math.Max(dt, 0.0001));
            instance.CollisionCooldown = System.Math.Max(0.0, instance.CollisionCooldown - dt);
            bool hitAsteroid = instance.Entities.Any(e => e.Kind == CombatEntityKind.Asteroid
                && e.Position.DistanceSquared(instance.ShipPosition) <= ShipCollisionRadius * ShipCollisionRadius);
            if (hitAsteroid && speed > ShipCollisionMinSpeed)
            {
                instance.ShipPosition = instance.ShipLastPosition; // always bounce back / stop at the impact
                if (instance.CollisionCooldown <= 0.0)
                {
                    // A ram dents the shield first, then the hull — never an instant kill (B56). Brief grace
                    // afterwards so holding thrust into the rock doesn't stack damage every tick.
                    ApplyShipDamage(System.Math.Min(ShipCollisionMaxDamage, speed * ShipCollisionDamageFactor));
                    instance.CollisionCooldown = ShipCollisionCooldown;
                    foreach (var playerId in instance.Players)
                    {
                        if (FindSessionByPlayerId(playerId) is { } s)
                        {
                            SendShipCombatStatus(s);
                        }
                    }

                    if (_ship.Hull <= 0f)
                    {
                        DisableShip(instance);
                        continue;
                    }
                }
            }

            instance.ShipLastPosition = instance.ShipPosition;

            // Hostile movement: drones/UFOs/cruisers patrol around their post and CHASE the ship when it
            // comes in range (they used to hang motionless at their spawn points forever).
            bool hostilesMoved = MoveSpaceHostiles(instance, dt);
            AnnounceHostileSpotting(instance); // warn the pilot the moment a hostile starts hunting the ship
            instance.HostileSyncTimer += dt;
            if (hostilesMoved && instance.HostileSyncTimer >= 0.15)
            {
                instance.HostileSyncTimer = 0;
                BroadcastSpaceState(instance);
            }

            float incoming = instance.Entities
                .Where(e => e.Hostile && e.Position.DistanceSquared(instance.ShipPosition) <= ShipEngageRange * ShipEngageRange)
                .Sum(e => e.DamagePerSecond);
            if (incoming > 0f)
            {
                bool evaded = ApplyShipDamage((float)(incoming * dt));
                if (_ship.Hull <= 0f)
                {
                    DisableShip(instance);
                    continue;
                }

                foreach (var playerId in instance.Players)
                {
                    if (FindSessionByPlayerId(playerId) is { } s)
                    {
                        SendShipCombatStatus(s);
                        ShipAiThreatCallout(s); // Mk2+: VEGA calls out hostile contact (rate-limited)
                        if (evaded)
                        {
                            ShipAiEvadeCallout(s); // Mk3: the dodge that just saved the hull
                        }
                    }
                }
            }
            else if (_ship.Shield < _shipShieldMax)
            {
                // Out of combat: shield recharges.
                _ship.Shield = System.Math.Min(_shipShieldMax, _ship.Shield + (float)(_shipShieldRegen * dt));
            }

            // A mined-out asteroid field slowly replenishes over the session (positions stay deterministic).
            RespawnAsteroids(instance, dt);
        }
    }

    private const int AsteroidFieldTarget = 3;            // large-equivalent asteroids the field tends toward
    private const double AsteroidRespawnInterval = 120.0; // seconds between replenishing spawns (B9: slower respawn)

    /// <summary>Slowly refills a mined-out asteroid field back toward its target so it isn't barren for the
    /// rest of the session (a fresh field is still generated on each space entry).</summary>
    private void RespawnAsteroids(SpaceInstance instance, double dt)
    {
        int count = instance.Entities.Count(e => e.Kind == CombatEntityKind.Asteroid);
        if (count >= AsteroidFieldTarget)
        {
            instance.AsteroidRespawnTimer = 0;
            return;
        }

        instance.AsteroidRespawnTimer += dt;
        if (instance.AsteroidRespawnTimer < AsteroidRespawnInterval)
        {
            return;
        }

        instance.AsteroidRespawnTimer = 0;
        int r = instance.AsteroidSpawnRotor++;
        // Spread successive rocks around the field at varied angle/height (B10) — but within weapon range so
        // a refilled rock is still reachable.
        float rang = r * 2.39996f;
        float rrad = 22f + (r % 3) * 6f; // 22 / 28 / 34
        var pos = new Vector3f(rrad * (float)System.Math.Cos(rang), ((r % 5) - 2) * 8f, rrad * (float)System.Math.Sin(rang));
        SpawnAsteroid(instance, pos, broadcast: true); // item 20 S3: voxel ore body (sends its mesh + state)
    }

    private const double SpottedCalloutCooldown = 15.0; // s between "hostile spotted you" warnings per instance

    /// <summary>Raises a one-shot "a hostile has spotted you" warning to every pilot in the instance the moment a
    /// hostile NPC first enters its aggro range and begins hunting the ship — for ALL AI-core tiers (the older
    /// <see cref="ShipAiThreatCallout"/> only fires once damage lands, and only on a Mk2+ core). A short
    /// per-instance cooldown keeps a pack that arrives together from raising one warning per ship.</summary>
    private void AnnounceHostileSpotting(SpaceInstance instance)
    {
        bool newlySpotted = false;
        foreach (var e in instance.Entities)
        {
            if (!e.Hostile || e.Hull <= 0f)
            {
                continue;
            }

            var (aggro, _, speed) = HostileProfile(e.Kind);
            if (speed <= 0f || aggro <= 0f)
            {
                continue; // not a mobile hunter (e.g. stations / asteroids / drops)
            }

            float distSq = e.Position.DistanceSquared(instance.ShipPosition);
            if (distSq <= aggro * aggro)
            {
                if (!e.Spotted)
                {
                    e.Spotted = true;
                    newlySpotted = true;
                }
            }
            else if (distSq > aggro * aggro * 1.21f)
            {
                e.Spotted = false; // lost the ship (with ~10% hysteresis) — a fresh approach warns again
            }
        }

        if (!newlySpotted || _uptime < instance.SpottedReadyAt)
        {
            return;
        }

        instance.SpottedReadyAt = _uptime + SpottedCalloutCooldown;
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } s)
            {
                SendVegaLine(s, "vega.sys.spotted", 3);
            }
        }
    }

    /// <summary>Per-kind movement profile for hostile space NPCs: how far they notice the ship, how close
    /// they press in, and how fast they fly.</summary>
    private static (float Aggro, float MinDist, float Speed) HostileProfile(CombatEntityKind kind) => kind switch
    {
        CombatEntityKind.Drone => (190f, 16f, 9f),
        CombatEntityKind.Ufo => (240f, 24f, 7f),
        CombatEntityKind.Cruiser => (260f, 36f, 4f),
        _ => (0f, 0f, 0f),
    };

    /// <summary>Moves the instance's hostile NPCs: a slow patrol orbit around their post when the ship is
    /// far, a closing chase (with a sideways weave so they read as flown, not railed) once it enters their
    /// aggro range — stopping at a per-kind stand-off distance where their weapon aura works.</summary>
    private bool MoveSpaceHostiles(SpaceInstance instance, double dt)
    {
        bool moved = false;
        foreach (var e in instance.Entities)
        {
            if (!e.Hostile || e.Hull <= 0f)
            {
                continue;
            }

            var (aggro, minDist, speed) = HostileProfile(e.Kind);
            if (speed <= 0f)
            {
                continue;
            }

            // Remember the spawn as the patrol post (first move initializes it).
            if (!e.PatrolInitialized)
            {
                e.PatrolCenter = e.Position;
                e.PatrolPhase = (uint)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(e.Id) % 628 / 100.0;
                e.PatrolInitialized = true;
            }

            float dx = instance.ShipPosition.X - e.Position.X;
            float dy = instance.ShipPosition.Y - e.Position.Y;
            float dz = instance.ShipPosition.Z - e.Position.Z;
            float distSq = dx * dx + dy * dy + dz * dz;

            float tx, ty, tz;
            float moveSpeed;
            float maxStep = float.MaxValue;
            if (distSq <= aggro * aggro && distSq > minDist * minDist)
            {
                // Chase: head for the ship with a sideways weave (perpendicular sway) so the approach arcs.
                float dist = (float)System.Math.Sqrt(distSq);
                float wob = (float)System.Math.Sin(_uptime * 1.7 + e.PatrolPhase) * 0.35f;
                tx = dx / dist - dz / dist * wob;
                ty = dy / dist;
                tz = dz / dist + dx / dist * wob;
                moveSpeed = speed;
                maxStep = dist - minDist * 0.9f; // never overshoot past the stand-off ring (big-dt safe)
            }
            else if (distSq <= minDist * minDist)
            {
                continue; // at stand-off range — hold and let the weapon aura work
            }
            else
            {
                // Patrol: drift around the post on a slow circle (with a light vertical bob).
                double t = _uptime * 0.15 + e.PatrolPhase;
                float px = e.PatrolCenter.X + (float)System.Math.Cos(t) * 18f;
                float py = e.PatrolCenter.Y + (float)System.Math.Sin(t * 2.0) * 4f;
                float pz = e.PatrolCenter.Z + (float)System.Math.Sin(t) * 18f;
                tx = px - e.Position.X;
                ty = py - e.Position.Y;
                tz = pz - e.Position.Z;
                float len = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);
                if (len < 0.5f)
                {
                    continue; // already on the patrol ring
                }

                tx /= len;
                ty /= len;
                tz /= len;
                moveSpeed = speed * 0.45f;
            }

            float norm = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (norm < 0.001f)
            {
                continue;
            }

            float step = System.Math.Min((float)(moveSpeed * dt), maxStep) / norm;
            e.Position = new Vector3f(e.Position.X + tx * step, e.Position.Y + ty * step, e.Position.Z + tz * step);
            moved = true;
        }

        return moved;
    }

    /// <summary>Damage hits the shield first, then the hull. Returns true when an Mk3 AI core evaded the
    /// whole event (VEGA's evasive manoeuvre — Phase C ability; no damage is applied then).</summary>
    private bool ApplyShipDamage(float amount)
    {
        if (VegaTryEvade())
        {
            return true;
        }

        float toShield = System.Math.Min(_ship.Shield, amount);
        _ship.Shield -= toShield;
        amount -= toShield;
        if (amount > 0f)
        {
            _ship.Hull = System.Math.Max(0f, _ship.Hull - amount);
        }

        return false;
    }

    /// <summary>
    /// The ship was defeated. Per §8.5 there is no permanent loss by default: the ship is
    /// recovered to its base with restored hull, present players respawn at the heal-tank,
    /// and the instance is unloaded.
    /// </summary>
    private void DisableShip(SpaceInstance instance)
    {
        _ship.Hull = _shipHullMax;
        _ship.Shield = _shipShieldMax; // recovered to base with shields restored too (baseline + modules)

        foreach (var playerId in instance.Players.ToList())
        {
            _playerInstance.Remove(playerId);
            if (FindSessionByPlayerId(playerId) is not { } session)
            {
                continue;
            }

            var p = session.State;
            p.Position = p.RespawnPoint;
            p.AboardShip = true;
            p.InEva = false; // the ship's loss ends any spacewalk
            p.Health = 100f;
            p.Oxygen = 100f;

            Send(session, new SpaceClosed { Reason = "Ship disabled — recovered to base.", ShipDisabled = true });
            SendShipCombatStatus(session);
            SendPlayerState(session);
        }

        instance.Players.Clear();
        _spaceInstances.Remove(instance.Id);
    }

    // ---------------- Helpers ----------------

    private static int ActivityCount(AlienActivity a) => a switch
    {
        AlienActivity.Rare => 1,
        AlienActivity.Normal => 2,
        AlienActivity.Frequent => 3,
        AlienActivity.Extreme => 4,
        _ => 0,
    };

    private string NextEntityId() => "e" + _nextEntityId++;

    private static NetCombatEntity ToNet(CombatEntity e) => new()
    {
        Id = e.Id,
        Kind = e.Kind.ToString(),
        Name = e.Name,
        Hostile = e.Hostile,
        Hull = e.Hull,
        HullMax = e.HullMax,
        X = e.Position.X,
        Y = e.Position.Y,
        Z = e.Position.Z,
        Scale = e.Scale,
    };

    private void SendSpaceState(PlayerSession session, SpaceInstance instance, bool skipLaunch = false, bool hyperjump = false)
        => Send(session, new SpaceState
        {
            InstanceId = instance.Id,
            Kind = instance.Kind,
            Entities = instance.Entities.Select(ToNet).ToArray(),
            SkipLaunch = skipLaunch,
            Hyperjump = hyperjump,
            Players = OtherPlayersInSpace(session.State.PlayerId, instance),
        });

    /// <summary>The other players this one currently sees in its space instance (ships + EVA suits).</summary>
    public NetSpacePlayer[] OtherSpacePlayers(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
            ? OtherPlayersInSpace(playerId, inst)
            : System.Array.Empty<NetSpacePlayer>();

    /// <summary>The other players currently sharing this instance (excludes the recipient), as ship/EVA poses
    /// for the flight view to render.</summary>
    private NetSpacePlayer[] OtherPlayersInSpace(string recipientId, SpaceInstance instance)
    {
        List<NetSpacePlayer> others = null;
        foreach (var kv in instance.PlayerPoses)
        {
            if (kv.Key == recipientId || !instance.Players.Contains(kv.Key))
            {
                continue; // skip self + stale poses of players who already left the instance
            }

            var pose = kv.Value;
            var owner = FindSessionByPlayerId(kv.Key);
            (others ??= new List<NetSpacePlayer>()).Add(new NetSpacePlayer
            {
                PlayerId = kv.Key,
                Name = owner?.State.Name ?? string.Empty,
                X = pose.Pos.X,
                Y = pose.Pos.Y,
                Z = pose.Pos.Z,
                Yaw = pose.Yaw,
                Eva = pose.Eva,
                Hull = owner?.HullColor ?? 0xD1D6E0, // item 32 — other players see this ship in its hull colour
            });
        }

        return others is null ? System.Array.Empty<NetSpacePlayer>() : others.ToArray();
    }

    private void BroadcastSpaceState(SpaceInstance instance)
    {
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } session)
            {
                SendSpaceState(session, instance);
            }
        }
    }

    private void BroadcastToInstance(SpaceInstance instance, object message)
    {
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } session)
            {
                Send(session, message);
            }
        }
    }

    private void RejectSpace(PlayerSession? session, string reason)
    {
        if (session is not null)
        {
            Reject(session, "space", reason);
        }
    }

    // ---------------- Intent handlers ----------------

    private void HandleEnterSpace(PlayerSession session)
    {
        // If the player is inside the ship interior, they are parked in space (the interior is only ever
        // entered from a space instance) — so returning to flight must SKIP the planet take-off animation and
        // restore the ship where it was parked, exactly like the helm (B40). Only a launch from a real planet
        // surface plays the take-off. This guards every path that fires EnterSpaceIntent, not just the helm UI.
        if (_inShipInterior.ContainsKey(session.State.PlayerId))
        {
            ExitShipToFlight(session.State.PlayerId);
            return;
        }

        EnterSpace(session.State.PlayerId);
    }

    /// <summary>Test hook: run the EnterSpaceIntent handler (covers the ship-interior skip path, B40).</summary>
    public void HandleEnterSpaceForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleEnterSpace(s);
        }
    }

    private void HandleHyperjumpSystem(PlayerSession session, HyperjumpSystemIntent intent)
        => HyperjumpToSystem(session.State.PlayerId, intent.SystemId);

    /// <summary>Hyperjumps into a (possibly never-visited) star system, arriving in FLIGHT mode in that
    /// system's space rather than landing — the way to reach a system whose bodies you can't yet see on the
    /// travel screen. Needs a jump generator; from there you fly to its worlds and land manually. Also the
    /// test/util entrypoint.</summary>
    public void HyperjumpToSystem(string playerId, string systemId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (!Rules.FreeSpaceFlight)
        {
            RejectSpace(session, "Free space flight is disabled on this server.");
            return;
        }

        Serve(session); // _ship = this player's ship

        var system = _galaxy?.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system is null || system.Bodies.Count == 0)
        {
            RejectSpace(session, "Unknown star system.");
            return;
        }

        var origin = _galaxy?.FindBody(session.CurrentLocationId);
        if (origin is not null && origin.SystemId == system.Id)
        {
            RejectSpace(session, "You are already in that system.");
            return;
        }

        if (_ship is null || !_ship.HasModule("jump_generator"))
        {
            RejectSpace(session, "Your ship has no jump generator — fit one to jump between star systems.");
            return;
        }

        // Arrive in flight anchored on the system's first landable body (the flight instance is keyed there);
        // you fly to its worlds and land manually from there.
        var anchor = system.Bodies.FirstOrDefault(b => !string.IsNullOrEmpty(b.PlanetType)) ?? system.Bodies[0];

        // Launching off a surface? Remove the parked ship from the OLD world before we switch systems.
        if (!InSpace(playerId) && SetActiveWorld(session.CurrentLocationId))
        {
            RemoveLandedShip(session);
        }

        LeaveSpace(playerId); // tear down any current flight instance (no-op on a surface)

        session.CurrentLocationId = anchor.Id;
        SetCurrent(session);
        if (_ship is not null)
        {
            _ship.CurrentLocationId = anchor.Id; // a later landing/launch uses this system's anchor
        }

        session.State.AboardShip = true; // you arrive piloting the ship
        session.State.InEva = false;
        MarkSystemKnown(session, system.Id); // its bodies + mini map are now revealed on the travel screen

        // Finale (P6): remember the world we jumped FROM so a death in the boss arena returns us there (no loop).
        if (system.Id == GuardianFinaleSystemId && origin is not null)
        {
            _finaleReturn[playerId] = origin.Id;
        }

        EnterSpace(playerId, skipLaunch: true, hyperjump: true); // warp in; no surface take-off
        SendStarMap(session); // refresh the travel screen with the now-known system
        _log.Info($"Player '{session.State.Name}' hyperjumped into system '{system.Name}' (flight).");
    }

    /// <summary>Test/util entry: leave space and land on a specific body (system-scale flight landing).</summary>
    public void LandOnBody(string playerId, string destinationBodyId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleLeaveSpace(session, new LeaveSpaceIntent { DestinationBodyId = destinationBodyId });
        }
    }

    /// <summary>Leaves space onto a chosen body: the current world (default) or another body in the same
    /// system the player flew to (system-scale flight). Same-system landing is free; a body in another
    /// system would need a hyperspace jump (offered via the star map, not from flight).</summary>
    /// <summary>On an EVA spacewalk you may only set down on a small <b>asteroid</b>; planets and moons need
    /// the ship (board it first). Defends the rule on the server regardless of what the client offers.</summary>
    public bool EvaLandingAllowed(string bodyId)
    {
        var body = _galaxy?.FindBody(bodyId);
        return WorldConstants.SizeClassFor(body?.Kind ?? CelestialKind.Planet, body?.PlanetType ?? string.Empty)
               == WorldConstants.WorldSizeClass.Asteroid;
    }

    private void HandleLeaveSpace(PlayerSession session, LeaveSpaceIntent intent)
    {
        string dest = intent.DestinationBodyId ?? string.Empty;

        // From an EVA spacewalk you can only land on an asteroid — not a planet or moon.
        if (session.State.InEva)
        {
            string landBody = string.IsNullOrEmpty(dest) ? session.CurrentLocationId : dest;
            if (!EvaLandingAllowed(landBody))
            {
                Reject(session, "land", "On a spacewalk you can only land on an asteroid — board the ship to reach a planet or moon.");
                return;
            }
        }

        if (string.IsNullOrEmpty(dest) || dest == session.CurrentLocationId)
        {
            // Land back on the current body — claim a free landing pad first (item 38); a full body refuses.
            SetActiveWorld(session.CurrentLocationId);
            if (!ClaimPadOrReject(session, session.CurrentLocationId, intent.PadIndex))
            {
                return;
            }

            LeaveSpace(session.State.PlayerId);
            RelocateToAssignedPad(session); // set the player + their ship down on the claimed pad
            CheckpointSave("landed (returned to surface)"); // auto-save on landing
            return;
        }

        // Landed on a different body picked while flying — travel there (reuses the per-player travel, which
        // leaves space, loads the destination world and relocates only this player; it claims the pad too).
        // quickTravel:false — this is a MANUAL flight landing (you flew here), so it bypasses the Instant
        // Travel gate and marks the body as visited.
        HandleTravel(session, new TravelIntent { DestinationBodyId = dest, PadIndex = intent.PadIndex }, quickTravel: false);
    }

    private void HandleFireWeapon(PlayerSession session, FireWeaponIntent intent)
        => FireWeapon(session.State.PlayerId, intent.WeaponKey, intent.TargetEntityId);
}

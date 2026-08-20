// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

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
    Bandit,       // humanoid robber on foot, melee variant (lone hold-ups + camp guards)
    BanditGunner, // humanoid robber on foot, ranged variant (longer damage aura + tracer visuals)
    BanditShip,   // space raider that hails the player ship and demands cargo before opening fire
    EscapePod,    // #1129: a drifting life pod — fly close to rescue the survivor (never hostile/targetable)
    Anomaly,      // #1129: a shimmering unknown — scan it for knowledge + a lore text (never hostile)
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

    /// <summary>Seconds a creature roused from its off-phase rest stays awake (a player came too close, or it was
    /// hit). While &gt; 0 it ignores the day/night sleep gate and behaves per its temperament (flee/hunt/roam);
    /// decays each tick, after which it settles back to sleep. Server-only.</summary>
    public double AwakeOverrideTimer { get; set; }

    /// <summary>Seconds this creature stays startled (#653): set on itself and its nearby same-species kin when
    /// one of them is hurt or bolts. While &gt; 0 a NON-retaliating creature flees the nearest player; retaliators
    /// ignore it (they charge instead). Server-only, never persisted.</summary>
    public double PanicTimer { get; set; }

    /// <summary>What this entity drops when destroyed.</summary>
    public List<ItemAmount> Loot { get; set; } = new();

    // --- Hostile-NPC movement state (space drones/UFOs/cruisers patrol + chase; server-only) ---
    public bool PatrolInitialized { get; set; }
    public Vector3f PatrolCenter { get; set; }
    public double PatrolPhase { get; set; }

    /// <summary>Ground/surface locomotion state (stop-and-go, eased speed, turn inertia, vertical life) for
    /// planet fauna AND planet enemies. Server-only; a default value initialises itself on first step.</summary>
    public LocomotionState Loco;

    /// <summary>True once this hostile has noticed the ship (entered aggro range) and the "spotted" warning has
    /// been raised. Cleared again when it loses the ship, so re-engaging warns afresh. Server-only.</summary>
    public bool Spotted { get; set; }

    // --- Tamed companion (design: docs/developer/CREATURE_TAMING.md) ---

    /// <summary>Owner player id if this is a tamed companion (empty = wild fauna). Owned creatures follow their
    /// owner, never harm anyone, are excluded from the wild population cap + far-prune, and are spawned/
    /// despawned by the taming system (not the wild spawner).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The persisted companion record id (links this live entity to <c>PlayerState.TamedCreatures</c>).</summary>
    public string CompanionId { get; set; } = string.Empty;

    /// <summary>A tamed companion's player-given name (drawn as a nameplate); empty for wild fauna.</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>True when this entity is a tamed companion rather than wild fauna.</summary>
    public bool IsCompanion => OwnerId.Length > 0;

    // --- Bandit state (server-only; only meaningful on the Bandit* kinds) ---

    /// <summary>Where this bandit is in its hold-up script (approach → demand → fight/leave).</summary>
    public BanditPhase BanditPhase { get; set; }

    /// <summary>The player this bandit is stalking/robbing (empty = none; camp guards pick targets ad hoc).</summary>
    public string BanditTargetId { get; set; } = string.Empty;

    /// <summary>Camp anchor key when this bandit guards a bandit camp (empty = lone robber). Guards leash to
    /// their camp and their deaths count toward the camp's persisted "cleared" state.</summary>
    public string CampKey { get; set; } = string.Empty;

    /// <summary>True for the Bandit* kinds (targetable humans, not Guardian machines — no story credit).</summary>
    public bool IsBandit => Kind is CombatEntityKind.Bandit or CombatEntityKind.BanditGunner or CombatEntityKind.BanditShip;
}

/// <summary>A bandit's hold-up script phase (server-only).</summary>
public enum BanditPhase
{
    None,      // camp guards: plain hostile, no script
    Approach,  // walking/flying toward its mark, not hostile yet
    Demanding, // demand sent, waiting for the answer (or the deadline)
    Fighting,  // refused/attacked — plain hostile now
    Leaving,   // paid off or gave up — wanders away and despawns
}

/// <summary>A loaded local space region (orbit / asteroid field) around a location.</summary>
public sealed class SpaceInstance
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "orbit";
    public List<CombatEntity> Entities { get; set; } = new();
    public HashSet<string> Players { get; set; } = new();

    /// <summary>The last reported position of ANY pilot — ambient NPC targeting (traders/bandits) only.
    /// Player-triggered actions (fire/tractor/board/structure edits) resolve per pilot via
    /// <see cref="PlayerPoses"/> (#994); collision and incoming fire via <see cref="PilotSims"/> (#955).</summary>
    public Vector3f ShipPosition { get; set; }
    public Vector3f ShipLastPosition { get; set; }

    /// <summary>Each present player's pose (ship or floating EVA suit) so everyone in the instance can be drawn
    /// for the others — and, since #955, the per-pilot position for collision and incoming fire.</summary>
    public Dictionary<string, SpacePlayerPose> PlayerPoses { get; } = new();

    /// <summary>Per-pilot collision bookkeeping (#955): last ticked position + damage cooldown. One shared
    /// field per instance meant two pilots overwrote each other and ram damage could hit the wrong hull.</summary>
    public Dictionary<string, PilotSim> PilotSims { get; } = new();

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

    /// <summary>Rock count the LAUNCH-POINT field replenishes toward (#683 S1): the classic 3, or the
    /// dense-field target when this instance is anchored at an asteroid body (the ship launched inside
    /// the belt). Belt rock clusters parked at the OTHER asteroid bodies don't count toward it.</summary>
    public int AsteroidFieldTarget { get; set; } = 3;

    /// <summary>Voxel structures floating in this instance (item 20). S1: each present player's own ship,
    /// keyed by player id, seeded from its ship-editor design. Later stages add stations + voxel asteroids.</summary>
    public Dictionary<string, SpaceStructure> Structures { get; } = new();

    // ---- Peaceful NPC trader traffic (ambient liveliness) — transient, in-memory only ----

    /// <summary>NPC trader ships currently flying in this instance (warp in → cruise → dock/depart).</summary>
    internal List<NpcTrader> Traders { get; } = new();

    /// <summary>Uptime at which this instance may spawn its next NPC trader (paces ambient traffic by the
    /// system's traffic level).</summary>
    public double NextTraderSpawnAt { get; set; }

    /// <summary>True once this instance has rolled its first trader-spawn time (so a fresh instance isn't
    /// instantly busy).</summary>
    public bool TraderScheduleInit { get; set; }

    /// <summary>Throttle for streaming trader-movement updates to clients (mirrors the hostile sync cadence).</summary>
    public double TraderSyncTimer { get; set; }

    // ---- Bandit-ship ambush (one per flight at most; see GameServerBanditShips) ----

    /// <summary>True once this instance rolled its ambush dice (rolled exactly once per instance).</summary>
    public bool BanditRolled { get; set; }

    /// <summary>Uptime at which the bandit ship warps in (0 = no ambush this flight).</summary>
    public double BanditAmbushAt { get; set; }

    /// <summary>Entity id of the live bandit ship in this instance (empty = none yet/anymore).</summary>
    public string BanditShipId { get; set; } = string.Empty;

    /// <summary>Throttle for streaming the raider's approach/leave movement (#756) — those phases run outside
    /// <c>MoveSpaceHostiles</c> (the raider isn't hostile yet), so without this the ship froze between the
    /// warp-in point and the hail and clients saw teleports instead of an approach.</summary>
    public double BanditSyncTimer { get; set; }

    /// <summary>Throttle for re-broadcasting remote-pilot poses (#756): <c>HandleShipMove</c> stores a pose but
    /// never broadcast, so other players' ships only refreshed when a hostile or trader happened to move.</summary>
    public double PilotSyncTimer { get; set; }

    // ---- Peaceful encounters (#1129): at most one life pod / anomaly per instance ----

    /// <summary>True once this instance rolled its encounter dice (rolled exactly once per instance).</summary>
    public bool EncounterRolled { get; set; }

    /// <summary>0 = none this flight, 1 = drifting life pod, 2 = scannable anomaly.</summary>
    public int EncounterKind { get; set; }

    /// <summary>Uptime at which the encounter appears (a little into the flight, never instantly).</summary>
    public double EncounterAt { get; set; }

    /// <summary>Entity id of the live encounter object (empty = not spawned yet / resolved).</summary>
    public string EncounterId { get; set; } = string.Empty;
}

/// <summary>A player's pose in a space instance — where their ship (or EVA suit) is + which way it faces.</summary>
public readonly record struct SpacePlayerPose(Vector3f Pos, float Yaw, bool Eva);

/// <summary>Mutable per-pilot tick state in a space instance (#955): collision speed baseline + cooldown.</summary>
public sealed class PilotSim
{
    public Vector3f LastPosition { get; set; }
    public double CollisionCooldown { get; set; }
}

/// <summary>
/// Free space flight and ship combat (technical requirements / `anf_space_flight.md` §6-11).
/// A small, fully server-authoritative PvE slice (see `docs/developer/SPACE_COMBAT_CONCEPT.md`): local
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
    private readonly record struct WeaponSpec(float Damage, float Range, double Cooldown, float Energy, bool IsCombat, bool CanMine);

    // #694: ship weapons are rate-limited and energy-gated SERVER-side now (both stats existed in
    // data/ship_modules.json but were never enforced — the only limit was the client's local fire timer).
    private readonly Dictionary<string, double> _shipWeaponReadyAt = new(); // "playerId|weaponKey" → uptime next shot is allowed

    // Ship energy is a lazily-regenerating pool fed by the reactor's energy_production: capacity = a few
    // seconds of production, refill = production per second. With the stock reactor it never throttles
    // legitimate fire (production far outpaces any weapon's draw) — it exists so weapon_energy is real
    // and modded rapid-fire clients drain dry instead of firing forever.
    private readonly Dictionary<string, (double Time, float Energy)> _shipEnergyByPlayer = new();

    /// <summary>True while the player is flying in a space instance.</summary>
    public bool InSpace(string playerId) => _playerInstance.ContainsKey(playerId);

    /// <summary>The acting pilot's OWN position in the instance (#994). Instances are shared per body and
    /// <see cref="SpaceInstance.ShipPosition"/> is last-writer-wins across all pilots, so range checks and
    /// aim for player-triggered actions must read the pilot's pose instead. Falls back to the shared field
    /// only while no pose has arrived yet (the client reports one within its first ~0.1 s in space).</summary>
    private static Vector3f PilotPositionIn(SpaceInstance instance, string playerId)
        => instance.PlayerPoses.TryGetValue(playerId, out var pose) ? pose.Pos : instance.ShipPosition;

    /// <summary>The combat entities in the player's current space instance (empty if not in space).</summary>
    public IReadOnlyList<CombatEntity> SpaceEntitiesFor(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var inst)
            ? inst.Entities
            : Array.Empty<CombatEntity>();

    // ---------------- Ship combat stats ----------------

    /// <summary>Recomputes hull/shield maxima from built modules and clamps current values.</summary>
    private void RecomputeShipCombatStats()
    {
        // Base stats come from the active ship's design (data/ships.json); modules add on top. A self-built
        // ship has no content design — its hull derives from the geometry it was built with (#949).
        var design = _content.GetShip(_ship.ShipType);
        float hull = _ship.IsCustom ? CustomShipStatsFor(_ship).HullMax : design?.BaseHull ?? BaseHull;
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

        // A freshly created ship starts at full hull; clamp persisted values into range. A downed wreck
        // legitimately sits at zero hull — topping it up here would undo the wreck penalty on reload.
        if ((_ship.Hull <= 0f && !_ship.Downed) || _ship.Hull > _shipHullMax)
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
            Reject(session, "build_module", "@srv.module.unknown");
            return;
        }

        if (_ship.HasModule(module.Key))
        {
            Reject(session, "build_module", "@srv.module.already");
            return;
        }

        if (!p.AboardShip)
        {
            Reject(session, "build_module", "@srv.module.aboard");
            return;
        }

        if (!_ship.HasModule("workshop"))
        {
            Reject(session, "build_module", "@srv.module.workshop");
            return;
        }

        if (!string.IsNullOrEmpty(module.RequiredBlueprint) &&
            !p.UnlockedBlueprints.Contains(module.RequiredBlueprint!))
        {
            Reject(session, "build_module", "@srv.craft.blueprint_locked");
            return;
        }

        bool free = !Rules.CraftingCostsMaterialsFor(p.ModeOverride) || p.InstantBuild;
        var pool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (!pool.Has(module.BuildCost))
            {
                Reject(session, "build_module", "@srv.craft.missing_materials");
                return;
            }

            pool.Remove(module.BuildCost);
        }

        // The Mk3 core REPLACES the Mk2 (#799): the old core comes out of the rack and is salvaged at the
        // disassembly rate. Without this the obsolete module sat in ship.Modules forever, fully paid, while
        // VegaCoreTier just picked the max. Salvage is skipped in free mode — nothing was paid for the Mk2
        // build either, and creative refunds would mint materials.
        if (module.Key == "ai_core_mk3" && _ship.Modules.Remove("ai_core_mk2"))
        {
            if (!free && _content.GetShipModule("ai_core_mk2") is { } mk2)
            {
                foreach (var part in mk2.BuildCost)
                {
                    int recovered = (int)System.Math.Floor(part.Count * DisassemblyRecoveryRate);
                    if (recovered > 0)
                    {
                        pool.Add(part.Item, recovered);
                    }
                }
            }
        }

        _ship.Modules.Add(module.Key);
        ResizeCargo(_ship);
        RecomputeShipCombatStats();

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.module.built")
                .Replace("{name}", LocalizedName(session.Locale, module.NameKey, module.Key)),
        });
        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: salvaged Mk2 parts that found no room are gone — say so
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
            RejectSpace(session, "@srv.space.flight_disabled");
            return;
        }

        if (session is not null && !session.State.AboardShip)
        {
            RejectSpace(session, "@srv.space.board_first");
            return;
        }

        if (_playerInstance.ContainsKey(playerId))
        {
            return; // already in space
        }

        if (_ship.Downed)
        {
            RejectSpace(session, "@srv.space.wrecked");
            return;
        }

        // A self-built ship must (still) be flight-worthy: commissioning can be edited away again on foot,
        // so the same validation gate re-runs on every launch (#950).
        if (_ship.IsCustom)
        {
            if (!_ship.Commissioned)
            {
                RejectSpace(session, "@srv.ship.not_commissioned");
                return;
            }

            if (CustomShipLaunchProblem(_ship) is { } problem)
            {
                RejectSpace(session, problem);
                return;
            }
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

        // Seed this pilot's pose + collision baseline at the launch point (#955). Poses used to appear only
        // once the client sent its first ShipMove, so the others' avatars popped in late (and were destroyed
        // again by any snapshot that arrived before it), and the collision speed baseline started at
        // wherever the pilot already was instead of where they launched.
        var launchPose = new SpacePlayerPose(instance.ShipPosition, 0f, session?.State.InEva ?? false);
        instance.PlayerPoses[playerId] = launchPose;
        instance.PilotSims[playerId] = new PilotSim { LastPosition = launchPose.Pos };

        // Launch with the shields up (baseline + modules). The clamp in RecomputeShipCombatStats only ever lowers
        // the stored shield, so a fresh ship would otherwise start a flight at 0 shield and have to charge it.
        RecomputeShipCombatStats();
        _ship.Shield = _shipShieldMax;

        if (session is not null)
        {
            ShipAiOnEnterSpace(session); // VEGA onboarding: first launch into space
            ShipAiBanditSectorWarning(session, instance); // pirate space? warn BEFORE any raider appears
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
            instance.PilotSims.Remove(playerId); // per-pilot collision state dies with the flight (#955)
            if (instance.Players.Count == 0)
            {
                _spaceInstances.Remove(instanceId);
            }
        }

        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            Send(session, new SpaceClosed { Reason = "@srv.space.returned", ShipDisabled = false });
        }
    }

    private SpaceInstance CreateSpaceInstance(string instanceId)
    {
        var instance = new SpaceInstance { Id = instanceId, Kind = "orbit" };

        string anchorId = instanceId.StartsWith("space:") ? instanceId.Substring("space:".Length) : instanceId;
        // A never-launched ship still carries its creation placeholder (the default planet TYPE, not a
        // body id) as its location, so resolve the true start body through the save's active location
        // when the instance key doesn't name a real body.
        var anchor = _galaxy?.FindBody(anchorId) ?? _galaxy?.FindBody(_meta.ActiveLocationId);

        // Asteroids are always present as scenery + mining targets; breaking them is gated at fire
        // time. Launching from an asteroid body means the ship starts INSIDE the belt, so the local
        // field is dense (#683 S1); anywhere else it stays the classic sparse trio.
        int asteroids = anchor?.Kind == CelestialKind.AsteroidField ? DenseAsteroidFieldTarget : AsteroidFieldTarget;
        instance.AsteroidFieldTarget = asteroids;
        for (int i = 0; i < asteroids; i++)
        {
            // B10: scatter them around the body (a golden-angle ring at varied radius/height) instead of a
            // tight line — but inside weapon range (asteroid_breaker reaches ~40) so they stay shootable.
            // The dense field stacks extra layers in height rather than radius for the same reason.
            float ang = i * 2.39996f;
            float rad = 18f + (i % 3) * 8f; // 18 / 26 / 34
            // item 20 S3: each asteroid is a voxel ore body (entity + structure) you can shoot AND EVA-mine.
            // #687: the ordinal seeds the family/size roll (0 = the pinned classic metallic rock).
            SpawnAsteroid(instance,
                new Vector3f(rad * (float)System.Math.Cos(ang), ((i % 3) - 1) * 9f + (i / 3) * 14f, rad * (float)System.Math.Sin(ang)),
                ordinal: i,
                broadcast: false);
        }

        AddBeltRockClusters(instance, anchor); // #683 S2: mineable rocks AT the system's asteroid bodies

        AddStationContacts(instance);
        AddPersistedStations(instance); // item 20 S4: re-create player-built stations floating in this instance
        AddDerelictToInstance(instance); // #1129: "The Long Quiet" drifts in exactly one body's space

        // Hostile NPC drones only when space combat is enabled and NPC enemies are switched on — and never
        // once the Guardian core is destroyed (P6 pacification: the galaxy is at peace).
        bool combatEnabled = Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both;
        if (combatEnabled && !_storyState.GuardianDefeated)
        {
            // The finale system runs its own scripted ELITE gauntlet (P6 Stage 1) instead of the ambient
            // hostiles — the anchor body id (the "space:" prefix already stripped above) keys the check.
            if (IsGuardianSystemLocation(anchorId))
            {
                SpawnGuardianGauntlet(instance);
            }
            else
            {
                // Spawn hostiles FAR from the launch point (well beyond ShipEngageRange) so launching/docking is
                // safe and combat is opt-in — you choose to fly out to them. They used to spawn ~25u away and
                // hammered the ship the instant it launched (continuous damage → destroyed → respawn at base).
                // #547: the system archetype shades the ambient hostility — Desolate space is truly empty
                // (no drones, no UFO), a Pirate Haven runs one extra drone when NPC enemies are on at all.
                // Deliberately keyed on the RAW anchor id (not the resolved start-body fallback above):
                // a never-launched ship carries its type placeholder here, which never resolves — so the
                // first launch has always been shaded Standard, and changing that would silently raise
                // the fresh-start difficulty in pirate-space starts.
                var archetype = SystemArchetypeOf(_galaxy?.FindBody(anchorId)?.SystemId);
                int drones = ActivityCount(Rules.SpaceNpcEnemies);
                if (archetype == SystemArchetype.Desolate)
                {
                    drones = 0;
                }
                else if (archetype == SystemArchetype.PirateHaven && drones > 0)
                {
                    drones = System.Math.Min(4, drones + 1);
                }

                // #741: per-location wave memory — repeat launches stop replaying the identical wave. The
                // flight ordinal rotates every bearing (so the wave sits somewhere new each launch), every
                // 4th flight runs quieter, and hostiles destroyed here stay dead until the sector re-arms.
                var wave = AmbientWaveFor(instanceId);
                int flight = wave.FlightOrdinal++;
                if (flight % 4 == 3 && drones > 1)
                {
                    drones--;
                }

                drones = System.Math.Max(0, drones - wave.DronesKilled);

                for (int i = 0; i < drones; i++)
                {
                    // Bearings fan out golden-angle-rotated per flight; the radius stays far outside the
                    // drone's (reduced) aggro range so its patrol drift can never reach the launch point.
                    float ang = -0.71f + flight * 2.39996f + i * 0.35f;
                    float rad = 205f + (i % 3) * 18f;
                    instance.Entities.Add(new CombatEntity
                    {
                        Id = NextEntityId(),
                        Kind = CombatEntityKind.Drone,
                        Hostile = true,
                        Hull = 40f,
                        HullMax = 40f,
                        Position = new Vector3f(rad * (float)System.Math.Cos(ang),
                            10f + ((i + flight) % 3 - 1) * 8f,
                            rad * (float)System.Math.Sin(ang)),
                        DamagePerSecond = 5f,
                        Loot = { new ItemAmount("data_fragment", 1) },
                    });
                }

                if (Rules.AlienUfos != AlienActivity.Off && archetype != SystemArchetype.Desolate
                    && wave.UfosKilled == 0)
                {
                    float uang = 2.42f + flight * 2.39996f; // roughly opposite the drone fan, rotating per flight
                    instance.Entities.Add(new CombatEntity
                    {
                        Id = NextEntityId(),
                        Kind = CombatEntityKind.Ufo,
                        Hostile = true,
                        // Softened for a forgiving PvE feel: was 70 hull / 8 dps, which killed an unshielded ship in
                        // ~12s and took a long time to down. Now closer to a drone so UFOs read as a light threat.
                        Hull = 40f,
                        HullMax = 40f,
                        Position = new Vector3f(230f * (float)System.Math.Cos(uang), 14f, 230f * (float)System.Math.Sin(uang)),
                        DamagePerSecond = 4f,
                        Loot = { new ItemAmount("data_fragment", 3) },
                    });
                }
            }
        }

        return instance;
    }

    /// <summary>#741: session-scoped ambient-wave memory for one location — how many launches happened here
    /// (varies the wave layout per flight) and which ambient hostiles were destroyed (kept dead until the
    /// sector re-arms). Survives the instance teardown on landing, so relaunching doesn't reset the fight.</summary>
    private sealed class AmbientWave
    {
        public int FlightOrdinal;
        public int DronesKilled;
        public int UfosKilled;
        public double ReplenishAt; // uptime at which the destroyed hostiles return (rolls from the last kill)
    }

    private readonly Dictionary<string, AmbientWave> _ambientWaves = new(); // instanceId → wave memory
    private const double AmbientReplenishSeconds = 480.0; // destroyed ambient hostiles return after ~8 min

    private AmbientWave AmbientWaveFor(string instanceId)
    {
        if (!_ambientWaves.TryGetValue(instanceId, out var wave))
        {
            wave = new AmbientWave();
            _ambientWaves[instanceId] = wave;
        }

        if (wave.ReplenishAt > 0 && _uptime >= wave.ReplenishAt)
        {
            wave.DronesKilled = 0; // the sector re-arms — the next launch faces a full wave again
            wave.UfosKilled = 0;
            wave.ReplenishAt = 0;
        }

        return wave;
    }

    /// <summary>Records a destroyed ambient hostile in its location's wave memory (#741) so the next launch
    /// doesn't replay it. Finale-gauntlet instances never rolled a wave record, so they are unaffected.</summary>
    private void RecordAmbientHostileKill(SpaceInstance instance, CombatEntity target)
    {
        if (!_ambientWaves.TryGetValue(instance.Id, out var wave))
        {
            return;
        }

        if (target.Kind == CombatEntityKind.Drone)
        {
            wave.DronesKilled++;
        }
        else if (target.Kind == CombatEntityKind.Ufo)
        {
            wave.UfosKilled++;
        }
        else
        {
            return;
        }

        wave.ReplenishAt = _uptime + AmbientReplenishSeconds;
    }

    private const int DenseAsteroidFieldTarget = 9;  // launch-field rocks when anchored at an asteroid (#683 S1)
    private const int BeltClusterRocks = 4;          // mineable rocks parked at each other asteroid body (#683 S2)
    private const int BeltClusterCap = 24;           // belt rocks per instance, total (broadcast/entity budget)
    private const float BeltClusterMinRadius = 18f;  // just outside an asteroid body's keep-out shell

    /// <summary>#683 S2: parks a small mineable rock cluster at the flight-view position of every OTHER
    /// landable asteroid body in the resident system, so flying INTO the belt means flying through rocks
    /// worth mining — not just past the sized, landable bodies. Positions replicate the client's layout
    /// transform (star-map delta to the anchor × <see cref="SystemBodyLayout.FlightViewScale"/>); the
    /// client's overlap-relax pass can nudge a BODY slightly off that spot in a legacy scattered layout,
    /// but the cluster still reads as "the rocks around that asteroid". Deterministic per body id, so
    /// re-entering the instance rebuilds the same field.</summary>
    private void AddBeltRockClusters(SpaceInstance instance, CelestialBody? anchor)
    {
        if (anchor is null || _galaxy?.Systems.FirstOrDefault(s => s.Id == anchor.SystemId) is not { } system)
        {
            return;
        }

        int spawned = 0;
        foreach (var b in system.Bodies)
        {
            if (b.Kind != CelestialKind.AsteroidField || b.Id == anchor.Id || spawned >= BeltClusterCap)
            {
                continue;
            }

            float cx = (b.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale;
            float cz = (b.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale;
            int h = 17;
            foreach (char c in b.Id)
            {
                h = h * 31 + c;
            }

            for (int r = 0; r < BeltClusterRocks && spawned < BeltClusterCap; r++, spawned++)
            {
                float ang = ((h & 0xff) / 255f) * 6.2831853f + r * 2.39996f; // per-body phase + golden spread
                float rad = BeltClusterMinRadius + ((h >> (r * 3 + 8)) & 15); // 18..33
                SpawnAsteroid(instance,
                    new Vector3f(
                        cx + rad * (float)System.Math.Cos(ang),
                        ((r % 3) - 1) * 10f,
                        cz + rad * (float)System.Math.Sin(ang)),
                    // #687 family/size roll: belt rocks use their own ordinal series (well past any
                    // launch-field/respawn ordinal, never the pinned 0) — deterministic per entry
                    // because bodies iterate in stable galaxy order.
                    ordinal: 100 + spawned,
                    broadcast: false);
            }
        }
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
    public void FireWeapon(string playerId, string weaponKey, string targetId, float dirX = 0f, float dirY = 0f, float dirZ = 0f)
    {
        var session = FindSessionByPlayerId(playerId);

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            RejectSpace(session, "@srv.space.not_flying");
            return;
        }

        if (session is not null)
        {
            SetCurrent(session); // pin the ship cursor to the firing player so _ship (tractor check / loot) is theirs
        }

        if (!TryGetWeapon(weaponKey, out var weapon))
        {
            RejectSpace(session, "@srv.space.no_weapon");
            return;
        }

        // #694: the module's fire rate is authoritative now (it was client-only before). A small slack
        // absorbs network jitter so an honest client firing exactly on cadence never gets rejected.
        // The cooldown is only COMMITTED once the shot actually fires (below) — a rejected shot
        // (bad target/arc/rules) must not eat the cycle.
        string cdKey = playerId + "|" + weaponKey;
        if (weapon.Cooldown > 0.0 && _shipWeaponReadyAt.TryGetValue(cdKey, out var readyAt) && _uptime < readyAt)
        {
            return; // still cycling — swallow silently (no reject spam while the trigger is held)
        }

        var target = instance.Entities.FirstOrDefault(e => e.Id == targetId);
        if (target is null)
        {
            RejectSpace(session, "@srv.attack.no_target");
            return;
        }

        var shipPos = PilotPositionIn(instance, playerId); // #994: THIS pilot's ship, not whoever moved last
        if (target.Position.DistanceSquared(shipPos) > weapon.Range * weapon.Range)
        {
            RejectSpace(session, "@srv.space.out_of_range");
            return;
        }

        if (!ValidateSpaceAim(session, shipPos, target, dirX, dirY, dirZ))
        {
            return;
        }

        if (!WeaponAllowedAgainst(weapon, target, out var reason))
        {
            RejectSpace(session, reason);
            return;
        }

        // #694: weapon_energy draws from the reactor-fed pool (was defined in the module data but unused).
        if (!TryDrawShipEnergy(playerId, weapon.Energy))
        {
            RejectSpace(session, "@srv.space.no_energy");
            return;
        }

        if (weapon.Cooldown > 0.0)
        {
            _shipWeaponReadyAt[cdKey] = _uptime + weapon.Cooldown * 0.95;
        }

        target.Hull -= weapon.Damage;

        // item 20 S3: a voxel ore asteroid carves down to match its hull as you shoot it (visible depletion).
        if (target.Kind == CombatEntityKind.Asteroid && instance.Structures.ContainsKey(target.Id))
        {
            CarveAsteroidToHull(instance, target);
        }

        if (target.Hull > 0f)
        {
            if (target.Kind == CombatEntityKind.BanditShip && !target.Hostile)
            {
                OnBanditShipAttacked(instance, target); // opening fire during the hail IS the answer
            }

            BroadcastSpaceState(instance);
            return;
        }

        // Destroyed. A large/medium asteroid splits into smaller chunks instead of dropping loot;
        // only the smallest asteroids (and other entities) yield resources.
        instance.Entities.Remove(target);
        if (target.Kind == CombatEntityKind.BanditShip)
        {
            OnBanditShipKilled(instance, target); // a person, not a Guardian machine — no story credit
            if (session is not null)
            {
                OnMissionDefeat(session, DefeatTargetShip); // #731: the raider bounty counts the drive-off
            }
        }
        else if (target.Hostile)
        {
            RecordStoryMachineKill(); // space machine (drone/UFO) destroyed → advances the story (P4)
            RecordAmbientHostileKill(instance, target); // #741: stays dead across relaunches for a while
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
            BankLoot(session, pool, target.Loot); // target is already destroyed — warn rather than lose it silently
            SendInventory(session);
        }

        BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = target.Id });
        BroadcastSpaceState(instance);
    }

    /// <summary>Validates the client's reported firing direction (the ship's nose) against the claimed
    /// target (#693). Mirrors the on-foot <c>ValidateAim</c>: generous tolerances (latency, drifting
    /// targets), zero direction = older client = skip. AutoAim ON needs the target roughly ahead;
    /// AutoAim OFF needs a genuine boresight line — the nose ray must pass near the target's body.</summary>
    private bool ValidateSpaceAim(PlayerSession? session, Vector3f shipPos, CombatEntity target, float dirX, float dirY, float dirZ)
    {
        float dirLenSq = dirX * dirX + dirY * dirY + dirZ * dirZ;
        if (dirLenSq < 0.0001f)
        {
            return true; // no aim data (older client) — keep the legacy range-only behaviour
        }

        float tx = target.Position.X - shipPos.X;
        float ty = target.Position.Y - shipPos.Y;
        float tz = target.Position.Z - shipPos.Z;
        float dist = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (dist < 3f)
        {
            return true; // point-blank
        }

        float dirLen = (float)System.Math.Sqrt(dirLenSq);
        float dot = (dirX * tx + dirY * ty + dirZ * tz) / (dirLen * dist);

        if (!Rules.AutoAim)
        {
            // Boresight: perpendicular miss distance of the nose ray from the target's centre, allowing
            // the body itself plus a distance-scaled corridor (space entities are big and drift fast).
            float along = System.Math.Max(0f, dot) * dist;
            float missSq = dist * dist - along * along;
            float allowed = 2.5f * System.Math.Max(1f, target.Scale) + 0.1f * dist;
            if (dot <= 0f || missSq > allowed * allowed)
            {
                RejectSpace(session, "@srv.space.missed");
                return false;
            }

            return true;
        }

        if (dot < 0.5f) // ~60°: server-side guardrail above the client's ~±30° acquisition cone
        {
            RejectSpace(session, "@srv.space.arc");
            return false;
        }

        return true;
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
                reason = "@srv.space.asteroids_off";
                return false;
            }

            // Mining tools + dual lasers always break asteroids; a pure combat cannon only where the
            // server allows weapons against rocks.
            if (!weapon.CanMine && Rules.AsteroidDestruction != AsteroidDestructionMode.WeaponsAllowed)
            {
                reason = "@srv.space.asteroids_mining_only";
                return false;
            }

            return true;
        }

        if (target.Kind == CombatEntityKind.SpaceStation)
        {
            reason = "@srv.space.no_fire_station";
            return false;
        }

        if (target.Kind == CombatEntityKind.ResourceDrop || (!target.Hostile && target.Kind != CombatEntityKind.BanditShip))
        {
            // Non-hostiles can't be shot — EXCEPT a hailing bandit ship: opening fire on the extortionist
            // is a legitimate answer (it turns the hold-up into a fight).
            reason = "@srv.space.invalid_target";
            return false;
        }

        // Hostile NPC target: needs an actual combat weapon and combat-enabling rules.
        if (!weapon.IsCombat)
        {
            reason = "@srv.space.mining_tool";
            return false;
        }

        if (Rules.SpaceCombat is not (SpaceCombatMode.PvE or SpaceCombatMode.Both))
        {
            reason = "@srv.space.combat_off";
            return false;
        }

        if (Rules.ShipWeapons is ShipWeaponMode.Off or ShipWeaponMode.MiningOnly)
        {
            reason = "@srv.space.weapons_off";
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
            Energy: (float)def.Stats.GetValueOrDefault("weapon_energy", 0),
            IsCombat: weaponClass >= 1,                  // combat weapons + dual lasers can hit hostiles
            CanMine: weaponClass == 0 || weaponClass == 2); // mining tools + dual lasers can break asteroids
        return true;
    }

    /// <summary>Total reactor output of the current ship (energy per second) — feeds the weapon-energy pool.</summary>
    private float ShipEnergyProduction()
    {
        float prod = 0f;
        foreach (var key in _ship.Modules)
        {
            if (_content.GetShipModule(key) is { } m)
            {
                prod += (float)m.Stats.GetValueOrDefault("energy_production", 0);
            }
        }

        return prod;
    }

    /// <summary>Tries to draw <paramref name="amount"/> from the player's lazily-regenerating ship-energy
    /// pool (#694). Capacity is ~3 s of reactor output; refill happens on access, so no per-tick work.</summary>
    private bool TryDrawShipEnergy(string playerId, float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        float production = ShipEnergyProduction();
        if (production <= 0f)
        {
            return true; // no reactor data on this ship — never lock the trigger over a missing stat
        }

        float capacity = System.Math.Max(amount, production * 3f);
        var pool = _shipEnergyByPlayer.TryGetValue(playerId, out var state)
            ? System.Math.Min(capacity, state.Energy + (float)((_uptime - state.Time) * production))
            : capacity;
        if (pool < amount)
        {
            _shipEnergyByPlayer[playerId] = (_uptime, pool);
            return false;
        }

        _shipEnergyByPlayer[playerId] = (_uptime, pool - amount);
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

    /// <summary>Tractor beam: pulls salvage drops within <paramref name="range"/> of the COLLECTING pilot's
    /// ship into that ship's cargo hold (until full). The passive tick uses a short range; a manual pull
    /// (quick-bar) sweeps a wider one. The caller must have pinned the ship cursor to the collector (#994).</summary>
    private void CollectSalvage(SpaceInstance instance, float range, string playerId)
    {
        if (!_ship.HasModule(TractorModule))
        {
            return;
        }

        var shipPos = PilotPositionIn(instance, playerId); // #994: sweep around the collector's own ship
        bool changed = false;
        foreach (var drop in instance.Entities.Where(e => e.Kind == CombatEntityKind.ResourceDrop).ToList())
        {
            if (drop.Position.DistanceSquared(shipPos) > range * range)
            {
                continue;
            }

            if (StowDrop(instance, drop))
            {
                changed = true;
            }
        }

        if (changed)
        {
            BroadcastSpaceState(instance);
            foreach (var presentId in instance.Players)
            {
                if (FindSessionByPlayerId(presentId) is { } s)
                {
                    SendInventory(s); // cargo is part of the inventory update when aboard
                }
            }
        }
    }

    /// <summary>Stows one salvage drop's loot into the ship's cargo hold (until full), removing the drop when it
    /// is emptied. Returns true if anything was stowed (cargo full ⇒ false, loot stays floating).</summary>
    private bool StowDrop(SpaceInstance instance, CombatEntity drop)
    {
        bool stowed = false;
        var leftover = new List<ItemAmount>();
        foreach (var item in drop.Loot)
        {
            int max = _content.GetItem(item.Item)?.MaxStack ?? ItemDefinition.DefaultMaxStack;
            int notStowed = _ship.Cargo.Add(item.Item, item.Count, max); // cargo full → leave the rest floating
            if (notStowed < item.Count)
            {
                stowed = true;
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

        return stowed;
    }

    private const float TractorPullRange = 30f; // a manual quick-bar tractor sweep reaches further than the passive pull
    private const float TractorReach = 45f;     // an AIMED (auto-locked) drop pulls in from as far as the laser reaches

    /// <summary>Manual tractor pull (quick-bar). With a locked <paramref name="targetId"/> the client picked,
    /// pulls THAT drop in from a generous range (3D depth is hard to eyeball, so the blind radius sweep used to
    /// miss drops that looked close). With no target it falls back to the legacy radius sweep.</summary>
    public void TractorPull(string playerId, string targetId = "")
    {
        var session = FindSessionByPlayerId(playerId);
        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            return;
        }

        if (!_ship.HasModule(TractorModule))
        {
            RejectSpace(session, Localize(session?.Locale ?? "en", "space.tractor.none_fitted"));
            return;
        }

        if (!string.IsNullOrEmpty(targetId))
        {
            var drop = instance.Entities.FirstOrDefault(e => e.Id == targetId
                && e.Kind == CombatEntityKind.ResourceDrop);
            if (drop is null)
            {
                return; // already collected / gone — no need to nag
            }

            if (drop.Position.DistanceSquared(PilotPositionIn(instance, playerId)) > TractorReach * TractorReach)
            {
                RejectSpace(session, Localize(session?.Locale ?? "en", "space.tractor.out_of_range"));
                return;
            }

            if (!StowDrop(instance, drop))
            {
                RejectSpace(session, Localize(session?.Locale ?? "en", "space.tractor.cargo_full"));
                return;
            }

            BroadcastSpaceState(instance);
            foreach (var playerId2 in instance.Players)
            {
                if (FindSessionByPlayerId(playerId2) is { } s)
                {
                    SendInventory(s);
                }
            }

            return;
        }

        CollectSalvage(instance, TractorPullRange, playerId);
    }

    private void HandleTractorPull(PlayerSession session, TractorPullIntent intent)
        => TractorPull(session.State.PlayerId, intent.TargetEntityId);

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
            // collision bounce doesn't move the ship away from the drop first). Per pilot (#994): each
            // fitted tractor sweeps around its OWN ship into its own cargo, not the shared position.
            foreach (var collectorId in instance.Players.ToList())
            {
                if (FindSessionByPlayerId(collectorId) is { Joined: true } collector)
                {
                    SetCurrent(collector); // module check + cargo below resolve to this pilot's ship
                    CollectSalvage(instance, TractorRange, collectorId);
                }
            }

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

            // Peaceful NPC trader traffic: spawn (warp in), fly toward a station/inner system, dock or pass
            // through, depart (warp out). Purely ambient — invulnerable, never damages anyone.
            TickSpaceTraders(instance, dt);

            // Bandit-ship ambush: in flagged systems a raider may warp in, hail the ship and demand cargo —
            // comply and it leaves, refuse and it fights (see GameServerBanditShips).
            TickBanditShips(instance, dt);

            // Peaceful encounters (#1129): sometimes a life pod drifts by (fly close = rescue) or an
            // anomaly shimmers (scan it). Never hostile — runs under every preset, like the traders.
            TickSpaceEncounters(instance);

            // Remote pilots (#756): HandleShipMove only STORES poses — without a periodic re-broadcast the
            // other players' ships only refreshed when a hostile or trader happened to move (0 Hz in a
            // quiet instance). Solo instances skip it; the pose data is theirs alone.
            instance.PilotSyncTimer += dt;
            if (instance.Players.Count > 1 && instance.PilotSyncTimer >= 0.2)
            {
                instance.PilotSyncTimer = 0;
                BroadcastSpaceState(instance);
            }

            // Collision + hostile fire, per PILOT (#955): position, speed baseline and cooldown used to
            // live in one shared field per instance, so with two pilots they overwrote each other and the
            // damage could land on the other player's hull (the ship cursor was wherever the last message
            // left it). Runs AFTER MoveSpaceHostiles so freshly-aggroed hostiles bite within the same tick
            // (the pre-#955 ordering the combat tests rely on).
            bool instanceClosed = false;
            foreach (var pilotId in instance.Players.ToList())
            {
                if (FindSessionByPlayerId(pilotId) is not { Joined: true } pilot
                    || !instance.PlayerPoses.TryGetValue(pilotId, out var pose))
                {
                    continue; // no pose yet — the client reports one within its first ~0.1 s in space
                }

                if (!instance.PilotSims.TryGetValue(pilotId, out var sim))
                {
                    instance.PilotSims[pilotId] = sim = new PilotSim { LastPosition = pose.Pos };
                }

                SetCurrent(pilot); // damage/shield below must resolve to THIS pilot's ship
                float speed = (float)(System.Math.Sqrt(pose.Pos.DistanceSquared(sim.LastPosition))
                                      / System.Math.Max(dt, 0.0001));
                sim.CollisionCooldown = System.Math.Max(0.0, sim.CollisionCooldown - dt);
                bool hitAsteroid = instance.Entities.Any(e => e.Kind == CombatEntityKind.Asteroid
                    && e.Position.DistanceSquared(pose.Pos) <= ShipCollisionRadius * ShipCollisionRadius);
                if (hitAsteroid && speed > ShipCollisionMinSpeed)
                {
                    if (sim.CollisionCooldown <= 0.0)
                    {
                        // A ram dents the shield first, then the hull — never an instant kill (B56). Brief
                        // grace afterwards so holding thrust into the rock doesn't stack damage every tick.
                        ApplyShipDamage(System.Math.Min(ShipCollisionMaxDamage, speed * ShipCollisionDamageFactor));
                        sim.CollisionCooldown = ShipCollisionCooldown;
                        SendShipCombatStatus(pilot);
                        if (_ship.Hull <= 0f)
                        {
                            DisableShip(instance);
                            instanceClosed = true;
                            break;
                        }
                    }
                }
                else
                {
                    sim.LastPosition = pose.Pos; // keep the pre-impact baseline while touching the rock
                }

                // Hostile fire on THIS pilot: only hostiles within engagement range of their own pose.
                float incoming = instance.Entities
                    .Where(e => e.Hostile && e.Position.DistanceSquared(pose.Pos) <= ShipEngageRange * ShipEngageRange)
                    .Sum(e => e.DamagePerSecond);
                if (incoming > 0f)
                {
                    bool evaded = ApplyShipDamage((float)(incoming * dt));
                    if (_ship.Hull <= 0f)
                    {
                        DisableShip(instance);
                        instanceClosed = true;
                        break;
                    }

                    SendShipCombatStatus(pilot);
                    ShipAiThreatCallout(pilot); // Mk2+: VEGA calls out hostile contact (rate-limited)
                    if (evaded)
                    {
                        ShipAiEvadeCallout(pilot); // Mk3: the dodge that just saved the hull
                    }
                }
                else if (_ship.Shield < _shipShieldMax)
                {
                    // Out of combat: this pilot's shield recharges.
                    _ship.Shield = System.Math.Min(_shipShieldMax, _ship.Shield + (float)(_shipShieldRegen * dt));
                }
            }

            if (instanceClosed)
            {
                continue;
            }

            // A mined-out asteroid field slowly replenishes over the session (positions stay deterministic).
            RespawnAsteroids(instance, dt);
        }
    }

    private const int AsteroidFieldTarget = 3;            // large-equivalent asteroids the field tends toward
    private const double AsteroidRespawnInterval = 120.0; // seconds between replenishing spawns (B9: slower respawn)
    private const float LaunchFieldRange = 60f;           // rocks this close to the launch point ARE the local field

    /// <summary>Slowly refills a mined-out asteroid field back toward its target so it isn't barren for the
    /// rest of the session (a fresh field is still generated on each space entry). Only the LAUNCH-POINT
    /// field counts toward the target — the belt rock clusters parked at the system's other asteroid
    /// bodies (#683 S2) sit far outside <see cref="LaunchFieldRange"/> and must not satisfy it, or the
    /// local field would never replenish in a belt-rich system.</summary>
    private void RespawnAsteroids(SpaceInstance instance, double dt)
    {
        int count = instance.Entities.Count(e => e.Kind == CombatEntityKind.Asteroid
            && e.Position.X * e.Position.X + e.Position.Z * e.Position.Z <= LaunchFieldRange * LaunchFieldRange);
        if (count >= instance.AsteroidFieldTarget)
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
        // item 20 S3: voxel ore body (sends its mesh + state). #687: respawn ordinals continue past the
        // initial batch (field target + rotor — the launch field may be the dense 9, #683 S1) so
        // replenished rocks roll fresh families/sizes deterministically.
        SpawnAsteroid(instance, pos, ordinal: instance.AsteroidFieldTarget + r, broadcast: true);
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
    /// they press in, and how fast they fly. Aggro MUST stay well below the ambient spawn distances
    /// (~200-230u; gauntlet ≥150u) (#741): the old radii (drone 190 / UFO 240 / cruiser 260) exceeded them,
    /// so the UFO hunted the ship the instant it launched and every flight auto-started the same fight —
    /// the "combat is opt-in, you fly out to them" spawn design only holds when they can't see that far.</summary>
    private static (float Aggro, float MinDist, float Speed) HostileProfile(CombatEntityKind kind) => kind switch
    {
        CombatEntityKind.Drone => (120f, 16f, 9f),
        CombatEntityKind.Ufo => (150f, 24f, 7f),
        CombatEntityKind.Cruiser => (170f, 36f, 4f),
        CombatEntityKind.BanditShip => (280f, 20f, 8f), // once hostile it presses in hard (it already knows you)
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
            // The hold band reaches 15 % past the stand-off ring (#756): chase vs hold used to flip at the
            // exact ring every tick while the player's ship drifted, and each flip gated the movement
            // broadcast — irregular update spacing the client rendered as stutter.
            float holdSq = minDist * 1.15f * (minDist * 1.15f);
            if (distSq <= aggro * aggro && distSq > holdSq)
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
            else if (distSq <= holdSq)
            {
                continue; // inside the stand-off hold band — hold and let the weapon aura work
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
                if (len < 0.05f)
                {
                    continue; // exactly on the ring point — nothing to do this tick
                }

                tx /= len;
                ty /= len;
                tz /= len;
                // Ease toward the (moving) ring point instead of the old hard 0.5-block dead-zone (#756):
                // catch-freeze-catch made every patroller stop-go by construction, 2–3 ticks at a time.
                moveSpeed = speed * 0.45f * System.Math.Clamp(len / 3f, 0.15f, 1f);
                maxStep = len; // never overshoot the ring point in one big-dt step
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
    /// The ship was defeated. The outcome depends on the <see cref="GameRules.KeepShipOnDeath"/> world rule:
    /// <list type="bullet">
    /// <item><b>true</b> (default, §8.5 casual safety net): no permanent loss — the ship is recovered to base
    /// with restored hull/shields and present players respawn at the heal-tank.</item>
    /// <item><b>false</b>: the ship is left a WRECK — its hull stays at zero and a chunk of the hull is carved
    /// away (durable edits) — parked on the owner's home landing pad. The owner must repair it through the
    /// normal own-ship repair flow before it can launch again (enforced in <see cref="EnterSpace"/>).</item>
    /// </list>
    /// Either way the flight instance is unloaded.
    /// </summary>
    private void DisableShip(SpaceInstance instance)
    {
        bool keepShip = Rules.KeepShipOnDeath;
        string shipOwnerId = instance.Structures.Values.FirstOrDefault(s => s.Kind == "ship")?.OwnerId ?? string.Empty;

        if (keepShip)
        {
            _ship.Hull = _shipHullMax;
            _ship.Shield = _shipShieldMax; // recovered to base with shields restored too (baseline + modules)
            _ship.Downed = false;
        }
        else
        {
            _ship.Hull = 0f;
            _ship.Shield = 0f;
            _ship.Downed = true; // grounded until repaired (gate in EnterSpace)
            WreckShipHull(shipOwnerId); // carve a chunk of the hull (durable) so it reads + repairs as a wreck
        }

        foreach (var playerId in instance.Players.ToList())
        {
            _playerInstance.Remove(playerId);
            if (FindSessionByPlayerId(playerId) is not { } session)
            {
                continue;
            }

            var p = session.State;
            p.InEva = false; // the ship's loss ends any spacewalk
            p.Health = 100f;
            p.Oxygen = 100f;

            if (!keepShip && playerId == shipOwnerId)
            {
                // Park the wreck on the owner's home pad so it occupies a landing spot AND is repairable there
                // (the repair flow needs a placed own-ship structure). The medbay survives the carving, so the
                // heal-tank respawn still works.
                SetCurrent(session);
                if (SetActiveWorld(session.CurrentLocationId))
                {
                    PlaceLandedShip();
                }

                p.AboardShip = true;
                p.Position = _healTank;
                p.RespawnPoint = _healTank;
            }
            else
            {
                p.Position = p.RespawnPoint;
                p.AboardShip = true;
            }

            Send(session, new SpaceClosed
            {
                Reason = keepShip
                    ? "@srv.space.ship_disabled"
                    : "@srv.space.ship_destroyed",
                ShipDisabled = true,
            });
            SendShipCombatStatus(session);
            SendPlayerState(session);
            if (!keepShip && playerId == shipOwnerId)
            {
                SendShipRepairStatus(session); // show the repair job immediately
            }
        }

        instance.Players.Clear();
        _spaceInstances.Remove(instance.Id);
    }

    /// <summary>Carves a scattered ~40% of the owner ship's non-critical hull cells away as durable edits, so a
    /// ship lost under <c>KeepShipOnDeath = false</c> reads as a breached wreck and the existing own-ship repair
    /// flow (hull plating + per-cell rebuild) gives a real repair job. Station/module cells and the medbay cell
    /// are spared so the heal-tank respawn keeps working.</summary>
    private void WreckShipHull(string ownerId)
    {
        if (string.IsNullOrEmpty(ownerId))
        {
            return;
        }

        if (FindSessionByPlayerId(ownerId) is { } owner)
        {
            SetCurrent(owner); // pin _ship/design reference to the wreck's owner
        }

        var design = OwnShipDesignReference(ownerId);
        var spared = new HashSet<Vector3i>(design.StationCells.Select(sc => sc.Cell));
        if (design.MedbayCell is { } mb)
        {
            spared.Add(mb);
        }

        int i = 0, carved = 0;
        foreach (var cell in design.Baseline)
        {
            if (spared.Contains(cell))
            {
                continue;
            }

            if (i++ % 5 < 2) // ~2 of every 5 hull cells, scattered deterministically
            {
                _repo.SetStructureBlock(StructureEditStoreId(design), cell, BlockId.AirValue);
                carved++;
            }
        }

        _log.Info($"Ship of {ownerId} wrecked: carved {carved} hull cells (KeepShipOnDeath off).");
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
            // Other real pilots PLUS the peaceful NPC traders out here — both ride the flight view's
            // remote-ship render path (their voxel hull arrives via a "ship_remote" SpaceShipDesign).
            Players = AppendTraderPoses(OtherPlayersInSpace(session.State.PlayerId, instance), instance),
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
        List<NetSpacePlayer>? others = null;
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
            RejectSpace(session, "@srv.space.flight_disabled");
            return;
        }

        Serve(session); // _ship = this player's ship

        var system = _galaxy?.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system is null || system.Bodies.Count == 0)
        {
            RejectSpace(session, "@srv.space.unknown_system");
            return;
        }

        var origin = _galaxy?.FindBody(session.CurrentLocationId);
        if (origin is not null && origin.SystemId == system.Id)
        {
            RejectSpace(session, "@srv.space.same_system");
            return;
        }

        // A jump lane between the two systems substitutes for the generator (#1125): the relays carry you.
        if ((_ship is null || !_ship.HasModule("jump_generator")) && !HasJumpLane(origin?.SystemId, system.Id))
        {
            RejectSpace(session, "@srv.travel.no_jump_generator");
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
        OnAchievementHyperjump(session);        // "Jump Pilot" (#1102)
        RecordStoryMilestone("hyperjump:first"); // the save's first jump between stars advances the arc (#1105)
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
                Reject(session, "land", "@srv.space.eva_asteroid_only");
                return;
            }
        }

        if (string.IsNullOrEmpty(dest) || dest == session.CurrentLocationId)
        {
            // Land back on the current body — claim a free landing pad first (item 38); a full body refuses.
            // An observer takes no pad (#487/#996): pads are finite and communal — same rule as HandleTravel.
            SetActiveWorld(session.CurrentLocationId);
            if (!session.Spectating && !ClaimPadOrReject(session, session.CurrentLocationId, intent.PadIndex))
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
        => FireWeapon(session.State.PlayerId, intent.WeaponKey, intent.TargetEntityId, intent.DirX, intent.DirY, intent.DirZ);
}

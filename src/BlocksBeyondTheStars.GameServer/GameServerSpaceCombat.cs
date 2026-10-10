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
    Wreck,        // #1664: the star map's derelict ship — a voxel hull you carve for salvage (never hostile)
    Wormhole,     // #2242: a tear in space-time to a twin in another star system — fly through it (never hostile)
    DebrisField,  // #2353: a debris field's marker — chart / radar / waypoint / scanner target, its flight recorder (never hostile)
    Debris,       // #2353/#2356: a wreckage fragment — a small voxel hull carved for scrap like an asteroid (never hostile)
    SalvageCapsule, // #2353: a sealed salvage capsule — collected like a resource drop, pays once per galaxy (never hostile)
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

    /// <summary>#2009: an arachnid ambusher is sitting in wait this tick — motionless until a player comes within
    /// <see cref="Shared.Definitions.ArachnidRules.LurkRange"/>. Transient; on the wire only for the client's pose.</summary>
    public bool Lurking { get; set; }

    /// <summary>Server uptime after which a gift-giving species (#1760, the flowerling) may spill its next gift.</summary>
    public double GiftReadyAt { get; set; }

    /// <summary>#2018: where a begging herd animal is in its routine (<see cref="BegPhase.None"/> for everything that does not
    /// beg). Server-only, never persisted — on the wire only as <c>NetCreature.Begging</c> for the pose and the call cadence.</summary>
    public BegPhase BegPhase { get; set; }

    /// <summary>#2018: server uptime at which the current begging phase ends — the interest runs out, the squabble is decided,
    /// the trot away is over.</summary>
    public double BegUntil { get; set; }

    /// <summary>#2018: server uptime before which this animal ignores food again after a bout of begging.</summary>
    public double BegCooldownUntil { get; set; }

    /// <summary>#2082: the piece this animal rushes is its species' favourite food — after it the herd wants more, so it comes
    /// back after <see cref="Shared.Definitions.HerdRules.FavouriteCooldownSeconds"/> instead of the long cooldown.</summary>
    public bool BegFavourite { get; set; }

    /// <summary>#2018: server uptime of the next begging hop (a grounded jumper hops on a beat while it begs).</summary>
    public double NextBegHopAt { get; set; }

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

    /// <summary>#2278: server uptime until which this creature, machine or bandit is dazed by a shock-glove push — it
    /// neither moves nor attacks (a machine's damage aura pauses, a bandit holds its fire). Networked as
    /// <c>Staggered</c> for the dizzy look. Server-only runtime state, never persisted.</summary>
    public double StaggerUntil { get; set; }

    /// <summary>#2278: uptime before which no new daze may start (<see cref="KnockbackRules.StaggerImmuneSeconds"/> after
    /// the last one ended) — the target can still be pushed. Server-only.</summary>
    public double StaggerImmuneUntil { get; set; }

    /// <summary>Seconds a creature roused from its off-phase rest stays awake (a player came too close, or it was
    /// hit). While &gt; 0 it ignores the day/night sleep gate and behaves per its temperament (flee/hunt/roam);
    /// decays each tick, after which it settles back to sleep. Server-only.</summary>
    public double AwakeOverrideTimer { get; set; }

    /// <summary>Seconds this creature stays startled (#653): set on itself and its nearby same-species kin when
    /// one of them is hurt or bolts. While &gt; 0 a NON-retaliating creature flees the nearest player; retaliators
    /// ignore it (they charge instead). Server-only, never persisted.</summary>
    public double PanicTimer { get; set; }

    /// <summary>Server uptime at which this creature next re-validates that its body sits clear of every
    /// colliding block (#1357; sleepers check every tick regardless). Server-only, never persisted.</summary>
    public double NextBodyCheckAt { get; set; }

    /// <summary>What this entity drops when destroyed.</summary>
    public List<ItemAmount> Loot { get; set; } = new();

    /// <summary>#2357: heading in degrees about Y for an entity that flies a real hull (the raider turns its voxel ship
    /// toward its course). On the wire as <c>NetCombatEntity.Yaw</c>; 0 for everything else.</summary>
    public float Yaw { get; set; }

    /// <summary>#2353: for a debris fragment or a salvage capsule — the debris field body it belongs to. Empty for the
    /// fragments a destroyed ship leaves (#2356), which the per-instance cap counts.</summary>
    public string FieldId { get; set; } = string.Empty;

    // --- Hostile-NPC movement state (space drones/UFOs/cruisers patrol + chase; server-only) ---
    public bool PatrolInitialized { get; set; }
    public Vector3f PatrolCenter { get; set; }
    public double PatrolPhase { get; set; }

    /// <summary>Ground/surface locomotion state (stop-and-go, eased speed, turn inertia, vertical life) for
    /// planet fauna AND planet enemies. Server-only; a default value initialises itself on first step.</summary>
    public LocomotionState Loco;

    /// <summary>Vertical mechanics state for planet fauna (#1331): jump/fall velocity, airborne flag, a flier's
    /// perch phase, an amphibian's water hysteresis. Server-only; a default value is "grounded".</summary>
    public VerticalState Vert;

    /// <summary>True once this hostile has noticed the ship (entered aggro range) and the "spotted" warning has
    /// been raised. Cleared again when it loses the ship, so re-engaging warns afresh. Server-only.</summary>
    public bool Spotted { get; set; }

    /// <summary>#2285: the pilot this space hostile hunts (empty = none yet). Each hostile picks its own — the nearest,
    /// kept for a while — so with several pilots in one instance it no longer swings to whoever reported last.
    /// Server-only.</summary>
    public string ChaseTargetId { get; set; } = string.Empty;

    /// <summary>#2285: uptime before which the hostile keeps <see cref="ChaseTargetId"/> without looking for a nearer
    /// pilot. Server-only.</summary>
    public double ChaseRetargetAt { get; set; }

    // --- Tamed companion (design: docs/developer/CREATURE_TAMING.md) ---

    /// <summary>Owner player id if this is a tamed companion (empty = wild fauna). Owned creatures follow their
    /// owner, never harm anyone, are excluded from the wild population cap + far-prune, and are spawned/
    /// despawned by the taming system (not the wild spawner).</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>The persisted companion record id (links this live entity to <c>PlayerState.TamedCreatures</c>).</summary>
    public string CompanionId { get; set; } = string.Empty;

    /// <summary>#2057: the clone tank ("x,y,z") this wild animal grew in — never far-pruned while the tank stands; empty for everything else.</summary>
    public string CloneOf { get; set; } = string.Empty;

    /// <summary>A tamed companion's player-given name (drawn as a nameplate); empty for wild fauna.</summary>
    public string CustomName { get; set; } = string.Empty;

    /// <summary>True when this entity is a tamed companion rather than wild fauna.</summary>
    public bool IsCompanion => OwnerId.Length > 0;

    /// <summary>A one-per-world giant's own state (#1998, the colossus or a sandworm); null for every other entity.
    /// Giants live in the creature list for the wire, the hits and the kills, but move, attack, spawn and leave by
    /// their own rules (<c>GameServerGiants</c>) — the spawner, the far prune, the bite aura, sentries, fire, the
    /// stasis projector and taming all pass them by.</summary>
    public GiantRuntime? Giant { get; set; }

    /// <summary>True for the colossus and the sandworms (#1998).</summary>
    public bool IsGiant => Giant is not null;

    /// <summary>Companion payoff (#1210, server-only): uptime until which the pet poses "alert" (client flag),
    /// and when its next produce drop is due (0 = not armed yet).</summary>
    public double AlertUntil { get; set; }
    public double NextProduceAt { get; set; }

    /// <summary>Bandit side of the companion payoff (#1210, server-only): a robber stalled by a companion holds
    /// until <see cref="StallUntil"/>; it can be stalled again from <see cref="NextStallAt"/> on.</summary>
    public double StallUntil { get; set; }
    public double NextStallAt { get; set; }

    // --- Bandit state (server-only; only meaningful on the Bandit* kinds) ---

    /// <summary>Where this bandit is in its hold-up script (approach → demand → fight/leave).</summary>
    public BanditPhase BanditPhase { get; set; }

    /// <summary>The player this bandit is stalking/robbing (empty = none; camp guards pick targets ad hoc).</summary>
    public string BanditTargetId { get; set; } = string.Empty;

    /// <summary>Camp anchor key when this bandit guards a bandit camp (empty = lone robber). Guards leash to
    /// their camp and their deaths count toward the camp's persisted "cleared" state.</summary>
    public string CampKey { get; set; } = string.Empty;
    /// <summary>The base this bandit came to look at as a scout (#1224), 0 = not a scout. Kept after it turns
    /// hostile, so beating it still credits the homestead bounty.</summary>
    public int ScoutBaseId { get; set; }

    /// <summary>True for the Bandit* kinds (targetable humans, not Guardian machines — no story credit).</summary>
    public bool IsBandit => Kind is CombatEntityKind.Bandit or CombatEntityKind.BanditGunner or CombatEntityKind.BanditShip;
}

/// <summary>A begging herd animal's routine (#2018, server-only): it <see cref="Beg"/>s from a player holding food (approaches and
/// orbits), <see cref="Rush"/>es a thrown piece, <see cref="Squabble"/>s over it, and <see cref="Leave"/>s — a trot away before it
/// goes back to roaming.</summary>
public enum BegPhase : byte
{
    None,
    Beg,
    Rush,
    Squabble,
    Called,    // #2057: coming to a caller block (non-begging species use this phase alone)
    Leave,
}

/// <summary>A bandit's hold-up script phase (server-only).</summary>
public enum BanditPhase
{
    None,      // camp guards: plain hostile, no script
    Approach,  // walking/flying toward its mark, not hostile yet
    Demanding, // demand sent, waiting for the answer (or the deadline)
    Fighting,  // refused/attacked — plain hostile now
    Leaving,   // paid off or gave up — wanders away and despawns
    Scouting,  // #1224: walks to a base's zone edge, stands there a minute, leaves — never enters, never demands
}

/// <summary>A loaded local space region (orbit / asteroid field) around a location.</summary>
public sealed class SpaceInstance
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "orbit";
    public List<CombatEntity> Entities { get; set; } = new();
    public HashSet<string> Players { get; set; } = new();

    /// <summary>The last reported position of ANY pilot — ambient NPC targeting (traders, a raider's approach) only.
    /// Player-triggered actions (fire/tractor/board/structure edits) resolve per pilot via
    /// <see cref="PlayerPoses"/> (#994); collision and incoming fire via <see cref="PilotSims"/> (#955); a hostile's
    /// chase and its "spotted you" warning via its own target pilot (<see cref="CombatEntity.ChaseTargetId"/>, #2285).</summary>
    public Vector3f ShipPosition { get; set; }
    public Vector3f ShipLastPosition { get; set; }

    /// <summary>Each present player's pose (ship or floating EVA suit) so everyone in the instance can be drawn
    /// for the others — and, since #955, the per-pilot position for collision and incoming fire.</summary>
    public Dictionary<string, SpacePlayerPose> PlayerPoses { get; } = new();

    /// <summary>#2118: each pilot's SHIP pose — the last report made while flying, not on an EVA. During a spacewalk
    /// <see cref="PlayerPoses"/> follows the suit, while the ship floats where the pilot stepped out; boarding it
    /// again must remember the ship, not the suit.</summary>
    public Dictionary<string, SpacePlayerPose> ShipPoses { get; } = new();

    /// <summary>Per-pilot collision bookkeeping (#955): last ticked position + damage cooldown. One shared
    /// field per instance meant two pilots overwrote each other and ram damage could hit the wrong hull.</summary>
    public Dictionary<string, PilotSim> PilotSims { get; } = new();

    /// <summary>Seconds until the ship can take asteroid-collision damage again — a brief grace after a bump so a
    /// ram dents the shield/hull instead of stacking damage every tick and instantly destroying the ship (B56).</summary>
    public double CollisionCooldown { get; set; }

    /// <summary>Throttle for streaming hostile-movement updates (drones/UFOs patrol + chase now).</summary>
    public double HostileSyncTimer { get; set; }

    /// <summary>Per pilot: uptime after which another "hostile spotted you" warning may be raised for that pilot — so a
    /// pack arriving together raises one warning, not one per ship (#2285: per pilot, so one pilot's warning never
    /// swallows the next pilot's).</summary>
    public Dictionary<string, double> SpottedReadyAt { get; } = new();

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

    /// <summary>#2355: seconds until drifting debris may tap this pilot's shield again.</summary>
    public double DebrisBumpCooldown { get; set; }

    /// <summary>#2355: the "debris is tapping the shield" line went out once this flight.</summary>
    public bool DebrisBumpToasted { get; set; }
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

    /// <summary>Test/inspection: the active ship's current hull maximum (design + modules).</summary>
    public float HullMaxForTest => _shipHullMax;
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

    /// <summary>Test hook: the id of the flight instance the player is in (null on a surface).</summary>
    public string? SpaceInstanceIdForTest(string playerId) => _playerInstance.TryGetValue(playerId, out var id) ? id : null;

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

        // A fitted higher tier already does this module's job (#2240): building the lower one would sit beside it.
        if (_ship.Modules.Any(m => _content.GetShipModule(m)?.Replaces.Contains(module.Key) == true))
        {
            Reject(session, "build_module", "@srv.module.superseded");
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

        // A higher tier REPLACES the lower ones it names (#799 for the AI core, data-driven since #2240 — the Quantum
        // scanner replaces the Deep scanner): the old module comes out of the rack and is salvaged at the disassembly
        // rate. Without this the obsolete module sat in ship.Modules forever, fully paid. Salvage is skipped in free
        // mode — nothing was paid for the old build either, and creative refunds would mint materials.
        foreach (string replaced in module.Replaces)
        {
            if (!_ship.Modules.Remove(replaced) || free || _content.GetShipModule(replaced) is not { } old)
            {
                continue;
            }

            foreach (var part in old.BuildCost)
            {
                int recovered = (int)System.Math.Floor(part.Count * DisassemblyRecoveryRate);
                if (recovered > 0)
                {
                    pool.Add(part.Item, recovered);
                }
            }
        }

        _ship.Modules.Add(module.Key);
        ResizeCargo(_ship);
        RecomputeShipCombatStats();

        if (module.Stats.ContainsKey("shield") ||
            module.Stats.ContainsKey("shield_regen"))
        {
            _ship.Shield = _shipShieldMax;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.module.built")
                .Replace("{name}", LocalizedName(session.Locale, module.NameKey, module.Key)),
        });
        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: salvaged Mk2 parts that found no room are gone — say so
        SendShipCombatStatus(session);
        SendPlayerState(session); // AiCoreTier may have changed (gates the client autopilot)
        BroadcastOwnedShips(); // the fleet message carries each ship's fit (#1268)
        ShipAiOnModuleBuilt(session, module.Key); // VEGA welcomes her new core
    }

    // ---------------- Enter / leave space ----------------

    /// <summary>Why the current ship (<c>_ship</c>, point it with <see cref="Serve"/> first) cannot fly right now —
    /// a wrecked hull, or a self-built ship that is not (or no longer) commissioned or flight-worthy (#950: the gate
    /// re-runs on every launch, since commissioning can be edited away again) — else null.</summary>
    private string? ShipLaunchProblem()
    {
        if (_ship.Downed)
        {
            return "@srv.space.wrecked";
        }

        if (_ship.IsCustom)
        {
            if (!_ship.Commissioned)
            {
                return "@srv.ship.not_commissioned";
            }

            return CustomShipLaunchProblem(_ship);
        }

        return null;
    }

    /// <summary>Every gate <see cref="EnterSpace"/> applies before it moves anyone (#2233): space flight allowed,
    /// the pilot aboard, the ship able to fly. Callers that leave another world first — the ship interior's helm and
    /// hatch — ask this BEFORE they leave, so a refusal keeps the pilot where they are instead of stranding them on
    /// the body below. <paramref name="requireAboard"/> is off for the interior: a pilot stepping out through the
    /// hatch already stands outside the hull (not "aboard"), and the return puts them back aboard itself.
    /// Null = good to go.</summary>
    private string? SpaceLaunchProblem(PlayerSession session, bool requireAboard = true)
    {
        if (!Rules.FreeSpaceFlight)
        {
            return "@srv.space.flight_disabled";
        }

        if (requireAboard && !session.State.AboardShip)
        {
            return "@srv.space.board_first";
        }

        return ShipLaunchProblem();
    }

    /// <summary>Launches the player into a space instance around the ship's location. <paramref name="resume"/>
    /// (#2118) puts the ship back where it floated before the player left the flight view without landing (the
    /// ship interior, a station boarded from an EVA) — the flight view is told that pose, instead of drawing the
    /// ship at the launch point and reporting THAT back over the remembered spot.</summary>
    public void EnterSpace(string playerId, bool skipLaunch = false, bool hyperjump = false, SpacePlayerPose? resume = null, bool wormhole = false)
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

        if (ShipLaunchProblem() is { } problem)
        {
            RejectSpace(session, problem);
            return;
        }

        // #1584: the flight instance is keyed by the body the PILOT launches from, not by the body the ship
        // remembers. The ship's record only ever followed the ACTIVE ship (landing, hyperjump, respawn), so a
        // switched-in hull, a claimed wreck or a shipyard purchase could still carry another body — or the
        // creation placeholder — and the launch put the pilot into THAT orbit (home-system station, pod and
        // asteroids over a planet three systems away). The pilot is aboard, so the ship is physically here;
        // write it back so a later death re-homes to this body too.
        string locationId = session is not null && !string.IsNullOrEmpty(session.CurrentLocationId)
            ? session.CurrentLocationId
            : string.IsNullOrEmpty(_ship.CurrentLocationId) ? _meta.ActiveLocationId : _ship.CurrentLocationId;
        // The ambient-hostility shading keeps its documented rule (see CreateSpaceInstance): a ship that has never
        // flown still carries its type placeholder, which resolves to no system — its first launch stays shaded
        // Standard. A ship with a real (even stale) body is shaded by the system it actually launches in.
        string hostilityAnchorId = _galaxy?.FindBody(_ship.CurrentLocationId) is null && !string.IsNullOrEmpty(_ship.CurrentLocationId)
            ? _ship.CurrentLocationId
            : locationId;
        _ship.CurrentLocationId = locationId;
        string instanceId = "space:" + locationId;
        if (!_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            instance = CreateSpaceInstance(instanceId, hostilityAnchorId);
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
        if (resume is { } back)
        {
            instance.ShipPosition = back.Pos;
            instance.ShipLastPosition = back.Pos;
        }

        var launchPose = new SpacePlayerPose(instance.ShipPosition, resume?.Yaw ?? 0f, session?.State.InEva ?? false);
        instance.PlayerPoses[playerId] = launchPose;
        instance.ShipPoses[playerId] = launchPose with { Eva = false };
        instance.PilotSims[playerId] = new PilotSim { LastPosition = launchPose.Pos };

        // Launch with the shields up (baseline + modules). The clamp in RecomputeShipCombatStats only ever lowers
        // the stored shield, so a fresh ship would otherwise start a flight at 0 shield and have to charge it.
        RecomputeShipCombatStats();
        _ship.Shield = _shipShieldMax;

        if (session is not null)
        {
            ShipAiOnEnterSpace(session); // VEGA onboarding: first launch into space
            ShipAiBanditSectorWarning(session, instance); // pirate space? warn BEFORE any raider appears
            // #1677: the star map goes FIRST. The flight view builds its scene — star, planets, landables —
            // from the map the moment it sees the space state, so a map arriving after it (a big message, a
            // late packet) had the view build the DEPARTURE system and offer its planets to land on.
            SendStarMap(session); // the space view needs the system's bodies to render + land on them
            SendSystemWeather(session); // #2173: …and their live weather, before the view builds its cloud shells
            SendSpaceState(session, instance, skipLaunch, hyperjump, resume, wormhole);
            SendShipCombatStatus(session);

            // item 20 S1: carry the player's ship as a voxel structure in the instance + send it so the flight
            // view renders the real designed ship (1:1) instead of the hand-built cube model. Rebuilt fresh on
            // every entry: ALL ship edits persist as per-cell deltas now (EVA hull work, interior furnishing),
            // so the rebuild is lossless and picks up edits made while landed.
            var structureId = "ship:" + playerId;
            var structure = BuildShipStructure(playerId);
            instance.Structures[structure.Id] = structure; // keyed by structure id ("ship:<playerId>")

            SendShipDesign(session, structure);

            // item 20 S3: also send every voxel asteroid body so the flight view renders + can mine them —
            // and every player-built station (#1470): its design was only ever sent at commission, so after a
            // landing or a restart re-entering pilots saw the generic placeholder instead of the real hull.
            // The system's derelict (#1664) is a voxel hull of the same family.
            foreach (var st in instance.Structures.Values)
            {
                if (st.Kind == "asteroid" || st.Kind == "station" || st.Kind == "wreck" || st.Kind == "debris")
                {
                    SendShipDesign(session, st); // a debris fragment (#2353/#2356) is a voxel hull of the same family
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
            instance.ShipPoses.Remove(playerId);
            if (instance.Players.Count == 0 && !AnyParkedShipIn(instance)) // #2431: a parked ship keeps the instance
            {
                StashFloatingSalvage(instance); // #1475: uncollected ore outlives the flight
                PersistSpaceSalvageLedger();    // #2354: a half-carved wreck keeps its state across the teardown
                _spaceInstances.Remove(instanceId);
            }
        }

        var session = FindSessionByPlayerId(playerId);
        if (session is not null)
        {
            Send(session, new SpaceClosed { Reason = "@srv.space.returned", ShipDisabled = false });
        }
    }

    /// <param name="instanceId">The flight instance key, <c>space:</c> + the body the pilot launches from (#1584).</param>
    /// <param name="hostilityAnchorId">The body id that shades the ambient hostiles — the launch body, or a
    /// never-flown ship's type placeholder (which resolves to no system and so to the Standard shading).</param>
    private SpaceInstance CreateSpaceInstance(string instanceId, string? hostilityAnchorId = null)
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
        AddSpaceWrecks(instance, anchor);      // #1664: the system's derelict, AT its star-map position
        AddSpaceWormholes(instance, anchor);   // #2242: the system's wormhole end, if it has one
        AddDebrisFields(instance, anchor);     // #2353: the system's debris field — fragments, capsules, the recorder

        AddStationContacts(instance);
        AddPersistedStations(instance); // item 20 S4: re-create player-built stations floating in this instance
        AddDerelictToInstance(instance); // #1129: "The Long Quiet" drifts in exactly one body's space
        RestoreFloatingSalvage(instance); // #1475: salvage left floating when this space was last torn down

        // Hostile NPC drones only when space combat is enabled and NPC enemies are switched on. Once the
        // Guardian core is destroyed the finale gauntlet stays down for good, and the ambient waves survive
        // only where space is dangerous by its own nature — pirate havens and the opt-in frontier-danger
        // tier (Remnant Protocol, #1206) — so the post-game galaxy is calmer, not empty.
        bool combatEnabled = Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both;
        if (combatEnabled)
        {
            // The finale system runs its own scripted ELITE gauntlet (P6 Stage 1) instead of the ambient
            // hostiles — the anchor body id (the "space:" prefix already stripped above) keys the check.
            if (IsGuardianSystemLocation(anchorId))
            {
                if (!_storyState.GuardianDefeated)
                {
                    SpawnGuardianGauntlet(instance);
                }
            }
            else
            {
                // Spawn hostiles FAR from the launch point (well beyond ShipEngageRange) so launching/docking is
                // safe and combat is opt-in — you choose to fly out to them. They used to spawn ~25u away and
                // hammered the ship the instant it launched (continuous damage → destroyed → respawn at base).
                // #547: the system archetype shades the ambient hostility — Desolate space is truly empty
                // (no drones, no UFO), a Pirate Haven runs one extra drone when NPC enemies are on at all.
                // Deliberately keyed on the RAW hostility anchor (not the resolved start-body fallback above):
                // a never-launched ship passes its type placeholder here, which never resolves — so the
                // first launch has always been shaded Standard, and changing that would silently raise
                // the fresh-start difficulty in pirate-space starts. Since #1584 the instance itself is keyed
                // by the pilot's body, so EnterSpace hands the ship's pre-launch record in separately.
                string hostilityId = hostilityAnchorId ?? anchorId;
                var archetype = SystemArchetypeOf(_galaxy?.FindBody(hostilityId)?.SystemId);
                int drones = ActivityCount(Rules.SpaceNpcEnemies);
                bool remnantSpace = RemnantSpaceHostile(archetype, hostilityId);
                if (archetype == SystemArchetype.Desolate || !remnantSpace)
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
                    && remnantSpace && wave.UfosKilled == 0)
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

    /// <summary>Remnant Protocol (#1206): before the win every non-desolate system carries ambient hostiles;
    /// after it only the places that are dangerous by their own nature keep them — a pirate haven, or the
    /// full-frontier tier when the world's opt-in FrontierDanger rule is on.</summary>
    private bool RemnantSpaceHostile(SystemArchetype archetype, string anchorBodyId)
        => !_storyState.GuardianDefeated
           || archetype == SystemArchetype.PirateHaven
           || (Rules.FrontierDanger && FrontierTierForBody(anchorBodyId) >= 2);

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
        // A derelict hull (#1664) is salvaged the same way — plating comes off shot by shot — and so is a
        // debris fragment (#2353).
        if (IsCarvedKind(target.Kind) && instance.Structures.ContainsKey(target.Id))
        {
            CarveAsteroidToHull(instance, target);
            if (target.Kind == CombatEntityKind.Wreck)
            {
                NoteWreckHull(instance, target); // #2354: the carved state outlives the flight
            }
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
        if (LeavesCombatDebris(target.Kind))
        {
            SpawnCombatDebris(instance, target); // #2356: wreckage — before the raider's hull structure goes below
        }

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
                OnMachineDefeated(session); // #1213: post-win only — the survey orders' Defeat step
                TryDropPlayerMemory(session); // a chance to release a personal memory (P4)
            }
        }

        if (IsCarvedKind(target.Kind) && instance.Structures.ContainsKey(target.Id))
        {
            RemoveAsteroidStructure(instance, target.Id); // S3: drop the voxel body too (loot handled below)
            // fall through to the loot branch (voxel asteroids are tier 0 → they yield ore)
        }

        if (target.Kind == CombatEntityKind.Wreck)
        {
            CompleteWreckSalvage(target.Id); // #2354: salvaged down to nothing — gone for good, paid once
            if (session is not null)
            {
                MarkSpaceWreckVisited(session, target.Id); // #1664: salvaged down to nothing counts as having been there
            }
        }

        if (target.Kind == CombatEntityKind.Asteroid && target.AsteroidTier > 0)
        {
            SplitAsteroid(instance, target);
        }
        else
        {
            PayEntityLoot(instance, target, session);
        }

        BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = target.Id });
        BroadcastSpaceState(instance);
    }

    /// <summary>The voxel kinds a weapon carves cell by cell and an EVA pick hand-mines: asteroids, the derelict wreck
    /// (#1664) and debris fragments (#2353).</summary>
    private static bool IsCarvedKind(CombatEntityKind kind)
        => kind is CombatEntityKind.Asteroid or CombatEntityKind.Wreck or CombatEntityKind.Debris;

    /// <summary>Pays a destroyed entity's loot: with a tractor beam fitted it floats as a salvage drop to be collected;
    /// otherwise it goes straight to the backpack and the hold, whatever finds no room floating as a drop (#1475), with
    /// the "+n → where" toasts (#1317). Shared by the laser kill and the EVA pick that mines a hull to nothing (#2354 —
    /// the pick used to remove a wreck without paying its salvage). The ship cursor must point at the collector.</summary>
    private void PayEntityLoot(SpaceInstance instance, CombatEntity target, PlayerSession? session)
    {
        if (target.Loot.Count == 0)
        {
            return;
        }

        if (_ship.HasModule(TractorModule))
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
            return;
        }

        if (session is null)
        {
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        var backpackBefore = target.Loot.Select(l => session.State.Inventory.CountOf(l.Item)).ToList();
        var cargoBefore = target.Loot.Select(l => _ship.Cargo.CountOf(l.Item)).ToList();
        BankLoot(session, pool, target.Loot); // target is already destroyed — warn rather than lose it silently
        // #1475: whatever found no room floats as salvage at the rock instead of vanishing — space has no
        // drop packets, so this is its lossless path (a tractor beam, or flying through it, collects it later).
        var leftovers = pool.TakeLeftovers();
        if (leftovers.Count > 0)
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.ResourceDrop,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = target.Position,
                Loot = leftovers,
            });
        }

        SendInventory(session);

        // #1317: say where the ore went. The pool fills the backpack first and the hold after, and until
        // now neither path said a word — "what happens after I break it?" was a support question.
        for (int i = 0; i < target.Loot.Count; i++)
        {
            var l = target.Loot[i];
            QueueLootToast(session, l.Item,
                toBackpack: System.Math.Max(0, session.State.Inventory.CountOf(l.Item) - backpackBefore[i]),
                toCargo: System.Math.Max(0, _ship.Cargo.CountOf(l.Item) - cargoBefore[i]));
        }
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

        if (IsCarvedKind(target.Kind))
        {
            // Asteroid mining/breaking is governed by AsteroidDestruction, independent of combat. A derelict
            // hull (#1664) is salvage, not a fight — it follows the same rule.
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
    // clear of the fight actually stops the damage and lets the shield recharge. The number lives in Shared
    // (#2284) because the client draws the enemy shots and the "attacking" lock state from the very same range.
    private const float ShipEngageRange = SpaceCombatRules.EngageRange;

    /// <summary>Test seam (#2284): the engage range the hostiles' damage aura really uses — pinned to the shared constant.</summary>
    public static float ShipEngageRangeForTest => ShipEngageRange;

    private const string TractorModule = "tractor_beam";
    // Passive auto-collect radius. Was 8 — too tight: salvage spawns at the destroyed rock's centre, so after a
    // mid-range kill you often couldn't get close enough to vacuum it (most noticeable on your very first kill,
    // before you've learned to nose right into the wreck). Widened so flying near the wreck reliably collects it.
    /// <summary>Passive tractor pull range — the module's <c>tractor_range</c> stat (#1477: the data value was
    /// dead and the server always used 16; now the stat IS the number, 16 when the data has none).</summary>
    private float TractorRange => (float)(_content.GetShipModule(TractorModule)?.Stats.GetValueOrDefault("tractor_range", 16.0) ?? 16.0);

    /// <summary>Without a tractor beam a drop is still collected by flying THROUGH it (#1475): the overflow path
    /// floats salvage for beam-less ships too, so there has to be a way to pick it up.</summary>
    private const float TouchCollectRange = 3f;

    /// <summary>Tractor beam: pulls salvage drops within <paramref name="range"/> of the COLLECTING pilot's
    /// ship into that ship's cargo hold (until full). The passive tick uses a short range; a manual pull
    /// (quick-bar) sweeps a wider one. The caller must have pinned the ship cursor to the collector (#994).</summary>
    private void CollectSalvage(SpaceInstance instance, float range, string playerId)
    {
        if (!_ship.HasModule(TractorModule))
        {
            range = System.Math.Min(range, TouchCollectRange); // no beam: only what the hull touches (#1475)
        }

        var shipPos = PilotPositionIn(instance, playerId); // #994: sweep around the collector's own ship
        bool changed = false;
        foreach (var drop in instance.Entities.Where(e => IsCollectableDrop(e.Kind)).ToList())
        {
            if (drop.Position.DistanceSquared(shipPos) > range * range)
            {
                continue;
            }

            if (StowDrop(instance, drop, FindSessionByPlayerId(playerId)))
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
    /// is emptied. Returns true if anything was stowed (cargo full ⇒ false, loot stays floating). With a
    /// <paramref name="collector"/> the stowed amounts are announced (#1317).</summary>
    private bool StowDrop(SpaceInstance instance, CombatEntity drop, PlayerSession? collector = null)
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
                if (collector is not null)
                {
                    QueueLootToast(collector, item.Item, toBackpack: 0, toCargo: item.Count - notStowed);
                }
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
            if (drop.Kind == CombatEntityKind.SalvageCapsule)
            {
                OnSalvageCapsuleCollected(drop); // #2354: paid once per galaxy
            }
        }

        return stowed;
    }

    /// <summary>What the tractor pulls in and a hull collects by flying through: salvage drops and the debris fields'
    /// sealed capsules (#2353).</summary>
    private static bool IsCollectableDrop(CombatEntityKind kind)
        => kind is CombatEntityKind.ResourceDrop or CombatEntityKind.SalvageCapsule;

    private const double LootToastCooldown = 1.5; // one "+n Ore → cargo hold" toast per burst of fragments (#1317)

    /// <summary>Queues a "+n Item → destination" line for the player (#1317) and sends it right away unless a
    /// toast went out within <see cref="LootToastCooldown"/> — then it merges into the pending batch, which
    /// <see cref="FlushLootToast"/> sends from the space tick. A burst of asteroid fragments reads as one
    /// summed line instead of a dozen flashes.</summary>
    private void QueueLootToast(PlayerSession session, string item, int toBackpack, int toCargo)
    {
        if (toBackpack <= 0 && toCargo <= 0)
        {
            return; // nothing banked (full hold): the existing "full" warning speaks, no misleading "+n"
        }

        var pending = session.PendingLootToast;
        int at = pending.FindIndex(p => p.Item == item);
        if (at >= 0)
        {
            var cur = pending[at];
            pending[at] = (item, cur.ToBackpack + toBackpack, cur.ToCargo + toCargo);
        }
        else
        {
            pending.Add((item, toBackpack, toCargo));
        }

        FlushLootToast(session);
    }

    /// <summary>Sends the pending loot line once the cooldown allows: one <c>@srv.space.loot_to_*</c> token whose
    /// <c>{name}</c> argument is the summed "+12 Iron ore, +3 Copper ore" list, localized server-side.</summary>
    private void FlushLootToast(PlayerSession session)
    {
        if (session.PendingLootToast.Count == 0 || _uptime < session.NextLootToastAt)
        {
            return;
        }

        bool anyBackpack = session.PendingLootToast.Any(p => p.ToBackpack > 0);
        bool anyCargo = session.PendingLootToast.Any(p => p.ToCargo > 0);
        string key = anyBackpack && anyCargo ? "srv.space.loot_to_both"
            : anyCargo ? "srv.space.loot_to_cargo"
            : "srv.space.loot_to_backpack";
        string list = string.Join(", ", session.PendingLootToast
            .Select(p => $"+{p.ToBackpack + p.ToCargo} {ItemDisplayName(session, p.Item)}"));
        session.PendingLootToast.Clear();
        session.NextLootToastAt = _uptime + LootToastCooldown;
        Send(session, new ServerMessage { Text = "@" + key + ":" + list });
    }

    private const float TractorPullRange = 30f; // a manual quick-bar tractor sweep reaches further than the passive pull
    private const float TractorReach = 60f;     // an AIMED (auto-locked) drop pulls in from as far as the starter laser reaches (ship_laser_basic weapon_range 60, #2284)

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
            var drop = instance.Entities.FirstOrDefault(e => e.Id == targetId && IsCollectableDrop(e.Kind));
            if (drop is null)
            {
                return; // already collected / gone — no need to nag
            }

            if (drop.Position.DistanceSquared(PilotPositionIn(instance, playerId)) > TractorReach * TractorReach)
            {
                RejectSpace(session, Localize(session?.Locale ?? "en", "space.tractor.out_of_range"));
                return;
            }

            if (!StowDrop(instance, drop, session))
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

    /// <summary>#2428: the farthest a ship pose may lie from the instance origin on any axis. The flight clamp a client
    /// enforces is the system's reach plus the station hulls — a few thousand units; a hundred thousand leaves every
    /// legitimate flight room and still cuts off the overflow range (2.1e8 and up) long before the HUD can choke on it.</summary>
    public const float ShipPoseSanityBound = 100_000f;

    private const double BadPoseLogInterval = 30.0;
    private readonly Dictionary<string, double> _badPoseLoggedAt = new();

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

        // #2428: a pose is relayed to every pilot in the instance, and a client gone astray (an overflow, a hack) used to
        // push a position like 5e8 to all of them — everyone who locked that ship then crashed on the distance readout.
        // The flight scene is a few thousand units across; anything past the sanity bound is dropped and logged once in
        // a while per player, so the server log names the client that spins.
        if (System.Math.Abs(x) > ShipPoseSanityBound || System.Math.Abs(y) > ShipPoseSanityBound || System.Math.Abs(z) > ShipPoseSanityBound)
        {
            if (!_badPoseLoggedAt.TryGetValue(playerId, out double at) || _uptime - at > BadPoseLogInterval)
            {
                _badPoseLoggedAt[playerId] = _uptime;
                _log.Warn($"Rejected ship pose of '{playerId}' at ({x:0}, {y:0}, {z:0}) — beyond the {ShipPoseSanityBound:0} unit sanity bound of instance '{instanceId}'.");
            }

            return;
        }

        var pos = new Vector3f(x, y, z);
        instance.ShipPosition = pos; // shared, for collision (the acting player's ship)

        // Per-player pose for visibility — so the others in this instance can render this ship / EVA suit.
        bool eva = FindSessionByPlayerId(playerId)?.State.InEva ?? false;
        instance.PlayerPoses[playerId] = new SpacePlayerPose(pos, yaw, eva);
        if (!eva)
        {
            instance.ShipPoses[playerId] = new SpacePlayerPose(pos, yaw, false); // #2118: the ship itself
        }

        CheckSpaceWreckApproach(instance, playerId, pos); // #1664: flying up to a derelict "visits" it
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

        // #1530: the loop bodies may close an instance / change a roster, so they iterate COPIES — reused scratch
        // lists instead of three fresh ToList() allocations per tick.
        _spaceInstanceScratch.Clear();
        _spaceInstanceScratch.AddRange(_spaceInstances.Values);
        foreach (var instance in _spaceInstanceScratch)
        {
            if (instance.Players.Count == 0)
            {
                continue;
            }

            // Tractor beam: pull nearby salvage drops into the cargo hold (before collision, so the
            // collision bounce doesn't move the ship away from the drop first). Per pilot (#994): each
            // fitted tractor sweeps around its OWN ship into its own cargo, not the shared position.
            _spacePlayerScratch.Clear();
            _spacePlayerScratch.AddRange(instance.Players);
            foreach (var collectorId in _spacePlayerScratch)
            {
                if (FindSessionByPlayerId(collectorId) is { Joined: true } collector)
                {
                    SetCurrent(collector); // module check + cargo below resolve to this pilot's ship
                    CollectSalvage(instance, TractorRange, collectorId);
                    FlushLootToast(collector); // #1317: a batch held back by the cooldown goes out now
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
            _spacePlayerScratch.Clear();
            _spacePlayerScratch.AddRange(instance.Players);
            foreach (var pilotId in _spacePlayerScratch)
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
                // #1530: one pass over the entities answers both the asteroid contact and the incoming fire below
                // (two closures + an enumerator chain per pilot per tick before). The fire sum accumulates in
                // double like Enumerable.Sum(float) did.
                bool hitAsteroid = false;
                bool inDebrisField = false;
                double incomingSum = 0.0;
                foreach (var e in instance.Entities)
                {
                    float dsq = e.Position.DistanceSquared(pose.Pos);
                    if (e.Kind == CombatEntityKind.Asteroid && dsq <= ShipCollisionRadius * ShipCollisionRadius)
                    {
                        hitAsteroid = true;
                    }
                    else if (e.Kind == CombatEntityKind.DebrisField && !inDebrisField && InsideDebrisField(e, pose.Pos))
                    {
                        inDebrisField = true; // #2355: the rubble taps the shield below
                    }

                    if (e.Hostile && dsq <= ShipEngageRange * ShipEngageRange)
                    {
                        incomingSum += e.DamagePerSecond;
                    }
                }

                TickDebrisBump(pilot, sim, pose, speed, inDebrisField, dt);
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
                float incoming = (float)incomingSum;
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

    private readonly List<SpaceInstance> _spaceInstanceScratch = new();
    private readonly List<string> _spacePlayerScratch = new();

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

    private const double SpottedCalloutCooldown = 15.0; // s between "hostile spotted you" warnings per pilot (#2285)

    /// <summary>Raises a one-shot "a hostile has spotted you" warning to the pilot a hostile hunts the moment that
    /// pilot's ship first enters its aggro range — for ALL AI-core tiers (the older
    /// <see cref="ShipAiThreatCallout"/> only fires once damage lands, and only on a Mk2+ core). A short
    /// per-pilot cooldown keeps a pack that arrives together from raising one warning per ship.</summary>
    private void AnnounceHostileSpotting(SpaceInstance instance)
    {
        // #2285: a hostile spots ITS target pilot (picked in MoveSpaceHostiles), and only that pilot is warned. Before a
        // pilot has a pose the hostile hunts the shared position, and every pilot is warned as before.
        _spottedPilotScratch.Clear();
        bool spottedShared = false;
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

            float distSq = e.Position.DistanceSquared(ChasePosition(instance, e));
            if (distSq <= aggro * aggro)
            {
                if (!e.Spotted)
                {
                    e.Spotted = true;
                    if (e.ChaseTargetId.Length > 0)
                    {
                        _spottedPilotScratch.Add(e.ChaseTargetId);
                    }
                    else
                    {
                        spottedShared = true;
                    }
                }
            }
            else if (distSq > aggro * aggro * 1.21f)
            {
                e.Spotted = false; // lost the ship (with ~10% hysteresis) — a fresh approach warns again
            }
        }

        if (_spottedPilotScratch.Count == 0 && !spottedShared)
        {
            return;
        }

        foreach (var playerId in instance.Players)
        {
            if (!spottedShared && !_spottedPilotScratch.Contains(playerId))
            {
                continue;
            }

            if (instance.SpottedReadyAt.TryGetValue(playerId, out var readyAt) && _uptime < readyAt)
            {
                continue; // this pilot was warned moments ago — a pack arriving together raises one warning
            }

            instance.SpottedReadyAt[playerId] = _uptime + SpottedCalloutCooldown;
            if (FindSessionByPlayerId(playerId) is { } s)
            {
                SendVegaLine(s, "vega.sys.spotted", 3);
            }
        }
    }

    private readonly HashSet<string> _spottedPilotScratch = new();

    /// <summary>Seconds a space hostile keeps its target pilot before it looks for a nearer one (#2285).</summary>
    private const double ChaseStickSeconds = 3.0;

    /// <summary>A hostile only switches to another pilot who is clearly nearer than its current one — this fraction of
    /// the current distance or less (#2285). Two pilots flying side by side never make it flip back and forth.</summary>
    private const float ChaseSwitchRatio = 0.7f;

    /// <summary>#2285: picks (and keeps) the pilot a space hostile hunts — the nearest pilot in the instance, sticky for
    /// <see cref="ChaseStickSeconds"/> and then switched only to one clearly nearer (<see cref="ChaseSwitchRatio"/>).
    /// Returns that pilot's own position. With a single pilot this is simply that pilot's ship, exactly the position the
    /// shared <see cref="SpaceInstance.ShipPosition"/> held; before any pilot has a pose it falls back to that field.</summary>
    private Vector3f PickChaseTarget(SpaceInstance instance, CombatEntity e)
    {
        SpacePlayerPose current = default;
        bool hasCurrent = e.ChaseTargetId.Length > 0
            && instance.Players.Contains(e.ChaseTargetId)
            && instance.PlayerPoses.TryGetValue(e.ChaseTargetId, out current);
        if (hasCurrent && _uptime < e.ChaseRetargetAt)
        {
            return current.Pos;
        }

        string nearestId = string.Empty;
        float nearestSq = float.MaxValue;
        Vector3f nearestPos = default;
        foreach (var pilotId in instance.Players)
        {
            if (!instance.PlayerPoses.TryGetValue(pilotId, out var pose))
            {
                continue; // no pose yet — the client reports one within its first ~0.1 s in space
            }

            float dsq = pose.Pos.DistanceSquared(e.Position);
            if (dsq < nearestSq)
            {
                nearestSq = dsq;
                nearestId = pilotId;
                nearestPos = pose.Pos;
            }
        }

        if (nearestId.Length == 0)
        {
            e.ChaseTargetId = string.Empty;
            return instance.ShipPosition; // nobody has a pose yet — hunt the shared position, as before
        }

        e.ChaseRetargetAt = _uptime + ChaseStickSeconds;
        if (hasCurrent && nearestSq >= current.Pos.DistanceSquared(e.Position) * (ChaseSwitchRatio * ChaseSwitchRatio))
        {
            return current.Pos; // the other pilot is not clearly nearer — keep the hunt
        }

        if (nearestId != e.ChaseTargetId)
        {
            e.ChaseTargetId = nearestId;
            e.Spotted = false; // a new quarry: spotting it warns that pilot afresh
        }

        return nearestPos;
    }

    /// <summary>The position a space hostile currently hunts, without re-picking (#2285): its target pilot's pose, or the
    /// shared <see cref="SpaceInstance.ShipPosition"/> while it has none.</summary>
    private static Vector3f ChasePosition(SpaceInstance instance, CombatEntity e)
        => e.ChaseTargetId.Length > 0
           && instance.Players.Contains(e.ChaseTargetId)
           && instance.PlayerPoses.TryGetValue(e.ChaseTargetId, out var pose)
            ? pose.Pos
            : instance.ShipPosition;

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

            // #2285: chase its OWN target pilot, not the shared last-writer-wins position.
            var quarry = PickChaseTarget(instance, e);
            float dx = quarry.X - e.Position.X;
            float dy = quarry.Y - e.Position.Y;
            float dz = quarry.Z - e.Position.Z;
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
            FaceCourse(e, tx, tz); // #2357: a raider's voxel hull turns toward its course
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
        // #2357: the defeated pilot's OWN hull — the ship cursor points at them. The first "ship" structure used to do,
        // but an NPC trader's or a raider's ownerless hull (kind "ship" too) can come first in the dictionary.
        string currentId = _current?.State.PlayerId ?? string.Empty;
        string shipOwnerId = (instance.Structures.TryGetValue("ship:" + currentId, out var ownHull)
                ? ownHull
                : instance.Structures.Values.FirstOrDefault(s => s.Kind == "ship" && s.OwnerId.Length > 0))?.OwnerId
            ?? string.Empty;

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

            string reason = keepShip ? "@srv.space.ship_disabled" : "@srv.space.ship_destroyed";
            Send(session, new SpaceClosed { Reason = reason, ShipDisabled = true });

            // Put the pilot back on their ship's world for real (#1945). Two lines used to do it — position and
            // the aboard flag — which is no world change at all: the client stayed on the world it had last been
            // told about (the ship interior after a walkabout), dropped the arriving chunks of the planet it now
            // stood on and hovered there with no ground and no ship, since the keep branch never re-parked one.
            // This is the same transition a death does, minus the death: park the ship, land on its heal tank,
            // send the WorldReset + RespawnNotice and everything the client drops with them.
            // The wreck has to stand on its owner's pad: the repair flow needs a placed own-ship structure, and
            // the medbay survives the carving, so the heal-tank landing still works.
            RecoverToShip(session, reason, salvaged: false, died: false,
                parkShip: !keepShip && playerId == shipOwnerId);

            SendShipCombatStatus(session);
            if (!keepShip && playerId == shipOwnerId)
            {
                SendShipRepairStatus(session); // show the repair job immediately
            }
        }

        instance.Players.Clear();
        StashFloatingSalvage(instance); // #1475
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

    /// <summary>#1475: salvage drops still floating when a space instance is torn down (last pilot landed, ship
    /// disabled), keyed by instance id, and re-added the next time that space is created — so uncollected ore
    /// survives a landing instead of dying with the instance. In-memory only: a server restart still loses
    /// it, which the issue accepts (no save-shape change).</summary>
    private readonly Dictionary<string, List<CombatEntity>> _floatingSalvage = new();

    private void StashFloatingSalvage(SpaceInstance instance)
    {
        var drops = instance.Entities.Where(e => e.Kind == CombatEntityKind.ResourceDrop && e.Loot.Count > 0).ToList();
        if (drops.Count == 0)
        {
            _floatingSalvage.Remove(instance.Id);
            return;
        }

        _floatingSalvage[instance.Id] = drops;
    }

    private void RestoreFloatingSalvage(SpaceInstance instance)
    {
        if (_floatingSalvage.Remove(instance.Id, out var drops))
        {
            instance.Entities.AddRange(drops);
        }
    }

    /// <summary>Test/inspection: salvage drops parked for a torn-down instance (#1475).</summary>
    public int FloatingSalvageParkedForTest(string instanceId)
        => _floatingSalvage.TryGetValue(instanceId, out var drops) ? drops.Count : 0;

    private NetCombatEntity ToNet(CombatEntity e) => new()
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
        Staggered = IsStaggered(e), // #2278: a pushed machine or bandit is dazed for a moment
        Yaw = e.Yaw,                // #2357: the raider's heading for its voxel hull
    };

    /// <summary>#2440: how far to the side the n-th ship launches into an orbit that already holds <paramref name="shipsAhead"/>
    /// others — 8 units, alternating sides (8, −8, 16, −16 …), so two friends lifting off together never sit inside each
    /// other at the launch point. 0 for the first ship, which keeps the classic launch column.</summary>
    public static float LaunchOffsetFor(int shipsAhead)
        => shipsAhead <= 0 ? 0f : (shipsAhead % 2 == 1 ? 1f : -1f) * 8f * ((shipsAhead + 1) / 2);

    private void SendSpaceState(PlayerSession session, SpaceInstance instance, bool skipLaunch = false, bool hyperjump = false,
        SpacePlayerPose? resume = null, bool wormhole = false)
    {
        var (systemName, bodyName) = LocationNamesFor(session.CurrentLocationId); // #1565: the flight names where it is

        // #2440: a fresh launch (no resume pose) into an orbit with other ships rises beside them — the offset rides the
        // resume fields, which the flight view reads as a launch column when SkipLaunch is off. Parked ships count too.
        float launchX = 0f;
        bool launchBeside = false;
        if (!resume.HasValue && !skipLaunch)
        {
            int ahead = instance.Players.Count(p => p != session.State.PlayerId)
                + _inShipInterior.Count(kv => kv.Key != session.State.PlayerId && kv.Value.InstanceId == instance.Id && !kv.Value.Ship.Eva);
            launchX = LaunchOffsetFor(ahead);
            launchBeside = launchX != 0f;
        }

        Send(session, new SpaceState
        {
            InstanceId = instance.Id,
            Kind = instance.Kind,
            Entities = instance.Entities.Select(ToNet).ToArray(),
            SkipLaunch = skipLaunch,
            Hyperjump = hyperjump,
            HasResumePose = resume.HasValue || launchBeside, // #2118: where the ship floated before the flight view closed; #2440: the launch column
            ResumeX = resume?.Pos.X ?? launchX,
            ResumeY = resume?.Pos.Y ?? 0f,
            ResumeZ = resume?.Pos.Z ?? 0f,
            ResumeYaw = resume?.Yaw ?? 0f,
            // Automatic landed-ship transit (#1614): the client plays each stage hands-off and reports it, and the
            // landing descent heads for the destination instead of whatever lies ahead of the nose.
            AutomaticTransit = session.AutomaticTransit,
            TransitDestinationBodyId = session.AutomaticTransit ? session.PendingTransitBodyId ?? string.Empty : string.Empty,
            SystemName = systemName,
            BodyName = bodyName,
            // Other real pilots PLUS the peaceful NPC traders out here — both ride the flight view's
            // remote-ship render path (their voxel hull arrives via a "ship_remote" SpaceShipDesign).
            Players = AppendTraderPoses(OtherPlayersInSpace(session.State.PlayerId, instance), instance),
            ScannedIds = ScannedIdsIn(instance, session), // #2238: what this pilot's scanner has already read here
            Wormhole = wormhole,                         // #2242: arrived through a rift, not a warp
        });
    }

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

        // #2431: the ships of pilots who are walking inside them float parked where they were left — they used to vanish
        // for everyone the moment the pilot left the helm (the walkabout takes the player out of the instance). The parked
        // pose is the one the return trip uses (#2118), so the hull sits exactly where its owner will take the helm again.
        foreach (var kv in _inShipInterior)
        {
            if (kv.Key == recipientId || kv.Value.InstanceId != instance.Id || kv.Value.Ship.Eva)
            {
                continue;
            }

            var owner = FindSessionByPlayerId(kv.Key);
            if (owner is not { Joined: true })
            {
                continue; // a walkabout whose owner is gone leaves no ship behind
            }

            (others ??= new List<NetSpacePlayer>()).Add(new NetSpacePlayer
            {
                PlayerId = kv.Key,
                Name = owner.State.Name,
                X = kv.Value.Ship.Pos.X,
                Y = kv.Value.Ship.Pos.Y,
                Z = kv.Value.Ship.Pos.Z,
                Yaw = kv.Value.Ship.Yaw,
                Eva = false,
                Hull = owner.HullColor,
                Parked = true,
            });
        }

        return others is null ? System.Array.Empty<NetSpacePlayer>() : others.ToArray();
    }

    /// <summary>#2431: whether any joined pilot's ship floats parked in <paramref name="instance"/> — such an instance is
    /// kept loaded without pilots, so the hull is still there (and still seen) when the next pilot arrives.</summary>
    private bool AnyParkedShipIn(SpaceInstance instance)
    {
        foreach (var kv in _inShipInterior)
        {
            if (kv.Value.InstanceId == instance.Id && !kv.Value.Ship.Eva && FindSessionByPlayerId(kv.Key) is { Joined: true })
            {
                return true;
            }
        }

        return false;
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
        ClearTransit(session); // a launch from the helm is never a transit (#1614)
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
    /// <summary>Removes a fitted module from the active ship (#1269 — Marcel's call: uninstall with salvage,
    /// no transfer between ships yet). Same gates as building (aboard, workshop); the hull essentials, the walk-up
    /// stations and the basic hold stay (<see cref="ShipModuleDefinition.Removable"/>); a cargo expansion only
    /// comes out when the hold still fits every stack without it — ResizeCargo would silently drop the rest.
    /// Parts come back at <see cref="ShipModuleDefinition.SalvageRate"/> (per item, rounded down) into the
    /// backpack/hold pool; nothing is refunded in free-crafting worlds (nothing was paid either).</summary>
    private void HandleUninstallModule(PlayerSession session, UninstallShipModuleIntent intent)
    {
        var p = session.State;
        var module = _content.GetShipModule(intent.ModuleKey);
        if (module is null)
        {
            Reject(session, "uninstall_module", "@srv.module.unknown");
            return;
        }

        if (!_ship.HasModule(module.Key))
        {
            Reject(session, "uninstall_module", "@srv.module.not_fitted");
            return;
        }

        if (!module.Removable)
        {
            Reject(session, "uninstall_module", "@srv.module.not_removable");
            return;
        }

        if (!p.AboardShip)
        {
            Reject(session, "uninstall_module", "@srv.module.aboard");
            return;
        }

        if (!_ship.HasModule("workshop"))
        {
            Reject(session, "uninstall_module", "@srv.module.workshop");
            return;
        }

        if (module.Stats.TryGetValue("cargo_slots", out var lostSlots) && lostSlots > 0)
        {
            int remaining = System.Math.Max(1, _ship.Cargo.SlotCount - (int)lostSlots);
            int used = 0;
            for (int i = 0; i < _ship.Cargo.SlotCount; i++)
            {
                if (_ship.Cargo.Slots[i] is { IsEmpty: false })
                {
                    used++;
                }
            }

            if (used > remaining)
            {
                Send(session, new ServerMessage
                {
                    Text = Localize(session.Locale, "srv.module.hold_in_use").Replace("{count}", (used - remaining).ToString()),
                });
                return;
            }
        }

        bool free = !Rules.CraftingCostsMaterialsFor(p.ModeOverride) || p.InstantBuild;
        _ship.Modules.Remove(module.Key);

        int recovered = 0;
        MaterialPool? pool = null;
        if (!free)
        {
            pool = new MaterialPool(_content, p, _ship);
            foreach (var part in module.BuildCost)
            {
                int back = (int)System.Math.Floor(part.Count * ShipModuleDefinition.SalvageRate);
                if (back > 0)
                {
                    pool.Add(part.Item, back);
                    recovered += back;
                }
            }
        }

        ResizeCargo(_ship);
        RecomputeShipCombatStats();

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.module.removed")
                .Replace("{name}", LocalizedName(session.Locale, module.NameKey, module.Key))
                .Replace("{count}", recovered.ToString()),
        });
        SendInventory(session);
        if (pool is not null)
        {
            WarnIfPoolOverflowed(session, pool); // #600: salvaged parts that found no room are gone — say so
        }

        SendShipCombatStatus(session);
        SendPlayerState(session); // AiCoreTier / weapons may have changed
        BroadcastOwnedShips();
    }

    /// <summary>Test entrypoint: removes a fitted module (mirrors <see cref="HandleUninstallModule"/>). Returns
    /// whether the module is gone afterwards.</summary>
    public bool UninstallModuleForTest(string playerId, string moduleKey)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return false;
        }

        SetCurrent(s);
        bool fitted = _ship.HasModule(moduleKey);
        HandleUninstallModule(s, new UninstallShipModuleIntent { ModuleKey = moduleKey });
        return fitted && !_ship.HasModule(moduleKey); // "removed" — a module that was never aboard reports false
    }

    /// <summary>Test entrypoint: builds a ship module for a player (mirrors <see cref="HandleBuildModule"/>;
    /// the player must be aboard a ship with a workshop). Returns whether the module is fitted afterwards.</summary>
    public bool BuildModuleForTest(string playerId, string moduleKey)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return false;
        }

        SetCurrent(s);
        HandleBuildModule(s, new BuildShipModuleIntent { ModuleKey = moduleKey });
        return _ship.HasModule(moduleKey);
    }

    public void HandleEnterSpaceForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleEnterSpace(s);
        }
    }

    private void HandleHyperjumpSystem(PlayerSession session, HyperjumpSystemIntent intent)
        => HyperjumpToSystem(session.State.PlayerId, intent.SystemId, takeOffFirst: true);

    /// <summary>Hyperjumps into a (possibly never-visited) star system, arriving in FLIGHT mode in that
    /// system's space rather than landing — the way to reach a system whose bodies you can't yet see on the
    /// travel screen. Needs a jump generator; from there you fly to its worlds and land manually. Also the
    /// test/util entrypoint. <paramref name="takeOffFirst"/> (#1614) is the pilot's own jump from the map: a parked
    /// ship plays its take-off before the warp, as an automatic transit. The default jumps at once — the tests and
    /// tools use it to put a pilot into a system's flight.</summary>
    public void HyperjumpToSystem(string playerId, string systemId, bool takeOffFirst = false)
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

        // #1614: from a parked ship the take-off plays first, in the system you leave — the warp and the arrival in
        // flight follow once the client reports the take-off (AdvanceTransit). An observer has no ship to launch.
        if (takeOffFirst && !InSpace(playerId) && !session.Spectating)
        {
            BeginTransit(session, destinationBodyId: null, system.Id, padIndex: -1);
            return;
        }

        // Arrive in flight anchored on the system's first landable body (the flight instance is keyed there);
        // you fly to its worlds and land manually from there.
        ClearTransit(session); // a jump the pilot starts in flight replaces any transit still pending
        var anchor = system.Bodies.FirstOrDefault(b => !string.IsNullOrEmpty(b.PlanetType)) ?? system.Bodies[0];
        JumpFlightToSystem(session, system, anchor);
        TellArrivedInFlight(session, system, anchor);
    }

    /// <summary>The chat line for a jump that arrives in flight. The landing path says where you arrived; the in-flight
    /// arrival said nothing, so the chat scrollback never told the pilot the jump had happened at all (#1565).</summary>
    private void TellArrivedInFlight(PlayerSession session, StarSystem system, CelestialBody anchor)
    {
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.travel.hyperjumped").Replace("{system}", system.Name).Replace("{planet}", anchor.Name),
        });
        _log.Info($"Player '{session.State.Name}' hyperjumped into system '{system.Name}' (flight).");
    }

    /// <summary>Moves a player's flight into <paramref name="system"/> — the warp itself, past every gate: the
    /// current flight instance closes, the ship and the pilot are re-homed on <paramref name="anchor"/> (the flight
    /// instance of the new system is keyed there) and a flight state with the warp flag opens. Used by the star-map
    /// jump and by the warp stage of an automatic transit (#1614), which anchors on its destination so the landing
    /// descent that follows heads for it.</summary>
    private void JumpFlightToSystem(PlayerSession session, StarSystem system, CelestialBody anchor)
    {
        string playerId = session.State.PlayerId;
        var origin = _galaxy?.FindBody(session.CurrentLocationId);
        Serve(session); // _ship = this player's ship

        bool wasLanded = !InSpace(playerId);
        // Launching off a surface? Remove the parked ship from the OLD world before we switch systems.
        if (wasLanded && SetActiveWorld(session.CurrentLocationId))
        {
            RemoveLandedShip(session);
        }

        LeaveSpace(playerId); // tear down any current flight instance (no-op on a surface)

        session.CurrentLocationId = anchor.Id;
        if (origin is not null && origin.Id != anchor.Id)
        {
            OnMarkerOwnerLeftWorld(session, origin.Id, disconnected: false); // the players left behind lose this pilot's pings (#1293)
        }

        // #1679: the pad claim belongs to the body we just left. Carried across, it silently reserved that
        // index on the ANCHOR body (PadOccupiedByOther matches index + location) and PlayerPad then trusted it,
        // stamping the first landing on a pad the pilot never claimed — including one a trader was parked on.
        // A pilot arriving in flight holds no pad; they claim one when they land, like every other arrival.
        session.AssignedPadIndex = -1;
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

        EnterSpace(playerId, skipLaunch: true, hyperjump: true); // warp in; a parked ship took off before (#1614)
        SendStarMap(session); // refresh the travel screen with the now-known system
    }

    /// <summary>#1614: re-sends the flight state of a pilot whose automatic transit ended without leaving the flight
    /// (a refused landing) — the client re-reads the transit flag and hands the controls back.</summary>
    private void ResendSpaceState(PlayerSession session)
    {
        if (_playerInstance.TryGetValue(session.State.PlayerId, out var instanceId)
            && _spaceInstances.TryGetValue(instanceId, out var instance))
        {
            SendSpaceState(session, instance, skipLaunch: true);
        }
    }

    /// <summary>Test/util entry: leave space and land on a specific body (system-scale flight landing).</summary>
    public void LandOnBody(string playerId, string destinationBodyId, int padIndex = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleLeaveSpace(session, new LeaveSpaceIntent { DestinationBodyId = destinationBodyId, PadIndex = padIndex });
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
            // #1566: the "current body" of a pilot who hyperjumped in flight is the target system's anchor —
            // a world that was never loaded (the jump keys the flight instance there without landing). The
            // relocate path below assumes a resident world: SetActiveWorld failed silently, the cursor stayed
            // on the planet he had LEFT, and he was set down on that old terrain while every id said the new
            // body — "landed on a known world that suddenly looked different". Land it like a fresh arrival.
            // #2117: the same when the client was last told about ANOTHER world (a visit to the ship interior,
            // a station, …): the relocate below sends no WorldReset, so the client would drop every chunk of
            // this body as "the world we just left" and stand on nothing but its own ship.
            if (!_worlds.IsLoaded(session.CurrentLocationId) || session.AnnouncedWorldId != WorldIdOf(session.CurrentLocationId))
            {
                HandleTravel(session, new TravelIntent { DestinationBodyId = session.CurrentLocationId, PadIndex = intent.PadIndex }, quickTravel: false, allowCurrentBody: true);
                return;
            }

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

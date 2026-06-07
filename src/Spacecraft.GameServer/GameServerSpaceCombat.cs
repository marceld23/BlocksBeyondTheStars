using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;

namespace Spacecraft.GameServer;

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
    ResourceDrop,
}

/// <summary>A server-authoritative combat entity (space object or planet enemy).</summary>
public sealed class CombatEntity
{
    public string Id { get; set; } = string.Empty;
    public CombatEntityKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Hostile { get; set; }

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
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public Vector3f Position { get; set; }

    /// <summary>Damage this hostile deals to the ship/player per second while engaged.</summary>
    public float DamagePerSecond { get; set; }

    /// <summary>What this entity drops when destroyed.</summary>
    public List<ItemAmount> Loot { get; set; } = new();
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

    /// <summary>Counts up while the asteroid field is below its target so mined-out fields slowly replenish.</summary>
    public double AsteroidRespawnTimer { get; set; }

    /// <summary>Spreads successive respawned asteroids so they don't stack on one spot.</summary>
    public int AsteroidSpawnRotor { get; set; }
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
        float shield = design?.BaseShield ?? 0f;
        float regen = 0f;
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
    }

    // ---------------- Enter / leave space ----------------

    /// <summary>Launches the player into a space instance around the ship's location.</summary>
    public void EnterSpace(string playerId, bool skipLaunch = false)
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

        instance.Players.Add(playerId);
        _playerInstance[playerId] = instanceId;

        if (session is not null)
        {
            SendSpaceState(session, instance, skipLaunch);
            SendShipCombatStatus(session);
            SendStarMap(session); // the space view needs the system's bodies to render + land on them
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
            instance.Entities.Add(MakeAsteroid(LargeAsteroidTier,
                new Vector3f(rad * (float)System.Math.Cos(ang), (i - 1) * 9f, rad * (float)System.Math.Sin(ang))));
        }

        AddStationContacts(instance);

        // Hostile NPC drones only when space combat is enabled and NPC enemies are switched on.
        bool combatEnabled = Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both;
        if (combatEnabled)
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
                    Hull = 70f,
                    HullMax = 70f,
                    Position = new Vector3f(-170f, 14f, 150f),
                    DamagePerSecond = 8f,
                    Loot = { new ItemAmount("data_fragment", 3) },
                });
            }
        }

        return instance;
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
        if (target.Hull > 0f)
        {
            BroadcastSpaceState(instance);
            return;
        }

        // Destroyed. A large/medium asteroid splits into smaller chunks instead of dropping loot;
        // only the smallest asteroids (and other entities) yield resources.
        instance.Entities.Remove(target);
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
    private const float ShipCollisionMaxDamage = 50f;
    // Hostiles only fire on the ship once they're within engagement range — so a distant drone can't plink
    // you forever (which read as the ship being shaken + flashing red with no visible attacker), and flying
    // clear of the fight actually stops the damage and lets the shield recharge.
    private const float ShipEngageRange = 70f;

    private const string TractorModule = "tractor_beam";
    private const float TractorRange = 8f;

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
            bool hitAsteroid = instance.Entities.Any(e => e.Kind == CombatEntityKind.Asteroid
                && e.Position.DistanceSquared(instance.ShipPosition) <= ShipCollisionRadius * ShipCollisionRadius);
            if (hitAsteroid && speed > ShipCollisionMinSpeed)
            {
                ApplyShipDamage(System.Math.Min(ShipCollisionMaxDamage, speed * ShipCollisionDamageFactor));
                instance.ShipPosition = instance.ShipLastPosition; // bounce back / stop at the impact
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

            instance.ShipLastPosition = instance.ShipPosition;

            float incoming = instance.Entities
                .Where(e => e.Hostile && e.Position.DistanceSquared(instance.ShipPosition) <= ShipEngageRange * ShipEngageRange)
                .Sum(e => e.DamagePerSecond);
            if (incoming > 0f)
            {
                ApplyShipDamage((float)(incoming * dt));
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
        instance.Entities.Add(MakeAsteroid(LargeAsteroidTier, pos));
        BroadcastSpaceState(instance);
    }

    /// <summary>Damage hits the shield first, then the hull.</summary>
    private void ApplyShipDamage(float amount)
    {
        float toShield = System.Math.Min(_ship.Shield, amount);
        _ship.Shield -= toShield;
        amount -= toShield;
        if (amount > 0f)
        {
            _ship.Hull = System.Math.Max(0f, _ship.Hull - amount);
        }
    }

    /// <summary>
    /// The ship was defeated. Per §8.5 there is no permanent loss by default: the ship is
    /// recovered to its base with restored hull, present players respawn at the heal-tank,
    /// and the instance is unloaded.
    /// </summary>
    private void DisableShip(SpaceInstance instance)
    {
        _ship.Hull = _shipHullMax;
        _ship.Shield = 0f;

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
    };

    private void SendSpaceState(PlayerSession session, SpaceInstance instance, bool skipLaunch = false)
        => Send(session, new SpaceState
        {
            InstanceId = instance.Id,
            Kind = instance.Kind,
            Entities = instance.Entities.Select(ToNet).ToArray(),
            SkipLaunch = skipLaunch,
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
            (others ??= new List<NetSpacePlayer>()).Add(new NetSpacePlayer
            {
                PlayerId = kv.Key,
                Name = FindSessionByPlayerId(kv.Key)?.State.Name ?? string.Empty,
                X = pose.Pos.X,
                Y = pose.Pos.Y,
                Z = pose.Pos.Z,
                Yaw = pose.Yaw,
                Eva = pose.Eva,
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

    private void HandleEnterSpace(PlayerSession session) => EnterSpace(session.State.PlayerId);

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
            LeaveSpace(session.State.PlayerId); // land back on the current world
            CheckpointSave("landed (returned to surface)"); // auto-save on landing
            return;
        }

        // Landed on a different body picked while flying — travel there (reuses the per-player travel,
        // which leaves space, loads the destination world and relocates only this player).
        HandleTravel(session, new TravelIntent { DestinationBodyId = dest });
    }

    private void HandleFireWeapon(PlayerSession session, FireWeaponIntent intent)
        => FireWeapon(session.State.PlayerId, intent.WeaponKey, intent.TargetEntityId);
}

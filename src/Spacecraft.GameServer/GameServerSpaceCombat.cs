using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

/// <summary>Kind of combat entity in a space instance or on a planet (`anf_space_flight.md` §8, §10, §12).</summary>
public enum CombatEntityKind
{
    Asteroid,
    Drone,
    Ufo,
    Cruiser,
    Creature,
    AlienMonster,
}

/// <summary>A server-authoritative combat entity (space object or planet enemy).</summary>
public sealed class CombatEntity
{
    public string Id { get; set; } = string.Empty;
    public CombatEntityKind Kind { get; set; }
    public bool Hostile { get; set; }

    /// <summary>For planet fauna: the procedural species id this entity is an instance of.</summary>
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Seconds a (territorial) creature stays provoked after being attacked (hunts + bites back).</summary>
    public double ProvokeTimer { get; set; }
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
}

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
    private float _shipHullMax = BaseHull;
    private float _shipShieldMax;
    private float _shipShieldRegen;

    private readonly record struct WeaponSpec(float Damage, float Range, double Cooldown, bool IsCombat);

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
        foreach (var key in _ship.Modules)
        {
            if (_content.GetShipModule(key) is not { } m)
            {
                continue;
            }

            hull += (float)m.Stats.GetValueOrDefault("hull", 0);
            shield += (float)m.Stats.GetValueOrDefault("shield", 0);
            regen += (float)m.Stats.GetValueOrDefault("shield_regen", 0);
        }

        _shipHullMax = hull;
        _shipShieldMax = shield;
        _shipShieldRegen = regen;

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
    public void EnterSpace(string playerId)
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
            SendSpaceState(session, instance);
            SendShipCombatStatus(session);
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

        // Asteroids are always present as scenery + mining targets; breaking them is gated at fire time.
        int asteroids = 3;
        for (int i = 0; i < asteroids; i++)
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = NextEntityId(),
                Kind = CombatEntityKind.Asteroid,
                Hostile = false,
                Hull = 50f,
                HullMax = 50f,
                Position = new Vector3f(12 + i * 8, 0, 18),
                Loot = { new ItemAmount("iron_ore", 5), new ItemAmount("titanium_ore", 2) },
            });
        }

        // Hostile NPC drones only when space combat is enabled and NPC enemies are switched on.
        bool combatEnabled = Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both;
        if (combatEnabled)
        {
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
                    Position = new Vector3f(-8 - i * 4, 5, 22 + i * 5),
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
                    Position = new Vector3f(0, -6, 30),
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

        if (target.Position.DistanceSquared(Vector3f.Zero) > weapon.Range * weapon.Range)
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

        // Destroyed: award loot to the firing player and remove the entity.
        instance.Entities.Remove(target);
        if (session is not null)
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

            if (weapon.IsCombat && Rules.AsteroidDestruction != AsteroidDestructionMode.WeaponsAllowed)
            {
                reason = "Only mining tools may break asteroids on this server.";
                return false;
            }

            return true;
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

        spec = new WeaponSpec(
            Damage: (float)def.Stats.GetValueOrDefault("weapon_damage", 10),
            Range: (float)def.Stats.GetValueOrDefault("weapon_range", 50),
            Cooldown: def.Stats.GetValueOrDefault("weapon_cooldown", 1.0),
            IsCombat: (int)def.Stats.GetValueOrDefault("weapon_class", 1) == 1);
        return true;
    }

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

            float incoming = instance.Entities.Where(e => e.Hostile).Sum(e => e.DamagePerSecond);
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
        }
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
        Hostile = e.Hostile,
        Hull = e.Hull,
        HullMax = e.HullMax,
        X = e.Position.X,
        Y = e.Position.Y,
        Z = e.Position.Z,
    };

    private void SendSpaceState(PlayerSession session, SpaceInstance instance)
        => Send(session, new SpaceState
        {
            InstanceId = instance.Id,
            Kind = instance.Kind,
            Entities = instance.Entities.Select(ToNet).ToArray(),
        });

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

    private void HandleLeaveSpace(PlayerSession session) => LeaveSpace(session.State.PlayerId);

    private void HandleFireWeapon(PlayerSession session, FireWeaponIntent intent)
        => FireWeapon(session.State.PlayerId, intent.WeaponKey, intent.TargetEntityId);
}

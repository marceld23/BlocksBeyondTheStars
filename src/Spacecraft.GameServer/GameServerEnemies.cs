using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;

namespace Spacecraft.GameServer;

/// <summary>
/// Planet enemies (technical requirements / `anf_space_flight.md` §12). Hostile creatures
/// spawn near players on the surface when the rules allow it, deal proximity damage, and are
/// killed with the held tool/weapon. Disabled in Creative and on peaceful servers (§12.4).
/// </summary>
public sealed partial class GameServer
{
    private const double EnemySpawnInterval = 5.0;
    private const float EnemyProximityRange = 4f;
    private const float EnemyAttackReach = 6f;

    private readonly List<CombatEntity> _planetEnemies = new();
    private double _enemySpawnTimer;

    /// <summary>Hostile creatures currently active on the surface.</summary>
    public IReadOnlyList<CombatEntity> PlanetEnemies => _planetEnemies;

    /// <summary>Whether hostile planet enemies may exist given the active rules.</summary>
    private bool PlanetEnemiesActive => Rules.PlanetEnemies != AlienActivity.Off && Rules.GameMode == GameMode.Survival;

    private void TickEnemies(double dt)
    {
        if (!PlanetEnemiesActive)
        {
            return;
        }

        // Eligible targets: joined players on the surface (outside the ship, not flying in space).
        var targets = _sessions.Values
            .Where(s => s.Joined && !s.State.AboardShip && !InSpace(s.State.PlayerId))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        int cap = ActivityCount(Rules.PlanetEnemies) * targets.Count;
        _enemySpawnTimer += dt;
        if (_enemySpawnTimer >= EnemySpawnInterval && _planetEnemies.Count < cap)
        {
            _enemySpawnTimer = 0;
            SpawnPlanetEnemyNear(targets[_planetEnemies.Count % targets.Count].State);
            BroadcastPlanetEnemies();
        }

        // Proximity damage: each enemy hurts nearby players.
        foreach (var enemy in _planetEnemies)
        {
            foreach (var session in targets)
            {
                var p = session.State;
                if (p.GodMode)
                {
                    continue;
                }

                if (p.Position.DistanceSquared(enemy.Position) <= EnemyProximityRange * EnemyProximityRange)
                {
                    p.Health = System.Math.Max(0f, p.Health - (float)(enemy.DamagePerSecond * dt));
                    SendPlayerState(session);
                    if (p.Health <= 0f)
                    {
                        RespawnPlayer(session, "Overwhelmed by a hostile creature — recovery to the Medbay heal-tank.");
                    }
                }
            }
        }
    }

    private void SpawnPlanetEnemyNear(Shared.State.PlayerState player)
    {
        bool tougher = Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme;
        _planetEnemies.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = tougher ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            Hostile = true,
            Hull = tougher ? 60f : 30f,
            HullMax = tougher ? 60f : 30f,
            Position = new Vector3f(player.Position.X + 3f, player.Position.Y, player.Position.Z),
            DamagePerSecond = tougher ? 6f : 4f,
            Loot = { new ItemAmount("carbon", 2) },
        });
    }

    /// <summary>Player attacks a planet enemy or creature with the held tool/weapon. Server resolves the hit.</summary>
    public void AttackEntity(string playerId, string entityId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (_planetEnemies.FirstOrDefault(e => e.Id == entityId) is { } enemy)
        {
            AttackCombatEntity(session, enemy, _planetEnemies, isCreature: false);
            return;
        }

        if (_creatures.FirstOrDefault(e => e.Id == entityId) is { } creature)
        {
            AttackCombatEntity(session, creature, _creatures, isCreature: true);
            return;
        }

        Reject(session, "attack", "No such target.");
    }

    private void AttackCombatEntity(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature)
    {
        var p = session.State;
        var tool = ActiveTool(p);
        bool isWeapon = tool.Kind == ToolKind.Weapon;

        // A crafted weapon's own range governs reach (melee = short, ranged = long); else default.
        float reach = isWeapon && tool.Range > 0f ? tool.Range : EnemyAttackReach;
        if (p.Position.DistanceSquared(target.Position) > reach * reach)
        {
            Reject(session, "attack", "Target is out of reach.");
            return;
        }

        // Energy weapons (laser/plasma) draw suit energy per shot.
        if (isWeapon && tool.EnergyPerUse > 0f)
        {
            if (p.SuitEnergy < tool.EnergyPerUse)
            {
                Reject(session, "attack", "Not enough suit energy to fire.");
                return;
            }

            p.SuitEnergy -= tool.EnergyPerUse;
            SendPlayerState(session);
        }

        // A crafted weapon uses its own damage; any other tool keeps the tier-scaled fallback.
        float damage = isWeapon
            ? (tool.Damage > 0f ? tool.Damage : 20f + tool.Tier * 15f)
            : 15f + tool.Tier * 10f;
        target.Hull -= damage;

        if (target.Hull > 0f)
        {
            if (isCreature) BroadcastCreatures(); else BroadcastPlanetEnemies();
            return;
        }

        list.Remove(target);
        var pool = new MaterialPool(_content, p, _ship);
        foreach (var drop in target.Loot)
        {
            pool.Add(drop.Item, drop.Count);
        }

        SendInventory(session);
        if (isCreature)
        {
            BroadcastCreatures();
        }
        else
        {
            Broadcast(new PlanetEnemyDefeated { Id = target.Id });
            BroadcastPlanetEnemies();
        }
    }

    private void BroadcastPlanetEnemies()
        => Broadcast(new PlanetEnemyList { Enemies = _planetEnemies.Select(ToNet).ToArray() });

    private void HandleAttackEntity(PlayerSession session, AttackEntityIntent intent)
        => AttackEntity(session.State.PlayerId, intent.EntityId);
}

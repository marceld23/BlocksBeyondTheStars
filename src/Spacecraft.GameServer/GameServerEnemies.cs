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

    private List<CombatEntity> _planetEnemies => _worlds.Active.PlanetEnemies;
    private double _enemySpawnTimer { get => _worlds.Active.EnemySpawnTimer; set => _worlds.Active.EnemySpawnTimer = value; }

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

        // Orbital stations (void worlds) are safe havens: only peaceful NPCs live there, never hostiles.
        if (_world.Planet.Void)
        {
            return;
        }

        // Eligible targets: joined players on the surface (outside the ship, not flying in space).
        var targets = JoinedInActiveWorld()
            .Where(s => !s.State.AboardShip && !InSpace(s.State.PlayerId))
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
                if (p.GodMode || p.Stealthed) // cloaked players aren't detected
                {
                    continue;
                }

                if (WrapDistSq(p.Position, enemy.Position) <= EnemyProximityRange * EnemyProximityRange)
                {
                    p.Health = System.Math.Max(0f, p.Health - Mitigate(p, (float)(enemy.DamagePerSecond * dt)));
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

        // Spawn a comfortable distance away (not right on top of the player — that read as unfairly
        // aggressive) and spread around them, then drop it onto the actual surface at that column so it
        // never spawns buried in the terrain. The golden angle gives an even spread as more spawn.
        int n = _planetEnemies.Count;
        double ang = n * 2.39996323; // golden angle (radians)
        float dist = 9f + (n % 3) * 2f; // 9..13 blocks out
        int ex = (int)System.Math.Round(player.Position.X + System.Math.Cos(ang) * dist);
        int ez = (int)System.Math.Round(player.Position.Z + System.Math.Sin(ang) * dist);
        int ey = _generator.SurfaceHeight(_world.Planet, ex, ez) + 1; // stand on the ground, not in it

        _planetEnemies.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = tougher ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            Hostile = true,
            Hull = tougher ? 60f : 30f,
            HullMax = tougher ? 60f : 30f,
            Position = new Vector3f(ex, ey, ez),
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

    private const double MeleeCooldown = 1.5;                       // melee weapons swing at most this often (B44)
    private readonly Dictionary<string, double> _meleeReadyAt = new(); // playerId → uptime the next melee swing is allowed

    private void AttackCombatEntity(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature)
    {
        var p = session.State;
        var tool = ActiveTool(p);
        bool isWeapon = tool.Kind == ToolKind.Weapon;

        // A weapon swings on a cooldown, so it can't be spammed (B44). The per-weapon cooldown comes from the
        // item (machete = 1.5s); an energy-free melee weapon with no explicit cooldown falls back to the default.
        // Ranged energy weapons without a cooldown are still rate-limited by their suit-energy cost.
        if (isWeapon)
        {
            double cd = tool.CooldownSeconds > 0f ? tool.CooldownSeconds : (tool.EnergyPerUse <= 0f ? MeleeCooldown : 0.0);
            if (cd > 0.0)
            {
                if (_meleeReadyAt.TryGetValue(p.PlayerId, out var readyAt) && _uptime < readyAt)
                {
                    return; // still on cooldown — ignore the swing (no reject spam)
                }

                _meleeReadyAt[p.PlayerId] = _uptime + cd;
            }
        }

        // A ranged weapon's longer reach extends the default; a melee weapon never *reduces* it below the
        // default swing reach (the client targets any creature within EnemyAttackReach, so a short melee
        // range like the machete's must not silently reject those hits — equipping a weapon must never make
        // you worse than bare fists).
        float reach = isWeapon ? System.Math.Max(tool.Range, EnemyAttackReach) : EnemyAttackReach;
        if (WrapDistSq(p.Position, target.Position) > reach * reach)
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
            // A surviving creature that retaliates (territorial / already hostile) is provoked:
            // for a while it hunts and bites back (and a pack-hunter rallies nearby kin).
            if (isCreature)
            {
                ProvokeCreature(target);
            }

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
            BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });
            BroadcastPlanetEnemies();
        }
    }

    private void BroadcastPlanetEnemies()
        => BroadcastToWorld(new PlanetEnemyList { Enemies = _planetEnemies.Select(ToNet).ToArray() });

    private void HandleAttackEntity(PlayerSession session, AttackEntityIntent intent)
        => AttackEntity(session.State.PlayerId, intent.EntityId);
}

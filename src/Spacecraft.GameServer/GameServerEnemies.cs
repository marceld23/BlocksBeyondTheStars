using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;

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

        // Movement + proximity damage: enemies HUNT the nearest detectable player in range, and idly
        // WANDER otherwise (they used to stand rooted at their spawn point forever).
        bool moved = false;
        foreach (var enemy in _planetEnemies)
        {
            moved |= MovePlanetEnemy(enemy, targets, dt);

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

        // Stream the new positions, throttled so a wandering pack doesn't flood the channel.
        _enemySyncTimer += dt;
        if (moved && _enemySyncTimer >= 0.2)
        {
            _enemySyncTimer = 0;
            BroadcastPlanetEnemies();
        }
    }

    private const float EnemyHuntRange = 28f;   // detection radius — inside it the fiend stalks the player
    private const float EnemyStopRange = 1.6f;  // close enough — the proximity aura does the biting
    private const float EnemyHuntSpeed = 3.1f;  // blocks/s while hunting (slightly slower than a running player)
    private const float EnemyToughHuntSpeed = 3.7f;
    private const float EnemyWanderSpeed = 1.1f;

    private double _enemySyncTimer;
    private readonly Dictionary<string, (double Heading, double Until)> _enemyWander = new();

    /// <summary>Moves one planet enemy: hunt the nearest detectable player inside the hunt range, else
    /// wander on a seeded heading that re-rolls every few seconds. Terrain-following (the enemy stands on
    /// the surface at its new column); a cliff step taller than 3 blocks blocks the move and re-rolls the
    /// wander heading. Returns true when the enemy actually moved.</summary>
    private bool MovePlanetEnemy(CombatEntity enemy, List<PlayerSession> targets, double dt)
    {
        // Nearest detectable (non-cloaked, non-god) player.
        PlayerSession nearest = null;
        double bestSq = (double)EnemyHuntRange * EnemyHuntRange;
        foreach (var s in targets)
        {
            if (s.State.GodMode || s.State.Stealthed)
            {
                continue;
            }

            double sq = WrapDistSq(s.State.Position, enemy.Position);
            if (sq < bestSq)
            {
                bestSq = sq;
                nearest = s;
            }
        }

        double dx, dz;
        float speed;
        if (nearest != null)
        {
            if (bestSq <= EnemyStopRange * EnemyStopRange)
            {
                return false; // in biting range — hold position (the aura damages)
            }

            // The short way round both wrap seams toward the player.
            dx = WorldConstants.WrapDeltaX(nearest.State.Position.X - enemy.Position.X, _world.Circumference);
            dz = WorldConstants.WrapDeltaZ(nearest.State.Position.Z - enemy.Position.Z, _world.Circumference);
            double len = System.Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.001)
            {
                return false;
            }

            dx /= len;
            dz /= len;
            speed = enemy.Kind == CombatEntityKind.AlienMonster ? EnemyToughHuntSpeed : EnemyHuntSpeed;
        }
        else
        {
            // Idle wander: keep a heading for a few seconds, then re-roll (seeded — no Random allocs).
            if (!_enemyWander.TryGetValue(enemy.Id, out var w) || _uptime >= w.Until)
            {
                uint h = (uint)Spacecraft.WorldGeneration.WorldGenerator.StableHash(enemy.Id + ":" + (long)(_uptime * 10));
                w = (h % 6283 / 1000.0, _uptime + 3.0 + h % 40 / 10.0); // heading 0..2π, 3..7 s
                _enemyWander[enemy.Id] = w;
            }

            dx = System.Math.Cos(w.Heading);
            dz = System.Math.Sin(w.Heading);
            speed = EnemyWanderSpeed;
        }

        float nx = (float)WorldConstants.WrapX(enemy.Position.X + dx * speed * dt, _world.Circumference);
        float nz = (float)WorldConstants.WrapZ(enemy.Position.Z + dz * speed * dt, _world.Circumference);
        int groundY = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(nx), (int)System.Math.Floor(nz)) + 1;

        if (System.Math.Abs(groundY - enemy.Position.Y) > 3f)
        {
            _enemyWander.Remove(enemy.Id); // cliff/spike in the way — stop and pick a new direction next tick
            return false;
        }

        enemy.Position = new Vector3f(nx, groundY, nz);
        return true;
    }

    private void SpawnPlanetEnemyNear(Shared.State.PlayerState player)
    {
        bool tougher = Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme;

        // Spawn well OUTSIDE the 28-block detection range (9–13 felt like an ambush): fiends appear
        // 35–50 blocks out, roam the area on wander headings, and only start hunting when the player
        // comes near them. Spread around the player with the golden angle, then drop onto the actual
        // surface at that column so they never spawn buried in the terrain.
        int n = _planetEnemies.Count;
        double ang = n * 2.39996323; // golden angle (radians)
        float dist = 35f + (n % 4) * 5f; // 35..50 blocks out — beyond EnemyHuntRange
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
        _enemyWander.Remove(target.Id); // drop the dead enemy's wander state
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

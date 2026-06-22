using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

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
    private readonly List<PlayerSession> _enemyTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private readonly HashSet<string> _wardedPlayers = new();    // reused per tick (companion-ward set)

    /// <summary>Hostile creatures currently active on the surface.</summary>
    public IReadOnlyList<CombatEntity> PlanetEnemies => _planetEnemies;

    /// <summary>Whether hostile planet enemies may exist given the active rules. Once the Guardian core is
    /// destroyed (P6 pacification) no machine spawns anywhere — the galaxy is at peace.</summary>
    private bool PlanetEnemiesActive => Rules.PlanetEnemies != AlienActivity.Off
        && Rules.GameMode == GameMode.Survival
        && !_storyState.GuardianDefeated;

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
        // Reuse a field list instead of allocating a fresh Where(...).ToList() every tick (15 Hz).
        _enemyTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId))
            {
                _enemyTargets.Add(s);
            }
        }

        var targets = _enemyTargets;

        if (targets.Count == 0)
        {
            return;
        }

        int cap = ActivityCount(Rules.PlanetEnemies) * targets.Count;
        _enemySpawnTimer += dt;
        if (_enemySpawnTimer >= EnemySpawnInterval && _planetEnemies.Count < cap)
        {
            _enemySpawnTimer = 0;
            // A fraction (~2 in 5) of the population spawns as the flying scan-drone variant (P4), the rest as
            // walking three-eyed ground robots — both within the same PlanetEnemies cap (count unchanged).
            bool asDrone = Rules.PlanetDrones && (_planetEnemies.Count % 5) < 2;
            SpawnPlanetEnemyNear(targets[_planetEnemies.Count % targets.Count].State, asDrone);
            BroadcastPlanetEnemies();
        }

        // A present tamed companion makes the Guardian machines read its owner as part of the protected
        // biosphere rather than as prey: while one wards a player the machines neither hunt nor bite them.
        // Computed once per tick (consulted by both the proximity-damage pass and MovePlanetEnemy's chase
        // scan). Story: RevealCompanionWardInsight has VEGA explain it the first time a machine stands down.
        var warded = _wardedPlayers;
        warded.Clear();
        foreach (var session in targets)
        {
            if (WardedByCompanion(session.State))
            {
                warded.Add(session.State.PlayerId);
            }
        }

        // Movement + proximity damage: enemies HUNT the nearest detectable player in range, and idly
        // WANDER otherwise (they used to stand rooted at their spawn point forever).
        bool moved = false;
        foreach (var enemy in _planetEnemies)
        {
            moved |= MovePlanetEnemy(enemy, targets, warded, dt);

            foreach (var session in targets)
            {
                var p = session.State;
                if (p.GodMode || p.Stealthed) // cloaked players aren't detected
                {
                    continue;
                }

                if (warded.Contains(p.PlayerId))
                {
                    // A companion shields them — the machine holds even point-blank. When one is near enough
                    // to have struck, the player witnesses it stand down: VEGA's cue to explain why (once).
                    if (WrapDistSq(p.Position, enemy.Position) <= EnemyHuntRange * EnemyHuntRange)
                    {
                        RevealCompanionWardInsight(session);
                    }

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

    // Uniform per-kind gaits (NO per-individual variation). Walking robots are heavy + deliberate (slow accel,
    // slow pivots, pause-and-scan between patrol legs); the flying scan-drone is nimble and hover-bobs.
    private static readonly LocomotionProfile RobotProfile = new()
    {
        Style = LocomotionStyle.Prowler,
        CruiseSpeed = EnemyWanderSpeed,
        BurstSpeed = EnemyHuntSpeed,
        Accel = 3.0f,
        TurnRate = 1.8f,
        HoldMin = 3.0f,
        HoldMax = 7.0f,
        PauseChance = 0.5f,
        PauseMin = 1.0f,
        PauseMax = 2.5f,
        WeaveAmp = 0.12f,
        WeaveFreq = 0.8f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    private static readonly LocomotionProfile ToughRobotProfile = new()
    {
        Style = LocomotionStyle.Prowler,
        CruiseSpeed = EnemyWanderSpeed,
        BurstSpeed = EnemyToughHuntSpeed,
        Accel = 3.4f,
        TurnRate = 2.0f,
        HoldMin = 3.0f,
        HoldMax = 7.0f,
        PauseChance = 0.45f,
        PauseMin = 0.8f,
        PauseMax = 2.0f,
        WeaveAmp = 0.12f,
        WeaveFreq = 0.8f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    private static readonly LocomotionProfile DroneProfile = new()
    {
        Style = LocomotionStyle.Glider,
        CruiseSpeed = EnemyWanderSpeed * 1.3f,
        BurstSpeed = EnemyHuntSpeed,
        Accel = 5.0f,
        TurnRate = 3.5f,
        HoldMin = 2.0f,
        HoldMax = 4.5f,
        PauseChance = 0.25f,
        PauseMin = 0.6f,
        PauseMax = 1.5f,
        WeaveAmp = 0.25f,
        WeaveFreq = 1.1f,
        VertAmp = 0.6f,
        VertFreq = 1.4f, // hover bob
    };

    private const float DroneStandoff = 7f;     // a hunting drone hovers this far from the player rather than ramming
    private const float DroneStrafe = 3f;       // ...oscillating in/out by this for darting strafes
    private const float DroneOrbitSpeed = 0.9f; // rad/s it circles the player
    private const float DroneBob = 0.6f;        // vertical hover-bob amplitude

    private const float CompanionWardRange = 12f; // a tamed companion within this of its owner wards them

    /// <summary>True when one of the player's tamed companions is present and close enough to make the
    /// Guardian machines stand down — they read a creature-bonded human as part of the living world they were
    /// built to guard, not as prey. (Design: companions show the network the player belongs to the biosphere;
    /// the inverse of a non-cube structure, which reads as a constructed anomaly.)</summary>
    private bool WardedByCompanion(Shared.State.PlayerState p)
    {
        float r2 = CompanionWardRange * CompanionWardRange;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion && c.OwnerId == p.PlayerId && WrapDistSq(p.Position, c.Position) <= r2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves one planet enemy through the shared locomotion controller (eased speed + turn inertia +
    /// stop-and-go), with kind-specific behaviour: a walking robot stalks the nearest detectable player straight
    /// in (heavy + deliberate) or patrols with pause-and-scan; the flying scan-drone orbits/strafes the player at
    /// a standoff and hover-bobs instead of ramming. Terrain-following; a cliff step taller than 3 blocks blocks
    /// the move and re-rolls the heading. Returns true when the enemy actually moved.</summary>
    private bool MovePlanetEnemy(CombatEntity enemy, List<PlayerSession> targets, HashSet<string> warded, double dt)
    {
        // Safety net: a machine that ended up inside a parked ship (ship placed/grown over it) is pushed back
        // out of the hull rather than left stalking from inside the cabin.
        if (TryPushOutsideShip(enemy.Position, out var ejected))
        {
            enemy.Position = ejected;
            return false;
        }

        // Nearest detectable player — cloaked, god-mode and companion-warded players read as undetectable, so
        // the machine never paths toward them.
        PlayerSession? nearest = null;
        double bestSq = (double)EnemyHuntRange * EnemyHuntRange;
        foreach (var s in targets)
        {
            if (s.State.GodMode || s.State.Stealthed || warded.Contains(s.State.PlayerId))
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

        bool drone = enemy.Kind == CombatEntityKind.ScanDrone;
        var profile = drone ? DroneProfile : (enemy.Kind == CombatEntityKind.AlienMonster ? ToughRobotProfile : RobotProfile);

        MoveMode intent;
        Vector3f? target;
        if (nearest != null)
        {
            if (bestSq <= EnemyStopRange * EnemyStopRange)
            {
                return false; // in biting range — hold position (the aura damages)
            }

            intent = MoveMode.Seek;

            // Resolve the player to the enemy's local (unwrapped) frame so heading points the short way round the seams.
            var player = Unwrapped(enemy.Position, nearest.State.Position);
            if (drone)
            {
                // Don't ram: orbit the player at a standoff that oscillates in/out for darting strafes, so the
                // drone circles + banks. Target a moving point on a ring around the player.
                float bearing = (float)System.Math.Atan2(enemy.Position.Z - player.Z, enemy.Position.X - player.X);
                bearing += DroneOrbitSpeed * (float)dt; // advance around the ring → it circles
                float ring = DroneStandoff + DroneStrafe *
                    (float)System.Math.Sin(_uptime * 0.7 + (StableStringHash(enemy.Id) % 628) / 100.0);
                target = new Vector3f(player.X + (float)System.Math.Cos(bearing) * ring, player.Y,
                                      player.Z + (float)System.Math.Sin(bearing) * ring);
            }
            else
            {
                target = player; // robots stalk straight in — now eased + heavy via the controller
            }
        }
        else
        {
            intent = MoveMode.Roam;
            target = null;
        }

        var res = LocomotionController.Step(enemy.Loco, profile, enemy.Position, intent, target, dt, (uint)StableStringHash(enemy.Id));
        enemy.Loco = res.State;

        float nx = (float)WorldConstants.WrapX(res.Position.X, _world.Circumference);
        float nz = (float)WorldConstants.WrapZ(res.Position.Z, _world.Circumference);
        int prevGround = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(enemy.Position.X), (int)System.Math.Floor(enemy.Position.Z)) + 1;
        int groundY = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(nx), (int)System.Math.Floor(nz)) + 1;
        if (System.Math.Abs(groundY - prevGround) > 3)
        {
            enemy.Loco.ModeTimer = 0f; // cliff/spike in the way — pick a new direction next tick
            return false;
        }

        int hover = drone ? ScanDroneHover : 0;          // scan-drones float above the ground
        float bob = drone ? DroneBob * res.VertWave : 0f; // ...and hover-bob; robots stay grounded
        var candidate = new Vector3f(nx, groundY + hover + bob, nz);
        if (EntityBlockedByShip(candidate))
        {
            enemy.Loco.ModeTimer = 0f; // ship hull in the way — re-roll the heading next tick instead of entering
            return false;
        }

        enemy.Position = candidate;
        return res.Moving;
    }

    /// <summary>Returns <paramref name="to"/> expressed in <paramref name="from"/>'s local frame across the world's
    /// wrap seams, so a direction computed as (result - from) takes the short way round.</summary>
    private Vector3f Unwrapped(Vector3f from, Vector3f to) => new(
        from.X + (float)WorldConstants.WrapDeltaX(to.X - from.X, _world.Circumference),
        to.Y,
        from.Z + (float)WorldConstants.WrapDeltaZ(to.Z - from.Z, _world.Circumference));

    private void SpawnPlanetEnemyNear(Shared.State.PlayerState player, bool asDrone)
    {
        bool tougher = !asDrone && Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme;

        // Spawn well OUTSIDE the 28-block detection range (9–13 felt like an ambush): fiends appear
        // 35–50 blocks out, roam the area on wander headings, and only start hunting when the player
        // comes near them. Spread around the player with the golden angle, then drop onto the actual
        // surface at that column so they never spawn buried in the terrain.
        int n = _planetEnemies.Count;
        double ang = n * 2.39996323; // golden angle (radians)
        float dist = 35f + (n % 4) * 5f; // 35..50 blocks out — beyond EnemyHuntRange
        int ex = (int)System.Math.Round(player.Position.X + System.Math.Cos(ang) * dist);
        int ez = (int)System.Math.Round(player.Position.Z + System.Math.Sin(ang) * dist);

        // Count-neutral wreck coupling (P5): when a wreck is near the player, bias THIS spawn's position to
        // cluster at the wreck (the count + cadence are unchanged — only where it appears), and make a
        // wreck-spawned machine hit harder. Crashed network tech becomes a danger zone you learn to read.
        bool atWreck = false;
        if (Rules.MachineWreckCoupling && _worlds.Active.WreckStamped && _worlds.Active.WreckMarkers.Count > 0)
        {
            var markers = _worlds.Active.WreckMarkers;
            float cx = (float)markers.Average(m => m.Pos.X);
            float cz = (float)markers.Average(m => m.Pos.Z);
            float wdx = (float)WorldConstants.WrapDeltaX(cx - player.Position.X, _world.Circumference);
            float wdz = cz - player.Position.Z;
            if (wdx * wdx + wdz * wdz <= WreckCouplingRange * WreckCouplingRange)
            {
                double wang = n * 2.39996323;
                float wr = 4f + (n % 4) * 3f; // 4..13 blocks around the wreck centroid
                // Leave ex unwrapped (like the golden-angle path) so it stays in the same coordinate space as
                // the wreck markers; SurfaceHeight wraps internally.
                ex = (int)System.Math.Round(cx + System.Math.Cos(wang) * wr);
                ez = (int)System.Math.Round(cz + System.Math.Sin(wang) * wr);
                atWreck = true;
            }
        }

        int ey = _generator.SurfaceHeight(_world.Planet, ex, ez) + 1; // stand on the ground, not in it
        if (asDrone)
        {
            ey += ScanDroneHover; // the flying scan-drone hovers above the surface
        }

        float hull = asDrone ? 25f : (tougher ? 60f : 30f);
        _planetEnemies.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = asDrone ? CombatEntityKind.ScanDrone : (tougher ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature),
            Hostile = true,
            Hull = hull,
            HullMax = hull,
            Position = new Vector3f(ex, ey, ez),
            DamagePerSecond = (asDrone ? 3f : (tougher ? 6f : 4f)) * (atWreck ? 1.5f : 1f), // wreck machines are angrier
            Loot = { new ItemAmount("carbon", 2) }, // all Guardian machines drop salvage carbon
        });
    }

    private const float WreckCouplingRange = 64f; // bias spawns to a wreck within this of the player (P5)
    private const int ScanDroneHover = 4;          // blocks the flying scan-drone floats above the surface (P4)

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

        // Player-vs-player combat is not implemented yet: only creatures/NPCs are valid targets. When players do
        // become targetable (on foot here, or ship-vs-ship in FireWeapon), gate the damage on the alliance —
        // allies must never harm one another, even on a PVP server: `if (AreAllied(playerId, targetId)) reject`.
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
                target.AwakeOverrideTimer = CreatureWakeSeconds; // a hit jolts any sleeping creature awake (then it acts per temperament)
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
            RecordStoryMachineKill(); // planet machine destroyed → advances the story (P4: combat-as-progress)
            TryDropPlayerMemory(session); // a chance to release a personal memory (P4)
            BroadcastPlanetEnemies();
        }
    }

    private void BroadcastPlanetEnemies()
        => BroadcastToWorld(new PlanetEnemyList { Enemies = _planetEnemies.Select(ToNet).ToArray() });

    private void HandleAttackEntity(PlayerSession session, AttackEntityIntent intent)
        => AttackEntity(session.State.PlayerId, intent.EntityId);

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: whether a tamed companion is currently warding the player from the planet machines
    /// (present + within <see cref="CompanionWardRange"/>).</summary>
    public bool PlayerWardedByCompanionForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && WardedByCompanion(s.State);

    /// <summary>Test/util: spawn a hostile planet enemy at a position so combat can be tested deterministically
    /// without waiting on the random surface spawner (which appears 35–50 blocks out).</summary>
    public void SpawnPlanetEnemyAtForTest(Vector3f at, float damagePerSecond = 20f)
        => _planetEnemies.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Creature,
            Hostile = true,
            Hull = 30f,
            HullMax = 30f,
            Position = at,
            DamagePerSecond = damagePerSecond,
        });
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    private const double EnemySpawnInterval = 5.0;      // initial fill cadence while a fresh world ramps to its cap
    private const double EnemyRefillMinInterval = 20.0; // #740: once the cap was reached, refills come slow…
    private const double EnemyRefillMaxInterval = 45.0; //       …and jittered, so machine encounters read as events
    private const double EnemyKillSpawnGrace = 10.0;    // #740: extra breather added when a machine is destroyed
    // #740: machines far from every player despawn (like creatures, which prune at 70). The leash is wide
    // enough that ambient wander (≈0.5 b/s net with pauses) can't plausibly cross it — only a player
    // actually LEAVING (walking 4-5 b/s) does, so the count-neutral wreck-coupling guarantee stays intact.
    private const float EnemyFarDespawnRange = 150f;
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
        if (_planetEnemies.Count >= cap)
        {
            // At the cap the timer must not bank time (#740): it used to keep accumulating here, so the
            // moment a machine died its replacement spawned on the very next tick — fighting back turned
            // the spawner into a zero-gap stream. Holding it at zero makes every refill wait its interval,
            // and reaching the cap for the first time switches the world to the slow jittered refill pace.
            if (!_worlds.Active.EnemyCapSeen)
            {
                _worlds.Active.EnemyCapSeen = true;
                _worlds.Active.EnemyNextSpawnIn = RollEnemyRefillInterval();
            }

            _enemySpawnTimer = 0;
        }
        else if ((_enemySpawnTimer += dt) >= (_worlds.Active.EnemyCapSeen ? _worlds.Active.EnemyNextSpawnIn : EnemySpawnInterval))
        {
            _enemySpawnTimer = 0;
            _worlds.Active.EnemyNextSpawnIn = RollEnemyRefillInterval();
            // A fraction (~2 in 5) of the population spawns as the flying scan-drone variant (P4), the rest as
            // walking three-eyed ground robots — both within the same PlanetEnemies cap (count unchanged).
            // Key the mix off how many drones are ALREADY alive, not the raw spawn count: keying off the count
            // (`count % 5 < 2`) front-loaded both slots as drones at the default cap (Normal + solo = 2, and the
            // guard only spawns while count < 2), so the walking robots were never reached (#398). Spawning a
            // drone only while drones stay a minority (< 2/5) of the live population guarantees a robot appears
            // even at the smallest cap, while still converging on the ~2-in-5 drone ratio at larger caps.
            int droneCount = 0;
            foreach (var e in _planetEnemies)
            {
                if (e.Kind == CombatEntityKind.ScanDrone) { droneCount++; }
            }
            bool asDrone = Rules.PlanetDrones && droneCount * 5 < (_planetEnemies.Count + 1) * 2;
            SpawnPlanetEnemyNear(targets[_planetEnemies.Count % targets.Count].State, asDrone);
            BroadcastPlanetEnemies();
        }

        // #740: machines that ended up far from every surface player despawn — walking away from a fight
        // actually ends it instead of leaving a pack trailing you forever. Freed slots refill near the
        // players on the normal (post-cap: slow) cadence.
        bool pruned = false;
        for (int i = _planetEnemies.Count - 1; i >= 0; i--)
        {
            var e = _planetEnemies[i];
            bool near = false;
            foreach (var s in targets)
            {
                if (WrapDistSq(s.State.Position, e.Position) <= EnemyFarDespawnRange * EnemyFarDespawnRange)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                _planetEnemies.RemoveAt(i);
                _enemyWander.Remove(e.Id);
                _enemySightSeenAt.Remove(e.Id);
                pruned = true;
            }
        }

        if (pruned)
        {
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

                if (WrapDistSq(p.Position, enemy.Position) <= EnemyProximityRange * EnemyProximityRange
                    && HasLineOfSight(enemy.Position, p.Position)) // can't bite a target it can't see — duck behind cover/into a cave to break it
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

    /// <summary>Rolls the next post-cap refill interval (#740): 20–45 s, jittered deterministically from the
    /// world id + a per-world spawn ordinal so the cadence varies without wall-clock randomness.</summary>
    private double RollEnemyRefillInterval()
    {
        uint h = (uint)StableStringHash(_worlds.Active.LocationId + ":" + _worlds.Active.EnemySpawnOrdinal++);
        return EnemyRefillMinInterval + (h % 1000) / 999.0 * (EnemyRefillMaxInterval - EnemyRefillMinInterval);
    }

    private const float EnemyHuntRange = 28f;   // detection radius — inside it the fiend stalks the player
    private const float EnemyStopRange = 1.6f;  // close enough — the proximity aura does the biting
    private const float EnemyHuntSpeed = 3.1f;  // blocks/s while hunting (slightly slower than a running player)
    private const float EnemyToughHuntSpeed = 3.7f;
    private const float EnemyWanderSpeed = 1.1f;
    private const double EnemySightGiveUpSeconds = 6.0; // out of sight this long → give up the hunt and resume roaming

    // Per-world (routes through the active world) — a shared field would starve enemy syncs on all but one world.
    private double _enemySyncTimer { get => _worlds.Active.SinceEnemySync; set => _worlds.Active.SinceEnemySync = value; }
    private readonly Dictionary<string, (double Heading, double Until)> _enemyWander = new();
    private readonly Dictionary<string, double> _enemySightSeenAt = new(); // enemy id → uptime it last had line-of-sight to its prey

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

        // Line-of-sight gate: a machine only locks on to a target it can actually see. Once it has seen the
        // player it keeps pressing toward the last-known spot for a short grace (so ducking behind a pillar
        // doesn't instantly shake it), but if it stays blind past the grace it gives up and roams again. A
        // target it has never had a clear line to (already hidden in a cave / behind terrain) reads as no
        // target at all. Damage is separately sight-gated, so a blind chaser does no harm while it closes in.
        if (nearest != null)
        {
            if (HasLineOfSight(enemy.Position, nearest.State.Position))
            {
                _enemySightSeenAt[enemy.Id] = _uptime;
            }
            else if (!_enemySightSeenAt.TryGetValue(enemy.Id, out var seenAt) || _uptime - seenAt > EnemySightGiveUpSeconds)
            {
                nearest = null;
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
        int hover = drone ? ScanDroneHover : 0;          // scan-drones float above the ground
        float bob = drone ? DroneBob * res.VertWave : 0f; // ...and hover-bob; robots stay grounded
        // Ground from REAL blocks (like creatures, #650), not the pure noise surface, so machines honour
        // stamped structures and player edits instead of walking on air over them (#711). The reference Y
        // is the feet (a drone's hover offset removed); noise surface only when the chunk isn't loaded.
        int refY = (int)System.Math.Floor(enemy.Position.Y) - hover;
        int prevGround = GroundFeetYAt((int)System.Math.Floor(enemy.Position.X), (int)System.Math.Floor(enemy.Position.Z), refY);
        int groundY = GroundFeetYAt((int)System.Math.Floor(nx), (int)System.Math.Floor(nz), refY);
        if (System.Math.Abs(groundY - prevGround) > 3)
        {
            enemy.Loco.ModeTimer = 0f; // cliff/spike in the way — pick a new direction next tick
            return false;
        }

        var candidate = new Vector3f(nx, groundY + hover + bob, nz);
        if (EntityBlockedByShip(candidate) || BlockedByEnergyFence(enemy.Position, candidate))
        {
            enemy.Loco.ModeTimer = 0f; // ship hull or an energy fence in the way — re-roll the heading next tick
            return false;
        }

        // A machine never steps into a player's body (#749): the chase hold above only covers a live
        // lock, so a roaming cruiser — or one whose target is cloaked/warded — would otherwise walk
        // straight through the player. Chasers just wait at the ring; roamers re-roll their heading.
        foreach (var s in targets)
        {
            if (WrapDistSq(s.State.Position, candidate) <= EnemyStopRange * EnemyStopRange)
            {
                if (intent == MoveMode.Roam)
                {
                    enemy.Loco.ModeTimer = 0f;
                }

                return false;
            }
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

        // Stand on the ground, not in it — real blocks when the column is loaded, noise surface otherwise.
        int ey = GroundFeetYAt(ex, ez, _generator.SurfaceHeight(_world.Planet, ex, ez) + 1);
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

    /// <summary>Player attacks a planet enemy or creature with the held tool/weapon. Server resolves the hit.
    /// The optional aim direction is the client's camera ray at the moment of firing (#693); a zero vector
    /// (older client) skips the aim validation.</summary>
    public void AttackEntity(string playerId, string entityId, float dirX = 0f, float dirY = 0f, float dirZ = 0f)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var dir = new Vector3f(dirX, dirY, dirZ);
        if (_planetEnemies.FirstOrDefault(e => e.Id == entityId) is { } enemy)
        {
            AttackCombatEntity(session, enemy, _planetEnemies, isCreature: false, dir);
            return;
        }

        if (_creatures.FirstOrDefault(e => e.Id == entityId) is { } creature)
        {
            AttackCombatEntity(session, creature, _creatures, isCreature: true, dir);
            return;
        }

        if (_bandits.FirstOrDefault(e => e.Id == entityId) is { } bandit)
        {
            AttackCombatEntity(session, bandit, _bandits, isCreature: false, dir);
            return;
        }

        // Player-vs-player combat is not implemented yet: only creatures/NPCs are valid targets. When players do
        // become targetable (on foot here, or ship-vs-ship in FireWeapon), gate the damage on the alliance —
        // allies must never harm one another, even on a PVP server: `if (AreAllied(playerId, targetId)) reject`.
        Reject(session, "attack", "No such target.");
    }

    private const double MeleeCooldown = 1.5;                       // melee weapons swing at most this often (B44)
    private readonly Dictionary<string, double> _meleeReadyAt = new(); // playerId → uptime the next melee swing is allowed

    private void AttackCombatEntity(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature, Vector3f aimDir = default)
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

        if (!ValidateAim(session, target, tool, isWeapon, aimDir))
        {
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

        if (isCreature)
        {
            // Any hit — surviving or fatal — startles the victim's nearby kin (#653): non-retaliating
            // herd members scatter instead of grazing on beside the corpse; retaliators charge instead.
            StartleKin(target);
        }

        if (target.Hull > 0f)
        {
            // A surviving creature that retaliates (territorial / already hostile) is provoked:
            // for a while it hunts and bites back (and a pack-hunter rallies nearby kin).
            if (isCreature)
            {
                target.AwakeOverrideTimer = CreatureWakeSeconds; // a hit jolts any sleeping creature awake (then it acts per temperament)
                ProvokeCreature(target);
            }
            else if (target.IsBandit)
            {
                OnBanditAttacked(session, target); // attacking a robber counts as refusing its hold-up
            }

            if (isCreature) BroadcastCreatures(); else BroadcastPlanetEnemies();
            return;
        }

        list.Remove(target);
        _enemyWander.Remove(target.Id); // drop the dead enemy's wander state
        var pool = new MaterialPool(_content, p, _ship);
        BankLoot(session, pool, target.Loot); // the kill already happened — warn if a full inventory loses the drop
        SendInventory(session);
        OnAchievementDefeat(session);
        if (isCreature)
        {
            BroadcastCreatures();
        }
        else
        {
            BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });
            if (target.IsBandit)
            {
                OnBanditKilled(target); // bandits are people, not machines — no story credit, but camps clear
                OnMissionDefeat(session, DefeatTargetBandit); // #730: bounty objectives count the drive-off
            }
            else
            {
                RecordStoryMachineKill(); // planet machine destroyed → advances the story (P4: combat-as-progress)
                TryDropPlayerMemory(session); // a chance to release a personal memory (P4)
                // #740: a destroyed machine buys an extra breather on top of the refill interval — the
                // negative timer delays the freed slot, so a fight is followed by quiet, not reinforcements.
                _enemySpawnTimer = System.Math.Min(_enemySpawnTimer, -EnemyKillSpawnGrace);
            }

            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Validates the client's claimed aim against the claimed target (#693). Anti-cheat guardrail,
    /// not a precision hitbox: latency + interpolation mean the client's view lags the server's, so every
    /// tolerance is generous — the client already did the precise crosshair test. A zero direction (older
    /// client, or a melee swing) skips the angle checks entirely. With AutoAim ON the target only has to sit
    /// in a wide forward cone; with AutoAim OFF the crosshair ray must actually pass near the target's body.
    /// Ranged weapons additionally need a clear sightline — no shooting through walls.</summary>
    private bool ValidateAim(PlayerSession session, CombatEntity target, ToolProperties tool, bool isWeapon, Vector3f aimDir)
    {
        float dirLenSq = aimDir.X * aimDir.X + aimDir.Y * aimDir.Y + aimDir.Z * aimDir.Z;
        if (dirLenSq < 0.0001f)
        {
            return true; // no aim data (older client) — keep the legacy range-only behaviour
        }

        bool ranged = isWeapon && tool.Range > EnemyAttackReach;
        var p = session.State;

        // Ranged shots respect walls: the same voxel sightline that gates enemy bites (glass blocks it too).
        if (ranged && !HasLineOfSight(p.Position, target.Position))
        {
            Reject(session, "attack", "No clear line of fire.");
            return false;
        }

        const float eye = 1.5f; // matches HasLineOfSight/the client camera height
        var dst = Unwrapped(p.Position, target.Position);
        float tx = dst.X - p.Position.X;
        float ty = (dst.Y + 0.9f) - (p.Position.Y + eye); // aim roughly at the body, not the feet
        float tz = dst.Z - p.Position.Z;
        float dist = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (dist < 0.75f)
        {
            return true; // point-blank — any angle is honest
        }

        float dirLen = (float)System.Math.Sqrt(dirLenSq);
        float dot = (aimDir.X * tx + aimDir.Y * ty + aimDir.Z * tz) / (dirLen * dist);

        // Ray-precision only for ranged manual aiming; melee and auto-aim keep a wide forward cone.
        if (!Rules.AutoAim && ranged)
        {
            // Perpendicular miss distance of the crosshair ray from the target's body centre, with a
            // body-size + distance-scaled corridor (≈6° plus the body itself).
            float along = System.Math.Max(0f, dot) * dist;
            float missSq = dist * dist - along * along;
            float scale = System.Math.Max(1f, System.Math.Max(target.Scale, target.SizeScale));
            float allowed = 1.5f * scale + 0.1f * dist;
            if (dot <= 0f || missSq > allowed * allowed)
            {
                Reject(session, "attack", "Shot went wide.");
                return false;
            }

            return true;
        }

        if (dot < 0.35f) // ~70° half-angle: forgiving even for a swirling melee fight, but never behind the back
        {
            Reject(session, "attack", "Target is not in front of you.");
            return false;
        }

        return true;
    }

    // Bandits ride the planet-enemy wire (same list message), so client targeting/health bars/defeat
    // handling work unchanged — the client tells them apart by the Kind string.
    private void BroadcastPlanetEnemies()
        => BroadcastToWorld(new PlanetEnemyList { Enemies = _planetEnemies.Concat(_bandits).Select(ToNet).ToArray() });

    private void HandleAttackEntity(PlayerSession session, AttackEntityIntent intent)
        => AttackEntity(session.State.PlayerId, intent.EntityId, intent.DirX, intent.DirY, intent.DirZ);

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

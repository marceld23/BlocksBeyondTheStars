// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Planet enemies (technical requirements / `anf_space_flight.md` §12). Hostile creatures
/// spawn near players on the surface when the rules allow it, deal proximity damage, and are
/// killed with the held tool/weapon. Disabled in Creative and on peaceful servers (§12.4).
/// </summary>
public sealed partial class GameServer
{
    private const double EnemySpawnInterval = 5.0;      // initial fill cadence: the first `cap` spawns after a world load (#2517)
    private const double EnemyRefillMinInterval = 20.0; // #740: once the cap was reached, refills come slow…
    private const double EnemyRefillMaxInterval = 45.0; //       …and jittered, so machine encounters read as events
    private const double EnemyKillSpawnGrace = 10.0;    // #740: extra breather added when a machine is destroyed
    // #740: machines far from every player despawn (like creatures, which prune at 70). The leash is wide
    // enough that ambient wander (≈0.5 b/s net with pauses) can't plausibly cross it — only a player
    // actually LEAVING (walking 4-5 b/s) does, so the count-neutral wreck-coupling guarantee stays intact.
    private const float EnemyFarDespawnRange = 150f;
    private const float EnemyProximityRange = 4f;
    private const float EnemyAttackReach = MeleeRules.MeleeReach;
    private const double EnemyMotionSyncInterval = 0.125; // #2670: moving machines/bandits sync every second 15 Hz tick

    /// <summary>
    /// How far a RANGED machine can hurt the player. The scan-drone shoots; it does not bite, and its own
    /// AI deliberately keeps its distance — <see cref="DroneStandoff"/> ± <see cref="DroneStrafe"/>
    /// horizontally plus <see cref="ScanDroneHover"/> of hover — so the closest it ever gets is
    /// <c>sqrt(4² + 4²) ≈ 5.7</c> blocks. Against the 4-block melee aura that meant its
    /// <see cref="CombatEntity.DamagePerSecond"/> could never once be applied: it circled the player firing
    /// its laser (the client draws that out to 16 blocks) and was mechanically harmless. A player found it
    /// in minutes — "Diese Drohne macht mir keinen Schaden."
    /// <para>Matches the client's <c>DroneFireRange</c>, so what you SEE hitting you is what hurts.</para>
    /// </summary>
    private const float EnemyRangedRange = 16f;

    /// <summary>True for machines that attack at range rather than by touching the player.</summary>
    private static bool IsRangedEnemy(CombatEntityKind kind) => kind == CombatEntityKind.ScanDrone;

    /// <summary>Reach of a machine's damage aura: melee for walkers, the full firing range for shooters.</summary>
    private static float EnemyDamageRange(CombatEntityKind kind)
        => IsRangedEnemy(kind) ? EnemyRangedRange : EnemyProximityRange;

    private List<CombatEntity> _planetEnemies => _worlds.Active.PlanetEnemies;
    private double _enemySpawnTimer { get => _worlds.Active.EnemySpawnTimer; set => _worlds.Active.EnemySpawnTimer = value; }
    private readonly List<PlayerSession> _enemyTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private readonly HashSet<string> _wardedPlayers = new();    // reused per tick (companion-ward set)

    /// <summary>Hostile creatures currently active on the surface.</summary>
    public IReadOnlyList<CombatEntity> PlanetEnemies => _planetEnemies;

    /// <summary>Whether hostile planet enemies may exist given the active rules. The Guardian's defeat no
    /// longer switches them off (#1206): see <see cref="RemnantEra"/>.</summary>
    private bool PlanetEnemiesActive => Rules.PlanetEnemies != AlienActivity.Off
        && Rules.GameMode == GameMode.Survival;

    /// <summary>"Remnant Protocol" (#1206): once the Guardian core is shut down the galaxy is calmer, not
    /// empty. Before, <c>GuardianDefeated</c> switched every planet machine, space wave and bandit ship off
    /// for good — the post-game world was emptier than the one the player had just saved, and the raider
    /// bounty could never be offered again. Now the flag is a <em>factor</em>: the planet cap halves (never
    /// below one), refills come at twice the interval, and only the leftover scan-drones roam (the walking
    /// hunter robots are gone — lore: the drones were the explore units); space waves keep only the places
    /// that are dangerous by their own nature (pirate havens, the opt-in frontier-danger tier) and bandit
    /// ships keep only their havens. Peaceful/family rules (PlanetEnemies Off) stay at zero as before.</summary>
    private bool RemnantEra => _storyState.GuardianDefeated;

    /// <summary>Post-win pacing multiplier for the planet-machine spawn cadence (#1206).</summary>
    private double RemnantPaceFactor => RemnantEra ? 2.0 : 1.0;

    /// <summary>The live planet-machine cap for the given number of surface targets: the activity rule times
    /// the players, halved (floor one) once the galaxy is pacified (#1206).</summary>
    private int PlanetEnemyCap(int targets)
    {
        int cap = ActivityCount(Rules.PlanetEnemies) * targets;
        // 2026-09 (generation 8): a type's machine density — Titas' "very many guardians" ×2.5.
        if (_world.Planet is { EnemyDensity: not 1.0 } planet && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration)
        {
            cap = (int)System.Math.Round(cap * planet.EnemyDensity);
        }

        return RemnantEra ? System.Math.Max(1, cap / 2) : cap;
    }

    private void TickEnemies(double dt)
    {
        // Orbital stations (void worlds) are safe havens: only peaceful NPCs live there, never hostiles. The one
        // deliberate exception (#2635): a generated derelict's guardian bay releases its own robots and drones inside
        // its hull — gated like the planet machines (see GameServerDerelictGuardians).
        if (_world.Planet.Void)
        {
            if (IsGeneratedDerelictWorld(_world.LocationId))
            {
                TickDerelictGuardians(dt);
            }

            return;
        }

        if (!PlanetEnemiesActive)
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

        int cap = PlanetEnemyCap(targets.Count);
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
        else if ((_enemySpawnTimer += dt) >= (_worlds.Active.EnemyCapSeen ? _worlds.Active.EnemyNextSpawnIn : EnemySpawnInterval) * RemnantPaceFactor)
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
            // Remnant era (#1206): only the scan-drones are left; the world option PlanetDrones=false keeps
            // its meaning (then the few remnants are walkers — still fewer and slower than before the win).
            bool asDrone = Rules.PlanetDrones && (RemnantEra || droneCount * 5 < (_planetEnemies.Count + 1) * 2);
            // #2554: a walking robot never appears with water on its feet — when every spot tried is wet (a player on
            // an island), this spawn is skipped and the next interval tries again.
            if (SpawnPlanetEnemyNear(targets[_planetEnemies.Count % targets.Count].State, asDrone))
            {
                BroadcastPlanetEnemies();

                // #2517: the quick start is the first `cap` SPAWNS after a world load, not "until the live count reaches
                // the cap" — a player who destroyed every machine the moment it showed up never let the count get there,
                // so the world stayed on the 5 s cadence for good and a new machine arrived exactly 15 s after each kill.
                // From here on every spawn waits the slow jittered interval (rolled above) plus the kill grace.
                if (!_worlds.Active.EnemyCapSeen && ++_worlds.Active.EnemyQuickSpawns >= cap)
                {
                    _worlds.Active.EnemyCapSeen = true;
                }
            }
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
                ForgetWadePlan(e.Id);
                pruned = true;
            }
        }

        if (pruned)
        {
            BroadcastPlanetEnemies();
        }

        RunMachinePass(targets, dt);
    }

    /// <summary>The machines' half of an enemy tick, shared by the planet machines and a derelict's guardians (#2635):
    /// movement (hunt or roam), the damage aura, the stranded-robot cleanup and the throttled position stream. Ends early
    /// when a death carried the victim to another world — the active world (and its machine list) changed under the
    /// pass.</summary>
    private void RunMachinePass(List<PlayerSession> targets, double dt)
    {
        var world = _worlds.Active;

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

            if (StaggerJustEnded(enemy))
            {
                BroadcastPlanetEnemies(); // #2278: the dizzy look ends even for a machine that stands still
            }

            if (IsStaggered(enemy))
            {
                continue; // #2278: dazed by a shock-glove push — its damage aura pauses
            }

            foreach (var session in targets)
            {
                var p = session.State;
                if (HostilesIgnore(p)) // cloaked, god-mode, creative-override (#1121) and Sandbox (#2712) players aren't detected
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
                        if (_uptime - session.CompanionWardEventAt >= InsightEventCooldownSeconds)
                        {
                            session.CompanionWardEventAt = _uptime; // #2465: the ward holds every tick — one event per stretch
                            RaiseGameEvent(session, GameEventKeys.InsightCompanionWard);
                        }
                    }

                    continue;
                }

                float reach = EnemyDamageRange(enemy.Kind); // shooters reach as far as they fire; walkers bite
                if (WrapDistSq(p.Position, enemy.Position) <= reach * reach
                    && HasLineOfSight(enemy.Position, p.Position)) // can't bite a target it can't see — duck behind cover/into a cave to break it
                {
                    DamagePlayer(p, (float)(enemy.DamagePerSecond * dt), mitigated: true);
                    NoteCombat(session); // #2286
                    NoteHitFrom(session, enemy, enemy.Position); // #2564: the HUD marker points at this machine
                    MarkPlayerStateDirty(session); // #1530: one state per tick, not one per biting machine
                    if (p.Health <= 0f)
                    {
                        RespawnPlayer(session, "@srv.death.creature");
                        if (!ReferenceEquals(_worlds.Active, world))
                        {
                            return; // #2635: the death took the victim to another world (a derelict → the ship) — this pass ends
                        }
                    }
                }
            }
        }

        // #2554: a robot that found no way out of deep water powers down (removed after the pass, which iterates the list).
        if (RemoveStrandedMachines())
        {
            BroadcastPlanetEnemies();
        }

        // Stream the new positions, throttled so a wandering pack doesn't flood the channel. #2670: every second tick
        // (7.5 Hz at 15 Hz), so the client's interpolation delay stays ~0.2 s like the animals'.
        _enemySyncTimer += dt;
        if (moved && _enemySyncTimer >= EnemyMotionSyncInterval)
        {
            _enemySyncTimer = 0;
            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Rolls the next post-cap refill interval (#740): 20–45 s, jittered deterministically from the
    /// world id + a per-world spawn ordinal so the cadence varies without wall-clock randomness.</summary>
    private double RollEnemyRefillInterval()
        => EnemyRefillInterval(_worlds.Active.LocationId, _worlds.Active.EnemySpawnOrdinal++, _meta.Seed);

    /// <summary>The refill interval for one spawn ordinal of a world. #2687: well-mixed (<see cref="StableHashing"/>) and
    /// salted with the save's seed — the h·31 hash of <c>{location}:{ordinal}</c> moved by a few units per ordinal, so
    /// <c>% 1000</c> stepped the 20–45 s window by fractions of a second and the cadence was effectively constant.</summary>
    private static double EnemyRefillInterval(string locationId, int ordinal, long seed)
        => EnemyRefillMinInterval + StableHashing.Unit($"enemy-refill:{locationId}:{ordinal}", seed) * (EnemyRefillMaxInterval - EnemyRefillMinInterval);

    /// <summary>Test seam (#2687): the post-cap refill interval of one spawn ordinal of a world.</summary>
    internal static double EnemyRefillIntervalForTest(string locationId, int ordinal, long seed)
        => EnemyRefillInterval(locationId, ordinal, seed);

    /// <summary>A scan drone's own phase (0..2π) of its in/out strafe around the player, stable per individual. #2687: from
    /// the well-mixed hash of the id — <c>h·31 % 628</c> of <c>e{n}</c> put drones spawned together 0.01 rad apart, so a
    /// pair strafed in lock-step. The id alone (no salt string) keeps this per-tick call allocation-free.</summary>
    private static double DroneBobPhase(string id) => StableHashing.Unit(id) * 2.0 * System.Math.PI;

    /// <summary>Test seam (#2687): a scan drone's strafe phase in radians.</summary>
    internal static double DroneBobPhaseForTest(string id) => DroneBobPhase(id);

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
    /// the move and re-rolls the heading, and so does water deeper than a walking robot wades (#2554) — one that
    /// stands in such water already walks out of it first (<see cref="WadeOutOfWater"/>). Returns true when the
    /// enemy actually moved.</summary>
    private bool MovePlanetEnemy(CombatEntity enemy, List<PlayerSession> targets, HashSet<string> warded, double dt)
    {
        // Safety net: a machine that ended up inside a parked ship (ship placed/grown over it) is pushed back out of the
        // hull rather than left stalking from inside the cabin; #2950: a drone buried in a block is lifted out of it.
        if (TryPushOutsideShip(enemy.Position, out var ejected) || TryLiftEmbeddedDrone(enemy, out ejected))
        {
            enemy.Position = ejected;
            return false;
        }

        if (IsStaggered(enemy))
        {
            return false; // #2278: dazed by a shock-glove push — it stands where the push left it
        }

        bool drone = enemy.Kind == CombatEntityKind.ScanDrone;
        int hover = drone ? DroneHoverHere : 0; // scan-drones float above the ground (low under a station's ceiling, #2635)
        // Ground from REAL blocks (like creatures, #650), not the pure noise surface, so machines honour
        // stamped structures and player edits instead of walking on air over them (#711). The reference Y
        // is the feet (a drone's hover offset removed); noise surface only when the chunk isn't loaded.
        int refY = (int)System.Math.Floor(enemy.Position.Y) - hover;
        int ownX = (int)System.Math.Floor(enemy.Position.X), ownZ = (int)System.Math.Floor(enemy.Position.Z);
        int prevGround = drone ? DroneGroundAt(ownX, ownZ, refY) : WalkerFeetAt(ownX, ownZ, refY); // #2586: a drone over open gas rides its surface

        // #2554: a walking robot that stands in water deeper than it wades — a pond dug or flooded around it, a
        // push, an older spawn — neither hunts nor patrols down there: it walks out to the nearest dry ground first.
        if (!drone && MachineWaterDepth(ownX, ownZ, prevGround) > MachineWadeDepth)
        {
            return WadeOutOfWater(enemy, targets, ownX, ownZ, prevGround, dt);
        }

        ForgetWadePlan(enemy.Id); // out of the deep water (or never in it): no exit plan to keep

        // Nearest detectable player — cloaked, god-mode and companion-warded players read as undetectable, so
        // the machine never paths toward them.
        PlayerSession? nearest = null;
        double bestSq = (double)EnemyHuntRange * EnemyHuntRange;
        foreach (var s in targets)
        {
            if (HostilesIgnore(s.State) || warded.Contains(s.State.PlayerId))
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
                bool cramped = _world.Planet.Void; // #2635: a derelict's rooms are a few blocks wide — a tight ring
                float ring = (cramped ? GuardianDroneStandoff : DroneStandoff) + (cramped ? GuardianDroneStrafe : DroneStrafe) *
                    (float)System.Math.Sin(_uptime * 0.7 + DroneBobPhase(enemy.Id));
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
        float bob = drone ? DroneBob * res.VertWave : 0f; // scan-drones hover-bob; robots stay grounded
        int nextX = (int)System.Math.Floor(nx), nextZ = (int)System.Math.Floor(nz);
        // #1482: the destination column is read from REAL blocks only. The old probe fell back to the noise
        // surface when nothing standable sat within its ±6 window — which is exactly what a wall taller than
        // six blocks looks like from its foot — so a walking robot read a seven-block wall as level ground
        // and strolled straight through it, while a three-block wall lifted it onto the parapet in a single
        // tick. Now: no standable cell in a loaded column = a wall; a walker steps up two blocks at most (a
        // long-legged hunter clears a natural ledge, a three-block protective wall holds it) and a drone's
        // hover clears three; anything higher is an obstacle. (The standable probe already demands two free
        // cells above the feet, so a walker's body never lands inside a wall; #2950: a drone needs every cell up to
        // its hovering body clear — tree crowns and water still pass, a roof or a low tunnel is a wall.)
        if (!MachineGroundAt(nextX, nextZ, refY, realWaterBed: !drone, out int groundY, overGas: drone)
            || groundY - prevGround > (drone ? hover - 1 : MachineStepUp)
            || prevGround - groundY > MachineStepDown || (drone && !DroneBodyClear(nextX, nextZ, groundY, hover)))
        {
            enemy.Loco.ModeTimer = 0f; // wall/cliff/spike in the way — pick a new direction next tick
            return false;
        }

        // #2586: a gas giant's gas sea carries no walking robot — off a deck or an island's rim it turns back, chasing or
        // patrolling alike. (A drone crosses open gas at its hover over the surface.)
        if (!drone && WalkerStepsIntoGasSea(ownX, ownZ, prevGround, nextX, nextZ, groundY))
        {
            enemy.Loco.ModeTimer = 0f;
            return false;
        }

        // #2554: water deeper than a walking robot wades is a wall to it, chasing or patrolling alike — a chaser stops
        // at the shore, a patroller turns back. It used to walk the lake bed under water (a column of water was "no
        // wall"), which read as a glitch and left a robot nobody could fight properly. A drone hovers over a lake as before.
        if (!drone && MachineWaterDepth(nextX, nextZ, groundY) > MachineWadeDepth)
        {
            enemy.Loco.ModeTimer = 0f;
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

    /// <summary>Blocks a walking machine steps up in one go (#1482): a natural two-block ledge, never a three-block wall.</summary>
    private const int MachineStepUp = 2;

    /// <summary>Blocks a machine steps down in one go; a deeper drop is a cliff it turns away from.</summary>
    private const int MachineStepDown = 3;

    /// <summary>Water a walking robot wades through (#2554): one block, like a land animal
    /// (<see cref="CreatureBehaviour.TerrainStepBlocked"/>); two or more is a wall to it.</summary>
    private const int MachineWadeDepth = 1;

    /// <summary>How many cells up from the feet <see cref="MachineWaterDepth"/> looks: past a stack of seabed plants
    /// (an underwater forest's kelp grows twelve cells tall) to the water above them.</summary>
    private const int MachineWaterScan = 16;

    /// <summary>How far (columns, each way) a robot standing in deep water searches for dry ground (#2554): the
    /// search runs in a fixed (2r+1)² grid, so it is bounded and allocation-free.</summary>
    private const int MachineExitRadius = 12;

    private const int MachineExitSide = (2 * MachineExitRadius) + 1;

    /// <summary>Blocks per second a robot walks out of deep water — its patrol pace.</summary>
    private const float MachineWadeOutSpeed = EnemyWanderSpeed;

    // #2554: reused buffers of the water-exit search (one search at a time on the tick thread, no per-tick allocation).
    private readonly int[] _exitQueue = new int[MachineExitSide * MachineExitSide];
    private readonly int[] _exitFeet = new int[MachineExitSide * MachineExitSide];
    private readonly int[] _exitParent = new int[MachineExitSide * MachineExitSide];
    private readonly int[] _exitSeen = new int[MachineExitSide * MachineExitSide];
    private int _exitStamp;
    private readonly List<CombatEntity> _strandedMachines = new(); // robots with no way out of deep water this tick
    private System.Func<int, int, int, ushort>? _machineBlockAt;   // no-load block read for WetCell, cached once

    /// <summary>Shortest gap (seconds of the robot's own walking) between two water-exit searches for one robot (#2554).</summary>
    private const double MachineWadeReplanSeconds = 0.5;

    /// <summary>#2554: a robot's planned way out of deep water — the column it planned from, the side neighbour it walks
    /// into, the seconds since the plan, and whether the planned step failed — so the search runs about once per column
    /// crossed instead of every tick. A struct in a dictionary keyed by the robot's id: no allocation per tick.</summary>
    private struct WadePlan
    {
        public int FromX, FromZ, StepX, StepZ;
        public double Age;
        public bool Stale;
    }

    private readonly Dictionary<string, WadePlan> _wadePlans = new();
    private int _waterExitSearches; // test seam: how often TryFindWaterExit ran

    /// <summary>The feet cell a planet machine would rest on in a column (#1482): a REAL standable cell near the
    /// reference height, or the generator surface when the column's chunk is not loaded or the column is water /
    /// lava in the probe window (a lake is no wall to a drone — it crosses it the way it always did, on the noise
    /// surface). A loaded column with nothing standable and no fluid in the window is solid ground-to-sky as
    /// far as the machine can see: a wall.
    ///
    /// <para>With <paramref name="realWaterBed"/> (a walking robot, #2554) a loaded WATER column answers the feet on its
    /// real bed — the lowest water (or plant in water) cell of the body in the probe window — instead of the generator's
    /// surface, which knows nothing of a pond the player dug and flooded. The walker's water gate measures the depth from
    /// there. Lava, and a water column that hangs in the air (a waterfall), keep the generator surface as before. With
    /// <paramref name="overGas"/> (a scan-drone, #2586) a gas giant's open gas answers its surface
    /// (<see cref="GasSurfaceFloorFor"/>): the drone crosses it as it crosses a lake. Without it, a loaded open-gas column
    /// stays a wall, as it always read.</para></summary>
    private bool MachineGroundAt(int x, int z, int refY, bool realWaterBed, out int feetY, bool overGas = false)
    {
        if (TryGroundFeetYAt(x, z, refY, out feetY))
        {
            if (overGas)
            {
                feetY = DroneFloorOverGas(x, z, feetY); // #2586: a block standing in the gas is no floor to hover over
            }

            return true;
        }

        if (overGas)
        {
            int gasFloor = GasSurfaceFloorFor(x, z, ScanDroneHover);
            if (gasFloor != int.MinValue)
            {
                feetY = gasFloor;
                return true;
            }
        }

        bool loaded = _world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, refY, z)));
        int fluidTop = int.MinValue; // the topmost fluid cell in the probe window
        for (int dy = CreatureGroundScan; dy >= -CreatureGroundScan && loaded; dy--)
        {
            if (IsFluid(_world.GetBlockIfLoaded(new Vector3i(x, refY + dy, z)).Value))
            {
                fluidTop = refY + dy;
                break;
            }
        }

        if (loaded && fluidTop == int.MinValue)
        {
            return false;
        }

        if (realWaterBed && loaded && _waterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, fluidTop, z)).Value == _waterId)
        {
            int y = fluidTop;
            int floor = fluidTop - FluidColumnScan;
            while (y > floor && MachineCellWet(x, y - 1, z))
            {
                y--;
            }

            if (IsSupportCell(x, y - 1, z))
            {
                feetY = y; // on the bed, in the water
                return true;
            }
        }

        feetY = _generator.SurfaceHeight(_world.Planet, x, z) + 1; // off-screen or over a fluid: the noise surface, as before
        return true;
    }

    /// <summary>A walking robot's feet in its own column: <see cref="MachineGroundAt(int, int, int, bool, out int, bool)"/>
    /// with the real water bed, or the creatures' probe (generator surface fallback) when the column reads as a wall.</summary>
    private int WalkerFeetAt(int x, int z, int refY)
        => MachineGroundAt(x, z, refY, realWaterBed: true, out int feet) ? feet : GroundFeetYAt(x, z, refY);

    /// <summary>How deep the water stands on feet at <paramref name="feetY"/> (#2554), capped at
    /// <see cref="MachineWadeDepth"/> + 1 (0 = dry, 1 = wading, 2 = too deep): the cells from the feet up that are
    /// water — or a plant, prop or building form that the water fills (<see cref="WetCell"/>'s rule) or that stands in
    /// a stack of such cells with water above it, so seabed kelp, seagrass and coral under a lake count as the lake.
    /// Real blocks where the column is loaded; the generator's water body elsewhere. No-load reads.</summary>
    private int MachineWaterDepth(int x, int z, int feetY)
    {
        const int Cap = MachineWadeDepth + 1;
        if (_waterId == 0)
        {
            return 0;
        }

        if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, feetY, z))))
        {
            int cx = WorldConstants.WrapX(x, _world.Circumference), cz = WorldConstants.WrapZ(z, _world.Circumference);
            return _generator.TryGetWaterSurface(_world.Planet, cx, cz, out int top, out _) && top >= feetY
                ? System.Math.Min(top - feetY + 1, Cap)
                : 0;
        }

        _machineBlockAt ??= (bx, by, bz) => _world.GetBlockIfLoaded(new Vector3i(bx, by, bz)).Value;
        int depth = 0, pending = 0; // pending: plant cells below that count only if water turns up above them
        for (int y = feetY; y < feetY + MachineWaterScan && depth < Cap; y++)
        {
            var id = _world.GetBlockIfLoaded(new Vector3i(x, y, z));
            if (id.Value == _waterId)
            {
                depth += pending + 1;
                pending = 0;
            }
            else if (!id.IsAir && MachineNonFullCell(id, new Vector3i(x, y, z)))
            {
                if (WetCell.WaterSurrounds(_machineBlockAt, _waterId, x, y, z))
                {
                    depth += pending + 1;
                    pending = 0;
                }
                else
                {
                    pending++;
                }
            }
            else
            {
                break; // air or a full block: the water above the feet ends here
            }
        }

        return System.Math.Min(depth, Cap);
    }

    /// <summary>Whether a cell is wet for <see cref="MachineGroundAt(int, int, int, bool, out int, bool)"/>'s bed descent:
    /// water, or a plant / prop / form that water surrounds (<see cref="WetCell.IsWet"/>), or a plant with a wet cell
    /// straight above it (the foot of a kelp stalk in a kelp forest).</summary>
    private bool MachineCellWet(int x, int y, int z)
    {
        var id = _world.GetBlockIfLoaded(new Vector3i(x, y, z));
        if (id.Value == _waterId)
        {
            return true;
        }

        if (id.IsAir || !MachineNonFullCell(id, new Vector3i(x, y, z)))
        {
            return false;
        }

        _machineBlockAt ??= (bx, by, bz) => _world.GetBlockIfLoaded(new Vector3i(bx, by, bz)).Value;
        if (WetCell.WaterSurrounds(_machineBlockAt, _waterId, x, y, z))
        {
            return true;
        }

        // The bed descent comes from above, so the cell above was wet already: a stalk standing in the lake.
        var above = _world.GetBlockIfLoaded(new Vector3i(x, y + 1, z));
        return above.Value == _waterId || (!above.IsAir && MachineNonFullCell(above, new Vector3i(x, y + 1, z)));
    }

    /// <summary>A block that fills only part of its cell (#1902): every plant, the slim props, a non-cube building form.</summary>
    private bool MachineNonFullCell(BlockId id, Vector3i pos)
        => WetCell.IsNonFullKey(_content.BlockById(id)?.Key) || !ShapeCode.IsCube(_world.GetShapeIfLoaded(pos));

    /// <summary>#2554: a walking robot standing in water deeper than it wades walks out — straight at the next column
    /// of the shortest walkable path (<see cref="TryFindWaterExit"/>) to the nearest dry ground, at its patrol pace,
    /// re-planned once it stands in a new column or its step is refused (<see cref="WadePlan"/>, at most every
    /// <see cref="MachineWadeReplanSeconds"/>), so each stride goes between two columns the search approved. It neither hunts nor
    /// patrols until it is out (its damage aura still works). A robot with no way out within
    /// <see cref="MachineExitRadius"/> — a flooded pit with sheer walls — powers down: it is removed after the
    /// movement pass, like a machine left far behind. Returns true when it moved.</summary>
    private bool WadeOutOfWater(CombatEntity enemy, List<PlayerSession> targets, int ownX, int ownZ, int feet, double dt)
    {
        // The search runs when there is no plan yet, and again once the robot stands in a new column (normally the step it
        // was walking to) or the planned step failed — at most every MachineWadeReplanSeconds, never per tick.
        bool planned = _wadePlans.TryGetValue(enemy.Id, out var plan);
        plan.Age += dt;
        if (!planned || ((plan.FromX != ownX || plan.FromZ != ownZ || plan.Stale) && plan.Age >= MachineWadeReplanSeconds))
        {
            if (!TryFindWaterExit(ownX, ownZ, feet, out int nextX, out int nextZ))
            {
                _wadePlans.Remove(enemy.Id);
                _strandedMachines.Add(enemy);
                return false;
            }

            plan = new WadePlan { FromX = ownX, FromZ = ownZ, StepX = nextX, StepZ = nextZ };
        }

        _wadePlans[enemy.Id] = plan;
        int stepX = plan.StepX, stepZ = plan.StepZ;

        // The next column's centre in the robot's own (unwrapped) frame: a straight line from inside one column to the
        // centre of a side neighbour crosses only those two columns.
        float dx = stepX + 0.5f - enemy.Position.X;
        float dz = stepZ + 0.5f - enemy.Position.Z;
        float dist = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        float stride = System.Math.Min(MachineWadeOutSpeed * (float)dt, dist);
        if (stride <= 1e-4f)
        {
            return false;
        }

        float nx = (float)WorldConstants.WrapX(enemy.Position.X + (dx / dist * stride), _world.Circumference);
        float nz = (float)WorldConstants.WrapZ(enemy.Position.Z + (dz / dist * stride), _world.Circumference);
        if (!MachineGroundAt((int)System.Math.Floor(nx), (int)System.Math.Floor(nz), feet, realWaterBed: true, out int groundY)
            || groundY - feet > MachineStepUp || feet - groundY > MachineStepDown)
        {
            MarkWadePlanStale(enemy.Id); // the world changed under the plan — search again (throttled)
            return false;
        }

        var candidate = new Vector3f(nx, groundY, nz);
        if (EntityBlockedByShip(candidate) || BlockedByEnergyFence(enemy.Position, candidate))
        {
            MarkWadePlanStale(enemy.Id);
            return false;
        }

        foreach (var s in targets)
        {
            if (WrapDistSq(s.State.Position, candidate) <= EnemyStopRange * EnemyStopRange)
            {
                return false; // never into a player's body (#749)
            }
        }

        enemy.Loco.Heading = (float)System.Math.Atan2(dz, dx);
        enemy.Loco.Turning = false; // #2668: the wade-out sets its own course — no roam turn may pull it back
        enemy.Loco.Speed = MachineWadeOutSpeed;
        enemy.Loco.ModeTimer = 0f; // once out, it picks a fresh patrol heading
        enemy.Position = candidate;
        return true;
    }

    /// <summary>Flags a robot's water-exit plan for a new search (#2554) — its planned step was refused.</summary>
    private void MarkWadePlanStale(string enemyId)
    {
        if (_wadePlans.TryGetValue(enemyId, out var plan))
        {
            plan.Stale = true;
            _wadePlans[enemyId] = plan;
        }
    }

    /// <summary>Drops a robot's water-exit plan (#2554): it is out of the deep water, gone or destroyed.</summary>
    private void ForgetWadePlan(string enemyId)
    {
        if (_wadePlans.Count > 0)
        {
            _wadePlans.Remove(enemyId);
        }
    }

    /// <summary>#2554: a breadth-first search over the columns around a robot standing in deep water, by the robot's
    /// own step rules (up <see cref="MachineStepUp"/>, down <see cref="MachineStepDown"/>, no ship hull, no energy
    /// fence; any water depth on the way — it is in the water already), for the nearest column where the water on
    /// its feet is gone — or, if no dry ground is in reach, one it can wade. Answers the FIRST step of that path (a
    /// side neighbour of the robot's column). Bounded by <see cref="MachineExitRadius"/>; reuses fixed buffers.</summary>
    private bool TryFindWaterExit(int ownX, int ownZ, int feet, out int stepX, out int stepZ)
    {
        stepX = ownX;
        stepZ = ownZ;
        _waterExitSearches++;
        if (++_exitStamp == int.MaxValue)
        {
            System.Array.Clear(_exitSeen, 0, _exitSeen.Length);
            _exitStamp = 1;
        }

        int start = (MachineExitRadius * MachineExitSide) + MachineExitRadius;
        int head = 0, tail = 0;
        _exitQueue[tail++] = start;
        _exitSeen[start] = _exitStamp;
        _exitFeet[start] = feet;
        _exitParent[start] = -1;
        int goal = -1, wade = -1;
        while (head < tail && goal < 0)
        {
            int i = _exitQueue[head++];
            int gx = i % MachineExitSide, gz = i / MachineExitSide;
            int fromFeet = _exitFeet[i];
            int fromX = ownX + gx - MachineExitRadius, fromZ = ownZ + gz - MachineExitRadius;
            foreach (var (ddx, ddz) in WallFillDirs)
            {
                int ngx = gx + ddx, ngz = gz + ddz;
                if (ngx < 0 || ngz < 0 || ngx >= MachineExitSide || ngz >= MachineExitSide)
                {
                    continue;
                }

                int j = (ngz * MachineExitSide) + ngx;
                if (_exitSeen[j] == _exitStamp)
                {
                    continue;
                }

                int cx = fromX + ddx, cz = fromZ + ddz;
                if (!MachineGroundAt(cx, cz, fromFeet, realWaterBed: true, out int f)
                    || f - fromFeet > MachineStepUp || fromFeet - f > MachineStepDown)
                {
                    continue; // not from here — another neighbour may still reach it
                }

                var from = new Vector3f(WorldConstants.WrapX(fromX, _world.Circumference) + 0.5f, fromFeet,
                    WorldConstants.WrapZ(fromZ, _world.Circumference) + 0.5f);
                var to = new Vector3f(WorldConstants.WrapX(cx, _world.Circumference) + 0.5f, f,
                    WorldConstants.WrapZ(cz, _world.Circumference) + 0.5f);
                if (EntityBlockedByShip(to) || BlockedByEnergyFence(from, to))
                {
                    continue;
                }

                _exitSeen[j] = _exitStamp;
                _exitFeet[j] = f;
                _exitParent[j] = i;
                int depth = MachineWaterDepth(cx, cz, f);
                if (depth == 0)
                {
                    goal = j;
                    break;
                }

                if (depth <= MachineWadeDepth && wade < 0)
                {
                    wade = j;
                }

                _exitQueue[tail++] = j;
            }
        }

        if (goal < 0)
        {
            goal = wade;
        }

        if (goal < 0)
        {
            return false;
        }

        int k = goal;
        while (_exitParent[k] != start)
        {
            k = _exitParent[k];
        }

        stepX = ownX + (k % MachineExitSide) - MachineExitRadius;
        stepZ = ownZ + (k / MachineExitSide) - MachineExitRadius;
        return true;
    }

    /// <summary>Drops the robots that found no way out of deep water this tick (#2554) — after the movement pass, which
    /// iterates the list. Returns true when any went.</summary>
    private bool RemoveStrandedMachines()
    {
        if (_strandedMachines.Count == 0)
        {
            return false;
        }

        foreach (var e in _strandedMachines)
        {
            _planetEnemies.Remove(e);
            _enemyWander.Remove(e.Id);
            _enemySightSeenAt.Remove(e.Id);
            ForgetWadePlan(e.Id);
        }

        _strandedMachines.Clear();
        return true;
    }

    /// <summary>A walking Guardian robot (not the hovering scan-drone, not a bandit) — the machines the water rule binds.</summary>
    private static bool IsWalkingMachine(CombatEntity e) => e.Kind is CombatEntityKind.Creature or CombatEntityKind.AlienMonster;

    /// <summary>The water on a planet machine's or bandit's feet for the /bump snapshot (#2555): "dry", "wading" or
    /// "deep" — measured at its own cell (a hovering drone's body, not the ground under it).</summary>
    private string MachineWaterWord(CombatEntity e)
        => MachineWaterDepth((int)System.Math.Floor(e.Position.X), (int)System.Math.Floor(e.Position.Z), (int)System.Math.Floor(e.Position.Y)) switch
        {
            0 => "dry",
            1 => "wading",
            _ => "deep",
        };

    /// <summary>What a planet machine or bandit is doing, in a word, for the /bump snapshot (#2555): dazed by a push,
    /// leaving deep water, or its locomotion mode — chasing (it locked on to a player, or a bandit heads for its mark /
    /// camp), patrolling, pausing between patrol legs, fleeing.</summary>
    private string MachineMotionWord(CombatEntity e)
    {
        if (IsStaggered(e))
        {
            return "dazed";
        }

        if (IsWalkingMachine(e) && MachineWaterDepth((int)System.Math.Floor(e.Position.X), (int)System.Math.Floor(e.Position.Z),
                (int)System.Math.Floor(e.Position.Y)) > MachineWadeDepth)
        {
            return "leavingWater";
        }

        return e.Loco.Mode switch
        {
            MoveMode.Seek => "chasing",
            MoveMode.Flee => "fleeing",
            MoveMode.Pause => "pausing",
            _ => "patrolling",
        };
    }

    /// <summary>Returns <paramref name="to"/> expressed in <paramref name="from"/>'s local frame across the world's
    /// wrap seams, so a direction computed as (result - from) takes the short way round.</summary>
    private Vector3f Unwrapped(Vector3f from, Vector3f to) => new(
        from.X + (float)WorldConstants.WrapDeltaX(to.X - from.X, _world.Circumference),
        to.Y,
        from.Z + (float)WorldConstants.WrapDeltaZ(to.Z - from.Z, _world.Circumference));

    /// <summary>Spots a walking robot's spawn tries (#2554) before it gives up for this interval: the classic spot first,
    /// then the next golden-angle slots around the same anchor.</summary>
    private const int PlanetEnemySpawnTries = 6;

    /// <summary>Spawns one planet machine near <paramref name="player"/> (or at a wreck / SPS lab near them). A walking
    /// robot never appears with water on its feet (#2554): a wet spot is passed over for the next slot, and when every
    /// try is wet nothing spawns (returns false). A hovering scan-drone takes the classic spot as before. Neither ever
    /// appears in a gas giant's gas sea (#2586): a robot needs an island or a deck under it, a drone hovers over the gas.</summary>
    private bool SpawnPlanetEnemyNear(Shared.State.PlayerState player, bool asDrone)
    {
        // Frontier danger (#1122, opt-in world rule): out in the full-frontier tier the machines hit like
        // the Frequent/Extreme ones — never in the home system, never unless the admin switched it on
        // (family/peaceful setups have PlanetEnemies Off and never even spawn these).
        bool tougher = !asDrone && (Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme
            || (Rules.FrontierDanger && FrontierTierForBody(_world.LocationId) >= 2));

        int n = _planetEnemies.Count;

        // Count-neutral wreck coupling (P5): when a wreck is near the player, bias THIS spawn's position to
        // cluster at the wreck (the count + cadence are unchanged — only where it appears), and make a
        // wreck-spawned machine hit harder. Crashed network tech becomes a danger zone you learn to read.
        bool atWreck = false;
        float wreckX = 0f, wreckZ = 0f;
        if (Rules.MachineWreckCoupling && _worlds.Active.WreckStamped && _worlds.Active.WreckMarkers.Count > 0)
        {
            var markers = _worlds.Active.WreckMarkers;
            float cx = (float)markers.Average(m => m.Pos.X);
            float cz = (float)markers.Average(m => m.Pos.Z);
            float wdx = (float)WorldConstants.WrapDeltaX(cx - player.Position.X, _world.Circumference);
            float wdz = cz - player.Position.Z;
            if (wdx * wdx + wdz * wdz <= WreckCouplingRange * WreckCouplingRange)
            {
                atWreck = true;
                wreckX = cx;
                wreckZ = cz;
            }
        }

        // 2026-09 (Titas): near an abandoned SPS lab the machines gather at the lab — they still guard it.
        var lab = atWreck ? null : NearestSpsLab(player.Position, SpsLabGuardRange);

        int ex = 0, ez = 0, ey = 0;
        bool placed = false;
        for (int attempt = 0; attempt < (asDrone ? 1 : PlanetEnemySpawnTries) && !placed; attempt++)
        {
            int slot = n + attempt; // attempt 0 is the classic spot
            double ang = slot * 2.39996323; // golden angle (radians)
            if (atWreck)
            {
                float wr = 4f + (slot % 4) * 3f; // 4..13 blocks around the wreck centroid
                // Leave ex unwrapped (like the golden-angle path) so it stays in the same coordinate space as
                // the wreck markers; SurfaceHeight wraps internally.
                ex = (int)System.Math.Round(wreckX + System.Math.Cos(ang) * wr);
                ez = (int)System.Math.Round(wreckZ + System.Math.Sin(ang) * wr);
            }
            else if (lab is { } l)
            {
                float lr = 20f + (slot % 4) * 4f; // 20..32 blocks around the compound's centre — just outside its modules
                ex = (int)System.Math.Round(l.X + System.Math.Cos(ang) * lr);
                ez = (int)System.Math.Round(l.Z + System.Math.Sin(ang) * lr);
            }
            else
            {
                // Spawn well OUTSIDE the 28-block detection range (9–13 felt like an ambush): fiends appear
                // 35–50 blocks out, roam the area on wander headings, and only start hunting when the player
                // comes near them. Spread around the player with the golden angle, then drop onto the actual
                // surface at that column so they never spawn buried in the terrain.
                float dist = 35f + (slot % 4) * 5f; // 35..50 blocks out — beyond EnemyHuntRange
                ex = (int)System.Math.Round(player.Position.X + System.Math.Cos(ang) * dist);
                ez = (int)System.Math.Round(player.Position.Z + System.Math.Sin(ang) * dist);
            }

            // Stand on the ground, not in it — real blocks when the column is loaded, noise surface otherwise. #2586: on a gas
            // giant the probe starts from the real top of the column above the gas, a drone hovers over the gas where nothing
            // stands, and a walking robot with no island or deck under it is not placed. #2950: a drone's probe starts at the
            // live top of the column (a raised base, not the tunnel under it) and its hover must be clear.
            int refY = asDrone ? DroneSpawnRefY(ex, ez) : GroundProbeRefY(ex, ez);
            if (asDrone)
            {
                ey = DroneGroundAt(ex, ez, refY) + ScanDroneHover; // the flying scan-drone hovers above the surface
                placed = !FeetInGasSeaAt(ex, ey, ez) && DroneBodyClear(ex, ez, ey - ScanDroneHover, ScanDroneHover);
            }
            else
            {
                ey = WalkerFeetAt(ex, ez, refY);
                placed = MachineWaterDepth(ex, ez, ey) == 0 // #2554: no water on a walking robot's feet
                    && !FeetInGasSeaAt(ex, ey, ez);
            }
        }

        if (!placed)
        {
            return false;
        }

        _planetEnemies.Add(NewPlanetMachine(new Vector3f(ex, ey, ez), asDrone, tougher, atWreck ? 1.5f : 1f)); // wreck machines are angrier
        return true;
    }

    /// <summary>A planet machine's body and stats — a hovering scan-drone, a walking Guardian robot or (with
    /// <paramref name="tougher"/>) the hunter — shared by the surface spawner and a derelict's guardian bay (#2635).</summary>
    private CombatEntity NewPlanetMachine(Vector3f at, bool asDrone, bool tougher, float damageFactor = 1f)
    {
        // #2971: the stats are data/enemies.json's. The scan drone's 3/s was written for an aura it could never enter; now
        // that it really does hurt from its firing range it is in reach for most of an encounter, so it is 2/s — roughly
        // 50 s of being ignored before it kills you, which is pressure without being a young player's ambush.
        var machine = asDrone ? NewEnemy(EnemyCatalog.ScanDrone, EnemyTiers.Normal, CombatEntityKind.ScanDrone, at)
            : tougher ? NewEnemy(EnemyCatalog.Hunter, EnemyTiers.Normal, CombatEntityKind.AlienMonster, at)
            : NewEnemy(EnemyCatalog.GuardianRobot, EnemyTiers.Normal, CombatEntityKind.Creature, at);
        machine.DamagePerSecond *= damageFactor; // a wreck's machines are angrier
        return machine;
    }

    private const float WreckCouplingRange = 64f; // bias spawns to a wreck within this of the player (P5)
    private const int ScanDroneHover = 4;          // blocks the flying scan-drone floats above the surface (P4)

    /// <summary>Player attacks a planet enemy or creature with the held tool/weapon. Server resolves the hit.
    /// The optional aim direction is the client's camera ray at the moment of firing (#693); a zero vector
    /// (older client) skips the aim validation. An aim with a NaN or infinite component is garbage no client
    /// sends — the attack is ignored, so it can never slip past the aim check or turn a push into a NaN position.</summary>
    public void AttackEntity(string playerId, string entityId, float dirX = 0f, float dirY = 0f, float dirZ = 0f)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || !float.IsFinite(dirX) || !float.IsFinite(dirY) || !float.IsFinite(dirZ))
        {
            return;
        }

        var dir = new Vector3f(dirX, dirY, dirZ);
        if (PvpRules.TryPlayerId(entityId, out var victim))
        {
            AttackPlayer(session, victim, dir); // #2972: on-foot PvP, under the world's PvP rules
            return;
        }

        if (_planetEnemies.FirstOrDefault(e => e.Id == entityId) is { } enemy)
        {
            AttackCombatEntity(session, enemy, _planetEnemies, isCreature: false, dir);
            return;
        }

        if (_creatures.FirstOrDefault(e => e.Id == entityId) is { } creature)
        {
            // Companions and pets — the player's own, another player's, a tamer NPC's — are refused inside
            // AttackCombatEntity (#2281), with a friendly line instead of the old "no such target".
            AttackCombatEntity(session, creature, _creatures, isCreature: true, dir);
            return;
        }

        if (_bandits.FirstOrDefault(e => e.Id == entityId) is { } bandit)
        {
            AttackCombatEntity(session, bandit, _bandits, isCreature: false, dir);
            return;
        }

        Reject(session, "attack", "@srv.attack.no_target");
    }

    private const double MeleeCooldown = 1.5;                       // melee weapons swing at most this often (B44)
    private readonly Dictionary<string, double> _meleeReadyAt = new(); // playerId → uptime the next melee swing is allowed
    private readonly Dictionary<string, double> _fistReadyAt = new();  // playerId → uptime the next bare-hand punch is allowed (#2280)

    /// <summary>True for a creature nobody may attack (#2281): a tamed companion — the player's own, another player's —
    /// or a tamer NPC's pet. The parents' guide promises that friends cannot harm each other, and a pet is part of a
    /// friend. The one rule for every player-driven hit and push on a creature.</summary>
    private static bool ProtectedFromPlayers(CombatEntity creature) => creature.IsCompanion;

    private void AttackCombatEntity(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature, Vector3f aimDir = default)
    {
        var p = session.State;
        if (isCreature && ProtectedFromPlayers(target))
        {
            Reject(session, "attack", "@srv.attack.companion"); // #2281: before any cooldown is spent
            return;
        }

        var tool = ActiveTool(p);
        bool isWeapon = tool.Kind == ToolKind.Weapon;
        if (!SwingReady(session, tool, isWeapon)) // the weapon's cooldown or the hand's rhythm (GameServerSwing)
        {
            return;
        }

        // A ranged weapon's longer reach extends the default; a melee weapon never *reduces* it below the
        // default swing reach (the client targets any creature within EnemyAttackReach, so a short melee
        // range like the machete's must not silently reject those hits — equipping a weapon must never make
        // you worse than bare fists).
        float reach = isWeapon ? System.Math.Max(tool.Range, EnemyAttackReach) : EnemyAttackReach;

        // #1998: a giant is not one point at its feet — reach, sightline and aim are measured to the nearest point of its
        // body (the legs of a colossus, the part of a sandworm above the sand). A buried sandworm cannot be hit.
        var aimAt = target.Position;
        if (isCreature && target.IsGiant)
        {
            if (!GiantHittable(target))
            {
                Reject(session, "attack", "@srv.attack.no_target");
                return;
            }

            aimAt = GiantAimPoint(target, p.Position);
        }

        if (WrapDistSq(p.Position, aimAt) > reach * reach)
        {
            Reject(session, "attack", "@srv.attack.out_of_reach");
            return;
        }

        if (!ValidateAim(session, target, tool, aimDir, aimAt))
        {
            return;
        }

        if (!PayShot(session, tool, isWeapon)) // energy weapons draw suit energy per shot
        {
            return;
        }

        target.Hull -= SwingDamage(p, tool, isWeapon);
        NoteCombat(session); // #2286: a blow landed — "Back to my ship" waits out the fight

        // 2026-09 (Valuma): a hit turns the shapeshifter on its attacker; at zero its disguise breaks instead of it dying.
        if (isCreature && IsSreekmakra(target) && OnSreekmakraHit(session, target))
        {
            return;
        }

        if (isCreature)
        {
            // Any hit — surviving or fatal — startles the victim's nearby kin (#653): non-retaliating
            // herd members scatter instead of grazing on beside the corpse; retaliators charge instead.
            StartleKin(target);
        }

        if (target.Hull > 0f)
        {
            if (isCreature && target.IsGiant)
            {
                OnGiantHit(target); // #1998: a giant answers a hit by its own rules
                BroadcastCreatures();
                return;
            }

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

            // #2278: the shock gloves push the survivor away and daze it (never a player, a pet or a giant).
            if (isWeapon)
            {
                ApplyKnockback(session, target, tool, isCreature, aimDir);
            }

            if (isCreature) BroadcastCreatures(); else BroadcastPlanetEnemies();
            return;
        }

        list.Remove(target);
        _enemyWander.Remove(target.Id); // drop the dead enemy's wander state
        ForgetWadePlan(target.Id);
        var pool = new MaterialPool(_content, p, _ship);
        // The kill already happened, so the loot cannot be refused — a full inventory drops it at the fallen
        // enemy's feet instead of losing it (#853).
        BankLoot(session, pool, target.Loot, spillAt: target.Position.ToBlock(), creatureLoot: isCreature);
        if (isCreature)
        {
            BioOnCreatureDefeated(session, target); // #2201: a sample of its species, with the loot
            ForgetClone(target); // #2214: a defeated clone does not come back beside its tank
        }

        SendInventory(session);
        OnAchievementDefeat(session);
        RaiseGameEvent(session, GameEventKeys.DefeatAny, // #2465
            isCreature ? (target.IsGiant ? GameEventKeys.DefeatGiant : GameEventKeys.DefeatCreature)
            : target.IsBandit ? GameEventKeys.DefeatBandit : GameEventKeys.DefeatMachine);
        if (isCreature)
        {
            OnCreatureKilled(target, session); // 2026-09: the shapeshifter's death, or one of its shape's kind
            if (target.IsGiant)
            {
                OnGiantDefeated(target, session); // #1998: the return clock, the achievement, the witnesses
            }

            BroadcastCreatures();
        }
        else
        {
            BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });
            if (target.IsBandit)
            {
                OnBanditKilled(target, session); // bandits are people, not machines — no story credit, but camps clear (#2599: a deed)
                OnMissionDefeat(session, DefeatTargetBandit); // #730: bounty objectives count the drive-off
                OnScoutDefeated(session, target); // #1224: a beaten base scout credits the homestead bounty
            }
            else
            {
                RecordStoryMachineKill(); // planet machine destroyed → advances the story (P4: combat-as-progress)
                OnMachineDefeated(session); // #1213: post-win only — the survey orders' Defeat step

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
    /// client, or a melee swing) skips the angle checks. With AutoAim ON the target only has to sit
    /// in a wide forward cone; with AutoAim OFF the crosshair ray must actually pass near the target's body.
    /// Every attack, at any range, needs a clear sightline — no hitting through walls.</summary>
    private bool ValidateAim(PlayerSession session, CombatEntity target, ToolProperties tool, Vector3f aimDir,
        Vector3f? aimPoint = null)
    {
        var p = session.State;
        var targetPos = aimPoint ?? target.Position; // #1998: a giant's nearest body point

        // Walls stop attacks — ALL of them, and BEFORE the aim-data check.
        //
        // This used to be gated on `ranged` (a weapon with Range > 6), which left every melee weapon free to
        // swing straight through a wall: the machete and vibro-knife reach 3.5 blocks, the plasma sword 4, and
        // the swing reach is 6 regardless — comfortably enough to hit somebody on the other side of a
        // one-block wall. Worse, the aim-data early-return above it meant a client that simply sent a zero aim
        // vector skipped the wall check too, ranged shots included. A player reported hitting through walls in
        // both directions; the enemy→player half was already sightline-gated everywhere
        // (see the proximity damage above, GameServerCreatures and GameServerBandits), so this side was it.
        //
        // Same voxel sightline the machines use, so a wall is cover whoever is shooting; glass blocks it too,
        // being Solid. One difference (#2533): the player's shot passes what the player walks through — leaves,
        // grass and other flora, a torch — while the machines' sight keeps treating foliage as cover.
        if (!HasAttackLineOfSight(p.Position, targetPos))
        {
            Reject(session, "attack", "@srv.attack.no_line");
            return false;
        }

        float dirLenSq = aimDir.X * aimDir.X + aimDir.Y * aimDir.Y + aimDir.Z * aimDir.Z;
        if (dirLenSq < 0.0001f)
        {
            return true; // no aim data (older client) — keep the legacy range-only behaviour for the ANGLE
        }

        bool ranged = MeleeRules.IsRanged(tool); // #2800: the client holds and draws a weapon by the same rule

        const float eye = 1.5f; // matches HasLineOfSight/the client camera height
        var dst = Unwrapped(p.Position, targetPos);
        float tx = dst.X - p.Position.X;
        float ty = (dst.Y + (aimPoint is null ? 0.9f : eye)) - (p.Position.Y + eye); // aim roughly at the body, not the feet
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
                Reject(session, "attack", "@srv.attack.missed");
                return false;
            }

            return true;
        }

        if (dot < 0.35f) // ~70° half-angle: forgiving even for a swirling melee fight, but never behind the back
        {
            Reject(session, "attack", "@srv.attack.not_ahead");
            return false;
        }

        return true;
    }

    // Bandits ride the planet-enemy wire (same list message), so client targeting/health bars/defeat
    // handling work unchanged — the client tells them apart by the Kind string.
    private void BroadcastPlanetEnemies() => _worlds.Active.EnemyListDirty = true; // #1530: flushed once per tick

    private void SendPlanetEnemyList()
        => BroadcastToWorld(new PlanetEnemyList { Enemies = _planetEnemies.Concat(_bandits).Select(ToNet).ToArray(), ServerTime = SnapshotTime });

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
        => SpawnPlanetEnemyAtForTest(at, CombatEntityKind.Creature, damagePerSecond);

    /// <summary>Test/util: as above, but choosing the KIND — the scan-drone damages at its firing range
    /// rather than by touch, so its regression needs to spawn that specific machine.</summary>
    public void SpawnPlanetEnemyAtForTest(Vector3f at, CombatEntityKind kind, float damagePerSecond = 20f)
        => _planetEnemies.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = kind,
            Hostile = true,
            Hull = 30f,
            HullMax = 30f,
            Position = at,
            DamagePerSecond = damagePerSecond,
        });

    /// <summary>Test/util: run one enemy tick for the active world without the rest of the simulation.</summary>
    public void TickEnemiesForTest(double dt) => TickEnemies(dt);

    /// <summary>Test seam (#2554): the water standing on feet at (<paramref name="x"/>, <paramref name="feetY"/>,
    /// <paramref name="z"/>) as a walking robot measures it — 0 dry, 1 wading, 2 too deep.</summary>
    public int MachineWaterDepthForTest(int x, int z, int feetY) => MachineWaterDepth(x, z, feetY);

    /// <summary>Test seam (#2554): how many water-exit searches have run — once per column crossed, not per tick.</summary>
    public int WaterExitSearchesForTest => _waterExitSearches;
}

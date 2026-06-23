using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Procedural creatures / fauna (technical requirements / `anf_space_flight.md` §12). Each world
/// derives its own species roster from the seed + planet (see <see cref="CreatureGenerator"/>);
/// live creatures spawn near surface players within the world's biodiversity cap. Behaviour is
/// server-authoritative:
/// <list type="bullet">
/// <item>Most species are <b>not hostile</b> (passive/skittish/territorial) — they wander and do
/// no damage; only aggressive/pack-hunter species attack, and only while <b>active</b> (a
/// diurnal/nocturnal/crepuscular cycle — they <b>sleep</b> in their off-phase) and only where the
/// hostility rules allow it (peaceful servers → no creature damage, §12.4).</item>
/// <item>Defeating/harvesting a creature drops its species material — which may be a building-
/// material <b>substitute</b>, <b>food</b> (edible) or a <b>poison</b> (toxic). Eating food heals;
/// eating poison harms (the consume system).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const double CreatureSpawnInterval = 4.0;
    private const float CreatureProximityRange = 4f;
    private const int CreatureCapPerPlayer = 4;
    private const float CreatureDespawnRange = 70f; // creatures far from every player despawn (frees the cap)
    private const double CreatureBroadcastInterval = 0.5;  // position-sync cadence (client interpolates)
    private const double CreatureMoveDtCap = 0.25;         // cap per-step movement so big ticks can't teleport
    private const float CreatureAggroRange = 8f;           // hunters approach within this (B18: smaller → less hounding)
    private const float CreatureFleeRange = 6f;            // skittish flee within this
    private const double CreatureProvokeSeconds = 12.0;    // how long a provoked creature retaliates
    private const float CreaturePackRallyRange = 14f;      // pack-hunters rally kin within this
    private const double CreatureChaseGiveUpSeconds = 7.0;     // an aggressor gives up after chasing this long
    private const double CreatureGiveUpCooldownSeconds = 15.0; // ...then leaves you alone (no chase/attack) for this
    private const double CreatureBlindChaseGiveUpRate = 2.0;   // ...tiring twice as fast while it can't see you (hide to shake it ~halve the time)
    private const float CreatureWakeDistance = 4f;             // a sleeping creature stirs awake when a player comes this close
    private const double CreatureWakeSeconds = 9.0;            // ...and then stays roused (alert/active) for this long

    private CreatureSpecies[] _speciesRoster = System.Array.Empty<CreatureSpecies>();
    private readonly List<PlayerSession> _creatureTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private readonly Dictionary<string, CreatureSpecies> _speciesById = new();
    private readonly Dictionary<string, LocomotionProfile> _locoProfiles = new(); // per-species movement tuning
    private List<CombatEntity> _creatures => _worlds.Active.Creatures;
    private double _creatureSpawnTimer { get => _worlds.Active.CreatureSpawnTimer; set => _worlds.Active.CreatureSpawnTimer = value; }
    private double _creatureClock { get => _worlds.Active.CreatureClock; set => _worlds.Active.CreatureClock = value; }
    private double _creatureBroadcastTimer { get => _worlds.Active.CreatureBroadcastTimer; set => _worlds.Active.CreatureBroadcastTimer = value; }
    private int _creatureSpawnRotor { get => _worlds.Active.CreatureSpawnRotor; set => _worlds.Active.CreatureSpawnRotor = value; }
    private ushort _creatureWaterId, _creatureLavaId;

    /// <summary>Live creatures on the surface (passive + hostile fauna).</summary>
    public IReadOnlyList<CombatEntity> Creatures => _creatures;

    /// <summary>Wild fauna only (excludes tamed companions) — companions don't count against the world's cap.</summary>
    private int WildCreatureCount => _creatures.Count(c => !c.IsCompanion);

    /// <summary>The procedural species this world derived from its seed + planet.</summary>
    public IReadOnlyList<CreatureSpecies> SpeciesRoster => _speciesRoster;

    private void InitCreatures()
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        _speciesRoster = planet is null
            ? System.Array.Empty<CreatureSpecies>()
            : CreatureGenerator.GenerateRoster(planet, _meta.Seed).ToArray();

        _speciesById.Clear();
        _locoProfiles.Clear();
        foreach (var sp in _speciesRoster)
        {
            _speciesById[sp.Id] = sp;
            _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        }

        _creatures.Clear();
        _creatureSpawnTimer = 0;
        _creatureClock = 0;
        _creatureBroadcastTimer = 0;
        _creatureSpawnRotor = 0;
        _creatureWaterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _creatureLavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
    }

    // --- Day/night activity (ties into the World-systems clock) ---

    private bool IsNight => TimeOfDay < 0.25f || TimeOfDay > 0.75f;

    private bool IsDawnOrDusk => (TimeOfDay >= 0.20f && TimeOfDay <= 0.30f)
                                 || (TimeOfDay >= 0.70f && TimeOfDay <= 0.80f);

    /// <summary>Whether a species is awake/active right now (else it is sleeping/resting).</summary>
    private bool SpeciesActive(CreatureSpecies s) => s.Activity switch
    {
        CreatureActivity.Diurnal => !IsNight,
        CreatureActivity.Nocturnal => IsNight,
        CreatureActivity.Crepuscular => IsDawnOrDusk,
        _ => true, // Cathemeral
    };

    /// <summary>This world's live-fauna cap (2026-06-10 — "belebte Planeten"): no fixed global limit. Each
    /// world derives its own population from its <c>CreatureAbundance</c>, its SIZE (bigger planets carry
    /// more fauna) and a seeded per-world jitter — so the same planet type can be teeming on one world and
    /// sparse on the next — scaled gently by how many players are on the surface. Typical results: a lush
    /// big world ~25–45 live creatures around the players, a sparse small one ~5–9 (old fixed cap: 12).</summary>
    private int WorldCreatureCap(int players)
    {
        double baseN = _world.Planet.CreatureAbundance?.ToLowerInvariant() switch
        {
            "many" => 20.0,
            "none" => 0.0,
            _ => 10.0, // "few" / default
        };

        // World options: the creature-abundance rule scales every world's population (live-editable).
        baseN *= Rules.CreatureAbundance switch
        {
            Shared.Configuration.AlienActivity.Off => 0.0,
            Shared.Configuration.AlienActivity.Rare => 0.5,
            Shared.Configuration.AlienActivity.Frequent => 1.5,
            Shared.Configuration.AlienActivity.Extreme => 2.2,
            _ => 1.0,
        };

        if (baseN <= 0)
        {
            return 0;
        }

        double size = System.Math.Clamp(_world.Circumference / 6000.0, 0.5, 1.8);
        uint h = (uint)WorldGenerator.StableHash($"fauna:{_meta.Seed}:{_worlds.Active.LocationId}");
        double jitter = 0.7 + 0.6 * (h % 1000 / 999.0); // 0.7..1.3, stable per world
        return (int)System.Math.Round(baseN * size * jitter * System.Math.Sqrt(System.Math.Max(1, players)));
    }

    private void TickCreatures(double dt)
    {
        // Orbital stations (void worlds) have no wildlife at all — only peaceful NPCs.
        if (_world.Planet.Void)
        {
            return;
        }

        if (_speciesRoster.Length == 0)
        {
            return; // barren world — no life
        }

        // Companions follow their owners — keep their presence in sync with who is on this world (runs even when
        // nobody is on foot, so a pet despawns the moment its owner flies off / boards into space).
        if (ReconcileCompanions())
        {
            BroadcastCreatures();
        }

        // Reuse a field list instead of allocating a fresh Where(...).ToList() every tick (15 Hz).
        _creatureTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId))
            {
                _creatureTargets.Add(s);
            }
        }

        var targets = _creatureTargets;
        if (targets.Count == 0)
        {
            return;
        }

        int cap = WorldCreatureCap(targets.Count);
        _creatureSpawnTimer += dt;
        // Fill faster while the world is far below its cap (a freshly visited world comes alive quickly),
        // then ease to the slow trickle near the cap.
        double interval = WildCreatureCount < cap / 2 ? 1.5 : CreatureSpawnInterval;
        if (_creatureSpawnTimer >= interval && WildCreatureCount < cap)
        {
            _creatureSpawnTimer = 0;
            if (TrySpawnCreatureNear(targets[_creatures.Count % targets.Count].State))
            {
                BroadcastCreatures();
            }
        }

        MoveCreatures(targets, dt);

        // Despawn creatures that drifted far from every player so the cap frees up and fauna keeps
        // appearing around players as they explore — life is spread across the whole planet, not just
        // stuck at the start area. (Travel clears creatures entirely via ResetWorldRuntimeState.)
        if (PruneFarCreatures(targets))
        {
            BroadcastCreatures();
        }

        // Position-sync cadence so clients can interpolate wandering/fleeing/hunting creatures.
        _creatureBroadcastTimer += dt;
        if (_creatures.Count > 0 && _creatureBroadcastTimer >= CreatureBroadcastInterval)
        {
            _creatureBroadcastTimer = 0;
            BroadcastCreatures();
        }

        // Only hostile, awake creatures hurt the player — and only where the hostility rules allow
        // it (peaceful servers keep wildlife harmless). Passive/sleeping creatures never damage.
        if (!PlanetEnemiesActive)
        {
            return;
        }

        foreach (var creature in _creatures)
        {
            if (creature.IsCompanion)
            {
                continue; // a tamed companion never harms anyone (even if its species is a hostile kind)
            }

            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            if (creature.FrozenTimer > 0)
            {
                continue; // held in stasis (item 36) — can't bite while frozen, so you can scan it safely
            }

            // Hostile species attack; so do provoked (territorial) creatures fighting back.
            bool aggressiveNow = sp.Hostile || creature.ProvokeTimer > 0;
            if (!aggressiveNow || !SpeciesActive(sp))
            {
                continue;
            }

            // A creature that has given up the chase backs off and won't bite until its cooldown lapses.
            if (creature.GiveUpTimer > 0)
            {
                continue;
            }

            foreach (var session in targets)
            {
                var p = session.State;
                if (p.GodMode || p.Stealthed) // cloaked players aren't detected
                {
                    continue;
                }

                if (WrapDistSq(p.Position, creature.Position) <= CreatureProximityRange * CreatureProximityRange
                    && HasLineOfSight(creature.Position, p.Position)) // no bite through a wall — break sight (cover/cave) to stop it
                {
                    float bite = (float)(creature.DamagePerSecond * dt);

                    // While driving a speeder its hull soaks most of the bite (the player is shielded by the chassis).
                    if (TryGetDrivenSpeeder(p, out var speeder))
                    {
                        DamageSpeeder(speeder, bite * SpeederCreatureDamageShare, "wildlife");
                        bite *= 1f - SpeederCreatureDamageShare;
                    }

                    p.Health = System.Math.Max(0f, p.Health - Mitigate(p, bite));
                    SendPlayerState(session);
                    if (p.Health <= 0f)
                    {
                        RespawnPlayer(session, "Killed by hostile wildlife — recovery to the Medbay heal-tank.");
                    }
                }
            }
        }
    }

    private const int CreatureHardCap = 12;

    // Offsets scattered around the player at ~18-45 blocks so fauna appears spread out across the
    // surroundings — not stacked on one spot and not right on top of the player's ship/landing site.
    // Mixed radii and angles (an inner, a mid and an outer band) keep encounters from feeling ringed.
    private static readonly (int Dx, int Dz)[] SpawnRing =
    {
        (18, 5), (13, 16), (3, 22), (-12, 18), (-21, 7), (-19, -12), (-9, -20), (8, -19),
        (28, 10), (15, 27), (-8, 31), (-30, 16), (-33, -14), (-14, -30),
        (12, -32), (34, -18), (40, 12), (-24, 38), (-42, -16), (16, -41),
    };

    // Outward probe offsets (radius 0..8) used to find a water column near a dry ring spot, so aquatic life
    // can spawn in a lake/sea the player is standing beside even though the chosen ring cell is on land.
    private static readonly (int Dx, int Dz)[] WaterProbe =
    {
        (0, 0),
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
        (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, 3), (3, -3), (-3, -3),
        (5, 0), (-5, 0), (0, 5), (0, -5), (5, 5), (-5, 5), (5, -5), (-5, -5),
        (8, 0), (-8, 0), (0, 8), (0, -8),
    };

    /// <summary>Finds the nearest water column (global sea or upland pond) to a spot, returning its
    /// coordinates and the water-surface / seabed Y. False if no water is within the probe radius.</summary>
    private bool TryFindWaterColumnNear(int x, int z, out int wx, out int wz, out int waterTopY, out int seabedY)
    {
        foreach (var (dx, dz) in WaterProbe)
        {
            if (_generator.TryGetWaterSurface(_world.Planet, x + dx, z + dz, out waterTopY, out seabedY))
            {
                wx = x + dx;
                wz = z + dz;
                return true;
            }
        }

        wx = x;
        wz = z;
        waterTopY = 0;
        seabedY = 0;
        return false;
    }

    /// <summary>
    /// Spawns one roster species suited to a spread-out spot around the player (habitat-gated, on the
    /// ground). Returns true if one spawned. The rotor advances both the ring slot and the species so
    /// repeated calls scatter different creatures around the player.
    /// </summary>
    private bool TrySpawnCreatureNear(Shared.State.PlayerState player)
    {
        if (WildCreatureCount >= CreatureHardCap)
        {
            return false;
        }

        var (dx, dz) = SpawnRing[_creatureSpawnRotor % SpawnRing.Length];
        int x = (int)System.Math.Floor(player.Position.X) + dx;
        int z = (int)System.Math.Floor(player.Position.Z) + dz;
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        int biome = _generator.BiomeIndexAt(_world.Planet, x, z);

        // Two passes: first only species native to this biome (so a multi-biome world shows different fauna in
        // different regions), then any species — so a biome never goes empty if none of its natives fit here.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int n = 0; n < _speciesRoster.Length; n++)
            {
                var sp = _speciesRoster[(_creatureSpawnRotor + n) % _speciesRoster.Length];
                if (pass == 0 && sp.BiomeAffinity >= 0 && sp.BiomeAffinity != biome)
                {
                    continue; // not native to this biome (relaxed on the second pass)
                }

                float y;
                int px = x, pz = z; // the actual spawn column (water species relocate to nearby water)
                if (sp.Habitat == CreatureHabitat.Cave)
                {
                    int caveY = FindCaveFloorY(x, z, surface);
                    if (caveY < 0)
                    {
                        continue; // no open cave under this spot — try another species/spot
                    }

                    y = caveY;
                }
                else if (sp.Habitat == CreatureHabitat.Water || sp.Habitat == CreatureHabitat.Amphibian)
                {
                    // Aquatic life must spawn IN water. The ring spot is usually dry land, and the visible lakes
                    // (upland ponds) fill flush to the surface — so probing surface+1 always hit air and water
                    // creatures never spawned. Seek the nearest water column (sea/pond) around the spot and place
                    // inside it; skip the species if there's no water nearby (no land fallback).
                    if (!TryFindWaterColumnNear(x, z, out px, out pz, out int waterTopY, out int seabedY))
                    {
                        continue;
                    }

                    // Swimmers sit mid-water; amphibians wade up at the surface cell (still counts as water).
                    y = sp.Habitat == CreatureHabitat.Water
                        ? (seabedY + 1 + waterTopY) * 0.5f
                        : waterTopY;
                }
                else
                {
                    y = surface + (sp.Habitat == CreatureHabitat.Air ? 4f : 1f);
                }

                var pos = new Vector3f(px + 0.5f, y, pz + 0.5f);
                if (!HabitatSuitable(sp, pos) || EntityBlockedByShip(pos))
                {
                    // Reject the SAME volume the movement barrier guards (not just the tight interior box), so a
                    // creature never spawns in the thin shell where it would immediately be frozen against the hull.
                    continue; // never spawn inside (or clipping into) a landed ship
                }

                _creatureSpawnRotor = (_creatureSpawnRotor + n + 1) % _speciesRoster.Length;
                SpawnCreature(sp, pos);
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds a live creature of the species at the position.</summary>
    private void SpawnCreature(CreatureSpecies sp, Vector3f pos)
    {
        string id = NextEntityId();
        _creatures.Add(new CombatEntity
        {
            Id = id,
            Kind = sp.Hostile ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            SpeciesId = sp.Id,
            Hostile = sp.Hostile,
            Hull = sp.MaxHealth,
            HullMax = sp.MaxHealth,
            Position = pos,
            DamagePerSecond = sp.AttackDamage,
            SizeScale = FaunaSizeScale(id), // this individual's own size within its species (cosmetic)
            Loot = { new ItemAmount(sp.DropItem, sp.DropCount) },
        });
    }

    /// <summary>A per-individual COSMETIC size factor (a "bell" centred on 1.0, ±30% — the average of two
    /// pseudo-randoms is triangular, so most animals are about normal and runts/giants are rare). Derived
    /// from the entity id so it stays stable for that individual. Does not change health/damage/loot.</summary>
    private static float FaunaSizeScale(string id)
    {
        int h = 0;
        foreach (char c in id)
        {
            h = unchecked(h * 31 + c);
        }

        uint u = (uint)h;
        float a = (u & 0xFFFF) / 65535f;
        float b = ((u >> 16) & 0xFFFF) / 65535f;
        float t = (a + b) * 0.5f;
        return 1f + (t - 0.5f) * 2f * 0.30f;
    }

    /// <summary>
    /// Immediately seeds fauna around a spot so a world feels alive the moment a player enters or
    /// arrives (instead of trickling in one creature every few seconds). Habitat-gated + capped; no-op
    /// on barren worlds. The caller sends/broadcasts the creature list.
    /// </summary>
    private void PopulateCreaturesNear(Shared.State.PlayerState player, int count)
    {
        // World options: the join-time seeding respects the abundance rule too — at Off the world
        // stays lifeless, and at low settings the initial burst doesn't overshoot the world cap.
        int cap = WorldCreatureCap(System.Math.Max(1, JoinedInActiveWorld().Count()));
        if (_speciesRoster.Length == 0 || cap <= 0)
        {
            return;
        }

        for (int i = 0; i < count && WildCreatureCount < System.Math.Min(cap, CreatureHardCap); i++)
        {
            TrySpawnCreatureNear(player);
        }
    }

    /// <summary>Land/air creatures spawn near the player; water/lava ones only in their fluid.</summary>
    private bool HabitatSuitable(CreatureSpecies sp, Vector3f at)
    {
        switch (sp.Habitat)
        {
            case CreatureHabitat.Water:
                return BlockValueAt(at) == _creatureWaterId && _creatureWaterId != 0;
            case CreatureHabitat.Lava:
                return BlockValueAt(at) == _creatureLavaId && _creatureLavaId != 0;
            case CreatureHabitat.Cave:
                // a standable air pocket on solid ground (the spawn probe places it in a real cave)
                return _world.GetBlock(new Vector3i((int)System.Math.Floor(at.X), (int)System.Math.Floor(at.Y), (int)System.Math.Floor(at.Z))).IsAir
                    && !_world.GetBlock(new Vector3i((int)System.Math.Floor(at.X), (int)System.Math.Floor(at.Y) - 1, (int)System.Math.Floor(at.Z))).IsAir;
            case CreatureHabitat.Amphibian:
                return BlockValueAt(at) == _creatureWaterId || WaterWithin(at, 2); // in or beside water
            default:
                return true; // Land, Air
        }
    }

    private ushort BlockValueAt(Vector3f at)
        => _world.GetBlock(new Vector3i((int)System.Math.Floor(at.X), (int)System.Math.Floor(at.Y), (int)System.Math.Floor(at.Z))).Value;

    /// <summary>Advances every creature: hunters approach, skittish flee, the rest wander; sleepers rest.</summary>
    private void MoveCreatures(List<PlayerSession> targets, double dt)
    {
        if (_creatures.Count == 0)
        {
            return;
        }

        double moveDt = System.Math.Min(dt, CreatureMoveDtCap);
        _creatureClock += moveDt;

        foreach (var creature in _creatures)
        {
            if (creature.IsCompanion)
            {
                MoveCompanion(creature, moveDt); // tamed companions follow their owner instead of wandering/hunting
                continue;
            }

            // Safety net: a wild creature that somehow ended up inside a parked ship (ship placed/grown over it,
            // an old save, a numeric edge) is pushed back out of the hull this tick instead of being stuck inside.
            if (TryPushOutsideShip(creature.Position, out var ejected))
            {
                creature.Position = ejected;
                continue;
            }

            if (creature.FrozenTimer > 0)
            {
                creature.FrozenTimer = System.Math.Max(0, creature.FrozenTimer - dt);
                continue; // held in stasis (item 36) — no movement this tick
            }

            if (creature.ProvokeTimer > 0)
            {
                creature.ProvokeTimer = System.Math.Max(0, creature.ProvokeTimer - dt);
            }

            if (creature.AwakeOverrideTimer > 0)
            {
                creature.AwakeOverrideTimer = System.Math.Max(0, creature.AwakeOverrideTimer - dt);
            }

            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            // A provoked territorial creature hunts like an aggressor until it calms down.
            var temperament = CreatureBehaviour.EffectiveTemperament(sp.Temperament, creature.ProvokeTimer > 0);
            Vector3f? nearest = NearestPlayerPosition(targets, creature.Position);

            // Give-up leash: an aggressor that has been chasing within aggro range too long backs off for a
            // while — it wanders away and won't chase/attack — so creatures never hound the player forever.
            bool aggressor = temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
            if (creature.GiveUpTimer > 0)
            {
                creature.GiveUpTimer = System.Math.Max(0, creature.GiveUpTimer - dt);
            }
            else if (aggressor && nearest is { } np && WithinAggro(creature.Position, np))
            {
                // While it can see the prey the chase clock ticks normally; once the player breaks line-of-sight
                // (behind cover, into a cave) the creature tires of the hunt faster, so hiding actually shakes it
                // off rather than only stopping the bite.
                bool sees = HasLineOfSight(creature.Position, np);
                creature.ChaseTimer += dt * (sees ? 1.0 : CreatureBlindChaseGiveUpRate);
                if (creature.ChaseTimer >= CreatureChaseGiveUpSeconds)
                {
                    creature.GiveUpTimer = CreatureGiveUpCooldownSeconds;
                    creature.ChaseTimer = 0;
                }
            }
            else
            {
                creature.ChaseTimer = System.Math.Max(0, creature.ChaseTimer - dt); // decay when not chasing
            }

            var profile = ProfileFor(creature.SpeciesId);

            // A creature in its off-phase is asleep — but a player coming within wake distance stirs it (being
            // hit does too, via ProvokeCreature). Once roused it stays alert for a while, then settles back.
            if (!SpeciesActive(sp) && creature.AwakeOverrideTimer <= 0 && nearest is { } wakePos
                && WrapDistSq(creature.Position, wakePos) <= CreatureWakeDistance * CreatureWakeDistance)
            {
                creature.AwakeOverrideTimer = CreatureWakeSeconds;
            }

            // Sleepers rest in place during their off-phase — only their habitat Y is kept (a sleeping flier
            // still hovers), no roaming or hunting. A roused sleeper falls through to normal temperament-driven
            // behaviour (skittish ones flee, hunters seek, others just wander).
            if (!SpeciesActive(sp) && creature.AwakeOverrideTimer <= 0)
            {
                creature.Position = AdjustHabitatHeight(sp, creature.Position, 0f, profile);
                continue;
            }

            // Decide intent: hunters Seek a nearby player, skittish flee one, everyone else (and a give-up
            // aggressor) roams with stop-and-go. Pack-hunters angle their approach so kin converge from spread
            // directions (encircle) rather than all stacking on one beeline.
            var intent = MoveMode.Roam;
            Vector3f? target = null;
            Vector3f? stepTarget = aggressor && creature.GiveUpTimer > 0 ? null : nearest;
            if (stepTarget is { } tp)
            {
                float dx = tp.X - creature.Position.X, dz = tp.Z - creature.Position.Z;
                float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (aggressor && dist <= CreatureAggroRange)
                {
                    intent = MoveMode.Seek;
                    target = temperament == CreatureTemperament.PackHunter ? FlankPoint(creature, tp, dx, dz) : tp;
                }
                else if (temperament == CreatureTemperament.Skittish && dist <= CreatureFleeRange)
                {
                    intent = MoveMode.Flee;
                    target = tp;
                }
            }

            uint seed = (uint)StableStringHash(creature.Id);
            var res = LocomotionController.Step(creature.Loco, profile, creature.Position, intent, target, moveDt, seed);
            creature.Loco = res.State;

            // Follow the world as they roam: land/lava walkers track the ground (hoppers pop up), fliers hover +
            // swoop, swimmers porpoise through the water column — driven by the per-creature vertical wave.
            var next = AdjustHabitatHeight(sp, res.Position, res.VertWave, profile);

            // Creatures don't walk into the player's ship — hold position at the hull.
            creature.Position = EntityBlockedByShip(next) ? creature.Position : next;
        }
    }

    /// <summary>Pushes any WILD creature standing inside a parked ship's hull back outside (companions are left
    /// alone — they may legitimately follow their owner aboard). Called when a ship is (re-)placed so a creature
    /// the ship parks on/over isn't sealed into the cabin; <see cref="MoveCreatures"/> is the continuous net.</summary>
    private void EvictWildlifeFromShips()
    {
        foreach (var creature in _creatures)
        {
            if (!creature.IsCompanion && TryPushOutsideShip(creature.Position, out var outside))
            {
                creature.Position = outside;
            }
        }
    }

    /// <summary>Spawns a wild creature of the first roster species at an exact position, bypassing the spawn
    /// habitat/ship checks. Test-only — lets a test plant a creature inside a parked ship to prove it is
    /// evicted. Returns the new creature's id.</summary>
    public string SpawnCreatureAtForTest(Vector3f at)
    {
        SpawnCreature(_speciesRoster[0], at);
        return _creatures[^1].Id;
    }

    /// <summary>Runs the placement-time eviction sweep directly (no tick) so a test can isolate it.</summary>
    public void EvictWildlifeFromShipsForTest() => EvictWildlifeFromShips();

    /// <summary>2D (x,z) distance check matching <see cref="CreatureBehaviour"/>'s aggro test.</summary>
    private static bool WithinAggro(Vector3f from, Vector3f to)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        return dx * dx + dz * dz <= CreatureAggroRange * CreatureAggroRange;
    }

    /// <summary>The movement profile for a species id (falls back to a default if somehow unknown).</summary>
    private LocomotionProfile ProfileFor(string speciesId)
        => _locoProfiles.TryGetValue(speciesId, out var p) ? p : default;

    /// <summary>A flanking target for a pack-hunter: a point on a small ring around the player, offset by a
    /// per-individual angle, so kin converge from spread directions and encircle instead of all stacking on one
    /// approach line. <paramref name="dx"/>/<paramref name="dz"/> are (player - creature).</summary>
    private Vector3f FlankPoint(CombatEntity c, Vector3f player, float dx, float dz)
    {
        float bearing = (float)System.Math.Atan2(-dz, -dx); // player → creature
        float spread = ((StableStringHash(c.Id) % 1000) / 1000f - 0.5f) * 1.4f; // ±0.7 rad, stable per individual
        float a = bearing + spread;
        const float ring = 2.0f;
        return new Vector3f(player.X + (float)System.Math.Cos(a) * ring, player.Y, player.Z + (float)System.Math.Sin(a) * ring);
    }

    private const float CreatureFlyAltitude = 5f; // how high above the ground fliers hover

    /// <summary>Habitat Y-snap for a one-off placement (spawn / teleport) — no vertical-life wave.</summary>
    private Vector3f AdjustHabitatHeight(CreatureSpecies sp, Vector3f p) => AdjustHabitatHeight(sp, p, 0f, default);

    /// <summary>Snaps a creature's Y to suit its habitat as it roams: land/lava walk on the ground (hoppers pop
    /// up on their hop beat), fliers hover above it (gliders swoop), swimmers porpoise between the seabed and the
    /// surface. <paramref name="vertWave"/> is the creature's own vertical-life wave (sin, ∈ [-1,1]) and
    /// <paramref name="prof"/> supplies its amplitude — so each animal's vertical motion is its own, not a shared
    /// global sine.</summary>
    private Vector3f AdjustHabitatHeight(CreatureSpecies sp, Vector3f p, float vertWave, in LocomotionProfile prof)
    {
        int surface = _generator.SurfaceHeight(_world.Planet, (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Z));
        switch (sp.Habitat)
        {
            case CreatureHabitat.Air:
                // Hover above the ground; gliders/drifters swoop up and down on their own wave (others hold steady).
                return new Vector3f(p.X, surface + CreatureFlyAltitude + prof.VertAmp * vertWave, p.Z);
            case CreatureHabitat.Water:
                // Use the LOCAL water column (sea or upland pond) — not just the global sea level — so swimmers
                // stay submerged in the upland lakes they were spawned in, not only the deep sea.
                if (_generator.TryGetWaterSurface(_world.Planet, (int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Z),
                        out int waterTopY, out int seabedY)
                    && waterTopY > seabedY + 1)
                {
                    float lo = seabedY + 1f, hi = waterTopY - 0.5f;
                    // Porpoise up and down the water column on the creature's OWN vertical wave (per-species freq,
                    // decorrelated) instead of one shared global sine. Clamped to the column, so shallow water
                    // just keeps them low.
                    float target = lo + (hi - lo) * (0.5f + 0.45f * vertWave);
                    return new Vector3f(p.X, target, p.Z);
                }

                return new Vector3f(p.X, surface + 1f, p.Z); // no water in this column → rest on the bed
            case CreatureHabitat.Cave:
                // Stay down in the caves: re-find the cave floor at the new column; if it wandered out from under
                // any cave, hold its current depth rather than popping up to the surface.
                int caveY = FindCaveFloorY((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Z), surface);
                return caveY >= 0 ? new Vector3f(p.X, caveY, p.Z) : p;
            default:
                // Land / lava / amphibian follow the ground. Hoppers (and floaty drifters) pop up on their own
                // wave; everyone else has VertAmp 0 and walks flat.
                float pop = prof.VertAmp > 0f ? System.Math.Max(0f, prof.VertAmp * vertWave) : 0f;
                return new Vector3f(p.X, surface + 1f + pop, p.Z);
        }
    }

    /// <summary>Finds a standable cave floor (an air pocket on solid ground, with headroom) in a column, scanning
    /// from just below the surface downward. Returns the floor's air-cell Y, or -1 if the column has no open cave.</summary>
    private int FindCaveFloorY(int x, int z, int surface)
    {
        for (int y = surface - 3; y > surface - 50; y--)
        {
            if (!_world.GetBlock(new Vector3i(x, y - 1, z)).IsAir   // solid floor
                && _world.GetBlock(new Vector3i(x, y, z)).IsAir      // feet in air
                && _world.GetBlock(new Vector3i(x, y + 1, z)).IsAir) // headroom
            {
                return y;
            }
        }

        return -1;
    }

    /// <summary>True if any water block sits within <paramref name="r"/> cells (horizontally, ±1 in Y) of a
    /// position — used to keep amphibians on the shoreline.</summary>
    private bool WaterWithin(Vector3f at, int r)
    {
        if (_creatureWaterId == 0)
        {
            return false;
        }

        int x = (int)System.Math.Floor(at.X), y = (int)System.Math.Floor(at.Y), z = (int)System.Math.Floor(at.Z);
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (_world.GetBlock(new Vector3i(x + dx, y + dy, z + dz)).Value == _creatureWaterId)
                    {
                        return true;
                    }
                }

        return false;
    }

    /// <summary>Marks an attacked creature as provoked if its species retaliates; pack-hunters rally kin.</summary>
    private void ProvokeCreature(CombatEntity target)
    {
        if (!_speciesById.TryGetValue(target.SpeciesId, out var sp)
            || !CreatureBehaviour.RetaliatesWhenAttacked(sp.Temperament))
        {
            return;
        }

        target.ProvokeTimer = CreatureProvokeSeconds;

        if (sp.Temperament == CreatureTemperament.PackHunter)
        {
            foreach (var other in _creatures)
            {
                if (!ReferenceEquals(other, target) && other.SpeciesId == target.SpeciesId
                    && WrapDistSq(other.Position, target.Position) <= CreaturePackRallyRange * CreaturePackRallyRange)
                {
                    other.ProvokeTimer = CreatureProvokeSeconds;
                }
            }
        }
    }

    /// <summary>Removes creatures farther than <see cref="CreatureDespawnRange"/> from every player.
    /// Returns true if any were removed (so the caller re-broadcasts the list).</summary>
    private bool PruneFarCreatures(List<PlayerSession> targets)
    {
        float maxSq = CreatureDespawnRange * CreatureDespawnRange;
        int removed = _creatures.RemoveAll(c =>
        {
            if (c.IsCompanion)
            {
                return false; // companions are managed by ReconcileCompanions, never far-pruned
            }

            var nearest = NearestPlayerPosition(targets, c.Position);
            return nearest is not { } np || WrapDistSq(np, c.Position) > maxSq;
        });
        return removed > 0;
    }

    private Vector3f? NearestPlayerPosition(List<PlayerSession> targets, Vector3f from)
    {
        Vector3f? best = null;
        float bestSq = float.MaxValue;
        foreach (var s in targets)
        {
            float d = (float)WrapDistSq(s.State.Position, from);
            if (d < bestSq)
            {
                bestSq = d;
                best = s.State.Position;
            }
        }

        return best;
    }

    private void BroadcastCreatures() => BroadcastToWorld(new CreatureList { Creatures = _creatures.Select(ToNetCreature).ToArray() });

    private void SendCreatures(PlayerSession session)
        => Send(session, new CreatureList { Creatures = _creatures.Select(ToNetCreature).ToArray() });

    private NetCreature ToNetCreature(CombatEntity e)
    {
        _speciesById.TryGetValue(e.SpeciesId, out var sp);
        bool asleep = sp != null && !SpeciesActive(sp) && e.AwakeOverrideTimer <= 0 && !e.IsCompanion; // roused or companion → not asleep
        return new NetCreature
        {
            Id = e.Id,
            SpeciesId = e.SpeciesId,
            NameKey = sp?.NameKey ?? "creature.generic.name",
            Name = sp?.Name ?? string.Empty,
            Hostile = !e.IsCompanion && (e.Hostile || e.ProvokeTimer > 0), // provoked creatures read as hostile (red tint); companions never
            Asleep = asleep,
            Frozen = e.FrozenTimer > 0, // held in stasis (item 36) — client tints it icy blue
            OwnerId = e.OwnerId,        // tamed companion → client draws friendly tint + nameplate
            CustomName = e.CustomName,

            Hull = e.Hull,
            HullMax = e.HullMax,
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            Habitat = (sp?.Habitat ?? CreatureHabitat.Land).ToString(),
            Activity = (sp?.Activity ?? CreatureActivity.Diurnal).ToString(),
            Temperament = (sp?.Temperament ?? CreatureTemperament.Passive).ToString(),
            Size = (sp?.Size ?? 1f) * e.SizeScale, // species size × this individual's own variance (cosmetic)
            Legs = sp?.Legs ?? 4,
            HasWings = sp?.HasWings ?? false,
            HasTail = sp?.HasTail ?? false,
            BodySegments = sp?.BodySegments ?? 1,
            ColorRgb = sp?.ColorRgb ?? 0xFFFFFF,
            Glows = sp?.Glows ?? false,
            Eyes = sp?.Eyes ?? 2,
            Horns = sp?.Horns ?? 0,
            HasCrest = sp?.HasCrest ?? false,
            BellyRgb = sp?.BellyRgb ?? (sp?.ColorRgb ?? 0xFFFFFF),
            Tentacles = sp?.Tentacles ?? 0,
            EyeStalks = sp?.EyeStalks ?? false,
            HasGasSac = sp?.HasGasSac ?? false,
        };
    }

    // --- Eating / consuming (food heals, poison harms) ---

    /// <summary>Player eats/uses a consumable item. Server applies its effect and consumes one.</summary>
    public void ConsumeItem(string playerId, string itemKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var p = session.State;
        var item = _content.GetItem(itemKey);
        if (item is null || item.Category != ItemCategory.Consumable)
        {
            Reject(session, "consume", "This item cannot be consumed.");
            return;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            Reject(session, "consume", "You don't have that item.");
            return;
        }

        p.Inventory.Remove(itemKey, 1);
        if (item.ConsumeHealth != 0f)
        {
            p.Health = System.Math.Min(100f, System.Math.Max(0f, p.Health + item.ConsumeHealth));
        }

        if (item.ConsumeHunger != 0f)
        {
            p.Hunger = System.Math.Min(100f, System.Math.Max(0f, p.Hunger + item.ConsumeHunger));
        }

        SendInventory(session);
        SendPlayerState(session);
        if (p.Health <= 0f)
        {
            RespawnPlayer(session, "Poisoned — recovery to the Medbay heal-tank.");
        }
    }

    private void HandleConsume(PlayerSession session, ConsumeItemIntent intent)
        => ConsumeItem(session.State.PlayerId, intent.ItemKey);
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    private const float CreatureStopRange = 1.6f;          // hunters hold this far from prey (#749), scaled up by Size
    private const double CreatureProvokeSeconds = 12.0;    // how long a provoked creature retaliates
    private const float CreaturePackRallyRange = 14f;      // pack-hunters rally kin within this
    private const double CreatureChaseGiveUpSeconds = 7.0;     // an aggressor gives up after chasing this long
    private const double CreatureGiveUpCooldownSeconds = 15.0; // ...then leaves you alone (no chase/attack) for this
    private const double CreatureBlindChaseGiveUpRate = 2.0;   // ...tiring twice as fast while it can't see you (hide to shake it ~halve the time)
    private const float CreatureWakeDistance = 4f;             // a sleeping creature stirs awake when a player comes this close
    private const double CreatureWakeSeconds = 9.0;            // ...and then stays roused (alert/active) for this long

    // #1325: no single species may fill the world — a share of the live cap per species (a herd counts its
    // members against it and spawns partially, exactly as it already does against the world cap), floored
    // so a tiny cap still allows a herd, and never below cap/roster so a short roster can still reach the cap.
    private const float SpeciesShareOfCap = 0.4f;
    private const int SpeciesShareMin = 3;
    private const float CreatureCrowdDespawnRange = 40f;       // an over-share member this far from every player wanders off

    // #1320: a sleeper whose body ends up inside blocks is roused + moved to the nearest clear spot within
    // this many blocks (same level first) — or despawns when boxed in on every side.
    private const int SleeperRelocateRadius = 6;

    private CreatureSpecies[] _speciesRoster = System.Array.Empty<CreatureSpecies>();
    private readonly List<PlayerSession> _creatureTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private readonly Dictionary<string, CreatureSpecies> _speciesById = new();
    private readonly Dictionary<string, LocomotionProfile> _locoProfiles = new(); // per-species movement tuning
    private List<CombatEntity> _creatures => _worlds.Active.Creatures;
    private double _creatureSpawnTimer { get => _worlds.Active.CreatureSpawnTimer; set => _worlds.Active.CreatureSpawnTimer = value; }
    private double _creatureClock { get => _worlds.Active.CreatureClock; set => _worlds.Active.CreatureClock = value; }
    private double _creatureBroadcastTimer { get => _worlds.Active.CreatureBroadcastTimer; set => _worlds.Active.CreatureBroadcastTimer = value; }
    private int _creatureSpawnRotor { get => _worlds.Active.CreatureSpawnRotor; set => _worlds.Active.CreatureSpawnRotor = value; }
    private int _creatureRingRotor { get => _worlds.Active.CreatureRingRotor; set => _worlds.Active.CreatureRingRotor = value; }
    private ushort _creatureWaterId, _creatureLavaId;

    /// <summary>Live creatures on the surface (passive + hostile fauna).</summary>
    public IReadOnlyList<CombatEntity> Creatures => _creatures;

    /// <summary>Wild fauna only (excludes tamed companions) — companions don't count against the world's cap.</summary>
    private int WildCreatureCount => _creatures.Count(c => !c.IsCompanion);

    /// <summary>The procedural species this world derived from its seed + planet.</summary>
    public IReadOnlyList<CreatureSpecies> SpeciesRoster => _speciesRoster;

    private void InitCreatures()
    {
        // Per-BODY roster (#478): the seed is salted with the location id (same formula as
        // WorldGenerator.RosterSeed) so two worlds of the same planet type host different species.
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        long rosterSeed = _meta.Seed ^ BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(_world.LocationId);
        _speciesRoster = planet is null
            ? System.Array.Empty<CreatureSpecies>()
            : CreatureGenerator.GenerateRoster(planet, rosterSeed).ToArray();

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
        _creatureRingRotor = 0;
        _creatureSpawnRotor = 0;
        _creatureWaterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _creatureLavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
        InitFences();
        InitHealTanks();
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
            if (TrySpawnCreatureNear(targets[_creatures.Count % targets.Count].State, cap))
            {
                BroadcastCreatures();
            }
        }

        // #900: in a storm, a blizzard or an ion squall the wildlife hunkers down — same movement code,
        // just a slower clock, so herds visibly settle while the weather rages and pick up again after.
        if (MoveCreatures(targets, dt * WeatherCreatureActivity()))
        {
            BroadcastCreatures(); // a boxed-in sleeper was removed (#1320)
        }

        // Despawn creatures that drifted far from every player so the cap frees up and fauna keeps
        // appearing around players as they explore — life is spread across the whole planet, not just
        // stuck at the start area. (Travel clears creatures entirely via ResetWorldRuntimeState.)
        if (PruneFarCreatures(targets, cap))
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

            // A titan's bite reaches further than a sheep-sized nip: the proximity range grows with the
            // species size past 2 (#638), so a provoked giant can't be safely poked from just outside 4
            // blocks. Half-rate growth caps it at 6 for the size-6 giants — exactly EnemyAttackReach, so
            // anything that can bite the player can always be hit back.
            float prox = CreatureProximityRange + System.Math.Max(0f, (sp.Size - 2f) * 0.5f);

            foreach (var session in targets)
            {
                var p = session.State;
                if (p.IgnoredByHostiles) // cloaked, god-mode and creative-override (#1121) players aren't detected
                {
                    continue;
                }

                if (WrapDistSq(p.Position, creature.Position) <= prox * prox
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
                        RespawnPlayer(session, "@srv.death.wildlife");
                    }
                }
            }
        }
    }

    // #470 (decision #4): a SAFETY ceiling only — the real population comes from WorldCreatureCap. The old
    // value of 12 sat below the model's 25–47 range for lush worlds, silently reducing "belebte Planeten"
    // back to the pre-overhaul fixed cap on exactly the worlds it was built for (and TickCreatures spun in
    // its fast fill cadence forever against it).
    private const int CreatureHardCap = 64;

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

    /// <summary>The molten sibling of <see cref="TryFindWaterColumnNear"/> (#470/F4): the nearest lava
    /// column — a volcano crater pool, the global lava sea, or a lava flow — around a spot.</summary>
    private bool TryFindLavaColumnNear(int x, int z, out int wx, out int wz, out int lavaTopY)
    {
        foreach (var (dx, dz) in WaterProbe)
        {
            if (_generator.TryGetLavaSurface(_world.Planet, x + dx, z + dz, out lavaTopY, out _))
            {
                wx = x + dx;
                wz = z + dz;
                return true;
            }
        }

        wx = x;
        wz = z;
        lavaTopY = 0;
        return false;
    }

    /// <summary>
    /// Spawns one roster species suited to a spread-out spot around the player (habitat-gated, on the
    /// ground) — or a whole herd/school/flock of it when the species is social (#639). Returns true if
    /// at least one spawned. Two independent rotors (#470): the ring rotor walks all 20 scatter offsets
    /// (advancing even on failure), the species rotor walks the roster on success. <paramref name="cap"/>
    /// is the world's live cap — every group member counts against it individually, so a group that no
    /// longer fits spawns partially, never over the cap.
    /// </summary>
    private bool TrySpawnCreatureNear(Shared.State.PlayerState player, int cap)
    {
        cap = System.Math.Min(cap, CreatureHardCap);
        if (WildCreatureCount >= cap)
        {
            return false;
        }

        // #470/F2+F3: the ring slot has its OWN rotor (the species rotor runs 0..rosterLength, which kept
        // this index below 6 forever — only the first arc of the 20 offsets was ever used, fauna always
        // appeared from the same direction and the outer band was dead), and it advances on EVERY attempt —
        // a failed spot no longer stalls the spawner on the identical column until the player moves.
        var (dx, dz) = SpawnRing[_creatureRingRotor % SpawnRing.Length];
        _creatureRingRotor = (_creatureRingRotor + 1) % SpawnRing.Length;
        int x = (int)System.Math.Floor(player.Position.X) + dx;
        int z = (int)System.Math.Floor(player.Position.Z) + dz;
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        int biome = _generator.BiomeIndexAt(_world.Planet, x, z);
        int share = SpeciesShare(cap);

        // Two passes: first only species native to this biome (so a multi-biome world shows different fauna in
        // different regions), then any species — so a biome never goes empty if none of its natives fit here.
        // A biome with a single native skips the native pass outright (#1325): otherwise every spawn in that
        // region was that one species, and a base there drowned in a monoculture up to the world cap.
        for (int pass = CreatureBehaviour.BiomePassStarved(_speciesRoster, biome) ? 1 : 0; pass < 2; pass++)
        {
            for (int n = 0; n < _speciesRoster.Length; n++)
            {
                var sp = _speciesRoster[(_creatureSpawnRotor + n) % _speciesRoster.Length];
                if (pass == 0 && sp.BiomeAffinity >= 0 && sp.BiomeAffinity != biome)
                {
                    continue; // not native to this biome (relaxed on the second pass)
                }

                if (WildCountOf(sp.Id) >= share)
                {
                    continue; // this species already holds its share of the world (#1325) — let another fill the cap
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
                else if (sp.Habitat == CreatureHabitat.Lava)
                {
                    // #470/F4: lava fauna needs the same courtesy water got — the ring spot is practically
                    // never molten itself (surface+1 is air above the melt), so these species were rolled
                    // into rosters but never appeared. Seek the nearest lava column (crater pool, lava sea
                    // or flow) and bask at its surface; skip the species when no melt is nearby.
                    if (!TryFindLavaColumnNear(x, z, out px, out pz, out int lavaTopY))
                    {
                        continue;
                    }

                    y = lavaTopY;
                }
                else
                {
                    // Land spawns stand on the REAL ground (#650) — a dug pit's floor or a built platform,
                    // not the generator's original surface (which would float them over player terraforming).
                    y = sp.Habitat == CreatureHabitat.Air ? surface + 4f : GroundFeetYAt(x, z, surface + 1);
                }

                var pos = new Vector3f(px + 0.5f, y, pz + 0.5f);
                if (!SpawnSpotClear(sp, pos, px, pz, surface))
                {
                    continue;
                }

                _creatureSpawnRotor = (_creatureSpawnRotor + n + 1) % _speciesRoster.Length;
                SpawnCreature(sp, pos);
                SpawnGroupAround(sp, px, pz, cap); // social species (#639) bring their herd/school/flock
                return true;
            }
        }

        return false;
    }

    /// <summary>The one reject list every spawn placement runs — the ring leader and each herd member
    /// alike (#1314: the two used to drift apart, and a gate added to one leaked through the other):
    /// habitat, the parked-ship volume (body-aware for large species, #1320), the body cells (#855), titan
    /// flatness (#638), the large-body volume (#750) and — new — a founded base's SEALED rooms (#1314): the
    /// spawner never consulted the base systems, so a room the player had made airtight still filled with
    /// wildlife. <see cref="InSealedBaseRoom"/> is a cached set lookup, effectively free.</summary>
    private bool SpawnSpotClear(CreatureSpecies sp, Vector3f pos, int px, int pz, int surface)
    {
        if (!HabitatSuitable(sp, pos) || EntityBlockedByShip(pos, CreatureShipMargin(sp)))
        {
            // Reject the SAME volume the movement barrier guards (not just the tight interior box), so a
            // creature never spawns in the thin shell where it would immediately be frozen against the hull.
            return false; // never spawn inside (or clipping into) a landed ship
        }

        if (CreatureBodyBlocked(sp, pos))
        {
            return false; // its body would materialise inside a wall / ruin masonry (#855)
        }

        // Titans need level ground (#638): a 3×3 clearance whose surface stays within ±1 of the
        // centre column, so a six-block giant doesn't materialise half-buried in a cliff face —
        // creatures have no colliders, so the spawn spot is the only terrain check they ever get.
        if (sp.BodyPlan == CreatureBodyPlan.Titan && !TitanGroundClear(px, pz, surface))
        {
            return false;
        }

        // A big body must actually FIT (#750): flatness alone let titans materialise inside
        // ruin rooms (stamped floors are perfectly flat) — the only other spatial gate was a
        // single 1×1 column with two air cells, for bodies that render ~5×10×11 blocks.
        if (sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land
            && !LargeBodyFits(sp, px, (int)System.Math.Floor(pos.Y), pz))
        {
            return false;
        }

        // #1314: nothing spawns inside a base's sealed rooms — the volume the air fill already knows.
        var cell = new Vector3i((int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z));
        return !InSealedBaseRoom(cell);
    }

    /// <summary>How many live wild individuals of one species a world may hold (#1325): a share of the
    /// live cap, floored at <see cref="SpeciesShareMin"/> (a herd must still be possible on a sparse
    /// world) and never below cap ÷ roster size (a two-species world must still reach its cap).</summary>
    private int SpeciesShare(int cap)
    {
        int roster = System.Math.Max(1, _speciesRoster.Length);
        int share = (int)System.Math.Ceiling(cap * SpeciesShareOfCap);
        return System.Math.Max(SpeciesShareMin, System.Math.Max(share, (cap + roster - 1) / roster));
    }

    /// <summary>Live WILD individuals of one species (companions never count, as with the world cap).</summary>
    private int WildCountOf(string speciesId)
    {
        int n = 0;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion && c.SpeciesId == speciesId)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The footprint radius a species adds to the parked-ship guard (#1320): large species keep
    /// their body clear of the hull, small fauna keeps the plain point test (fliers still cross over).</summary>
    private static float CreatureShipMargin(CreatureSpecies sp)
        => sp.Size >= LargeBodySize && sp.Habitat != CreatureHabitat.Air ? sp.Size * 0.5f : 0f;

    /// <summary>3×3 flatness gate for titan spawns (#638): every neighbouring column's ground must sit
    /// within ±1 block of the centre's. Reads REAL blocks (#650), so a titan neither materialises against
    /// a player wall nor half-buried in an edited cliff face.</summary>
    private bool TitanGroundClear(int x, int z, int centerSurface)
    {
        int centerFeet = GroundFeetYAt(x, z, centerSurface + 1);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int feet = GroundFeetYAt(x + dx, z + dz, centerFeet);
                if (System.Math.Abs(feet - centerFeet) > 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Places the rest of a social species' group (#639) around a just-spawned member: up to
    /// <see cref="CreatureSpecies.SocialGroupSize"/> − 1 more individuals, 4–8 blocks out on spread
    /// golden-angle bearings, each habitat-gated and each counting against the cap — unsuitable spots
    /// are simply skipped, so a group spawns partially rather than forcing bad placements.</summary>
    private void SpawnGroupAround(CreatureSpecies sp, int x, int z, int cap)
    {
        int group = System.Math.Clamp(sp.SocialGroupSize, 1, 5);
        int share = SpeciesShare(cap);
        for (int k = 1; k < group && WildCreatureCount < cap && WildCountOf(sp.Id) < share; k++)
        {
            // Golden-angle bearings with alternating radii — spread out, not a neat ring.
            double a = k * 2.399963;
            float dist = 4f + (k % 3) * 2f;
            int mx = x + (int)System.Math.Round(System.Math.Cos(a) * dist);
            int mz = z + (int)System.Math.Round(System.Math.Sin(a) * dist);
            int surface = _generator.SurfaceHeight(_world.Planet, mx, mz);

            float y;
            if (sp.Habitat == CreatureHabitat.Water || sp.Habitat == CreatureHabitat.Amphibian)
            {
                if (!_generator.TryGetWaterSurface(_world.Planet, mx, mz, out int waterTopY, out int seabedY))
                {
                    continue; // this member's spot is dry — the school stays smaller
                }

                y = sp.Habitat == CreatureHabitat.Water ? (seabedY + 1 + waterTopY) * 0.5f : waterTopY;
            }
            else if (sp.Habitat == CreatureHabitat.Lava)
            {
                if (!_generator.TryGetLavaSurface(_world.Planet, mx, mz, out int lavaTop, out _))
                {
                    continue;
                }

                y = lavaTop;
            }
            else if (sp.Habitat == CreatureHabitat.Cave)
            {
                int caveY = FindCaveFloorY(mx, mz, surface);
                if (caveY < 0)
                {
                    continue;
                }

                y = caveY;
            }
            else
            {
                y = sp.Habitat == CreatureHabitat.Air ? surface + 4f : GroundFeetYAt(mx, mz, surface + 1); // real ground (#650)
            }

            // Herd members run the leader's full reject list (#638/#750/#855/#1314): the herd stays smaller
            // rather than planting a member inside a wall, a ship, or a sealed base room.
            var pos = new Vector3f(mx + 0.5f, y, mz + 0.5f);
            if (!SpawnSpotClear(sp, pos, mx, mz, surface))
            {
                continue;
            }

            SpawnCreature(sp, pos);
        }
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
            TrySpawnCreatureNear(player, cap);
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

    private readonly List<CombatEntity> _creatureEvictions = new(); // boxed-in sleepers removed after the move loop (#1320)

    /// <summary>Advances every creature: hunters approach, skittish flee, the rest wander; sleepers rest.
    /// Returns true when a creature was REMOVED (a boxed-in sleeper, #1320) so the caller re-broadcasts.</summary>
    private bool MoveCreatures(List<PlayerSession> targets, double dt)
    {
        if (_creatures.Count == 0)
        {
            return false;
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

            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            // Safety net: a wild creature that somehow ended up inside a parked ship (ship placed/grown over it,
            // an old save, a numeric edge) is pushed back out of the hull this tick instead of being stuck inside.
            // Body-aware for large species (#1320): a sleeping titan herd lay with its centres a block outside
            // the hull and its bodies filling the cabin — the point test never saw it.
            if (TryPushOutsideShip(creature.Position, out var ejected, CreatureShipMargin(sp)))
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

            if (creature.PanicTimer > 0)
            {
                creature.PanicTimer = System.Math.Max(0, creature.PanicTimer - dt); // startle (#653) wears off
            }

            if (creature.AwakeOverrideTimer > 0)
            {
                creature.AwakeOverrideTimer = System.Math.Max(0, creature.AwakeOverrideTimer - dt);
            }

            // A provoked territorial creature hunts like an aggressor until it calms down.
            var temperament = CreatureBehaviour.EffectiveTemperament(sp.Temperament, creature.ProvokeTimer > 0);
            Vector3f? nearest = NearestPlayerPosition(targets, creature.Position);

            // Give-up leash: an aggressor that has been chasing within aggro range too long backs off for a
            // while — it wanders away and won't chase/attack — so creatures never hound the player forever.
            // Big species notice you from further away (#638): the range grows with size past 2.
            bool aggressor = temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
            float aggro = CreatureAggroRange + System.Math.Max(0f, sp.Size - 2f);
            if (creature.GiveUpTimer > 0)
            {
                creature.GiveUpTimer = System.Math.Max(0, creature.GiveUpTimer - dt);
            }
            else if (aggressor && nearest is { } np && WrapDistSq(creature.Position, np) <= aggro * aggro)
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

            // Sleepers rest in place during their off-phase — only their vertical state runs (#1331/#1332: a
            // sleeping walker still falls if its floor is dug away, a sleeping flier lands and sleeps on the
            // ground when there is any, a hoverer holds its altitude), no roaming or hunting. A roused sleeper
            // falls through to normal temperament-driven behaviour (skittish ones flee, hunters seek, others
            // just wander).
            var motion = EffectiveMotion(creature, sp);
            if (!SpeciesActive(sp) && creature.AwakeOverrideTimer <= 0)
            {
                // #1320: a sleeper skips every collision gate on the movement path, so a player building a
                // wall or floor THROUGH a sleeping herd left the bodies embedded in the masonry all night.
                // Re-validate the body BEFORE the vertical resolve (which would otherwise hop it onto the new
                // wall): rouse + step aside to the nearest clear spot, or despawn when boxed in. A few block
                // reads per sleeper per tick — far cheaper than the movement path an awake animal runs.
                if (DisplaceEmbeddedSleeper(creature, sp))
                {
                    continue;
                }

                creature.Position = ResolveVertical(creature, sp, motion, creature.Position, 0f, profile, moveDt,
                    asleep: true, MoveMode.Roam, moving: false);
                continue;
            }

            // Decide intent: hunters Seek a nearby player, skittish flee one, everyone else (and a give-up
            // aggressor) roams with stop-and-go. Pack-hunters angle their approach so kin converge from spread
            // directions (encircle) rather than all stacking on one beeline. A startled non-retaliator (#653)
            // flees too — panic reaches further than the skittish reflex and moves even placid grazers.
            var intent = MoveMode.Roam;
            Vector3f? target = null;
            Vector3f? stepTarget = aggressor && creature.GiveUpTimer > 0 ? null : nearest;
            if (stepTarget is { } tp)
            {
                float dx = tp.X - creature.Position.X, dz = tp.Z - creature.Position.Z;
                float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (aggressor && dist <= aggro)
                {
                    // Hold at a size-scaled ring around the prey instead of steering into its exact
                    // position (#749) — without this a hunter overshoots the player's coordinates and
                    // oscillates back and forth through the body. The proximity damage aura reaches
                    // well past the ring, so combat is unaffected; only the overlap goes away.
                    if (dist <= System.Math.Max(CreatureStopRange, sp.Size * 0.9f))
                    {
                        creature.Position = ResolveVertical(creature, sp, motion, creature.Position, 0f, profile, moveDt,
                            asleep: false, MoveMode.Seek, moving: false);
                        continue;
                    }

                    intent = MoveMode.Seek;
                    target = temperament == CreatureTemperament.PackHunter ? FlankPoint(creature, tp, dx, dz) : tp;
                }
                else if (temperament == CreatureTemperament.Skittish && dist <= CreatureFleeRange)
                {
                    intent = MoveMode.Flee;
                    target = tp;
                }
                else if (creature.PanicTimer > 0 && dist <= CreaturePanicFleeRange
                         && !CreatureBehaviour.RetaliatesWhenAttacked(temperament))
                {
                    intent = MoveMode.Flee;
                    target = tp;
                }
            }

            // A skittish animal BOLTING (entering flee) startles its nearby kin (#653) — the whole herd
            // takes off together. Only the genuine reflex spreads panic; already-panicked members don't
            // re-trigger it, so a startled herd settles back down instead of chain-panicking forever.
            if (intent == MoveMode.Flee && temperament == CreatureTemperament.Skittish
                && creature.PanicTimer <= 0 && creature.Loco.Mode != MoveMode.Flee)
            {
                StartleKin(creature);
            }

            uint seed = (uint)StableStringHash(creature.Id);
            var res = LocomotionController.Step(creature.Loco, profile, creature.Position, intent, target, moveDt, seed);
            creature.Loco = res.State;

            // Group steering (#639/#651): a roaming member of a social species drifts gently toward its
            // nearby kin (cohesion), keeps its personal space (separation) and — for schoolers — falls in
            // with the group's heading (alignment). Fleeing/hunting always wins, loners are unaffected.
            var stepped = res.Position;
            if (intent == MoveMode.Roam && sp.SocialGroupSize > 1 && res.Moving)
            {
                stepped = GroupSteer(creature, sp, stepped, moveDt, profile);
            }

            // Apply the horizontal step through every barrier (ship, fence, terrain gate, body sweep) and then
            // the class's vertical mechanics (#1331): walkers jump ledges and fall, crawlers haul over, fliers
            // land and take off, hoverers drift, swimmers porpoise.
            ApplyCreatureStep(creature, sp, motion, stepped, res.VertWave, profile, moveDt, intent, res.Moving,
                terrainGates: true);
        }

        if (_creatureEvictions.Count == 0)
        {
            return false;
        }

        foreach (var gone in _creatureEvictions)
        {
            _creatures.Remove(gone);
        }

        _creatureEvictions.Clear();
        return true;
    }

    /// <summary>
    /// One creature's movement step (#1331): the horizontal move from the locomotion controller is checked
    /// against the ship hull, energy fences, the terrain gate (wild fauna only) and the swept body check; a
    /// blocked step steers around (#651) or re-rolls; a ground mover facing a one-block rise first jumps or
    /// climbs in place and only steps over once its feet are up (so the body never clips the ledge); then the
    /// class's vertical mechanics resolve the Y at the accepted spot. Shared by wild fauna and companions.
    /// </summary>
    private void ApplyCreatureStep(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f stepped,
        float vertWave, in LocomotionProfile prof, double dt, MoveMode intent, bool moving, bool terrainGates)
    {
        var cur = c.Position;
        float targetX = cur.X, targetZ = cur.Z;

        // A flier coming down onto a perch or sitting on one does not walk (#1332).
        bool hold = motion == MotionClass.Flier && c.Vert.Flight is FlightPhase.Landing or FlightPhase.Perched;
        if (!hold)
        {
            var cand = PreviewStep(sp, motion, cur, stepped, out bool needsRise, out int riseFeet);
            if (StepBlocked(c, sp, motion, cur, cand, needsRise, terrainGates))
            {
                // Creatures don't walk into the player's ship — hold position at the hull. Energy fences pen
                // them in the same way, and terrain gates (#648) reuse the exact same mechanic. Instead of only
                // re-rolling a random heading, a blocked creature first probes alternative headings around the
                // obstacle (#651) — so it slides along cliff bases, walls and fence lines like an animal
                // skirting an obstacle. Only when every probe is blocked too does it fall back to the re-roll,
                // which preserves the "nothing can ever get stuck" property.
                c.Vert.ClimbTargetY = 0f; // whatever it was hauling toward is off the table
                if (TrySteerAround(c, sp, motion, stepped, terrainGates, out var detour, out float detourHeading))
                {
                    c.Loco.Heading = detourHeading;
                    targetX = detour.X;
                    targetZ = detour.Z;
                }
                else
                {
                    c.Loco.ModeTimer = 0f; // boxed in — re-roll a fresh heading next tick
                }
            }
            else if (needsRise)
            {
                // A one-block ledge ahead: get the feet up FIRST — a walker jumps, a crawler or giant hauls
                // itself up in place — and step over on a later tick once the body clears the lip.
                if (!c.Vert.Airborne && c.Vert.ClimbTargetY <= 0f)
                {
                    if (CreatureMotion.CanJump(sp))
                    {
                        VerticalMotion.Launch(ref c.Vert, LedgeJumpImpulse(sp));
                    }
                    else
                    {
                        VerticalMotion.BeginClimb(ref c.Vert, riseFeet);
                    }
                }
            }
            else
            {
                c.Vert.ClimbTargetY = 0f; // no rise ahead any more (heading changed) — don't finish a pointless haul
                targetX = cand.X;
                targetZ = cand.Z;
            }
        }

        c.Position = ResolveVertical(c, sp, motion, new Vector3f(targetX, cur.Y, targetZ), vertWave, prof, dt,
            asleep: false, intent, moving);
    }

    /// <summary>Every barrier a step must pass (#1331): ship hull, energy fence, the terrain gate (wild fauna;
    /// companions keep their freedom to follow the owner anywhere), and the swept body check — swept at the
    /// RAISED height when the step is a ledge the animal will have climbed first, so the ledge's own block
    /// doesn't read as a wall the way it used to.</summary>
    private bool StepBlocked(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f cand,
        bool needsRise, bool terrainGates)
    {
        if (EntityBlockedByShip(cand, CreatureShipMargin(sp)) || BlockedByEnergyFence(cur, cand))
        {
            return true; // body-aware hull guard for large species (#1320)
        }

        if (terrainGates && StepBlockedByTerrain(sp, motion, cur, cand))
        {
            return true;
        }

        var from = needsRise ? new Vector3f(cur.X, cand.Y, cur.Z) : cur;
        return CreaturePathBlocked(sp, from, cand, motion == MotionClass.Flier && c.Vert.Flight == FlightPhase.Flying);
    }

    /// <summary>The jump that clears a one-block ledge on this world (Q9: lighter worlds jump higher, exactly
    /// like the player's); a gliding ground bird gets the same height under its reduced airborne gravity.</summary>
    private float LedgeJumpImpulse(CreatureSpecies sp)
    {
        float g = VerticalMotion.Gravity(_gravityFactor) * (CreatureMotion.Glides(sp) ? VerticalMotion.GlideGravityScale : 1f);
        return VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeightFor(_gravityFactor));
    }

    /// <summary>The motion class in effect for a creature right now (#1334): amphibians swim while their feet
    /// are in water and walk/crawl ashore, with one cell of hysteresis so a shoreline animal doesn't flicker
    /// between the two; everyone else keeps the class its body implies.</summary>
    private MotionClass EffectiveMotion(CombatEntity c, CreatureSpecies sp)
    {
        if (sp.Habitat != CreatureHabitat.Amphibian)
        {
            return CreatureMotion.ClassOf(sp);
        }

        int x = (int)System.Math.Floor(c.Position.X), y = (int)System.Math.Floor(c.Position.Y), z = (int)System.Math.Floor(c.Position.Z);
        bool feetWet = _creatureWaterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value == _creatureWaterId;
        bool belowWet = _creatureWaterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, y - 1, z)).Value == _creatureWaterId;
        c.Vert.InWater = feetWet || (c.Vert.InWater && belowWet);
        return CreatureMotion.EffectiveClass(sp, c.Vert.InWater);
    }

    /// <summary>The sleeper's body check (#1320). False when the body sits clear of every colliding block.
    /// Otherwise the creature is roused (<see cref="CreatureWakeSeconds"/>) and stepped to the nearest
    /// clear standable spot within <see cref="SleeperRelocateRadius"/> — or, boxed in on every side, queued
    /// for removal (the caller drops it after the loop; the list can't change mid-iteration).</summary>
    private bool DisplaceEmbeddedSleeper(CombatEntity creature, CreatureSpecies sp)
    {
        if (!CreatureBodyBlocked(sp, creature.Position))
        {
            return false;
        }

        if (TryFindClearSpotNear(sp, creature.Position, out var clear))
        {
            creature.Position = clear;
            creature.AwakeOverrideTimer = CreatureWakeSeconds; // it wakes up and walks off, like a creature you bump
        }
        else
        {
            _creatureEvictions.Add(creature);
        }

        return true;
    }

    private static readonly (int Dx, int Dz)[] RelocateDirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
    };

    /// <summary>The nearest spot around <paramref name="from"/> where the species can actually stand: rings
    /// of 1..<see cref="SleeperRelocateRadius"/> blocks in eight directions, each column probed for a REAL
    /// standable feet cell with the species' own headroom, then the body, ship and large-body gates the
    /// spawner uses. Spots on the creature's own level (±1) are tried first, so an animal steps ASIDE out
    /// of a wall before it would climb onto the floor built over it. Never falls back to the noise
    /// surface — no real floor, no relocation.</summary>
    private bool TryFindClearSpotNear(CreatureSpecies sp, Vector3f from, out Vector3f spot)
    {
        int ox = (int)System.Math.Floor(from.X), oz = (int)System.Math.Floor(from.Z);
        int refY = (int)System.Math.Floor(from.Y);
        float margin = CreatureShipMargin(sp);
        bool large = sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land;
        int headroom = CreatureHeadroom(sp);
        for (int pass = 0; pass < 2; pass++)
        {
            int vertical = pass == 0 ? 1 : SleeperRelocateRadius;
            for (int r = 1; r <= SleeperRelocateRadius; r++)
            {
                foreach (var (dx, dz) in RelocateDirs)
                {
                    int x = ox + dx * r, z = oz + dz * r;
                    if (!TryGroundFeetYAt(x, z, refY, headroom, vertical, out int feet))
                    {
                        continue;
                    }

                    var cand = new Vector3f(x + 0.5f, feet, z + 0.5f);
                    if (CreatureBodyBlocked(sp, cand) || EntityBlockedByShip(cand, margin)
                        || (large && !LargeBodyFits(sp, x, feet, z)))
                    {
                        continue;
                    }

                    spot = cand;
                    return true;
                }
            }
        }

        spot = from;
        return false;
    }

    private const float CreaturePanicRadius = 12f;    // how far a startle (#653) spreads to same-species kin
    private const double CreaturePanicSeconds = 4.0;  // how long the startle lasts
    private const float CreaturePanicFleeRange = 24f; // a panicked animal flees players well past the skittish reflex

    /// <summary>Startles a hurt/bolting creature's same-species kin within <see cref="CreaturePanicRadius"/>
    /// (#653): non-retaliators among them flee the nearest player while their timer runs. The source itself is
    /// startled too (a hurt passive grazer bolts even though nothing else scares it).</summary>
    private void StartleKin(CombatEntity source)
    {
        source.PanicTimer = System.Math.Max(source.PanicTimer, CreaturePanicSeconds);
        foreach (var other in _creatures)
        {
            if (ReferenceEquals(other, source) || other.IsCompanion || other.SpeciesId != source.SpeciesId)
            {
                continue;
            }

            if (WrapDistSq(other.Position, source.Position) <= CreaturePanicRadius * CreaturePanicRadius)
            {
                other.PanicTimer = System.Math.Max(other.PanicTimer, CreaturePanicSeconds);
            }
        }
    }

    private static readonly float[] SteerOffsets = { 0.61f, 1.22f, 1.92f }; // ±35°, ±70°, ±110°

    /// <summary>Probes alternative headings around a blocked step (#651): the intended step length swung
    /// ±35°/±70°/±110° off the blocked heading (side order fixed per individual, so a herd doesn't wheel in
    /// unison), first candidate that passes every barrier wins and becomes the new heading — contour/wall
    /// following. A detour never takes a ledge (an animal skirting an obstacle follows the contour, it doesn't
    /// start a climb sideways). Returns false when all probes are blocked (caller falls back to the re-roll).</summary>
    private bool TrySteerAround(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f stepped,
        bool terrainGates, out Vector3f detour, out float heading)
    {
        detour = default;
        heading = 0f;
        float dx = stepped.X - c.Position.X, dz = stepped.Z - c.Position.Z;
        float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-5f)
        {
            return false; // not actually moving — nothing to steer
        }

        float baseHeading = (float)System.Math.Atan2(dz, dx);
        float side = (StableStringHash(c.Id) & 1) == 0 ? 1f : -1f;
        foreach (float off in SteerOffsets)
        {
            for (int s = 0; s < 2; s++)
            {
                float h = baseHeading + off * (s == 0 ? side : -side);
                var swung = new Vector3f(
                    c.Position.X + (float)System.Math.Cos(h) * len,
                    c.Position.Y,
                    c.Position.Z + (float)System.Math.Sin(h) * len);
                var cand = PreviewStep(sp, motion, c.Position, swung, out bool needsRise, out _);
                if (!needsRise && !StepBlocked(c, sp, motion, c.Position, cand, false, terrainGates))
                {
                    detour = cand;
                    heading = h;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The Y a step candidate is evaluated at (#1331), without touching any state: for a ground mover heading
    /// into a higher column that is the ledge's feet cell (it will have jumped or climbed before it steps
    /// over — <paramref name="needsRise"/> tells the caller the feet are still below it); otherwise the
    /// current Y (fliers, hoverers and swimmers ease in place; a drop is taken by falling after the step).
    /// </summary>
    private Vector3f PreviewStep(CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f stepped,
        out bool needsRise, out int riseFeet)
    {
        needsRise = false;
        riseFeet = 0;
        if (!CreatureMotion.IsGroundBound(motion))
        {
            return new Vector3f(stepped.X, cur.Y, stepped.Z);
        }

        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int nx = (int)System.Math.Floor(stepped.X), nz = (int)System.Math.Floor(stepped.Z);
        if (cx == nx && cz == nz)
        {
            return new Vector3f(stepped.X, cur.Y, stepped.Z);
        }

        int refY = (int)System.Math.Floor(cur.Y);
        int nextFeet = GroundFeetFor(sp, nx, nz, refY);
        if (nextFeet > cur.Y)
        {
            riseFeet = nextFeet;
            needsRise = VerticalMotion.IsBelow(cur.Y, nextFeet);
            return new Vector3f(stepped.X, nextFeet, stepped.Z);
        }

        return new Vector3f(stepped.X, cur.Y, stepped.Z);
    }

    /// <summary>Feeds the world's column data (real ground heights + water depths) into the pure
    /// <see cref="CreatureBehaviour.TerrainStepBlocked"/> gate (#648, per motion class since #1331). Only
    /// consulted when the step actually crosses a column boundary, and only for the classes the gate cares
    /// about — so the extra world queries stay off the common same-column tick. Ground heights come from
    /// REAL blocks (#650), so player-built walls read as impassable steps and dug ramps as walkable ones.</summary>
    private bool StepBlockedByTerrain(CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f next)
    {
        if (!CreatureMotion.IsGroundBound(motion) && motion != MotionClass.Swimmer)
        {
            return false; // fliers and hoverers keep their freedom
        }

        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int nx = (int)System.Math.Floor(next.X), nz = (int)System.Math.Floor(next.Z);
        if (cx == nx && cz == nz)
        {
            return false; // same column — nothing to gate
        }

        int refY = (int)System.Math.Floor(cur.Y);
        int curFeet = GroundFeetFor(sp, cx, cz, refY);
        int nextFeet = GroundFeetFor(sp, nx, nz, refY);

        // A large body treats a filled column as a wall (#750): ground-height deltas alone made ruin
        // walls invisible to fauna (NPCs have PathBlockedByWorld; creatures had nothing), so titans
        // pathed straight through masonry and bit the player from inside rooms. Titan-scale only, so
        // the extra block reads stay off the common path.
        if (sp.Size >= LargeBodySize && CreatureMotion.IsGroundBound(motion)
            && !LargeBodyColumnOpen(sp, nx, nextFeet, nz))
        {
            return true;
        }

        int curDepth = WaterDepthAtFeet(cx, cz, curFeet);
        int nextDepth = WaterDepthAtFeet(nx, nz, nextFeet);
        return CreatureBehaviour.TerrainStepBlocked(motion, CreatureMotion.IsGiant(sp), CreatureMotion.IsAmphibious(sp),
            curFeet, nextFeet, curDepth, nextDepth);
    }

    /// <summary>The generator's water depth in a column, but only when that water actually reaches the
    /// creature's feet: a real floor built ABOVE a pond (a bridge, a floating platform, a filled-in shore)
    /// is dry ground, not a swim — the old gate read the pond underneath and walled the animal at the
    /// first column over water.</summary>
    private int WaterDepthAtFeet(int x, int z, int feetY)
    {
        if (!_generator.TryGetWaterSurface(_world.Planet, x, z, out int top, out int bed))
        {
            return 0;
        }

        return top >= feetY - 1 ? top - bed : 0;
    }

    /// <summary>The feet cell a ground mover of this species stands on in a column, nearest to
    /// <paramref name="refY"/>: real blocks first (#650); a cave dweller with no standable cell near its depth
    /// holds that depth (it never pops up to the noise surface); everyone else falls back to the generator
    /// surface for unloaded columns.</summary>
    private int GroundFeetFor(CreatureSpecies sp, int x, int z, int refY)
    {
        // Species-aware headroom + the wide real-ground scan (#1320), so a titan never "stands" in a two-cell
        // hollow and a fresh pit is found before the noise surface is trusted.
        if (TryGroundFeetYAt(x, z, refY, CreatureHeadroom(sp), CreatureWideGroundScan, out int feet))
        {
            return feet;
        }

        return sp.Habitat == CreatureHabitat.Cave ? refY : _generator.SurfaceHeight(_world.Planet, x, z) + 1;
    }

    private const float CreatureSweepStep = 0.25f; // swept-step sampling distance (same as the NPC path check)
    private const int CreatureBodyMinHeight = 2;   // even a mouse-sized animal occupies feet + head
    private const int CreatureBodyMaxHeight = 8;   // ...and a titan is gated by its shoulders, not its full crown

    /// <summary>How many cells tall a creature's body is for collision purposes (its render height is
    /// <c>Size × 1.8</c>), clamped so tiny fauna still gets a head cell and a titan can still duck under an
    /// overhang instead of being walled in by its own crown.</summary>
    private static int CreatureBodyHeight(CreatureSpecies sp) => System.Math.Clamp(
        (int)System.Math.Ceiling(sp.Size * 1.8f), CreatureBodyMinHeight, CreatureBodyMaxHeight);

    /// <summary>Whether a creature's BODY would sit inside colliding blocks at a spot (#855). Creatures have no
    /// colliders and the server tracks a single point, so before this gate a wall was only ever seen indirectly —
    /// as a ground-height delta (<see cref="StepBlockedByTerrain"/>), which reads a 1–2 block player wall as a
    /// walkable step and lets the rendered body clip into it. Checks the creature's own column from the feet cell
    /// up through its body height; flying species pass through tree canopies (they hover inside them).</summary>
    private bool CreatureBodyBlocked(CreatureSpecies sp, Vector3f pos)
        => CreatureBodyBlocked(sp, pos, foliagePasses: sp.Habitat == CreatureHabitat.Air);

    /// <summary>Body check with an explicit canopy rule (#1332): a flier in the air weaves through tree crowns,
    /// but a flier coming down to PERCH treats the canopy as solid — it sits on a crown, never inside one.</summary>
    private bool CreatureBodyBlocked(CreatureSpecies sp, Vector3f pos, bool foliagePasses)
    {
        int x = (int)System.Math.Floor(pos.X);
        int y = (int)System.Math.Floor(pos.Y);
        int z = (int)System.Math.Floor(pos.Z);
        int height = CreatureBodyHeight(sp);
        for (int dy = 0; dy < height; dy++)
        {
            if (IsCollidingCellIfLoaded(x, y + dy, z, foliagePasses))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The swept version of <see cref="CreatureBodyBlocked"/> (#855): samples the whole step every
    /// <see cref="CreatureSweepStep"/> of a block, so a fast animal can't tunnel through a one-block-thin wall
    /// between two ticks. Mirrors <c>PathBlockedByWorld</c>, which fixed exactly this for NPCs.</summary>
    private bool CreaturePathBlocked(CreatureSpecies sp, Vector3f from, Vector3f to)
        => CreaturePathBlocked(sp, from, to, foliagePasses: sp.Habitat == CreatureHabitat.Air);

    private bool CreaturePathBlocked(CreatureSpecies sp, Vector3f from, Vector3f to, bool foliagePasses)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / CreatureSweepStep));
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            if (CreatureBodyBlocked(sp, new Vector3f(from.X + dx * f, from.Y + dy * f, from.Z + dz * f), foliagePasses))
            {
                return true;
            }
        }

        return false;
    }

    private const float LargeBodySize = 3f; // species at/above this Size get the body-volume checks (#750)

    /// <summary>Whether a large creature's body volume fits at the spot (#750): every column in the
    /// footprint radius must be open from just above the feet through the body height. The scan starts
    /// two cells up so the ±1 ground tolerance <see cref="TitanGroundClear"/> allows isn't misread as a
    /// wall. Unloaded chunks read as air (permissive, matching <see cref="StandableAt"/>).</summary>
    private bool LargeBodyFits(CreatureSpecies sp, int x, int feetY, int z)
    {
        int radius = (int)System.Math.Ceiling(sp.Size * 0.5f);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (!LargeBodyColumnOpen(sp, x + dx, feetY, z + dz))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Single-column variant of the body-volume check for the per-step gate (#750): the
    /// destination column must be open from just above the feet through the body height, so a titan
    /// treats a ruin wall as a barrier instead of a ground-height quirk.</summary>
    private bool LargeBodyColumnOpen(CreatureSpecies sp, int x, int feetY, int z)
    {
        int height = (int)System.Math.Ceiling(sp.Size * 1.8f);
        for (int dy = 2; dy < height; dy++)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(x, feetY + dy, z)).IsAir)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The standable feet cell in a column nearest to <paramref name="refY"/> (#650), read from
    /// REAL blocks via <see cref="ServerWorld.GetBlockIfLoaded"/> (unloaded chunks read as air and fall
    /// through to the generator surface — today's behaviour). Scanned outward from the reference, downward
    /// first at equal distance, so a creature under a player bridge keeps the ground instead of snapping
    /// onto the deck. This is what makes fauna honour player builds: dug pits, ramps, walls, floors.</summary>
    private int GroundFeetYAt(int x, int z, int refY)
        => TryGroundFeetYAt(x, z, refY, out int feet) ? feet : _generator.SurfaceHeight(_world.Planet, x, z) + 1;

    /// <summary>The species-aware feet snap for a creature's own column (#1320): a large body needs its
    /// whole height of headroom, not the two cells a mouse gets — the plain probe read a two-cell hollow
    /// under a floor as "standable" for a ten-block titan and held its body inside the floor above. Also
    /// looks further for REAL ground before trusting the noise surface: the ±6 window fell back to the
    /// generator's pre-excavation height, which floated a creature over a freshly dug pit.</summary>
    private int GroundFeetYAt(CreatureSpecies sp, int x, int z, int refY)
        => TryGroundFeetYAt(x, z, refY, CreatureHeadroom(sp), maxScan: CreatureWideGroundScan, out int feet)
            ? feet
            : _generator.SurfaceHeight(_world.Planet, x, z) + 1;

    private const int CreatureGroundScan = 6;      // the legacy ±window every feet probe scans first
    private const int CreatureWideGroundScan = 24; // ...and how far a creature's own column keeps looking (#1320)

    /// <summary>Cells of headroom a species' feet cell must offer: its collision body for large land species
    /// (mirrors <see cref="LargeBodyColumnOpen"/>/<see cref="CreatureBodyBlocked"/>), feet + head otherwise.</summary>
    private static int CreatureHeadroom(CreatureSpecies sp)
        => sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land ? CreatureBodyHeight(sp) : CreatureBodyMinHeight;

    /// <summary>Like <see cref="GroundFeetYAt(int, int, int)"/> but reports whether a REAL standable cell was
    /// found, so callers that must never snap to the noise surface (settlement NPCs standing on stamped floors
    /// that the generator knows nothing about) can keep their current Y when the column is unloaded/blocked.</summary>
    private bool TryGroundFeetYAt(int x, int z, int refY, out int feetY)
        => TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureGroundScan, out feetY);

    /// <summary>Scans outward from <paramref name="refY"/> — downward first at equal distance, so a creature
    /// under a player bridge keeps the ground instead of snapping onto the deck — for a feet cell with
    /// <paramref name="headroom"/> air cells, up to <paramref name="maxScan"/> cells away.</summary>
    private bool TryGroundFeetYAt(int x, int z, int refY, int headroom, int maxScan, out int feetY)
    {
        for (int r = 0; r <= maxScan; r++)
        {
            if (StandableAt(x, refY - r, z, headroom))
            {
                feetY = refY - r;
                return true;
            }

            if (r > 0 && StandableAt(x, refY + r, z, headroom))
            {
                feetY = refY + r;
                return true;
            }
        }

        feetY = refY;
        return false;
    }

    /// <summary>Whether feet placed at <paramref name="y"/> stand on something real: solid (non-water)
    /// support below, air from the feet up through <paramref name="headroom"/> cells. Uses the no-load
    /// block read.</summary>
    private bool StandableAt(int x, int y, int z, int headroom = CreatureBodyMinHeight)
    {
        var below = _world.GetBlockIfLoaded(new Vector3i(x, y - 1, z));
        if (below.IsAir || below.Value == _creatureWaterId)
        {
            return false;
        }

        for (int dy = 0; dy < headroom; dy++)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(x, y + dy, z)).IsAir)
            {
                return false;
            }
        }

        return true;
    }

    private const float GroupCohesionRange = 24f;   // kin within this count toward the group centre (#639)
    private const float GroupCohesionMinDist = 3f;  // close enough — no pull (herds shouldn't stack up)

    /// <summary>The boids trio for a social creature's roam step (#639/#651): <b>cohesion</b> toward the
    /// centre of same-species neighbours within <see cref="GroupCohesionRange"/>, <b>separation</b> away
    /// from the nearest kin when it is inside the personal-space bubble (size-scaled — titans stop standing
    /// inside each other), and <b>alignment</b> for schooler-style species (the heading eases toward the kin
    /// average, so schools/flocks actually swim/fly as one). Plain-coordinate scan (the odd seam-straddling
    /// herd just loses cohesion for a moment); O(n) per social creature against a ≤64-entity list, at 15 Hz.</summary>
    private Vector3f GroupSteer(CombatEntity self, CreatureSpecies sp, Vector3f stepped, double dt, in LocomotionProfile prof)
    {
        float cx = 0f, cz = 0f;
        int kin = 0;
        float nearestSq = float.MaxValue, nearX = 0f, nearZ = 0f;
        float headSin = 0f, headCos = 0f;
        foreach (var other in _creatures)
        {
            if (ReferenceEquals(other, self) || other.IsCompanion || other.SpeciesId != self.SpeciesId)
            {
                continue;
            }

            float dx = other.Position.X - stepped.X, dz = other.Position.Z - stepped.Z;
            float distSq = dx * dx + dz * dz;
            if (distSq > GroupCohesionRange * GroupCohesionRange)
            {
                continue;
            }

            cx += other.Position.X;
            cz += other.Position.Z;
            headSin += (float)System.Math.Sin(other.Loco.Heading);
            headCos += (float)System.Math.Cos(other.Loco.Heading);
            kin++;
            if (distSq < nearestSq)
            {
                nearestSq = distSq;
                nearX = other.Position.X;
                nearZ = other.Position.Z;
            }
        }

        if (kin == 0)
        {
            return stepped; // no kin in range — roam free
        }

        var pos = stepped;
        float tx = cx / kin - pos.X, tz = cz / kin - pos.Z;
        float dist = (float)System.Math.Sqrt(tx * tx + tz * tz);
        if (dist > GroupCohesionMinDist)
        {
            // Cohesion: pull a fraction of the cruise step toward the centroid — strongest when far.
            float k = System.Math.Min(1f, (dist - GroupCohesionMinDist) / GroupCohesionRange);
            float pull = prof.CruiseSpeed * 0.35f * k * (float)dt / dist;
            pos = new Vector3f(pos.X + tx * pull, pos.Y, pos.Z + tz * pull);
        }

        // Separation (#651): personal space scaled by species size, so herd members never overlap bodies.
        float sepDist = System.Math.Max(1.5f, sp.Size * 0.6f);
        float nd = (float)System.Math.Sqrt(nearestSq);
        if (nd > 1e-4f && nd < sepDist)
        {
            float push = System.Math.Min(sepDist - nd, (float)(prof.CruiseSpeed * dt));
            pos = new Vector3f(pos.X + (pos.X - nearX) / nd * push, pos.Y, pos.Z + (pos.Z - nearZ) / nd * push);
        }

        // Alignment (#651): schoolers fall in with the group's average heading (bounded ease per tick).
        if (sp.LocoStyle == LocomotionStyle.Schooler)
        {
            float avg = (float)System.Math.Atan2(headSin / kin, headCos / kin);
            self.Loco.Heading = CreatureBehaviour.BlendHeading(
                self.Loco.Heading, avg, (float)System.Math.Min(1.0, 1.5 * dt));
        }

        return pos;
    }

    /// <summary>Pushes any WILD creature standing inside a parked ship's hull back outside (companions are left
    /// alone — they may legitimately follow their owner aboard). Called when a ship is (re-)placed so a creature
    /// the ship parks on/over isn't sealed into the cabin; <see cref="MoveCreatures"/> is the continuous net.</summary>
    private void EvictWildlifeFromShips()
    {
        foreach (var creature in _creatures)
        {
            if (creature.IsCompanion)
            {
                continue;
            }

            float margin = _speciesById.TryGetValue(creature.SpeciesId, out var sp) ? CreatureShipMargin(sp) : 0f;
            if (TryPushOutsideShip(creature.Position, out var outside, margin))
            {
                creature.Position = outside;
            }
        }
    }

    /// <summary>Spawns a wild creature of the first roster species (or <paramref name="speciesId"/>) at an
    /// exact position, bypassing the spawn habitat/ship checks. Test-only — lets a test plant a creature
    /// inside a parked ship to prove it is evicted. Returns the new creature's id.</summary>
    public string SpawnCreatureAtForTest(Vector3f at, string? speciesId = null)
    {
        SpawnCreature(speciesId is null ? _speciesRoster[0] : _speciesById[speciesId], at);
        return _creatures[^1].Id;
    }

    /// <summary>Runs the placement-time eviction sweep directly (no tick) so a test can isolate it.</summary>
    public void EvictWildlifeFromShipsForTest() => EvictWildlifeFromShips();

    /// <summary>Test-only: puts a creature's locomotion controller into a roam PAUSE for <paramref name="seconds"/>
    /// (as if it had rolled one), so a test can trigger the behaviour a pause drives — a flier landing to
    /// rest (#1332) — without waiting for the seeded roll.</summary>
    public void PauseCreatureForTest(string id, float seconds)
    {
        var c = _creatures.First(x => x.Id == id);
        c.Loco.Initialized = true;
        c.Loco.Mode = MoveMode.Pause;
        c.Loco.ModeTimer = seconds;
        c.Loco.Speed = 0f;
    }

    /// <summary>The spawner's full reject list for the first roster species at a spot (#1314 seam).</summary>
    public bool SpawnSpotClearForTest(Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        return SpawnSpotClear(_speciesRoster[0], at, x, z, _generator.SurfaceHeight(_world.Planet, x, z));
    }

    /// <summary>The per-species share of this world's live cap for one player on foot (#1325 seam).</summary>
    public int SpeciesShareForTest() => SpeciesShare(System.Math.Min(WorldCreatureCap(1), CreatureHardCap));

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

    private const float FlierDescendRate = 4f;   // blocks/s a landing flier comes down at (#1332)
    private const float FlierClimbRate = 5f;     // blocks/s a flier climbs back to its hover band
    private const float FlierCruiseRate = 4f;    // blocks/s the hover target is eased at (#652)
    private const float HovererEaseRate = 2f;    // slower — a gas sac lags the terrain instead of tracing it
    private const float LandHovererHeight = 0.8f; // a floating land grazer rides this far above its feet
    private const float PerchReach = 2f;         // a perch may sit this far below the hover band's floor
    private const float TakeOffSettle = 0.3f;    // within this of the hover target → cruising again

    /// <summary>Habitat Y-snap for a one-off placement (spawn / teleport / companion re-place): the pure
    /// habitat height with no easing and no vertical state — fliers at their hover, swimmers mid-column,
    /// ground movers on the real feet cell.</summary>
    private Vector3f AdjustHabitatHeight(CreatureSpecies sp, Vector3f p)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        switch (sp.Habitat)
        {
            case CreatureHabitat.Air:
                float hover = sp.HoverAltitude > 0f ? sp.HoverAltitude : CreatureFlyAltitude;
                return new Vector3f(p.X, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y - hover)) + hover, p.Z);
            case CreatureHabitat.Water:
                return new Vector3f(p.X, WaterColumnY(x, z, 0f, surface), p.Z);
            case CreatureHabitat.Lava:
                return _generator.TryGetLavaSurface(_world.Planet, x, z, out int lavaTop, out _)
                    ? new Vector3f(p.X, lavaTop, p.Z)
                    : new Vector3f(p.X, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y)), p.Z);
            case CreatureHabitat.Amphibian:
                return _generator.TryGetWaterSurface(_world.Planet, x, z, out int ampTop, out _)
                    ? new Vector3f(p.X, ampTop, p.Z)
                    : new Vector3f(p.X, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y)), p.Z);
            default:
                return new Vector3f(p.X, GroundFeetFor(sp, x, z, (int)System.Math.Floor(p.Y)), p.Z);
        }
    }

    /// <summary>
    /// The per-tick vertical mechanics (#1331/#1332/#1334) at an accepted X/Z: which of the five motion
    /// classes the creature moves in decides how its Y is found. Ground movers run under gravity (real
    /// jumps, real falls, a slow haul for the ones that can't jump), fliers cruise / land / perch / take off,
    /// hoverers drift on a buoyant ease, swimmers porpoise their water column. <paramref name="vertWave"/> is
    /// the creature's own vertical-life wave (sin ∈ [-1,1]); <paramref name="prof"/> supplies its amplitude.
    /// Mutates the creature's <see cref="CombatEntity.Vert"/> state.
    /// </summary>
    private Vector3f ResolveVertical(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f p, float vertWave,
        in LocomotionProfile prof, double dt, bool asleep, MoveMode intent, bool moving)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        switch (motion)
        {
            case MotionClass.Swimmer:
                c.Vert.Airborne = false;
                return new Vector3f(p.X, WaterColumnY(x, z, vertWave, surface,
                    holdY: sp.Habitat == CreatureHabitat.Amphibian ? p.Y : null), p.Z);

            case MotionClass.Hoverer:
                {
                    // Buoyant (Q5): never lands, never sinks — asleep it simply holds its band. The target eases
                    // slowly, so a gas sac visibly lags the terrain instead of contour-tracing it.
                    c.Vert.Airborne = false;
                    float baseY = sp.Habitat == CreatureHabitat.Air
                        ? GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y - HoverOf(sp))) + HoverOf(sp)
                        : GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y)) + LandHovererHeight;
                    float target = baseY + prof.VertAmp * vertWave;
                    return new Vector3f(p.X, VerticalMotion.Ease(p.Y, target, dt, HovererEaseRate, 24f), p.Z);
                }

            case MotionClass.Flier:
                return new Vector3f(p.X, ResolveFlier(c, sp, p, vertWave, prof, dt, asleep, intent), p.Z);

            default:
                {
                    // Walker / crawler. Lava dwellers in the melt swim it like water; ashore they walk the rock.
                    if (sp.Habitat == CreatureHabitat.Lava
                        && _generator.TryGetLavaSurface(_world.Planet, x, z, out int lavaTop, out _))
                    {
                        c.Vert.Airborne = false;
                        return new Vector3f(p.X, lavaTop, p.Z);
                    }

                    float g = VerticalMotion.Gravity(_gravityFactor);
                    int groundY = GroundFeetFor(sp, x, z, (int)System.Math.Floor(p.Y));
                    if (!asleep && moving && !c.Vert.Airborne && c.Vert.ClimbTargetY <= 0f)
                    {
                        if (sp.LocoStyle == LocomotionStyle.Hopper && CreatureMotion.CanJump(sp)
                            && VerticalMotion.HopBeat(ref c.Vert, vertWave))
                        {
                            // A hopper's beat is a real ballistic hop now (#1331) — the stride pulse in the
                            // controller rides the same wave, so hop and stride stay in step.
                            VerticalMotion.Launch(ref c.Vert, VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeightFor(_gravityFactor, VerticalMotion.HopHeight)));
                        }
                        else if (intent == MoveMode.Flee && CreatureMotion.Glides(sp) && c.Vert.JumpCooldown <= 0f)
                        {
                            // A startled ground bird bounds (#1334): the ledge jump under glide gravity → a long, flat arc.
                            VerticalMotion.Launch(ref c.Vert, LedgeJumpImpulse(sp));
                            c.Vert.JumpCooldown = VerticalMotion.BoundCooldown;
                        }
                    }
                    else if (!moving)
                    {
                        c.Vert.LastWave = vertWave; // keep the beat detector current so a resumed hopper doesn't fire on stale sign
                    }

                    float riseRate = motion == MotionClass.Crawler ? VerticalMotion.ClimbRate : VerticalMotion.StepUpRate;
                    float gravityScale = CreatureMotion.Glides(sp) ? VerticalMotion.GlideGravityScale : 1f;
                    return new Vector3f(p.X, VerticalMotion.Ground(ref c.Vert, p.Y, groundY, g, dt, riseRate, gravityScale), p.Z);
                }
        }
    }

    private static float HoverOf(CreatureSpecies sp) => sp.HoverAltitude > 0f ? sp.HoverAltitude : CreatureFlyAltitude;

    /// <summary>
    /// A flier's vertical life (#1332): cruise at its hover band over the REAL ground (a player roof counts),
    /// swooping on its own wave; come down to a perch when the controller pauses or sleep begins and there is
    /// something to sit on within reach; sit under gravity (a removed branch drops it); climb back out when
    /// the pause ends, on any hunt/flee intent (a skittish bird flushes when the player nears), when hurt, or
    /// when roused from sleep.
    /// </summary>
    private float ResolveFlier(CombatEntity c, CreatureSpecies sp, Vector3f p, float vertWave, in LocomotionProfile prof,
        double dt, bool asleep, MoveMode intent)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        float hover = HoverOf(sp);
        float airTarget = GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y - hover)) + hover + prof.VertAmp * vertWave;
        bool mustFly = intent is MoveMode.Seek or MoveMode.Flee || c.PanicTimer > 0 || c.ProvokeTimer > 0;
        bool wantsDown = !mustFly && (asleep || c.Loco.Mode == MoveMode.Pause);
        ref var v = ref c.Vert;
        switch (v.Flight)
        {
            case FlightPhase.Landing:
                if (!wantsDown)
                {
                    v.Flight = FlightPhase.TakingOff;
                    goto case FlightPhase.TakingOff;
                }

                v.Airborne = false;
                float y = VerticalMotion.Ease(p.Y, v.PerchY, dt, FlierDescendRate, 24f);
                if (System.Math.Abs(y - v.PerchY) < 1e-3f)
                {
                    v.Flight = FlightPhase.Perched;
                }

                return y;

            case FlightPhase.Perched:
                if (!wantsDown)
                {
                    v.Flight = FlightPhase.TakingOff;
                    v.Airborne = false;
                    goto case FlightPhase.TakingOff;
                }

                // Sitting: plain gravity — if the perch is dug away it falls to the next floor and sits there.
                return VerticalMotion.Ground(ref v, p.Y, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y)),
                    VerticalMotion.Gravity(_gravityFactor), dt);

            case FlightPhase.TakingOff:
                v.Airborne = false;
                float up = VerticalMotion.Ease(p.Y, airTarget, dt, FlierClimbRate, 24f);
                if (System.Math.Abs(up - airTarget) <= TakeOffSettle)
                {
                    v.Flight = FlightPhase.Flying;
                }

                return up;

            default:
                v.Airborne = false;
                if (wantsDown && TryPerchSpot(sp, p, hover, out float perchY))
                {
                    v.Flight = FlightPhase.Landing;
                    v.PerchY = perchY;
                    return VerticalMotion.Ease(p.Y, perchY, dt, FlierDescendRate, 24f);
                }

                return VerticalMotion.Ease(p.Y, airTarget, dt, FlierCruiseRate, 24f);
        }
    }

    /// <summary>A standable feet cell under a flier within its hover band plus <see cref="PerchReach"/>, read
    /// from real blocks, where its body fits with the canopy counted as SOLID (a bird sits on a crown, never
    /// inside it). False over a pit, deep water or an unloaded column — the flier keeps hovering.</summary>
    private bool TryPerchSpot(CreatureSpecies sp, Vector3f p, float hover, out float perchY)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        perchY = 0f;
        if (!TryGroundFeetYAt(x, z, (int)System.Math.Floor(p.Y - hover), out int feet) || feet > p.Y
            || p.Y - feet > hover + PerchReach)
        {
            return false;
        }

        if (CreatureBodyBlocked(sp, new Vector3f(p.X, feet, p.Z), foliagePasses: false))
        {
            return false;
        }

        perchY = feet;
        return true;
    }

    /// <summary>The Y inside a column's LOCAL water body (sea or upland pond — not just the global sea level,
    /// so swimmers stay in the lakes they spawned in): porpoising on the creature's own wave, clamped to the
    /// column (shallow water just keeps them low). A dry column rests on the bed (or holds
    /// <paramref name="holdY"/> when given — an amphibian in a player-made pool the generator knows nothing about).</summary>
    private float WaterColumnY(int x, int z, float vertWave, int surface, float? holdY = null)
    {
        if (_generator.TryGetWaterSurface(_world.Planet, x, z, out int waterTopY, out int seabedY)
            && waterTopY > seabedY + 1)
        {
            float lo = seabedY + 1f, hi = waterTopY - 0.5f;
            return lo + (hi - lo) * (0.5f + 0.45f * vertWave);
        }

        return holdY ?? surface + 1f;
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

    private const float TitanDespawnRange = 110f; // a landmark animal must not evaporate mid-approach (#638)

    /// <summary>Removes creatures farther than <see cref="CreatureDespawnRange"/> from every player
    /// (titans keep a wider leash — <see cref="TitanDespawnRange"/> — so the huge silhouette you are
    /// walking toward doesn't vanish). Then the crowding pass (#1325): a species OVER its share of
    /// <paramref name="cap"/> (the cap shrank, or a pre-share save) sheds its farthest members that are
    /// out of sight of every player — beyond <see cref="CreatureCrowdDespawnRange"/>, not provoked — until
    /// it is back at its share, so the mix can recover while the player stays at their base. Returns true
    /// if any were removed (caller re-broadcasts).</summary>
    private bool PruneFarCreatures(List<PlayerSession> targets, int cap)
    {
        float maxSq = CreatureDespawnRange * CreatureDespawnRange;
        float titanSq = TitanDespawnRange * TitanDespawnRange;
        int removed = _creatures.RemoveAll(c =>
        {
            if (c.IsCompanion)
            {
                return false; // companions are managed by ReconcileCompanions, never far-pruned
            }

            float limitSq = _speciesById.TryGetValue(c.SpeciesId, out var sp) && sp.BodyPlan == CreatureBodyPlan.Titan
                ? titanSq
                : maxSq;
            var nearest = NearestPlayerPosition(targets, c.Position);
            return nearest is not { } np || WrapDistSq(np, c.Position) > limitSq;
        });

        int share = SpeciesShare(System.Math.Min(cap, CreatureHardCap));
        float crowdSq = CreatureCrowdDespawnRange * CreatureCrowdDespawnRange;
        foreach (var sp in _speciesRoster)
        {
            int over = WildCountOf(sp.Id) - share;
            if (over <= 0)
            {
                continue;
            }

            // Farthest-from-any-player first, out-of-sight members only — the animals in view stay put.
            var shed = _creatures
                .Where(c => !c.IsCompanion && c.SpeciesId == sp.Id && c.ProvokeTimer <= 0)
                .Select(c => (Creature: c, DistSq: NearestPlayerPosition(targets, c.Position) is { } np ? WrapDistSq(np, c.Position) : double.MaxValue))
                .Where(t => t.DistSq > crowdSq)
                .OrderByDescending(t => t.DistSq)
                .Take(over)
                .ToList();
            foreach (var (creature, _) in shed)
            {
                _creatures.Remove(creature);
                removed++;
            }
        }

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

        // Motion class + vertical state on the wire (#1333, additive): a walker is airborne mid-jump/fall (with
        // its velocity so the client can integrate the arc between updates), a flier is airborne unless perched,
        // a hoverer always is; swimmers never.
        var motion = sp is null ? MotionClass.Walker : CreatureMotion.EffectiveClass(sp, e.Vert.InWater);
        bool airborne = motion switch
        {
            MotionClass.Flier => e.Vert.Flight != FlightPhase.Perched,
            MotionClass.Hoverer => true,
            MotionClass.Swimmer => false,
            _ => e.Vert.Airborne,
        };
        return new NetCreature
        {
            Motion = CreatureMotion.Key(motion),
            Airborne = airborne,
            Perched = motion == MotionClass.Flier && e.Vert.Flight == FlightPhase.Perched,
            VertVel = CreatureMotion.IsGroundBound(motion) && e.Vert.Airborne ? e.Vert.VertVel : 0f,

            Id = e.Id,
            SpeciesId = e.SpeciesId,
            NameKey = sp?.NameKey ?? "creature.generic.name",
            Name = sp?.Name ?? string.Empty,
            Hostile = !e.IsCompanion && (e.Hostile || e.ProvokeTimer > 0), // provoked creatures read as hostile (red tint); companions never
            Asleep = asleep,
            Frozen = e.FrozenTimer > 0, // held in stasis (item 36) — client tints it icy blue
            OwnerId = e.OwnerId,        // tamed companion → client draws friendly tint + nameplate
            CustomName = e.CustomName,
            Alerting = e.IsCompanion && _uptime < e.AlertUntil, // #1210: growling at a hostile in sight

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
            BodyPlan = (sp?.BodyPlan ?? CreatureBodyPlan.Standard).ToString(),
            NeckLength = sp?.NeckLength ?? 0,
            HasTrunk = sp?.HasTrunk ?? false,
            VoiceSeed = sp?.VoiceSeed ?? 0, // 0 → client falls back to hashing the trait tuple (#907)
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
            Reject(session, "consume", "@srv.misc.not_consumable");
            return;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            Reject(session, "consume", "@srv.misc.no_item");
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

        if (item.ConsumeHunger > 0f)
        {
            ShipAiOnEat(session); // VEGA's survival lesson: actually eating real food (not just biting a poison gland)
        }

        SendInventory(session);
        SendPlayerState(session);
        if (p.Health <= 0f)
        {
            RespawnPlayer(session, "@srv.death.poison");
        }
    }

    private void HandleConsume(PlayerSession session, ConsumeItemIntent intent)
        => ConsumeItem(session.State.PlayerId, intent.ItemKey);
}

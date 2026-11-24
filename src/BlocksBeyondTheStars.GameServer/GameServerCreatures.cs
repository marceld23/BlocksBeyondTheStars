// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
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
    private const float CreatureCrowdDespawnRange = 40f;       // an over-share member at least this far from every player wanders off (#1356: and past every player's streaming radius)

    // #1320: a sleeper whose body ends up inside blocks is roused + moved to the nearest clear spot within
    // this many blocks (same level first) — or despawns when boxed in on every side.
    private const int SleeperRelocateRadius = 6;

    // #1357: an awake creature re-validates its own body cells this often (sleepers do it every tick — they
    // run nothing else), so one walled in by the player steps out instead of standing in the block for good.
    private const double AwakeBodyCheckInterval = 2.0;

    // #2226: the species tables are the active WORLD's (see LoadedWorld.SpeciesRoster) — the rolled ids ("sp0",
    // "sp1", …) repeat from world to world, so one table for the whole server gave a world the animals of whichever
    // world had loaded last. Read them where they are used: a reference kept across a world switch is the old world's.
    private CreatureSpecies[] _speciesRoster { get => _worlds.Active.SpeciesRoster; set => _worlds.Active.SpeciesRoster = value; }
    private readonly List<PlayerSession> _creatureTargets = new(); // reused per tick (no per-tick LINQ alloc)
    private readonly List<PlayerSession> _creatureReactors = new(); // #3502: the targets without observers — who animals react to
    private Dictionary<string, CreatureSpecies> _speciesById => _worlds.Active.SpeciesById;
    private Dictionary<string, LocomotionProfile> _locoProfiles => _worlds.Active.LocoProfiles; // per-species movement tuning
    private List<CombatEntity> _creatures => _worlds.Active.Creatures;
    private double _creatureSpawnTimer { get => _worlds.Active.CreatureSpawnTimer; set => _worlds.Active.CreatureSpawnTimer = value; }
    private double _creatureClock { get => _worlds.Active.CreatureClock; set => _worlds.Active.CreatureClock = value; }
    private double _creatureBroadcastTimer { get => _worlds.Active.CreatureBroadcastTimer; set => _worlds.Active.CreatureBroadcastTimer = value; }
    private int _creatureSpawnRotor { get => _worlds.Active.CreatureSpawnRotor; set => _worlds.Active.CreatureSpawnRotor = value; }
    private int _creatureRingRotor { get => _worlds.Active.CreatureRingRotor; set => _worlds.Active.CreatureRingRotor = value; }
    private ushort _creatureWaterId, _creatureLavaId;

    /// <summary>Live creatures on the surface (passive + hostile fauna).</summary>
    public IReadOnlyList<CombatEntity> Creatures => _creatures;

    /// <summary>Wild fauna only (excludes tamed companions) — companions don't count against the world's cap. #2018: the count
    /// is WEIGHTED — members of a big-herd species count one per <see cref="HerdRules.BigHerdCapDivisor"/> (a herd of twelve
    /// costs four slots), so a "few" world with a dozen animals can still carry one herd. The hard cap stays a real count
    /// (<see cref="TrySpawnCreatureNear"/> checks <see cref="RawWildCount"/> against it).</summary>
    private int WildCreatureCount
    {
        get
        {
            _wildCounts.Clear();
            foreach (var c in _creatures)
            {
                if (c.IsCompanion || c.IsGiant || c.OtherSideOnly) // #1998: giants are outside the cap; #3905: so is the other side
                {
                    continue;
                }

                _wildCounts.TryGetValue(c.SpeciesId, out int n);
                _wildCounts[c.SpeciesId] = n + 1;
            }

            int total = 0;
            foreach (var kv in _wildCounts)
            {
                total += _speciesById.TryGetValue(kv.Key, out var sp) ? HerdRules.WeightedCount(sp, kv.Value) : kv.Value;
            }

            return total;
        }
    }

    private readonly Dictionary<string, int> _wildCounts = new(); // reused by WildCreatureCount (no per-call alloc)

    /// <summary>Every wild animal counted once — the hard cap and the client are about bodies, not budget (#2018).</summary>
    private int RawWildCount
    {
        get
        {
            int n = 0;
            foreach (var c in _creatures)
            {
                if (!c.IsCompanion && !c.IsGiant)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The procedural species the active world derived from its seed + planet (every resident world keeps
    /// its own, #2226).</summary>
    public IReadOnlyList<CreatureSpecies> SpeciesRoster => _speciesRoster;

    /// <summary>Test seam (#2226): points the server at a resident world, as the tick and every message handler do
    /// before they act on one — so a test with two resident worlds reads the roster, the animals and the tanks of the
    /// world it means. False when the body is not resident.</summary>
    public bool ActivateWorldForTest(string locationId) => SetActiveWorld(locationId);

    /// <summary>#2029: the population base of a "none" world whose roster kept a few cave species (a toxic world).</summary>
    private const double CaveOnlyFaunaBase = 4.0;

    private void InitCreatures()
    {
        // Per-BODY roster (#478): the seed is salted with the location id — THE formula (#1722: one shared
        // function, not a hand copy) — so two worlds of the same planet type host different species.
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        // #1763: the save's generation and the type's authored species — on a generation-5 world they join the
        // roster after the procedural slots (Leni on the ice worlds, the flowerling on the flower fields).
        _speciesRoster = planet is null
            ? System.Array.Empty<CreatureSpecies>()
            : RosterFor(planet, _world.LocationId).ToArray();

        _speciesById.Clear();
        _locoProfiles.Clear();
        foreach (var sp in _speciesRoster)
        {
            _speciesById[sp.Id] = sp;
            _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        }

        InitKeptAnimals(planet); // #3976: the Earth animals people keep, known to the species table beside the roster
        _creatures.Clear();
        _creatureSpawnTimer = 0;
        _creatureClock = 0;
        _creatureBroadcastTimer = 0;
        _creatureRingRotor = 0;
        _creatureSpawnRotor = 0;
        _creatureWaterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _creatureLavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
        _creatureIceId = _content.GetBlock("ice")?.NumericId.Value ?? 0; // #2955: the lid of a frozen sea …
        _creatureSnowId = _content.GetBlock("snow")?.NumericId.Value ?? 0; // … and the snow drifted onto it
        InitFences();
        InitHealTanks();
        InitGiants(); // #1998: the colossus / the sandworms of this world, outside the roster
    }

    // --- Day/night activity (ties into the World-systems clock) ---

    /// <summary>Whether a species is awake/active right now at <paramref name="at"/> (else it is sleeping/resting).
    /// Day and night are LOCAL (#1865): the sky a player sees is shifted by longitude, so an animal on the far side
    /// of the planet keeps the hours of the sun above it, not of the world clock.</summary>
    private bool SpeciesActive(CreatureSpecies s, Vector3f at) => s.Activity switch
    {
        CreatureActivity.Diurnal => !IsNightAt(at),
        CreatureActivity.Nocturnal => IsNightAt(at),
        CreatureActivity.Crepuscular => IsDawnOrDuskAt(at),
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
            // #2029: a lifeless world that kept a few cave species (a toxic world's rare underground life) — a small herd
            // of them, never more.
            "none" => ActiveTraits.CaveFauna && _speciesRoster.Length > 0 ? CaveOnlyFaunaBase : 0.0,
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
        // #1530: hashed once per world (the string interpolation + hash ran 15× per second before).
        double jitter = _worlds.Active.FaunaJitter ??= 0.7 + 0.6 * ((uint)WorldGenerator.StableHash($"fauna:{_meta.Seed}:{_worlds.Active.LocationId}") % 1000 / 999.0); // 0.7..1.3, stable per world
        return (int)System.Math.Round(baseN * size * jitter * System.Math.Sqrt(System.Math.Max(1, players)));
    }

    private void TickCreatures(double dt)
    {
        // #2530: first, on every world (a station or a barren moon too): a taming attempt whose animal died, despawned or
        // is out of reach ends — and its player is told, so the taming panel never holds the controls for nothing.
        SweepTameAttempts();

        // #2960: the first batch of animals for a player who just joined or landed, placed once the ground around them has
        // streamed in. This comes before every early return below: a void world drops the mark, and a player still aboard
        // gets the batch.
        if (TickFaunaSeeds(dt))
        {
            BroadcastCreatures();
        }

        // Orbital stations (void worlds) have no wildlife at all — only peaceful NPCs.
        if (_world.Planet.Void)
        {
            return;
        }

        // #2215: a world with no roster has no life of its own — but an animal that IS there (a guest clone from a
        // sample on an airless moon, a companion tamed from one) must still move, sleep and be synced. A truly empty
        // world still leaves here at once; its giants tick by their own rules and do not count.
        bool rosterless = _speciesRoster.Length == 0;
        if (rosterless && !AnyOrdinaryCreature())
        {
            return; // barren world — no life
        }

        // Companions follow their owners — keep their presence in sync with who is on this world (runs even when
        // nobody is on foot, so a pet despawns the moment its owner flies off / boards into space).
        if (ReconcileCompanions())
        {
            BroadcastCreatures();
        }

        FillCreatureTargets();
        var targets = _creatureTargets;
        if (targets.Count == 0)
        {
            return;
        }

        var reactors = _creatureReactors;

        // #1717: the cadence and the fill gate read the SAME ceiling the spawner enforces. The model alone can
        // reach ~200 on a huge lush world with the Extreme rule and four players; TrySpawnCreatureNear clamps
        // to the hard cap and refuses, so an unclamped gate here kept the 1.5 s fast fill (a full ring + roster
        // walk with terrain probes) running forever against a world that was already full — the very shape of
        // the old cap-of-12 bug, moved to a rarer threshold.
        int cap = System.Math.Min(WorldCreatureCap(targets.Count), CreatureHardCap);
        int wild = WildCreatureCount;
        _creatureSpawnTimer += dt;
        // Fill faster while the world is far below its cap (a freshly visited world comes alive quickly),
        // then ease to the slow trickle near the cap.
        double interval = wild < cap / 2 ? 1.5 : CreatureSpawnInterval;
        if (_creatureSpawnTimer >= interval && wild < cap && !rosterless) // no roster, nothing to spawn (#2215)
        {
            _creatureSpawnTimer = 0;
            // #1720: the player who gets the next spawn rotates on the WILD population — companions used to
            // skew the round robin, so a pet owner's surroundings filled faster than everyone else's. #3656: a train rider
            // anchors no spawn (GameServerRailWildlife).
            if (CreatureSpawnAnchor(targets, wild) is { } anchor && TrySpawnCreatureNear(anchor.State, cap))
            {
                BroadcastCreatures();
            }
        }

        // #900: in a storm, a blizzard or an ion squall the wildlife hunkers down — same movement code,
        // just a slower clock, so herds visibly settle while the weather rages and pick up again after.
        if (MoveCreatures(reactors, dt * WeatherCreatureActivity()))
        {
            BroadcastCreatures(); // a boxed-in sleeper was removed (#1320)
        }

        TickCalmGifts(reactors); // #1760: a calm flowerling spills a gift for a player who has not been mining

        // Despawn creatures that drifted far from every player so the cap frees up and fauna keeps
        // appearing around players as they explore — life is spread across the whole planet, not just
        // stuck at the start area. (Travel clears creatures entirely via ResetWorldRuntimeState.)
        if (PruneFarCreatures(targets, cap))
        {
            BroadcastCreatures();
        }

        // #2521: a wild animal that got into a base anyway — an open gate, a gap, a wall that loaded late — leaves it
        // again, quietly, while nobody is looking (throttled; see GameServerBaseIntruders).
        if (PruneBaseIntruders(dt))
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

        // #2226: this world's table, read once. A bite that kills sends the player to their ship or bed — on another
        // body that loads its world and moves the cursor, and the animals still to come in this loop are this world's.
        var speciesById = _speciesById;
        foreach (var creature in _creatures)
        {
            if (creature.IsCompanion || creature.IsGiant)
            {
                continue; // a tamed companion never harms anyone; a giant stomps and strikes by its own rules (#1998)
            }

            if (!speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            TickBiteCooldown(creature, dt); // #3326: a chunk-biter's clock runs whatever keeps it from biting right now

            if (creature.FrozenTimer > 0 || IsStaggered(creature))
            {
                continue; // held in stasis (item 36) — can't bite while frozen, so you can scan it safely; dazed by a push (#2278) neither
            }

            // Hostile species attack; so do provoked (territorial) creatures fighting back.
            bool aggressiveNow = sp.Hostile || creature.ProvokeTimer > 0;
            // #1997: a roused sleeper (hit, or woken by mining beside it) hunts per its temperament — and now bites too;
            // the day/night gate alone left a woken hunter chasing the player all night without ever landing a bite.
            if (!aggressiveNow || (!SpeciesActive(sp, creature.Position) && creature.AwakeOverrideTimer <= 0))
            {
                continue;
            }

            if (IsCalmed(creature))
            {
                continue; // #3359: a calmed animal (the calm disc, the Resonance Blaster) bites nobody
            }

            // A creature that has given up the chase backs off and won't bite until its cooldown lapses.
            if (creature.GiveUpTimer > 0)
            {
                continue;
            }

            // A titan's bite reaches further than a sheep-sized nip: the proximity range grows with the
            // species size past 2 (#638), so a provoked giant can't be safely poked from just outside 4
            // blocks. Half-rate growth caps it at 6 for the size-6 giants — exactly EnemyAttackReach, so
            // anything that can bite the player can always be hit back. #2565: the rule lives in Shared — the
            // client's lunge and attack call use the same reach, so no bite lands silently any more.
            float prox = CreatureReach.BiteRange(sp.Size);

            // A chunk-biter lands ONE bite per interval, on the nearest player it can reach (GameServerHounds.cs); a
            // drainer hurts everybody in its reach (who may be bitten at all: BiteCanReach).
            bool chunks = CreatureReach.BitesInChunks(sp);
            var only = chunks ? NearestBiteTarget(creature, reactors, prox) : null;
            foreach (var session in reactors)
            {
                var p = session.State;
                if (chunks ? !ReferenceEquals(session, only) : !BiteCanReach(creature, session, prox))
                {
                    continue;
                }

                float bite = BiteThisTick(creature, sp, dt); // #3326: the Schuppenhund bites in chunks, the rest drain
                if (bite <= 0f && chunks)
                {
                    continue; // between two bites
                }

                // While driving a speeder its hull soaks most of the bite (the player is shielded by the chassis).
                if (TryGetDrivenSpeeder(p, out var speeder))
                {
                    DamageSpeeder(speeder, bite * SpeederCreatureDamageShare, "wildlife");
                    bite *= 1f - SpeederCreatureDamageShare;
                }

                DamagePlayer(p, bite, mitigated: true);
                NoteCombat(session); // #2286
                NoteHitFrom(session, creature, creature.Position); // #2564: the HUD marker points at this animal
                MarkPlayerStateDirty(session); // #1530
                if (p.Health <= 0f)
                {
                    RespawnPlayer(session, "@srv.death.wildlife");
                }
            }
        }
    }

    /// <summary>Fills the two per-tick player lists (reused fields — no per-tick LINQ allocation at 15 Hz): every player on
    /// foot in the active world (<see cref="_creatureTargets"/>) keeps the population and the pruning going — an observer's
    /// view must not empty the land — but only the players who are really there make an animal react
    /// (<see cref="_creatureReactors"/>, #3502): an observer wakes no sleeper, scares no skittish animal off, draws no Soundrak
    /// scream and no VEGA announcement, earns no calm gift and is nobody's prey. #3656: a player riding a train stays a
    /// target (the land along the line keeps living for the window view, the cap counts them) but is no reactor — a hunter
    /// never comes for the seated rider of a halted wagon — and anchors no spawn (<see cref="CreatureSpawnAnchor"/>).</summary>
    private void FillCreatureTargets()
    {
        _creatureTargets.Clear();
        _creatureReactors.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId))
            {
                _creatureTargets.Add(s);
                if (!s.Watching && !RidesTrain(s) && !s.OtherSide) // #3905: no animal reacts to a player who is over there
                {
                    _creatureReactors.Add(s);
                }
            }
        }
    }

    /// <summary>Whether any animal other than a giant lives on the active world (a plain loop; the list is empty on a
    /// barren world and holds a giant or two at most where only giants live).</summary>
    private bool AnyOrdinaryCreature()
    {
        foreach (var c in _creatures)
        {
            if (!c.IsGiant)
            {
                return true;
            }
        }

        return false;
    }

    // #470 (decision #4): a SAFETY ceiling only — the real population comes from WorldCreatureCap. The old
    // value of 12 sat below the model's 25–47 range for lush worlds, silently reducing "belebte Planeten"
    // back to the pre-overhaul fixed cap on exactly the worlds it was built for (and TickCreatures spun in
    // its fast fill cadence forever against it).
    private const int CreatureHardCap = 64;

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
        // #1367 (decided): the far leash is view-aware like the crowding pass (#1356) — at the 8-chunk view
        // distance a herd 90 blocks out is on screen, and the unconditional 70-block prune popped it out.
        float stream = MaxStreamRadiusBlocks(targets);
        float maxRange = System.Math.Max(CreatureDespawnRange, stream);
        float titanRange = System.Math.Max(TitanDespawnRange, stream);
        float maxSq = maxRange * maxRange;
        float titanSq = titanRange * titanRange;
        int removed = _creatures.RemoveAll(c =>
        {
            if (c.IsCompanion || c.IsGiant || c.CloneOf.Length > 0 || DenKeeps(c))
            {
                return false; // companions are managed by ReconcileCompanions, giants by TickGiants (#1998), a den's animals by the den (#3495) — never far-pruned
            }

            float limitSq = _speciesById.TryGetValue(c.SpeciesId, out var sp)
                            && CreatureBodyPlans.Of(sp.BodyPlan).LongLeash // #2009: a speeder-sized silhouette keeps the titan's leash
                ? titanSq
                : maxSq;
            var nearest = NearestPlayerPosition(targets, c.Position);
            return nearest is not { } np || WrapDistSq(np, c.Position) > limitSq;
        });

        int share = SpeciesShare(System.Math.Min(cap, CreatureHardCap));

        // #1356: "out of sight" is the players' real streaming radius, not a fixed 40 blocks — at the 8-chunk
        // view distance a herd 50 m out on a plain is in plain view, and a member vanishing there was exactly
        // the pop-out reported. Whatever the widest view among the players streams stays put.
        float crowdRange = System.Math.Max(CreatureCrowdDespawnRange, MaxStreamRadiusBlocks(targets));
        float crowdSq = crowdRange * crowdRange;
        foreach (var sp in _speciesRoster)
        {
            int over = WeightedWildCountOf(sp) - share; // #2018: the same weighted count the spawner used, or it sheds the herd
            if (over <= 0)
            {
                continue;
            }

            // Farthest-from-any-player first, out-of-sight members only — the animals in view stay put, and so
            // does a hunter mid-charge (#1356: a Seek intent is a live hunt/approach, not just a provoked one).
            var shed = _creatures
                .Where(c => !c.IsCompanion && !c.IsGiant && c.CloneOf.Length == 0 && c.SpeciesId == sp.Id && c.ProvokeTimer <= 0 && c.Loco.Mode != MoveMode.Seek)
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

    /// <summary>The farthest block any of these players streams (#1356): their widest view radius in chunks,
    /// plus the player's own chunk, in blocks. Anything inside it may be on screen.</summary>
    private float MaxStreamRadiusBlocks(List<PlayerSession> targets)
    {
        int chunks = 0;
        foreach (var s in targets)
        {
            chunks = System.Math.Max(chunks, EffectiveViewRadius(s));
        }

        return (chunks + 1) * WorldConstants.ChunkSize;
    }

    /// <summary>The nearest on-foot player's position, or null. <paramref name="preyOnly"/> skips players hostiles ignore
    /// (<see cref="HostilesIgnore"/>, #2712) — what an aggressor may hunt; with a <paramref name="hunter"/> also the players it
    /// overlooks (#3354: the obsidian set's quiet steps, a Soundrak soothed by the calm disc — <see cref="PreyOverlooked"/>).</summary>
    private Vector3f? NearestPlayerPosition(List<PlayerSession> targets, Vector3f from, bool preyOnly = false, CombatEntity? hunter = null)
    {
        Vector3f? best = null;
        float bestSq = float.MaxValue;
        foreach (var s in targets)
        {
            if (preyOnly && HostilesIgnore(s.State))
            {
                continue;
            }

            float d = (float)WrapDistSq(s.State.Position, from);
            if (preyOnly && PreyOverlooked(hunter, s, d))
            {
                continue;
            }

            if (d < bestSq)
            {
                bestSq = d;
                best = s.State.Position;
            }
        }

        return best;
    }
}

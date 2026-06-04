using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

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
    private const float CreatureAggroRange = 10f;          // hunters approach within this
    private const float CreatureFleeRange = 6f;            // skittish flee within this
    private const double CreatureProvokeSeconds = 12.0;    // how long a provoked creature retaliates
    private const float CreaturePackRallyRange = 14f;      // pack-hunters rally kin within this

    private CreatureSpecies[] _speciesRoster = System.Array.Empty<CreatureSpecies>();
    private readonly Dictionary<string, CreatureSpecies> _speciesById = new();
    private List<CombatEntity> _creatures => _worlds.Active.Creatures;
    private double _creatureSpawnTimer { get => _worlds.Active.CreatureSpawnTimer; set => _worlds.Active.CreatureSpawnTimer = value; }
    private double _creatureClock { get => _worlds.Active.CreatureClock; set => _worlds.Active.CreatureClock = value; }
    private double _creatureBroadcastTimer { get => _worlds.Active.CreatureBroadcastTimer; set => _worlds.Active.CreatureBroadcastTimer = value; }
    private int _creatureSpawnRotor { get => _worlds.Active.CreatureSpawnRotor; set => _worlds.Active.CreatureSpawnRotor = value; }
    private ushort _creatureWaterId, _creatureLavaId;

    /// <summary>Live creatures on the surface (passive + hostile fauna).</summary>
    public IReadOnlyList<CombatEntity> Creatures => _creatures;

    /// <summary>The procedural species this world derived from its seed + planet.</summary>
    public IReadOnlyList<CreatureSpecies> SpeciesRoster => _speciesRoster;

    private void InitCreatures()
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        _speciesRoster = planet is null
            ? System.Array.Empty<CreatureSpecies>()
            : CreatureGenerator.GenerateRoster(planet, _meta.Seed).ToArray();

        _speciesById.Clear();
        foreach (var sp in _speciesRoster)
        {
            _speciesById[sp.Id] = sp;
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

    private void TickCreatures(double dt)
    {
        if (_speciesRoster.Length == 0)
        {
            return; // barren world — no life
        }

        var targets = _sessions.Values
            .Where(s => s.Joined && !s.State.AboardShip && !InSpace(s.State.PlayerId))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        int cap = System.Math.Min(CreatureCapPerPlayer * targets.Count, 12);
        _creatureSpawnTimer += dt;
        if (_creatureSpawnTimer >= CreatureSpawnInterval && _creatures.Count < cap)
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
            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            // Hostile species attack; so do provoked (territorial) creatures fighting back.
            bool aggressiveNow = sp.Hostile || creature.ProvokeTimer > 0;
            if (!aggressiveNow || !SpeciesActive(sp))
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

                if (p.Position.DistanceSquared(creature.Position) <= CreatureProximityRange * CreatureProximityRange)
                {
                    p.Health = System.Math.Max(0f, p.Health - Mitigate(p, (float)(creature.DamagePerSecond * dt)));
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

    // A spread of offsets around the player so fauna appears *around* them, not stacked on one spot.
    private static readonly (int Dx, int Dz)[] SpawnRing =
    {
        (7, 0), (5, 5), (0, 7), (-5, 5), (-7, 0), (-5, -5), (0, -7), (5, -5),
        (12, 4), (-4, 12), (-12, -4), (4, -12),
    };

    /// <summary>
    /// Spawns one roster species suited to a spread-out spot around the player (habitat-gated, on the
    /// ground). Returns true if one spawned. The rotor advances both the ring slot and the species so
    /// repeated calls scatter different creatures around the player.
    /// </summary>
    private bool TrySpawnCreatureNear(Shared.State.PlayerState player)
    {
        if (_creatures.Count >= CreatureHardCap)
        {
            return false;
        }

        var (dx, dz) = SpawnRing[_creatureSpawnRotor % SpawnRing.Length];
        int x = (int)System.Math.Floor(player.Position.X) + dx;
        int z = (int)System.Math.Floor(player.Position.Z) + dz;
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);

        for (int n = 0; n < _speciesRoster.Length; n++)
        {
            var sp = _speciesRoster[(_creatureSpawnRotor + n) % _speciesRoster.Length];
            float y = surface + (sp.Habitat == CreatureHabitat.Air ? 4f : 1f);
            var pos = new Vector3f(x + 0.5f, y, z + 0.5f);
            if (!HabitatSuitable(sp, pos) || ShipInteriorContains(pos))
            {
                continue; // never spawn inside (or clipping into) a landed ship
            }

            _creatureSpawnRotor = (_creatureSpawnRotor + n + 1) % _speciesRoster.Length;
            SpawnCreature(sp, pos);
            return true;
        }

        return false;
    }

    /// <summary>Adds a live creature of the species at the position.</summary>
    private void SpawnCreature(CreatureSpecies sp, Vector3f pos)
        => _creatures.Add(new CombatEntity
        {
            Id = NextEntityId(),
            Kind = sp.Hostile ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            SpeciesId = sp.Id,
            Hostile = sp.Hostile,
            Hull = sp.MaxHealth,
            HullMax = sp.MaxHealth,
            Position = pos,
            DamagePerSecond = sp.AttackDamage,
            Loot = { new ItemAmount(sp.DropItem, sp.DropCount) },
        });

    /// <summary>
    /// Immediately seeds fauna around a spot so a world feels alive the moment a player enters or
    /// arrives (instead of trickling in one creature every few seconds). Habitat-gated + capped; no-op
    /// on barren worlds. The caller sends/broadcasts the creature list.
    /// </summary>
    private void PopulateCreaturesNear(Shared.State.PlayerState player, int count)
    {
        if (_speciesRoster.Length == 0)
        {
            return;
        }

        for (int i = 0; i < count && _creatures.Count < CreatureHardCap; i++)
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
            if (creature.ProvokeTimer > 0)
            {
                creature.ProvokeTimer = System.Math.Max(0, creature.ProvokeTimer - dt);
            }

            if (!_speciesById.TryGetValue(creature.SpeciesId, out var sp))
            {
                continue;
            }

            // A provoked territorial creature hunts like an aggressor until it calms down.
            var temperament = CreatureBehaviour.EffectiveTemperament(sp.Temperament, creature.ProvokeTimer > 0);
            Vector3f? nearest = NearestPlayerPosition(targets, creature.Position);
            double phase = _creatureClock * 0.8 + (StableStringHash(creature.Id) % 360) * (System.Math.PI / 180.0);
            var next = CreatureBehaviour.Step(
                creature.Position, temperament, sp.Speed, SpeciesActive(sp),
                nearest, CreatureAggroRange, CreatureFleeRange, moveDt, phase);

            // Creatures don't walk into the player's ship — hold position at the hull.
            creature.Position = EntityBlockedByShip(next) ? creature.Position : next;
        }
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
                    && other.Position.DistanceSquared(target.Position) <= CreaturePackRallyRange * CreaturePackRallyRange)
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
            var nearest = NearestPlayerPosition(targets, c.Position);
            return nearest is not { } np || np.DistanceSquared(c.Position) > maxSq;
        });
        return removed > 0;
    }

    private static Vector3f? NearestPlayerPosition(List<PlayerSession> targets, Vector3f from)
    {
        Vector3f? best = null;
        float bestSq = float.MaxValue;
        foreach (var s in targets)
        {
            float d = s.State.Position.DistanceSquared(from);
            if (d < bestSq)
            {
                bestSq = d;
                best = s.State.Position;
            }
        }

        return best;
    }

    private void BroadcastCreatures() => Broadcast(new CreatureList { Creatures = _creatures.Select(ToNetCreature).ToArray() });

    private void SendCreatures(PlayerSession session)
        => Send(session, new CreatureList { Creatures = _creatures.Select(ToNetCreature).ToArray() });

    private NetCreature ToNetCreature(CombatEntity e)
    {
        _speciesById.TryGetValue(e.SpeciesId, out var sp);
        bool asleep = sp != null && !SpeciesActive(sp);
        return new NetCreature
        {
            Id = e.Id,
            SpeciesId = e.SpeciesId,
            NameKey = sp?.NameKey ?? "creature.generic.name",
            Hostile = e.Hostile || e.ProvokeTimer > 0, // provoked creatures read as hostile (red tint)
            Asleep = asleep,
            Hull = e.Hull,
            HullMax = e.HullMax,
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            Habitat = (sp?.Habitat ?? CreatureHabitat.Land).ToString(),
            Activity = (sp?.Activity ?? CreatureActivity.Diurnal).ToString(),
            Temperament = (sp?.Temperament ?? CreatureTemperament.Passive).ToString(),
            Size = sp?.Size ?? 1f,
            Legs = sp?.Legs ?? 4,
            HasWings = sp?.HasWings ?? false,
            HasTail = sp?.HasTail ?? false,
            BodySegments = sp?.BodySegments ?? 1,
            ColorRgb = sp?.ColorRgb ?? 0xFFFFFF,
            Glows = sp?.Glows ?? false,
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

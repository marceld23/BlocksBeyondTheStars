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
    private const double CreatureSpawnInterval = 6.0;
    private const float CreatureProximityRange = 4f;
    private const int CreatureCapPerPlayer = 4;
    private const double CreatureBroadcastInterval = 0.5;  // position-sync cadence (client interpolates)
    private const double CreatureMoveDtCap = 0.25;         // cap per-step movement so big ticks can't teleport
    private const float CreatureAggroRange = 10f;          // hunters approach within this
    private const float CreatureFleeRange = 6f;            // skittish flee within this
    private const double CreatureProvokeSeconds = 12.0;    // how long a provoked creature retaliates
    private const float CreaturePackRallyRange = 14f;      // pack-hunters rally kin within this

    private CreatureSpecies[] _speciesRoster = System.Array.Empty<CreatureSpecies>();
    private readonly Dictionary<string, CreatureSpecies> _speciesById = new();
    private readonly List<CombatEntity> _creatures = new();
    private double _creatureSpawnTimer;
    private double _creatureClock;
    private double _creatureBroadcastTimer;
    private int _creatureSpawnRotor;
    private ushort _creatureWaterId, _creatureLavaId;

    /// <summary>Live creatures on the surface (passive + hostile fauna).</summary>
    public IReadOnlyList<CombatEntity> Creatures => _creatures;

    /// <summary>The procedural species this world derived from its seed + planet.</summary>
    public IReadOnlyList<CreatureSpecies> SpeciesRoster => _speciesRoster;

    private void InitCreatures()
    {
        var planet = _content.GetPlanet(_meta.DefaultPlanetType);
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
                if (p.GodMode)
                {
                    continue;
                }

                if (p.Position.DistanceSquared(creature.Position) <= CreatureProximityRange * CreatureProximityRange)
                {
                    p.Health = System.Math.Max(0f, p.Health - (float)(creature.DamagePerSecond * dt));
                    SendPlayerState(session);
                    if (p.Health <= 0f)
                    {
                        RespawnPlayer(session, "Killed by hostile wildlife — recovery to the Medbay heal-tank.");
                    }
                }
            }
        }
    }

    /// <summary>Spawns a roster species suited to the spot near the player (habitat-gated). Returns true if one spawned.</summary>
    private bool TrySpawnCreatureNear(Shared.State.PlayerState player)
    {
        // Rotate through the roster so different species appear; skip ones whose habitat doesn't fit here.
        for (int n = 0; n < _speciesRoster.Length; n++)
        {
            var sp = _speciesRoster[(_creatureSpawnRotor + n) % _speciesRoster.Length];
            float yOffset = sp.Habitat == CreatureHabitat.Air ? 2f : 0f;
            var pos = new Vector3f(player.Position.X + 3f, player.Position.Y + yOffset, player.Position.Z);
            if (!HabitatSuitable(sp, pos))
            {
                continue;
            }

            _creatureSpawnRotor = (_creatureSpawnRotor + n + 1) % _speciesRoster.Length;
            _creatures.Add(new CombatEntity
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
            return true;
        }

        return false;
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
            creature.Position = CreatureBehaviour.Step(
                creature.Position, temperament, sp.Speed, SpeciesActive(sp),
                nearest, CreatureAggroRange, CreatureFleeRange, moveDt, phase);
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

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The hunter's work core (#2645, wilderness folk #2639), shared by the lodge residents (#2641) and the wanderers (#2646).
/// Hunting is real but rare and kid-friendly: <see cref="FindNpcHuntTarget"/> only ever picks a wild, common, passive or
/// skittish land animal — never a companion or a tamer's pet (<c>npc:&lt;id&gt;</c>), a giant or titan, a story creature
/// (an authored species, the Sreekmakra), a clone, a begging or called animal, one a player stands next to or with a player
/// in the line of fire, nor one out of sight. <see cref="NpcShootCreature"/> sends the arrow (<see cref="WorldFx"/> kind
/// <c>"arrow"</c>) and the bow's sound, and the animal leaves exactly like a sentry's kill — it breaks into sparkles on every
/// client (<see cref="CreatureDefeated"/>) — but its yield goes to the caller's <c>bank(itemKey, count)</c> (the hunter's
/// store crate) instead of onto the ground. Nobody is credited: no mission step, achievement counter, deed, bounty, bio
/// sample or game event — the path never touches a player session.
/// <para><b>Animals notice hunters.</b> Animals only ever reacted to players; a hunter is a non-player threat
/// (<see cref="CombatEntity.ThreatPos"/> + <see cref="CombatEntity.ThreatTimer"/>, read by the creature intent through
/// <see cref="ApplyThreatIntent"/>). After a shot the kin within <see cref="CreaturePanicRadius"/> are startled and run from
/// the HUNTER; on a hunting round the caller calls <see cref="ScareCreaturesFrom"/> around the walking hunter, and a
/// skittish animal within its flee range runs from it.</para>
/// <para>The core does ONE action per call: the cadence (one kill every 4–6 minutes, three per in-game day per hunter), the
/// approach and the aim are the caller's.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The longest shot a hunter takes (blocks) — the sentry's reach.</summary>
    private const float NpcHuntShotRange = 14f;

    /// <summary>No shot at an animal a player stands this close to.</summary>
    private const float NpcHuntPlayerClearance = 4f;

    /// <summary>No shot while a player's body is this close to the line of fire (from the bow past the animal).</summary>
    private const float NpcHuntLineClearance = 1.2f;

    /// <summary>How far past the animal the line of fire is kept clear.</summary>
    private const float NpcHuntOvershoot = 3f;

    /// <summary>The heights above a player's feet tested against the line of fire (feet, middle, head).</summary>
    private static readonly float[] NpcBodySampleLifts = { 0.3f, 0.9f, 1.5f };

    /// <summary>The bow's height above the hunter's feet, and the aim point's above the animal's.</summary>
    private const float NpcBowLift = 1.35f;
    private const float NpcHuntAimLift = 0.5f;

    /// <summary>Players within this many blocks see the arrow fly; within the second they hear the bow (short and quiet —
    /// no NPC sound jumble).</summary>
    private const float NpcArrowFxRange = 64f;
    private const float NpcBowSoundRange = 32f;

    /// <summary>The bow's cue id on the client (a short, quiet positional sound).</summary>
    internal const string NpcBowShotSound = "npc_bow_shot";

    /// <summary>The kin of a shot animal are startled and keep the hunter in mind this long.</summary>
    private const double NpcHuntKinPanicSeconds = 6.0;

    /// <summary>"Common": the species' rarity tier on the bio scale (<see cref="BioRules.RarityOfPoints"/>, before a profile's
    /// luck) is at most this — Common or Uncommon.</summary>
    private const int NpcHuntMaxRarity = 1;

    /// <summary>The nearest animal this hunter may hunt within <paramref name="range"/>: wild, common, passive or skittish,
    /// on land, in sight — never a companion or owned animal, a giant, a story creature or clone, a begging or called animal,
    /// one a player stands next to or with a player in the line of fire. Null when there is none.</summary>
    private CombatEntity? FindNpcHuntTarget(ServerNpc npc, float range)
    {
        double rangeSq = (double)range * range;
        List<(CombatEntity C, double D)>? candidates = null;
        foreach (var c in _creatures)
        {
            double d = WrapDistSq(npc.Pos, c.Position);
            if (d <= rangeSq && NpcMayHunt(c))
            {
                (candidates ??= new List<(CombatEntity, double)>()).Add((c, d));
            }
        }

        if (candidates is null)
        {
            return null;
        }

        candidates.Sort((a, b) => a.D.CompareTo(b.D));
        foreach (var (c, _) in candidates)
        {
            if (NpcHuntSees(npc, c) && !NpcHuntEndangersPlayer(npc, c))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Shoots <paramref name="target"/> (range ≤ 14, line of sight): the arrow <c>WorldFx</c> + the bow sound, the
    /// animal leaves with the sparkle like a sentry kill, its loot is banked, its kin flee from the hunter. No player gets
    /// any credit. False when the shot is not possible (out of range, no sight, no longer eligible). It removes the animal
    /// from the creature list — never call it from inside a loop over that list.</summary>
    private bool NpcShootCreature(ServerNpc npc, CombatEntity target, System.Action<string, int> bank)
    {
        if (!_creatures.Contains(target) || !NpcMayHunt(target)
            || WrapDistSq(npc.Pos, target.Position) > (double)NpcHuntShotRange * NpcHuntShotRange
            || !NpcHuntSees(npc, target) || NpcHuntEndangersPlayer(npc, target))
        {
            return false;
        }

        // The arrow flies from the bow (chest height) to the animal — its own coordinates, as CreatureDefeated carries them:
        // the client holds the animal's sparkle until the arrow lands and matches the animal within 3 m of ToX/ToY/ToZ. The
        // arrow goes out in the same call as the kill, before the removal.
        var bow = NpcBowPoint(npc);
        var aim = NpcHuntAimPoint(target);
        FaceTowards(npc, target.Position);
        var arrow = new WorldFx { Kind = "arrow", X = bow.X, Y = bow.Y, Z = bow.Z, ToX = aim.X, ToY = aim.Y, ToZ = aim.Z, SourceId = npc.Id };
        var sound = new SoundFx { SoundId = NpcBowShotSound, X = bow.X, Y = bow.Y, Z = bow.Z };
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue;
            }

            double d = WrapDistSq(s.State.Position, bow);
            if (d <= (double)NpcArrowFxRange * NpcArrowFxRange)
            {
                Send(s, arrow);
            }

            if (d <= (double)NpcBowSoundRange * NpcBowSoundRange)
            {
                Send(s, sound);
            }
        }

        // The animal leaves the way a sentry's kill makes it leave (KillBySentry) — off the list, its wander and wade plans
        // forgotten, the sparkle on every client — but its yield goes into the hunter's store instead of onto the ground.
        _creatures.Remove(target);
        _enemyWander.Remove(target.Id);
        ForgetWadePlan(target.Id);
        foreach (var loot in target.Loot)
        {
            if (loot.Count > 0 && !string.IsNullOrEmpty(loot.Item))
            {
                bank(loot.Item, loot.Count);
            }
        }

        OnCreatureKilled(target, null); // the sparkle (CreatureDefeated); no killer — so no grudge, no credit
        ForgetClone(target); // never a clone (NpcMayHunt) — kept so this path stays the sentry's
        ScareKinFromHunter(target, npc.Pos);
        BroadcastCreatures();
        _log.Info($"NPC '{npc.Name}' ({npc.Id}) hunted '{target.Name}' ({target.Id}).");
        return true;
    }

    /// <summary>A hunter on its round (#2645): every wild animal within <paramref name="radius"/> of
    /// <paramref name="pos"/> notices it for <paramref name="seconds"/>. What it does about it is its temperament's — a
    /// skittish one within its flee range runs from it, a passive one grazes on (unless a shot startled it). Companions
    /// and giants pay no attention. The caller (a lodge's or a wanderer's hunting round) calls it while the hunter walks,
    /// e.g. every second with a radius of 8 and a few seconds.</summary>
    private void ScareCreaturesFrom(Vector3f pos, float radius, double seconds)
    {
        double radiusSq = (double)radius * radius;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion && !c.IsGiant && WrapDistSq(c.Position, pos) <= radiusSq)
            {
                c.ThreatPos = pos;
                c.ThreatTimer = System.Math.Max(c.ThreatTimer, seconds);
            }
        }
    }

    /// <summary>The creature intent's answer to a non-player threat (called only while <see cref="CombatEntity.ThreatTimer"/>
    /// runs, never for an animal that is begging or hunting a player): a skittish animal within <see cref="CreatureFleeRange"/>
    /// of the threat runs from it — unless a player it already runs from is nearer — and a startled non-retaliator within
    /// <see cref="CreaturePanicFleeRange"/> runs from the threat instead of the nearest player. A skittish animal that bolts
    /// from it passes the threat on to its kin, whom the bolt startles (<see cref="StartleKin"/>), so the herd runs from the
    /// hunter too.</summary>
    private void ApplyThreatIntent(CombatEntity c, CreatureTemperament temperament, ref MoveMode intent, ref Vector3f? target)
    {
        double distSq = WrapDistSq(c.Position, c.ThreatPos);
        bool reflex = temperament == CreatureTemperament.Skittish && distSq <= (double)CreatureFleeRange * CreatureFleeRange;
        bool panic = c.PanicTimer > 0 && distSq <= (double)CreaturePanicFleeRange * CreaturePanicFleeRange
                     && !CreatureBehaviour.RetaliatesWhenAttacked(temperament);
        if (!reflex && !panic)
        {
            return;
        }

        if (!panic && intent == MoveMode.Flee && target is { } fleeing && WrapDistSq(c.Position, fleeing) < distSq)
        {
            return; // the reflex runs from the nearer danger — here a player
        }

        if (!panic && c.Loco.Mode != MoveMode.Flee)
        {
            ShareThreatWithKin(c);
        }

        intent = MoveMode.Flee;
        target = Unwrapped(c.Position, c.ThreatPos);
    }

    /// <summary>A skittish animal bolting from a threat hands it to its kin within <see cref="CreaturePanicRadius"/> — the
    /// startle its bolt spreads then runs from the same hunter.</summary>
    private void ShareThreatWithKin(CombatEntity source)
    {
        double radiusSq = (double)CreaturePanicRadius * CreaturePanicRadius;
        foreach (var other in _creatures)
        {
            if (!ReferenceEquals(other, source) && !other.IsCompanion && other.SpeciesId == source.SpeciesId
                && WrapDistSq(other.Position, source.Position) <= radiusSq)
            {
                other.ThreatPos = source.ThreatPos;
                other.ThreatTimer = System.Math.Max(other.ThreatTimer, source.ThreatTimer);
            }
        }
    }

    /// <summary>After a shot: the shot animal's kin within <see cref="CreaturePanicRadius"/> wake, are startled and drop any
    /// food they were after, and run from the hunter for <see cref="NpcHuntKinPanicSeconds"/>.</summary>
    private void ScareKinFromHunter(CombatEntity shot, Vector3f hunter)
    {
        double radiusSq = (double)CreaturePanicRadius * CreaturePanicRadius;
        foreach (var other in _creatures)
        {
            if (other.IsCompanion || other.IsGiant || other.SpeciesId != shot.SpeciesId
                || WrapDistSq(other.Position, shot.Position) > radiusSq)
            {
                continue;
            }

            Startle(other, NpcHuntKinPanicSeconds, dropFood: true);
            other.AwakeOverrideTimer = System.Math.Max(other.AwakeOverrideTimer, CreatureWakeSeconds); // a shot beside a sleeper wakes it
            other.ThreatPos = hunter;
            other.ThreatTimer = System.Math.Max(other.ThreatTimer, NpcHuntKinPanicSeconds);
        }
    }

    /// <summary>Whether a hunter may take this animal (its place in the world aside): wild — no owner (a player's companion
    /// or a tamer's pet), no clone, no giant, not the Sreekmakra in any shape — calm (not hostile, not provoked, not held in
    /// stasis, not begging, called or trotting off after a meal, not the animal a player is taming) and of a huntable
    /// species.</summary>
    private bool NpcMayHunt(CombatEntity c)
        => !c.IsCompanion && c.CompanionId.Length == 0 && !c.IsGiant && c.CloneOf.Length == 0 && !IsSreekmakra(c)
           && !c.Hostile && c.ProvokeTimer <= 0 && c.FrozenTimer <= 0 && c.BegPhase == BegPhase.None
           && !NpcHuntBeingTamed(c)
           && _speciesById.TryGetValue(c.SpeciesId, out var sp) && NpcHuntableSpecies(sp);

    /// <summary>True while a player's taming attempt holds this animal — the player may stand up to 12 blocks off.</summary>
    private bool NpcHuntBeingTamed(CombatEntity c)
    {
        foreach (var attempt in _tameAttempts.Values)
        {
            if (attempt.CreatureId == c.Id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A species a hunter hunts: one of this world's own rolled species (no authored or story creature, no guest
    /// species of a clone or a companion), a passive or skittish land animal that walks or crawls, of an ordinary body (no
    /// titan, giant, arachnid, biped, medusa, ray or flowerling), and common (<see cref="NpcHuntMaxRarity"/>).</summary>
    private bool NpcHuntableSpecies(CreatureSpecies sp)
    {
        if (sp.Habitat != CreatureHabitat.Land || sp.Temperament is not (CreatureTemperament.Passive or CreatureTemperament.Skittish)
            || sp.Hostile || sp.IsGiant || sp.BodyPlan is not (CreatureBodyPlan.Standard or CreatureBodyPlan.Worm)
            || CreatureMotion.ClassOf(sp) is not (MotionClass.Walker or MotionClass.Crawler)
            || sp.Id.StartsWith("au_", System.StringComparison.Ordinal))
        {
            return false;
        }

        bool rolled = false;
        foreach (var r in _speciesRoster)
        {
            if (r.Id == sp.Id)
            {
                rolled = true;
                break;
            }
        }

        return rolled && BioRules.RarityOfPoints(BioContexts.ForCreature(sp, _world.Planet).RarityPoints) <= NpcHuntMaxRarity;
    }

    /// <summary>Whether the active world's own roster holds a species a hunter may hunt at all — many worlds roll none (only
    /// cave, water, air or hostile life), and a hunter there would never find anything (the lodge's job choice asks).</summary>
    private bool WorldHasHuntableSpecies()
    {
        foreach (var sp in _speciesRoster)
        {
            if (NpcHuntableSpecies(sp))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The hunter's sightline: from the bow to the animal's body.</summary>
    private bool NpcHuntSees(ServerNpc npc, CombatEntity target)
        => HasLineOfSight(npc.Pos, target.Position, NpcBowLift, NpcHuntAimLift, skipToCell: false);

    /// <summary>True when a player stands within <see cref="NpcHuntPlayerClearance"/> of the animal or near the line of
    /// fire — from the bow to <see cref="NpcHuntOvershoot"/> past the animal, within <see cref="NpcHuntLineClearance"/> of
    /// the player's feet, middle or head. Any player of this world counts, an invisible observer too.</summary>
    private bool NpcHuntEndangersPlayer(ServerNpc npc, CombatEntity target)
    {
        var bow = NpcBowPoint(npc);
        var aim = Unwrapped(bow, NpcHuntAimPoint(target));
        float dx = aim.X - bow.X, dy = aim.Y - bow.Y, dz = aim.Z - bow.Z;
        float len = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var end = len > 0.01f
            ? new Vector3f(aim.X + dx / len * NpcHuntOvershoot, aim.Y + dy / len * NpcHuntOvershoot, aim.Z + dz / len * NpcHuntOvershoot)
            : aim;
        double nearSq = (double)NpcHuntPlayerClearance * NpcHuntPlayerClearance;
        double lineSq = (double)NpcHuntLineClearance * NpcHuntLineClearance;
        foreach (var s in JoinedInActiveWorld())
        {
            if (InSpace(s.State.PlayerId))
            {
                continue;
            }

            var feet = s.State.Position;
            if (WrapDistSq(feet, target.Position) <= nearSq)
            {
                return true;
            }

            var body = Unwrapped(bow, feet);
            foreach (float lift in NpcBodySampleLifts)
            {
                if (SegmentDistSq(new Vector3f(body.X, body.Y + lift, body.Z), bow, end) <= lineSq)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Squared distance from <paramref name="p"/> to the segment <paramref name="a"/>–<paramref name="b"/> (one frame).</summary>
    private static double SegmentDistSq(Vector3f p, Vector3f a, Vector3f b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
        double apx = p.X - a.X, apy = p.Y - a.Y, apz = p.Z - a.Z;
        double lenSq = abx * abx + aby * aby + abz * abz;
        double t = lenSq > 1e-9 ? System.Math.Clamp((apx * abx + apy * aby + apz * abz) / lenSq, 0.0, 1.0) : 0.0;
        double cx = apx - abx * t, cy = apy - aby * t, cz = apz - abz * t;
        return cx * cx + cy * cy + cz * cz;
    }

    /// <summary>Where the arrow leaves the hunter's bow.</summary>
    private static Vector3f NpcBowPoint(ServerNpc npc) => new(npc.Pos.X, npc.Pos.Y + NpcBowLift, npc.Pos.Z);

    /// <summary>Where the arrow strikes the animal.</summary>
    private static Vector3f NpcHuntAimPoint(CombatEntity target)
        => new(target.Position.X, target.Position.Y + NpcHuntAimLift, target.Position.Z);

    // ------------------------------------------------------------------------------------------------------
    // Test seams
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Test seam (#2645): the id of <see cref="FindNpcHuntTarget"/>'s pick for the NPC with this id, or null.</summary>
    internal string? FindNpcHuntTargetForTest(int npcId, float range)
        => FindNpcHuntTarget(_npcs.First(n => n.Id == npcId), range)?.Id;

    /// <summary>Test seam (#2645): <see cref="NpcShootCreature"/> by ids; the loot lands in <paramref name="banked"/>.</summary>
    internal bool NpcShootCreatureForTest(int npcId, string creatureId, List<ItemAmount> banked)
        => _creatures.FirstOrDefault(c => c.Id == creatureId) is { } target
           && NpcShootCreature(_npcs.First(n => n.Id == npcId), target, (item, count) => banked.Add(new ItemAmount(item, count)));

    /// <summary>Test seam (#2645): <see cref="WorldHasHuntableSpecies"/>.</summary>
    internal bool WorldHasHuntableSpeciesForTest => WorldHasHuntableSpecies();

    /// <summary>Test seam (#2645): <see cref="ScareCreaturesFrom"/>.</summary>
    internal void ScareCreaturesFromForTest(Vector3f pos, float radius, double seconds) => ScareCreaturesFrom(pos, radius, seconds);
}

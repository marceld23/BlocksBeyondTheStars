// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Personal space between animals (#2753): "some big animals stand inside each other". The only spacing there was kept
/// members of ONE species apart, only the nearest one, only while roaming, at 1.5 blocks for nearly every body; the spawner
/// never looked at other animals, and a sleeper was no obstacle at all — an awake animal walked eight blocks into a sleeping
/// one. Now every ground-bound wild animal keeps <see cref="CreatureFootprint.SeparationFactor"/> × the two drawn bodies'
/// radii (<see cref="CreatureFootprint.Radius"/>) from every other one, of any species:
/// <list type="bullet">
/// <item><b>Awake</b>, the nudge runs on the step before it goes through the usual barriers (ship, fence, terrain, body
/// sweep), so a nudge into a wall is refused like any other step; standing animals are nudged too, so an existing pile-up
/// drifts apart on its own. An awake animal next to a sleeper takes the whole push.</item>
/// <item><b>Asleep</b>, overlapping sleepers shuffle apart slowly without waking — only the later one in the list (the
/// later-spawned one) yields, so two sleepers never push each other back and forth.</item>
/// <item><b>Spawning</b> refuses a spot inside another animal's footprint (natural spawns, herd members, admin summons).</item>
/// </list>
/// Fliers, hoverers in the air and swimmers keep their freedom, companions and the giants have their own rules, and two
/// begging animals of one species keep the ring's own spacing (<see cref="BegSeparation"/>). At most
/// <see cref="CreatureHardCap"/> wild animals per world: a few thousand distance checks per tick, no allocation, the list's
/// own deterministic order.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How fast an overlapping sleeper shuffles aside, in blocks per second — slow enough to read as an animal
    /// turning over in its sleep, not as a slide.</summary>
    private const float SleeperShuffleSpeed = 0.4f;

    /// <summary>The slowest push an awake animal gets per second, so a grazer with a tiny cruise speed still clears a
    /// neighbour it stands inside.</summary>
    private const float SpacingMinPushSpeed = 0.5f;

    /// <summary>A roam step the nudge cut to less than this share is a walk into another animal: the roamer picks a fresh
    /// heading instead of pressing on into the other body tick after tick.</summary>
    private const float SpacingStalledRoamShare = 0.3f;

    /// <summary>The herd's and the begging ring's floor (#651/#2018): small members still keep this much room.</summary>
    private const float HerdMinSeparation = 1.5f;

    /// <summary>How far two members of one herd keep apart (<see cref="GroupSteer"/>, <see cref="BegSeparation"/>): the
    /// footprint spacing of their two bodies, never below <see cref="HerdMinSeparation"/>.</summary>
    private static float HerdSeparation(CreatureSpecies sp, float scaleA, float scaleB)
        => System.Math.Max(HerdMinSeparation,
            CreatureFootprint.SeparationDistance(CreatureFootprint.Radius(sp, scaleA), CreatureFootprint.Radius(sp, scaleB)));

    /// <summary>The footprint radius of a live animal: its species' drawn body at its own size factor. 0 for an unknown
    /// species (an old save's id the roster no longer has).</summary>
    private float CreatureFootprintRadius(CombatEntity c)
        => _speciesById.TryGetValue(c.SpeciesId, out var sp) ? CreatureFootprint.Radius(sp, c.SizeScale) : 0f;

    /// <summary>Whether a wild animal is asleep right now — the same rule the movement loop and the wire use.</summary>
    private bool CreatureAsleep(CombatEntity c, CreatureSpecies sp)
        => !c.IsCompanion && !SpeciesActive(sp, c.Position) && c.AwakeOverrideTimer <= 0;

    /// <summary>Whether the spacing applies to a body of this class at all: ground-bound animals and land hoverers (a gas-sac
    /// grazer floats a hand above its feet). Fliers, air hoverers and swimmers pass over and under each other.</summary>
    private static bool KeepsGroundSpacing(CreatureSpecies sp, MotionClass motion)
        => !sp.IsGiant && CreatureMotion.ObeysGroundRules(sp, motion);

    /// <summary>How far an awake animal may be nudged this tick: undoing its whole step plus a cruise step outward, so a
    /// sprinting hunter or fleer cannot push through another body either.</summary>
    private static float SpacingStep(Vector3f from, Vector3f stepped, in LocomotionProfile prof, double dt)
    {
        float dx = stepped.X - from.X, dz = stepped.Z - from.Z;
        float stepLen = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        return stepLen + (System.Math.Max(prof.CruiseSpeed, SpacingMinPushSpeed) * (float)dt);
    }

    /// <summary>Nudges <paramref name="at"/> (the animal's next position) out of the footprint of the neighbour it stands
    /// deepest inside, by at most <paramref name="maxStep"/>. Awake, every other ground-bound wild animal counts; asleep,
    /// only the sleepers EARLIER in the list (the later one yields, so a pair never pushes back and forth). Pairs a body
    /// height or more apart vertically (a cave under a meadow, an animal on a ledge) do not touch. Seam-safe: the neighbour
    /// is unwrapped into this animal's frame.</summary>
    private Vector3f SeparateFromCreatures(CombatEntity self, CreatureSpecies sp, MotionClass motion, Vector3f at,
        float maxStep, bool asleep)
    {
        if (self.IsCompanion || self.IsGiant || maxStep <= 0f || !KeepsGroundSpacing(sp, motion))
        {
            return at;
        }

        float rSelf = CreatureFootprint.Radius(sp, self.SizeScale);
        float reach = CreatureFootprint.SeparationDistance(rSelf, CreatureFootprint.MaxRadius);
        int heightSelf = CreatureBodyHeight(sp);
        bool selfBegs = self.BegPhase != BegPhase.None;
        float bestDepth = 0f, bestSep = 0f;
        Vector3f bestPos = default;
        bool found = false;
        foreach (var other in _creatures)
        {
            if (ReferenceEquals(other, self))
            {
                if (asleep)
                {
                    break; // a sleeper yields only to the sleepers before it
                }

                continue;
            }

            if (other.IsCompanion || other.IsGiant)
            {
                continue;
            }

            var o = Unwrapped(at, other.Position);
            float dx = at.X - o.X, dz = at.Z - o.Z;
            float distSq = (dx * dx) + (dz * dz);
            if (distSq >= reach * reach)
            {
                continue; // the cheap pre-check: not even the largest body would reach this far
            }

            if (!_speciesById.TryGetValue(other.SpeciesId, out var osp) || !KeepsGroundSpacing(osp, MotionClassOf(other, osp)))
            {
                continue;
            }

            if (System.Math.Abs(o.Y - at.Y) >= System.Math.Max(heightSelf, CreatureBodyHeight(osp)))
            {
                continue;
            }

            // Two beggars of one species keep the ring's own spacing (BegSeparation) — the ring is their business.
            if (selfBegs && other.BegPhase != BegPhase.None && other.SpeciesId == self.SpeciesId)
            {
                continue;
            }

            if (asleep && !CreatureAsleep(other, osp))
            {
                continue; // a sleeper never yields to an awake animal — the awake one steps aside
            }

            float sep = CreatureFootprint.SeparationDistance(rSelf, CreatureFootprint.Radius(osp, other.SizeScale));
            float depth = sep - (float)System.Math.Sqrt(distSq);
            if (depth > bestDepth)
            {
                bestDepth = depth;
                bestSep = sep;
                bestPos = o;
                found = true;
            }
        }

        return found ? NudgeApart(at, bestPos, bestSep, maxStep) : at;
    }

    /// <summary>The awake half (#2753): the nudge on the step the movement loop is about to apply. A roamer whose step the
    /// nudge cancelled (it walked into another animal) re-rolls its heading next tick.</summary>
    private Vector3f SpaceAwakeStep(CombatEntity creature, CreatureSpecies sp, MotionClass motion, Vector3f stepped,
        in LocomotionProfile prof, double dt, bool roaming)
    {
        var cur = creature.Position;
        var spaced = SeparateFromCreatures(creature, sp, motion, stepped, SpacingStep(cur, stepped, prof, dt), asleep: false);
        if (roaming && (spaced.X != stepped.X || spaced.Z != stepped.Z))
        {
            float ix = stepped.X - cur.X, iz = stepped.Z - cur.Z;
            float nx = spaced.X - cur.X, nz = spaced.Z - cur.Z;
            float intended = (ix * ix) + (iz * iz);
            if (intended > 1e-6f && ((nx * ix) + (nz * iz)) < SpacingStalledRoamShare * intended)
            {
                creature.Loco.ModeTimer = 0f; // it walked into another body — turn somewhere else
            }
        }

        return spaced;
    }

    /// <summary>The sleeping half (#2753): an overlapping sleeper shuffles aside at <see cref="SleeperShuffleSpeed"/>
    /// without waking — only onto ground it could step to (no climb, no wall, no fence, no ship, no water it would not
    /// walk into). Returns its position unchanged when nothing overlaps or the way is shut.</summary>
    private Vector3f ShuffleSleeper(CombatEntity creature, CreatureSpecies sp, MotionClass motion, double dt, out int? groundHint)
    {
        groundHint = null;
        var cur = creature.Position;
        var pushed = SeparateFromCreatures(creature, sp, motion, cur, SleeperShuffleSpeed * (float)dt, asleep: true);
        if (pushed.X == cur.X && pushed.Z == cur.Z)
        {
            return cur;
        }

        var cand = PreviewStep(sp, motion, cur, pushed, out bool needsRise, out _, out int? nextFeet);
        if (needsRise || StepBlocked(creature, sp, motion, cur, cand, needsRise: false, terrainGates: true, nextFeet))
        {
            return cur;
        }

        groundHint = nextFeet;
        return new Vector3f(cand.X, cur.Y, cand.Z);
    }

    /// <summary>The spawn half (#2753): whether a new animal of <paramref name="sp"/> at <paramref name="pos"/> would stand
    /// inside a live animal's footprint. The newcomer's size factor is not rolled yet, so it counts at its species size; the
    /// full radii (no 0.8 factor) keep fresh herds clear of the movement spacing even for a big individual.</summary>
    private bool CrowdedByCreatures(CreatureSpecies sp, Vector3f pos)
    {
        if (!KeepsGroundSpacing(sp, CreatureMotion.ClassOf(sp)))
        {
            return false;
        }

        float r = CreatureFootprint.Radius(sp, 1f);
        int height = CreatureBodyHeight(sp);
        foreach (var other in _creatures)
        {
            if (other.IsCompanion || other.IsGiant)
            {
                continue;
            }

            var o = Unwrapped(pos, other.Position);
            float dx = pos.X - o.X, dz = pos.Z - o.Z;
            float distSq = (dx * dx) + (dz * dz);
            float reach = r + CreatureFootprint.MaxRadius;
            if (distSq >= reach * reach
                || !_speciesById.TryGetValue(other.SpeciesId, out var osp)
                || !KeepsGroundSpacing(osp, MotionClassOf(other, osp))
                || System.Math.Abs(o.Y - pos.Y) >= System.Math.Max(height, CreatureBodyHeight(osp)))
            {
                continue;
            }

            float sep = r + CreatureFootprint.Radius(osp, other.SizeScale);
            if (distSq < sep * sep)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam (#2753): the footprint radius of a live animal.</summary>
    public float CreatureFootprintRadiusForTest(string id) => CreatureFootprintRadius(_creatures.Find(c => c.Id == id)!);
}

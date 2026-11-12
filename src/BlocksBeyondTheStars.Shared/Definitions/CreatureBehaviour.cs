// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Pure, deterministic creature movement (World systems / §12), kept separate from the server so
/// it is trivially unit-testable. Given a creature's current spot, its temperament and the nearest
/// player, returns the next position one step later:
/// <list type="bullet">
/// <item><b>Sleeping/inactive</b> (off its activity phase) → stays put.</item>
/// <item><b>Aggressive / pack-hunter</b> → moves <i>toward</i> a player within aggro range (hunts).</item>
/// <item><b>Skittish</b> → moves <i>away</i> from a player within flee range.</item>
/// <item>Everything else (and when no player is in range) → <b>wanders</b> on a slow drift.</item>
/// </list>
/// Movement is horizontal (X/Z); the caller keeps the creature's Y (surface/hover/fluid level).
/// </summary>
public static class CreatureBehaviour
{
    public static Vector3f Step(
        Vector3f current,
        CreatureTemperament temperament,
        float speed,
        bool active,
        Vector3f? player,
        float aggroRange,
        float fleeRange,
        double dt,
        double wanderPhase)
    {
        if (!active || speed <= 0f || dt <= 0)
        {
            return current; // resting / sleeping
        }

        double dirX = 0, dirZ = 0;
        if (player is { } p)
        {
            double dx = p.X - current.X;
            double dz = p.Z - current.Z;
            double dist = System.Math.Sqrt(dx * dx + dz * dz);
            if (dist > 1e-4)
            {
                bool hunts = temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
                bool flees = temperament == CreatureTemperament.Skittish;
                if (hunts && dist <= aggroRange)
                {
                    dirX = dx / dist;
                    dirZ = dz / dist;
                }
                else if (flees && dist <= fleeRange)
                {
                    dirX = -dx / dist;
                    dirZ = -dz / dist;
                }
            }
        }

        if (dirX == 0 && dirZ == 0)
        {
            // Organic wander (B12): hold a hashed heading for ~a couple of seconds, then turn to a new one, with
            // a gentle weave within each segment — so creatures roam the biome in varied directions instead of
            // milling on the spot in tight circles (which is what a continuously-rotating drift produced).
            double seg = System.Math.Floor(wanderPhase / 2.2);
            double h = seg * 0.61803398875;            // golden-ratio hash → a fresh heading per segment
            h -= System.Math.Floor(h);
            double heading = h * 6.2831853 + System.Math.Sin(wanderPhase * 1.7) * 0.4;
            dirX = System.Math.Cos(heading);
            dirZ = System.Math.Sin(heading);
            // fall through to the shared step below (|dir| == 1, so distance == speed*dt)
        }

        float step = (float)(speed * dt);
        return new Vector3f(current.X + (float)(dirX * step), current.Y, current.Z + (float)(dirZ * step));
    }

    /// <summary>
    /// A tamed companion's movement (design: <c>docs/developer/CREATURE_TAMING.md</c>): walk toward its owner
    /// when farther than <paramref name="followDistance"/> (hurrying when it has fallen well behind),
    /// otherwise mill gently nearby so it doesn't stand frozen. Horizontal only (the caller keeps Y for
    /// terrain/water/flight), mirroring <see cref="Step"/>.
    /// </summary>
    public static Vector3f FollowStep(
        Vector3f current,
        Vector3f owner,
        float speed,
        float followDistance,
        double dt,
        double wanderPhase)
    {
        if (speed <= 0f || dt <= 0)
        {
            return current;
        }

        double dx = owner.X - current.X;
        double dz = owner.Z - current.Z;
        double dist = System.Math.Sqrt(dx * dx + dz * dz);

        double dirX, dirZ, pace;
        if (dist > followDistance && dist > 1e-4)
        {
            dirX = dx / dist;
            dirZ = dz / dist;
            pace = dist > followDistance * 3 ? speed * 1.8 : speed; // hurry when far behind, ease off when close
        }
        else
        {
            // Close enough: a slow idle drift (same hashed-heading wander as Step) so it loiters by the owner.
            double seg = System.Math.Floor(wanderPhase / 2.2);
            double h = seg * 0.61803398875;
            h -= System.Math.Floor(h);
            double heading = h * 6.2831853 + System.Math.Sin(wanderPhase * 1.7) * 0.4;
            dirX = System.Math.Cos(heading);
            dirZ = System.Math.Sin(heading);
            pace = speed * 0.35;
        }

        float step = (float)(pace * dt);
        return new Vector3f(current.X + (float)(dirX * step), current.Y, current.Z + (float)(dirZ * step));
    }

    /// <summary>
    /// Whether a roaming step from one terrain column into another is blocked for this creature (#648,
    /// reworked per motion class in #1331). Creatures have no colliders, so without this gate cliffs would be
    /// climbed in a single step, land animals would march along the seabed and water animals strand ashore.
    /// Blocked steps are handled like the ship-hull barrier: the caller steers around or re-rolls a heading,
    /// so nothing can ever get stuck.
    /// <list type="bullet">
    /// <item><b>Walkers / crawlers</b>: the ground may rise by at most <see cref="CreatureMotion.StepUpLimit"/>
    /// (one block — a jump for walkers, a slow climb-over for crawlers and giants; two is a wall, as for the
    /// player) and drop by at most <see cref="CreatureMotion.StepDownLimit"/> (a real fall: walkers 3, crawlers
    /// 2, giants 1). The target column's water must be wadeable (≤ 1 deep) unless the animal is amphibious.</item>
    /// <item><b>Swimmers</b>: never step into a dry column — in water they keep to their water body, and a stranded one
    /// (#3552: its old "legacy freedom" let a fish glide along a player's walkway) can only step back into water. The
    /// server sends a stranded fish away while nobody looks. Amphibians may leave.</item>
    /// <item><b>Fliers and hoverers</b> are unaffected — except a LAND hoverer (a gas-sac grazer), which the server
    /// gates as a walker (#1862, <see cref="CreatureMotion.IsLandHoverer"/>): it floats 0.8 above the ground, so a
    /// two-block wall and a moat must stop it like everything else that lives down there.</item>
    /// </list>
    /// Depths are in water cells (surface − bed); pass 0 for a dry column. Since #3552 only the target column's depth
    /// decides; <paramref name="curWaterDepth"/> is kept so the callers' argument lists stay as they are.
    /// </summary>
    public static bool TerrainStepBlocked(
        MotionClass motion,
        bool giant,
        bool amphibious,
        int curFeetY,
        int nextFeetY,
        int curWaterDepth,
        int nextWaterDepth)
    {
        switch (motion)
        {
            case MotionClass.Walker:
            case MotionClass.Crawler:
                int rise = nextFeetY - curFeetY;
                if (rise > CreatureMotion.StepUpLimit(motion))
                {
                    return true; // too high to jump or haul over — a wall
                }

                if (-rise > CreatureMotion.StepDownLimit(motion, giant))
                {
                    return true; // too far to drop — a cliff edge
                }

                return !amphibious && nextWaterDepth > 1; // wading is fine, swimming is not (amphibians swim)
            case MotionClass.Swimmer:
                return !amphibious && nextWaterDepth <= 0; // never onto dry land — stranded, only back into water (#3552)
            default:
                return false; // fliers + hoverers
        }
    }

    /// <summary>Whether the spawner's biome-native pass would starve a spot (#1325): fewer than two roster
    /// species are native to (or agnostic of) <paramref name="biome"/>. With a 9-species roster spread over
    /// several biomes a region can easily have exactly ONE native, and the native-first pass then turned
    /// every spawn around a base in that region into the same species — 36 of one kind at a swamp base.
    /// A starved spot skips straight to the any-species pass so the region still gets variety.</summary>
    public static bool BiomePassStarved(System.Collections.Generic.IReadOnlyList<CreatureSpecies> roster, int biome)
    {
        int eligible = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            int affinity = roster[i].BiomeAffinity;
            if (affinity < 0 || affinity == biome)
            {
                eligible++;
            }
        }

        return eligible < 2;
    }

    /// <summary>Blends a heading toward a target heading by fraction <paramref name="t"/> (0..1) along the
    /// SHORT way around the circle (#651 — school/flock alignment). Pure and wrap-safe, so ±π seams never
    /// produce a spin the long way round.</summary>
    public static float BlendHeading(float current, float target, float t)
    {
        float diff = target - current;
        while (diff > System.Math.PI) diff -= 6.2831853f;
        while (diff < -System.Math.PI) diff += 6.2831853f;
        return current + diff * System.Math.Clamp(t, 0f, 1f);
    }

    /// <summary>
    /// Whether a creature fights back when attacked. Already-hostile hunters do; <b>territorial</b>
    /// species turn hostile when provoked; passive grazers and skittish fleers do not retaliate.
    /// </summary>
    public static bool RetaliatesWhenAttacked(CreatureTemperament temperament) => temperament
        is CreatureTemperament.Territorial
        or CreatureTemperament.Aggressive
        or CreatureTemperament.PackHunter;

    /// <summary>
    /// The temperament a creature acts on right now: a <b>provoked territorial</b> creature behaves
    /// as <see cref="CreatureTemperament.Aggressive"/> (hunts + attacks); otherwise its base
    /// temperament stands (skittish keep fleeing, passives keep wandering even if provoked).
    /// </summary>
    public static CreatureTemperament EffectiveTemperament(CreatureTemperament baseTemperament, bool provoked)
        => provoked && baseTemperament == CreatureTemperament.Territorial
            ? CreatureTemperament.Aggressive
            : baseTemperament;

    // ---------------- Duel taming + pack defence (#3365, the wild Soundis) ----------------

    /// <summary>The share of its health a duelling animal never drops below — it gives up there instead (kid-friendly: no
    /// animal ever dies in a duel).</summary>
    public const float DuelSurrenderShare = 0.2f;

    /// <summary>A duel ends when the player walks this far (blocks) from the animal.</summary>
    public const float DuelWalkAwayRange = 24f;

    /// <summary>Kin within this range (blocks) of an attacked pack defender come to defend it.</summary>
    public const float PackDefenceRange = 14f;

    /// <summary>How long (seconds) a defending pack stays angry with the attacker before it calms down.</summary>
    public const double PackDefenceSeconds = 20.0;

    /// <summary>The gentlest bite (health per second) of a duelling or defending animal: a species that never bites still
    /// fights back a little.</summary>
    public const float DuelMinBite = 2f;

    /// <summary>The hardest bite (health per second) of a duelling or defending animal: none fights too hard.</summary>
    public const float DuelMaxBite = 5f;

    /// <summary>Whether this species is tamed in a duel instead of the translator ritual.</summary>
    public static bool TamesByDuel(CreatureSpecies sp) => sp.DuelTame;

    /// <summary>Whether this species' kin defend an attacked member — a behaviour next to the temperaments: the pack defender
    /// fights back whatever its temperament, but only the attacker and only for a while.</summary>
    public static bool DefendsKin(CreatureSpecies sp) => sp.PackDefender;

    /// <summary>The lowest health a duelling animal can be brought to: <see cref="DuelSurrenderShare"/> of its maximum.</summary>
    public static float DuelSurrenderHull(float hullMax) => System.Math.Max(1f, hullMax * DuelSurrenderShare);

    /// <summary>The bite of a duelling or defending animal: its own, held between <see cref="DuelMinBite"/> and
    /// <see cref="DuelMaxBite"/>.</summary>
    public static float DuelBite(float attackDamage) => System.Math.Clamp(attackDamage, DuelMinBite, DuelMaxBite);
}

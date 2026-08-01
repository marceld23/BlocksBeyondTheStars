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
    /// Whether a roaming step from one terrain column into another is blocked for this creature (#648).
    /// Creatures have no colliders — their Y is snapped to the habitat height every tick — so without
    /// this gate cliffs are climbed in a single-tick teleport, land animals march along the seabed and
    /// water animals strand ashore. Blocked steps are handled like the ship-hull barrier: the caller
    /// discards the move and re-rolls a heading, so nothing can ever get stuck.
    /// <list type="bullet">
    /// <item><b>Land</b>: the surface may step by at most 2 blocks (1 for a <see cref="CreatureBodyPlan.Titan"/>,
    /// matching its spawn flatness gate), and the target column's water must be wadeable (≤ 1 deep).</item>
    /// <item><b>Water</b>: a creature that is in water never steps into a dry column (an already-stranded
    /// individual keeps its legacy freedom so it can still wander at all).</item>
    /// <item>Fliers, cave and lava dwellers and amphibians are unaffected.</item>
    /// </list>
    /// Depths are in water cells (surface − bed); pass 0 for a dry column.
    /// </summary>
    public static bool TerrainStepBlocked(
        CreatureHabitat habitat,
        CreatureBodyPlan bodyPlan,
        int curSurfaceY,
        int nextSurfaceY,
        int curWaterDepth,
        int nextWaterDepth)
    {
        switch (habitat)
        {
            case CreatureHabitat.Land:
                int limit = bodyPlan == CreatureBodyPlan.Titan ? 1 : 2;
                if (System.Math.Abs(nextSurfaceY - curSurfaceY) > limit)
                {
                    return true; // cliff — a soft wall in both directions (no climbing, no plunging)
                }

                return nextWaterDepth > 1; // wading is fine, swimming is not
            case CreatureHabitat.Water:
                return curWaterDepth > 0 && nextWaterDepth <= 0; // never leave the water body
            default:
                return false;
        }
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
}

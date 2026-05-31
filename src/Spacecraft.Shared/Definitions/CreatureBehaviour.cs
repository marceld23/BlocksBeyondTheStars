using Spacecraft.Shared.Geometry;

namespace Spacecraft.Shared.Definitions;

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
            // Slow wander along a per-creature drift direction.
            dirX = System.Math.Cos(wanderPhase);
            dirZ = System.Math.Sin(wanderPhase);
        }

        float step = (float)(speed * dt);
        return new Vector3f(current.X + (float)(dirX * step), current.Y, current.Z + (float)(dirZ * step));
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

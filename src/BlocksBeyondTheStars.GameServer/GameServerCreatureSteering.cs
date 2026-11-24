// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Herd steering (#639): the pull toward the group centre, a pack hunter's flank point, and the per-species
/// movement profiles.
/// </summary>
public sealed partial class GameServer
{
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
        float nearestSq = float.MaxValue, nearX = 0f, nearZ = 0f, nearScale = 1f;
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
                nearScale = other.SizeScale;
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

        // Separation (#651): personal space sized by the two drawn bodies (#2753, CreatureFootprint), so herd members never
        // overlap — it was Size × 0.6, which ignored the segments and the size factor and gave nearly every body 1.5 blocks.
        float sepDist = HerdSeparation(sp, self.SizeScale, nearScale);
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

    /// <summary>The movement profile for a species id (falls back to a default if somehow unknown).</summary>
    private LocomotionProfile ProfileFor(string speciesId)
        => _locoProfiles.TryGetValue(speciesId, out var p) ? p : default;

    /// <summary>Test seam (#2668): the movement profile a species moves with.</summary>
    internal LocomotionProfile ProfileForTest(string speciesId) => ProfileFor(speciesId);

    /// <summary>A flanking target for a pack-hunter: a point on a small ring around the player, offset by a
    /// per-individual angle, so kin converge from spread directions and encircle instead of all stacking on one
    /// approach line. <paramref name="dx"/>/<paramref name="dz"/> are (player - creature).</summary>
    private Vector3f FlankPoint(CombatEntity c, Vector3f player, float dx, float dz)
    {
        float bearing = (float)System.Math.Atan2(-dz, -dx); // player → creature
        float a = bearing + FlankSpread(c.Id);
        const float ring = 2.0f;
        return new Vector3f(player.X + (float)System.Math.Cos(a) * ring, player.Y, player.Z + (float)System.Math.Sin(a) * ring);
    }

    /// <summary>A pack hunter's own flank offset (±0.7 rad), stable per individual. #2687: drawn from the well-mixed
    /// <see cref="StableHashing"/> hash — <c>h·31 % 1000</c> of <c>e{n}</c> put consecutive hunters 0.0014 rad apart, so a
    /// pack spawned together all took the same line and never encircled. No salt string: this runs every tick per hunter,
    /// and the id alone allocates nothing.</summary>
    private static float FlankSpread(string id) => (float)((StableHashing.Unit(id) - 0.5) * 1.4);

    /// <summary>Test seam (#2687): a pack hunter's flank offset in radians.</summary>
    internal static float FlankSpreadForTest(string id) => FlankSpread(id);
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// How far apart celestial bodies must sit in the space-flight view, and the relaxation that
/// enforces it. Pure math, no Unity — the client's space view is the only caller, but keeping it
/// here (next to <see cref="WorldConstants.CircumferenceFor"/>, the single source of body size)
/// makes the spacing testable and keeps the size model in one place.
/// <para>Why a separation pass is needed at all: the star map lays bodies out in system units with
/// fixed constants (a moon orbits its planet at 90 of them), while a body's RENDERED size comes
/// from its real walkable circumference — a planet's radius alone is 69…169 system units. A moon's
/// star-map orbit is therefore always *inside* its own planet once drawn, so the view has to decide
/// the spacing itself (#493).</para>
/// </summary>
public static class SystemBodyLayout
{
    /// <summary>Smallest clear space between two bodies' surfaces, in flight-view units. Deliberately
    /// above the ship's keep-out margin (10) so there is always a gap the ship can fly through.</summary>
    public const float MinBodyGap = 14f;

    /// <summary>…and the gap grows with the two bodies, so big worlds stay visibly apart instead of
    /// being separated by the same hairline as two small rocks. A flat gap made every moon hug its
    /// planet: the clamp fires for essentially every moon, so the gap it leaves *is* the moon's
    /// visible altitude.</summary>
    public const float BodyGapFraction = 0.55f;

    /// <summary>Clear space to keep between the surfaces of two bodies of these radii.</summary>
    public static float ClearGapFor(float radiusA, float radiusB)
        => System.MathF.Max(MinBodyGap, (radiusA + radiusB) * BodyGapFraction);

    /// <summary>Centre distance at which a body of <paramref name="bodyRadius"/> clears the surface of
    /// its parent — the minimum orbit a moon is drawn at.</summary>
    public static float MinOrbitFor(float parentRadius, float bodyRadius)
        => parentRadius + bodyRadius + ClearGapFor(parentRadius, bodyRadius);

    /// <summary>
    /// Relaxes overlapping bodies apart in the x-z plane until every pair has its
    /// <see cref="ClearGapFor"/> of clear space, nudging both halves of a pair by half the deficit.
    /// <para>The body the player launched from is passed separately: it never moves (the scene is
    /// centred on it) and hangs far below the flight plane, so it only pushes the bodies listed in
    /// <paramref name="boundToFixed"/> — its own moons, which are laid out in *its* plane. Without
    /// that, nothing stopped the pass from shoving one of those back into it.</para>
    /// </summary>
    /// <param name="x">Body x coordinates, updated in place.</param>
    /// <param name="z">Body z coordinates, updated in place.</param>
    /// <param name="radius">Body radii, parallel to <paramref name="x"/>/<paramref name="z"/>.</param>
    /// <param name="fixedX">x of the body that never moves.</param>
    /// <param name="fixedZ">z of the body that never moves.</param>
    /// <param name="fixedRadius">Radius of the body that never moves.</param>
    /// <param name="boundToFixed">Indices laid out in the fixed body's plane, which must clear it.</param>
    /// <param name="iterations">Relaxation budget. Runs once per launch, never per frame.</param>
    public static void SeparateXZ(
        System.Span<float> x,
        System.Span<float> z,
        System.ReadOnlySpan<float> radius,
        float fixedX,
        float fixedZ,
        float fixedRadius,
        System.ReadOnlySpan<int> boundToFixed,
        int iterations = 24)
    {
        int count = radius.Length;
        for (int iter = 0; iter < iterations; iter++)
        {
            bool moved = false;

            for (int a = 0; a < count; a++)
            {
                for (int c = a + 1; c < count; c++)
                {
                    float dx = x[a] - x[c], dz = z[a] - z[c];
                    float dist = System.MathF.Sqrt(dx * dx + dz * dz);
                    float need = radius[a] + radius[c] + ClearGapFor(radius[a], radius[c]);
                    if (dist >= need)
                    {
                        continue;
                    }

                    float nx, nz;
                    if (dist > 0.0001f)
                    {
                        nx = dx / dist; nz = dz / dist;
                    }
                    else
                    {
                        // Co-located → spread by a golden angle, so a pile-up fans out instead of
                        // picking one arbitrary direction for every body in it.
                        nx = System.MathF.Cos(a * 2.39996f); nz = System.MathF.Sin(a * 2.39996f);
                    }

                    float push = (need - dist) * 0.5f;
                    x[a] += nx * push; z[a] += nz * push;
                    x[c] -= nx * push; z[c] -= nz * push;
                    moved = true;
                }
            }

            foreach (int a in boundToFixed)
            {
                float dx = x[a] - fixedX, dz = z[a] - fixedZ;
                float dist = System.MathF.Sqrt(dx * dx + dz * dz);
                float need = radius[a] + fixedRadius + ClearGapFor(radius[a], fixedRadius);
                if (dist >= need)
                {
                    continue;
                }

                float nx = dist > 0.0001f ? dx / dist : System.MathF.Cos(a * 2.39996f);
                float nz = dist > 0.0001f ? dz / dist : System.MathF.Sin(a * 2.39996f);
                x[a] = fixedX + nx * need; // the fixed body never gives way
                z[a] = fixedZ + nz * need;
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }
    }
}

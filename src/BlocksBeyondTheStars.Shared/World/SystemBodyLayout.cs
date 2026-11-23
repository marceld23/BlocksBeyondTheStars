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
    /// <summary>System (star-map) units → flight-view units. The client renders the resident system at
    /// this scale; the server uses the SAME constant when it parks mineable rock clusters at the
    /// flight-view positions of the system's asteroid bodies (#683) — one number, or the two sides
    /// disagree about where "at that asteroid" is.
    /// <para>History: 0.30 until 2026-06 ("a slog"), 0.16 until 2026-09 ("planets huddle" — one planet in
    /// ten had barely a planet-width of empty space to its neighbour). 0.24 (#1600) trades a longer hop
    /// (nearest planet ≈ 20 s instead of 13 s in the starter ship) for roughly twice the room.</para></summary>
    public const float FlightViewScale = 0.24f;

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

    /// <summary>A moon's clear gap to its parent is this much wider than the general body gap (#1601).
    /// Every moon's star-map orbit lies inside its drawn planet, so the clamp below IS the moon layout —
    /// at the plain gap a planet and its moons read as one clump. Applies to the parent pair only; moon ↔
    /// moon spacing in the ladder and the relax pass keep <see cref="ClearGapFor"/>.</summary>
    public const float MoonOrbitGapFactor = 1.5f;

    /// <summary>Centre distance at which a body of <paramref name="bodyRadius"/> clears the surface of
    /// its parent by <see cref="MoonOrbitGapFactor"/> × the clear gap — the minimum orbit a moon is drawn at.</summary>
    public static float MinOrbitFor(float parentRadius, float bodyRadius)
        => parentRadius + bodyRadius + ClearGapFor(parentRadius, bodyRadius) * MoonOrbitGapFactor;

    /// <summary>The orbit of the next moon slot around a parent (#548): outside the parent's surface
    /// (<see cref="MinOrbitFor"/>) and a clear gap above the slot before it, held at <paramref name="lastOrbit"/> by a body
    /// of <paramref name="lastRadius"/> — so a planet's moons stack outward in rings instead of sharing one radius.</summary>
    public static float NextMoonOrbit(float parentRadius, float moonRadius, float lastOrbit, float lastRadius)
        => System.MathF.Max(
            MinOrbitFor(parentRadius, moonRadius),
            lastOrbit + lastRadius + moonRadius + ClearGapFor(lastRadius, moonRadius));

    // ---- the flight scene's own numbers (#3824): shared, so the server can send an NPC ship to a body ----

    /// <summary>Where the body the pilot launched from hangs in the flight scene: below the flight plane, a little
    /// astern. The scene is centred on it, so this never changes.</summary>
    public const float HomeX = 0f;

    /// <inheritdoc cref="HomeX"/>
    public const float HomeY = -150f;

    /// <inheritdoc cref="HomeX"/>
    public const float HomeZ = -20f;

    /// <summary>The launch body is drawn this much bigger than the same body seen from another world's flight.</summary>
    public const float HomeDiameterFactor = 3.2f;

    /// <summary>How far outside a body's surface a ship is held (it cannot fly into the sphere).</summary>
    public const float KeepOutMargin = 10f;

    /// <summary>The land prompt shows within body radius + <see cref="KeepOutMargin"/> + this band.</summary>
    public const float LandBand = 40f;

    /// <summary>A body's drawn diameter from its walkable circumference: ~13 (an asteroid) … ~23 (a moon) … ~46–62
    /// (a planet; a lone giant reaches ~81).</summary>
    public static float OrbitDiameter(int circumference) => 8f + (circumference / 220f);

    /// <summary>#3969: a body's drawn diameter — the one its data fixes (<paramref name="fixedDiameter"/> above 0: Jupiter looks
    /// far bigger than its walkable size), else the one its circumference gives (<see cref="OrbitDiameter"/>). The server
    /// (<see cref="BodyProfile"/>) and the client (<c>NetBody.VisibleDiameter</c>) both draw and fly by it.</summary>
    public static float VisibleDiameter(float fixedDiameter, int circumference)
        => fixedDiameter > 0f ? fixedDiameter : OrbitDiameter(circumference);

    /// <summary>Centre distance at which a ship is well inside a body's landing range — where the autopilot stops and
    /// where an NPC ship begins its descent.</summary>
    public static float LandApproachDistance(float bodyRadius) => bodyRadius + KeepOutMargin + (LandBand * 0.5f);

    // ---- a moon's own flight scene (#3919) ----

    /// <summary>Clear space between the launch column and any body's keep-out shell. The launch column is the line every
    /// lift-off climbs (x = z = 0, from below up to the flight plane); its top is where the pilot gets the controls.
    /// Roomy enough for the ship that launches beside another one (8 units to the side, #2440).</summary>
    public const float LaunchColumnClearance = 8f;

    /// <summary>
    /// #3919: the centre distance from the launch column at which a moon's parent planet is drawn in that moon's own
    /// flight scene. The star map's moon orbit (90 system units, 21.6 flight units for a first moon) lies inside the
    /// planet's keep-out shell (its radius + <see cref="KeepOutMargin"/>, at least 25.4), so every launch from a first moon
    /// ended its climb inside the shell: the keep-out shoved the ship out in one frame and the ship shot away from the
    /// planet. The planet now sits at the first moon slot it hands out anywhere (<see cref="MinOrbitFor"/> of the moon's
    /// true radius, not of its oversized launch-body drawing) — never closer than its shell plus
    /// <see cref="LaunchColumnClearance"/>, and never closer than the star map puts it.
    /// </summary>
    public static float HomeParentDistance(float starMapDistance, float parentRadius, float homeMoonRadius)
        => System.MathF.Max(
            starMapDistance,
            System.MathF.Max(MinOrbitFor(parentRadius, homeMoonRadius), parentRadius + KeepOutMargin + LaunchColumnClearance));

    /// <summary>
    /// #3919: where a moon's parent planet sits in that moon's flight scene — in its true direction (the star-map offset
    /// <paramref name="dx"/>/<paramref name="dz"/>, already in flight units) at <see cref="HomeParentDistance"/>. The moon
    /// you launched from holds that first slot of the planet's ring (the column is where it is), so the planet's other
    /// moons, which are laid out around it, take the slots above it (<see cref="NextMoonOrbit"/>). The client draws the
    /// planet here and the server sends its landing traders here — one rule, or the two disagree.
    /// </summary>
    public static (float X, float Z) HomeParentPosition(float dx, float dz, float parentRadius, float homeMoonRadius)
    {
        float len = System.MathF.Sqrt((dx * dx) + (dz * dz));
        float dist = HomeParentDistance(len, parentRadius, homeMoonRadius);
        return len > 0.0001f ? (dx / len * dist, dz / len * dist) : (dist, 0f);
    }

    /// <summary>
    /// Relaxes overlapping bodies apart in the x-z plane until every pair has its
    /// <see cref="ClearGapFor"/> of clear space, nudging both halves of a pair by half the deficit.
    /// <para>The body the player launched from is passed separately: it never moves (the scene is
    /// centred on it) and hangs far below the flight plane, so it only pushes the bodies listed in
    /// <paramref name="boundToFixed"/> — its own moons, which are laid out in *its* plane. Without
    /// that, nothing stopped the pass from shoving one of those back into it.</para>
    /// <para>The launch column never gives way either (#3919): every other body is held clear of it
    /// (<see cref="ClearLaunchColumn"/>), so no climb ends inside a body's keep-out shell.</para>
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

            moved |= ClearLaunchColumn(x, z, radius, boundToFixed);
            if (!moved)
            {
                break;
            }
        }
    }

    /// <summary>
    /// #3919: holds every body of the flight plane clear of the launch column (the scene's x = z = 0 axis, where every
    /// climb ends): a body whose keep-out shell, plus <see cref="LaunchColumnClearance"/>, reaches the column is pushed
    /// straight out from it — the column never gives way. The bodies in <paramref name="boundToFixed"/> are skipped: they
    /// hang in the launch body's plane, far below the climb. A body there would have caught the ship as the climb ended
    /// (its keep-out shoves the ship out in a single frame), so this used to hit a launch from a moon — its planet hung
    /// that close — and, more rarely, a landable asteroid that happened to orbit right by the launch body.
    /// </summary>
    /// <returns>True when a body moved.</returns>
    private static bool ClearLaunchColumn(
        System.Span<float> x, System.Span<float> z, System.ReadOnlySpan<float> radius, System.ReadOnlySpan<int> boundToFixed)
    {
        bool moved = false;
        for (int a = 0; a < radius.Length; a++)
        {
            float need = radius[a] + KeepOutMargin + LaunchColumnClearance;
            float dist = System.MathF.Sqrt((x[a] * x[a]) + (z[a] * z[a]));
            if (dist >= need || Contains(boundToFixed, a))
            {
                continue;
            }

            x[a] = dist > 0.0001f ? x[a] / dist * need : System.MathF.Cos(a * 2.39996f) * need;
            z[a] = dist > 0.0001f ? z[a] / dist * need : System.MathF.Sin(a * 2.39996f) * need;
            moved = true;
        }

        return moved;
    }

    private static bool Contains(System.ReadOnlySpan<int> indices, int value)
    {
        foreach (int i in indices)
        {
            if (i == value)
            {
                return true;
            }
        }

        return false;
    }
}

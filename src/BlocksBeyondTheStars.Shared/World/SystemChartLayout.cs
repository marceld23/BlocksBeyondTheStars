// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The projection behind the flight system chart (#597, orbit paths #623): how the current system's
/// flight coordinates are fitted into the square chart, and how an orbit ring's radius follows from a
/// body's projected position. Pure math, no Unity — the client's <c>SpaceMap</c> is the only caller,
/// but keeping it here (beside <see cref="SystemBodyLayout"/>, which decides where the bodies are
/// rendered in the first place) makes the two invariants testable.
/// <para>Invariant 1 — <b>everything fits</b>: <see cref="FitScale"/> is chosen so every body, plus the
/// marker drawn at it, lands inside the chart's usable half-width. Without that the star would have to be
/// rim-pinned again and every ring centred on it would be in the wrong place. Centring on the star rather
/// than on the launch body is not free — it zooms out by up to a measured 1.72× on real systems, where the
/// launch body sits inside a far-flung asteroid's orbit — which the drawn-marker floor absorbs.</para>
/// <para>Invariant 2 — <b>rings pass through their markers</b>: an orbit radius is derived from the
/// body's PROJECTED position (<see cref="OrbitRadius"/>), never from its star-map orbit radius. Three
/// passes distort the latter before a body is drawn — the launch body is pinned to a fixed spot below
/// the flight plane, moons are re-laddered onto <see cref="SystemBodyLayout.MinOrbitFor"/> slots, and
/// <see cref="SystemBodyLayout.SeparateXZ"/> nudges overlapping pairs apart — so a ring drawn at the
/// star-map radius would visibly miss its planet.</para>
/// </summary>
public static class SystemChartLayout
{
    /// <summary>Breathing room on the fitted extent, so the outermost body's label and ring don't sit
    /// exactly on the chart edge.</summary>
    public const float FitMargin = 1.05f;

    /// <summary>Smallest orbit radius, in chart units, still worth drawing a ring for. Below this the
    /// ring is inside its own body's disc and reads as a smudge rather than a path — the guard that
    /// keeps a tightly-packed system legible.</summary>
    public const float MinRingRadius = 10f;

    /// <summary>Smallest extent the chart is ever fitted to, so a system with a single nearby body
    /// doesn't zoom in absurdly far.</summary>
    public const float MinExtent = 60f;

    /// <summary>
    /// Scene units → chart units, chosen so every point fits inside <paramref name="chartHalf"/> of
    /// <paramref name="centreX"/>/<paramref name="centreZ"/> (the star, once the chart is star-centred),
    /// with <paramref name="markerPad"/> chart units held back for the marker drawn at each point.
    /// <para>The fit deliberately measures POSITIONS only and reserves a flat chart-unit margin for the
    /// markers, rather than padding each point by its body's scene radius. A body's drawn disc is clamped
    /// to a maximum size no matter how big the body is, so its scene radius is not what it occupies on the
    /// chart — and the launch body, drawn 3.2× oversized in the flight scene, would otherwise dominate the
    /// fit and zoom the whole chart out (measured: up to 1.77× on real systems) for a disc that is capped
    /// anyway.</para>
    /// </summary>
    /// <param name="chartHalf">Usable half-width of the chart, in chart units.</param>
    /// <param name="x">Scene x of every point that must fit.</param>
    /// <param name="z">Scene z, parallel to <paramref name="x"/>.</param>
    /// <param name="centreX">Scene x the chart centres on.</param>
    /// <param name="centreZ">Scene z the chart centres on.</param>
    /// <param name="markerPad">Chart units to hold back for the largest marker drawn at a point.</param>
    public static float FitScale(
        float chartHalf,
        System.ReadOnlySpan<float> x,
        System.ReadOnlySpan<float> z,
        float centreX,
        float centreZ,
        float markerPad = 0f)
    {
        float extent = MinExtent;
        for (int i = 0; i < x.Length; i++)
        {
            float dx = x[i] - centreX, dz = z[i] - centreZ;
            float reach = System.MathF.Sqrt(dx * dx + dz * dz);
            if (reach > extent)
            {
                extent = reach;
            }
        }

        return System.MathF.Max(1f, chartHalf - markerPad) / (extent * FitMargin);
    }

    /// <summary>
    /// Projects a flight-scene point onto the chart: chart units from the chart's centre, with
    /// <paramref name="chartY"/> carrying the scene's z. Paired with <see cref="FromChart"/> — the two live
    /// together so the click path cannot drift out of step with the drawing path, which is exactly how
    /// clicks ended up landing somewhere other than where they were made.
    /// </summary>
    public static void ToChart(
        float sceneX, float sceneZ, float scale, float centreX, float centreZ,
        out float chartX, out float chartY)
    {
        chartX = (sceneX - centreX) * scale;
        chartY = (sceneZ - centreZ) * scale;
    }

    /// <summary>The exact inverse of <see cref="ToChart"/>: the flight-scene point under a chart position
    /// (a click). <paramref name="scale"/> must be the one the chart was drawn with.</summary>
    public static void FromChart(
        float chartX, float chartY, float scale, float centreX, float centreZ,
        out float sceneX, out float sceneZ)
    {
        sceneX = (scale == 0f ? 0f : chartX / scale) + centreX;
        sceneZ = (scale == 0f ? 0f : chartY / scale) + centreZ;
    }

    /// <summary>The orbit radius to draw for a body whose projected chart position is
    /// <paramref name="chartX"/>/<paramref name="chartZ"/>, measured from the chart's centre — i.e. from
    /// the star. Guarantees the ring passes exactly through the body's own marker.</summary>
    public static float OrbitRadius(float chartX, float chartZ)
        => System.MathF.Sqrt(chartX * chartX + chartZ * chartZ);

    /// <summary>Whether an orbit of this drawn radius is worth a ring (see
    /// <see cref="MinRingRadius"/>).</summary>
    public static bool ShowRing(float chartRadius) => chartRadius >= MinRingRadius;
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The projection and the display rules behind the hyperspace chart (#1603): the stars-only galaxy view
/// on the flight chart's second tab. Pure math and string rules, no Unity — the client's
/// <c>GalaxyChartWidget</c> / <c>SpaceMap</c> are the only callers, but keeping the rules here makes the
/// three invariants testable against REAL generated galaxies:
/// <list type="number">
/// <item><b>Everything fits</b> — a galaxy grows OUTWARD of the home system (#1123, up to ~1500 map units
/// out), so the chart is fitted to the bounding box of every system rather than to the fixed 1000² box
/// the procedural systems are drawn in.</item>
/// <item><b>Names never leak</b> — a system the player has never entered shows as an unknown star
/// (#1113), unless a radar array is fitted; the travel screen applies the very same rule.</item>
/// <item><b>Lanes are undirected</b> — a relay jump lane (#1125) links two systems in either order.</item>
/// </list>
/// </summary>
public static class GalaxyChartLayout
{
    /// <summary>Breathing room on the fitted extent so a rim star's label stays inside the chart.</summary>
    public const float FitMargin = 1.08f;

    /// <summary>Smallest extent (map units) the chart is ever fitted to, so a two-system galaxy is not
    /// blown up until the stars touch the rim.</summary>
    public const float MinExtent = 150f;

    /// <summary>The bounding-box centre of the given systems — the point the chart centres on. A galaxy
    /// with grown systems is lopsided around home, so a box centre (not home, not a mean) keeps the
    /// far rim and the dense core both on the chart.</summary>
    public static void Centre(System.ReadOnlySpan<float> x, System.ReadOnlySpan<float> y, out float centreX, out float centreY)
    {
        if (x.Length == 0)
        {
            centreX = 0f;
            centreY = 0f;
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < x.Length; i++)
        {
            minX = System.MathF.Min(minX, x[i]);
            maxX = System.MathF.Max(maxX, x[i]);
            minY = System.MathF.Min(minY, y[i]);
            maxY = System.MathF.Max(maxY, y[i]);
        }

        centreX = (minX + maxX) * 0.5f;
        centreY = (minY + maxY) * 0.5f;
    }

    /// <summary>Map units → chart units, chosen so every star lands inside <paramref name="chartHalf"/>
    /// of the centre with <paramref name="markerPad"/> chart units held back for the star disc drawn there.</summary>
    public static float FitScale(
        float chartHalf,
        System.ReadOnlySpan<float> x,
        System.ReadOnlySpan<float> y,
        float centreX,
        float centreY,
        float markerPad = 0f)
    {
        float extent = MinExtent;
        for (int i = 0; i < x.Length; i++)
        {
            float dx = x[i] - centreX, dy = y[i] - centreY;
            float reach = System.MathF.Sqrt(dx * dx + dy * dy);
            if (reach > extent)
            {
                extent = reach;
            }
        }

        return System.MathF.Max(1f, chartHalf - markerPad) / (extent * FitMargin);
    }

    /// <summary>Projects a star-map position onto the chart (chart units from the chart centre).</summary>
    public static void ToChart(float mapX, float mapY, float scale, float centreX, float centreY, out float chartX, out float chartY)
    {
        chartX = (mapX - centreX) * scale;
        chartY = (mapY - centreY) * scale;
    }

    /// <summary>The star closest to a chart point within <paramref name="snapRadius"/>, or -1. Ties go to
    /// the lower index, so a click between two overlapping stars is at least deterministic.</summary>
    public static int Pick(System.ReadOnlySpan<float> chartX, System.ReadOnlySpan<float> chartY, float pointX, float pointY, float snapRadius)
    {
        int best = -1;
        float bestSq = snapRadius * snapRadius;
        for (int i = 0; i < chartX.Length; i++)
        {
            float dx = chartX[i] - pointX, dy = chartY[i] - pointY;
            float sq = dx * dx + dy * dy;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The name a system shows on any chart or list (#1113): its real name once the player has
    /// entered it, is in it, or flies a radar array — otherwise the localized "unknown" label. The
    /// frontier tag is NOT part of this (callers append it), so the rule stays a pure name gate.</summary>
    public static string DisplayName(string name, bool known, bool isCurrent, bool hasRadarArray, string unknownLabel)
        => known || isCurrent || hasRadarArray ? name : unknownLabel;

    /// <summary>Whether a relay jump lane links the two systems, in either direction (#1125). The lane
    /// arrays are parallel; a mismatch in length is tolerated by walking the shorter one.</summary>
    public static bool HasLane(string[]? laneA, string[]? laneB, string systemA, string systemB)
    {
        if (laneA is null || laneB is null || string.IsNullOrEmpty(systemA) || string.IsNullOrEmpty(systemB) || systemA == systemB)
        {
            return false;
        }

        int n = System.Math.Min(laneA.Length, laneB.Length);
        for (int i = 0; i < n; i++)
        {
            if ((laneA[i] == systemA && laneB[i] == systemB) || (laneA[i] == systemB && laneB[i] == systemA))
            {
                return true;
            }
        }

        return false;
    }
}

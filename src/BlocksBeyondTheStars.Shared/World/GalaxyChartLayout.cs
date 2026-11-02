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

    /// <summary>Projects a star-map position through a zoomed and panned <paramref name="view"/> (#3129):
    /// <paramref name="scale"/> is the fit scale of the whole galaxy, the view multiplies it and centres on its focus.</summary>
    public static void ToChart(float mapX, float mapY, float scale, GalaxyChartView view, out float chartX, out float chartY)
        => ToChart(mapX, mapY, scale * view.Zoom, view.FocusX, view.FocusY, out chartX, out chartY);

    /// <summary>The star-map point under a chart point — the inverse of <see cref="ToChart(float, float, float, GalaxyChartView, out float, out float)"/>.</summary>
    public static void FromChart(float chartX, float chartY, float scale, GalaxyChartView view, out float mapX, out float mapY)
    {
        float s = System.MathF.Max(1e-6f, scale * view.Zoom);
        mapX = view.FocusX + chartX / s;
        mapY = view.FocusY + chartY / s;
    }

    /// <summary>Zooms to <paramref name="zoom"/> about a chart point: the star-map point under (<paramref name="chartX"/>,
    /// <paramref name="chartY"/>) stays under it, so the mouse wheel zooms into what the cursor points at.</summary>
    public static GalaxyChartView ZoomAbout(GalaxyChartView view, float zoom, float chartX, float chartY, float scale)
    {
        FromChart(chartX, chartY, scale, view, out float mapX, out float mapY);
        float s = System.MathF.Max(1e-6f, scale * zoom);
        return new GalaxyChartView(zoom, mapX - chartX / s, mapY - chartY / s);
    }

    /// <summary>Moves the view by a drag of (<paramref name="dChartX"/>, <paramref name="dChartY"/>) chart units — the
    /// galaxy follows the pointer.</summary>
    public static GalaxyChartView Pan(GalaxyChartView view, float dChartX, float dChartY, float scale)
    {
        float s = System.MathF.Max(1e-6f, scale * view.Zoom);
        return new GalaxyChartView(view.Zoom, view.FocusX - dChartX / s, view.FocusY - dChartY / s);
    }

    /// <summary>Holds a view inside sensible limits: zoom between 1 (every star fits) and <paramref name="maxZoom"/>, and
    /// the focus inside the stars' bounding box — the further in, the more of the box it may roam, and at zoom 1 it sits
    /// on the box centre (the fitted chart), so panning can never lose the galaxy off the chart.</summary>
    public static GalaxyChartView Clamp(GalaxyChartView view, float maxZoom, float minX, float maxX, float minY, float maxY)
    {
        float zoom = System.Math.Clamp(float.IsNaN(view.Zoom) ? 1f : view.Zoom, 1f, System.MathF.Max(1f, maxZoom));
        float roam = 1f - 1f / zoom; // 0 at zoom 1 → the focus is pinned to the centre
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
        float hx = (maxX - minX) * 0.5f * roam, hy = (maxY - minY) * 0.5f * roam;
        return new GalaxyChartView(zoom, System.Math.Clamp(view.FocusX, cx - hx, cx + hx), System.Math.Clamp(view.FocusY, cy - hy, cy + hy));
    }

    /// <summary>Smallest zoom limit, so even a roomy galaxy can be looked at closely.</summary>
    public const float MinMaxZoom = 3f;

    /// <summary>Largest zoom limit — beyond it a chart shows two stars and nothing else.</summary>
    public const float MaxZoomCap = 12f;

    /// <summary>How far the chart may zoom in (#3129): far enough that the two CLOSEST stars stand at least
    /// <paramref name="minSeparation"/> chart units apart (a random galaxy has no minimum spacing, so two discs may sit on
    /// top of each other at the fitted scale), held between <see cref="MinMaxZoom"/> and <see cref="MaxZoomCap"/>.</summary>
    public static float MaxZoom(System.ReadOnlySpan<float> x, System.ReadOnlySpan<float> y, float scale, float minSeparation)
    {
        float closest = float.MaxValue;
        for (int i = 0; i < x.Length; i++)
        {
            for (int j = i + 1; j < x.Length; j++)
            {
                float dx = x[i] - x[j], dy = y[i] - y[j];
                closest = System.MathF.Min(closest, System.MathF.Sqrt(dx * dx + dy * dy));
            }
        }

        if (closest == float.MaxValue)
        {
            return MinMaxZoom; // fewer than two stars
        }

        float needed = closest * scale <= 1e-6f ? MaxZoomCap : minSeparation / (closest * scale);
        return System.Math.Clamp(needed, MinMaxZoom, MaxZoomCap);
    }

    /// <summary>The bounding box of the given systems (all zero for none).</summary>
    public static void Bounds(System.ReadOnlySpan<float> x, System.ReadOnlySpan<float> y, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = maxX = minY = maxY = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            minX = i == 0 ? x[i] : System.MathF.Min(minX, x[i]);
            maxX = i == 0 ? x[i] : System.MathF.Max(maxX, x[i]);
            minY = i == 0 ? y[i] : System.MathF.Min(minY, y[i]);
            maxY = i == 0 ? y[i] : System.MathF.Max(maxY, y[i]);
        }
    }

    /// <summary>Which labels to draw so none covers another (#3129): greedily in <paramref name="order"/> (most important
    /// first — the current system, the selection, other players, known systems, then the unknown "?"), a label is shown
    /// when its box (centre, width <paramref name="w"/>, height <paramref name="h"/>, plus <paramref name="gap"/>) touches
    /// no box already shown. Zooming in spreads the stars, so hidden labels come back by themselves.</summary>
    public static bool[] LabelsShown(System.ReadOnlySpan<float> cx, System.ReadOnlySpan<float> cy, System.ReadOnlySpan<float> w,
        System.ReadOnlySpan<float> h, System.ReadOnlySpan<int> order, float gap = 2f)
    {
        var shown = new bool[cx.Length];
        var kept = new System.Collections.Generic.List<int>(cx.Length);
        foreach (int i in order)
        {
            if (i < 0 || i >= cx.Length || w[i] <= 0f)
            {
                continue;
            }

            bool free = true;
            foreach (int k in kept)
            {
                if (System.MathF.Abs(cx[i] - cx[k]) * 2f < w[i] + w[k] + gap * 2f
                    && System.MathF.Abs(cy[i] - cy[k]) * 2f < h[i] + h[k] + gap * 2f)
                {
                    free = false;
                    break;
                }
            }

            if (free)
            {
                shown[i] = true;
                kept.Add(i);
            }
        }

        return shown;
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

/// <summary>A zoomed and panned look at the hyperspace chart (#3129): <see cref="Zoom"/> multiplies the fit scale (1 = the
/// whole galaxy), <see cref="FocusX"/>/<see cref="FocusY"/> is the star-map point in the chart's middle.</summary>
public readonly struct GalaxyChartView
{
    public GalaxyChartView(float zoom, float focusX, float focusY)
    {
        Zoom = zoom;
        FocusX = focusX;
        FocusY = focusY;
    }

    public float Zoom { get; }

    public float FocusX { get; }

    public float FocusY { get; }
}

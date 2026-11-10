// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3444 — the bend speed limits along a rail line, sampled every <see cref="Step"/> blocks of arc once per line (the
/// server builds it lazily when a line changes). Each sample's limit is <see cref="RailMotion.CurveSpeed(float, float)"/>
/// of the line's two curvatures there (<see cref="Curvature"/>): the turn between the chords before and after it, over
/// their length, split into the <b>sideways</b> turn (the change of heading, held to
/// <see cref="RailMotion.LateralAcceleration"/>) and the turn <b>up or down</b> (the change of slope — a crest or a dip,
/// held to the more generous <see cref="RailMotion.VerticalAcceleration"/>), the lower limit of the two. The limits are
/// then spread along the line both ways by the braking curve (a sample is never above √(neighbour² + 2·a·step)), so a
/// train running at the limit slows down <i>before</i> a bend and speeds up again after it at
/// <see cref="RailMotion.Acceleration"/>. Pure, Unity-free.
/// <para>The chords are measured on the spline's sampled polyline (ten segments per span between two pylons), so they
/// must be long against its segments: a chord shorter than a segment sees one polyline corner as a tight bend (the old
/// 4-block chord read a curve of radius ≈ 240 on 64-block spans as one of radius ≈ 95 — 19 blocks/s instead of 24). A
/// chord of at least twice the local segment (<see cref="ChordAt"/>, never under <see cref="MinChord"/>) keeps that error
/// at about 6 % at most — the polyline's corners shift a measured curvature by no more than (segment / chord)² / 4 of the
/// true one; in the middle of a line it matches the Catmull-Rom curve's own curvature within a few per cent. (The
/// alternative, the spline's curvature evaluated per span, would read its own kinks between very uneven spans as
/// hairpins; the chord smooths them over its length, as the train's own wagons do.)</para>
/// </summary>
public sealed class RailCurveLimits
{
    /// <summary>The sample spacing along the arc (blocks).</summary>
    public const float Step = 2f;

    /// <summary>The shortest chord the curvature is measured over (blocks) — twice the ≈ 6.4-block segments of the
    /// longest span the linker makes (<see cref="RailRules.LinkerRange"/>, ten segments per span), rounded down; a longer
    /// local segment lengthens the chord (<see cref="ChordAt"/>).</summary>
    public const float MinChord = 12f;

    private readonly float[] _limits;

    private RailCurveLimits(RailSpline spline, float[] limits)
    {
        Spline = spline;
        _limits = limits;
    }

    /// <summary>The line these limits were sampled from (a rebuilt line has a new spline: rebuild the limits too).</summary>
    public RailSpline Spline { get; }

    /// <summary>The number of samples.</summary>
    public int Count => _limits.Length;

    /// <summary>Samples a line's bend limits.</summary>
    public static RailCurveLimits Build(RailSpline spline)
    {
        float total = spline.TotalArc;
        if (total <= 0f)
        {
            return new RailCurveLimits(spline, Array.Empty<float>());
        }

        int n = Math.Max(1, (int)Math.Ceiling(total / Step) + (spline.Closed ? 0 : 1));
        var limits = new float[n];
        for (int i = 0; i < n; i++)
        {
            var (horizontal, vertical) = Curvature(spline, Math.Min(i * Step, total));
            limits[i] = RailMotion.CurveSpeed(horizontal, vertical);
        }

        Spread(limits, spline.Closed);
        return new RailCurveLimits(spline, limits);
    }

    /// <summary>The chord the curvature at an arc is measured over (blocks): twice the spline's local segment
    /// (<see cref="RailSpline.SampleSpacingAt"/>), at least <see cref="MinChord"/>.</summary>
    public static float ChordAt(RailSpline spline, float arc) => Math.Max(MinChord, 2f * spline.SampleSpacingAt(arc));

    /// <summary>The line's curvatures (1 / radius) at an arc, from the chords to the points <see cref="ChordAt"/> blocks
    /// before and after it: <b>horizontal</b> — the change of heading between them (seen from above) over their mean
    /// length; <b>vertical</b> — the change of slope between them over the same length (a crest or a dip). Both 0 at an open
    /// line's very ends (no chord behind or ahead) and where a chord is far longer than its arc (a loop that circles the
    /// world's seam); the heading's change is 0 where a chord runs (nearly) straight up or down.</summary>
    public static (float Horizontal, float Vertical) Curvature(RailSpline spline, float arc)
    {
        float total = spline.TotalArc;
        float chord = ChordAt(spline, arc);
        float back = arc - chord, ahead = arc + chord;
        if (!spline.Closed)
        {
            back = Math.Max(0f, back);
            ahead = Math.Min(total, ahead);
        }

        var p0 = spline.PointAtUnwrapped(back);
        var p1 = spline.PointAtUnwrapped(arc);
        var p2 = spline.PointAtUnwrapped(ahead);
        var d1 = new Vector3f(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
        var d2 = new Vector3f(p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z);
        float l1 = Length(d1), l2 = Length(d2);
        if (l1 < chord * 0.25f || l2 < chord * 0.25f || l1 > chord * 1.5f || l2 > chord * 1.5f)
        {
            return (0f, 0f);
        }

        float h1 = (float)Math.Sqrt((d1.X * d1.X) + (d1.Z * d1.Z)), h2 = (float)Math.Sqrt((d2.X * d2.X) + (d2.Z * d2.Z));
        float mean = (l1 + l2) * 0.5f;
        float pitchTurn = Math.Abs((float)(Math.Atan2(d2.Y, h2) - Math.Atan2(d1.Y, h1)));
        float headingTurn = 0f;
        if (h1 > l1 * 0.05f && h2 > l2 * 0.05f)
        {
            headingTurn = Math.Abs((float)Math.IEEERemainder(Math.Atan2(d2.Z, d2.X) - Math.Atan2(d1.Z, d1.X), 2.0 * Math.PI));
        }

        return (headingTurn / mean, pitchTurn / mean);
    }

    /// <summary>The bend limit at an arc (the sample at or before it and the next, the lower one).</summary>
    public float At(float arc) => MinOver(arc, arc);

    /// <summary>The lowest bend limit anywhere between two arcs (either order; a loop's arcs may run past its ends) —
    /// over a whole train, so its last wagon is not whipped round the bend its cab just left.</summary>
    public float MinOver(float from, float to)
    {
        int n = _limits.Length;
        if (n == 0 || float.IsNaN(from) || float.IsNaN(to) || float.IsInfinity(from) || float.IsInfinity(to))
        {
            return RailMotion.MaxSpeed;
        }

        float lo = Math.Min(from, to), hi = Math.Max(from, to);
        long i0 = (long)Math.Floor(lo / Step), i1 = (long)Math.Ceiling(hi / Step);
        if (i1 - i0 >= n)
        {
            i1 = i0 + n - 1; // the whole line
        }

        bool closed = Spline.Closed;
        float min = RailMotion.MaxSpeed;
        for (long i = i0; i <= i1; i++)
        {
            int k = closed ? (int)(((i % n) + n) % n) : (int)Math.Clamp(i, 0L, n - 1L);
            min = Math.Min(min, _limits[k]);
        }

        return min;
    }

    /// <summary>Spreads every limit along the line both ways by the braking curve: a sample is never more than
    /// √(neighbour² + 2·a·<see cref="Step"/>). A loop is swept twice round so a bend near its start reaches its end.</summary>
    private static void Spread(float[] limits, bool closed)
    {
        int n = limits.Length;
        float rise = 2f * RailMotion.Acceleration * Step;
        int sweep = closed ? 2 * n : n - 1;
        for (int k = 1; k <= sweep; k++)
        {
            int i = k % n, prev = (k - 1) % n;
            limits[i] = Math.Min(limits[i], (float)Math.Sqrt((limits[prev] * limits[prev]) + rise));
        }

        for (int k = sweep; k >= 1; k--)
        {
            int i = (k - 1) % n, next = k % n;
            limits[i] = Math.Min(limits[i], (float)Math.Sqrt((limits[next] * limits[next]) + rise));
        }
    }

    private static float Length(Vector3f v) => (float)Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
}

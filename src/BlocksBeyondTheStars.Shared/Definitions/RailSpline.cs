// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The energy line of a monorail (#2113): a Catmull-Rom spline through the tops of a line's pylons, sampled into a
/// polyline with an arc-length table — the <see cref="SandwormPath"/> idea, in three dimensions and wrap-aware. The
/// server and every client build the same curve from the same pylon list (the wire sends the points), so a train's
/// arc position is all that has to travel: both sides read the same pose at the same arc. A closed line (the last pylon
/// linked back to the first) is a loop; an open one is a path the train reverses on at both ends. The points are
/// unwrapped along X relative to the first point, so a line across the world seam is one continuous curve; the pose's
/// X is wrapped back on the way out.
/// </summary>
public sealed class RailSpline
{
    private const int SamplesPerSpan = 10;

    private readonly List<Vector3f> _points = new();
    private readonly List<float> _arc = new();
    private readonly int _circumference;

    private RailSpline(bool closed, int circumference)
    {
        Closed = closed;
        _circumference = circumference;
    }

    public bool Closed { get; }

    /// <summary>The curve's total arc length (the loop's full round for a closed line).</summary>
    public float TotalArc => _arc.Count == 0 ? 0f : _arc[_arc.Count - 1];

    /// <summary>The unwrapped control points the curve runs through (the pylon tops plus the rise).</summary>
    public IReadOnlyList<Vector3f> Controls { get; private set; } = Array.Empty<Vector3f>();

    /// <summary>Builds the line through <paramref name="pylonTops"/> (world points, in line order). Two points make a
    /// straight line; one point makes nothing (a train cannot run on it).</summary>
    public static RailSpline Build(IReadOnlyList<Vector3f> pylonTops, bool closed, int circumference)
    {
        var spline = new RailSpline(closed && pylonTops.Count >= 3, circumference);
        if (pylonTops.Count < 2)
        {
            return spline;
        }

        // Unwrap along X so a line over the seam is continuous.
        var ctrl = new List<Vector3f>(pylonTops.Count);
        var first = pylonTops[0];
        ctrl.Add(first);
        var prev = first;
        for (int i = 1; i < pylonTops.Count; i++)
        {
            var p = pylonTops[i];
            float dx = (float)WorldConstants.WrapDeltaX((double)(p.X - prev.X), circumference);
            var un = new Vector3f(prev.X + dx, p.Y, p.Z);
            ctrl.Add(un);
            prev = un;
        }

        spline.Controls = ctrl;
        spline.Construct(ctrl);
        return spline;
    }

    private void Construct(List<Vector3f> ctrl)
    {
        int n = ctrl.Count;
        int spans = Closed ? n : n - 1;
        AddPoint(ctrl[0]);
        for (int i = 0; i < spans; i++)
        {
            var p0 = ctrl[Wrap(i - 1, n)];
            var p1 = ctrl[Wrap(i, n)];
            var p2 = ctrl[Wrap(i + 1, n)];
            var p3 = ctrl[Wrap(i + 2, n)];
            if (!Closed)
            {
                p0 = ctrl[Math.Max(0, i - 1)];
                p3 = ctrl[Math.Min(n - 1, i + 2)];
            }

            for (int k = 1; k <= SamplesPerSpan; k++)
            {
                float t = k / (float)SamplesPerSpan;
                AddPoint(new Vector3f(CatmullRom(p0.X, p1.X, p2.X, p3.X, t), CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t), CatmullRom(p0.Z, p1.Z, p2.Z, p3.Z, t)));
            }
        }
    }

    private int Wrap(int i, int n) => ((i % n) + n) % n;

    private void AddPoint(Vector3f p)
    {
        if (_points.Count == 0)
        {
            _points.Add(p);
            _arc.Add(0f);
            return;
        }

        var q = _points[_points.Count - 1];
        float dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
        _points.Add(p);
        _arc.Add(_arc[_arc.Count - 1] + (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Normalises an arc position onto the curve: a loop wraps, a path clamps.</summary>
    public float NormalizeArc(float s)
    {
        float total = TotalArc;
        if (total <= 0f)
        {
            return 0f;
        }

        if (Closed)
        {
            s %= total;
            return s < 0f ? s + total : s;
        }

        return Math.Clamp(s, 0f, total);
    }

    /// <summary>The unwrapped point at arc length <paramref name="s"/>.</summary>
    public Vector3f PointAtUnwrapped(float s)
    {
        if (_points.Count == 0)
        {
            return default;
        }

        s = NormalizeArc(s);
        if (s <= 0f)
        {
            return _points[0];
        }

        if (s >= TotalArc)
        {
            return _points[_points.Count - 1];
        }

        int lo = 0, hi = _arc.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_arc[mid] <= s)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        float span = _arc[hi] - _arc[lo];
        float f = span < 1e-6f ? 0f : (s - _arc[lo]) / span;
        var a = _points[lo];
        var b = _points[hi];
        return new Vector3f(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);
    }

    /// <summary>The world point at arc length <paramref name="s"/> (X wrapped into the world).</summary>
    public Vector3f PointAt(float s)
    {
        var p = PointAtUnwrapped(s);
        return new Vector3f((float)WorldConstants.WrapX((double)p.X, _circumference), p.Y, (float)WorldConstants.WrapZ((double)p.Z, _circumference));
    }

    /// <summary>The heading (radians about +Y, forward = (cos, 0, sin)) at arc length <paramref name="s"/> in the direction of
    /// increasing arc; <paramref name="direction"/> −1 turns it round.</summary>
    public float YawAt(float s, int direction = 1)
    {
        var a = PointAtUnwrapped(s - 0.75f);
        var b = PointAtUnwrapped(s + 0.75f);
        float dx = b.X - a.X, dz = b.Z - a.Z;
        if (dx * dx + dz * dz < 1e-6f)
        {
            return 0f;
        }

        float yaw = (float)Math.Atan2(dz, dx);
        return direction < 0 ? yaw + (float)Math.PI : yaw;
    }

    /// <summary>The wagon pose at an arc: its centre on the line (the floor sits <see cref="RailRules.HoverHeight"/> higher) and its heading.</summary>
    public (Vector3f Pos, float Yaw) PoseAt(float s, int direction = 1)
    {
        var p = PointAt(s);
        return (new Vector3f(p.X, p.Y + RailRules.HoverHeight, p.Z), YawAt(s, direction));
    }

    /// <summary>The arc of the point on the curve nearest to a world point, and its distance (wrap-aware, sampled).</summary>
    public (float Arc, float Distance) Nearest(Vector3f world)
    {
        if (_points.Count == 0)
        {
            return (0f, float.MaxValue);
        }

        float bestD = float.MaxValue, bestArc = 0f;
        for (int i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            float dx = (float)WorldConstants.WrapDeltaX((double)(world.X - p.X), _circumference);
            float dy = world.Y - p.Y;
            float dz = world.Z - p.Z;
            float d = dx * dx + dy * dy + dz * dz;
            if (d < bestD)
            {
                bestD = d;
                bestArc = _arc[i];
            }
        }

        return (bestArc, (float)Math.Sqrt(bestD));
    }

    /// <summary>The sampled polyline (unwrapped), for drawing and for the clearance check.</summary>
    public IReadOnlyList<Vector3f> Samples => _points;
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>What a sandworm does above the sand (#2001).</summary>
public enum WormMove : byte
{
    None,   // under the sand — nothing of it shows
    Breach, // an arc like a whale: up through the surface, over, and back down — partly visible
    Rear,   // it rises out of the sand as a tower, sways, and strikes down at its target
}

/// <summary>
/// The path of one sandworm move (#2001), deterministic from a handful of numbers so the server (hits, damage) and every
/// client (the drawn body) agree without streaming the body: the server sends the move, its anchor, direction, height
/// and the seconds already run; both sides build the same curve and read the same head position at the same time.
///
/// The curve is a Catmull-Rom spline through a few control points in the vertical plane along the move's direction,
/// sampled into a polyline with an arc-length table. The head runs along it at <see cref="HeadSpeed"/> (a Rear holds
/// at the top and strikes faster); the body follows the head's own track — segment i sits i·spacing behind the head
/// along the curve — so the worm always slides through the hole its head made. Everything below the surface is simply
/// drawn inside the terrain: the voxel ground hides it, and no block ever moves.
/// </summary>
public sealed class SandwormPath
{
    /// <summary>How fast the head travels along the curve (blocks/s).</summary>
    public const float HeadSpeed = 18f;

    /// <summary>A Rear pauses at the top of the tower this long before it strikes.</summary>
    public const float HoldSeconds = 2.4f;

    /// <summary>The strike comes down this much faster than the climb.</summary>
    public const float StrikeSpeedFactor = 1.8f;

    private const int SamplesPerSpan = 14;

    private readonly List<Vector3f> _points = new();
    private readonly List<float> _arc = new();
    private readonly float _dirX, _dirZ;

    private float _startArc, _endArc, _topArc, _strikeArc;
    private float _tRise, _tHold, _tStrike;

    private SandwormPath(WormMove move, Vector3f anchor, float dirX, float dirZ, float peak, float length, float girth, float strikeDistance)
    {
        Move = move;
        Anchor = anchor;
        float len = (float)System.Math.Sqrt(dirX * dirX + dirZ * dirZ);
        _dirX = len < 1e-5f ? 1f : dirX / len;
        _dirZ = len < 1e-5f ? 0f : dirZ / len;
        Peak = peak < 6f ? 6f : peak;
        Length = length < 10f ? 10f : length;
        Girth = girth < 2f ? 2f : girth;
        StrikeDistance = strikeDistance;
        Depth = Girth * 1.4f + 2f;
    }

    public WormMove Move { get; }

    /// <summary>Breach: the ground point under the top of the arc. Rear: the strike target on the surface. Y = the surface.</summary>
    public Vector3f Anchor { get; }

    public float Peak { get; }
    public float Length { get; }
    public float Girth { get; }
    public float StrikeDistance { get; }

    /// <summary>How deep the worm runs under the sand while hidden.</summary>
    public float Depth { get; }

    /// <summary>The move's total running time (seconds).</summary>
    public float Duration { get; private set; }

    /// <summary>Rear only: when the head reaches its target (seconds into the move); -1 for a breach.</summary>
    public float StrikeTime { get; private set; } = -1f;

    /// <summary>The curve's total arc length.</summary>
    public float TotalArc => _arc.Count == 0 ? 0f : _arc[_arc.Count - 1];

    /// <summary>Builds a move. <paramref name="strikeDistance"/> (Rear): how far before the target the tower rises.</summary>
    public static SandwormPath Build(WormMove move, Vector3f anchor, float dirX, float dirZ, float peak, float length, float girth,
        float strikeDistance = 0f)
    {
        var p = new SandwormPath(move, anchor, dirX, dirZ, peak, length, girth, strikeDistance);
        p.Construct();
        return p;
    }

    private void Construct()
    {
        float d = Depth, l = Length, h = Peak;
        var ctrl = new List<(float U, float Y)>(12);
        int topIndex, strikeIndex, backIndex;
        if (Move == WormMove.Rear)
        {
            float dist = StrikeDistance > 4f ? StrikeDistance : System.Math.Max(8f, h * 0.45f);
            // Rises at u = -dist beside its target, hooks over at the top and strikes down onto u = 0.
            ctrl.Add((-dist - l - 12f, -d));
            ctrl.Add((-dist - l * 0.5f, -d));
            ctrl.Add((-dist - Girth * 1.5f, -d * 0.8f));
            ctrl.Add((-dist, h * 0.3f));
            ctrl.Add((-dist, h * 0.72f));
            topIndex = ctrl.Count;
            ctrl.Add((-dist * 0.7f, h));
            ctrl.Add((-dist * 0.3f, h * 0.62f));
            strikeIndex = ctrl.Count;
            ctrl.Add((0f, 0f));
            backIndex = ctrl.Count;
            ctrl.Add((Girth * 1.2f, -d));
            ctrl.Add((l * 0.5f, -d));
            ctrl.Add((l + 12f, -d));
        }
        else
        {
            float r = System.Math.Max(14f, System.Math.Min(60f, h * 0.9f));
            ctrl.Add((-r - l - 12f, -d));
            ctrl.Add((-r - l * 0.5f, -d));
            ctrl.Add((-r, -d));
            ctrl.Add((-r * 0.5f, h * 0.55f));
            topIndex = ctrl.Count;
            ctrl.Add((0f, h));
            ctrl.Add((r * 0.5f, h * 0.55f));
            strikeIndex = -1;
            backIndex = ctrl.Count;
            ctrl.Add((r, -d));
            ctrl.Add((r + l * 0.5f, -d));
            ctrl.Add((r + l + 12f, -d));
        }

        // Catmull-Rom through the control points (the end points repeated as their own neighbours).
        var ctrlArc = new float[ctrl.Count];
        AddPoint(ctrl[0].U, ctrl[0].Y);
        ctrlArc[0] = 0f;
        for (int i = 0; i < ctrl.Count - 1; i++)
        {
            var p0 = ctrl[i > 0 ? i - 1 : i];
            var p1 = ctrl[i];
            var p2 = ctrl[i + 1];
            var p3 = ctrl[i + 2 < ctrl.Count ? i + 2 : i + 1];
            for (int k = 1; k <= SamplesPerSpan; k++)
            {
                float t = k / (float)SamplesPerSpan;
                AddPoint(CatmullRom(p0.U, p1.U, p2.U, p3.U, t), CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t));
            }

            ctrlArc[i + 1] = _arc[_arc.Count - 1];
        }

        // The head starts with the whole body in the underground lead-in and finishes when the tail is back under.
        _startArc = System.Math.Min(l, ctrlArc[2]);
        _endArc = System.Math.Min(TotalArc, ctrlArc[backIndex] + l);
        _topArc = ctrlArc[topIndex];
        if (Move == WormMove.Rear)
        {
            _strikeArc = ctrlArc[strikeIndex];
            _tRise = System.Math.Max(0f, _topArc - _startArc) / HeadSpeed;
            _tHold = HoldSeconds;
            _tStrike = System.Math.Max(0f, _strikeArc - _topArc - HoldAdvance) / (HeadSpeed * StrikeSpeedFactor);
            StrikeTime = _tRise + _tHold + _tStrike;
            Duration = StrikeTime + System.Math.Max(0f, _endArc - _strikeArc) / HeadSpeed;
        }
        else
        {
            Duration = System.Math.Max(0f, _endArc - _startArc) / HeadSpeed;
        }
    }

    /// <summary>How far the head creeps on during the hold at the top of a Rear (blocks).</summary>
    private const float HoldAdvance = 2.5f;

    private void AddPoint(float u, float y)
    {
        var p = new Vector3f(Anchor.X + _dirX * u, Anchor.Y + y, Anchor.Z + _dirZ * u);
        if (_points.Count == 0)
        {
            _points.Add(p);
            _arc.Add(0f);
            return;
        }

        var q = _points[_points.Count - 1];
        float dx = p.X - q.X, dy = p.Y - q.Y, dz = p.Z - q.Z;
        _points.Add(p);
        _arc.Add(_arc[_arc.Count - 1] + (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Where along the curve (arc length) the head is <paramref name="t"/> seconds into the move.</summary>
    public float HeadArcAt(float t)
    {
        if (t <= 0f)
        {
            return _startArc;
        }

        if (Move != WormMove.Rear)
        {
            return System.Math.Min(_endArc, _startArc + t * HeadSpeed);
        }

        if (t < _tRise)
        {
            return _startArc + t * HeadSpeed;
        }

        t -= _tRise;
        if (t < _tHold)
        {
            return _topArc + HoldAdvance * (t / _tHold);
        }

        t -= _tHold;
        if (t < _tStrike)
        {
            return _topArc + HoldAdvance + t * HeadSpeed * StrikeSpeedFactor;
        }

        t -= _tStrike;
        return System.Math.Min(_endArc, _strikeArc + t * HeadSpeed);
    }

    /// <summary>The point at arc length <paramref name="s"/> (clamped to the curve).</summary>
    public Vector3f PointAt(float s)
    {
        if (_points.Count == 0)
        {
            return Anchor;
        }

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

    /// <summary>The head's position <paramref name="t"/> seconds into the move.</summary>
    public Vector3f HeadAt(float t) => PointAt(HeadArcAt(t));

    /// <summary>The body's segment centres at time <paramref name="t"/>: <paramref name="count"/> points from the head
    /// back to the tail, evenly spaced along the head's own track.</summary>
    public void SegmentCenters(float t, Vector3f[] into)
    {
        if (into is null || into.Length == 0)
        {
            return;
        }

        float head = HeadArcAt(t);
        float spacing = into.Length > 1 ? Length / (into.Length - 1) : 0f;
        for (int i = 0; i < into.Length; i++)
        {
            into[i] = PointAt(head - i * spacing);
        }
    }

    /// <summary>The spheres of the body that are above the surface at time <paramref name="t"/> (the hittable part):
    /// centre + radius, sampled every half girth along the body.</summary>
    public List<(Vector3f Center, float Radius)> ExposedSpheres(float t)
    {
        var list = new List<(Vector3f, float)>();
        float head = HeadArcAt(t);
        float step = Girth * 0.5f;
        float r = Girth * 0.5f;
        for (float s = 0f; s <= Length; s += step)
        {
            var c = PointAt(head - s);
            if (c.Y + r > Anchor.Y)
            {
                list.Add((c, r));
            }
        }

        return list;
    }

    /// <summary>True while any of the body is above the surface at <paramref name="t"/>.</summary>
    public bool Exposed(float t)
    {
        float head = HeadArcAt(t);
        float step = Girth;
        for (float s = 0f; s <= Length; s += step)
        {
            if (PointAt(head - s).Y + Girth * 0.5f > Anchor.Y)
            {
                return true;
            }
        }

        return false;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3614/#3615: ranges and approach points measured from a big hull's box instead of its centre. A giant wreck is up to 256
/// blocks a side, so its centre lies far inside the hull: a scan range or an autopilot target taken from the centre is wrong
/// by a hundred blocks (the ship would fly into the hull and never arrive). The server measures the same way
/// (<c>HullBoxDistance</c>, <c>ScanDistanceSq</c>). Boxes are axis-aligned (a station hull never turns), given by centre and
/// half size in the flight frame. Unity-free.
/// </summary>
public static class HullApproach
{
    /// <summary>The distance from <paramref name="point"/> to the box — 0 on or inside it.</summary>
    public static float BoxDistance(Vector3 centre, Vector3 half, Vector3 point)
    {
        Vector3 d = Vector3.Abs(point - centre) - Vector3.Abs(half);
        return Vector3.Max(d, Vector3.Zero).Length();
    }

    /// <summary>The point of the box nearest to <paramref name="point"/> (the point itself when inside).</summary>
    public static Vector3 Nearest(Vector3 centre, Vector3 half, Vector3 point)
    {
        Vector3 h = Vector3.Abs(half);
        return Vector3.Clamp(point, centre - h, centre + h);
    }

    /// <summary>
    /// Where a ship coming from <paramref name="from"/> stops before the hull: <paramref name="standOff"/> out from the box
    /// point nearest to it, straight away from the box. A ship already inside the box (in a hangar mouth, a breach) is there:
    /// its own position comes back.
    /// </summary>
    public static Vector3 StandOffPoint(Vector3 centre, Vector3 half, Vector3 from, float standOff)
    {
        Vector3 nearest = Nearest(centre, half, from);
        Vector3 outward = from - nearest;
        float length = outward.Length();
        if (length < 1e-4f)
        {
            return from; // inside (or on) the box
        }

        return nearest + (outward / length) * Math.Max(0f, standOff);
    }
}

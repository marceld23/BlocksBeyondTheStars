// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The person under the crosshair (#3088). E used to talk only to the NEAREST person, and only when no door, beam pad
    /// or station stood closer — so in a base full of doors a resident could not be talked to at all (Justus 2026-11-01).
    /// Looking at somebody now picks them: the aim ray against an upright body cylinder over each person's feet, for the
    /// people within talk reach of the player (<see cref="InteractReach.TalkReach"/>, the server's reach). Positions arrive
    /// canonical and are measured the short way round both seams, like every other pick (#2613).
    /// </summary>
    public static class NpcAim
    {
        /// <summary>The radius of the body the crosshair hits (a little wider than the drawn body, so the aim is forgiving).</summary>
        public const float BodyRadius = 0.45f;

        /// <summary>The height of that body over the feet.</summary>
        public const float BodyHeight = 1.9f;

        /// <summary>The person within talk reach of the player at (<paramref name="px"/>, <paramref name="py"/>,
        /// <paramref name="pz"/>) whose body the ray from the eye (<paramref name="ox"/>, <paramref name="oy"/>,
        /// <paramref name="oz"/>) along the unit direction (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>)
        /// enters first within <paramref name="maxDistance"/>, or null. <paramref name="distance"/> is where the ray enters
        /// the body (the caller compares it with the first solid block — a wall in front hides the person).</summary>
        public static NetNpc? Aimed(IReadOnlyList<NetNpc>? npcs, float ox, float oy, float oz, float dx, float dy, float dz,
            float maxDistance, float px, float py, float pz, int circumference, out float distance)
        {
            distance = float.PositiveInfinity;
            if (npcs == null)
            {
                return null;
            }

            NetNpc? best = null;
            double reachSq = InteractReach.TalkReach * (double)InteractReach.TalkReach;
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n == null || InteractReach.SeamDistanceSquared(px, py, pz, n.X, n.Y, n.Z, circumference) >= reachSq)
                {
                    continue; // the talk prompt's own reach rule: the edge itself is out
                }

                double rx = circumference > 0 ? WorldConstants.WrapDeltaX(n.X - ox, circumference) : n.X - ox;
                double rz = circumference > 0 ? WorldConstants.WrapDeltaZ(n.Z - oz, circumference) : n.Z - oz;
                float t = RayCylinder(rx, n.Y - oy, rz, dx, dy, dz, BodyRadius, BodyHeight);
                if (t <= maxDistance && t < distance)
                {
                    distance = t;
                    best = n;
                }
            }

            return best;
        }

        /// <summary>Where a ray from the origin along (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>)
        /// enters an upright cylinder whose base centre lies at (<paramref name="rx"/>, <paramref name="ry"/>,
        /// <paramref name="rz"/>) relative to the origin — in units of the direction's length; 0 when the origin is inside,
        /// <see cref="float.PositiveInfinity"/> when the ray misses or the body lies behind.</summary>
        public static float RayCylinder(double rx, double ry, double rz, float dx, float dy, float dz, float radius, float height)
        {
            double lo = 0d, hi = double.PositiveInfinity;
            if (!HorizontalSpan(rx, rz, dx, dz, radius, ref lo, ref hi) || !VerticalSpan(ry, dy, height, ref lo, ref hi))
            {
                return float.PositiveInfinity;
            }

            return (float)lo;
        }

        /// <summary>Narrows [lo, hi] to where the ray runs within <paramref name="radius"/> of the body's axis (seen from
        /// above); false when it never does.</summary>
        private static bool HorizontalSpan(double rx, double rz, float dx, float dz, float radius, ref double lo, ref double hi)
        {
            double a = (double)dx * dx + (double)dz * dz;
            double c = rx * rx + rz * rz - (double)radius * radius;
            if (a < 1e-12)
            {
                return c <= 0d; // straight up or down: inside the circle or never
            }

            double b = -2d * (dx * rx + dz * rz);
            double disc = b * b - 4d * a * c;
            if (disc < 0d)
            {
                return false;
            }

            double root = Math.Sqrt(disc);
            lo = Math.Max(lo, (-b - root) / (2d * a));
            hi = Math.Min(hi, (-b + root) / (2d * a));
            return lo <= hi;
        }

        /// <summary>Narrows [lo, hi] to where the ray runs between the feet and the top of the body; false when it never
        /// does.</summary>
        private static bool VerticalSpan(double ry, float dy, float height, ref double lo, ref double hi)
        {
            if (Math.Abs(dy) < 1e-12)
            {
                return ry <= 0d && ry + height >= 0d; // level: at the body's height or never
            }

            double t0 = ry / dy, t1 = (ry + height) / dy;
            lo = Math.Max(lo, Math.Min(t0, t1));
            hi = Math.Min(hi, Math.Max(t0, t1));
            return lo <= hi;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3095 ("the drill bit never spins"): which parts of a held drill model are its bit, and how fast the bit turns. The
    /// bit spins while the drill mines (every swing keeps it driven) and in the drill's equip flourish
    /// (<see cref="HeldFlourishKind.DrillSpinUp"/>); it spins up quickly and coasts down once it is no longer driven.
    /// <para>
    /// The bit is found from the model's boxes alone, so it works for every drill in the data, the kind's default model and a
    /// player's own tool look: the foremost box must sit on the drill's axis (the holder's z axis) and be about square across;
    /// the bit is then that box plus every other square box on the axis whose centre lies at or ahead of the foremost box's
    /// back face (a chuck, a collar, the beam's emitter ring). Nothing spins when an off-axis box reaches into the bit's
    /// length — a pickaxe's head, which is a drill by its tool kind but has no bit — or when the foremost box is off the axis.
    /// A box on the axis turns in place about its own z, which is the drill's axis, so nothing has to be re-parented.
    /// </para>
    /// </summary>
    public static class HeldDrillBit
    {
        /// <summary>The bit's top speed, degrees per second (three turns a second).</summary>
        public const float FullDegreesPerSecond = 1080f;

        /// <summary>Seconds from standing still to top speed.</summary>
        public const float SpinUpSeconds = 0.25f;

        /// <summary>Seconds from top speed to a stop once the bit is no longer driven.</summary>
        public const float CoastSeconds = 0.6f;

        /// <summary>How far off the axis a box's centre may sit and still count as on it (metres, model space).</summary>
        private const float AxisTolerance = 0.012f;

        /// <summary>How much a bit box's width and height may differ (fraction of the larger) — a bit is round, so square.</summary>
        private const float SquareTolerance = 0.2f;

        /// <summary>How far behind the foremost box's back face a box's centre may lie and still belong to the bit.</summary>
        private const float BitReach = 0.03f;

        /// <summary>
        /// The indices (into <paramref name="parts"/>) of the boxes that make the drill's bit, in order; empty when the model
        /// has no bit that could spin.
        /// </summary>
        public static List<int> SpinParts(IReadOnlyList<HeldItemShapes.Part> parts)
        {
            var bit = new List<int>();
            if (parts == null || parts.Count == 0)
            {
                return bit;
            }

            int front = 0;
            for (int i = 1; i < parts.Count; i++)
            {
                if (Front(parts[i]) > Front(parts[front]))
                {
                    front = i;
                }
            }

            if (!IsRoundOnAxis(parts[front]))
            {
                return bit;
            }

            float from = parts[front].Position.Z - (parts[front].Size.Z * 0.5f) - BitReach;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < parts.Count; i++)
            {
                if (IsRoundOnAxis(parts[i]) && parts[i].Position.Z >= from)
                {
                    bit.Add(i);
                    lo = Math.Min(lo, parts[i].Position.Z - (parts[i].Size.Z * 0.5f));
                    hi = Math.Max(hi, Front(parts[i]));
                }
            }

            // A box off the axis that reaches into the bit's length makes it a pick (or something else that must not turn).
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (bit.Contains(i) || IsOnAxis(p))
                {
                    continue;
                }

                float back = p.Position.Z - (p.Size.Z * 0.5f);
                if (Front(p) > lo && back < hi)
                {
                    bit.Clear();
                    return bit;
                }
            }

            return bit;
        }

        /// <summary>The bit's speed (0..1 of <see cref="FullDegreesPerSecond"/>) after <paramref name="dt"/> seconds: rising
        /// toward full while <paramref name="driven"/>, coasting toward a stop otherwise.</summary>
        public static float NextSpeed(float speed, bool driven, float dt)
        {
            if (dt <= 0f)
            {
                return Math.Max(0f, Math.Min(1f, speed));
            }

            float next = driven ? speed + (dt / SpinUpSeconds) : speed - (dt / CoastSeconds);
            return Math.Max(0f, Math.Min(1f, next));
        }

        private static float Front(HeldItemShapes.Part p) => p.Position.Z + (p.Size.Z * 0.5f);

        private static bool IsOnAxis(HeldItemShapes.Part p)
            => Math.Abs(p.Position.X) <= AxisTolerance && Math.Abs(p.Position.Y) <= AxisTolerance;

        private static bool IsRoundOnAxis(HeldItemShapes.Part p)
        {
            if (!IsOnAxis(p))
            {
                return false;
            }

            float wide = Math.Max(p.Size.X, p.Size.Y);
            return wide > 0f && Math.Abs(p.Size.X - p.Size.Y) <= wide * SquareTolerance;
        }
    }
}

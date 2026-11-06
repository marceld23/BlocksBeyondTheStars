// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The sampled stage of <see cref="TransitionShotPlan"/>: where the hull and the camera are at each moment and
    /// whether that moment is visible.</summary>
    public sealed partial class TransitionShotPlan
    {
        /// <summary>A touchdown candidate: its samples from the start while they stay visible (the cockpit takes over at the
        /// first blocked one, and never hands back).</summary>
        private static Outcome EvaluateTouchdown(TransitionShotInput input, float duration, Point spot)
        {
            var o = Outcome.None;
            if (!SpotUsable(input, spot))
            {
                return o;
            }

            int visible = 0;
            var prev = spot;
            for (; visible < SampleCount; visible++)
            {
                float t = TimeAt(visible, duration);
                var cam = TouchdownCamera(input, spot, t);
                var hull = new Point(input.CenterX, input.CenterY + TransitionFlightPath.TouchdownHeight(t, input.Drop), input.CenterZ);
                if (!SampleVisible(input, cam, prev, hull))
                {
                    break;
                }

                prev = cam;
            }

            if (visible == 0)
            {
                return o;
            }

            o.VisibleFrom = 0f;
            o.VisibleTo = TimeAt(visible - 1, duration);
            o.GlideClear = visible == SampleCount && PathClear(input.Path, prev, Eye(input));
            float dustBeat = TransitionFlightPath.TouchdownTimeAt(TransitionFlightPath.DustHeight, input.Drop);
            (o.OutsideFrom, o.OutsideTo) = TouchdownWindow(visible, duration, dustBeat);
            return o;
        }

        /// <summary>A lift-off candidate: its samples from the end back while they stay visible (the shot comes outside once
        /// and stays there up to the clouds).</summary>
        private static Outcome EvaluateLiftoff(TransitionShotInput input, float duration, Point spot)
        {
            var o = Outcome.None;
            if (!SpotUsable(input, spot))
            {
                return o;
            }

            int first = SampleCount;
            var next = spot;
            for (int i = SampleCount - 1; i >= 0; i--)
            {
                float t = TimeAt(i, duration);
                var cam = LiftoffCamera(input, spot, t);
                var pose = TransitionFlightPath.Liftoff(t);
                var hull = new Point(input.CenterX + pose.X, input.CenterY + pose.Y, input.CenterZ + pose.Z);
                if (!SampleVisible(input, cam, first == SampleCount ? cam : next, hull))
                {
                    break;
                }

                first = i;
                next = cam;
            }

            if (first == SampleCount)
            {
                return o;
            }

            o.VisibleFrom = TimeAt(first, duration);
            o.VisibleTo = duration;
            o.GlideClear = first == 0 && PathClear(input.Path, Eye(input), spot);
            (o.OutsideFrom, o.OutsideTo) = LiftoffWindow(first, duration, o.GlideClear);
            return o;
        }

        /// <summary>The touchdown camera: on its spot while the ship comes down, then a push a quarter of the way toward the
        /// hull while it settles (the director's <c>MoveShot</c>).</summary>
        private static Point TouchdownCamera(TransitionShotInput input, Point spot, float t)
        {
            float push = Clamp01((t - TransitionFlightPath.DescendSeconds) / TransitionFlightPath.SettleSeconds) * PushIn;
            return new Point(spot.X + (input.CenterX - spot.X) * push, spot.Y + (input.CenterY - spot.Y) * push,
                spot.Z + (input.CenterZ - spot.Z) * push);
        }

        /// <summary>The lift-off camera: on its spot, rising after the climbing hull by a quarter of its height (the
        /// director's <c>AimLiftoff</c>).</summary>
        private static Point LiftoffCamera(TransitionShotInput input, Point spot, float t)
        {
            float follow = Clamp01((t - TransitionFlightPath.ClimbAt) / 1.5f);
            float rise = Math.Max(0f, TransitionFlightPath.Liftoff(t).Y) * 0.25f * follow;
            return new Point(spot.X, spot.Y + rise, spot.Z);
        }

        /// <summary>One moment is visible when the camera's spot is clear, both lines of sight (to the hull's centre and to
        /// near its top) are clear, and so is the camera's way from the neighbouring sample.</summary>
        private static bool SampleVisible(TransitionShotInput input, Point cam, Point neighbour, Point hull)
        {
            if (InHullBox(input, cam) || !CinematicStageScan.SpotClear(input.Spot, cam.X, cam.Y, cam.Z))
            {
                return false;
            }

            var top = new Point(hull.X, hull.Y + input.ExtentY * 0.8f, hull.Z);
            return PathClear(input.Path, neighbour, cam)
                   && PathClear(input.Path, cam, hull)
                   && PathClear(input.Path, cam, top);
        }

        private static bool SpotUsable(TransitionShotInput input, Point spot)
            => !InHullBox(input, spot) && CinematicStageScan.SpotClear(input.Spot, spot.X, spot.Y, spot.Z);

        private static bool InHullBox(TransitionShotInput input, Point p)
            => Math.Abs(p.X - input.CenterX) < input.ExtentX + BoxMargin
               && Math.Abs(p.Y - input.CenterY) < input.ExtentY + BoxMargin
               && Math.Abs(p.Z - input.CenterZ) < input.ExtentZ + BoxMargin;

        private static bool PathClear(CinematicStageScan.ClearSampler clear, Point from, Point to)
            => CinematicStageScan.PathClear(clear, from.X, from.Y, from.Z, to.X, to.Y, to.Z);

        private static Point Eye(TransitionShotInput input) => new Point(input.EyeX, input.EyeY, input.EyeZ);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private readonly struct Point
        {
            public Point(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }
        }

        /// <summary>What one candidate spot sees of the stage.</summary>
        private struct Outcome
        {
            public float VisibleFrom;
            public float VisibleTo;
            public float OutsideFrom;
            public float OutsideTo;
            public bool GlideClear;

            public static Outcome None => new Outcome { VisibleTo = -1f };
        }
    }
}

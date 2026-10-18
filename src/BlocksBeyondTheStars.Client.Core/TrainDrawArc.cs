// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The arc a monorail train is drawn at between the server's updates (#2113, #2741): the drawn arc runs on at the
    /// train's own speed and is eased toward the server's last arc (run on by the time since it arrived), both through
    /// <see cref="RailRules.Advance"/> — so the drawing bounces off an open line's ends exactly like the server, instead of
    /// running past the end until the next update pulls it back. The drawn direction is the drawing's own; it takes the
    /// server's when the two disagree for longer than a bounce near the same end explains (a halted train the server
    /// turned round at a stop, a fresh train). Unity-free so the headless tests can drive it at any frame time.
    /// </summary>
    public static class TrainDrawArc
    {
        /// <summary>How fast the drawn arc closes the gap to the server's (per second).</summary>
        public const float EaseRate = 4f;

        /// <summary>The longest the server's arc is run on (seconds): past this the train is waited for, not guessed.</summary>
        public const float MaxPredictSeconds = 1f;

        /// <summary>One frame: the new drawn arc and direction. <paramref name="hasArc"/> false (the train's first frame)
        /// takes the server's predicted arc as it is. <paramref name="speed"/> is 0 for a halted train.
        /// <paramref name="span"/> is <see cref="RailRules.TrainSpan"/> of the train's wagons.</summary>
        public static (float Arc, int Direction) Step(
            bool hasArc, float drawArc, int drawDirection,
            float serverArc, int serverDirection, int facing, float speed, float age,
            float dt, float total, float span, bool closed)
        {
            if (!IsFinite(speed) || speed < 0f)
            {
                speed = 0f;
            }

            if (!IsFinite(dt) || dt < 0f)
            {
                dt = 0f;
            }

            if (!IsFinite(age) || age < 0f)
            {
                age = 0f;
            }

            var (target, targetDir) = RailRules.Advance(serverArc, serverDirection, facing, speed * Math.Min(age, MaxPredictSeconds), total, span, closed);
            if (!hasArc || !IsFinite(drawArc) || total <= 0f)
            {
                return (target, targetDir);
            }

            var (run, runDir) = RailRules.Advance(drawArc, drawDirection, facing, speed * dt, total, span, closed);
            float delta = target - run;
            if (closed)
            {
                // A loop: take the short way round.
                delta = ((delta % total) + total) % total;
                if (delta > total * 0.5f)
                {
                    delta -= total;
                }
            }

            float eased = run + (delta * Math.Min(1f, dt * EaseRate));
            float arc;
            if (closed)
            {
                arc = eased % total;
                if (arc < 0f)
                {
                    arc += total;
                }
            }
            else
            {
                var (lo, hi) = RailRules.CabRange(total, span, facing);
                arc = Math.Clamp(eased, lo, hi);
            }

            int dir = runDir;
            if (runDir != targetDir && !BouncingAtTheSameEnd(run, runDir, target, targetDir, speed, total, span, facing, closed))
            {
                dir = targetDir;
            }

            return (arc, dir);
        }

        /// <summary>Whether the drawing and the server disagree on the direction only because one of them already bounced off
        /// an end the other is about to reach (both are near that end).</summary>
        private static bool BouncingAtTheSameEnd(float run, int runDir, float target, int targetDir, float speed, float total, float span, int facing, bool closed)
        {
            if (closed || speed <= 0f)
            {
                return false; // a loop never turns round; a halted train has no bounce coming — take the server's
            }

            var (lo, hi) = RailRules.CabRange(total, span, facing);
            float near = (speed * (MaxPredictSeconds + 0.5f)) + 1f;
            float toward = runDir > 0 ? hi : lo; // the end the drawing runs to: the server bounced there already
            float behind = targetDir > 0 ? hi : lo; // the end the server runs to: the drawing bounced there already
            return Math.Abs(toward - run) + Math.Abs(toward - target) <= near
                || Math.Abs(behind - run) + Math.Abs(behind - target) <= near;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}

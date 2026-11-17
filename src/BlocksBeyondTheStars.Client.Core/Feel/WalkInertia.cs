// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// The walk's own start and stop curve. The raw movement input (keys are 0 or ±1, a stick is analogue) is eased into
    /// the input the controller walks with: a short run-up, a short stop, and less authority in the air so a jump keeps
    /// its momentum. It replaces the input manager's generic axis smoothing (a third of a second both ways), which was
    /// never tuned for the walk: the body answered late and slid on after the key was let go — awkward when building on
    /// an edge. Client-only; the speeds themselves are untouched.
    /// </summary>
    public sealed class WalkInertia
    {
        /// <summary>Seconds from standing to full input on the ground.</summary>
        public const float GroundStartSeconds = 0.08f;

        /// <summary>Seconds from full input to standing on the ground — short enough to stop on the block you meant.</summary>
        public const float GroundStopSeconds = 0.10f;

        /// <summary>Seconds from nothing to full input in the air (or the water): steering works, but slower.</summary>
        public const float AirStartSeconds = 0.16f;

        /// <summary>Seconds a let-go input takes to fade in the air: a jump carries on.</summary>
        public const float AirStopSeconds = 0.30f;

        /// <summary>The eased strafe input, −1..1.</summary>
        public float X { get; private set; }

        /// <summary>The eased forward input, −1..1.</summary>
        public float Y { get; private set; }

        /// <summary>
        /// One frame: eases toward the raw input (<paramref name="rawX"/>, <paramref name="rawY"/>). <paramref name="firm"/>:
        /// the feet (or the hands) have a hold — standing, climbing, on a ladder; false in the air and in water.
        /// </summary>
        public void Step(float rawX, float rawY, float dt, bool firm)
        {
            if (dt <= 0f)
            {
                return;
            }

            rawX = Clamp(rawX);
            rawY = Clamp(rawY);
            X = Ease(X, rawX, dt, firm);
            Y = Ease(Y, rawY, dt, firm);
        }

        /// <summary>Stands still at once (a teleport, a respawn, a menu that took the input).</summary>
        public void Reset()
        {
            X = 0f;
            Y = 0f;
        }

        private static float Ease(float current, float target, float dt, bool firm)
        {
            // Letting go, or steering against the current motion, is a stop; pushing further the same way is a start.
            bool stopping = Math.Abs(target) < Math.Abs(current) || (target * current) < 0f;
            float seconds = firm
                ? (stopping ? GroundStopSeconds : GroundStartSeconds)
                : (stopping ? AirStopSeconds : AirStartSeconds);
            float step = dt / seconds;
            float delta = target - current;
            return Math.Abs(delta) <= step ? target : current + (Math.Sign(delta) * step);
        }

        private static float Clamp(float v) => v < -1f ? -1f : v > 1f ? 1f : v;
    }
}

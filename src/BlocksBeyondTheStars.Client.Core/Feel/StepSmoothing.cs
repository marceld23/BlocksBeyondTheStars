// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// Takes the jolt out of the auto-step. The character controller lifts the body onto a slab or a stair tread within
    /// one frame; the eye used to jump with it. The eye now stays behind by the height of the step
    /// (<see cref="Offset"/>, negative) and catches up within about a tenth of a second. Only a real step counts
    /// (<see cref="IsStep"/>): a lift, a train or a slope carries the body up slowly and is followed rigidly.
    /// </summary>
    public sealed class StepSmoothing
    {
        /// <summary>A rise this small in one frame is no step.</summary>
        public const float MinimumRise = 0.1f;

        /// <summary>A step lifts the body faster than this (m/s) — anything slower is a platform carrying it.</summary>
        public const float MinimumRiseSpeed = 6f;

        /// <summary>The eye never hangs further below its place than this.</summary>
        public const float MaximumLag = 0.7f;

        /// <summary>How fast the eye catches up (per second, exponential).</summary>
        public const float CatchUpRate = 14f;

        /// <summary>How far the eye sits below its place right now (0 or negative, metres).</summary>
        public float Offset { get; private set; }

        /// <summary>Whether a rise of <paramref name="rise"/> metres within <paramref name="dt"/> seconds is an auto-step.</summary>
        public static bool IsStep(float rise, float dt) => rise > MinimumRise && dt > 0f && rise / dt > MinimumRiseSpeed;

        /// <summary>The body stepped up by <paramref name="rise"/> metres this frame: the eye stays where it was.</summary>
        public void Absorb(float rise)
        {
            if (rise > 0f)
            {
                Offset = Math.Max(-MaximumLag, Offset - rise);
            }
        }

        /// <summary>One frame of catching up.</summary>
        public void Tick(float dt)
        {
            if (Offset >= 0f || dt <= 0f)
            {
                Offset = Math.Min(0f, Offset);
                return;
            }

            Offset *= (float)Math.Exp(-CatchUpRate * dt);
            if (Offset > -0.002f)
            {
                Offset = 0f;
            }
        }

        /// <summary>Snaps the eye to its place (a teleport, a respawn).</summary>
        public void Reset() => Offset = 0f;
    }
}

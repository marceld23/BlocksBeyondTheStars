// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// The ship at the helm has mass — "arcade", not Newton. The thrust builds up over <see cref="SpoolSeconds"/>, a
    /// let-go stick lets the ship coast for <see cref="CoastSeconds"/> and then it stands (no drifting on forever, no
    /// counter-thrust needed). The hull leans into a turn (<see cref="BankTarget"/>) — shown only, the heading is
    /// unchanged — and the chase camera trails a little (<see cref="CameraFollowRate"/>). Client-only presentation: the
    /// position the client reports is the same kind of position as before.
    /// </summary>
    public static class FlightFeel
    {
        /// <summary>Seconds from standing to full speed.</summary>
        public const float SpoolSeconds = 0.4f;

        /// <summary>Seconds from full speed to standing once the stick is let go.</summary>
        public const float CoastSeconds = 0.8f;

        /// <summary>The steepest lean into a turn, degrees.</summary>
        public const float MaxBankDegrees = 25f;

        /// <summary>Yaw rate (degrees per second) that leans the hull all the way.</summary>
        public const float FullBankYawRate = 110f;

        /// <summary>How fast the lean follows (per second, exponential).</summary>
        public const float BankFollowRate = 5f;

        /// <summary>How fast the chase camera follows the ship (per second, exponential).</summary>
        public const float CameraFollowRate = 9f;

        /// <summary>The share of the lean the cockpit view takes (the horizon tilts a little, not all the way).</summary>
        public const float CockpitBankShare = 0.3f;

        /// <summary>
        /// How much the speed may change this frame, as a share of the top speed: toward a wanted speed
        /// (<paramref name="thrusting"/>) at the spool rate, toward a stop at the coast rate.
        /// </summary>
        public static float SpeedStep(float dt, bool thrusting)
            => dt <= 0f ? 0f : dt / (thrusting ? SpoolSeconds : CoastSeconds);

        /// <summary>The lean (degrees, positive = right wing down) a yaw rate asks for: into the turn, capped.</summary>
        public static float BankTarget(float yawRateDegPerSec)
        {
            float t = yawRateDegPerSec / FullBankYawRate;
            t = t < -1f ? -1f : t > 1f ? 1f : t;
            return t * MaxBankDegrees;
        }

        /// <summary>Exponential approach: the share of the remaining distance covered in <paramref name="dt"/> at <paramref name="rate"/>.</summary>
        public static float Follow(float rate, float dt) => dt <= 0f || rate <= 0f ? 0f : 1f - (float)Math.Exp(-rate * dt);
    }
}

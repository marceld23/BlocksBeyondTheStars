// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The size rule of the staged prologue orbit (#2461, B19). The shot — a wide entry ring, a settle orbit at radius
    /// 16 and a push-in to radius 8 — was tuned on the starter ship. A self-built ship can be three times as long, and
    /// the same radii put the camera inside its hull. The whole shot now scales with the ship's footprint, so a bigger
    /// ship gets the same framing from further out; a ship no bigger than the starter keeps the tuned numbers exactly.
    /// Unity-free so the rule is covered by plain .NET tests; <c>PrologueCinematic</c> applies it.
    /// </summary>
    public static class PrologueOrbit
    {
        /// <summary>The settle-orbit radius the shot was tuned on (the starter ship).</summary>
        public const float TunedSettleRadius = 16f;

        /// <summary>Blocks of air the settle orbit keeps between the camera and the hull's farthest corner.</summary>
        public const float HullClearance = 8f;

        /// <summary>The factor every radius and height of the shot is multiplied by: 1 for a ship whose half diagonal
        /// fits inside the tuned orbit with <see cref="HullClearance"/> to spare (or when the size is unknown), else
        /// the factor that restores that clearance.</summary>
        public static float Scale(int width, int length)
        {
            if (width <= 0 || length <= 0)
            {
                return 1f;
            }

            float halfDiagonal = 0.5f * (float)Math.Sqrt((double)width * width + (double)length * length);
            return Math.Max(1f, (halfDiagonal + HullClearance) / TunedSettleRadius);
        }
    }

    /// <summary>
    /// When the staged prologue tries to set its shot again (#2461, B19). It used to try exactly once, on the first
    /// prologue page — and on a fresh world (WebGL above all) the orbit ring around the ship had often not streamed in
    /// yet: unloaded chunks count as blocked, so the scan failed and the whole prologue fell back to "dim + panel".
    /// Now a failed attempt is retried every <see cref="RetryInterval"/> seconds until <see cref="GiveUpAfter"/> seconds
    /// after the first one; after that (or after a success) it stays as it is, and the existing fallback keeps the text
    /// readable. Unity-free so the schedule is covered by plain .NET tests; <c>VegaPanel</c> drives it.
    /// </summary>
    public sealed class StagingRetry
    {
        /// <summary>Seconds between two attempts while the ring streams in.</summary>
        public const float RetryInterval = 0.5f;

        /// <summary>Seconds after the first attempt when the staging gives up for this prologue run.</summary>
        public const float GiveUpAfter = 4f;

        private float _firstAt = -1f;
        private float _nextAt;
        private bool _succeeded;

        /// <summary>True once an attempt worked — nothing more is due.</summary>
        public bool Succeeded => _succeeded;

        /// <summary>A new prologue run: no attempt made yet, the first one is due at once.</summary>
        public void Reset()
        {
            _firstAt = -1f;
            _nextAt = 0f;
            _succeeded = false;
        }

        /// <summary>True when the window for attempts has closed without a success.</summary>
        public bool GaveUp(float now) => !_succeeded && _firstAt >= 0f && now - _firstAt > GiveUpAfter;

        /// <summary>Whether an attempt should be made at <paramref name="now"/>.</summary>
        public bool Due(float now)
        {
            if (_succeeded)
            {
                return false;
            }

            if (_firstAt < 0f)
            {
                return true;
            }

            return !GaveUp(now) && now >= _nextAt;
        }

        /// <summary>Records the outcome of an attempt made at <paramref name="now"/>.</summary>
        public void Record(float now, bool success)
        {
            if (_firstAt < 0f)
            {
                _firstAt = now;
            }

            _succeeded |= success;
            _nextAt = now + RetryInterval;
        }
    }
}

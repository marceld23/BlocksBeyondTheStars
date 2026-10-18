// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The gamepad's two rumble motors (#2746): the heavy low-frequency one (left) and the light high-frequency one
    /// (right). Every effect adds a <see cref="Pulse"/> — a strength per motor that fades linearly to zero over its time —
    /// and the motors run at the strongest pulse still going (max, not sum: ten bullets at once must not add up to a
    /// shaking that never stops). Levels are clamped to 0..1, a non-finite input is ignored, and a frame in which rumble
    /// is not allowed (setting off, a keyboard player, the window in the background, a menu, death) returns zero and
    /// drops every pulse, so nothing buzzes on when it is allowed again. Unity-free so the headless tests can drive it at
    /// any frame time; <c>PadRumble</c> sends the result to the motors.
    /// </summary>
    public sealed class RumbleMixer
    {
        /// <summary>The most pulses kept at once; a new one beyond it replaces the weakest.</summary>
        public const int MaxPulses = 16;

        /// <summary>Trauma below this does not rumble (#2746: the continuous heat shimmer of a hot hull adds ~0.02 a frame
        /// and must not make the pad buzz).</summary>
        public const float MinTrauma = 0.05f;

        /// <summary>The longest a pulse may last, seconds.</summary>
        public const float MaxSeconds = 2f;

        private readonly ActivePulse[] _pulses = new ActivePulse[MaxPulses];
        private int _count;

        private struct ActivePulse
        {
            public float Low;
            public float High;
            public float Duration;
            public float Remaining;
        }

        /// <summary>Whether any pulse is still running.</summary>
        public bool Active => _count > 0;

        /// <summary>Adds a pulse: <paramref name="low"/> and <paramref name="high"/> (0..1, clamped) fade linearly to zero
        /// over <paramref name="seconds"/> (clamped to <see cref="MaxSeconds"/>). A non-finite value, a pulse of no
        /// strength or no time is ignored.</summary>
        public void Pulse(float low, float high, float seconds)
        {
            if (!IsFinite(low) || !IsFinite(high) || !IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            low = Clamp01(low);
            high = Clamp01(high);
            if (low <= 0f && high <= 0f)
            {
                return;
            }

            var pulse = new ActivePulse { Low = low, High = high, Duration = Math.Min(seconds, MaxSeconds) };
            pulse.Remaining = pulse.Duration;
            if (_count < MaxPulses)
            {
                _pulses[_count++] = pulse;
                return;
            }

            // Full: the new pulse replaces the weakest one now running, if it is stronger.
            int weakest = 0;
            float weakestLevel = float.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                float level = Math.Max(LevelOf(_pulses[i].Low, _pulses[i]), LevelOf(_pulses[i].High, _pulses[i]));
                if (level < weakestLevel)
                {
                    weakestLevel = level;
                    weakest = i;
                }
            }

            if (Math.Max(low, high) > weakestLevel)
            {
                _pulses[weakest] = pulse;
            }
        }

        /// <summary>A shake of the camera as a pulse (#2746): explosions, ship hits, hyperspace, the hit you take. Trauma
        /// under <see cref="MinTrauma"/> does nothing. Returns whether it rumbled.</summary>
        public bool PulseTrauma(float trauma)
        {
            if (!IsFinite(trauma) || trauma < MinTrauma)
            {
                return false;
            }

            float t = Clamp01(trauma);
            Pulse(t, t * 0.6f, 0.15f + (0.35f * t));
            return true;
        }

        /// <summary>Advances every pulse by <paramref name="dt"/> seconds and returns the motor levels (low, high) to send
        /// now. Not <paramref name="allowed"/>: zero, and every pulse is dropped. A non-finite or negative step advances
        /// nothing.</summary>
        public (float Low, float High) Step(float dt, bool allowed)
        {
            if (!allowed)
            {
                Clear();
                return (0f, 0f);
            }

            if (!IsFinite(dt) || dt < 0f)
            {
                dt = 0f;
            }

            float low = 0f, high = 0f;
            int kept = 0;
            for (int i = 0; i < _count; i++)
            {
                var p = _pulses[i];
                p.Remaining -= dt;
                if (p.Remaining <= 0f)
                {
                    continue; // faded out
                }

                low = Math.Max(low, LevelOf(p.Low, p));
                high = Math.Max(high, LevelOf(p.High, p));
                _pulses[kept++] = p;
            }

            _count = kept;
            return (low, high);
        }

        /// <summary>Drops every pulse at once (the motors stop on the next step).</summary>
        public void Clear()
        {
            _count = 0;
        }

        private static float LevelOf(float strength, ActivePulse p) => strength * (p.Remaining / p.Duration);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}

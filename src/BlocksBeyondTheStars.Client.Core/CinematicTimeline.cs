// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free timing backbone for the scripted cinematics (intro, staged prologue): a sequence of
    /// legs with fixed durations, evaluated by wall-time into (leg index, 0..1 progress), plus the easing
    /// and fade-window helpers the camera/caption animation shares. Lives here (not in the MonoBehaviours)
    /// so the sequencing rules are covered by plain .NET tests.
    /// </summary>
    public sealed class CinematicTimeline
    {
        private readonly float[] _durations;
        private readonly float[] _starts; // absolute start time of each leg

        public CinematicTimeline(params float[] legDurations)
        {
            if (legDurations == null || legDurations.Length == 0)
            {
                throw new ArgumentException("A timeline needs at least one leg.", nameof(legDurations));
            }

            foreach (float d in legDurations)
            {
                if (d <= 0f)
                {
                    throw new ArgumentException("Leg durations must be positive.", nameof(legDurations));
                }
            }

            _durations = (float[])legDurations.Clone();
            _starts = new float[_durations.Length];
            float t = 0f;
            for (int i = 0; i < _durations.Length; i++)
            {
                _starts[i] = t;
                t += _durations[i];
            }

            Total = t;
        }

        /// <summary>Sum of all leg durations.</summary>
        public float Total { get; }

        public int LegCount => _durations.Length;

        /// <summary>Absolute start time of a leg (leg 0 starts at 0).</summary>
        public float StartOf(int leg) => _starts[leg];

        /// <summary>
        /// Maps an absolute time to the leg it falls into and the 0..1 progress within that leg.
        /// Clamped: t &lt; 0 → (0, 0); t ≥ Total → (last leg, 1).
        /// </summary>
        public (int Leg, float Progress) At(float t)
        {
            if (t <= 0f)
            {
                return (0, 0f);
            }

            if (t >= Total)
            {
                return (_durations.Length - 1, 1f);
            }

            for (int i = _durations.Length - 1; i >= 0; i--)
            {
                if (t >= _starts[i])
                {
                    return (i, (t - _starts[i]) / _durations[i]);
                }
            }

            return (0, 0f); // unreachable (t > 0 always lands in a leg), kept for the compiler
        }

        /// <summary>True once the timeline has run through completely.</summary>
        public bool Done(float t) => t >= Total;

        /// <summary>Smoothstep ease-in-out on a clamped 0..1 input.</summary>
        public static float EaseInOut(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>Quadratic ease-out on a clamped 0..1 input (the splash screens' settle curve).</summary>
        public static float EaseOut(float x)
        {
            x = Clamp01(x);
            return 1f - (1f - x) * (1f - x);
        }

        /// <summary>
        /// Alpha of a hold-with-fades window: 0 before <paramref name="start"/>, fades in over
        /// <paramref name="fade"/> seconds, holds at 1, fades back out to 0 at <paramref name="end"/>.
        /// Degenerate windows (end ≤ start) stay fully transparent.
        /// </summary>
        public static float FadeWindow(float t, float start, float end, float fade)
        {
            if (end <= start)
            {
                return 0f;
            }

            fade = Math.Min(Math.Max(fade, 0.0001f), (end - start) * 0.5f);
            float a = Clamp01((t - start) / fade);
            float b = Clamp01((end - t) / fade);
            return Math.Min(a, b);
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The surface cloud deck's motion and eased look (#3202), Unity-free so the headless tests pin it. Every cloud keeps
    /// its own azimuth and <see cref="Drift"/> moves it by one world step at the current wind — the deck used to place each
    /// cloud at <c>absolute world time × wind</c>, so any change of the wind re-scaled the whole accumulated offset and the
    /// deck jumped (after an hour of play a rain → storm flip was eleven full turns, a random reshuffle). The look (cover,
    /// darkening, wind, storm towers, cirrus) eases toward the weather's <see cref="WeatherLook.Sky"/> target over
    /// <see cref="EaseSeconds"/> instead of switching in one frame, and single clouds fade in and out with the cover
    /// (<see cref="Presence"/>) instead of popping. <see cref="Snap"/> skips the ease: when the deck re-appears after being
    /// hidden and for the capture tools, whose first frame must already be settled.
    /// </summary>
    public sealed class CloudDeck
    {
        /// <summary>Radians per second a cloud of drift 1 travels at wind scale 1 (the deck's historical speed).</summary>
        public const float DriftRate = 0.012f;

        /// <summary>Time constant of the look's ease toward a new weather, in seconds.</summary>
        public const float EaseSeconds = 10f;

        /// <summary>The ease never crawls slower than this (units per second), so it lands on the target in finite time.</summary>
        public const float MinEaseRate = 0.02f;

        private const float TwoPi = (float)(Math.PI * 2.0);

        private readonly float[] _angle;
        private readonly float[] _drift;
        private bool _hasLook;

        /// <param name="startAngles">Each cloud's azimuth at start (radians).</param>
        /// <param name="drift">Each cloud's wind multiplier (near clouds drift faster).</param>
        public CloudDeck(float[] startAngles, float[] drift)
        {
            if (startAngles is null)
            {
                throw new ArgumentNullException(nameof(startAngles));
            }

            if (drift is null || drift.Length != startAngles.Length)
            {
                throw new ArgumentException("one drift per cloud", nameof(drift));
            }

            _angle = new float[startAngles.Length];
            _drift = (float[])drift.Clone();
            for (int i = 0; i < _angle.Length; i++)
            {
                _angle[i] = Wrap(startAngles[i]);
            }
        }

        /// <summary>How many clouds the deck holds.</summary>
        public int Count => _angle.Length;

        /// <summary>The current, eased look (valid after the first <see cref="EaseToward"/> or <see cref="Snap"/>).</summary>
        public CloudStyle Look { get; private set; }

        /// <summary>True once a look has been set — before that the first ease snaps.</summary>
        public bool HasLook => _hasLook;

        /// <summary>Cloud <paramref name="index"/>'s azimuth around the camera, in radians (0..2π).</summary>
        public float Angle(int index) => _angle[index];

        /// <summary>The deck's target for a weather state: <see cref="WeatherLook.Sky"/> with the cover the sky shows (the
        /// larger of the planet's base cover and the weather's, <see cref="WeatherLook.Cover"/>).</summary>
        public static CloudStyle Target(string? state, float cloudDensity)
        {
            var style = WeatherLook.Sky(state, cloudDensity);
            return new CloudStyle(WeatherLook.Cover(state, cloudDensity), style.Darken, style.WindScale, style.StormTall, style.CirrusFade);
        }

        /// <summary>Moves every cloud by one world step (<paramref name="worldDt"/> = 0 while the world is held, so the deck
        /// parks) at <paramref name="windScale"/>. A change of the wind only changes the speed from here on — never where
        /// the clouds already are.</summary>
        public void Drift(float worldDt, float windScale)
        {
            if (!(worldDt > 0f))
            {
                return;
            }

            float step = worldDt * DriftRate * windScale;
            for (int i = 0; i < _angle.Length; i++)
            {
                _angle[i] = Wrap(_angle[i] + step * _drift[i]);
            }
        }

        /// <summary>Eases the look toward <paramref name="target"/> by one step of <paramref name="dt"/> seconds (the very
        /// first call snaps — there is nothing to ease from).</summary>
        public void EaseToward(in CloudStyle target, float dt)
        {
            if (!_hasLook)
            {
                Snap(target);
                return;
            }

            if (!(dt > 0f))
            {
                return;
            }

            float k = 1f - MathF.Exp(-dt / EaseSeconds);
            var look = Look;
            Look = new CloudStyle(
                Step(look.Cover, target.Cover, k, dt),
                Step(look.Darken, target.Darken, k, dt),
                Step(look.WindScale, target.WindScale, k, dt),
                Step(look.StormTall, target.StormTall, k, dt),
                Step(look.CirrusFade, target.CirrusFade, k, dt));
        }

        /// <summary>Jumps the look straight to <paramref name="target"/>.</summary>
        public void Snap(in CloudStyle target)
        {
            Look = target;
            _hasLook = true;
        }

        /// <summary>How much of cloud <paramref name="index"/> shows (0..1) at a <paramref name="cover"/> of 0..1: cloud i
        /// fades in while the cover crosses its band [i/max, (i+1)/max], so a cover change shows one cloud thickening or
        /// thinning at a time instead of whole clouds popping in and out.</summary>
        public static float Presence(int index, int maxClouds, float cover)
            => Clamp01((Clamp01(cover) * maxClouds) - index);

        private static float Step(float current, float target, float k, float dt)
        {
            float delta = target - current;
            float size = MathF.Abs(delta);
            float step = MathF.Max(size * k, MinEaseRate * dt);
            return size <= step ? target : current + (MathF.Sign(delta) * step);
        }

        private static float Wrap(float angle)
        {
            float a = angle % TwoPi;
            return a < 0f ? a + TwoPi : a;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}

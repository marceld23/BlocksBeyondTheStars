// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The camera's weapon kick (#2152): an upward pitch offset in degrees that a shot or a swing pushes and a damped
    /// spring pulls back to rest. Unity-free so the headless tests can drive it at any frame time (#2562).
    /// <para>The spring used to be integrated with semi-implicit Euler (<c>v += (−220·x − 24·v)·dt; x += v·dt</c>).
    /// That scheme is only stable while 220·dt² + 48·dt &lt; 4, i.e. for frames shorter than ~0.064 s (about 15 FPS).
    /// One slow frame after a shot — a hitch while chunks stream in on a weak laptop; Unity lets a frame last up to
    /// 0.333 s — made the kick flip sign and grow every frame, and the view wobbled up and down without end.</para>
    /// <para>Now every step is the EXACT solution of the damped oscillator over the frame time (closed form), so a frame
    /// of 1/144 s or of 1/3 s lands on the same curve and the offset can only fade. The constants are the continuous
    /// system the old Euler step amounted to at 60 FPS (its eigenvalues λ mapped back through ln λ / dt), so at 60 FPS
    /// the kick feels exactly as before: about 0.32° of lift per degree of kick, peaking after ~60 ms, settled within
    /// ~0.3 s, no visible overshoot.</para>
    /// <para>Offset and velocity are clamped to sane bounds, and a non-finite input or state resets the spring
    /// instead of poisoning the camera.</para>
    /// </summary>
    public sealed class CameraKickSpring
    {
        /// <summary>How fast the kick's envelope decays, 1/s (half the damping coefficient, ≈ 30.6 / 2).</summary>
        public const double DecayRate = 15.324768712979726;

        /// <summary>The damped angular frequency, rad/s (stiffness ≈ 282.8, damping ratio ≈ 0.91).</summary>
        public const double DampedFrequency = 6.924156209609051;

        /// <summary>Upward velocity (degrees per second) one degree of <see cref="Kick"/> adds — the old 18 °/s per
        /// degree, scaled so the curve matches the old 60 FPS samples.</summary>
        public const float ImpulsePerDegree = 13.973735878260308f;

        /// <summary>The largest single kick accepted, degrees (the guns use 0.4–2.2).</summary>
        public const float MaxKickDegrees = 10f;

        /// <summary>The fastest the kick may move, degrees per second — rapid fire stacks kicks up to this
        /// (a peak of about 14°).</summary>
        public const float MaxVelocity = 600f;

        /// <summary>The largest offset the kick may reach, degrees.</summary>
        public const float MaxOffset = 15f;

        /// <summary>Below this the spring counts as at rest and snaps to exactly zero (no endless denormal tail).</summary>
        private const float RestOffset = 1e-5f;
        private const float RestVelocity = 1e-4f;

        /// <summary>The current upward kick, degrees.</summary>
        public float Offset { get; private set; }

        /// <summary>The current kick velocity, degrees per second.</summary>
        public float Velocity { get; private set; }

        /// <summary>True while the spring is fully at rest.</summary>
        public bool AtRest => Offset == 0f && Velocity == 0f;

        /// <summary>Pushes the view up by a kick of <paramref name="degrees"/> (clamped to ±<see cref="MaxKickDegrees"/>).
        /// A non-finite kick resets the spring.</summary>
        public void Kick(float degrees)
        {
            if (!IsFinite(degrees))
            {
                Reset();
                return;
            }

            float kick = Math.Clamp(degrees, -MaxKickDegrees, MaxKickDegrees);
            Velocity = Math.Clamp(Velocity + (kick * ImpulsePerDegree), -MaxVelocity, MaxVelocity);
        }

        /// <summary>Advances the spring by <paramref name="dt"/> seconds — exactly, for any frame time — and returns the
        /// new <see cref="Offset"/>. A zero or negative step changes nothing; a non-finite one resets the spring.</summary>
        public float Step(float dt)
        {
            if (!IsFinite(dt))
            {
                Reset();
                return 0f;
            }

            if (dt <= 0f || AtRest)
            {
                return Offset;
            }

            // Under-damped oscillator x'' = −k·x − 2a·x', with k = a² + ω²: the closed-form state after dt.
            const double a = DecayRate;
            const double w = DampedFrequency;
            const double k = (a * a) + (w * w);
            double x = Offset;
            double v = Velocity;
            double decay = Math.Exp(-a * dt);
            double cos = Math.Cos(w * dt);
            double sin = Math.Sin(w * dt);
            double nextX = decay * ((x * cos) + ((v + (a * x)) / w * sin));
            double nextV = decay * ((v * cos) - (((a * v) + (k * x)) / w * sin));
            if (!IsFinite(nextX) || !IsFinite(nextV))
            {
                Reset();
                return 0f;
            }

            Offset = (float)Math.Clamp(nextX, -MaxOffset, MaxOffset);
            Velocity = (float)Math.Clamp(nextV, -MaxVelocity, MaxVelocity);
            if (Math.Abs(Offset) < RestOffset && Math.Abs(Velocity) < RestVelocity)
            {
                Offset = 0f;
                Velocity = 0f;
            }

            return Offset;
        }

        /// <summary>Puts the spring at rest (world teardown, cinematics).</summary>
        public void Reset()
        {
            Offset = 0f;
            Velocity = 0f;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

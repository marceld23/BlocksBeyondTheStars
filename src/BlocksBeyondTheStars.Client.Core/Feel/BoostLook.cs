// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// How hot the boost looks (#3925): a value 0…1 that rises over <see cref="RiseSeconds"/> once the boost burns and
    /// falls back over <see cref="FallSeconds"/> once it stops, eased at both ends and never past either end. The flight
    /// view blends its boost picture by it — the engine flame turning hot orange with more, bigger and faster flame, the
    /// engine light turning orange and brighter, a denser trail and the space dust's speed lines — so nothing snaps on or
    /// off. The numbers of that picture live here too, so the Unity side has no magic values. Presentation only: the
    /// server's <c>ShipBoostMeter</c> decides how fast the ship may fly.
    /// </summary>
    public struct BoostLook
    {
        /// <summary>Seconds from cold to the full boost look.</summary>
        public const float RiseSeconds = 0.15f;

        /// <summary>Seconds from the full boost look back to cold.</summary>
        public const float FallSeconds = 0.4f;

        /// <summary>The flame's particle rate at full boost, as a factor (it stays inside the thruster's particle cap).</summary>
        public const float FlameEmission = 1.6f;

        /// <summary>The flame's jet speed at full boost, as a factor: a longer, faster jet.</summary>
        public const float FlameSpeed = 1.4f;

        /// <summary>The flame particles' size at full boost, as a factor.</summary>
        public const float FlameSize = 1.3f;

        /// <summary>The engine trail's mote rate at full boost, as a factor.</summary>
        public const float TrailRate = 2f;

        /// <summary>The engine light's brightness at full boost, as a factor.</summary>
        public const float LightBrightness = 1.5f;

        /// <summary>The flame shader's glow at full boost (1 = as normal): hot enough to reach the bloom where there is one.</summary>
        public const float FlameGlow = 1.35f;

        /// <summary>The space dust's streak boost at full boost (the hyperjump's long star lines are 1).</summary>
        public const float DustShare = 0.3f;

        /// <summary>The view widens this far when the boost lights …</summary>
        public const float IgnitionFovDegrees = 8f;

        /// <summary>… and stays this much wider while it burns.</summary>
        public const float BurnFovDegrees = 6f;

        /// <summary>The camera shake (trauma) when the boost lights: a small jolt.</summary>
        public const float IgnitionTrauma = 0.25f;

        /// <summary>Motes the ignition throws out of each nozzle at full particle density.</summary>
        public const int IgnitionMotes = 14;

        /// <summary>The boost gauge's gentle pulse while the boost burns, per second (far below any flashing rate).</summary>
        public const float BarPulseHz = 1.6f;

        private float _t; // linear progress 0…1; Value eases it

        /// <summary>How hot the boost looks right now, 0 (cold) … 1 (full boost).</summary>
        public float Value => Smooth(_t);

        /// <summary>One frame: toward 1 while <paramref name="burning"/>, else back toward 0. A frame time of zero, below
        /// zero or not a number changes nothing. Returns <see cref="Value"/>.</summary>
        public float Tick(float dt, bool burning)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return Value;
            }

            float step = burning ? dt / RiseSeconds : -dt / FallSeconds;
            _t = Clamp01(_t + step);
            return Value;
        }

        /// <summary>Back to cold at once (a new flight).</summary>
        public void Reset() => _t = 0f;

        /// <summary>A factor that is 1 when cold and <paramref name="boosted"/> at full boost, of which only
        /// <paramref name="share"/> (0…1) is granted — the particle budget of the graphics tier.</summary>
        public float Scale(float boosted, float share = 1f) => 1f + ((boosted - 1f) * Value * Clamp01(share));

        /// <summary>The share of the extra boost particles a graphics tier gets: none on the lowest tier, else its particle
        /// density (which "Reduced effects" already halves). The colour, the speed, the light, the streaks and the camera
        /// do not depend on it.</summary>
        public static float ParticleShare(bool lowestTier, float density) => lowestTier ? 0f : Clamp01(density);

        /// <summary>Whether the boost's ignition burst (motes and a glow at the nozzles) plays on this tier.</summary>
        public static bool IgnitionBurst(bool lowestTier, float density) => ParticleShare(lowestTier, density) > 0f;

        /// <summary>The boost gauge's pulse, 0…1 over <see cref="BarPulseHz"/>; a steady 0 with <paramref name="steady"/>
        /// (reduced effects, reduce flashes).</summary>
        public static float BarPulse(float seconds, bool steady)
        {
            if (steady || float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                return 0f;
            }

            double phase = (seconds * (double)BarPulseHz) % 1.0;
            return (float)(0.5 - (0.5 * Math.Cos(phase * 2.0 * Math.PI)));
        }

        private static float Smooth(float t) => t * t * (3f - (2f * t));

        private static float Clamp01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;
    }
}

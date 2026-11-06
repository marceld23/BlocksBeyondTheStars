// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where the own ship is during its transition cinematics (#3140/#3141) as a pure function of the stage time: the
    /// touchdown stand-in sinking onto its pad and the lift-off hull rising, hovering and climbing away. The director
    /// flies the hull with these numbers and the shot plan (#3258, <see cref="TransitionShotPlan"/>) samples the very same
    /// path up front to find the stretch a camera spot can actually see.
    /// </summary>
    public static class TransitionFlightPath
    {
        /// <summary>The touchdown's descent from the drop height onto the pad.</summary>
        public const float DescendSeconds = 3.3f;

        /// <summary>The ship stands on its pad, the engines spool down, the camera pushes in a little.</summary>
        public const float SettleSeconds = 0.8f;

        /// <summary>The whole touchdown shot before the camera goes home (descent + settle).</summary>
        public const float TouchdownSeconds = DescendSeconds + SettleSeconds;

        /// <summary>The glide back into the player's eyes at the end of the touchdown.</summary>
        public const float EyeSeconds = 1.1f;

        /// <summary>The lift-off's glide out of the player's eyes (to the outside spot or the helm).</summary>
        public const float LiftoffGlideSeconds = 0.9f;

        /// <summary>The lift-off beats: the engines catch, the ship rises, the climb begins, the boom, into the clouds.</summary>
        public const float IgniteAt = 0.35f, LiftAt = 1.6f, ClimbAt = 2.8f, BoomAt = 3.3f, CloudsAt = 4.1f;

        /// <summary>Below this height over its pad the touchdown's engine fire whips up dust (the dust swirl's reach).</summary>
        public const float DustHeight = 14f;

        /// <summary>How high above its pad the touchdown stand-in starts, for a hull of <paramref name="hullHeight"/>.</summary>
        public static float TouchdownDrop(float hullHeight) => 34f + hullHeight * 2.5f;

        /// <summary>The descent's progress 0..1 at <paramref name="t"/> — an ease-out: fast high up, slowing near the ground.</summary>
        public static float TouchdownEase(float t)
        {
            float u = Clamp01(t / DescendSeconds);
            return 1f - (1f - u) * (1f - u) * (1f - u);
        }

        /// <summary>The stand-in's height over its pad at <paramref name="t"/> (0 once it has touched down).</summary>
        public static float TouchdownHeight(float t, float drop) => drop * (1f - TouchdownEase(t));

        /// <summary>The stage time at which the descending stand-in passes <paramref name="height"/> over its pad (0 when it
        /// starts below it).</summary>
        public static float TouchdownTimeAt(float height, float drop)
        {
            if (drop <= 0f || height >= drop)
            {
                return 0f;
            }

            float u = 1f - (float)Math.Pow(Math.Max(0f, height) / drop, 1.0 / 3.0);
            return u * DescendSeconds;
        }

        /// <summary>The stand-in's pose at <paramref name="t"/>: sinking on the ease-out with a fading sway, its nose dipped
        /// a little and rocking while it is still high.</summary>
        public static ShipFlightPose Touchdown(float t, float drop)
        {
            float e = TouchdownEase(t);
            float sway = (1f - e) * 0.6f;
            return new ShipFlightPose(
                (float)Math.Sin(t * 1.7f) * sway, drop * (1f - e), (float)Math.Cos(t * 1.3f) * sway,
                5f * (1f - e), 2.5f * (float)Math.Sin(t * 2.1f) * (1f - e), 0f, 1f);
        }

        /// <summary>The lifting hull's pose at <paramref name="t"/>: still on the pad while the engines catch, a heavy rise
        /// and a hover with the nose coming up, then an accelerating climb along the nose.</summary>
        public static ShipFlightPose Liftoff(float t)
        {
            float rise = t < LiftAt ? 0f : SmoothStep(0f, 1f, Clamp01((t - LiftAt) / (ClimbAt - LiftAt))) * 5f;
            float climbT = Math.Max(0f, t - ClimbAt);
            float pitch = SmoothStep(0f, 16f, Clamp01((t - LiftAt - 0.4f) / 1.2f));
            float rad = pitch * (float)(Math.PI / 180.0);
            float noseY = (float)Math.Sin(rad), noseZ = (float)Math.Cos(rad);

            // The climb heads along (nose · 0.6 + up), normalised, accelerating.
            float cy = noseY * 0.6f + 1f, cz = noseZ * 0.6f;
            float len = (float)Math.Sqrt(cy * cy + cz * cz);
            float climb = climbT * climbT * 14f / len;
            float wobble = t > LiftAt && t < ClimbAt + 0.5f ? 0.8f : 0.2f;
            float shakeX = (float)Math.Sin(t * 13f) * 0.05f * wobble;
            float shakeZ = (float)Math.Cos(t * 11f) * 0.05f * wobble;
            return new ShipFlightPose(shakeX, rise + cy * climb, cz * climb + shakeZ,
                -pitch, (float)Math.Sin(t * 2.3f) * 1.5f * wobble, noseY, noseZ);
        }

        /// <summary>Unity's <c>Mathf.SmoothStep</c>: a cubic ease between <paramref name="from"/> and <paramref name="to"/>.</summary>
        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>One moment of a flying hull, relative to where it stands at rest: the offset (blocks), the rotation as the
    /// transform's Euler angles (degrees, X then Z; Y never turns) and the nose's direction in the Y/Z plane.</summary>
    public readonly struct ShipFlightPose
    {
        public ShipFlightPose(float x, float y, float z, float rotX, float rotZ, float noseY, float noseZ)
        {
            X = x;
            Y = y;
            Z = z;
            RotX = rotX;
            RotZ = rotZ;
            NoseY = noseY;
            NoseZ = noseZ;
        }

        /// <summary>The offset from the resting position, in blocks.</summary>
        public float X { get; }

        /// <inheritdoc cref="X"/>
        public float Y { get; }

        /// <inheritdoc cref="X"/>
        public float Z { get; }

        /// <summary>The transform's Euler X (degrees; negative lifts the nose).</summary>
        public float RotX { get; }

        /// <summary>The transform's Euler Z (degrees; the roll).</summary>
        public float RotZ { get; }

        /// <summary>The nose's direction (Y, Z components; X is always 0).</summary>
        public float NoseY { get; }

        /// <inheritdoc cref="NoseY"/>
        public float NoseZ { get; }
    }
}

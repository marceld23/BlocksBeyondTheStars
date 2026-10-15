// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2593: the curves of the avatar's greeting gestures — the wave, the nod, the cheer — and the NPC head turn towards a
    /// nearby player; #2647: a hunter's bow shot. Unity-free; <c>PlayerAvatar</c> turns them into joint angles in its procedural pose. Every gesture
    /// takes its progress <c>c</c> (0 at the start, 1 at the end) and returns to rest at both ends, so it blends over
    /// whatever pose plays underneath (idle, sitting) without a snap.
    /// </summary>
    public static class AvatarGestures
    {
        /// <summary>A wave lasts this long.</summary>
        public const float WaveSeconds = 1.2f;

        /// <summary>A nod lasts this long.</summary>
        public const float NodSeconds = 0.6f;

        /// <summary>A cheer lasts this long.</summary>
        public const float CheerSeconds = 1.2f;

        /// <summary>Full periods of the hand's wag over a wave (2.5 = five wags, left-right-left-right-left).</summary>
        public const float WaveWagPeriods = 2.5f;

        /// <summary>The nod's depth (degrees, down).</summary>
        public const float NodDegrees = 12f;

        /// <summary>Bobs of the raised arms over a cheer.</summary>
        public const float CheerBobPeriods = 3f;

        /// <summary>The head turns at most this far towards a player (degrees, either side).</summary>
        public const float LookYawLimit = 70f;

        /// <summary>The head tilts at most this far up or down towards a player.</summary>
        public const float LookPitchLimit = 30f;

        /// <summary>A player further round than this behind the NPC's shoulder is not looked at.</summary>
        public const float LookFieldDegrees = 110f;

        /// <summary>
        /// The wave at progress <paramref name="c"/>: <c>Blend</c> raises the arm (smooth in over the first 15 %, out over the
        /// last 18 %, 0 outside 0..1) and <c>Wag</c> (−1..1) swings the forearm, <see cref="WaveWagPeriods"/> periods —
        /// zero at both ends.
        /// </summary>
        public static (float Blend, float Wag) Wave(float c)
        {
            if (!(c > 0f && c < 1f))
            {
                return (0f, 0f);
            }

            return (Envelope(c, 0.15f, 0.82f), (float)Math.Sin(2.0 * Math.PI * WaveWagPeriods * c));
        }

        /// <summary>The nod at progress <paramref name="c"/>: extra head pitch in degrees (positive = down), two dips that
        /// fade — 0 at both ends.</summary>
        public static float Nod(float c)
        {
            if (!(c > 0f && c < 1f))
            {
                return 0f;
            }

            return NodDegrees * (float)Math.Sin(2.0 * Math.PI * 2.0 * c) * (1f - c);
        }

        /// <summary>The cheer at progress <paramref name="c"/>: <c>Blend</c> raises both arms (in over 15 %, out over the
        /// last 20 %) and <c>Bob</c> (−1..1) pumps them, <see cref="CheerBobPeriods"/> times.</summary>
        public static (float Blend, float Bob) Cheer(float c)
        {
            if (!(c > 0f && c < 1f))
            {
                return (0f, 0f);
            }

            return (Envelope(c, 0.15f, 0.8f), (float)Math.Sin(2.0 * Math.PI * CheerBobPeriods * c));
        }

        /// <summary>A hunter's bow shot lasts this long (#2647): the draw, the release, a moment's follow-through, the bow
        /// lowered again.</summary>
        public const float BowShotSeconds = 0.9f;

        /// <summary>The string is let go at this share of <see cref="BowShotSeconds"/> — the arrow leaves then. Short on
        /// purpose: the server's shot (the arrow and the twang) has already happened when the client hears of it, so the
        /// draw is a quick snap and the release lands close to the sound.</summary>
        public const float BowReleaseFraction = 0.2f;

        /// <summary>How far the string hand flies on past its anchor after the release (a share of the full draw).</summary>
        public const float BowFollowThrough = 0.3f;

        /// <summary>Seconds from the start of a bow shot to the release — the arrow's launch delay.</summary>
        public static float BowReleaseSeconds => BowShotSeconds * BowReleaseFraction;

        /// <summary>
        /// The bow shot at progress <paramref name="c"/> (#2647): <c>Raise</c> lifts the bow arm to the target (up by the
        /// release, held, lowered over the last 38 %) and <c>Draw</c> pulls the string hand back — 0 at the string, 1 at its
        /// anchor at the release, then on to 1 + <see cref="BowFollowThrough"/> as the fingers let go. <c>Draw</c> is
        /// scaled by <c>Raise</c>, so both are 0 at the ends and outside 0..1.
        /// </summary>
        public static (float Raise, float Draw) BowShot(float c)
        {
            if (!(c > 0f && c < 1f))
            {
                return (0f, 0f);
            }

            float raise = Envelope(c, BowReleaseFraction, 0.62f);
            float pull = c < BowReleaseFraction
                ? SmoothStep(c / BowReleaseFraction)
                : 1f + (BowFollowThrough * SmoothStep((c - BowReleaseFraction) / 0.1f));
            return (raise, pull * raise);
        }

        /// <summary>
        /// Where an NPC's head turns to look at a player: <paramref name="bodyYaw"/> is the NPC's facing (degrees, 0 = +Z,
        /// 90 = +X), (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>) the player's eye relative to
        /// the NPC's head. False when the player stands more than <see cref="LookFieldDegrees"/> round behind the NPC
        /// (it does not twist its neck); else the head yaw relative to the body (clamped to ±<see cref="LookYawLimit"/>)
        /// and the pitch (degrees, up negative like the camera, ±<see cref="LookPitchLimit"/>).
        /// </summary>
        public static bool HeadLook(float bodyYaw, float dx, float dy, float dz, out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            float flat = (float)Math.Sqrt((dx * dx) + (dz * dz));
            if (flat < 0.05f)
            {
                return false; // standing on top of each other — nowhere sensible to look
            }

            float bearing = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
            float rel = DeltaAngle(bodyYaw, bearing);
            if (Math.Abs(rel) > LookFieldDegrees)
            {
                return false;
            }

            yaw = Clamp(rel, -LookYawLimit, LookYawLimit);
            pitch = Clamp((float)(-Math.Atan2(dy, flat) * 180.0 / Math.PI), -LookPitchLimit, LookPitchLimit);
            return true;
        }

        /// <summary>The shortest signed angle from <paramref name="from"/> to <paramref name="to"/> (degrees, −180..180).</summary>
        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f)
            {
                d -= 360f;
            }
            else if (d < -180f)
            {
                d += 360f;
            }

            return d;
        }

        /// <summary>A smooth 0→1→0 envelope: smoothstep in over 0..<paramref name="inEnd"/>, 1, smoothstep out over
        /// <paramref name="outStart"/>..1.</summary>
        private static float Envelope(float c, float inEnd, float outStart)
        {
            if (c < inEnd)
            {
                return SmoothStep(c / inEnd);
            }

            return c > outStart ? 1f - SmoothStep((c - outStart) / (1f - outStart)) : 1f;
        }

        private static float SmoothStep(float x)
        {
            float t = Clamp(x, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// How the soft contact shadow under a figure (the client's <c>BlobShadow</c>) fades with the figure's height above
    /// the ground. Two kinds:
    /// <list type="bullet">
    /// <item><b>Ground figures</b> (players, NPCs, walking animals, #3738): full while standing, gone
    /// <see cref="GroundReach"/> metres up — it grounds a figure that touches the floor.</item>
    /// <item><b>Flying animals</b> (player report 2026-11-27: Lyxette saw an air fish gliding ten blocks over a slope and
    /// read it as a fish climbing the mountain): a drop shadow straight under the animal that stays visible up to
    /// <see cref="FlierReach"/> metres, so the eye sees the gap between body and ground. It thins out with height, and
    /// it also shows by day where a preset has sun shadows (at <see cref="FlierDayStrength"/>): a real sun shadow falls
    /// far off to the side once the sun is low and says nothing about the height.</item>
    /// </list>
    /// Pure numbers — the Unity side probes the ground and lays the quad.
    /// </summary>
    public static class ContactShadowFade
    {
        /// <summary>How far under a ground figure the ground is looked for — higher than that there is no shadow.</summary>
        public const float GroundReach = 5f;

        /// <summary>How far under a flying animal the ground is looked for: an air fish hovers 3–12 blocks up plus its
        /// one-block swoop, a bird 5, a sky ray rides its band a little higher over a roof.</summary>
        public const float FlierReach = 18f;

        /// <summary>The darkest the blob gets (alpha).</summary>
        public const float MaxAlpha = 0.42f;

        /// <summary>The least a flier's drop shadow shows on a preset with sun shadows by day (the ground figures' blob is
        /// off there — the sun's own shadow grounds them).</summary>
        public const float FlierDayStrength = 0.6f;

        /// <summary>From this height a flier's drop shadow gets its full day strength; below it the floor ramps in, so a
        /// bird sitting on the ground by day is grounded by the sun's shadow alone, like a walker.</summary>
        public const float FlierAirborneHeight = 2f;

        /// <summary>The blob's alpha for a figure <paramref name="height"/> metres over the ground at the light
        /// <paramref name="strength"/> (0…1, the caller's day/night/roof verdict): a ground figure's fades fast
        /// (quadratic over <see cref="GroundReach"/>), a flier's slowly (1 − t² over <see cref="FlierReach"/>) and, once
        /// it is in the air, never below <see cref="FlierDayStrength"/> of its light.</summary>
        public static float Alpha(float height, float strength, bool flier)
        {
            float t = Near(height, flier);
            if (!flier)
            {
                return MaxAlpha * Clamp01(strength) * t * t;
            }

            float h = 1f - t; // 0 on the ground … 1 at the reach
            return MaxAlpha * Light(height, strength, flier: true) * (1f - (h * h));
        }

        /// <summary>The light a blob is drawn at: the caller's <paramref name="strength"/>, for a flier in the air at least
        /// <see cref="FlierDayStrength"/> (ramping in over the first <see cref="FlierAirborneHeight"/> metres).</summary>
        public static float Light(float height, float strength, bool flier)
        {
            float s = Clamp01(strength);
            return flier ? Math.Max(s, FlierDayStrength * Clamp01(Math.Max(0f, height) / FlierAirborneHeight)) : s;
        }

        /// <summary>The blob's size factor on top of the figure's radius: a little smaller the higher the figure is.</summary>
        public static float SizeFactor(float height, bool flier) => 0.7f + (0.3f * Near(height, flier));

        /// <summary>How far down the ground is looked for under this kind of figure.</summary>
        public static float Reach(bool flier) => flier ? FlierReach : GroundReach;

        /// <summary>1 on the ground → 0 at the reach.</summary>
        private static float Near(float height, bool flier) => 1f - Clamp01(Math.Max(0f, height) / Reach(flier));

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}

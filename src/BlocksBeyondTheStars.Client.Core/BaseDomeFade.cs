// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When the base's life-support dome shows (#2659). The dome marks the core zone — the start zone where a player
    /// breathes before building a room. Once any sealed room of the base holds air, every enclosed room breathes (#2651)
    /// and the dome would only read as a boundary that does not exist, so it fades out; when the last room loses its air
    /// it fades back in. Unity-free so the rule is covered by plain tests; <c>BaseShieldView</c> applies it.
    /// </summary>
    public static class BaseDomeFade
    {
        /// <summary>Seconds a full fade (visible ↔ hidden) takes.</summary>
        public const float FadeSeconds = 1f;

        /// <summary>The dome's target opacity for a base: <paramref name="visibleAlpha"/> while none of its sealed rooms
        /// holds air, 0 once one does.</summary>
        public static float TargetAlpha(int sealedRoomsWithAir, float visibleAlpha)
            => sealedRoomsWithAir > 0 ? 0f : visibleAlpha;

        /// <summary>One frame of the fade: moves <paramref name="current"/> towards <paramref name="target"/> at a rate
        /// that covers <paramref name="visibleAlpha"/> in <see cref="FadeSeconds"/>, never overshooting.</summary>
        public static float Step(float current, float target, float visibleAlpha, float deltaSeconds)
        {
            float maxDelta = visibleAlpha / FadeSeconds * (deltaSeconds > 0f ? deltaSeconds : 0f);
            if (current < target)
            {
                return current + maxDelta >= target ? target : current + maxDelta;
            }

            return current - maxDelta <= target ? target : current - maxDelta;
        }
    }
}

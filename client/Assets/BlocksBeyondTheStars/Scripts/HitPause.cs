// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The tiny stop that makes a hit land: for a few hundredths of a second the swing of the player's own hand holds
    /// still. Deliberately NOT <c>Time.timeScale</c> — the world, the other players and the server's clock run on; only
    /// the pose that reads <see cref="Active"/> waits. Off with "Reduced effects".
    /// </summary>
    public static class HitPause
    {
        /// <summary>How long a confirmed hit holds the swing, seconds.</summary>
        public const float HitSeconds = 0.05f;

        /// <summary>How long the hit that defeats the target holds it — a touch longer.</summary>
        public const float DefeatSeconds = 0.08f;

        private static float _until;

        /// <summary>True while the pose should hold still.</summary>
        public static bool Active => Time.unscaledTime < _until;

        /// <summary>Starts (or extends) a stop of <paramref name="seconds"/>.</summary>
        public static void Trigger(float seconds)
        {
            if (UiKit.ReducedMotion || seconds <= 0f)
            {
                return;
            }

            _until = Mathf.Max(_until, Time.unscaledTime + seconds);
        }
    }
}

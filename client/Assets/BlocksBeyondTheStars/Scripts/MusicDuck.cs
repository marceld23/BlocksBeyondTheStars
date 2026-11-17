// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Moments in which the music makes room: while someone speaks (VEGA's lines, a cutscene's speaker, a conversation
    /// with an NPC) and under a short musical accent. Whoever speaks calls <see cref="Speech"/> every frame it does;
    /// <see cref="ClientMusic"/> reads <see cref="Level"/> and slews its bus toward it, so the music swells back by
    /// itself a moment after the last call. Works without a mixer — the browser has none to offer.
    /// </summary>
    public static class MusicDuck
    {
        /// <summary>The music's level while someone speaks.</summary>
        public const float SpeechLevel = 0.55f;

        /// <summary>The music's level under a musical accent.</summary>
        public const float StingLevel = 0.4f;

        /// <summary>How long after the last <see cref="Speech"/> call the music stays down — the line is still being read.</summary>
        public const float SpeechHoldSeconds = 1.2f;

        private static float _speechUntil;
        private static float _stingUntil;

        /// <summary>Someone is speaking this frame.</summary>
        public static void Speech() => _speechUntil = Time.unscaledTime + SpeechHoldSeconds;

        /// <summary>A musical accent of <paramref name="seconds"/> starts now.</summary>
        public static void Sting(float seconds) => _stingUntil = Mathf.Max(_stingUntil, Time.unscaledTime + Mathf.Max(0f, seconds));

        /// <summary>The level the music should sit at right now (1 = untouched).</summary>
        public static float Level
        {
            get
            {
                float now = Time.unscaledTime;
                float level = 1f;
                if (now < _speechUntil)
                {
                    level = SpeechLevel;
                }

                if (now < _stingUntil)
                {
                    level = Mathf.Min(level, StingLevel);
                }

                return level;
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Musical accents: a few short stings that mark the moments worth marking — a new world, a find, a blueprint
    /// researched, an achievement, coming home. They are rare by rule: never two at once, a rest between any two, and
    /// the music steps back while one plays (<see cref="MusicDuck"/>). When an accent may not play (too soon, or its
    /// clip is not bundled) the event's plain cue plays instead, so nothing ever goes silent.
    /// </summary>
    public sealed partial class ClientAudio
    {
        /// <summary>The shortest rest between two accents, seconds.</summary>
        public const float StingRestSeconds = 8f;

        private float _stingBusyUntil;

        /// <summary>Plays the accent <paramref name="id"/>, or — when accents rest or the clip is missing — the plain cue
        /// <paramref name="fallback"/> (null = nothing). Returns true when the accent itself played.</summary>
        public bool Sting(string id, string fallback = null, float vol = 0.9f)
        {
            var clip = Recorded(id);
            if (clip == null || Time.unscaledTime < _stingBusyUntil)
            {
                if (!string.IsNullOrEmpty(fallback))
                {
                    Cue(fallback, vol);
                }

                return false;
            }

            _stingBusyUntil = Time.unscaledTime + clip.length + StingRestSeconds;
            MusicDuck.Sting(clip.length);
            Play2D(clip, vol);
            return true;
        }
    }
}

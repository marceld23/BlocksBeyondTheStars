// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Music
{
    /// <summary>
    /// When the on-foot tension music plays. A hostile close by used to only lower and darken the planet's bed; it now
    /// brings a pool of its own — alert, not frightening. The rule keeps the music from flickering: a hostile must stay
    /// close for <see cref="ArmSeconds"/> before the pool starts (walking past one changes nothing), the pool then plays
    /// for at least <see cref="MinimumSeconds"/>, and it ends only <see cref="ReleaseSeconds"/> after the last hostile
    /// left. A peaceful world has no hostiles and so never hears it.
    /// </summary>
    public sealed class TensionMusic
    {
        /// <summary>A hostile must stay close this long before the pool starts.</summary>
        public const float ArmSeconds = 2f;

        /// <summary>Once started, the pool plays at least this long.</summary>
        public const float MinimumSeconds = 20f;

        /// <summary>The pool ends this long after the last hostile left.</summary>
        public const float ReleaseSeconds = 8f;

        private float _near;
        private float _active;
        private float _clear;

        /// <summary>The tension pool plays.</summary>
        public bool Active { get; private set; }

        /// <summary>One frame: <paramref name="hostileNear"/> — a hostile is within reach; <paramref name="allowed"/> — the
        /// player is out on foot (not aboard). Returns <see cref="Active"/>.</summary>
        public bool Step(float dt, bool hostileNear, bool allowed)
        {
            if (!allowed)
            {
                Reset();
                return false;
            }

            float step = dt > 0f ? dt : 0f;
            if (hostileNear)
            {
                _near += step;
                _clear = 0f;
                if (!Active && _near >= ArmSeconds)
                {
                    Active = true;
                    _active = 0f;
                }
            }
            else
            {
                _near = 0f;
                _clear += step;
            }

            if (Active)
            {
                _active += step;
                if (_active >= MinimumSeconds && _clear >= ReleaseSeconds)
                {
                    Active = false;
                }
            }

            return Active;
        }

        /// <summary>Starts afresh (the player went aboard, flew, a cutscene took over).</summary>
        public void Reset()
        {
            Active = false;
            _near = 0f;
            _active = 0f;
            _clear = 0f;
        }
    }
}

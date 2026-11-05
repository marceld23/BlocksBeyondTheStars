// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The suit's little thrusters on a spacewalk (#3241): before this a suit move made no sound at all. A short puff when
    /// the push starts or turns by more than <see cref="TurnDegrees"/>, never a constant hiss while it holds, never two
    /// closer than <see cref="MinGapSeconds"/>. The two takes alternate with a small pitch spread. The input is the move in
    /// the look frame (strafe, lift, forward) — the mouse turning the view is no new push. Unity-free so it is unit-tested.
    /// </summary>
    public sealed class SuitThrusterPuffs
    {
        public const float MinGapSeconds = 0.25f;

        /// <summary>An input shorter than this is no push.</summary>
        public const float DeadZone = 0.2f;

        /// <summary>A push turned further than this from the last puff's direction is a new one.</summary>
        public const float TurnDegrees = 60f;

        public const float Level = 0.35f;

        private const float PitchSpread = 0.05f;

        private static readonly float TurnCosine = (float)Math.Cos(TurnDegrees * Math.PI / 180d);

        private readonly Random _rng;
        private float _since = MinGapSeconds;
        private bool _pushing;
        private float _ax, _ay, _az; // the direction of the last puff
        private bool _alt;

        public SuitThrusterPuffs(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Advances by <paramref name="dt"/> with this frame's move input; returns the puff to play now, or
        /// <see cref="BodyCue.None"/>.</summary>
        public BodyCue Tick(float dt, float strafe, float lift, float forward)
        {
            _since += dt;
            float length = (float)Math.Sqrt((strafe * strafe) + (lift * lift) + (forward * forward));
            if (length < DeadZone)
            {
                _pushing = false;
                return BodyCue.None;
            }

            float x = strafe / length, y = lift / length, z = forward / length;
            bool started = !_pushing;
            bool turned = !started && ((x * _ax) + (y * _ay) + (z * _az)) < TurnCosine;
            _pushing = true;
            if (!started && !turned)
            {
                return BodyCue.None;
            }

            if (_since < MinGapSeconds)
            {
                if (started)
                {
                    // Tapping fast: this push stays silent, but its direction is the one a later turn is measured from.
                    _ax = x;
                    _ay = y;
                    _az = z;
                }

                return BodyCue.None; // a turn within the gap keeps the old direction, so a later frame still puffs for it
            }

            _ax = x;
            _ay = y;
            _az = z;
            _since = 0f;
            _alt = !_alt;
            float pitch = FootstepRules.Jitter(_rng.NextDouble(), PitchSpread);
            return new BodyCue(_alt ? "suit_thruster" : "suit_thruster_2", started ? Level : Level * 0.85f, pitch);
        }
    }
}

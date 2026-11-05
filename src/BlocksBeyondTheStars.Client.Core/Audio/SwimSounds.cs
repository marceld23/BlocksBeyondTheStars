// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the water layer reads each frame (#3240).</summary>
    public readonly struct SwimInput
    {
        public SwimInput(float dt, bool feetWet, bool swimming, bool stroking, bool headUnder, float fallSpeed)
        {
            Dt = dt;
            FeetWet = feetWet;
            Swimming = swimming;
            Stroking = stroking;
            HeadUnder = headUnder;
            FallSpeed = fallSpeed;
        }

        public float Dt { get; }

        /// <summary>The feet are in water (any depth).</summary>
        public bool FeetWet { get; }

        /// <summary>The body swims (the chest is in water).</summary>
        public bool Swimming { get; }

        /// <summary>Swimming and pushing: a move key, or Jump to rise.</summary>
        public bool Stroking { get; }

        /// <summary>The head is under water.</summary>
        public bool HeadUnder { get; }

        /// <summary>The downward speed before this frame (m/s, downward positive) — how hard the body hits the water.</summary>
        public float FallSpeed { get; }
    }

    /// <summary>
    /// The water around the player's own body (#3240): before this, water was only a low-pass — no splash going in or out,
    /// no strokes. A splash when the feet hit the water falling (louder the faster; wading in from a beach is the footsteps'
    /// job), a smaller one when the body leaves the water after swimming, strokes in the swim rhythm alternating two takes,
    /// and a gasp on surfacing from a dive longer than <see cref="GaspAfterSeconds"/>. Bobbing at the surface keeps the
    /// feet wet, so it never spams splashes; a cooldown guards the rest. Unity-free so the rules are unit-tested.
    /// </summary>
    public sealed class SwimSounds
    {
        /// <summary>The feet must hit the water at least this fast (m/s) for a splash.</summary>
        public const float SplashMinSpeed = 2.5f;

        /// <summary>No two splashes closer than this (s).</summary>
        public const float SplashGap = 0.8f;

        /// <summary>Leaving the water splashes only when the body swam within this long (s) — not after a wade.</summary>
        public const float SplashOutWindow = 1.5f;

        public const float SplashOutLevel = 0.35f;

        /// <summary>One stroke every ~0.95 s (±10 %) while pushing through the water.</summary>
        public const float StrokeInterval = 0.95f;

        /// <summary>The first stroke comes this soon (s) after the push starts.</summary>
        public const float StrokeLead = 0.15f;

        public const float StrokeLevel = 0.3f;

        /// <summary>A dive longer than this (s) ends with a gasp at the surface.</summary>
        public const float GaspAfterSeconds = 6f;

        public const float GaspLevel = 0.4f;

        private const float StrokeSpread = 0.10f;
        private const float StrokePitchSpread = 0.04f;
        private const float LongAgo = 1000f;

        private readonly Random _rng;
        private bool _feetWet;
        private bool _known;
        private float _sinceSwim = LongAgo;
        private float _sinceSplash = LongAgo;
        private float _strokeIn = -1f; // >= 0 while stroking
        private bool _strokeAlt;
        private float _dive;

        public SwimSounds(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>The splash's level for a body hitting the water at <paramref name="speed"/> m/s — 0 below
        /// <see cref="SplashMinSpeed"/>, then from 0.25 up to 0.7 at a hard fall.</summary>
        public static float SplashInVolume(float speed)
        {
            if (speed < SplashMinSpeed)
            {
                return 0f;
            }

            float v = 0.25f + (0.035f * (speed - SplashMinSpeed));
            return v > 0.7f ? 0.7f : v;
        }

        /// <summary>Takes the current state without a sound — after a teleport or a respawn, which are no plunge.</summary>
        public void Resync(bool feetWet)
        {
            _feetWet = feetWet;
            _known = true;
            _sinceSwim = LongAgo;
            _strokeIn = -1f;
            _dive = 0f;
        }

        /// <summary>Advances the layer by one frame; returns the cue to play now (a splash before a gasp before a stroke), or
        /// <see cref="BodyCue.None"/>.</summary>
        public BodyCue Tick(in SwimInput input)
        {
            if (!_known)
            {
                Resync(input.FeetWet);
            }

            float dt = input.Dt;
            _sinceSplash = Advance(_sinceSplash, dt);
            _sinceSwim = input.Swimming ? 0f : Advance(_sinceSwim, dt);

            var splash = TickSplash(input);
            var gasp = TickDive(dt, input.HeadUnder);
            var stroke = TickStroke(dt, input.Swimming && input.Stroking);
            return !splash.IsNone ? splash : !gasp.IsNone ? gasp : stroke;
        }

        private BodyCue TickSplash(in SwimInput input)
        {
            if (input.FeetWet == _feetWet)
            {
                return BodyCue.None;
            }

            _feetWet = input.FeetWet;
            if (_sinceSplash < SplashGap)
            {
                return BodyCue.None;
            }

            if (input.FeetWet)
            {
                float level = SplashInVolume(input.FallSpeed);
                if (level <= 0f)
                {
                    return BodyCue.None;
                }

                _sinceSplash = 0f;
                return new BodyCue("water_splash_in", level, FootstepRules.Jitter(_rng.NextDouble(), StrokePitchSpread));
            }

            if (_sinceSwim > SplashOutWindow)
            {
                return BodyCue.None;
            }

            _sinceSplash = 0f;
            return new BodyCue("water_splash_out", SplashOutLevel, FootstepRules.Jitter(_rng.NextDouble(), StrokePitchSpread));
        }

        private BodyCue TickDive(float dt, bool headUnder)
        {
            if (headUnder)
            {
                _dive += dt;
                return BodyCue.None;
            }

            bool gasp = _dive > GaspAfterSeconds;
            _dive = 0f;
            return gasp ? new BodyCue("surface_gasp", GaspLevel, 1f) : BodyCue.None;
        }

        private BodyCue TickStroke(float dt, bool stroking)
        {
            if (!stroking)
            {
                _strokeIn = -1f;
                return BodyCue.None;
            }

            if (_strokeIn < 0f)
            {
                _strokeIn = StrokeLead;
            }

            _strokeIn -= dt;
            if (_strokeIn > 0f)
            {
                return BodyCue.None;
            }

            _strokeIn = StrokeInterval * FootstepRules.Jitter(_rng.NextDouble(), StrokeSpread);
            _strokeAlt = !_strokeAlt;
            float level = StrokeLevel * FootstepRules.Jitter(_rng.NextDouble(), FootstepRules.VolumeJitter);
            float pitch = FootstepRules.Jitter(_rng.NextDouble(), StrokePitchSpread);
            return new BodyCue(_strokeAlt ? "swim_stroke" : "swim_stroke_2", level, pitch);
        }

        private static float Advance(float seconds, float dt) => Math.Min(seconds + dt, LongAgo);
    }
}

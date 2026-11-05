// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One of the player's own body sounds to play now (#3239/#3240) — or none (<see cref="IsNone"/>).</summary>
    public readonly struct BodyCue
    {
        public BodyCue(string id, float volume, float pitch)
        {
            Id = id;
            Volume = volume;
            Pitch = pitch;
        }

        /// <summary>The clip id; null when nothing plays.</summary>
        public string? Id { get; }

        public float Volume { get; }

        public float Pitch { get; }

        public bool IsNone => Id == null;

        public static BodyCue None => default;
    }

    /// <summary>What the body layer reads each frame (#3240).</summary>
    public readonly struct BodyInput
    {
        public BodyInput(float dt, bool active, float healthFraction, float oxygen, bool headUnderWater, bool running, bool jetpacking, bool hardLanding)
        {
            Dt = dt;
            Active = active;
            HealthFraction = healthFraction;
            Oxygen = oxygen;
            HeadUnderWater = headUnderWater;
            Running = running;
            Jetpacking = jetpacking;
            HardLanding = hardLanding;
        }

        public float Dt { get; }

        /// <summary>Alive and in play: not dead or respawning, no menu, no cutscene, no cinematic camera.</summary>
        public bool Active { get; }

        /// <summary>Health as a share of the full bar (0..1).</summary>
        public float HealthFraction { get; }

        /// <summary>The suit's oxygen, in the units the HUD's alarm reads (it trips at <see cref="BodySounds.LowOxygen"/>).</summary>
        public float Oxygen { get; }

        /// <summary>The head is under water — the breath is held.</summary>
        public bool HeadUnderWater { get; }

        /// <summary>The feet run (a run gait on the ground) this frame.</summary>
        public bool Running { get; }

        /// <summary>The jetpack fires this frame.</summary>
        public bool Jetpacking { get; }

        /// <summary>A hard landing (<see cref="LandingTier.Heavy"/>) happened this frame.</summary>
        public bool HardLanding { get; }
    }

    /// <summary>
    /// A subtle body layer (#3240; maintainer decision: only light — never scary, never constant). A calm exhale after a
    /// run of more than <see cref="RunSeconds"/>, a jetpack burn of more than <see cref="BurnSeconds"/> or a hard landing,
    /// never more often than every <see cref="ExhaleGapSeconds"/>; quicker soft breaths while the oxygen is low, in addition
    /// to the HUD's O₂ beeps; and a soft, slow heartbeat that fades in while the health is low. All of it stops at once when
    /// the player is dead, respawning, in a menu or a cutscene, and no breath plays while the head is under water. One cue
    /// per frame at most — a second one due in the same frame waits for the next. Unity-free so the rules are unit-tested.
    /// </summary>
    public sealed class BodySounds
    {
        /// <summary>A run longer than this (s) ends with an exhale.</summary>
        public const float RunSeconds = 5f;

        /// <summary>A jetpack burn longer than this (s) ends with an exhale.</summary>
        public const float BurnSeconds = 3f;

        /// <summary>Never two exhales closer than this (s).</summary>
        public const float ExhaleGapSeconds = 8f;

        /// <summary>The exhale comes this long (s) after the run or the burn ends.</summary>
        public const float ExhaleDelay = 0.6f;

        /// <summary>…and this long after a hard landing — "land_heavy" carries its own effort sound first.</summary>
        public const float LandingExhaleDelay = 1.1f;

        public const float ExhaleLevel = 0.35f;

        /// <summary>The HUD's low-oxygen alarm line (HudUi.UpdateOxygenAlarm): at or below it the breaths quicken.</summary>
        public const float LowOxygen = 25f;

        /// <summary>The quick breaths come every 5–7 s, the first one shortly after the oxygen runs low.</summary>
        public const float QuickBreathMin = 5f;

        public const float QuickBreathMax = 7f;

        public const float QuickBreathFirst = 1.5f;

        public const float QuickBreathLevel = 0.3f;

        /// <summary>Below this share of the health bar the heart is heard.</summary>
        public const float LowHealth = 0.2f;

        /// <summary>One beat every 1.1–1.4 s — soft and slow, not a panic.</summary>
        public const float HeartbeatMin = 1.1f;

        public const float HeartbeatMax = 1.4f;

        public const float HeartbeatLevel = 0.25f;

        /// <summary>The heartbeat fades in over this long (s) and out over <see cref="HeartFadeOutSeconds"/>.</summary>
        public const float HeartFadeInSeconds = 4f;

        public const float HeartFadeOutSeconds = 1.5f;

        private const float AudibleFade = 0.05f;
        private const float BreathPitchSpread = 0.03f;

        private readonly Random _rng;
        private float _sinceExhale = ExhaleGapSeconds;
        private float _exhaleIn = -1f; // >= 0 while an exhale is pending
        private float _runFor;
        private float _burnFor;
        private bool _exhaleAlt;
        private float _quickIn = -1f;  // >= 0 while the oxygen is low
        private float _heartFade;
        private float _heartIn;

        public BodySounds(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>The heartbeat's current fade (0..1).</summary>
        public float HeartFade => _heartFade;

        /// <summary>Advances the layer by one frame; returns the cue to play now, or <see cref="BodyCue.None"/>.</summary>
        public BodyCue Tick(in BodyInput input)
        {
            if (!input.Active)
            {
                Reset();
                return BodyCue.None;
            }

            float dt = input.Dt;
            _sinceExhale += dt;
            TrackExertion(input);

            // The heart keeps its rhythm first; a breath due in the same frame waits for the next one.
            var heart = TickHeart(input);
            if (!heart.IsNone)
            {
                return heart;
            }

            var exhale = TickExhale(dt, input.HeadUnderWater);
            return !exhale.IsNone ? exhale : TickQuickBreath(dt, input);
        }

        /// <summary>Everything stops: no pending exhale, no counted run or burn, the heart faded out.</summary>
        public void Reset()
        {
            _exhaleIn = -1f;
            _runFor = 0f;
            _burnFor = 0f;
            _quickIn = -1f;
            _heartFade = 0f;
            _heartIn = 0f;
        }

        private void TrackExertion(in BodyInput input)
        {
            if (input.Running)
            {
                _runFor += input.Dt;
            }
            else
            {
                if (_runFor > RunSeconds)
                {
                    Schedule(ExhaleDelay);
                }

                _runFor = 0f;
            }

            if (input.Jetpacking)
            {
                _burnFor += input.Dt;
            }
            else
            {
                if (_burnFor > BurnSeconds)
                {
                    Schedule(ExhaleDelay);
                }

                _burnFor = 0f;
            }

            if (input.HardLanding)
            {
                Schedule(LandingExhaleDelay);
            }
        }

        private void Schedule(float delay)
        {
            if (_exhaleIn < 0f || delay < _exhaleIn)
            {
                _exhaleIn = delay;
            }
        }

        private BodyCue TickExhale(float dt, bool headUnderWater)
        {
            if (_exhaleIn < 0f)
            {
                return BodyCue.None;
            }

            _exhaleIn -= dt;
            if (_exhaleIn > 0f)
            {
                return BodyCue.None;
            }

            _exhaleIn = -1f;
            if (headUnderWater || _sinceExhale < ExhaleGapSeconds)
            {
                return BodyCue.None; // dropped, never queued: a breath under water, or too soon after the last one
            }

            _sinceExhale = 0f;
            _exhaleAlt = !_exhaleAlt;
            return new BodyCue(_exhaleAlt ? "breath_exhale" : "breath_exhale_2", ExhaleLevel, BreathPitch());
        }

        private BodyCue TickQuickBreath(float dt, in BodyInput input)
        {
            if (input.Oxygen > LowOxygen)
            {
                _quickIn = -1f;
                return BodyCue.None;
            }

            if (_quickIn < 0f)
            {
                _quickIn = QuickBreathFirst; // just ran low: the HUD's first beep comes first
            }

            _quickIn -= dt;
            if (_quickIn > 0f)
            {
                return BodyCue.None;
            }

            _quickIn = Between(QuickBreathMin, QuickBreathMax);
            return input.HeadUnderWater ? BodyCue.None : new BodyCue("breath_quick", QuickBreathLevel, BreathPitch());
        }

        private BodyCue TickHeart(in BodyInput input)
        {
            bool low = input.HealthFraction > 0f && input.HealthFraction < LowHealth;
            float rate = low ? input.Dt / HeartFadeInSeconds : -input.Dt / HeartFadeOutSeconds;
            _heartFade = Math.Min(1f, Math.Max(0f, _heartFade + rate));
            if (_heartFade < AudibleFade)
            {
                _heartIn = 0f; // the first beat of the next low spell comes at once (at the faintest)
                return BodyCue.None;
            }

            _heartIn -= input.Dt;
            if (_heartIn > 0f)
            {
                return BodyCue.None;
            }

            _heartIn = Between(HeartbeatMin, HeartbeatMax);
            return new BodyCue("heartbeat_soft", HeartbeatLevel * _heartFade, 1f);
        }

        private float BreathPitch() => FootstepRules.Jitter(_rng.NextDouble(), BreathPitchSpread);

        private float Between(float min, float max) => min + ((max - min) * (float)_rng.NextDouble());
    }
}

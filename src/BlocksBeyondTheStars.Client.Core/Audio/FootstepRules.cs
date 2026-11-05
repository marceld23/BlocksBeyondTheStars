// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How a body walks, for its footsteps (#3239).</summary>
    public enum FootGait
    {
        Sneak, // crouched: slower and softer
        Walk,
        Run,   // clearly faster than the world's walk (a speed preparation, a boost)
    }

    /// <summary>How hard a body landed (#3239).</summary>
    public enum LandingTier
    {
        None,   // a stair, a slope, a tiny drop: the footsteps carry it
        Soft,   // a hop: a footstep on the surface, a little louder
        Normal, // a real drop: "land"
        Heavy,  // close to (or past) the safe fall speed: "land_heavy" with its effort exhale
    }

    /// <summary>One footfall: which of the surface's takes, and its level and pitch as factors on the caller's base.</summary>
    public readonly struct StepShape
    {
        public StepShape(int take, float volume, float pitch, bool leftFoot)
        {
            Take = take;
            Volume = volume;
            Pitch = pitch;
            LeftFoot = leftFoot;
        }

        /// <summary>1..<see cref="FootstepRules.TakeCount"/> (take 1 is the clip without a suffix).</summary>
        public int Take { get; }

        public float Volume { get; }

        public float Pitch { get; }

        public bool LeftFoot { get; }
    }

    /// <summary>The sound of a landing: its tier, its clip (null for <see cref="LandingTier.Soft"/>, which is a footstep take
    /// on the surface), and its level for the player's own body (a remote player scales it down).</summary>
    public readonly struct LandingSound
    {
        public LandingSound(LandingTier tier, string? clip, float volume)
        {
            Tier = tier;
            Clip = clip;
            Volume = volume;
        }

        public LandingTier Tier { get; }

        public string? Clip { get; }

        public float Volume { get; }

        public static LandingSound Silent => new LandingSound(LandingTier.None, null, 0f);
    }

    /// <summary>
    /// Living footsteps (#3239). Before this every step played one take of the surface every 0.45 s at one level and
    /// pitch, walking, sneaking and running alike, and every landing — a hop or a five-block drop — was the same thud.
    /// Here: the cadence follows the horizontal speed (sneaking slower and softer), the feet alternate (a small pitch and
    /// level offset), the three takes of a surface never repeat back to back, every step carries a small random pitch and
    /// level, and a landing is chosen by the fall speed against the safe fall speed. Unity-free so the rules are
    /// unit-tested; <see cref="FootstepCadence"/> holds the per-walker state.
    /// </summary>
    public static class FootstepRules
    {
        /// <summary>Takes per surface (<c>step_rock</c>, <c>step_rock_2</c>, <c>step_rock_3</c>).</summary>
        public const int TakeCount = 3;

        /// <summary>The step interval at the world's own walking speed (the old fixed interval).</summary>
        public const float WalkInterval = 0.45f;

        /// <summary>The fastest step interval (a run).</summary>
        public const float RunInterval = 0.32f;

        /// <summary>The sneak's interval, and the slowest any walk steps at.</summary>
        public const float SneakInterval = 0.6f;

        /// <summary>A sneak's level against a walk's.</summary>
        public const float SneakVolume = 0.6f;

        /// <summary>A run's level against a walk's.</summary>
        public const float RunVolume = 1.15f;

        /// <summary>Faster than this share of the world's walking speed is a run.</summary>
        public const float RunSpeedRatio = 1.25f;

        /// <summary>The walking speed (m/s) when the caller does not know the world's (another player's body).</summary>
        public const float ReferenceWalkSpeed = 6f;

        /// <summary>Below this horizontal speed (m/s) the feet do not step (pushing into a wall, the sneak's edge stop).</summary>
        public const float MinStepSpeed = 0.5f;

        /// <summary>The left foot is this much lower in pitch, the right this much higher.</summary>
        public const float FootPitchOffset = 0.03f;

        /// <summary>The left foot is this much quieter, the right this much louder.</summary>
        public const float FootVolumeOffset = 0.08f;

        /// <summary>The random pitch spread of a single step (±).</summary>
        public const float PitchJitter = 0.04f;

        /// <summary>The random level spread of a single step (±).</summary>
        public const float VolumeJitter = 0.10f;

        /// <summary>The server's safe fall speed at 1 g (m/s), when the caller does not know the world's.</summary>
        public const float DefaultSafeFallSpeed = 14f;

        /// <summary>A fall slower than this share of the safe fall speed makes no landing sound at all.</summary>
        public const float SilentLandingRatio = 0.2f;

        /// <summary>Up to this share a landing is a hop (a footstep), from it on a real drop ("land").</summary>
        public const float NormalLandingRatio = 0.55f;

        /// <summary>From this share on a landing is a hard one ("land_heavy").</summary>
        public const float HeavyLandingRatio = 0.85f;

        /// <summary>A hop shorter than this in the air (s) is a stair or a slope, not a landing — the footsteps carry it.</summary>
        public const float MinAirSeconds = 0.28f;

        /// <summary>The gait of a body moving at <paramref name="speed"/> (m/s) in a world that walks at
        /// <paramref name="walkSpeed"/>.</summary>
        public static FootGait GaitFor(float speed, float walkSpeed, bool crouched)
        {
            if (crouched)
            {
                return FootGait.Sneak;
            }

            return walkSpeed > 0f && speed >= walkSpeed * RunSpeedRatio ? FootGait.Run : FootGait.Walk;
        }

        /// <summary>Seconds between two footfalls: <see cref="WalkInterval"/> at the world's walking speed, quicker the
        /// faster the body goes (down to <see cref="RunInterval"/>), slower when it goes slower (up to
        /// <see cref="SneakInterval"/>). A sneak always steps at <see cref="SneakInterval"/>.</summary>
        public static float IntervalFor(FootGait gait, float speed, float walkSpeed)
        {
            if (gait == FootGait.Sneak)
            {
                return SneakInterval;
            }

            if (walkSpeed <= 0f || speed <= 0f)
            {
                return WalkInterval;
            }

            float interval = WalkInterval * (float)Math.Pow(walkSpeed / speed, 0.75);
            return Clamp(interval, RunInterval, SneakInterval);
        }

        /// <summary>The gait's level against a walk's.</summary>
        public static float GaitVolume(FootGait gait) => gait switch
        {
            FootGait.Sneak => SneakVolume,
            FootGait.Run => RunVolume,
            _ => 1f,
        };

        /// <summary>The next take (1..<see cref="TakeCount"/>) — never <paramref name="previous"/>, so no take plays twice in
        /// a row. <paramref name="roll"/> is a random value in [0, 1); a <paramref name="previous"/> outside the range (0 at
        /// the start) picks among all takes.</summary>
        public static int NextTake(int previous, double roll)
        {
            double r = roll < 0d ? 0d : roll >= 1d ? 0.999999d : roll;
            if (previous < 1 || previous > TakeCount)
            {
                return 1 + (int)(r * TakeCount);
            }

            int pick = 1 + (int)(r * (TakeCount - 1)); // 1..TakeCount-1 among the others
            return pick >= previous ? pick + 1 : pick;
        }

        /// <summary>A factor of 1 ± <paramref name="spread"/> from a random value in [0, 1).</summary>
        public static float Jitter(double roll, float spread) => 1f + (spread * (float)((2d * roll) - 1d));

        /// <summary>The shape of one footfall: the gait's level, the foot's offset and the two random spreads.</summary>
        public static StepShape Shape(FootGait gait, bool leftFoot, int take, double pitchRoll, double volumeRoll)
        {
            float side = leftFoot ? -1f : 1f;
            float pitch = (1f + (side * FootPitchOffset)) * Jitter(pitchRoll, PitchJitter);
            float volume = GaitVolume(gait) * (1f + (side * FootVolumeOffset)) * Jitter(volumeRoll, VolumeJitter);
            return new StepShape(take, volume, pitch, leftFoot);
        }

        /// <summary>The sound of a landing at <paramref name="fallSpeed"/> (m/s, downward positive) after
        /// <paramref name="airSeconds"/> in the air, against the world's <paramref name="safeFallSpeed"/> (a non-positive
        /// value means <see cref="DefaultSafeFallSpeed"/>). The thresholds are shares of the safe speed, so a jump lands as a
        /// hop on every world (the jump and the safe speed both scale with gravity).</summary>
        public static LandingSound Landing(float fallSpeed, float safeFallSpeed, float airSeconds)
        {
            float safe = safeFallSpeed > 0f ? safeFallSpeed : DefaultSafeFallSpeed;
            float ratio = fallSpeed / safe;
            if (ratio < SilentLandingRatio || (ratio < NormalLandingRatio && airSeconds < MinAirSeconds))
            {
                return LandingSound.Silent;
            }

            if (ratio < NormalLandingRatio)
            {
                return new LandingSound(LandingTier.Soft, null, Lerp(0.45f, 0.55f, Share(ratio, SilentLandingRatio, NormalLandingRatio)));
            }

            if (ratio < HeavyLandingRatio)
            {
                return new LandingSound(LandingTier.Normal, "land", Lerp(0.45f, 0.6f, Share(ratio, NormalLandingRatio, HeavyLandingRatio)));
            }

            return new LandingSound(LandingTier.Heavy, "land_heavy", Lerp(0.6f, 0.8f, Share(ratio, HeavyLandingRatio, HeavyLandingRatio + 0.6f)));
        }

        private static float Share(float value, float from, float to) => Clamp((value - from) / (to - from), 0f, 1f);

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }

    /// <summary>
    /// One walker's footstep rhythm (#3239) — the player's own feet, a climber's hands, another player's feet. Counts down
    /// to the next footfall at the gait's interval, alternates the feet and never repeats a take back to back. A pause
    /// longer than <see cref="IdleReset"/> starts the next walk with a footfall at once; a shorter one (a stair, a slope
    /// that lifts the feet for a moment) keeps the rhythm.
    /// </summary>
    public sealed class FootstepCadence
    {
        /// <summary>A pause longer than this (s) starts the next walk with a footfall at once.</summary>
        public const float IdleReset = 0.35f;

        private readonly Random _rng;
        private float _timer;
        private float _idle;
        private bool _leftFoot;
        private int _lastTake;

        public FootstepCadence(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>The take of the last footfall (0 before the first).</summary>
        public int LastTake => _lastTake;

        /// <summary>Advances the rhythm by <paramref name="dt"/>; true when a foot lands this frame, with its
        /// <paramref name="step"/>.</summary>
        public bool Tick(float dt, bool walking, FootGait gait, float speed, float walkSpeed, out StepShape step)
        {
            step = default;
            if (!walking)
            {
                _idle += dt;
                if (_idle >= IdleReset)
                {
                    _timer = 0f;
                }

                return false;
            }

            _idle = 0f;
            _timer -= dt;
            if (_timer > 0f)
            {
                return false;
            }

            float interval = FootstepRules.IntervalFor(gait, speed, walkSpeed);
            _timer += interval;
            if (_timer <= 0f)
            {
                _timer = interval; // a long frame never fires a burst of steps to catch up
            }

            step = Next(gait);
            return true;
        }

        /// <summary>A footfall outside the rhythm (a hop's landing, a climber's hand): the other foot, a fresh take.</summary>
        public StepShape Next(FootGait gait)
        {
            _leftFoot = !_leftFoot;
            _lastTake = FootstepRules.NextTake(_lastTake, _rng.NextDouble());
            return FootstepRules.Shape(gait, _leftFoot, _lastTake, _rng.NextDouble(), _rng.NextDouble());
        }

        /// <summary>A landing just played its own footfall: the next step comes one walk interval later, not at once.</summary>
        public void Landed()
        {
            _idle = 0f;
            _timer = FootstepRules.WalkInterval;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#3095: the little things a Guardian robot does while it stands about alone.</summary>
    public enum RobotFlourishKind : byte
    {
        /// <summary>Nothing.</summary>
        None,

        /// <summary>Both claws come up in front and the head bends to look at them, turning them this way and that.</summary>
        LookAtHands,

        /// <summary>The shoulders roll twice — the arms circle, the body sways with them.</summary>
        ShoulderRoll,

        /// <summary>The head sweeps far to one side, then the other, chin up — a slow scan of the surroundings.</summary>
        ScanAround,
    }

    /// <summary>
    /// One frame of a robot's idle flourish, in degrees, composed on top of the robot's own pose (the client turns each joint
    /// a little further by it): the head's pitch (positive = down) and yaw, each arm's pitch (negative = forward and up), its
    /// twist round its own length and its roll (Unity's z: positive swings the hanging claw toward the body's right, +x), and
    /// the body's sideways roll. All 0 at rest.
    /// </summary>
    public readonly struct RobotFlourishPose
    {
        public RobotFlourishPose(float headPitch, float headYaw, float armPitchL, float armPitchR, float armTwistL, float armTwistR,
            float armRollL, float armRollR, float bodyRoll)
        {
            HeadPitch = headPitch;
            HeadYaw = headYaw;
            ArmPitchL = armPitchL;
            ArmPitchR = armPitchR;
            ArmTwistL = armTwistL;
            ArmTwistR = armTwistR;
            ArmRollL = armRollL;
            ArmRollR = armRollR;
            BodyRoll = bodyRoll;
        }

        public float HeadPitch { get; }

        public float HeadYaw { get; }

        public float ArmPitchL { get; }

        public float ArmPitchR { get; }

        public float ArmTwistL { get; }

        public float ArmTwistR { get; }

        public float ArmRollL { get; }

        public float ArmRollR { get; }

        public float BodyRoll { get; }

        /// <summary>True when the pose moves nothing.</summary>
        public bool IsRest => HeadPitch == 0f && HeadYaw == 0f && ArmPitchL == 0f && ArmPitchR == 0f && ArmTwistL == 0f
            && ArmTwistR == 0f && ArmRollL == 0f && ArmRollR == 0f && BodyRoll == 0f;

        /// <summary>This pose with every angle scaled by <paramref name="k"/> (the fade).</summary>
        public RobotFlourishPose Scaled(float k) => new RobotFlourishPose(HeadPitch * k, HeadYaw * k, ArmPitchL * k, ArmPitchR * k,
            ArmTwistL * k, ArmTwistR * k, ArmRollL * k, ArmRollR * k, BodyRoll * k);
    }

    /// <summary>
    /// #3095 ("a robot that's left alone might look at its hands sometimes"): a Guardian robot's idle flourishes, picked like
    /// the animals' (<c>CreatureAnimator</c>'s tail swat, ear flick and weight shift): only once the robot has stood still for
    /// <see cref="SettleSeconds"/>, then one every <see cref="MinGapSeconds"/>–<see cref="MaxGapSeconds"/>, never while it
    /// moves or hunts. When the robot stops being idle mid-flourish the pose fades out over <see cref="FadeSeconds"/> instead
    /// of snapping. Purely cosmetic; one per robot, seeded by its id so a robot's rhythm is its own. Unity-free so the
    /// headless suite pins it.
    /// </summary>
    public sealed class RobotIdle
    {
        /// <summary>Seconds a robot stands still before its first flourish.</summary>
        public const float SettleSeconds = 3f;

        /// <summary>The shortest pause between two flourishes.</summary>
        public const float MinGapSeconds = 4f;

        /// <summary>The longest pause between two flourishes.</summary>
        public const float MaxGapSeconds = 9f;

        /// <summary>Seconds a flourish takes to fade out when the robot moves off or starts to hunt.</summary>
        public const float FadeSeconds = 0.25f;

        private readonly Random _rng;
        private float _idleTime;
        private float _gap;
        private float _elapsed;
        private float _seconds;
        private float _weight;

        public RobotIdle(int seed)
        {
            _rng = new Random(seed);
            _gap = NextGap();
        }

        /// <summary>The flourish playing (or fading out), <see cref="RobotFlourishKind.None"/> when none.</summary>
        public RobotFlourishKind Current { get; private set; }

        /// <summary>How long a flourish lasts.</summary>
        public static float Seconds(RobotFlourishKind kind) => kind switch
        {
            RobotFlourishKind.LookAtHands => 1.8f,
            RobotFlourishKind.ShoulderRoll => 1.2f,
            RobotFlourishKind.ScanAround => 2.2f,
            _ => 0f,
        };

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds and returns the pose to compose this frame. <paramref name="idle"/> is
        /// true while the robot stands still, is not hunting, attacking or flinching.
        /// </summary>
        public RobotFlourishPose Step(float dt, bool idle)
        {
            if (dt <= 0f)
            {
                return Current == RobotFlourishKind.None ? default : PoseAt(Current, Progress).Scaled(_weight);
            }

            _idleTime = idle ? _idleTime + dt : 0f;
            if (Current != RobotFlourishKind.None)
            {
                _elapsed += dt;
                _weight = idle ? 1f : Math.Max(0f, _weight - (dt / FadeSeconds));
                if (_elapsed >= _seconds || _weight <= 0f)
                {
                    Current = RobotFlourishKind.None;
                    _gap = NextGap();
                    return default;
                }

                return PoseAt(Current, Progress).Scaled(_weight);
            }

            if (_idleTime < SettleSeconds)
            {
                return default;
            }

            _gap -= dt;
            if (_gap > 0f)
            {
                return default;
            }

            Begin((RobotFlourishKind)(1 + _rng.Next(3)));
            return default; // the pose starts at rest anyway
        }

        /// <summary>Starts <paramref name="kind"/> at once (tests; the client only uses <see cref="Step"/>).</summary>
        public void Begin(RobotFlourishKind kind)
        {
            Current = kind;
            _seconds = Seconds(kind);
            _elapsed = 0f;
            _weight = 1f;
            if (_seconds <= 0f)
            {
                Current = RobotFlourishKind.None;
            }
        }

        private float Progress => _seconds > 0f ? Math.Min(1f, _elapsed / _seconds) : 0f;

        private float NextGap() => MinGapSeconds + ((MaxGapSeconds - MinGapSeconds) * (float)_rng.NextDouble());

        /// <summary>The pose of <paramref name="kind"/> at progress <paramref name="t"/> (0..1); rest outside.</summary>
        public static RobotFlourishPose PoseAt(RobotFlourishKind kind, float t)
        {
            if (!(t > 0f && t < 1f))
            {
                return default;
            }

            return kind switch
            {
                RobotFlourishKind.LookAtHands => LookAtHands(t),
                RobotFlourishKind.ShoulderRoll => ShoulderRoll(t),
                RobotFlourishKind.ScanAround => ScanAround(t),
                _ => default,
            };
        }

        /// <summary>Claws up in front, turned inward, the head bent over them; the claws turn over twice while the head
        /// glances from one to the other.</summary>
        private static RobotFlourishPose LookAtHands(float t)
        {
            float env = HeldFlourishes.Envelope(t, 0.22f, 0.75f);
            float turn = (float)Math.Sin(2.0 * Math.PI * 2.0 * t) * 25f * env;
            float glance = (float)Math.Sin(2.0 * Math.PI * t) * 10f * env;
            return new RobotFlourishPose(28f * env, glance, -65f * env, -65f * env, turn, -turn, 14f * env, -14f * env, 0f);
        }

        /// <summary>Two circles of the shoulders, forward-up and round, the body swaying along.</summary>
        private static RobotFlourishPose ShoulderRoll(float t)
        {
            float env = (float)Math.Sin(Math.PI * t);
            double phase = 2.0 * Math.PI * 2.0 * t;
            float pitch = -16f * (float)Math.Sin(phase) * env;
            float roll = 9f * (1f - (float)Math.Cos(phase)) * 0.5f * env;
            return new RobotFlourishPose(-4f * env, 0f, pitch, pitch, 0f, 0f, -roll, roll, 3f * (float)Math.Sin(phase) * env);
        }

        /// <summary>Chin up, the head sweeps far to one side, then the other, and comes back to the front.</summary>
        private static RobotFlourishPose ScanAround(float t)
        {
            float env = HeldFlourishes.Envelope(t, 0.12f, 0.85f);
            float yaw = 55f * (float)Math.Sin(2.0 * Math.PI * t);
            return new RobotFlourishPose(-8f * env, yaw, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
        }
    }
}

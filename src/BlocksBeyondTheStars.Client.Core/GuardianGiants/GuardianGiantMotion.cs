// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using static BlocksBeyondTheStars.Client.GuardianGiantPose;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The guardian giant's motion set (#3676), Unity-free: one <see cref="GuardianGiantFrame"/> per move at a time into it.
    /// Every client evaluates the server's move at the same clock (<c>GuardianGiantStatus.MoveElapsed</c>), so the raised foot,
    /// the crouch, the glowing visor and the wind-up are on screen for exactly the move's warning and the blow lands at its
    /// <see cref="GuardianGiantMoveSpec.Impact"/> (<see cref="GuardianGiantRules.Spec"/>). The limbs are rigid (a block-built
    /// robot), so a crouch or a kneel tips the legs back and lowers the root by what the tipped leg no longer reaches.
    /// <para>Two poses are exact on purpose: <see cref="GuardianGiantMove.Rise"/> starts and <see cref="GuardianGiantMove.LieDown"/>
    /// and <see cref="GuardianGiantMove.Collapse"/> end in <see cref="GuardianGiantFrame.Lying"/> (every limb at rest, the root
    /// at the anchor) — the block body the server removes or stamps at that moment is cell for cell the model.</para>
    /// </summary>
    public static class GuardianGiantMotion
    {
        /// <summary>The walk's leg swing (degrees each way).</summary>
        public const float WalkAmp = 20f;

        /// <summary>When the thrown rock is torn from the ground and sits in the right hand (seconds into the throw).</summary>
        public const float RockGrab = 0.3f;

        /// <summary>How high a hop jumps (blocks, on top of the server's climb).</summary>
        public const float HopPeak = 2.5f;

        /// <summary>The kneel of a collapse (degrees the legs tip back).</summary>
        public const float KneelDegrees = 50f;

        /// <summary>The walk's gait cycles per second at <paramref name="speed"/> blocks/s (<see cref="CreatureGait.CycleRate"/>):
        /// a planted foot sweeps one <see cref="CreatureGait.StrideLength"/> while the body travels, and it is planted for the
        /// walk's duty factor of the cycle — so the rate is speed ÷ stride × duty, and the planted sole moves backwards at
        /// exactly the body's speed: it stays where it stands.</summary>
        public static float WalkCycleRate(float speed)
            => CreatureGait.CycleRate(speed, CreatureGait.StrideLength(LegLength, WalkAmp), CreatureGait.DutyFactor(Gait.Walk));

        /// <summary>The pose of <paramref name="move"/> <paramref name="t"/> seconds after it started. <paramref name="walk"/> is
        /// the gait phase (strides) the view advances by <see cref="WalkCycleRate"/>; <paramref name="moving"/> (0 … 1) blends
        /// the stride in over the idle stance.</summary>
        public static GuardianGiantFrame Evaluate(GuardianGiantMove move, float t, float walk, float moving)
        {
            switch (move)
            {
                case GuardianGiantMove.Rise:
                    return RiseFrame(t);
                case GuardianGiantMove.LieDown:
                    return RiseFrame(GuardianGiantRules.Spec(GuardianGiantMove.LieDown).Duration - t);
                case GuardianGiantMove.Idle:
                    return IdleFrame(t);
                case GuardianGiantMove.Walk:
                    return GuardianGiantFrame.Lerp(IdleFrame(t), WalkFrame(walk), Clamp01(moving));
                case GuardianGiantMove.Hop:
                    return HopFrame(t);
                case GuardianGiantMove.Stomp:
                    return StompFrame(t);
                case GuardianGiantMove.JumpAttack:
                    return JumpFrame(t);
                case GuardianGiantMove.Laser:
                    return LaserFrame(t);
                case GuardianGiantMove.Throw:
                    return ThrowFrame(t);
                case GuardianGiantMove.Summon:
                    return SummonFrame(t);
                case GuardianGiantMove.Search:
                    return SearchFrame(t);
                case GuardianGiantMove.Enrage:
                    return EnrageFrame(t);
                case GuardianGiantMove.Collapse:
                    return CollapseFrame(t);
                default:
                    return GuardianGiantFrame.Standing;
            }
        }

        /// <summary>How far the root sinks when the legs are tipped by <paramref name="legPitch"/> degrees: the rigid leg's
        /// sole stays on the ground.</summary>
        public static float TippedLegDrop(float legPitch)
            => -LegLength * (1f - (float)Math.Cos(legPitch * Math.PI / 180.0));

        /// <summary>Waking: it sits up on its arms (the first 40 %), then pushes up about its heels into the standing pose.
        /// Starts exactly lying (<see cref="GuardianGiantFrame.Lying"/>), ends exactly standing.</summary>
        private static GuardianGiantFrame RiseFrame(float t)
        {
            float d = GuardianGiantRules.Spec(GuardianGiantMove.Rise).Duration;
            float sit = Smooth(0f, 0.4f * d, t);
            float push = Smooth(0.33f * d, d, t);
            float hold = sit * (1f - push);
            var f = GuardianGiantFrame.Lying;
            f.Rise = push;
            f.TorsoPitch = 80f * hold;          // sits up, leaning back a little
            f.ArmLPitch = 55f * hold;           // the hands behind it on the ground, pushing
            f.ArmRPitch = 55f * hold;
            f.ArmLRoll = -12f * hold;
            f.ArmRRoll = 12f * hold;
            f.HeadPitch = -20f * hold;
            f.Shudder = 0.35f * Bump(0f, 0.25f * d, t);
            f.Flare = 1f - Smooth(0f, 0.5f * d, t);
            return f;
        }

        /// <summary>Standing: it breathes, sways its arms a little and scans with its head.</summary>
        private static GuardianGiantFrame IdleFrame(float t)
        {
            float breath = (float)Math.Sin(t * 1.4);
            var f = GuardianGiantFrame.Standing;
            f.RootY = -0.06f * (1f + breath);
            f.TorsoPitch = 1.5f * breath;
            f.HeadYaw = 28f * (float)Math.Sin(t * 0.45);
            f.HeadPitch = 4f * (float)Math.Sin((t * 0.7) + 1.0);
            f.ArmLPitch = -2f * breath;
            f.ArmRPitch = -2f * breath;
            f.ArmLRoll = -4f;
            f.ArmRRoll = 4f;
            return f;
        }

        /// <summary>The heavy stride at gait phase <paramref name="walk"/>: a biped's walk (<see cref="CreatureGait"/>), the
        /// arms swinging against the legs, the root lowered so the planted (rigid) leg's sole stays on the ground.</summary>
        private static GuardianGiantFrame WalkFrame(float walk)
        {
            float duty = CreatureGait.DutyFactor(Gait.Walk);
            var left = CreatureGait.Evaluate(walk + CreatureGait.PhaseOffset(Gait.Walk, 0, 0, 1), duty, WalkAmp);
            var right = CreatureGait.Evaluate(walk + CreatureGait.PhaseOffset(Gait.Walk, 1, 0, 1), duty, WalkAmp);
            var f = GuardianGiantFrame.Standing;
            f.LegLPitch = -left.SwingDeg;
            f.LegRPitch = -right.SwingDeg;
            f.LegLLift = 0.9f * left.Lift01;
            f.LegRLift = 0.9f * right.Lift01;
            // The straightest planted leg carries the hip: its sole is on the ground, a more tipped planted one hovers a hair
            // above it during the short double support (a rigid leg cannot reach lower) — none sinks in.
            float carry = float.MaxValue;
            if (left.Lift01 <= 0f)
            {
                carry = Math.Abs(left.SwingDeg);
            }

            if (right.Lift01 <= 0f)
            {
                carry = Math.Min(carry, Math.Abs(right.SwingDeg));
            }

            f.RootY = carry < float.MaxValue ? TippedLegDrop(carry) : 0f;
            f.ArmLPitch = -0.6f * right.SwingDeg;
            f.ArmRPitch = -0.6f * left.SwingDeg;
            f.ArmLRoll = -6f;
            f.ArmRRoll = 6f;
            double turn = walk * 2.0 * Math.PI;
            f.TorsoYaw = 5f * (float)Math.Sin(turn);
            f.TorsoRoll = 3f * CreatureGait.BodyRoll(walk, Gait.Walk);
            f.TorsoPitch = 6f;
            f.HeadPitch = -4f;
            f.HeadYaw = -3f * (float)Math.Sin(turn);
            return f;
        }

        /// <summary>A crouch of strength <paramref name="k"/> (0 … 1): the rigid legs tip back, the root sinks with them, the
        /// torso leans forward and the arms swing back.</summary>
        private static void Crouch(ref GuardianGiantFrame f, float k, float depth)
        {
            float leg = depth * k;
            f.LegLPitch += leg;
            f.LegRPitch += leg;
            f.RootY += TippedLegDrop(leg);
            f.TorsoPitch += 0.7f * leg;
            f.ArmLPitch += 1.2f * leg;
            f.ArmRPitch += 1.2f * leg;
        }

        /// <summary>A hop over a step: a short crouch (the warning), a jump <see cref="HopPeak"/> high, a landing dip.</summary>
        private static GuardianGiantFrame HopFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Hop);
            float takeOff = s.Warning, land = s.Impact;
            var f = GuardianGiantFrame.Standing;
            Crouch(ref f, Bump(0f, takeOff + 0.08f, t) + (0.8f * Bump(land - 0.02f, s.Duration, t)), 26f);
            float air = Bump(takeOff, land, t);
            if (t > takeOff && t < land)
            {
                float u = (t - takeOff) / (land - takeOff);
                f.RootY += 4f * HopPeak * u * (1f - u);
            }

            f.LegLPitch -= 18f * air;
            f.LegRPitch -= 18f * air;
            f.ArmLPitch -= 45f * air;
            f.ArmRPitch -= 45f * air;
            return f;
        }

        /// <summary>The stomp: the right foot rises high and forward (the warning), slams down at the impact, the giant leans
        /// into it and recovers.</summary>
        private static GuardianGiantFrame StompFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Stomp);
            float raised = Smooth(0f, 0.75f * s.Impact, t) * (1f - Smooth(0.85f * s.Impact, s.Impact, t));
            float lean = Bump(s.Impact, s.Impact + 0.4f, t);
            var f = GuardianGiantFrame.Standing;
            f.LegRPitch = -60f * raised;
            f.LegRLift = 1f * raised;
            f.TorsoPitch = (-8f * raised) + (10f * lean);
            f.ArmLRoll = -30f * raised;
            f.ArmRRoll = 30f * raised;
            f.ArmLPitch = -20f * raised;
            f.ArmRPitch = -20f * raised;
            f.Shudder = 0.3f * Bump(s.Impact, s.Impact + 0.3f, t);
            return f;
        }

        /// <summary>The jump attack: a deep crouch (the warning), a leap at <see cref="GuardianGiantRules.JumpTakeOff"/> with the
        /// arms thrown up (the view flies the root along <see cref="GuardianGiantFrame.Air"/>), a landing on the aim point at the
        /// impact and a recovering crouch.</summary>
        private static GuardianGiantFrame JumpFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.JumpAttack);
            float takeOff = GuardianGiantRules.JumpTakeOff;
            var f = GuardianGiantFrame.Standing;
            float crouch = (Smooth(0f, 0.8f * takeOff, t) * (1f - Smooth(takeOff - 0.05f, takeOff + 0.1f, t)))
                           + (0.8f * Bump(s.Impact, s.Duration, t));
            Crouch(ref f, crouch, 30f);
            if (t >= takeOff && t <= s.Impact)
            {
                f.Air = (t - takeOff) / Math.Max(0.01f, s.Impact - takeOff);
            }

            float arms = Smooth(takeOff, takeOff + 0.3f, t) * (1f - Smooth(s.Impact - 0.15f, s.Impact, t));
            f.ArmLPitch -= 150f * arms;
            f.ArmRPitch -= 150f * arms;
            float tuck = Bump(takeOff, s.Impact, t);
            f.LegLPitch -= 25f * tuck;
            f.LegRPitch -= 25f * tuck;
            f.Shudder = 0.3f * Bump(s.Impact, s.Impact + 0.3f, t);
            return f;
        }

        /// <summary>The visor laser: it turns its head to the aim and braces while the visor glows (the warning), the beam
        /// burns for <see cref="GuardianGiantRules.LaserBeamSeconds"/> from the impact on, then the glow fades.</summary>
        private static GuardianGiantFrame LaserFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Laser);
            float beamEnd = s.Impact + GuardianGiantRules.LaserBeamSeconds;
            float hold = Smooth(0f, 0.4f, t) * (1f - Smooth(beamEnd, s.Duration, t));
            var f = GuardianGiantFrame.Standing;
            f.LookAtAim = hold;
            f.Visor = t < s.Impact ? Smooth(0f, s.Warning, t) : 1f - Smooth(beamEnd, s.Duration, t);
            f.TorsoPitch = (-6f * hold) - (6f * Bump(s.Impact, s.Impact + 0.3f, t));
            f.ArmLRoll = -20f * hold;
            f.ArmRRoll = 20f * hold;
            f.ArmLPitch = 15f * hold;
            f.ArmRPitch = 15f * hold;
            f.Shudder = 0.15f * Bump(s.Impact, beamEnd, t);
            return f;
        }

        /// <summary>The throw: it bends down and tears a rock from the ground (from <see cref="RockGrab"/>), winds the right
        /// arm up behind its head, hurls it at <see cref="GuardianGiantRules.ThrowRelease"/> and follows through.</summary>
        private static GuardianGiantFrame ThrowFrame(float t)
        {
            float release = GuardianGiantRules.ThrowRelease;
            float grab = Smooth(0f, 0.35f, t) * (1f - Smooth(0.35f, 0.6f, t));
            float wind = Smooth(0.35f, release - 0.15f, t) * (1f - Smooth(release - 0.05f, release + 0.05f, t));
            float follow = Smooth(release - 0.05f, release + 0.1f, t) * (1f - Smooth(release + 0.4f, 2.2f, t));
            var f = GuardianGiantFrame.Standing;
            f.TorsoPitch = (35f * grab) - (12f * wind) + (18f * follow);
            f.TorsoYaw = (-25f * wind) + (15f * follow);
            f.ArmRPitch = (-45f * grab) - (200f * wind) - (70f * follow);
            f.ArmRRoll = 10f * wind;
            f.ArmLPitch = (-30f * grab) - (30f * wind);
            f.HeadPitch = (20f * grab) - (10f * wind);
            f.LookAtAim = wind + follow;
            return f;
        }

        /// <summary>The summons: both arms rise over the head, the lights flare to a pulse at the impact (the robots appear),
        /// the arms come down.</summary>
        private static GuardianGiantFrame SummonFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Summon);
            float up = Smooth(0f, 0.6f, t) * (1f - Smooth(s.Impact + 0.2f, s.Duration, t));
            var f = GuardianGiantFrame.Standing;
            f.ArmLPitch = -165f * up;
            f.ArmRPitch = -165f * up;
            f.ArmLRoll = -18f * up;
            f.ArmRRoll = 18f * up;
            f.HeadPitch = -22f * up;
            f.TorsoPitch = -6f * up;
            f.Flare = Smooth(0.3f, s.Impact, t) * (1f - Smooth(s.Impact, s.Impact + 0.6f, t));
            f.Shudder = 0.2f * Bump(s.Impact - 0.1f, s.Impact + 0.2f, t);
            return f;
        }

        /// <summary>The search: it stands, sweeps its head left and right with the search light and turns its shoulders with
        /// it.</summary>
        private static GuardianGiantFrame SearchFrame(float t)
        {
            var f = IdleFrame(t);
            f.HeadYaw = 55f * (float)Math.Sin(t * 0.9);
            f.HeadPitch = 14f;
            f.TorsoYaw = 15f * (float)Math.Sin((t * 0.9) - 0.5);
            f.ArmLPitch = -10f;
            f.ArmRPitch = -10f;
            f.SearchLight = Smooth(0f, 0.5f, t);
            return f;
        }

        /// <summary>The rage: it shudders, throws its arms out and up, lifts its head and its lights flare.</summary>
        private static GuardianGiantFrame EnrageFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Enrage);
            float k = Smooth(0f, 0.3f, t) * (1f - Smooth(s.Duration - 0.4f, s.Duration, t));
            var f = GuardianGiantFrame.Standing;
            f.ArmLPitch = -70f * k;
            f.ArmRPitch = -70f * k;
            f.ArmLRoll = -45f * k;
            f.ArmRRoll = 45f * k;
            f.HeadPitch = -28f * k;
            f.TorsoPitch = -10f * k;
            f.Shudder = k;
            f.Flare = k;
            return f;
        }

        /// <summary>The defeat: it sinks to its knees (the legs tip back), then falls backwards about its heels (gathering
        /// speed) into the lying pose at the corpse anchor, landing at the impact; a last small bounce settles it. Ends exactly
        /// lying.</summary>
        private static GuardianGiantFrame CollapseFrame(float t)
        {
            var s = GuardianGiantRules.Spec(GuardianGiantMove.Collapse);
            const float kneelEnd = 0.9f;
            float kneel = Smooth(0f, kneelEnd, t);
            float u = Clamp01((t - kneelEnd) / Math.Max(0.01f, s.Impact - kneelEnd));
            float fall = u * u; // it gathers speed as it falls
            float upright = kneel * (1f - fall);
            var f = GuardianGiantFrame.Lying;
            f.Rise = (1f - fall) + (0.04f * Bump(s.Impact, s.Impact + 0.35f, t));
            f.LegLPitch = KneelDegrees * upright;
            f.LegRPitch = KneelDegrees * upright;
            f.RootY = TippedLegDrop(KneelDegrees * upright);
            f.TorsoPitch = 22f * upright;
            f.HeadPitch = 25f * upright;
            f.ArmLPitch = -15f * upright;
            f.ArmRPitch = -15f * upright;
            f.Shudder = (0.5f * Bump(0f, 0.6f, t)) + (0.25f * Bump(s.Impact, s.Duration, t));
            return f;
        }
    }
}

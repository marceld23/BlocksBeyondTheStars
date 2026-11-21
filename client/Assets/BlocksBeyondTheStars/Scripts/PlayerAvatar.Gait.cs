// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively figure (#3882–#3886): the walk, the body and the joints of every humanoid figure — the own avatar, other
    /// players, villagers, bandits, the editor previews. The curves are <see cref="HumanGait"/>'s; this partial owns the rig
    /// they drive.
    /// <list type="bullet">
    /// <item><b>Body pivots</b> (#3883): <c>Hips</c> carries the pelvis and the legs, <c>UpperBody</c> (under it) the abdomen,
    /// chest, shoulders, arms, neck and head. Both are <i>origin pivots</i>: they sit at the root's origin and turn about a
    /// point (the hip joints, the waist) by moving as well as turning, so every part keeps the root-frame position it was
    /// built at — the 60-odd parts and gear plates that hang on the root need no new numbers, <see cref="RouteParent"/> just
    /// files each one under the pivot its height belongs to.</item>
    /// <item><b>Ankles</b> (#3884): an origin pivot under each knee turns the foot about the bottom of the shin; a boot's sole
    /// and toe follow it, its cuff stays on the shin.</item>
    /// <item><b>Joint blocks</b> (#3885) at knees and elbows, a touch darker than the limb (dark servo metal on a machine),
    /// painted with the limb when it is painted.</item>
    /// <item><b>Machines</b> (#3886) move with <see cref="HumanGait.MachineLiveliness"/>: stiffer than a villager, less stiff
    /// than the old rigid figure.</item>
    /// </list>
    /// Every pose with its own body (asleep, seated, climbing, crouched, gliding, swimming, in the air, sitting, defeated)
    /// eases the body motion out, so it looks as it did.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>The waist: the upper body turns about this height, and a part built at or above it hangs on it.</summary>
        private const float WaistY = 1.06f;

        /// <summary>A part hung on a knee whose centre is below this (knee frame) belongs to the foot: a sole, a toe cap.</summary>
        private const float FootPartBelowY = -0.38f;

        /// <summary>The arms swing this fraction of a stride after the legs.</summary>
        private const float ArmLag = 0.03f;

        private static readonly Vector3 HipsPoint = new Vector3(0f, AvatarArmourFit.HipY, 0f);
        private static readonly Vector3 WaistPoint = new Vector3(0f, WaistY, 0f);
        private static readonly Vector3 WaistBack = new Vector3(0f, WaistY, -0.12f); // the back of the waist: a bow hinges here
        private static readonly Vector3 AnklePoint = new Vector3(0f, -0.41f, 0f); // the bottom of the shin, knee frame

        /// <summary>The joints of a machine: dark servo metal.</summary>
        private static readonly Color ServoTone = new Color(0.30f, 0.32f, 0.36f);

        private Transform _hips, _upper, _ankleL, _ankleR;
        private Material _armJoint, _legJoint;
        private GaitStyle _gait = GaitStyle.Neutral;
        private float _liveliness = 1f;
        private bool _machine;
        private float _walkBody, _idleBody; // eased weights of the walking and the standing body motion
        private float _bowBend;             // the bow emote's bend at the waist this frame (PlayerAvatar.Feel.cs)
        private float _headSteadyPitch, _headSteadyYaw;
        private float _footLevel = 1f;      // 1 = the feet are held level with the ground, 0 = they hang relaxed

        /// <summary>One frame's body motion: the hips' side shift and dip, their roll and turn, the shoulders' net turn and
        /// the upper body's lean.</summary>
        private struct BodyPose
        {
            public float Shift, Drop, Roll, Yaw, Shoulders, Lean;
        }

        /// <summary>#3886: an android or a G.D.S. guardian — it moves stiffer than a villager (but no longer rigid) and its
        /// joints are dark servo metal. Call after <c>Build</c>.</summary>
        public void SetMachine(bool machine)
        {
            _machine = machine;
            _liveliness = machine ? HumanGait.MachineLiveliness : 1f;
            TintJoints();
        }

        /// <summary>#3885: the joints in their limb's colour, a touch darker — or a machine's servo metal. Called when the
        /// figure becomes a machine and whenever the limbs are recoloured (the editors, a remote player's new colours).</summary>
        private void TintJoints()
        {
            if (_armJoint != null)
            {
                _armJoint.color = ShaderColor.Srgb(_machine ? ServoTone : JointTone(_armsColor));
            }

            if (_legJoint != null)
            {
                _legJoint.color = ShaderColor.Srgb(_machine ? ServoTone : JointTone(_legsColor));
            }
        }

        /// <summary>#3886: how lively this figure moves (1 = a villager or a player, <see cref="HumanGait.MachineLiveliness"/>
        /// = a machine).</summary>
        public float Liveliness => _liveliness;

        /// <summary>#3885: a joint is the limb's colour, a touch darker.</summary>
        private static Color JointTone(Color limb) => new Color(limb.r * 0.78f, limb.g * 0.78f, limb.b * 0.78f, limb.a);

        private void BuildBodyPivots()
        {
            if (_hips != null)
            {
                return;
            }

            _hips = new GameObject("Hips").transform;
            _hips.SetParent(transform, false);
            _upper = new GameObject("UpperBody").transform;
            _upper.SetParent(_hips, false);
        }

        /// <summary>The pivot a part asked onto <paramref name="parent"/> really hangs on: a part of the root goes onto the
        /// upper body from the waist up and onto the hips below it; a part of a knee below the ankle onto that foot's ankle.
        /// The origin pivots keep the part's position meaningful in the frame it was given in.</summary>
        private Transform RouteParent(Transform parent, Vector3 localPos)
        {
            if (parent == transform && _upper != null)
            {
                return localPos.y >= WaistY ? _upper : _hips;
            }

            if (localPos.y < FootPartBelowY)
            {
                if (parent == _kneeL && _ankleL != null)
                {
                    return _ankleL;
                }

                if (parent == _kneeR && _ankleR != null)
                {
                    return _ankleR;
                }
            }

            return parent;
        }

        /// <summary>#3882: the walking limbs — the knee gives after the heel strike and folds in the swing, the arms swing a
        /// little after the opposite leg and bend at the elbow on their forward swing, and hang a little away from the body.
        /// A machine folds its knees and swings its arms a little less.</summary>
        private void WalkLimbs(float amp, float moving, float run, ref float armL, ref float armR, ref float elbowL,
            ref float elbowR, ref float kneeL, ref float kneeR, ref float rollL, ref float rollR)
        {
            float p = HumanGait.Phase(_walkPhase);
            float stiff = Mathf.Lerp(0.75f, 1f, _liveliness);
            kneeL = HumanGait.Knee(p, moving, run) * stiff;
            kneeR = HumanGait.Knee(p + 0.5f, moving, run) * stiff;

            float arms = amp * _gait.ArmSwing * Mathf.Lerp(0.7f, 1f, _liveliness);
            float ampL = arms * (1f + _gait.ArmAsymmetry), ampR = arms * (1f - _gait.ArmAsymmetry);
            float bias = 2f * moving; // a touch further forward than back
            armL = (ampL * HumanGait.Hip(p + 0.5f - ArmLag)) - bias; // the left arm swings with the right leg
            armR = (ampR * HumanGait.Hip(p - ArmLag)) - bias;
            elbowL = -HumanGait.Elbow(ampL > 0.01f ? -armL / ampL : 0f, moving, run, _gait.ElbowRest);
            elbowR = -HumanGait.Elbow(ampR > 0.01f ? -armR / ampR : 0f, moving, run, _gait.ElbowRest);
            float armOut = _gait.ArmOut * (1f - (0.5f * run));
            rollL = -armOut;
            rollR = armOut;
        }

        /// <summary>#3883: the body's motion this frame, written onto the hips and the upper body. The idle weight shift eases
        /// the free leg (<paramref name="legL"/> … <paramref name="kneeR"/>); <paramref name="legRoll"/> and
        /// <paramref name="legYaw"/> are what both legs take against the hips' roll, sway and turn, so the feet stay where
        /// they stand and step straight ahead.</summary>
        private void PoseBody(float dt, float t, float moving, float run, float amp, bool airborne, bool idle,
            ref float legL, ref float legR, ref float kneeL, ref float kneeR, out float legRoll, out float legYaw)
        {
            legRoll = legYaw = 0f;
            if (_hips == null || _upper == null)
            {
                return;
            }

            bool free = !_lying && !_climbing && !_seated && !_gliding && !_crouched && !airborne && !_swimming && !_sitting && !_down;
            float ease = dt > 0f ? 1f - Mathf.Exp(-dt * 6f) : 1f;
            _walkBody = Mathf.Lerp(_walkBody, free && !idle ? 1f : 0f, ease);
            _idleBody = Mathf.Lerp(_idleBody, free && idle ? 1f : 0f, ease);

            var walk = WalkBody(moving, run, amp);
            var stand = StandBody(t, ref legL, ref legR, ref kneeL, ref kneeR);
            float shift = (_walkBody * walk.Shift) + stand.Shift;
            float roll = (_walkBody * walk.Roll) + stand.Roll;
            float yaw = _walkBody * walk.Yaw;
            float shoulders = _walkBody * walk.Shoulders;
            float lean = (_walkBody * walk.Lean) + stand.Lean;

            // A chest piece plates the waist across the hips/upper-body seam (#3861): the armour stiffens the waist, so the
            // twist between hips and shoulders and the bow stay small enough that the plate and the body boxes under it do
            // not cut through each other.
            float waist = ChestPieceWorn ? ArmouredWaist : 1f;
            float twist = (shoulders - yaw) * waist;
            PoseAbout(_hips, HipsPoint, Quaternion.Euler(0f, yaw, roll), new Vector3(shift, -_walkBody * walk.Drop, 0f));
            PoseUpper(lean + (_bowBend * Mathf.Lerp(0.6f, 1f, waist)), twist, -0.85f * roll);
            _headSteadyPitch = 0.6f * lean; // the gaze stays level over a lean (not over a bow — that one means it)
            _headSteadyYaw = 0.8f * (yaw + twist);
            _bowBend = 0f;
            legRoll = -roll - (Mathf.Asin(Mathf.Clamp(shift / HumanGait.LegLength, -1f, 1f)) * Mathf.Rad2Deg);
            legYaw = -yaw; // the hips turn; the legs keep stepping straight ahead
        }

        /// <summary>How much of the waist's twist a chest piece leaves (its plate spans the hips/upper-body seam).</summary>
        private const float ArmouredWaist = 0.3f;

        /// <summary>The upper body's pose relative to the hips: it twists and rolls about the waist's centre, and leans or
        /// bows about the back of the waist — so a bow folds the belly into the pelvis instead of opening a notch at the back.
        /// The two turns combine into one origin-pivot pose: position = B + P·(C − B) − P·Q·C.</summary>
        private void PoseUpper(float pitch, float twist, float roll)
        {
            var q = Quaternion.Euler(0f, twist, roll); // about the centre C
            var p = Quaternion.Euler(pitch, 0f, 0f);   // about the back B
            _upper.localRotation = p * q;
            _upper.localPosition = WaistBack + (p * (WaistPoint - WaistBack)) - (p * q * WaistPoint);
        }

        /// <summary>The walking body: a dip at double support, the sway over the standing foot, the hips turning with the
        /// stride and the shoulders against them, the lean into the pace.</summary>
        private BodyPose WalkBody(float moving, float run, float amp)
        {
            float p = HumanGait.Phase(_walkPhase);
            float k = HumanGait.WalkIn(moving) * _liveliness;
            float turn = HumanGait.HipTurn(p);
            return new BodyPose
            {
                Shift = HumanGait.Sway(p) * (0.02f + (0.01f * (1f - moving))) * (1f - (0.6f * run)) * k,
                Drop = HumanGait.BodyDrop(p, amp, HumanGait.LegLength, moving, run, _gait.Bounce, _liveliness),
                Roll = -3f * Mathf.Sin(2f * Mathf.PI * p) * (1f - (0.5f * run)) * k, // the swinging leg's hip drops a little
                Yaw = (3f + (3f * run)) * turn * k,
                Shoulders = -(3f + (4f * run)) * turn * k,
                Lean = (_gait.Lean + (2f * moving) + (8f * run)) * k,
            };
        }

        /// <summary>The standing body: the weight on one leg for a while — the hips over it, the other hip lower, that knee
        /// eased and its foot a little forward — then over to the other; a breath in the upper body.</summary>
        private BodyPose StandBody(float t, ref float legL, ref float legR, ref float kneeL, ref float kneeR)
        {
            float k = _idleBody * _liveliness;
            float s = HumanGait.IdleShift(t, _gait) * k; // +1 = the weight on the left leg
            if (s > 0f)
            {
                kneeR += 9f * s;
                legR -= 3f * s;
            }
            else
            {
                kneeL -= 9f * s;
                legL += 3f * s;
            }

            return new BodyPose { Shift = -0.03f * s, Roll = -2.5f * s, Lean = 0.8f * Mathf.Sin(t * 1.5f) * k };
        }

        /// <summary>#3884: the feet. Held level with the ground whenever the figure stands on something (walking it rolls off
        /// them: toes up at the heel strike, the heel up at push-off); hanging relaxed in the water, under the glider, in the
        /// air and in bed. <paramref name="shinL"/>/<paramref name="shinR"/> are each shin's pitch (thigh + knee).</summary>
        private void PoseAnkles(float dt, float moving, float run, bool airborne, float shinL, float shinR)
        {
            if (_ankleL == null || _ankleR == null)
            {
                return;
            }

            bool level = !_gliding && !airborne && !_lying && !(_swimming && !_crouched);
            float ease = dt > 0f ? 1f - Mathf.Exp(-dt * 8f) : 1f;
            _footLevel = Mathf.Lerp(_footLevel, level ? 1f : 0f, ease);
            float p = HumanGait.Phase(_walkPhase);
            float roll = _walkBody * Mathf.Lerp(0.6f, 1f, _liveliness);
            float relaxed = 22f * (1f - _footLevel);
            float ankleL = (((HumanGait.FootPitch(p, moving, run) * roll) - shinL) * _footLevel) + relaxed;
            float ankleR = (((HumanGait.FootPitch(p + 0.5f, moving, run) * roll) - shinR) * _footLevel) + relaxed;
            PoseAbout(_ankleL, AnklePoint, Quaternion.Euler(Mathf.Clamp(ankleL, -35f, 55f), 0f, 0f), Vector3.zero);
            PoseAbout(_ankleR, AnklePoint, Quaternion.Euler(Mathf.Clamp(ankleR, -35f, 55f), 0f, 0f), Vector3.zero);
        }

        /// <summary>Turns an origin pivot by <paramref name="turn"/> about <paramref name="point"/> (its parent's frame) and
        /// moves it by <paramref name="shift"/>.</summary>
        private static void PoseAbout(Transform pivot, Vector3 point, Quaternion turn, Vector3 shift)
        {
            pivot.localRotation = turn;
            pivot.localPosition = point - (turn * point) + shift;
        }
    }
}

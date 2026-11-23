// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3095: the held item's flourish on the third-person figure, so other players see it too — once a draw has brought a
    /// drill, a gun or a blade into the hand (<see cref="HeldFlourishes"/>): the arm comes up to show it, a drill's bit spins
    /// up, a gun is racked, a blade spins round its length; a blade is twirled again now and then while its holder stands
    /// idle (a robber with its blade too). A swing, a blow, a gesture or a stance ends it at once — it is never in the way.
    /// The moving parts are the held model's own (<see cref="HeldSpin"/>), which also runs a drill's bit while it mines.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        // The flourish's arm: the upper arm a little forward, the forearm up in front of the chest, the item held level.
        private const float FlourishUpperArm = -45f, FlourishElbow = -45f;

        // #3134: the free (left) arm racking a gun — reached forward and across the chest so the hand lands on the gun's
        // slide in front of the right shoulder (yaw turns the arm toward the right side), then drawn back about 5 cm along
        // the gun with the pull: the upper arm sinks back while the elbow bends further, which keeps the hand at the slide's
        // height.
        private const float OffHandUpperArm = -68f, OffHandElbow = -15f, OffHandYaw = 38f;
        private const float OffHandPullUpperArm = 16f, OffHandPullElbow = -30f;

        private readonly HeldFlourish _flourish = new HeldFlourish();
        private HeldFlourishKind _flourishKind;
        private HeldSpin _heldSpin;
        private float _carryTip; // degrees the carried item tips up from level in the hand (#3763, HeldStances.CarryTip)
        private HeldStance _heldStance;

        // Justus 2026-11-23: a drill with a bit mines with the arm held steady ahead, the item level — no chop. The blend eases
        // in and out, and a held mouse button re-swings before it falls, so the arm stays put while the drill runs.
        private const float DrillUpperArm = -78f, DrillElbow = -12f, DrillBlendRate = 14f;
        private float _drillHold;

        /// <summary>A new held model is about to be built (<see cref="BuildHeld"/>): the old one's flourish and moving parts
        /// go with it.</summary>
        private void OnHeldRebuilding(HeldItem.Kind kind)
        {
            _flourish.Cancel();
            _heldSpin = null;
            _drillHold = 0f;
            _heldStance = HeldStances.For(kind.ToString());
            _carryTip = HeldStances.CarryTip(_heldStance); // #3763: a blade and a pick are carried tipped up
            _flourishKind = kind == HeldItem.Kind.Gloves || _fightGloves ? HeldFlourishKind.None : HeldFlourishes.ForKind(kind.ToString());
        }

        /// <summary>The use motion of the right arm while a swing runs: the chop — raise, then drive down with an elbow snap —
        /// or, for a drill whose bit spins (<see cref="HeldStances.HoldsStillWhileUsed"/>), the arm held steady ahead.</summary>
        private void PoseSwing(float dt, ref float armR, ref float elbowR, ref float level)
        {
            bool still = HeldStances.HoldsStillWhileUsed(_heldStance, _heldSpin != null && _heldSpin.HasBit);
            _drillHold = Mathf.MoveTowards(_drillHold, still && _swingTimer > 0f ? 1f : 0f, DrillBlendRate * dt);
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                if (!still)
                {
                    float c = 1f - Mathf.Clamp01(_swingTimer / SwingDuration);
                    armR = Mathf.Lerp(-115f, 30f, Mathf.SmoothStep(0f, 1f, c));
                    elbowR = -(20f + 45f * Mathf.Sin(Mathf.Clamp01(c) * Mathf.PI));
                }
            }

            if (_drillHold > 0f)
            {
                armR = Mathf.Lerp(armR, DrillUpperArm, _drillHold);
                elbowR = Mathf.Lerp(elbowR, DrillElbow, _drillHold);
                level = Mathf.Max(level, _drillHold);
            }
        }

        /// <summary>Starts the held item's flourish (if it has one) — after a draw, or an idle blade's twirl. Not on a hidden
        /// figure (the own avatar in first person, where the viewmodel shows it) and not with reduced effects.</summary>
        private void BeginHeldFlourish(float volume = 1f)
        {
            if (_held == null || !_visible || UiKit.ReducedMotion || !_flourish.Begin(_flourishKind))
            {
                return;
            }

            string cue = HeldFlourishes.Cue(_flourishKind);
            if (cue != null && _handR != null)
            {
                ClientAudio.Instance?.At(cue, _handR.position, Random.Range(0.95f, 1.06f), HeldFlourishes.CueVolume * volume);
            }
        }

        private void CancelHeldFlourish()
        {
            if (_flourish.Active)
            {
                _flourish.Cancel();
                _heldSpin?.SetTwirl(0f);
            }
        }

        /// <summary>The flourish over the pose underneath (<see cref="Animate"/>): the arm lifts the item into view, level,
        /// while its parts move; #3134: for a gun's rack the left arm reaches across to the slide and pulls with it. Anything
        /// else the arms do — a swing, a blow, a gesture, the rope, a robber's stance, a pose with its own arms — cuts it
        /// off.</summary>
        private void PoseHeldFlourish(float dt, bool idle, ref float armR, ref float elbowR, ref float level,
            ref float armL, ref float elbowL, ref float yawL)
        {
            bool busy = _swingTimer > 0f || _punchTimer > 0f || Gesturing || _roped || _stance != Stance.None
                || _lying || _climbing || _seated || _gliding || _swap.Current != HeldSwap.Phase.Idle || _pendingBuild || _held == null;
            if (busy)
            {
                CancelHeldFlourish();
            }

            if (!_flourish.Active && _flourish.IdleDue(dt, idle && !busy, _flourishKind, Random.value))
            {
                BeginHeldFlourish(volume: 0.6f); // an idle twirl is quieter than the draw's — a guard nearby does it now and then
            }

            if (!_flourish.Active)
            {
                return;
            }

            _flourish.Step(dt);
            var pose = _flourish.Pose;
            float lift = pose.Lift;
            armR = Mathf.Lerp(armR, FlourishUpperArm, lift);
            elbowR = Mathf.Lerp(elbowR, FlourishElbow, lift);
            level = Mathf.Max(level, lift);
            float off = pose.OffHand;
            if (off > 0f)
            {
                float pull = pose.OffHandPull;
                armL = Mathf.Lerp(armL, OffHandUpperArm + (OffHandPullUpperArm * pull), off);
                elbowL = Mathf.Lerp(elbowL, OffHandElbow + (OffHandPullElbow * pull), off);
                yawL = Mathf.Lerp(yawL, OffHandYaw, off);
            }

            if (_heldSpin != null)
            {
                _heldSpin.SetTwirl(pose.Twirl);
                if (pose.BitDriven)
                {
                    _heldSpin.Drive(0.1f);
                }
            }
        }
    }
}

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

        /// <summary>A new held model is about to be built (<see cref="BuildHeld"/>): the old one's flourish and moving parts
        /// go with it.</summary>
        private void OnHeldRebuilding(HeldItem.Kind kind)
        {
            _flourish.Cancel();
            _heldSpin = null;
            _flourishKind = kind == HeldItem.Kind.Gloves || _fightGloves ? HeldFlourishKind.None : HeldFlourishes.ForKind(kind.ToString());
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

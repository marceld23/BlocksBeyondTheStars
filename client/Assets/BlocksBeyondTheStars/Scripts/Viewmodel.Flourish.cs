// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3095 ("when you take a drill in your hand you should see your hand and arm and a short test drill … ranged weapons
    /// reload once, knife-like weapons are twirled"): the first-person flourish of the item just drawn — a drill comes up
    /// and its bit spins up and winds down, a gun is tipped and racked (cosmetic: there is no ammunition), a blade is
    /// flicked up and spun round its length; a held blade is twirled again now and then while the hand is idle. The curves
    /// are <see cref="HeldFlourishes"/> (Client.Core); the bit and the twirl are the model's own parts (<see cref="HeldSpin"/>),
    /// which also run a drill's bit while it mines. Using the item ends a flourish at once (<see cref="Swing"/>), and nothing
    /// ever waits for one — the server does not know it exists. #3134: the gun's rack is done by the free hand, which comes
    /// up on the left holder for it (<see cref="PoseOffHand"/>).
    /// </summary>
    public sealed partial class Viewmodel
    {
        private const float IdleSpeed = 0.5f;   // the view moving slower than this (m/s) counts as standing
        private const float BuzzDepth = 0.0025f; // the drill's buzz in the hand while its bit runs (camera space)

        // #3134: the free hand on the gun's slide. Where its fist takes hold, in the gun holder's own frame (metres — the
        // kind's gun body runs z −0.05..0.19 there, its top at y 0.036): over the back of the slide, a little to its left.
        private static readonly Vector3 OffHandGrip = new Vector3(-0.015f, 0.065f, 0.01f);

        private const float OffHandRack = 0.05f; // how far back along the gun the rack pulls the fist (gun frame, metres)

        // The fist's centre in the left holder: the climbing hand's fist (HeldItem.BuildClimbHand) at the item scale.
        private static readonly Vector3 OffHandFist = new Vector3(0f, 0f, 0.07f * ItemScale);

        // The hand on the slide (camera-local degrees): knuckles tipped up so the forearm runs down out of view, turned in
        // toward the gun.
        private static readonly Vector3 OffHandEuler = new Vector3(-40f, 30f, 10f);

        // Where the hand comes up from and drops back to (camera space, from the slide): below and left, out of view, tipped
        // forward.
        private static readonly Vector3 OffHandAway = new Vector3(-0.10f, -0.32f, -0.05f);
        private static readonly Vector3 OffHandAwayTilt = new Vector3(30f, -10f, 0f);

        private readonly HeldFlourish _flourish = new HeldFlourish();
        private HeldFlourishKind _flourishKind;
        private HeldSpin _spin;
        private bool _offHandShown; // #3134: the left climbing hand is up on the gun's slide

        /// <summary>A new model is in the hand (<see cref="BuildHeld"/>): its moving parts, and which flourish it plays.</summary>
        private void AttachFlourish(HeldItem.Kind kind)
        {
            _flourish.Cancel();
            _offHandShown = false; // #3134: BuildHeld's ApplyVisible puts the left holder and its hand back where they belong
            _flourishKind = HeldFlourishes.ForKind(kind.ToString());
            _spin = HeldSpin.Attach(_mesh, kind);
        }

        /// <summary>Starts the item's flourish and its sound — when a draw has just brought it up, or a blade's idle twirl.</summary>
        private void BeginFlourish(float volume = 1f)
        {
            if (!SwapShown || !_flourish.Begin(_flourishKind))
            {
                return;
            }

            string cue = HeldFlourishes.Cue(_flourishKind);
            if (cue != null)
            {
                ClientAudio.Instance?.Cue(cue, HeldFlourishes.CueVolume * volume);
            }
        }

        /// <summary>Ends a flourish at once (the item is used, the hand is hidden or busy).</summary>
        private void CancelFlourish()
        {
            if (_flourish.Active)
            {
                _flourish.Cancel();
                _spin?.SetTwirl(0f);
            }

            HideOffHand();
        }

        /// <summary>Lays the flourish over the right hand's pose (camera-local offsets on the rest pose), turns the blade and
        /// drives the bit. Anything that takes the hand — a swing, a switch, the rope, the climb — ends it.</summary>
        private void PoseFlourish(float dt, float speed, ref Vector3 pos, ref Vector3 rot)
        {
            bool busy = _swingTimer > 0f || _swap.Current != HeldSwap.Phase.Idle || _pendingBuild || _climbBlend > 0f || _roped
                || _kind == HeldItem.Kind.Gloves || UiKit.ReducedMotion;
            if (busy)
            {
                CancelFlourish();
            }

            if (!_flourish.Active && _flourish.IdleDue(dt, !busy && speed < IdleSpeed, _flourishKind, Random.value))
            {
                BeginFlourish(volume: 0.6f); // the idle twirl is quieter than the draw's
            }

            if (!_flourish.Active)
            {
                HideOffHand();
                return;
            }

            _flourish.Step(dt);
            var pose = _flourish.Pose;
            pos += new Vector3(pose.Offset.X, pose.Offset.Y, pose.Offset.Z);
            rot += new Vector3(pose.Euler.X, pose.Euler.Y, pose.Euler.Z);
            if (pose.Buzz > 0f)
            {
                float t = Time.time;
                pos += new Vector3(Mathf.Sin(t * 97f), Mathf.Sin(t * 113f), 0f) * (BuzzDepth * pose.Buzz);
            }

            if (_spin != null)
            {
                _spin.SetTwirl(pose.Twirl);
                if (pose.BitDriven)
                {
                    _spin.Drive(0.1f);
                }
            }

            PoseOffHand(pose, pos, rot);
        }

        /// <summary>
        /// #3134 ("only with weapons the other hand that reloads is missing"): while a gun is racked the free hand comes up
        /// from below the view to the slide, pulls back with it and drops away again (<see cref="HeldFlourishPose.OffHand"/>,
        /// <see cref="HeldFlourishPose.OffHandPull"/>). It is the left climbing hand (<see cref="EnsureClimbHands"/>: the suit
        /// colour, the painting and the worn climbing gear, rebuilt when they change) on the left holder — neither is in use
        /// meanwhile, since the gloves and the climb never let a flourish play. <paramref name="pos"/> / <paramref name="rot"/>
        /// are the gun holder's pose this frame, already final: whatever would still move it (a swing, a switch, the rope,
        /// the climb) cancels the flourish.
        /// </summary>
        private void PoseOffHand(HeldFlourishPose pose, Vector3 pos, Vector3 rot)
        {
            if (pose.OffHand <= 0f)
            {
                HideOffHand();
                return;
            }

            if (EnsureClimbHands())
            {
                ApplyVisible(); // a freshly built pair is hidden where it does not belong (the right one sits on the gun's holder)
            }

            _offHandShown = true;
            SetActive(_holderL.gameObject, true);
            SetActive(_climbL, true);

            // The fist's place on the slide in camera space — on the gun holder, which is scaled with the FOV like the item —
            // racked back along the gun by the pull; it comes up to there from out of view.
            var grip = OffHandGrip + new Vector3(0f, 0f, -OffHandRack * pose.OffHandPull);
            var onSlide = Compensated(pos) + (Quaternion.Euler(rot) * (grip * _fovScale));
            var fist = Vector3.Lerp(onSlide + Compensated(OffHandAway), onSlide, pose.OffHand);
            var euler = Vector3.Lerp(OffHandEuler + OffHandAwayTilt, OffHandEuler, pose.OffHand);
            _holderL.localPosition = fist - (Quaternion.Euler(euler) * (OffHandFist * _fovScale));
            _holderL.localEulerAngles = euler;
        }

        /// <summary>#3134: the free hand goes (the rack is done, or the flourish was cut short): the left holder and the left
        /// climbing hand are back to what the gloves and the climb want of them — hidden otherwise.</summary>
        private void HideOffHand()
        {
            if (!_offHandShown)
            {
                return;
            }

            _offHandShown = false;
            ApplyVisible();
        }
    }
}

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
    /// ever waits for one — the server does not know it exists.
    /// </summary>
    public sealed partial class Viewmodel
    {
        private const float IdleSpeed = 0.5f;   // the view moving slower than this (m/s) counts as standing
        private const float BuzzDepth = 0.0025f; // the drill's buzz in the hand while its bit runs (camera space)

        private readonly HeldFlourish _flourish = new HeldFlourish();
        private HeldFlourishKind _flourishKind;
        private HeldSpin _spin;

        /// <summary>A new model is in the hand (<see cref="BuildHeld"/>): its moving parts, and which flourish it plays.</summary>
        private void AttachFlourish(HeldItem.Kind kind)
        {
            _flourish.Cancel();
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
        }
    }
}

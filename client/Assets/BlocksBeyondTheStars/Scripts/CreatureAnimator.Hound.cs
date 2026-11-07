// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Schuppenhund's body language (#3327). The gait itself needs nothing new — a four-legger already walks, trots and
    /// bounds (gallops) by speed through <c>CreatureGait</c>, and a charging hound runs fast enough to bound. On top of the
    /// standard poses, which set every rotation fresh each frame, this layer adds: the head held low while it stalks; the
    /// tail carried a little up at rest, low while it stalks, straight back (the sketch) while it charges, and wagging on a
    /// tamed hound; the ears pricked while it watches you and pinned back while it charges.
    /// </summary>
    public sealed partial class CreatureAnimator
    {
        private bool _hound;       // the rig is a hound
        private bool _owned;       // a tamed companion — it wags instead of hunting
        private float _houndHunt;  // 0..1 smoothed: hunting at speed (the charge)
        private float _houndProwl; // 0..1 smoothed: hostile and moving slowly (the stalk)

        /// <summary>Whether the creature is someone's companion (the view sets it every update).</summary>
        public void SetOwned(bool owned) => _owned = owned;

        private void PoseHound(float dt, float t, float moving)
        {
            if (!_hound)
            {
                return;
            }

            bool hunting = _hostile && !_owned && !_asleep;
            _houndHunt = Mathf.MoveTowards(_houndHunt, hunting && moving > 0.6f ? 1f : 0f, dt / 0.3f);
            _houndProwl = Mathf.MoveTowards(_houndProwl, hunting && moving > 0.05f && moving <= 0.6f ? 1f : 0f, dt / 0.5f);
            float awake = 1f - _rest;

            // The head drops while it stalks (positive pitch lowers it, as in the sleep pose).
            float dip = 14f * _houndProwl * awake;
            if (dip > 0.01f)
            {
                foreach (var head in _heads)
                {
                    if (head != null)
                    {
                        head.localRotation *= Quaternion.Euler(dip, 0f, 0f);
                    }
                }
            }

            // The tail's root link: positive pitch raises a tail that points backwards.
            if (_tail.Length > 0 && _tail[0] != null)
            {
                float pitch = Mathf.Lerp(Mathf.Lerp(16f, -22f, _houndProwl), 0f, _houndHunt) * awake;
                float wag = _owned && !_asleep ? Mathf.Sin(t * 11f) * 24f * (1f - 0.5f * moving) * awake : 0f;
                _tail[0].localRotation = Quaternion.Euler(pitch, wag, 0f) * _tail[0].localRotation;
            }

            // Ears: only where PoseEars ran this frame (it sets them fresh) — otherwise the extra pitch would add up.
            if (FaceDetail && _ears.Length > 0)
            {
                float watching = hunting ? 1f - _houndHunt : 0f;
                float earPitch = (-55f * _houndHunt + 10f * watching) * awake - 20f * _rest; // forward = +, laid back = −
                for (int i = 0; i < _ears.Length; i++)
                {
                    if (_ears[i] != null)
                    {
                        _ears[i].localRotation *= Quaternion.Euler(earPitch, 0f, 0f);
                    }
                }
            }
        }
    }
}

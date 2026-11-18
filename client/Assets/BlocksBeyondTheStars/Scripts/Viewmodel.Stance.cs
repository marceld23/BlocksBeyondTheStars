// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The held item's stance in first person (#3763 — Justus: "sometimes you do not hold weapons and tools properly").
    /// Every held model is built along +Z and the view had one rest pose for all of them, so a sword pointed into the screen
    /// like a gun and the pickaxe's handle hid its head. The rule is <see cref="HeldStances"/> (Client.Core): a blade rests
    /// tipped up, a pick further up — and a pick chops over the head instead of jabbing forward like a drill.
    /// </summary>
    public sealed partial class Viewmodel
    {
        private HeldStance _stance;
        private Vector3 _stanceTurn;  // added to RestEuler
        private Vector3 _stanceShift; // added to RestPos

        /// <summary>The kind of the item in the hand, and with it its stance (from <see cref="BuildHeld"/>).</summary>
        private void SetKind(HeldItem.Kind kind)
        {
            _kind = kind;
            _stance = HeldStances.For(kind.ToString());
            var turn = HeldStances.RestTurn(_stance);
            var shift = HeldStances.RestShift(_stance);
            _stanceTurn = new Vector3(turn.X, turn.Y, turn.Z);
            _stanceShift = new Vector3(shift.X, shift.Y, shift.Z);
        }

        /// <summary>The use motion of everything that is neither a blade, a gun, the gloves nor the bare hand: a pick's
        /// overhead chop (<see cref="HeldStances.ChopPitch"/>), else the forward-down jab of tools, drills and blocks.
        /// <paramref name="t"/> is the swing's progress 0..1, <paramref name="jab"/> its 0→1→0 bell.</summary>
        private void PoseJabOrChop(float t, float jab, bool aroundForearm, ref Vector3 posOff, ref Vector3 rot)
        {
            if (HeldStances.Chops(_stance))
            {
                // The head comes down and forward with the strike; round the forearm the item travels on a longer radius.
                float pitch = HeldStances.ChopPitch(t) * (aroundForearm ? 0.8f : 1f);
                posOff += new Vector3(-0.03f, -0.05f, 0.10f) * jab;
                rot += new Vector3(pitch, -6f * jab, 0f);
                return;
            }

            posOff += new Vector3(-0.05f, -0.06f, 0.12f) * jab;
            rot += new Vector3((aroundForearm ? 34f : 55f) * jab, -8f * jab, 0f);
        }
    }
}

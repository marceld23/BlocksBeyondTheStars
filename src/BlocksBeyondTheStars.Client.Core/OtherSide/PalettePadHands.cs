// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The gamepad's two hands while the melody bar holds its shoulder buttons (#3908), Unity-free. With the Resonance
    /// Blaster in hand the bar is open, and a shoulder button is the bar's "tones 5–8" modifier — so the pad's place and
    /// mine controls went quiet, and on the other side nothing could be shaped or dissolved with a pad at all. Here the
    /// two meanings of a shoulder button are told apart:
    /// <list type="bullet">
    /// <item><b>With the d-pad</b> it is the bar's modifier, as always: a d-pad direction pushed at any moment of the
    /// hold makes the whole hold a chord, and a chord never shapes or dissolves.</item>
    /// <item><b>Alone</b> it is the hand — the left one shapes (the pad's place control), the right one dissolves (its
    /// mine control): a short press is one pulse as the button is let go, a hold past <see cref="ChordGrace"/> pulses
    /// for as long as it lasts (the pace is <see cref="SoundShaper"/>'s). The grace gives a chord the time to become
    /// one before anything goes out.</item>
    /// <item><b>The triggers</b> are never the bar's modifier: where the player switched them on (they mine and place
    /// only then — their resting value differs between pad families), they shape and dissolve at once.</item>
    /// </list>
    /// </summary>
    public sealed class PalettePadHands
    {
        /// <summary>Seconds a shoulder button must be held alone before the hold counts as the hand — and the longest
        /// press that still is a tap.</summary>
        public const float ChordGrace = 0.25f;

        private Shoulder _left, _right;

        /// <summary>The pad asks for a shaping pulse this frame.</summary>
        public bool Shape { get; private set; }

        /// <summary>The pad asks for a dissolving pulse this frame.</summary>
        public bool Dissolve { get; private set; }

        /// <summary>
        /// One frame. <paramref name="leftShoulder"/> / <paramref name="rightShoulder"/>: the shoulder buttons are down.
        /// <paramref name="leftTrigger"/> / <paramref name="rightTrigger"/>: the triggers are pulled (false while the
        /// player has them switched off). <paramref name="dpadPushed"/>: the d-pad points somewhere.
        /// </summary>
        public void Update(float dt, bool leftShoulder, bool rightShoulder, bool leftTrigger, bool rightTrigger, bool dpadPushed)
        {
            Shape = _left.Update(dt, leftShoulder, dpadPushed) | leftTrigger;
            Dissolve = _right.Update(dt, rightShoulder, dpadPushed) | rightTrigger;
        }

        /// <summary>The bar does not hold the shoulder buttons this frame (it is closed, a menu is open …): nothing is
        /// asked for, and a button still down when the bar takes them again must be let go first — it was pressed for
        /// something else.</summary>
        public void Reset()
        {
            _left.Stale();
            _right.Stale();
            Shape = false;
            Dissolve = false;
        }

        /// <summary>One shoulder button's press: a chord or the hand.</summary>
        private struct Shoulder
        {
            private bool _down;
            private bool _chord;
            private float _heldFor;

            public bool Update(float dt, bool held, bool dpadPushed)
            {
                if (!held)
                {
                    bool tap = _down && !_chord && _heldFor < ChordGrace; // a short press alone: one pulse as it is let go
                    _down = false;
                    _chord = false;
                    _heldFor = 0f;
                    return tap;
                }

                if (!_down)
                {
                    _down = true;
                    _chord = dpadPushed; // pressed onto a pushed d-pad: the modifier
                    _heldFor = 0f;
                }
                else
                {
                    _chord |= dpadPushed;
                    if (dt > 0f)
                    {
                        _heldFor += dt;
                    }
                }

                return !_chord && _heldFor >= ChordGrace;
            }

            /// <summary>Whatever is down now belongs to something else until it is released.</summary>
            public void Stale()
            {
                _down = true;
                _chord = true;
                _heldFor = 0f;
            }
        }
    }
}

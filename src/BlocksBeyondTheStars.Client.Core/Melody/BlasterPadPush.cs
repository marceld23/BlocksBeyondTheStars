// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Resonance Blaster's push on a gamepad on our side (#3917), Unity-free. With the blaster in hand the melody bar
    /// is open, and the open bar holds the pad's d-pad and shoulder buttons (a shoulder is its "tones 5–8" modifier) — so
    /// <c>InputMap</c> drops the pad's place control there, and a pad could not push at all. Here the one press that is
    /// plainly a push is told apart from the bar's modifier:
    /// <list type="bullet">
    /// <item><b>A short tap of the left shoulder alone</b> — let go within <see cref="TapSeconds"/>, the d-pad untouched at
    /// every moment of it — is one push, as the button is let go.</item>
    /// <item><b>A longer hold never pushes</b>, with or without the d-pad: a player who holds the shoulder first and then
    /// reaches for the d-pad is playing the bar's upper tones, and a push then — aimed at a monument's rune stone — would
    /// open its sound chamber. (The other side's palette lets a long hold shape, <see cref="PalettePadHands"/>; a push is
    /// a single shot, so the tap is all it needs.)</item>
    /// <item><b>The left trigger</b> is never the bar's modifier: where the player switched the triggers on, pulling it is
    /// one push at once.</item>
    /// </list>
    /// The right shoulder stays the bar's: the pad does not mine with the blaster in hand (#3346 — the pillar in front of
    /// the player must never be mined while a tune is played).
    /// </summary>
    public sealed class BlasterPadPush
    {
        /// <summary>The longest press of the left shoulder that is still a tap — the other side's chord grace.</summary>
        public const float TapSeconds = PalettePadHands.ChordGrace;

        private bool _down;
        private bool _chord;
        private float _heldFor;
        private bool _trigger;

        /// <summary>The pad asks for one push this frame.</summary>
        public bool Pushed { get; private set; }

        /// <summary>
        /// One frame while the bar holds the shoulders. <paramref name="leftShoulder"/>: the left shoulder button is down.
        /// <paramref name="leftTrigger"/>: the left trigger is pulled (false while the player has the triggers switched
        /// off). <paramref name="dpadPushed"/>: the d-pad points somewhere.
        /// </summary>
        public void Update(float dt, bool leftShoulder, bool leftTrigger, bool dpadPushed)
        {
            bool tap = false;
            if (!leftShoulder)
            {
                tap = _down && !_chord && _heldFor < TapSeconds;
                _down = false;
                _chord = false;
                _heldFor = 0f;
            }
            else if (!_down)
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

            bool pulled = leftTrigger && !_trigger;
            _trigger = leftTrigger;
            Pushed = tap || pulled;
        }

        /// <summary>The bar does not hold the shoulders this frame (it is closed, the blaster is put away, a menu took
        /// the frames …): nothing is asked for, and a button or trigger still down when the reading starts again must be
        /// let go first — it was pressed for something else.</summary>
        public void Reset()
        {
            _down = true;
            _chord = true;
            _heldFor = 0f;
            _trigger = true;
            Pushed = false;
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Engine-free rules of the "which device is in hand" detection that picks the button glyphs (#2461). The
    /// client's <c>InputMap.ActiveDevice</c> and its sources read the hardware; the decisions that went wrong live
    /// here so the headless suite pins them.
    /// </summary>
    public static class InputDeviceRules
    {
        /// <summary>
        /// Whether the shared movement axes count as PAD activity (#2461, N7). The project's input manager merges the
        /// left stick and the WASD/arrow keys into the same "Horizontal"/"Vertical" axes, so a keyboard walk read as a
        /// pushed stick and flipped every glyph to the pad set whenever a pad was merely plugged in — the glyphs
        /// flickered between the two sets. The axes count only while no movement key is held, read raw (the smoothed
        /// keyboard axis keeps decaying for a moment after the key is released) and past the stick dead zone.
        /// </summary>
        public static bool SharedMoveAxesArePad(float rawX, float rawY, bool movementKeyHeld, float deadzone)
        {
            if (movementKeyHeld)
            {
                return false;
            }

            float dz = Math.Max(0f, deadzone);
            return Pushed(rawX, dz) || Pushed(rawY, dz);
        }

        private static bool Pushed(float value, float deadzone)
        {
            float magnitude = Math.Abs(value);
            return magnitude > 0f && magnitude >= deadzone;
        }
    }
}

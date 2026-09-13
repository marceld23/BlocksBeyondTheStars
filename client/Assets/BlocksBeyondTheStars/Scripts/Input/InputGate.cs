// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pure "may this input fire right now?" rules (#1858). Typing "E" into the feedback dialog used to
    /// dock the ship, "U" left the station and "V" flipped the camera: every gameplay verb polled its key
    /// through <see cref="InputMap"/>, which knew nothing about a focused text field, and only six UI screens
    /// asked <see cref="UiKit.TextFieldFocused"/> by hand. The decision now lives here, in one place with no
    /// Unity state, so an EditMode test can pin it: while the player is typing, ONLY the two menu verbs
    /// (Esc / Tab, which every dialog needs to close itself — and which the field itself consumes first)
    /// pass; everything else reads "not pressed".
    /// </summary>
    public static class InputGate
    {
        /// <summary>True for the two menu verbs — the only actions a focused text field must not swallow, or a
        /// dialog with a field in it could never be closed from the keyboard.</summary>
        public static bool IsMenuVerb(InputAction action)
            => action == InputAction.UiCancel || action == InputAction.UiMenu;

        /// <summary>Whether <paramref name="action"/> may fire while <paramref name="textFieldFocused"/> says the
        /// player is typing (a focused uGUI field or the pad's on-screen keyboard). Gameplay verbs are
        /// swallowed; the menu verbs pass unchanged — their own callers already defer to the field.</summary>
        public static bool Allows(InputAction action, bool textFieldFocused)
            => !textFieldFocused || IsMenuVerb(action);

        /// <summary>Whether the flight controls (cruise, EVA, the V camera toggle) may read input this frame.
        /// A menu-style dialog (Tab menu, chart, feedback), the ship-destruction prompt, the chat box and any
        /// other focused text field all hold the ship still — the chat was the gap: <c>SpaceView</c> never read
        /// <c>ChatTyping</c>, so "E" typed into the chat while flying docked the ship.</summary>
        public static bool FlightControlAllowed(bool menuOpen, bool awaitingRespawnConfirm, bool chatTyping, bool textFieldFocused)
            => !menuOpen && !awaitingRespawnConfirm && !chatTyping && !textFieldFocused;
    }
}

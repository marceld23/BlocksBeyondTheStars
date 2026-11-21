// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Keeps the menus' keyboard navigation out of play (#3921), Unity-free. uGUI's input module turns the move axes
    /// (W/A/S/D, the arrows, a pad's stick) into a walk over the selectable controls and Submit (Enter — and Space, by
    /// the project's input settings — or a pad's A) into a click on the selected one. A screen that hides by switching
    /// its canvas off keeps its controls alive, so a control left selected there was still walked and clicked while the
    /// player was playing: W moved the invisible selection in the hidden Tab menu up to its "Pause menu" button, and
    /// the next jump opened the pause menu. Two rules, applied by the cursor arbiter once a frame:
    /// <list type="bullet">
    /// <item><b>Back in play, nothing is selected</b> (<see cref="ClearsSelection"/>): the frame the cursor goes from
    /// free to locked — the last menu or cursor owner closed — the selection is dropped, whoever left it.</item>
    /// <item><b>In play the keyboard does not walk menus</b> (<see cref="NavigationWanted"/>): while the cursor is
    /// locked, the module sends no move and no submit events — unless a gamepad is the device in hand, whose chat
    /// keyboard and HUD panels are walked with the stick while the cursor stays locked (a pad's menus claim their
    /// selection only while they are shown, so nothing hidden is left for it to click).</item>
    /// </list>
    /// </summary>
    public static class UiPlayGuard
    {
        /// <summary>Whether the selection is dropped this frame: the cursor was free last frame and is locked now.</summary>
        public static bool ClearsSelection(bool cursorWasFree, bool cursorFree) => cursorWasFree && !cursorFree;

        /// <summary>Whether the input module sends move and submit events: always while the cursor is free (a menu, a
        /// dialog, a cursor owner), and in play only for a gamepad.</summary>
        public static bool NavigationWanted(bool cursorFree, bool gamepadActive) => cursorFree || gamepadActive;
    }
}

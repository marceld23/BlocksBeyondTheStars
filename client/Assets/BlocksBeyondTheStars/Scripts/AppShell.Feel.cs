// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shell's share of the feel pass: the game steps aside when the player does. Losing the window's focus in a
    /// world opens the pause menu (settings → Display, on by default) — the same menu Esc opens, with the same hold on
    /// the world, so nothing happens to a player who switched to another window. Desktop players only: the browser
    /// loses "focus" with every click beside the canvas, and an unattended run (tests, screenshots, the performance
    /// probe) has no player to protect.
    /// </summary>
    public sealed partial class AppShell
    {
        private void OnApplicationFocus(bool focused)
        {
            if (focused || Settings == null || !Settings.PauseOnFocusLoss || Phase != ShellPhase.InGame)
            {
                return;
            }

            if (Application.isEditor || Application.isBatchMode || Application.platform == RuntimePlatform.WebGLPlayer
                || !AutoQualityCalibrator.Enabled)
            {
                return;
            }

            // The same guards as Esc, and a player who is typing keeps the chat line: switching windows in the middle
            // of a sentence must not put the pause menu over it.
            var boot = Boot();
            if (PauseMenuBlocked(boot) || (boot != null && boot.ChatTyping))
            {
                return;
            }

            OpenPauseMenu(); // a no-op while it is already up
        }

        /// <summary>
        /// Whether something else owns the screen, so the pause menu must stay closed — for Esc and for the lost focus
        /// alike: an in-game menu or a modal (also one that took its input this very frame), the death prompt, the
        /// first-spawn prologue, the story-resolution cinematic or any other cutscene. The pause dialog (canvas order
        /// 60) would open unseen behind the death prompt (85) or a cutscene (67) and hold the world.
        /// </summary>
        private static bool PauseMenuBlocked(GameBootstrap boot)
            => boot != null && (boot.MenuOpen || boot.MenuInputHandledThisFrame || boot.AwaitingRespawnConfirm || boot.VegaPrologueActive
                || GameBootstrap.CutsceneActive
                || (boot.Views.Resolution != null && boot.Views.Resolution.Playing));
    }
}

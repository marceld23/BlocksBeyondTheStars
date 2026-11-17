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

            OpenPauseMenu(); // a no-op while it is already up
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Loading screen (`anf_textures.md` §4/§3.1): a brief overlay shown while the in-game
    /// world is set up, then hands off to <see cref="AppShell.LaunchGame"/>. A real progress
    /// bar is added once asset/world load reports progress.
    /// </summary>
    public sealed class LoadingScreen
    {
        private readonly AppShell _shell;
        private float _elapsed;

        /// <summary>How long to hold the loading screen before launching (raised when hosting a local server).</summary>
        public float MinShow = 0.6f;

        /// <summary>Time-based load progress 0..1 (no real asset/world progress reported yet).</summary>
        public float Progress => MinShow <= 0f ? 1f : Mathf.Clamp01(_elapsed / MinShow);

        public LoadingScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Loading)
            {
                _elapsed = 0f;
                return;
            }

            if (!_shell.ContentReady)
            {
                return;
            }

            // The browser singleplayer host boots asynchronously (cloud-save lookup, then worldgen) and
            // hands its in-memory wire to the rig at launch. Launching on the timer alone raced that boot
            // and left the rig without a wire, so the browser world "could not connect" (#771) — MinShow
            // is the minimum time the screen shows, never a deadline the world start has to beat.
            _elapsed += Time.deltaTime;
            if (_elapsed >= MinShow && !_shell.BrowserWorldBooting)
            {
                _elapsed = 0f;
                _shell.LaunchGame();
            }
        }

        public void Draw()
        {
            _shell.DrawBackground();
            GUI.Label(new Rect(Screen.width / 2f - 100, Screen.height / 2f - 12, 200, 24), _shell.L("ui.loading.title"));
        }
    }
}

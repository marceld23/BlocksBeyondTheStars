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

        /// <summary>Bar fill 0..1: the time ramp over MinShow, or the creeping hold band while the bundled
        /// server still generates its world (<see cref="LoadingHandoffPolicy.Progress"/>).</summary>
        public float Progress => LoadingHandoffPolicy.Progress(_elapsed, MinShow, _shell.LocalServerBooting);

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

            // MinShow is the minimum time the screen shows, never a deadline the world start has to beat:
            // the browser singleplayer host boots asynchronously (cloud-save lookup, then worldgen) and
            // hands its in-memory wire to the rig at launch — launching on the timer alone raced that boot
            // and left the rig without a wire (#771). The bundled desktop server likewise generates a fresh
            // world for 10–20 s before it listens; handing off earlier only swapped this screen for the
            // rig's nameless "Loading world…" curtain for the rest of the boot (#1800). A server that never
            // reports ready is given up on at the policy's ceiling instead of pinning the screen forever.
            _elapsed += Time.deltaTime;
            switch (LoadingHandoffPolicy.Next(_elapsed, MinShow, _shell.BrowserWorldBooting, _shell.LocalServerBooting))
            {
                case LoadingHandoffPolicy.Verdict.Launch:
                    _elapsed = 0f;
                    _shell.LaunchGame();
                    break;

                case LoadingHandoffPolicy.Verdict.GiveUp:
                    _elapsed = 0f;
                    _shell.AbortLocalServerBoot();
                    break;
            }
        }

        public void Draw()
        {
            _shell.DrawBackground();
            GUI.Label(new Rect(Screen.width / 2f - 100, Screen.height / 2f - 12, 200, 24), _shell.L("ui.loading.title"));
        }
    }
}

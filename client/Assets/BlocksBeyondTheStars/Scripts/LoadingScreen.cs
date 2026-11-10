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

        /// <summary>#3261: time per frame the shader warm-up may take while the screen waits for the bundled server — the
        /// bar keeps moving, and a new world's 10–20 s boot leaves plenty of frames for the ~100 variants.</summary>
        private const double WarmUpBudgetMs = 6.0;

        /// <summary>Seconds the screen is up before the warm-up starts, so the screen itself appears without a hitch.</summary>
        private const float WarmUpDelaySeconds = 0.25f;

        /// <summary>How long to hold the loading screen before launching (raised when hosting a local server).</summary>
        public float MinShow = 0.6f;

        /// <summary>#3261: true for the one frame between the launch verdict and <see cref="AppShell.LaunchGame"/>. That frame
        /// is rendered with the status row saying "Building the world…", so the blocking rig build that follows freezes
        /// on the right words instead of the server's last stage.</summary>
        public bool HandingOff { get; private set; }

        /// <summary>Bar fill 0..1: the time ramp over MinShow, or the creeping hold band while the bundled
        /// server — or, in the browser, the in-process host (#3450: the bar used to sit at 100 % for that whole boot) —
        /// still builds its world (<see cref="LoadingHandoffPolicy.Progress"/>).</summary>
        public float Progress => LoadingHandoffPolicy.Progress(_elapsed, MinShow, _shell.LocalServerBooting || _shell.BrowserWorldBooting,
            _shell.LocalServerBootProgress);

        public LoadingScreen(AppShell shell) => _shell = shell;

        public void Update()
        {
            if (_shell.Phase != ShellPhase.Loading)
            {
                _elapsed = 0f;
                HandingOff = false;
                return;
            }

            if (!_shell.ContentReady)
            {
                return;
            }

            if (HandingOff)
            {
                HandingOff = false;
                _elapsed = 0f;
                _shell.LaunchGame(); // the previous frame showed "Building the world…" — the rig build runs behind it
                return;
            }

            // #3261: the client's own preparation used to wait for the server and then run in one block behind the world
            // veil. Shader variants need no world — compile them a few per frame while the server generates.
            if (_shell.LocalServerBooting && _elapsed >= WarmUpDelaySeconds)
            {
                ShaderPrewarm.WarmUpSome(WarmUpBudgetMs);
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
                    HandingOff = true; // one rendered frame with the client-prep line first, then LaunchGame (above)
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

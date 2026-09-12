// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When the shell loading screen (the progress bar) hands off to the in-game rig, and what the bar
    /// shows while it waits. Pure so an EditMode test can pin it; <see cref="LoadingScreen"/> feeds it the
    /// timers and <see cref="AppShell"/> the two boot gates.
    ///
    /// Desktop singleplayer spawns the bundled server, which generates a FRESH world before it listens —
    /// 10–20 s on a fast machine (Player.log 2026-09-12: 10.6 s rainbow sea, 20.2 s coral sea with two
    /// settlements). The bar used to run 0→100 % in 2.5 s and launch regardless, so the player then stared
    /// at the rig's nameless "Loading world…" curtain for the whole server boot: the world name only
    /// arrives with the join, and the join cannot happen before the server is up (#1800). The bar screen
    /// now holds until the launcher relays the server's "started on port" line — the gate the browser
    /// in-process host has had since #771 — and the curtain only covers the join and the first chunks.
    /// </summary>
    public static class LoadingHandoffPolicy
    {
        public enum Verdict { Hold, Launch, GiveUp }

        /// <summary>Ceiling on the server-boot hold: a process that lives but never reports ready would
        /// otherwise pin the loading screen forever. Same figure as the connect loop's local budget.</summary>
        public const float LocalBootCeilingSeconds = ConnectRetryPolicy.LocalBudgetSeconds;

        /// <summary>While the server boots the bar creeps from the floor toward the cap (never reaching it),
        /// so it visibly keeps working instead of parking at "100 %"; the ready signal snaps it to full.</summary>
        public const float HoldFloor = 0.60f;
        public const float HoldCap = 0.85f;
        public const float HoldTauSeconds = 12f;

        /// <param name="elapsed">Seconds the loading screen has been up.</param>
        /// <param name="minShow">Minimum on-screen time before any hand-off (the shell's MinShow).</param>
        /// <param name="browserBooting">The WebGL in-process host is still bringing its world up (#771).</param>
        /// <param name="localBooting">The bundled desktop server was spawned and has not reported ready.</param>
        public static Verdict Next(float elapsed, float minShow, bool browserBooting, bool localBooting)
        {
            if (elapsed < minShow || browserBooting)
            {
                return Verdict.Hold;
            }

            if (localBooting)
            {
                return elapsed >= LocalBootCeilingSeconds ? Verdict.GiveUp : Verdict.Hold;
            }

            return Verdict.Launch;
        }

        /// <summary>Bar fill 0..1: the plain time ramp when nothing holds the launch, the creeping hold band
        /// while the bundled server boots (monotonic in <paramref name="elapsed"/>, strictly below
        /// <see cref="HoldCap"/>), and back to the ramp — i.e. 100 % — the moment the server reports ready.</summary>
        public static float Progress(float elapsed, float minShow, bool localBooting)
        {
            float ramp = minShow <= 0f ? 1f : Clamp01(elapsed / minShow);
            if (!localBooting)
            {
                return ramp;
            }

            float creep = (HoldCap - HoldFloor) * (1f - (float)Math.Exp(-Math.Max(0f, elapsed) / HoldTauSeconds));
            return Math.Min(ramp, HoldFloor) + creep;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

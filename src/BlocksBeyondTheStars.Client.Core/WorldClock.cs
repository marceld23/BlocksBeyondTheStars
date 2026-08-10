// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's own clock for things it animates itself, which STANDS STILL while the server is holding the
    /// world for the singleplayer pause menu.
    ///
    /// <para>
    /// Issue #612 made the Esc menu actually stop the simulation, but only on the server: the hold is a server
    /// intent because singleplayer runs the bundled server as a separate process. Everything the client simulates
    /// on its own kept running regardless — insects swarming, rain falling, creatures calling and animating — so a
    /// held world still looked and sounded alive. Those systems read this clock instead of <c>Time.deltaTime</c> /
    /// <c>Time.time</c>, and freeze with the world.
    /// </para>
    ///
    /// Pure (no UnityEngine) so it lives in Client.Core and is unit-tested headless; the Unity layer sets the pause
    /// flag from <c>PauseState</c> and advances it once per frame.
    /// </summary>
    public sealed class WorldClock
    {
        /// <summary>True while the server is holding the world — world simulation must not advance.</summary>
        public bool Paused { get; private set; }

        /// <summary>Seconds to advance world simulation by this frame; always 0 while held.</summary>
        public float Delta { get; private set; }

        /// <summary>Monotonic count of UNPAUSED seconds. Timers scheduled against it (a creature's next idle
        /// call, say) survive a pause of any length instead of all firing at once on resume.</summary>
        public float Now { get; private set; }

        /// <summary>Applies the server's answer. Zeroing <see cref="Delta"/> on entry matters: a reader that runs
        /// before the next <see cref="Advance"/> would otherwise still see the last live frame's delta.</summary>
        public void SetPaused(bool paused)
        {
            Paused = paused;
            if (paused)
            {
                Delta = 0f;
            }
        }

        /// <summary>Advances the clock by one frame of real time and returns the world delta for it. Non-positive
        /// and non-finite real deltas contribute nothing, so a stalled or garbled frame can never rewind
        /// <see cref="Now"/> or teleport a simulation that is integrating against it.</summary>
        public float Advance(float realDeltaSeconds)
        {
            bool usable = realDeltaSeconds > 0f && !float.IsNaN(realDeltaSeconds) && !float.IsInfinity(realDeltaSeconds);
            Delta = Paused || !usable ? 0f : realDeltaSeconds;
            Now += Delta;
            return Delta;
        }
    }
}

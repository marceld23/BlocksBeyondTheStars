// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Debug = UnityEngine.Debug;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3261: what the client spends between "the server is ready" and the first world frame — measured, not guessed.
    /// A player's log showed ~2 s there (09:20:06 → 09:20:08) with nothing saying where it went. <see cref="AppShell.LaunchGame"/>
    /// starts a measurement, each step notes its milliseconds (rig build, atlas, shader warm-up, visor RT), and the
    /// world's first <c>Update</c> writes it all as ONE line:
    /// <c>[LaunchTiming] rig=310ms atlas=1ms shaders=12ms(3 left, 104 while waiting) visor=40ms total=520ms</c>.
    /// Main thread only.
    /// </summary>
    public static class LaunchTiming
    {
        private static readonly StringBuilder Line = new StringBuilder(160);
        private static readonly Stopwatch Total = new Stopwatch();
        private static bool _open;

        /// <summary>Starts a new measurement (a world launch); a previous one that never flushed is dropped.</summary>
        public static void Begin()
        {
            Line.Clear();
            Total.Restart();
            _open = true;
        }

        /// <summary>Notes one step's duration (ignored outside a measurement — a travel-time rebuild, an editor).</summary>
        public static void Note(string step, double milliseconds, string detail = null)
        {
            if (!_open)
            {
                return;
            }

            Line.Append(' ').Append(step).Append('=')
                .Append(milliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append("ms");
            if (!string.IsNullOrEmpty(detail))
            {
                Line.Append('(').Append(detail).Append(')');
            }
        }

        /// <summary>Writes the measurement once and closes it — called from the world's first frame.</summary>
        public static void Flush()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Total.Stop();
            Debug.Log("[LaunchTiming]" + Line + " total="
                      + Total.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + "ms");
        }
    }
}

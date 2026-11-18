// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The worlds a player has landed on in this session, for the music (Unity-free so the suite can hold it). The first
    /// landing on each body of a session opens with the sunrise track (#1174). The musical accent for "another world
    /// reached" plays <b>once per session</b> (#3795): on the first landing on a second body. It used to play on every
    /// further body too — on a tour through a system the same fanfare came again and again and stopped meaning
    /// anything. A new session (<see cref="Reset"/>: back in the main menu) starts the count afresh.
    /// </summary>
    public sealed class SessionArrivals
    {
        private readonly HashSet<string> _bodies = new HashSet<string>();
        private bool _accentPlayed;

        /// <summary>How many different bodies were landed on in this session.</summary>
        public int Count => _bodies.Count;

        /// <summary>A new session: nothing visited, the accent not yet played.</summary>
        public void Reset()
        {
            _bodies.Clear();
            _accentPlayed = false;
        }

        /// <summary>The player stands on <paramref name="body"/>. Returns true if that is its first visit in this session
        /// (the sunrise track opens it); <paramref name="accent"/> is true exactly once per session — on the first
        /// arrival on a second body.</summary>
        public bool Arrive(string body, out bool accent)
        {
            accent = false;
            if (string.IsNullOrEmpty(body) || !_bodies.Add(body))
            {
                return false;
            }

            if (_bodies.Count > 1 && !_accentPlayed)
            {
                _accentPlayed = true;
                accent = true;
            }

            return true;
        }
    }
}

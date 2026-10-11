// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The one shared "a cutscene owns the screen" state (#2461). Before it, every overlay asked its own question —
    /// the HUD hid under the staged prologue, nothing hid under the ending, and the touch controls (sorting order 100),
    /// the chat, the nameplates and the IMGUI overlays stayed on top of every cinematic. Now each cinematic registers a
    /// REASON while it plays (the prologue, the ending, the shell intro and later the cutscene director), and every
    /// overlay reads one answer: <see cref="Active"/>.
    /// <para>
    /// A set of reasons, not one bool: two cinematics that overlap (a late ending under a running prologue, a director
    /// scene chained into the next) must not clear each other — the state ends only when the LAST reason ends.
    /// <see cref="Begin"/> and <see cref="End"/> are idempotent per reason, so an owner may call them from every path.
    /// </para>
    /// Unity-free so the rule is covered by plain .NET tests; the client holds the one live instance
    /// (<c>GameBootstrap.Cutscenes</c>).
    /// </summary>
    public sealed class CutsceneGate
    {
        /// <summary>The first-spawn VEGA prologue (the staged orbit or its dim + panel fallback).</summary>
        public const string Prologue = "prologue";

        /// <summary>The story resolution: resolution card, credits roll, epilogue.</summary>
        public const string Resolution = "resolution";

        /// <summary>The shell intro between the title splash and the main menu.</summary>
        public const string Intro = "intro";

        private readonly HashSet<string> _reasons = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True while at least one cinematic owns the screen.</summary>
        public bool Active => _reasons.Count > 0;

        /// <summary>How many reasons are open right now.</summary>
        public int Count => _reasons.Count;

        /// <summary>Whether <paramref name="reason"/> is one of the open reasons.</summary>
        public bool IsActive(string reason) => reason != null && _reasons.Contains(reason);

        /// <summary>True while a cinematic OTHER than <paramref name="reason"/> owns the screen — for an overlay that is
        /// itself the presenter of one cinematic (VEGA's panel shows the prologue's text) but must yield to every other.</summary>
        public bool ActiveOtherThan(string reason) => _reasons.Count > (IsActive(reason) ? 1 : 0);

        /// <summary>Opens <paramref name="reason"/>. Returns true when it was not open yet.</summary>
        public bool Begin(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException("A cutscene reason needs a name.", nameof(reason));
            }

            return _reasons.Add(reason);
        }

        /// <summary>Closes <paramref name="reason"/>. Returns true when it was open. Closing a reason that is not open is a
        /// no-op, so an owner can call it from every exit path (finish, skip, teardown).</summary>
        public bool End(string reason) => reason != null && _reasons.Remove(reason);

        /// <summary><see cref="Begin"/> or <see cref="End"/> by a flag — for owners that compute "am I showing" per frame.</summary>
        public void Set(string reason, bool on)
        {
            if (on)
            {
                Begin(reason);
            }
            else
            {
                End(reason);
            }
        }

        /// <summary>Drops every reason — the world that ran the cinematics is gone.</summary>
        public void Clear() => _reasons.Clear();
    }
}

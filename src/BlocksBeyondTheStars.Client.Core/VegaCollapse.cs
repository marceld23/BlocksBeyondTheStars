// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When VEGA's speech panel folds into a small tab (#2126). Since #1011 a fully revealed line waits for the continue
    /// control only — no auto-dismiss, because slow readers missed lines — and a young tester who never pressed it had the
    /// panel on screen for 13–26 minutes. Now a fully revealed, non-prologue page that nobody has advanced for
    /// <see cref="CollapseAfterSeconds"/> collapses to a tab ("VEGA" + the waiting lines); the continue control or a click
    /// on the tab opens it again with the text intact, and the timer starts over. Nothing is ever dismissed unread.
    /// Unity-free so the rule is covered by plain .NET tests; <c>VegaPanel</c> owns the drawing.
    /// </summary>
    public sealed class VegaCollapse
    {
        /// <summary>Seconds a revealed page stays open without input before it folds away.</summary>
        public const float CollapseAfterSeconds = 45f;

        /// <summary>Whether the panel is folded into the tab right now.</summary>
        public bool Collapsed { get; private set; }

        /// <summary>How long the current page has waited, revealed, for the player (seconds the HUD was in view).</summary>
        public float IdleSeconds { get; private set; }

        /// <summary>A new page starts (or the panel empties): open, and the wait starts from zero.</summary>
        public void Reset()
        {
            Collapsed = false;
            IdleSeconds = 0f;
        }

        /// <summary>The player opened the tab again: open, and the page gets a fresh wait before it may fold again.</summary>
        public void Expand() => Reset();

        /// <summary>
        /// Advances the wait by one frame and returns true on the frame the panel should fold. Only a fully revealed page
        /// waits (a page still typing is being read); a prologue story page never folds (it has its own staging and
        /// Esc-to-skip); a hidden HUD (a menu over it) pauses the wait — the player could not read the line then.
        /// </summary>
        public bool Tick(float deltaSeconds, bool fullyRevealed, bool prologue, bool hudHidden)
        {
            if (Collapsed)
            {
                return false;
            }

            if (!fullyRevealed || prologue)
            {
                IdleSeconds = 0f;
                return false;
            }

            if (hudHidden || deltaSeconds <= 0f || float.IsNaN(deltaSeconds))
            {
                return false;
            }

            IdleSeconds += deltaSeconds;
            if (IdleSeconds < CollapseAfterSeconds)
            {
                return false;
            }

            Collapsed = true;
            return true;
        }

        /// <summary>The number the tab shows: the line on screen plus every line queued behind it.</summary>
        public static int WaitingLines(int queued) => 1 + Math.Max(0, queued);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The rows of the right pane of "My worlds" (<c>UiSaveSelect</c>): the New-world form above, the hosting row ("Open to
    /// friends", under both views) and the footer below. Pane-local pixels in the 1920 × 1080 reference, y down. Unity-free,
    /// so the headless suite holds the rows apart (<c>MyWorldsLayoutTests</c>).
    /// <para>#4039: the "World options…" button used to sit in a row of its own at y 470–516, and the hosting row's divider
    /// (<see cref="DividerY"/> = 506) ran through it: the mode cards, the Creative box and that button needed all of the 206 px
    /// between the cards and the divider, with no gap left. The button now sits beside the mode box, under the fourth card;
    /// the box and the mode hints take the width of the first three.</para>
    /// </summary>
    public static class MyWorldsLayout
    {
        /// <summary>The pane's width and the margin every row keeps from its edges.</summary>
        public const float PaneW = 1010f, Margin = 32f;

        /// <summary>The hosting row (under both views) and the thin divider over it.</summary>
        public const float LanY = 520f, DividerY = LanY - 14f;

        /// <summary>The footer row (Cancel + "Create &amp; play", the detail view's Delete).</summary>
        public const float FooterY = 664f;

        /// <summary>The four mode cards in one row (#3765).</summary>
        public const float ModeCardsY = 176f, ModeCardW = 229f, ModeCardH = 124f, ModeCardStep = 239f;

        /// <summary>Under the cards: the mode's own box (the Creative checklist, the Sandbox and Hard Survival hints) under
        /// the first three cards, with a gap to the cards over it.</summary>
        public const float ModeBoxGap = 12f;
        public const float ModeBoxY = ModeCardsY + ModeCardH + ModeBoxGap;
        public const float ModeBoxW = (3 * ModeCardStep) - (ModeCardStep - ModeCardW);
        public const float ModeBoxH = 160f;

        /// <summary>The Sandbox / Hard Survival hint text inside the mode box's room.</summary>
        public const float HintInset = 4f, HintH = 150f;

        /// <summary>"World options…": under the fourth card, as wide as it, level with the mode box.</summary>
        public const float OptionsX = Margin + (3 * ModeCardStep), OptionsY = ModeBoxY, OptionsW = ModeCardW, OptionsH = 46f;
    }
}

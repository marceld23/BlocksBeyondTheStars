// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The vertical layout of the HUD's right-hand column (#3217), in the HUD's top-left anchored reference space: the round
    /// compass with its three caption rows, then the clock panel, then the optional playtime panel. One place for these
    /// values, so a panel cannot slide under the compass captions again — from #1792 to #3217 the third caption row (the
    /// Guardian core, VEGA's goal or the net-fragment signal) sat exactly on the clock's text line and showed through it.
    /// </summary>
    public static class HudRightColumn
    {
        /// <summary>Top edge of the compass dial.</summary>
        public const float CompassY = 10f;

        /// <summary>Side of the (square) compass dial.</summary>
        public const float CompassSize = 120f;

        /// <summary>Top of the first caption row ("Ship 114 m") inside the compass rect — the rows hang below the dial.</summary>
        public const float CaptionTop = 100f;

        /// <summary>Height of one compass caption row.</summary>
        public const float CaptionRowH = 18f;

        /// <summary>The caption rows: ship, waypoint, and the row the core, VEGA's goal and the fragment signal share.</summary>
        public const int CaptionRows = 3;

        /// <summary>Gap between two stacked elements of the column.</summary>
        public const float Gap = 4f;

        /// <summary>Width of the clock and playtime panels.</summary>
        public const float PanelW = 200f;

        /// <summary>Height of the clock panel without its chip row: the readings line and the day bar with the season.</summary>
        public const float ClockH = 56f;

        /// <summary>The row the clock panel grows by while a weather or festival chip is shown (#3218).</summary>
        public const float ClockChipRowH = 18f;

        /// <summary>Height of the playtime panel (#3219: the session and the world's total on two lines).</summary>
        public const float PlaytimeH = 44f;

        /// <summary>The lowest HUD row the compass and its captions cover.</summary>
        public const float CompassBottom = CompassY + CaptionTop + (CaptionRows * CaptionRowH);

        /// <summary>Top of the clock panel: under the compass's last caption row.</summary>
        public const float ClockY = CompassBottom + Gap;

        /// <summary>Top of a compass caption row (0 = ship) inside the compass rect.</summary>
        public static float CaptionY(int row) => CaptionTop + (row * CaptionRowH);

        /// <summary>The clock panel's height with or without its chip row.</summary>
        public static float ClockHeight(bool chipRow) => ClockH + (chipRow ? ClockChipRowH : 0f);

        /// <summary>Top of the playtime panel: under the clock panel, whose height depends on the chip row.</summary>
        public static float PlaytimeY(bool chipRow) => ClockY + ClockHeight(chipRow) + Gap;
    }
}

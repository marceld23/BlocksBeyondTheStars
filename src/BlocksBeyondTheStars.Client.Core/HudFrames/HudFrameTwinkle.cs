// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Client.HudFrames
{
    /// <summary>
    /// The twinkle of a HUD frame (#3317, the garland's lights of #3320): every pixel of a twinkling palette index takes the
    /// next colour of the design's <see cref="HudFrameDesign.TwinkleColors"/> about twice a second, each index one colour
    /// further on than the one before it — so neighbouring lights of different bulbs never show the same colour and the
    /// string seems to run. A still frame (reduced effects) shows step 0 for good.
    /// </summary>
    public static class HudFrameTwinkle
    {
        /// <summary>Colour steps per second.</summary>
        public const double StepsPerSecond = 2.0;

        /// <summary>The colours for a design that names twinkling indices but no colours: warm white, gold, green, blue and
        /// a little red (red is the enemies' colour in this game — an accent, never the lead).</summary>
        public static readonly IReadOnlyList<string> DefaultColors = new[] { "#fff4d6", "#ffd34d", "#7cff8a", "#6cc8ff", "#ff7a7a" };

        /// <summary>The step of the cycle at <paramref name="seconds"/> (any clock); 0 while <paramref name="still"/>.</summary>
        public static int Step(double seconds, bool still)
            => still || double.IsNaN(seconds) || seconds <= 0 ? 0 : (int)(Math.Floor(seconds * StepsPerSecond) % int.MaxValue);

        /// <summary>The colours the design's twinkle cycles through: its own, else <see cref="DefaultColors"/>.</summary>
        public static IReadOnlyList<string> ColorsOf(HudFrameDesign design)
            => design.TwinkleColors.Count > 0 ? design.TwinkleColors : DefaultColors;

        /// <summary>Which of <see cref="ColorsOf"/> a pixel of <paramref name="paletteIndex"/> shows at
        /// <paramref name="step"/>, or -1 when that index does not twinkle.</summary>
        public static int ColorSlot(HudFrameDesign design, int paletteIndex, int step)
        {
            int slot = design.TwinkleSlot(paletteIndex);
            if (slot < 0)
            {
                return -1;
            }

            int count = ColorsOf(design).Count;
            return (int)(((long)slot + Math.Max(0, step)) % count);
        }

        /// <summary>A <c>#rrggbb</c> colour as 0xRRGGBB; false for anything else.</summary>
        public static bool TryParseRgb(string? hex, out int rgb)
        {
            rgb = 0;
            return HudFrameDesign.TryNormalizeColor(hex, out string normalized)
                && int.TryParse(normalized.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
        }
    }
}

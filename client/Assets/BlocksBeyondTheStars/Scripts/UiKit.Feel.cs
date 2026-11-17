// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public static partial class UiKit
    {
        /// <summary>The story text's own size factor, on top of <see cref="UserScale"/> (settings → Accessibility).</summary>
        public static float CaptionScale = 1f;

        /// <summary>A dark plate sits behind the story text (settings → Accessibility).</summary>
        public static bool CaptionBackdrop;

        private static Sprite _clickFrameSprite;

        /// <summary>The button shape as an outline: a white 3-pixel edge around a faint white fill, tinted by
        /// <see cref="UnityEngine.UI.Image.color"/> — what lights up when a button is clicked (<see cref="UiClickFx"/>).
        /// Same radius as <see cref="ButtonSprite"/>, so it sits exactly on a button's frame.</summary>
        public static Sprite ClickFrameSprite => _clickFrameSprite != null ? _clickFrameSprite
            : _clickFrameSprite = RoundedSprite(14, 3, new Color(1f, 1f, 1f, 0.16f), Color.white);
    }

    /// <summary>
    /// The interface's signal colours — "good" and "caution" — by the player's colour vision
    /// (<see cref="ColourVision"/>). The standard pair is green and amber; a red-green weakness gets blue and yellow,
    /// a blue-yellow weakness teal and rose: each pair differs in brightness as well as in hue. A signal that matters
    /// never rests on its colour alone (the comparison arrows, the icons, the words), but where a colour does carry
    /// meaning it should be one the player can tell apart. Screens pick the colours up when they are (re)built.
    /// </summary>
    public static class UiPalette
    {
        /// <summary>The palette in use (set from the settings).</summary>
        public static ColourVision Mode = ColourVision.Standard;

        /// <summary>"Good", "ready", "better".</summary>
        public static Color Ok => Mode switch
        {
            ColourVision.RedGreen => new Color(0.36f, 0.72f, 1.00f),   // clear blue
            ColourVision.BlueYellow => new Color(0.25f, 0.88f, 0.70f), // teal
            _ => new Color(0.35f, 0.95f, 0.55f),
        };

        /// <summary>"Caution", "missing", "worse".</summary>
        public static Color Warn => Mode switch
        {
            ColourVision.RedGreen => new Color(1.00f, 0.86f, 0.25f),   // bright yellow
            ColourVision.BlueYellow => new Color(1.00f, 0.50f, 0.60f), // rose
            _ => new Color(1.00f, 0.72f, 0.28f),
        };
    }
}

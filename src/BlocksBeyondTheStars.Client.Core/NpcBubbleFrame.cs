// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The frame of an NPC's speech bubble (#3769 — Justus: "put a frame around the texts that float above them, like in
    /// Zelda — it is easier to see then"). A bubble was bare text with a drop shadow, hard to read over a bright wall or a
    /// busy floor. It now sits on a rounded dark plate with a light edge (the dialogue window's look), sized to the
    /// wrapped text: this is the size rule, Unity-free so a test can hold it. Name tags stay plain — a full market square
    /// would otherwise be a wall of boxes.
    /// </summary>
    public static class NpcBubbleFrame
    {
        /// <summary>The plate's padding left and right of the text, in reference pixels.</summary>
        public const float PadX = 14f;

        /// <summary>The plate's padding above and below the text.</summary>
        public const float PadY = 8f;

        /// <summary>The plate is never narrower than this — a "Hi!" still reads as a bubble, and the rounded corners
        /// (radius 18) never overlap.</summary>
        public const float MinWidth = 72f;

        /// <summary>
        /// The plate for a text whose single-line width is <paramref name="textWidth"/> and whose height, wrapped at
        /// <paramref name="wrapWidth"/>, is <paramref name="textHeight"/>: as wide as the text (at most the wrap width)
        /// plus the padding, as high as the wrapped lines (at least one line of <paramref name="lineHeight"/>) plus the
        /// padding.
        /// </summary>
        public static (float Width, float Height) Plate(float textWidth, float textHeight, float wrapWidth, float lineHeight)
        {
            float width = Math.Min(Math.Max(0f, textWidth), wrapWidth) + (2f * PadX);
            float height = Math.Max(textHeight, lineHeight) + (2f * PadY);
            return (Math.Max(MinWidth, width), height);
        }
    }
}

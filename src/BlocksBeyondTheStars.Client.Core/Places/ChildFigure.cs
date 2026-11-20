// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: how a child villager is drawn (the server's look key <see cref="Look"/>). A child is not a
    /// grown-up scaled down: the figure is small (the server's size, about 0.6) and its head is drawn larger than the scale
    /// alone would leave it, so the proportions read as a child's from across a plaza. Unity-free numbers the avatar and the
    /// nameplate use, so the headless tests pin them.
    /// </summary>
    public static class ChildFigure
    {
        /// <summary>The look key a child villager carries on the wire.</summary>
        public const string Look = "child";

        /// <summary>The grown-up figure's head: a cube of this edge, its middle this high over the feet (model units).</summary>
        public const float HeadSize = 0.46f;
        public const float HeadCentre = 1.86f;

        /// <summary>How much larger a child's head is drawn (in the model, before the figure's own scale).</summary>
        public const float HeadScale = 1.45f;

        /// <summary>A figure smaller than this share of a grown-up gets its nameplate bound to its height (grown-ups keep
        /// the fixed height they always had).</summary>
        public const float SmallFigure = 0.85f;

        /// <summary>The nameplate of a standing grown-up, over the feet.</summary>
        public const float AdultPlate = 2.1f;

        /// <summary>The gap between the top of a small figure's head and its nameplate.</summary>
        public const float PlateGap = 0.24f;

        /// <summary>Whether a look key is a child's.</summary>
        public static bool Is(string? look) => look == Look;

        /// <summary>The middle of a child's head in the model: the neck stays where it is, the head grows upward.</summary>
        public static float ChildHeadCentre => HeadCentre - (HeadSize / 2f) + (HeadSize * HeadScale / 2f);

        /// <summary>The top of the head in the model (a grown-up's, or a child's larger one).</summary>
        public static float Top(bool child)
            => child ? ChildHeadCentre + (HeadSize * HeadScale / 2f) : HeadCentre + (HeadSize / 2f);

        /// <summary>How high over the feet a standing figure's nameplate sits: the fixed height for a grown-up, just over
        /// the head for a child or any other small figure.</summary>
        public static float PlateHeight(float scale, bool child)
            => !child && (scale <= 0f || scale >= SmallFigure) ? AdultPlate : (Top(child) * scale) + PlateGap;
    }
}

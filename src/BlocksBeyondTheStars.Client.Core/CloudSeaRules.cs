// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The sea of clouds and the dome clouds at altitude (#3272) as Unity-free rules. Above the world's cloud band
    /// (<c>WorldEnvironment.CloudBaseY</c>..<c>CloudTopY</c>) a large horizontal noise sheet at the band's top shows the band
    /// from above — summits and towers rise out of a sea of clouds. It is visible only from above the band (inside it the haze
    /// shader's band term is the cloud), its holes follow the weather's cover (<see cref="CloudBandRules.Cover"/>: none on a
    /// clear day, patchy with clouds, nearly closed in rain), and it needs Medium or better on a desktop. The low dome
    /// billboards (<c>Clouds</c>) fade out while the camera climbs through the band, so they no longer hover overhead while
    /// you stand in a cloud; the high cirrus stay.
    /// </summary>
    public static class CloudSeaRules
    {
        /// <summary>How far above the band's top the deck is fully visible (metres).</summary>
        public const float ShowAbove = 6f;

        /// <summary>How far below the band's top the deck starts to fade in (metres) — just inside the band, where its fog
        /// still hides the deck.</summary>
        public const float HideBelow = 1f;

        /// <summary>The deck's noise: how many blocks one tile of the coarse octave spans.</summary>
        public const float NoiseTileBlocks = 240f;

        /// <summary>The deck's radius range (blocks) — it follows the camera and reaches toward the far plane.</summary>
        public const float MinRadius = 300f, MaxRadius = 2000f;

        /// <summary>The noise range over which a cloud's edge goes from clear to full (0..1 noise units).</summary>
        public const float EdgeSoftness = 0.18f;

        /// <summary>Whether a client may draw the deck at all: Medium or better, never in the browser.</summary>
        /// <param name="preset">The quality preset as its index: 0 Potato, 1 Low, 2 Medium, 3 High.</param>
        /// <param name="browser">A WebGL/WebGPU (or other light-device) client.</param>
        public static bool Allowed(int preset, bool browser) => !browser && preset >= 2;

        /// <summary>0..1 how much of the deck shows at a camera height: nothing without a band, nothing inside or below it,
        /// fading in from <see cref="HideBelow"/> under the top to fully visible <see cref="ShowAbove"/> above it.</summary>
        public static float DeckVisibility(float cameraY, float baseY, float topY)
        {
            if (!CloudBandRules.HasBand(baseY, topY))
            {
                return 0f;
            }

            return MistCardRules.SmoothStep(topY - HideBelow, topY + ShowAbove, cameraY);
        }

        /// <summary>0..1 how much of the LOW dome clouds stays at a camera height: all of them below the band's base, fading
        /// out while the camera climbs through the band, none above its top (you look down on the clouds there, they no
        /// longer hover overhead). 1 on a world without a band. The high cirrus are not affected.</summary>
        public static float DomeLowFade(float cameraY, float baseY, float topY)
        {
            if (!CloudBandRules.HasBand(baseY, topY))
            {
                return 1f;
            }

            return 1f - MistCardRules.SmoothStep(baseY, topY, cameraY);
        }

        /// <summary>The noise level (0..1) from which the deck is cloud: high (almost nothing) for a thin cover, low (closed)
        /// for a full one.</summary>
        public static float Threshold(float cover) => 0.8f - 0.62f * MistCardRules.Clamp01(cover);

        /// <summary>0..1 the deck's opacity at a cover: nothing on a clear day (cover ≤ 0.02), see-through for a thin cover,
        /// full from 0.3 up.</summary>
        public static float Opacity(float cover)
        {
            if (cover <= 0.02f)
            {
                return 0f;
            }

            return MistCardRules.SmoothStep(0.02f, 0.3f, cover);
        }

        /// <summary>The deck's radius for a camera far plane: 90 % of it, between <see cref="MinRadius"/> and
        /// <see cref="MaxRadius"/>.</summary>
        public static float Radius(float farClip) => Math.Max(MinRadius, Math.Min(MaxRadius, farClip * 0.9f));
    }
}

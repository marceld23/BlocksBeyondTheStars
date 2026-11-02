// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How bright a star lights its worlds (#1610, #3124). The star colour should TINT the world, not dim it: the ramp's
    /// cool and red anchors carry less luma than the sun-like one, so a fifth of all systems once lit every face darker all
    /// day long. The block light is lifted until its sRGB luma reaches 93 % of the sun-like anchor (≈ 85 % once the value
    /// goes linear) — the hue is untouched, and the sun disc / god-rays / grade keep the raw star colour.
    /// </summary>
    public sealed partial class Sky
    {
        /// <summary>sRGB luma of the star ramp's sun-like anchor (FFF1CE, <c>StarColors.SunLikeRgb</c>) — the reference the
        /// block light is normalised against.</summary>
        private const float SunLikeLuma = 0.947f;

        /// <summary>#3124: the most the lift may raise the block light. The red dwarf (FF7444) would need ×1.48, which pushes
        /// its red channel far above full brightness; capped, its world sits at ~88 % of the sun-like luma — a dim red sun.
        /// Every other anchor of the ramp needs less than this.</summary>
        private const float MaxStarLift = 1.3f;

        /// <summary>The factor the block light is multiplied by under <paramref name="star"/> (1 for the sun-like star).</summary>
        private static float StarLift(Color star)
        {
            float luma = 0.299f * star.r + 0.587f * star.g + 0.114f * star.b;
            return luma > 0.001f ? Mathf.Clamp(SunLikeLuma * 0.93f / luma, 1f, MaxStarLift) : 1f;
        }
    }
}

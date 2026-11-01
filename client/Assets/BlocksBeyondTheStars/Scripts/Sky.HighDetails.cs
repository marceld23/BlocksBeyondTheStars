// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The sun's soft-shadow quality per preset (#3054).</summary>
    public sealed partial class Sky
    {
        private UniversalAdditionalLightData _sunData;

        /// <summary>High on a desktop client softens the sun shadow with URP's High soft-shadow kernel; everything else
        /// keeps the asset's quality. The block shaders' plain <c>_SHADOWS_SOFT</c> keyword branches on the light's
        /// quality at runtime, so no shader variant is involved. Checked per frame — a cheap compare.</summary>
        private void ApplySunShadowQuality()
        {
            if (_sun == null || _sun.shadows == UnityEngine.LightShadows.None)
            {
                return;
            }

            _sunData ??= _sun.GetUniversalAdditionalLightData();
            var want = HighDetails.Active ? SoftShadowQuality.High : SoftShadowQuality.UsePipelineSettings;
            if (_sunData.softShadowQuality != want)
            {
                _sunData.softShadowQuality = want;
            }
        }
    }
}

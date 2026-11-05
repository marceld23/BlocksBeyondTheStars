// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The "Details (High)" switches (#3061) and the per-preset sun-shadow tier (#3054).</summary>
    public sealed partial class ClientSettings
    {
        // Details (High), epic #3066: one switch per desktop-class extra. They only take effect on the High preset of a
        // desktop client (HighDetails.Active); default on — the preset, not the player, decides who gets them.
        /// <summary>Parallax depth on rock, ore, brick and metal (#3055).</summary>
        public bool HighRelief = true;
        /// <summary>The dense grass carpet on meadows (#3056).</summary>
        public bool HighGrass = true;
        /// <summary>Flowers on meadows (#3056).</summary>
        public bool HighFlowers = true;
        /// <summary>Reflections inside ship and base rooms (#3057).</summary>
        public bool HighRoomReflections = true;
        /// <summary>Scorch marks and impact craters (#3058).</summary>
        public bool HighDecals = true;
        /// <summary>Volumetric sun light, including dust in rooms (#3060), and the headlamps' beams (#3232).</summary>
        public bool HighVolumetricLight = true;
        /// <summary>Headlamp shadows from the blocks around the player (#3233).</summary>
        public bool HighLampShadows = true;

        /// <summary>Shadow reach and the shadow map by preset (the single URP asset serves every level). Potato turns
        /// shadows off; High reaches furthest. High on a desktop client (#3054) adds two cascades — the asset's 4-split
        /// puts them at ~10/30/70/150 m, so the first cascade gets millimetre texels for crisp contact shadows — and
        /// reaches 150 m; the sun's soft-shadow quality follows in <see cref="Sky"/>. Medium and below cover 70 m or
        /// less, where a 2048 map is indistinguishable from 4096 at half the shadow render cost.</summary>
        private void ApplyShadowTier(UniversalRenderPipelineAsset urp)
        {
            bool desktopHigh = Preset == QualityPreset.High && HighDetails.DesktopClass;
            urp.shadowDistance = Preset switch
            {
                QualityPreset.Potato => 0f,
                QualityPreset.Low => 40f,
                QualityPreset.Medium => 70f,
                _ => desktopHigh ? 150f : 90f,
            };
            urp.shadowCascadeCount = desktopHigh ? 4 : 2;
            urp.mainLightShadowmapResolution = Preset >= QualityPreset.High ? 4096 : 2048;
        }
    }
}

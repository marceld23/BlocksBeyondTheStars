// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which dust the volumetric sun light (#3060) adds near the camera.</summary>
    public enum VolumetricDust
    {
        /// <summary>No dust: only the haze's own air (the distance veil and the height fog) carries the beams.</summary>
        None,

        /// <summary>Outdoors under a roof, an overhang, a cave ceiling or a canopy: a little dust, so the sun coming through
        /// a cave mouth or a gap in the leaves shows as a beam.</summary>
        Shelter,

        /// <summary>Aboard the ship or at home in the own base: the haze is off indoors, so the room's dust alone shows the
        /// sun coming through a window or an open door.</summary>
        Indoor,
    }

    /// <summary>What the volumetric sun light aims for this frame: its strength and the dust near the camera.</summary>
    public readonly struct VolumetricLightTarget
    {
        public VolumetricLightTarget(float strength, VolumetricDust dust)
        {
            Strength = strength;
            Dust = dust;
        }

        /// <summary>0 = off (the pass is not drawn once the fade reaches it) … 1 = full strength.</summary>
        public float Strength { get; }

        /// <summary>The dust near the camera.</summary>
        public VolumetricDust Dust { get; }

        /// <summary>Nothing to draw.</summary>
        public static VolumetricLightTarget Off => new VolumetricLightTarget(0f, VolumetricDust.None);
    }

    /// <summary>
    /// When the volumetric sun light (#3060, the "Details (High)" epic #3066) runs, Unity-free so the headless tests pin
    /// it. The Unity side (<c>VolumetricSunLight</c>) eases toward the target and maps the dust to a density.
    /// <list type="bullet">
    /// <item>Off without the player's switch on the High preset of a desktop client, in the flight view and behind a
    /// screen menu.</item>
    /// <item>Off where there is no air: space, a station, an airless world — unless the camera is indoors (the cabin out in
    /// space has air, and the sun still shines through its windows).</item>
    /// <item>Fades with the sun's height (off once it is below the horizon), under water and partly in bad weather.</item>
    /// <item>Off in the open air with the haze switched off: nothing would carry the light there.</item>
    /// </list>
    /// </summary>
    public static class VolumetricLightPolicy
    {
        /// <summary>Sun height (the y of the direction to the sun) at which the beams start to fade in.</summary>
        public const float SunFadeStart = 0f;

        /// <summary>Sun height at which the beams are at full strength (~5° above the horizon — the low morning sun is
        /// where they look best, so they are there early).</summary>
        public const float SunFadeFull = 0.08f;

        /// <summary>How much of the strength the worst weather takes away (an overcast storm sky has hardly any direct
        /// sun; the cloud shadows dim the beams on top of this).</summary>
        public const float WeatherDim = 0.6f;

        /// <summary>The target for this frame.</summary>
        /// <param name="enabled">The player's switch on the High preset of a desktop client (<c>HighDetails.VolumetricLight</c>).</param>
        /// <param name="spaceView">The flight view owns the camera.</param>
        /// <param name="menuOpen">A screen-level menu is open.</param>
        /// <param name="air">The camera is in a planet's air (the haze is active: not space, a station or an airless sky).</param>
        /// <param name="hazeOn">The player's distance-haze switch — the open air carries the beams only with it.</param>
        /// <param name="indoors">Aboard (not on a spacewalk) or at home in the own base.</param>
        /// <param name="sheltered">A roof, a cave ceiling or a canopy is overhead.</param>
        /// <param name="sunUp">The y of the world direction to the sun.</param>
        /// <param name="underwater">How far the camera is under water, 0..1 (eased).</param>
        /// <param name="weather">The weather intensity, 0..1.</param>
        public static VolumetricLightTarget TargetFor(bool enabled, bool spaceView, bool menuOpen, bool air, bool hazeOn,
            bool indoors, bool sheltered, float sunUp, float underwater, float weather)
        {
            if (!enabled || spaceView || menuOpen || (!air && !indoors))
            {
                return VolumetricLightTarget.Off;
            }

            var dust = indoors ? VolumetricDust.Indoor : sheltered ? VolumetricDust.Shelter : VolumetricDust.None;
            if (dust == VolumetricDust.None && !hazeOn)
            {
                return VolumetricLightTarget.Off;
            }

            float strength = SunFade(sunUp) * (1f - Clamp01(underwater)) * (1f - WeatherDim * Clamp01(weather));
            return strength > 0f ? new VolumetricLightTarget(strength, dust) : VolumetricLightTarget.Off;
        }

        /// <summary>0 with the sun at or below the horizon, rising smoothly to 1 at <see cref="SunFadeFull"/>.</summary>
        public static float SunFade(float sunUp)
        {
            float t = Clamp01((sunUp - SunFadeStart) / (SunFadeFull - SunFadeStart));
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}

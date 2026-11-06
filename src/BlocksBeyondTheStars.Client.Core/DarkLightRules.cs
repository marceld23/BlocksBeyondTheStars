// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How dark the dark gets (#3265) — the values <c>Sky</c> publishes as shader globals, Unity-free so the headless tests
    /// pin them. Caves and dug tunnels are dark (never pitch black): every lit surface keeps a small flat floor
    /// (<c>_Sc_DarkFloor</c>), which the "Visibility in the dark" setting raises; the models drawn with the fixed studio
    /// light (creatures, avatars, doors, held items — <c>LitColor</c>) follow the world's light in the world
    /// (<c>_Sc_ModelLight</c>) instead of glowing in a cave; and a room of the player's own base gets a gentle fill so a
    /// home without lamps is dim-cosy rather than cave-dark.
    /// </summary>
    public static class DarkLightRules
    {
        /// <summary>The flat floor every lit surface keeps at "Visibility in the dark" 0 / 1 (scene-linear, × albedo).</summary>
        public const float DarkFloorMin = 0.045f, DarkFloorMax = 0.15f;

        /// <summary>The interior fill in a room of the player's own base (aboard and on a station it is ~1).</summary>
        public const float HomeBaseFill = 0.3f;

        /// <summary>Linear luma of the world light (<c>_Sc_Light</c>) at noon under a sun-like star — the models' full light.</summary>
        public const float NoonLuma = 0.89f;

        /// <summary>The models' light in the dark at "Visibility in the dark" 0 / 1: under a moonless sky, and the most they
        /// keep while the camera is underground.</summary>
        public const float ModelDarkMin = 0.12f, ModelDarkMax = 0.4f, ModelCave = 0.15f;

        /// <summary>The flat dark floor for the setting (0..1).</summary>
        public static float DarkFloor(float darkVisibility)
            => DarkFloorMin + (DarkFloorMax - DarkFloorMin) * Clamp01(darkVisibility);

        /// <summary>
        /// The scale on the models' key light and ambient floor (1 = the plain studio look). Outside it follows the world
        /// light's luma (<paramref name="lightLuma"/>, linear) from the dark value up to 1 at noon; while the camera is
        /// underground (<paramref name="cameraExposure"/> 0 … 1 open sky) it sinks toward the cave value, never above the
        /// outside value; an interior fill (<paramref name="indoorFill"/>: the cabin, a station deck, the home base) lifts it.
        /// </summary>
        public static float ModelLight(float lightLuma, float cameraExposure, float indoorFill, float darkVisibility)
        {
            float dark = ModelDarkMin + (ModelDarkMax - ModelDarkMin) * Clamp01(darkVisibility);
            float world = dark + (1f - dark) * Clamp01(lightLuma / NoonLuma);
            float cave = Math.Min(world, Math.Max(ModelCave, dark));
            float scale = cave + (world - cave) * Clamp01(cameraExposure);
            return Math.Max(scale, Clamp01(indoorFill));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}

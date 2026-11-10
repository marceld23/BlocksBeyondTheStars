// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The distance softness (#3461/#3462) as Unity-free rules: on a world with air the far part of the distance haze
    /// softens a little, so the far image calms down and reads as "far away". Two parts, both barely noticeable on purpose:
    /// <list type="bullet">
    /// <item><b>Texture softening</b> (#3461, every preset, the browser too): inside the haze the block textures fade toward
    /// their tile's average colour — the pixel detail that flickers at distance calms down, silhouettes stay crisp. Rides
    /// on the "Distance haze" switch.</item>
    /// <item><b>Soft distance</b> (#3462, Medium and better on a desktop client, its own switch): a slight real blur of the
    /// far third of the haze — the depth planes separate and the seam to the coarse far terrain softens. The open sky, the
    /// sun, the stars and the moons and planets in the sky are never touched.</item>
    /// </list>
    /// The gate is "the world has air" (<c>AtmosphereDensity &gt; 0</c>), not "the haze is on": the crystal world keeps
    /// a haze but has no air and stays as sharp as the moons. Weather needs no rule of its own — fog, rain and sandstorms
    /// pull the haze range in, and both parts follow it. <c>Sky.Mist.cs</c> turns the numbers into the shader global
    /// <c>_Sc_DistSoft</c>; <c>BlockAtlas.shader</c> and <c>DistanceSoft.shader</c> read it. Purely visual.
    /// </summary>
    public static class DistanceSoftLook
    {
        /// <summary>The texture softening's strength in thin air (density → 0) and in thick air (density 1): at most this
        /// share of the way from the texel to the tile's average colour, reached at the haze's far end.</summary>
        public const float TextureFadeThin = 0.25f, TextureFadeThick = 0.35f;

        /// <summary>The soft distance's largest blend of the blurred image over the sharp one, thin and thick air.</summary>
        public const float BlurBlendThin = 0.4f, BlurBlendThick = 0.5f;

        /// <summary>Where the blur band begins, as a share of the haze range (start → end); it is full at the haze end.</summary>
        public const float BandStartShare = 0.4f;

        /// <summary>The lowest quality preset (as its index: 0 Potato, 1 Low, 2 Medium, 3 High) the soft distance runs on —
        /// it needs the depth texture, which the two weakest presets switch off.</summary>
        public const int BlurMinPreset = 2;

        /// <summary>Whether the world has air: a world sent with density 0 (moons, asteroids, the void, the crystal world)
        /// stays sharp to the horizon.</summary>
        public static bool HasAir(float density) => density > 0f;

        /// <summary>The texture softening's strength (0 = off): only with air and the distance haze on, slightly stronger
        /// in thick air, capped low ("barely noticeable").</summary>
        /// <param name="density">The world's atmosphere density (0..1; 0 = no air).</param>
        /// <param name="hazeOn">The player's "Distance haze" switch.</param>
        public static float TextureFade(float density, bool hazeOn)
            => hazeOn && HasAir(density) ? Lerp(TextureFadeThin, TextureFadeThick, density) : 0f;

        /// <summary>The soft distance's largest blend (0 = off): its own switch (already gated to the presets and devices
        /// that may run it, <see cref="BlurAllowed"/>), the distance haze on, no Reduced effects, and air.</summary>
        public static float BlurBlend(float density, bool enabled, bool hazeOn, bool reducedEffects)
            => enabled && hazeOn && !reducedEffects && HasAir(density) ? Lerp(BlurBlendThin, BlurBlendThick, density) : 0f;

        /// <summary>Whether a client may run the soft distance at all: Medium or better, never in the browser or on another
        /// light device (the WebGPU render-texture pitfalls, and the frame budget there).</summary>
        /// <param name="preset">The quality preset as its index: 0 Potato, 1 Low, 2 Medium, 3 High.</param>
        /// <param name="lightDevice">A WebGL/WebGPU (or other light-device) client.</param>
        public static bool BlurAllowed(int preset, bool lightDevice) => !lightDevice && preset >= BlurMinPreset;

        /// <summary>Where the blur band begins (metres): <see cref="BandStartShare"/> of the way from the haze start to its
        /// end. A degenerate range (end not past start) puts it at the end.</summary>
        public static float BandStart(float fogStart, float fogEnd)
            => fogEnd > fogStart ? fogStart + ((fogEnd - fogStart) * BandStartShare) : fogEnd;

        /// <summary>The block atlas's mip that holds exactly one texel per tile — the tile's average colour the texture
        /// softening fades toward: log2 of the tile size (6 for 64 px), minus the quality level's global mip limit (Potato
        /// halves every runtime texture, so its mip 0 is the full atlas's mip 1). Never below 0.</summary>
        /// <param name="tilePixels">The tile's edge in pixels (a power of two).</param>
        /// <param name="mipLimit">The global texture mip limit of the active quality level.</param>
        public static int TileMeanMip(int tilePixels, int mipLimit)
        {
            int log2 = 0;
            for (int size = Math.Max(1, tilePixels); size > 1; size >>= 1)
            {
                log2++;
            }

            return Math.Max(0, log2 - Math.Max(0, mipLimit));
        }

        /// <summary>0..1 how far a point <paramref name="distance"/> metres from the camera lies in the blur band: 0 before
        /// <paramref name="bandStart"/>, smoothly to 1 at <paramref name="fogEnd"/> (the shader's composite repeats it).</summary>
        public static float BandWeight(float distance, float bandStart, float fogEnd)
        {
            if (fogEnd <= bandStart)
            {
                return distance >= fogEnd ? 1f : 0f;
            }

            float t = Clamp01((distance - bandStart) / (fogEnd - bandStart));
            return t * t * (3f - (2f * t));
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * Clamp01(t));

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}

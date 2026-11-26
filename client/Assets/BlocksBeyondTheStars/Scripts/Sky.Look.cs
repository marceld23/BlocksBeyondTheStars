// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The globals the look's shader branches read (values in <see cref="GameLook"/>) — the coloured ambient light, the
    /// sun's hue and direction for the models, the texture calm, the sky's zenith colour and the ranges of the near
    /// geometry. The layouts are listed in <c>AtmosphereCommon.hlsl</c>. Published every frame on a planet; the space
    /// view and the menu reset the model light and the gradient.
    /// </summary>
    public sealed partial class Sky
    {
        private static readonly int LookId = Shader.PropertyToID("_Sc_Look");
        private static readonly int Look2Id = Shader.PropertyToID("_Sc_Look2");
        private static readonly int Look3Id = Shader.PropertyToID("_Sc_Look3");
        private static readonly int AmbSkyId = Shader.PropertyToID("_Sc_AmbSky");
        private static readonly int AmbGroundId = Shader.PropertyToID("_Sc_AmbGround");
        private static readonly int LookSunHueId = Shader.PropertyToID("_Sc_LookSunHue");
        private static readonly int LookSunDirId = Shader.PropertyToID("_Sc_LookSunDir");
        private static readonly int LookModelId = Shader.PropertyToID("_Sc_LookModel");
        private static readonly int SkyZenithId = Shader.PropertyToID("_Sc_SkyZenith");
        private static readonly int Look4Id = Shader.PropertyToID("_Sc_Look4");
        private static readonly int LookEyeId = Shader.PropertyToID("_Sc_LookEye");
        private static readonly int LookGrassId = Shader.PropertyToID("_Sc_LookGrass");
        private static readonly int LookGrassShadowId = Shader.PropertyToID("_Sc_LookGrassShadow");
        private static readonly int LookPlantId = Shader.PropertyToID("_Sc_LookPlant");
        private static readonly int LookCoatId = Shader.PropertyToID("_Sc_LookCoat");
        private static readonly int LookWaterId = Shader.PropertyToID("_Sc_LookWater");
        private static readonly int LookGemId = Shader.PropertyToID("_Sc_LookGem");
        private static readonly int LookLeafId = Shader.PropertyToID("_Sc_LookLeaf");

        /// <summary>Publishes the look's globals for this frame. <paramref name="tint"/> is the world light as composed
        /// (sRGB), <paramref name="zenithBase"/> the sky colour before the golden-hour glow, <paramref name="plainSky"/>
        /// an airless body or a station (no sky hue in the shadows, no gradient).</summary>
        private void PublishLook(Color tint, Color zenithBase, float seenDay, float weatherIntensity, bool plainSky)
        {
            Color light = ShaderColor.Srgb(tint);
            float lum = Mathf.Max(Luminance(light), 1e-4f);
            Color lightHue = new Color(light.r / lum, light.g / lum, light.b / lum);

            Color zenith = ShaderColor.Srgb(Zenith(zenithBase));
            float zenithLum = Luminance(zenith);
            Color skyHue = plainSky || zenithLum < 0.004f
                ? lightHue
                : new Color(zenith.r / zenithLum, zenith.g / zenithLum, zenith.b / zenithLum);

            PublishLookLight(lightHue, skyHue, lum);
            PublishNearCrowns();
            PublishNearGround();
            PublishGlowHaze(); // the haze takes the colour of lava and crystals around (Sky.GlowHaze.cs)

            Shader.SetGlobalFloat(LookGemId, 1f); // crystals lit as cut stones

            // Water: clear turquoise shallows with the sun's web of light on the bed, and glitter on the surface.
            Shader.SetGlobalVector(LookWaterId, new Vector4(1f, 1f, Mathf.Clamp(GameLook.WaterGlitter, 0f, 3f), 0f));

            // The gradient: full by day, gone at night (the stars keep their sky) and thinner under heavy weather.
            float gradient = plainSky
                ? 0f
                : GameLook.ZenithAmount * Mathf.Clamp01(seenDay * 1.6f) * (1f - 0.6f * weatherIntensity);
            Shader.SetGlobalVector(SkyZenithId, new Vector4(zenith.r, zenith.g, zenith.b, gradient));
        }

        /// <summary>The light itself: the shares of ambient and sun, the texture calm, the crown mode and flutter, the
        /// hand's share, the soft edges, grain and rim, and the two ambient colours with the sun's hue.</summary>
        private static void PublishLookLight(Color lightHue, Color skyHue, float lum)
        {
            // Both ambient colours carry the sun light's luminance, so the look moves hue and contrast, not brightness.
            Color ambSky = Color.Lerp(lightHue, skyHue, Mathf.Clamp01(GameLook.AmbSat)) * lum;
            Color ambGround = lightHue * (lum * GameLook.GroundLevel);

            int meanMip = DistanceSoftLook.TileMeanMip(BlockTextureAtlas.Tile, QualitySettings.globalTextureMipmapLimit);
            Shader.SetGlobalVector(LookId, new Vector4(1f, Mathf.Clamp01(GameLook.Calm), GameLook.AmbShare, GameLook.SunShare));
            Shader.SetGlobalVector(Look2Id, new Vector4(
                meanMip, GameLook.CrownMode, Mathf.Clamp(GameLook.Flutter, 0f, 4f), Mathf.Clamp01(GameLook.Hand)));
            Shader.SetGlobalVector(Look3Id, new Vector4(
                Mathf.Clamp(GameLook.SoftEdge, 0f, 0.4f), Mathf.Clamp01(GameLook.Joint), Mathf.Clamp(GameLook.Grain, 0f, 2f),
                Mathf.Clamp(GameLook.Rim, 0f, 2f)));
            Shader.SetGlobalVector(AmbSkyId, new Vector4(ambSky.r, ambSky.g, ambSky.b, 1f));
            Shader.SetGlobalVector(AmbGroundId, new Vector4(ambGround.r, ambGround.g, ambGround.b, 1f));
            Shader.SetGlobalVector(LookSunHueId, new Vector4(lightHue.r, lightHue.g, lightHue.b, lum));
            Shader.SetGlobalFloat(LookModelId, 1f);
        }

        /// <summary>Staged crowns: the range of the real clusters, the cross-fade's width and the band right at the eye in
        /// which a cube shrinks away, the leaf picture on their small cubes, and the eye with the clusters' shadow range
        /// (published for the shadow pass, which renders from the sun's view).</summary>
        private static void PublishNearCrowns()
        {
            float crownRange = GameLook.CrownRangeNow;
            float crownNear = Mathf.Max(0f, GameLook.CrownNear);
            Shader.SetGlobalVector(Look4Id, new Vector4(
                crownRange, Mathf.Clamp(GameLook.CrownFade, 0.5f, Mathf.Max(0.5f, crownRange)), crownNear, crownNear * 0.4f));
            Shader.SetGlobalVector(LookLeafId, GameLook.LeafPictureNow);

            var eye = Camera.main;
            if (eye != null)
            {
                Vector3 eyePos = eye.transform.position;
                Shader.SetGlobalVector(LookEyeId, new Vector4(eyePos.x, eyePos.y, eyePos.z, GameLook.CrownShadowNow));
            }
        }

        /// <summary>Grass clumps, plant bodies and fur: their ranges and the bands at their ends.</summary>
        private static void PublishNearGround()
        {
            // Grass clumps: their range, the last third of it over which they grow out of the ground, the height factor,
            // and how much the carpet under them is calmed; grass takes the softened sun shadow of the ground beneath it.
            float grassRange = GameLook.GrassRangeNow;
            Shader.SetGlobalVector(LookGrassId, new Vector4(
                grassRange, Mathf.Max(1f, grassRange * 0.35f), Mathf.Clamp(GameLook.GrassHeight, 0.2f, 3f),
                Mathf.Clamp01(GameLook.GrassCalm)));
            Shader.SetGlobalFloat(LookGrassShadowId, LookProbe.GrassShadowSingle ? 0f : 1f);
            if (HighDetails.Grass)
            {
                GameLook.ApplyCarpetAo(MeadowAssets.ShellMaterial); // the carpet out of the SSAO (it flickered there)
            }

            // Plant bodies: their range, the cross-fade to the crossed picture at its end, and how far they cast their own
            // shadows. Zero until bodies can be drawn — a picture is never hidden without a body to take its place.
            bool plantsReady = PlantBodies.Ready;
            Shader.SetGlobalVector(LookPlantId, new Vector4(
                plantsReady ? GameLook.PlantRangeNow : 0f, GameLook.PlantFadeNow, plantsReady ? GameLook.PlantShadowNow : 0f, 0f));

            // Fur: the short felt's range and the band at its end over which the blades shrink away.
            GameLook.AdaptCoatRange();
            float coatRange = GameLook.CoatRangeNow;
            Shader.SetGlobalVector(LookCoatId, new Vector4(coatRange, Mathf.Min(4f, Mathf.Max(0.5f, coatRange * 0.25f)), 0f, 0f));
        }

        /// <summary>The sun's direction for the model light (the block shader's <c>_Sc_SunDir</c>, under a name every
        /// shader that includes AtmosphereCommon can read).</summary>
        private static void PublishLookSun(Vector3 toSun) => Shader.SetGlobalVector(LookSunDirId, toSun);

        /// <summary>Space view, menu: models keep the studio light and the sky dome draws no gradient.</summary>
        private static void ResetLook()
        {
            Shader.SetGlobalFloat(LookModelId, 0f);
            Shader.SetGlobalVector(SkyZenithId, Vector4.zero);
        }

        /// <summary>The sky straight overhead: the same hue, darker and more saturated than the horizon.</summary>
        private static Color Zenith(Color sky)
        {
            Color.RGBToHSV(sky, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * GameLook.ZenithSat + 0.08f), Mathf.Clamp01(v * GameLook.ZenithDark));
        }

        private static float Luminance(Color linear) => 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
    }
}

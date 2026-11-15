// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using FarViewRange = BlocksBeyondTheStars.Client.FarTerrain.FarViewRange;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The Graphics category of the settings screen (#3666): the picture, how far you see, the effects and the
    /// details of the High preset. Every row applies at once — changes made from the in-game pause menu are visible right
    /// away instead of only when the settings screen closes.</summary>
    public sealed partial class UiSettings
    {
        private void GraphicsRows(ref float y)
        {
            PictureRows(ref y);
            ViewRows(ref y);
            EffectRows(ref y);
            HighDetailRows(ref y); // #3061: the desktop-class extras of the High preset
        }

        private void PictureRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.picture"));
            // A hand-picked preset ends auto-calibration (#1423): PresetAuto off means the shell frame-time calibrator never
            // overrides the player's explicit choice again.
            Cycle(ref y, L("ui.settings.preset"), L("ui.settings.preset." + S.Preset.ToString().ToLowerInvariant()), dir =>
            {
                S.Preset = (QualityPreset)Wrap((int)S.Preset + dir, 4);
                S.PresetAuto = false;
                S.Apply();
                ApplyLiveWorld();
                Rebuild();
            }, L("ui.settings.preset_info"));
            // Brightness sits right under the preset (#1457): "the world is too dark" is the first thing a player looks for.
            Stepper(ref y, L("ui.settings.brightness"), (S.Brightness - 0.7f) / 0.8f,
                () => SetBrightness(S.Brightness - 0.05f), () => SetBrightness(S.Brightness + 0.05f),
                Mathf.RoundToInt(S.Brightness * 100f) + "%", L("ui.settings.brightness_info"));
            // Cosiness (#2820): 0–100 % in steps of 10, applied live like the brightness above.
            Stepper(ref y, L("ui.settings.cosiness"), S.Cosiness,
                () => SetCosiness(S.Cosiness - 0.1f), () => SetCosiness(S.Cosiness + 0.1f),
                Mathf.RoundToInt(S.Cosiness * 100f) + "%", L("ui.settings.cosiness_hint"));
            // #3265: caves and moonless nights are dark on purpose; this lifts the dark for players who find it too much —
            // 0–100 % in steps of 25, applied live (the dark floor, the eye adaptation underground, the creatures' light).
            Stepper(ref y, L("ui.settings.dark_visibility"), S.DarkVisibility,
                () => SetDarkVisibility(S.DarkVisibility - 0.25f), () => SetDarkVisibility(S.DarkVisibility + 0.25f),
                Mathf.RoundToInt(S.DarkVisibility * 100f) + "%", L("ui.settings.dark_visibility_hint"));
        }

        private void SetBrightness(float value)
        {
            S.Brightness = Mathf.Clamp(value, 0.7f, 1.5f);
            UrpScenePost.Instance?.SetBrightness(S.Brightness);
            Rebuild();
        }

        private void SetCosiness(float value)
        {
            S.Cosiness = Mathf.Clamp01(Mathf.Round(value * 10f) / 10f);
            UrpScenePost.Instance?.SetCosiness(S.Cosiness);
            Rebuild();
        }

        private void SetDarkVisibility(float value)
        {
            S.DarkVisibility = Mathf.Clamp01(Mathf.Round(value * 4f) / 4f);
            ApplyLiveWorld();
            Rebuild();
        }

        private void ViewRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.view"));
            // 1–16 chunks; the server clamps a join request to the same range (GameServer.MaxClientViewDistanceChunks). The
            // (local) server streams the radius it was STARTED with, so mid-session the card says it waits for the next world.
            string viewInfo = L("ui.settings.view_distance_info");
            if (_shell.CurrentBoot != null)
            {
                viewInfo += "\n\n" + L("ui.settings.view_distance_hint");
            }

            Stepper(ref y, L("ui.settings.view_distance"), (S.ViewDistanceChunks - 1) / 15f,
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks - 1, 1, 16); Rebuild(); },
                () => { S.ViewDistanceChunks = Mathf.Clamp(S.ViewDistanceChunks + 1, 1, 16); Rebuild(); },
                S.ViewDistanceChunks.ToString(), viewInfo);

            // #1820: the far terrain beyond the streamed chunks — Off / 512 / 1024 blocks (+ 2048 on desktop High, #3059;
            // the label shows the range in effect), applied live.
            int farViewInEffect = FarViewRange.Effective(S.FarViewBlocks, HighDetails.Active);
            Cycle(ref y, L("ui.settings.far_view"), FarViewLabel(farViewInEffect), dir =>
            {
                System.Func<int, int> next = range => FarViewRange.Next(range, HighDetails.Active);
                SetFarView(dir > 0 ? next(farViewInEffect) : Previous(farViewInEffect, next));
                Rebuild();
            }, L("ui.settings.far_view_info"));
        }

        /// <summary>Stores the far-view range and hands it to a running world at once.</summary>
        private void SetFarView(int blocks)
        {
            S.FarViewBlocks = blocks;
            var boot = _shell.CurrentBoot;
            if (boot != null)
            {
                boot.FarViewBlocks = S.FarViewBlocks;
                boot.FarView?.SetRange(S.FarViewBlocks);
            }
        }

        private void EffectRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.effects"));
            Toggle(ref y, L("ui.settings.smaa"), S.Smaa, () => { S.Smaa = !S.Smaa; S.ApplyCameraLook(); Rebuild(); });
            Toggle(ref y, L("ui.settings.lens_flare"), S.LensFlare, () => { S.LensFlare = !S.LensFlare; ApplyLiveWorld(); Rebuild(); });
            Toggle(ref y, L("ui.settings.motion_blur"), S.MotionBlur, () => { S.MotionBlur = !S.MotionBlur; ApplyLiveWorld(); Rebuild(); });
            Toggle(ref y, L("ui.settings.distance_haze"), S.DistanceHaze, () => { S.DistanceHaze = !S.DistanceHaze; ApplyLiveWorld(); Rebuild(); });
            // #3462: the soft distance on worlds with air — Medium+ on a desktop client; the browser never runs it, so no row.
            if (!ClientSettings.FarViewLightDevice)
            {
                Toggle(ref y, L("ui.settings.distance_softness"), S.DistanceSoftness, () => { S.DistanceSoftness = !S.DistanceSoftness; ApplyLiveWorld(); Rebuild(); });
            }

            // #3121: the grass tufts on natural ground — a builder keeping a garden or a field tidy switches them off.
            Toggle(ref y, L("ui.settings.ground_details"), S.GroundDetails, () => { S.GroundDetails = !S.GroundDetails; S.Apply(); Rebuild(); });
            AtmosphereRows(ref y);
        }

        /// <summary>Atmosphere package (#2404): one row — Off / Some (the cheap set) / All / Custom — and, on Custom, the
        /// per-effect switches. The presets still gate the expensive ones (Medium+), whatever the switch says.</summary>
        private void AtmosphereRows(ref float y)
        {
            Cycle(ref y, L("ui.settings.atmosphere"), L("ui.settings.atmosphere." + S.Atmosphere.ToString().ToLowerInvariant()), dir =>
            {
                S.ApplyAtmosphereMode((AtmosphereMode)Wrap((int)S.Atmosphere + dir, 4)); // Off → Some → All → Custom
                S.Apply();
                ApplyLiveWorld();
                Rebuild();
            }, L("ui.settings.atmosphere_info"));
            if (S.Atmosphere != AtmosphereMode.Custom)
            {
                return;
            }

            AtmoToggle(ref y, "height_fog", S.HeightFog, v => S.HeightFog = v);
            AtmoToggle(ref y, "cloud_shadows", S.CloudShadows, v => S.CloudShadows = v);
            AtmoToggle(ref y, "eye_adaptation", S.EyeAdaptation, v => S.EyeAdaptation = v);
            AtmoToggle(ref y, "torch_flicker", S.TorchFlicker, v => S.TorchFlicker = v);
            AtmoToggle(ref y, "wind_sway", S.WindSway, v => S.WindSway = v);
            AtmoToggle(ref y, "wet_surfaces", S.WetSurfaces, v => S.WetSurfaces = v);
            AtmoToggle(ref y, "weather_particles", S.WeatherParticles, v => S.WeatherParticles = v);
            AtmoToggle(ref y, "shooting_stars", S.ShootingStars, v => S.ShootingStars = v);
            AtmoToggle(ref y, "underwater", S.Underwater, v => S.Underwater = v);
            AtmoToggle(ref y, "light_shafts", S.LightShafts, v => S.LightShafts = v);
            AtmoToggle(ref y, "emitter_light", S.EmitterLight, v => S.EmitterLight = v);
            AtmoToggle(ref y, "soft_particles", S.SoftParticles, v => S.SoftParticles = v);
            AtmoToggle(ref y, "fog_wisps", S.FogWisps, v => S.FogWisps = v);    // #3271
            AtmoToggle(ref y, "cloud_fog", S.CloudFog, v => S.CloudFog = v);    // #3272
            AtmoToggle(ref y, "fog_banks", S.FogBanks, v => S.FogBanks = v);    // #3273
        }
    }
}

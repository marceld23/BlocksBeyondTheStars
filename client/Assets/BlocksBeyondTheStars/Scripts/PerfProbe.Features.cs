// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// PerfProbe's <c>-perfFeature</c> tokens beyond the first set in <c>PerfProbe.cs</c> (ssao, depth, smaa, scatter,
    /// softdist, shadowmap, shadowdist). One token switches one cost-bearing part off (or to a value) for the run, so a
    /// sweep at a fixed preset and view distance itemises what a preset costs (#4050), and a rule can be compared with
    /// the one before it in the same build (#4045). A token is <c>name=value</c>; a bare name means <c>off</c>.
    /// <list type="bullet">
    ///   <item><b>Chunk pipeline</b> — <c>meshbudget=frame</c>: the mesher's rule before #4045 (four builds started and
    ///   eight uploaded per frame, whatever the machine); <c>loadbudget=off</c>: the loading veil gets the budgets of
    ///   play. Both are applied before the world starts, so the fill is measured with them.</item>
    ///   <item><b>Instance data</b> — <c>instances=arrays</c>: the near drawers and the ground detail hand their
    ///   instances over as arrays each frame, as before #4046 part b, instead of drawing from buffers on the graphics
    ///   card (<see cref="GpuInstances"/>). Applied before the world starts, so nothing is uploaded.</item>
    ///   <item><b>Pipeline</b> — <c>msaa=1|2|4</c>, <c>scale=0.5</c> (render scale: how much of the frame is bound by the
    ///   pixel count), <c>cascades=2</c>, <c>sunshadow=pipeline</c> (9 taps instead of desktop High's 16) or
    ///   <c>sunshadow=hard</c>, <c>post=off</c> (every post effect), <c>opaque=off</c> (the colour copy the water reads),
    ///   <c>hudssao=off</c> (the visor's HUD camera on the renderer without ambient occlusion).</item>
    ///   <item><b>High details</b> — <c>relief</c>, <c>grass</c> (the carpet), <c>flowers</c>, <c>rooms</c> (room
    ///   reflections), <c>decals</c>, <c>volumetric</c>, <c>lampshadows</c>: the player's switches of the High preset.</item>
    ///   <item><b>The look's near drawers</b> — <c>crowns</c>, <c>clumps</c>, <c>plants</c>, <c>felt</c>,
    ///   <c>nearshadows</c> (crowns and plants cast none of their own), <c>grassshadow=single</c>, <c>dof</c>
    ///   (<see cref="LookProbe"/>).</item>
    ///   <item><b>World and screen</b> — <c>cloudshadow</c>, <c>fog</c>, <c>texsoft</c> (texture softening), <c>cloudsea</c>,
    ///   <c>mist</c> (fog banks), <c>farview</c>, <c>lensflare</c>, <c>visor</c> (the visor HUD pipeline).</item>
    /// </list>
    /// The run exits when done, so nothing is restored; the High-detail switches change the settings object in memory
    /// only (PerfProbe restores the settings file).
    /// </summary>
    public sealed partial class PerfProbe
    {
        /// <summary>The tokens that must hold from the world's first frame on (the fill is measured from there). Returns
        /// their tags; the tokens stay in the spec and are skipped by the later pass.</summary>
        private List<string> ApplyEarlyFeatures()
        {
            var tags = new List<string>();
            foreach (var (key, val) in FeatureTokens())
            {
                if (key == "meshbudget")
                {
                    GameBootstrap.LegacyMeshBudget = string.Equals(val, "frame", StringComparison.OrdinalIgnoreCase);
                    tags.Add(GameBootstrap.LegacyMeshBudget ? "meshFrame" : "meshCores");
                }
                else if (key == "loadbudget")
                {
                    GameBootstrap.LoadingBudgetOff = true;
                    tags.Add("loadBudgetOff");
                }
                else if (key == "instances")
                {
                    GpuInstances.Enabled = !string.Equals(val, "arrays", StringComparison.OrdinalIgnoreCase);
                    tags.Add(GpuInstances.Enabled ? "instBuffers" : "instArrays");
                }
            }

            return tags;
        }

        /// <summary>A token of this file's set, applied once the world runs. False for a token nobody knows.</summary>
        private static bool ApplyMoreFeature(string key, string val, List<string> tags)
            => key == "meshbudget" || key == "loadbudget" || key == "instances" // applied early (ApplyEarlyFeatures)
               || ApplyPipelineFeature(key, val, tags) || ApplyHighDetailFeature(key, val, tags)
               || ApplyNearDrawerFeature(key, val, tags) || ApplyWorldFeature(key, val, tags);

        private static bool ApplyPipelineFeature(string key, string val, List<string> tags)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var cam = ClientSettings.ActiveCameraData;
            switch (key)
            {
                case "msaa":
                    if (urp != null && int.TryParse(val, out int samples) && samples >= 1) { urp.msaaSampleCount = samples; tags.Add($"msaa{samples}"); }
                    return true;
                case "scale":
                    if (urp != null && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) && scale > 0f)
                    {
                        urp.renderScale = Mathf.Clamp(scale, 0.25f, 1f);
                        tags.Add($"scale{Mathf.RoundToInt(urp.renderScale * 100f)}");
                    }

                    return true;
                case "cascades":
                    if (urp != null && int.TryParse(val, out int cascades) && cascades >= 1) { urp.shadowCascadeCount = Mathf.Clamp(cascades, 1, 4); tags.Add($"casc{cascades}"); }
                    return true;
                case "sunshadow":
                    return ApplySunShadow(val, tags);
                case "post":
                    if (cam != null) { cam.renderPostProcessing = false; tags.Add("postOff"); }
                    return true;
                case "opaque":
                    if (cam != null) { cam.requiresColorOption = CameraOverrideOption.Off; }
                    Shader.SetGlobalFloat("_Sc_ScreenFx", 0f); // the water then takes its plain look instead of reading the copy
                    tags.Add("opaqueOff");
                    return true;
                case "hudssao":
                    var hud = GameObject.Find("HUD Camera");
                    if (hud != null && hud.TryGetComponent<Camera>(out var hudCamera)) { hudCamera.GetUniversalAdditionalCameraData().SetRenderer(1); tags.Add("hudSsaoOff"); }
                    return true;
                default:
                    return false;
            }
        }

        private static bool ApplySunShadow(string val, List<string> tags)
        {
            if (string.Equals(val, "hard", StringComparison.OrdinalIgnoreCase))
            {
                Light sun = null;
                foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (light.type == LightType.Directional && light.shadows != LightShadows.None) { sun = light; break; }
                }

                if (sun != null) { sun.shadows = LightShadows.Hard; tags.Add("sunHard"); }
            }
            else
            {
                LookProbe.SunShadowPipelineQuality = true;
                tags.Add("sunPipeline");
            }

            return true;
        }

        /// <summary>The player's switches of the High preset, changed in memory and pushed the way the settings screen does.</summary>
        private static bool ApplyHighDetailFeature(string key, string val, List<string> tags)
        {
            _ = val;
            var shell = FindAnyObjectByType<AppShell>();
            var settings = shell != null ? shell.Settings : null;
            if (settings == null)
            {
                return false;
            }

            switch (key)
            {
                case "relief": settings.HighRelief = false; break;
                case "grass": settings.HighGrass = false; break;
                case "flowers": settings.HighFlowers = false; break;
                case "rooms": settings.HighRoomReflections = false; break;
                case "decals": settings.HighDecals = false; break;
                case "volumetric": settings.HighVolumetricLight = false; break;
                case "lampshadows": settings.HighLampShadows = false; break;
                case "visor": settings.VisorEffects = false; break;
                default: return false;
            }

            HighDetails.Configure(settings);
            tags.Add(key + "Off");
            return true;
        }

        private static bool ApplyNearDrawerFeature(string key, string val, List<string> tags)
        {
            switch (key)
            {
                case "crowns": LookProbe.CrownsOff = true; break;
                case "clumps": LookProbe.ClumpsOff = true; break;
                case "plants": LookProbe.PlantsOff = true; break;
                case "felt": LookProbe.FeltOff = true; break;
                case "nearshadows": LookProbe.NearShadowsOff = true; break;
                case "dof": LookProbe.FocusBlurOff = true; break;
                case "grassshadow":
                    LookProbe.GrassShadowSingle = true;
                    tags.Add("grassShadow1");
                    return true;
                default: return false;
            }

            _ = val;
            tags.Add(key + "Off");
            return true;
        }

        private static bool ApplyWorldFeature(string key, string val, List<string> tags)
        {
            _ = val;
            var sky = FindAnyObjectByType<Sky>();
            switch (key)
            {
                case "cloudshadow": if (sky != null) { sky.CloudShadowsEnabled = false; } break;
                case "fog": if (sky != null) { sky.FogEnabled = false; } break;
                case "texsoft": if (sky != null) { sky.TextureSoftEnabled = false; } break;
                case "cloudsea":
                    var sea = FindAnyObjectByType<CloudSea>();
                    if (sea != null) { sea.Enabled = false; }
                    break;
                case "mist":
                    var mist = FindAnyObjectByType<MistCards>();
                    if (mist != null) { mist.MaxCards = 0; }
                    break;
                case "farview":
                    var shell = FindAnyObjectByType<AppShell>();
                    var far = shell != null && shell.CurrentBoot != null ? shell.CurrentBoot.FarView : null;
                    if (far != null) { far.SetRange(0); }
                    break;
                case "lensflare": if (UrpScenePost.Instance != null) { UrpScenePost.Instance.LensFlareEnabled = false; } break;
                default: return false;
            }

            tags.Add(key + "Off");
            return true;
        }

        /// <summary>The spec's tokens as lower-case key and trimmed value ("off" when a token has none).</summary>
        private IEnumerable<(string Key, string Val)> FeatureTokens()
        {
            if (string.IsNullOrEmpty(_featureSpec))
            {
                yield break;
            }

            foreach (var raw in _featureSpec.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split('=');
                yield return (parts[0].Trim().ToLowerInvariant(), parts.Length > 1 ? parts[1].Trim() : "off");
            }
        }
    }
}

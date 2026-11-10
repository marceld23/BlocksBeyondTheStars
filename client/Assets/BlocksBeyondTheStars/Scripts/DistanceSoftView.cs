// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Soft distance (#3462): on a world with air, a barely noticeable blur over the far third of the distance haze — Medium
    /// and better on a desktop client, its own graphics switch ("Soft distance"). Owns the <c>BlocksBeyondTheStars/DistanceSoft</c>
    /// material and the <see cref="DistanceSoftPass"/>, and enqueues the pass on the gameplay camera and the capture clones
    /// (<see cref="Sky.WorldCameras"/>) whenever the Sky has a blend to draw (<see cref="Sky.DistanceBlur01"/>: air, the haze
    /// on, not indoors, not under water, no Reduced effects) and the flight view is not up. The texture softening (#3461)
    /// needs no pass — the block shader does it on every preset.
    /// </summary>
    public sealed class DistanceSoftView : MonoBehaviour
    {
        /// <summary>After the transparents and the heat shimmer (a camera quad in the transparent queue), before bloom and
        /// the tone mapping — the blur works on the scene as it is lit, so a bright far emitter still blooms.</summary>
        private const RenderPassEvent PassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        /// <summary>Below this blend the pass is not drawn at all.</summary>
        private const float MinBlend = 0.002f;

        public GameBootstrap Game;
        public Camera Camera;
        public Sky Sky;

        private Material _mat;
        private DistanceSoftPass _pass;
        private bool _loggedOn;

        /// <summary>Whether this client may draw the soft distance: the player's switch, Medium or better, never in the
        /// browser or on another light device (<see cref="DistanceSoftLook.BlurAllowed"/>). The haze switch, Reduced effects
        /// and the world's air are the Sky's part.</summary>
        public static bool EnabledFor(ClientSettings s)
            => s != null && s.DistanceSoftness && DistanceSoftLook.BlurAllowed((int)s.Preset, ClientSettings.FarViewLightDevice);

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/DistanceSoft");
            if (GraphicsSettings.currentRenderPipeline == null || shader == null || !shader.isSupported || ClientSettings.FarViewLightDevice)
            {
                // Built-in RP, a stripped build, a device without the URP SubShader or the browser: no blur, nothing else changes.
                Debug.Log("[DistanceSoftView] shader unavailable or light device — soft distance off.");
                enabled = false;
                return;
            }

            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _pass = new DistanceSoftPass(_mat, PassEvent);
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_mat != null)
            {
                Destroy(_mat);
            }
        }

        /// <summary>The blend this frame (0 = nothing to draw).</summary>
        private float Blend => Sky == null || Game == null || Game.SpaceViewActive || !Game.FogActive ? 0f : Sky.DistanceBlur01;

        private void Update()
        {
            // One log line whenever the state flips, so capture and bug-report logs show whether the effect ran at all.
            bool on = Blend >= MinBlend;
            if (on != _loggedOn)
            {
                _loggedOn = on;
                Debug.Log(on ? $"[DistanceSoftView] on, blend {Blend:0.00}" : "[DistanceSoftView] off");
            }
        }

        /// <summary>Enqueues the pass on the world cameras only, right before each renders (code-only, like the volumetric
        /// light — no renderer-asset feature, so the build script's renderer clean-up never touches it).</summary>
        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_pass == null || Blend < MinBlend || cam == null || (cam != Camera && !Sky.WorldCameras.Contains(cam)))
            {
                return;
            }

            cam.GetUniversalAdditionalCameraData()?.scriptableRenderer?.EnqueuePass(_pass);
        }
    }
}

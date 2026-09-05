// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// URP version of the holographic visor composite (the Built-in path uses <see cref="VisorComposite"/> via
    /// OnRenderImage, which URP never calls). Render-graph passes laid over the main camera AFTER
    /// post-processing: the separately rendered HUD RT is first thresholded + downsampled to quarter
    /// resolution and blurred (a real hologram glow, passes 1–3 of <c>BlocksBeyondTheStars/Visor</c>), then the
    /// composite pass (pass 0) lays world (<c>_BlitTexture</c>), HUD (<c>_HudTex</c>) and glow (<c>_HudGlowTex</c>)
    /// together. Enqueued from <c>RenderPipelineManager.beginCameraRendering</c> each frame (code-only — no
    /// renderer-asset editing). Owned + parameterised by <see cref="VisorHud"/>; degrades to nothing if the
    /// shader/material is missing.
    /// </summary>
    public sealed class VisorUrpCompositor : System.IDisposable
    {
        private readonly Camera _mainCamera;
        private readonly Material _mat;
        private readonly VisorPass _pass;

        public Material Material => _mat;

        public VisorUrpCompositor(Camera mainCamera, Shader visorShader)
        {
            _mainCamera = mainCamera;
            _mat = new Material(visorShader) { hideFlags = HideFlags.HideAndDontSave };
            _pass = new VisorPass(_mat) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>The HUD render texture as an RTHandle (imported into the render graph each frame so the glow
        /// chain can read it). Null = composite only, no glow.</summary>
        public void SetHud(RTHandle hud)
        {
            _pass.Hud = hud;
            if (hud != null && hud.rt != null && _mat != null)
            {
                // Blitter does not publish _BlitTexture_TexelSize for blit passes, so the glow chain gets its
                // texel sizes from here: the HUD RT for the downsample, the quarter-res targets for the blurs.
                float w = hud.rt.width, h = hud.rt.height;
                _mat.SetVector(HudTexelId, new Vector4(1f / w, 1f / h, w, h));
                float gw = Mathf.Max(8, hud.rt.width / 4), gh = Mathf.Max(8, hud.rt.height / 4);
                _mat.SetVector(HudGlowTexelId, new Vector4(1f / gw, 1f / gh, gw, gh));
            }
        }

        private static readonly int HudTexelId = Shader.PropertyToID("_HudTexel");
        private static readonly int HudGlowTexelId = Shader.PropertyToID("_HudGlowTexel");

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _mainCamera || _mat == null)
            {
                return;
            }

            cam.GetUniversalAdditionalCameraData()?.scriptableRenderer?.EnqueuePass(_pass);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (_mat != null)
            {
                Object.Destroy(_mat);
            }
        }

        /// <summary>The render-graph passes: glow chain on the HUD RT, then blit cameraColor → temp through the
        /// visor material and swap the camera colour to the composited target (the documented URP "blit with
        /// material" pattern, plus extra inputs bound in the render function).</summary>
        private sealed class VisorPass : ScriptableRenderPass
        {
            private static readonly int HudTexId = Shader.PropertyToID("_HudTex");
            private static readonly int HudGlowTexId = Shader.PropertyToID("_HudGlowTex");

            private readonly Material _mat;
            public RTHandle Hud;

            private sealed class PassData
            {
                public TextureHandle Source, Hud, Glow;
                public Material Mat;
                public bool HasGlow;
            }

            public VisorPass(Material mat) => _mat = mat;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    return; // can't sample the back buffer as _BlitTexture — skip (HUD then simply isn't styled)
                }

                var source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "VisorComposited";
                desc.clearBuffer = false;
                var dest = renderGraph.CreateTexture(desc);

                TextureHandle hud = TextureHandle.nullHandle, glow = TextureHandle.nullHandle;
                bool hasGlow = false;
                if (Hud != null && Hud.rt != null)
                {
                    hud = renderGraph.ImportTexture(Hud);
                    int gw = Mathf.Max(8, Hud.rt.width / 4), gh = Mathf.Max(8, Hud.rt.height / 4);
                    var gdesc = new TextureDesc(gw, gh)
                    {
                        name = "VisorHudGlowA",
                        colorFormat = GraphicsFormat.B10G11R11_UFloatPack32,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        clearBuffer = false,
                    };
                    var glowA = renderGraph.CreateTexture(gdesc);
                    gdesc.name = "VisorHudGlowB";
                    var glowB = renderGraph.CreateTexture(gdesc);
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(hud, glowA, _mat, 1), passName: "Visor HUD Glow Down");
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(glowA, glowB, _mat, 2), passName: "Visor HUD Glow H");
                    renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(glowB, glowA, _mat, 3), passName: "Visor HUD Glow V");
                    glow = glowA;
                    hasGlow = true;
                }

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Visor HUD Composite", out var data))
                {
                    data.Source = source;
                    data.Hud = hud;
                    data.Glow = glow;
                    data.Mat = _mat;
                    data.HasGlow = hasGlow;
                    builder.UseTexture(source);
                    if (hasGlow)
                    {
                        builder.UseTexture(hud);
                        builder.UseTexture(glow);
                    }

                    builder.SetRenderAttachment(dest, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    {
                        if (d.HasGlow)
                        {
                            RTHandle hudRt = d.Hud, glowRt = d.Glow;
                            d.Mat.SetTexture(HudTexId, hudRt);
                            d.Mat.SetTexture(HudGlowTexId, glowRt);
                        }
                        else
                        {
                            d.Mat.SetTexture(HudGlowTexId, Texture2D.blackTexture);
                        }

                        RTHandle sourceRt = d.Source;
                        Blitter.BlitTexture(ctx.cmd, sourceRt, new Vector4(1f, 1f, 0f, 0f), d.Mat, 0);
                    });
                }

                resources.cameraColor = dest;
            }
        }
    }
}

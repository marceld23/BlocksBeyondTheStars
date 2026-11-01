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
    /// The render-graph passes of the volumetric sun light (#3060; the maths is in <c>VolumetricLight.shader</c>): a
    /// half-resolution raymarch through the sun's shadow map (pass 0), a depth-aware blur there and back (passes 1 and 2),
    /// and a depth-aware upsample ADDED onto the camera colour (pass 3) — it never copies the colour or replaces it, so it
    /// cannot darken the frame. Reads the camera depth texture (<see cref="ScriptableRenderPass.ConfigureInput"/>) and the
    /// main-light shadow map; records nothing when either is missing, so such a camera renders unchanged. Enqueued per
    /// camera by <see cref="VolumetricSunLight"/>, which also fills the per-frame parameters.
    /// </summary>
    public sealed class VolumetricLightPass : ScriptableRenderPass
    {
        private static readonly int MarchId = Shader.PropertyToID("_Sc_VolMarch");
        private static readonly int DustId = Shader.PropertyToID("_Sc_VolDust");
        private static readonly int LightId = Shader.PropertyToID("_Sc_VolLight");
        private static readonly int TexelId = Shader.PropertyToID("_Sc_VolTexel");

        private static GraphicsFormat? _format;

        private readonly Material _mat;

        /// <summary>_Sc_VolMarch without its x (the pass puts the camera's shadow distance there): y = haze density,
        /// z = height-fog share, w = extinction share.</summary>
        public Vector4 March;

        /// <summary>_Sc_VolDust: dust density per metre, its reach, the HG g and the HG share.</summary>
        public Vector4 Dust;

        /// <summary>_Sc_VolLight: intensity (fades folded in), the haze-hue share and the cap on the added light.</summary>
        public Vector4 Light;

        public VolumetricLightPass(Material mat, RenderPassEvent passEvent)
        {
            _mat = mat;
            renderPassEvent = passEvent;
            requiresIntermediateTexture = true; // the composite draws into the camera colour, never straight into the back buffer
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>The half-res target format: rgb = light, a = linear depth (needs a float alpha). Checked once; None =
        /// the device cannot render it and the pass records nothing.</summary>
        public static GraphicsFormat Format
        {
            get
            {
                if (!_format.HasValue)
                {
                    var f = GraphicsFormat.R16G16B16A16_SFloat;
                    bool ok = SystemInfo.IsFormatSupported(f, GraphicsFormatUsage.Render)
                              && SystemInfo.IsFormatSupported(f, GraphicsFormatUsage.Sample);
                    _format = ok ? f : GraphicsFormat.None;
                }

                return _format.Value;
            }
        }

        private sealed class MarchData
        {
            public Material Mat;
            public Vector4 March, Dust, Light, Texel;
        }

        private sealed class CompositeData
        {
            public Material Mat;
            public TextureHandle Source;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle depth = resources.cameraDepthTexture;
            if (_mat == null || resources.isActiveTargetBackBuffer || !depth.IsValid() || cameraData.maxShadowDistance <= 0f
                || Format == GraphicsFormat.None)
            {
                return; // no depth, no shadows (no beams without them) or nothing to draw into: the camera renders unchanged
            }

            var target = cameraData.cameraTargetDescriptor;
            int w = Mathf.Max(1, (target.width + 1) / 2), h = Mathf.Max(1, (target.height + 1) / 2);
            var desc = new TextureDesc(w, h)
            {
                name = "VolumetricLightA",
                colorFormat = Format,
                filterMode = FilterMode.Point, // every read is a point tap: the depth in alpha must not be filtered
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false,
            };
            var a = renderGraph.CreateTexture(desc);
            desc.name = "VolumetricLightB";
            var b = renderGraph.CreateTexture(desc);

            RecordMarch(renderGraph, resources, depth, a, new Vector4(1f / w, 1f / h, w, h), cameraData.maxShadowDistance);
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, _mat, 1), passName: "Volumetric Light Blur H");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, _mat, 2), passName: "Volumetric Light Blur V");
            RecordComposite(renderGraph, resources, depth, a);
        }

        /// <summary>Pass 0 into the half-res target. Publishes the parameters as globals first — the blur and the composite
        /// read the texel size from there (Blitter does not set <c>_BlitTexture_TexelSize</c>).</summary>
        private void RecordMarch(RenderGraph renderGraph, UniversalResourceData resources, TextureHandle depth, TextureHandle target,
            Vector4 texel, float distance)
        {
            using (var builder = renderGraph.AddRasterRenderPass<MarchData>("Volumetric Light March", out var data))
            {
                data.Mat = _mat;
                data.March = new Vector4(distance, March.y, March.z, March.w);
                data.Dust = Dust;
                data.Light = Light;
                data.Texel = texel;
                builder.UseTexture(depth);
                TextureHandle shadows = resources.mainShadowsTexture;
                if (shadows.IsValid())
                {
                    builder.UseTexture(shadows); // bound as _MainLightShadowmapTexture by the shadow pass itself
                }

                builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MarchData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(MarchId, d.March);
                    ctx.cmd.SetGlobalVector(DustId, d.Dust);
                    ctx.cmd.SetGlobalVector(LightId, d.Light);
                    ctx.cmd.SetGlobalVector(TexelId, d.Texel);
                    Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.Mat, 0);
                });
            }
        }

        /// <summary>Pass 3: the blurred half-res light, upsampled and added onto the camera colour.</summary>
        private void RecordComposite(RenderGraph renderGraph, UniversalResourceData resources, TextureHandle depth, TextureHandle light)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Volumetric Light Composite", out var data))
            {
                data.Mat = _mat;
                data.Source = light;
                builder.UseTexture(light);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write); // loads: the pass blends onto it
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                {
                    RTHandle source = d.Source;
                    Blitter.BlitTexture(ctx.cmd, source, new Vector4(1f, 1f, 0f, 0f), d.Mat, 3);
                });
            }
        }
    }
}

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
    /// The render-graph passes of the soft distance (#3462; the maths is in <c>DistanceSoft.shader</c>): the camera colour
    /// to quarter resolution with the scene's distance in alpha (pass 0), a depth-aware blur there and back (passes 1 and 2),
    /// and a depth-aware upsample blended over the camera colour by the band weight (pass 3). The composite binds the
    /// camera's depth-stencil read-only, so the stencil bit the sky bodies write (<c>SkyBodyPhase</c>) keeps the moons and
    /// planets of the surface sky sharp. Records nothing without the depth texture or an intermediate colour target, so
    /// such a camera renders unchanged. Enqueued per camera by <see cref="DistanceSoftView"/>.
    /// </summary>
    public sealed class DistanceSoftPass : ScriptableRenderPass
    {
        private static readonly int TexelId = Shader.PropertyToID("_Sc_DistSoftTexel");

        /// <summary>The blur runs at 1/<see cref="Downscale"/> of the camera's resolution (the downsample takes four depth-aware
        /// taps spread over each 4×4 block, <c>DistanceSoft.shader</c>).</summary>
        private const int Downscale = 4;

        private readonly Material _mat;

        public DistanceSoftPass(Material mat, RenderPassEvent passEvent)
        {
            _mat = mat;
            renderPassEvent = passEvent;
            requiresIntermediateTexture = true; // reads the camera colour and blends back onto it — never the back buffer
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        /// <summary>The low-res target format (rgb = colour, a = distance in metres: needs a float alpha) — the volumetric
        /// light's, checked once there; None = the device cannot render it and the pass records nothing.</summary>
        private static GraphicsFormat Format => VolumetricLightPass.Format;

        private sealed class DownData
        {
            public Material Mat;
            public TextureHandle Source;
            public Vector4 Texel;
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
            if (_mat == null || resources.isActiveTargetBackBuffer || !depth.IsValid() || Format == GraphicsFormat.None)
            {
                return; // no depth texture or nothing to draw into: the camera renders unchanged
            }

            var target = cameraData.cameraTargetDescriptor;
            // #3462: quarter resolution — the blur of a band far away needs no more, and half resolution cost ~0.9 ms at
            // 1080p on a desktop GPU (the plan's budget is ~0.5 ms).
            int w = Mathf.Max(1, (target.width + Downscale - 1) / Downscale), h = Mathf.Max(1, (target.height + Downscale - 1) / Downscale);
            var desc = new TextureDesc(w, h)
            {
                name = "DistanceSoftA",
                colorFormat = Format,
                filterMode = FilterMode.Point, // every read is a point tap: the distance in alpha must not be filtered
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false,
            };
            var a = renderGraph.CreateTexture(desc);
            desc.name = "DistanceSoftB";
            var b = renderGraph.CreateTexture(desc);

            RecordDownsample(renderGraph, resources.activeColorTexture, depth, a, new Vector4(1f / w, 1f / h, w, h));
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, _mat, 1), passName: "Distance Soft Blur H");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, _mat, 2), passName: "Distance Soft Blur V");
            RecordComposite(renderGraph, resources, depth, a);
        }

        /// <summary>Pass 0: the camera colour into the low-res target. Publishes the texel size first — the blur and the
        /// composite read it from there (Blitter does not set <c>_BlitTexture_TexelSize</c>).</summary>
        private void RecordDownsample(RenderGraph renderGraph, TextureHandle color, TextureHandle depth, TextureHandle target, Vector4 texel)
        {
            using (var builder = renderGraph.AddRasterRenderPass<DownData>("Distance Soft Downsample", out var data))
            {
                data.Mat = _mat;
                data.Source = color;
                data.Texel = texel;
                builder.UseTexture(color);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (DownData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(TexelId, d.Texel);
                    RTHandle source = d.Source;
                    Blitter.BlitTexture(ctx.cmd, source, new Vector4(1f, 1f, 0f, 0f), d.Mat, 0);
                });
            }
        }

        /// <summary>Pass 3: the blurred low-res image, upsampled and blended over the camera colour. The camera's
        /// depth-stencil is bound read-only for the stencil test that skips the sky bodies (when there is one: without it
        /// the test passes everywhere, and the moons would only get the same barely visible softening as the hills).</summary>
        private void RecordComposite(RenderGraph renderGraph, UniversalResourceData resources, TextureHandle depth, TextureHandle blurred)
        {
            using (var builder = renderGraph.AddRasterRenderPass<CompositeData>("Distance Soft Composite", out var data))
            {
                data.Mat = _mat;
                data.Source = blurred;
                builder.UseTexture(blurred);
                builder.UseTexture(depth);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write); // loads: the pass blends onto it
                TextureHandle stencil = resources.activeDepthTexture;
                if (stencil.IsValid() && !stencil.Equals(depth))
                {
                    builder.SetRenderAttachmentDepth(stencil, AccessFlags.Read);
                }

                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                {
                    RTHandle source = d.Source;
                    Blitter.BlitTexture(ctx.cmd, source, new Vector4(1f, 1f, 0f, 0f), d.Mat, 3);
                });
            }
        }
    }
}

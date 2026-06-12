using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// URP version of the holographic visor composite (the Built-in path uses <see cref="VisorComposite"/> via
    /// OnRenderImage, which URP never calls). A render-graph blit pass laid over the main camera AFTER
    /// post-processing: world colour arrives as <c>_BlitTexture</c>, the separately-rendered HUD as
    /// <c>_HudTex</c>, composited by the URP SubShader of <c>BlocksBeyondTheStars/Visor</c>. The pass is enqueued from
    /// <c>RenderPipelineManager.beginCameraRendering</c> each frame (code-only — no renderer-asset editing).
    /// Owned + parameterised by <see cref="VisorHud"/>; degrades to nothing if the shader/material is missing.
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

        /// <summary>The render-graph pass: blit cameraColor → temp through the visor material, then swap the
        /// camera colour to the composited target (the documented URP "blit with material" pattern).</summary>
        private sealed class VisorPass : ScriptableRenderPass
        {
            private readonly Material _mat;

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

                var blit = new RenderGraphUtils.BlitMaterialParameters(source, dest, _mat, 0);
                renderGraph.AddBlitPass(blit, passName: "Visor HUD Composite");
                resources.cameraColor = dest;
            }
        }
    }
}

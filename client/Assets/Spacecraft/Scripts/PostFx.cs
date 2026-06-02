using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A lightweight code-only post-processing stack for the Built-in render pipeline (no PPv2 package):
    /// <b>bloom</b> (bright-pass + separable Gaussian, HDR), <b>ACES filmic tonemapping</b> + exposure, a
    /// soft <b>vignette</b>, and optional <b>SSAO</b> (screen-space ambient occlusion from the camera's
    /// depth-normals). Driven entirely from <see cref="OnRenderImage"/> with the always-included
    /// <c>Spacecraft/Post*</c> shaders. Preset-gated: off on Potato/Low, bloom+tonemap on Medium, +AO on
    /// High. Falls back to a plain copy if the shaders are missing.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PostFx : MonoBehaviour
    {
        public bool Bloom = true;
        public bool Tonemap = true;
        public bool Ao = false;

        public float BloomThreshold = 0.85f;
        public float BloomIntensity = 0.7f;
        public float Exposure = 1.12f;
        public float Vignette = 0.22f;
        public float AoIntensity = 0.7f;
        public float AoRadius = 1.0f;

        private Camera _cam;
        private Material _bloom;
        private Material _composite;
        private Material _ao;
        private bool _ready;

        private void OnEnable()
        {
            _cam = GetComponent<Camera>();
            _cam.allowHDR = true;

            _bloom = Make("Spacecraft/PostBloom");
            _composite = Make("Spacecraft/PostComposite");
            _ao = Make("Spacecraft/PostAO");
            _ready = _composite != null;

            if (Ao && _ao != null)
            {
                _cam.depthTextureMode |= DepthTextureMode.DepthNormals;
            }
        }

        private static Material Make(string shaderName)
        {
            var sh = Shader.Find(shaderName);
            return sh != null ? new Material(sh) { hideFlags = HideFlags.HideAndDontSave } : null;
        }

        /// <summary>Configures the stack from the player's quality preset.</summary>
        public void ApplyPreset(QualityPreset preset, bool reducedEffects)
        {
            Bloom = !reducedEffects && preset >= QualityPreset.Medium;
            Tonemap = preset >= QualityPreset.Low;
            Ao = !reducedEffects && preset >= QualityPreset.High;
            if (_cam != null && Ao && _ao != null)
            {
                _cam.depthTextureMode |= DepthTextureMode.DepthNormals;
            }
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!_ready || (!Bloom && !Tonemap && !Ao))
            {
                Graphics.Blit(src, dst); // nothing enabled (e.g. Potato) → straight copy
                return;
            }

            // Ambient occlusion → a screen-size buffer the composite multiplies in.
            RenderTexture aoRT = null;
            if (Ao && _ao != null)
            {
                aoRT = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
                _ao.SetFloat("_AoIntensity", AoIntensity);
                _ao.SetFloat("_AoRadius", AoRadius);
                Graphics.Blit(src, aoRT, _ao, 0);
            }

            // Bloom → a blurred half-res buffer of the bright pixels.
            RenderTexture bloomRT = null;
            if (Bloom && _bloom != null)
            {
                int w = Mathf.Max(1, src.width / 2);
                int h = Mathf.Max(1, src.height / 2);
                var pre = RenderTexture.GetTemporary(w, h, 0, src.format);
                var tmp = RenderTexture.GetTemporary(w, h, 0, src.format);

                _bloom.SetFloat("_Threshold", BloomThreshold);
                Graphics.Blit(src, pre, _bloom, 0); // prefilter

                for (int it = 0; it < 2; it++)
                {
                    _bloom.SetVector("_BlurDir", new Vector4(1f / w, 0f, 0f, 0f));
                    Graphics.Blit(pre, tmp, _bloom, 1);
                    _bloom.SetVector("_BlurDir", new Vector4(0f, 1f / h, 0f, 0f));
                    Graphics.Blit(tmp, pre, _bloom, 1);
                }

                RenderTexture.ReleaseTemporary(tmp);
                bloomRT = pre;
            }

            _composite.SetTexture("_BloomTex", bloomRT != null ? bloomRT : Texture2D.blackTexture);
            _composite.SetTexture("_AoTex", aoRT != null ? aoRT : Texture2D.whiteTexture);
            _composite.SetFloat("_BloomIntensity", Bloom ? BloomIntensity : 0f);
            _composite.SetFloat("_Exposure", Tonemap ? Exposure : 1f);
            _composite.SetFloat("_Tonemap", Tonemap ? 1f : 0f);
            _composite.SetFloat("_Vignette", Vignette);
            Graphics.Blit(src, dst, _composite, 0);

            if (bloomRT != null)
            {
                RenderTexture.ReleaseTemporary(bloomRT);
            }

            if (aoRT != null)
            {
                RenderTexture.ReleaseTemporary(aoRT);
            }
        }

        private void OnDisable()
        {
            if (_bloom != null) Destroy(_bloom);
            if (_composite != null) Destroy(_composite);
            if (_ao != null) Destroy(_ao);
        }
    }
}

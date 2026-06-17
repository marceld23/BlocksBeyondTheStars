using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Holographic visor HUD (item 9). The diegetic HUD canvases are rendered through a dedicated UI
    /// camera into a render texture; a fullscreen <c>BlocksBeyondTheStars/Visor</c> pass then composites that over
    /// the post-processed world with barrel curvature, chromatic fringing, scanlines, a fresnel rim glow,
    /// glow and a faint reflection — so the HUD reads as projected onto the inside of the suit visor.
    /// Always on. Degrades safely: if the layer or shader is unavailable the HUD stays a normal
    /// screen-space overlay (<see cref="UiKit.HudCamera"/> stays null), so it is never lost.
    /// </summary>
    public sealed class VisorHud : MonoBehaviour
    {
        public Camera MainCamera;

        /// <summary>Client settings — read live so the "visor effect" toggle takes effect immediately.</summary>
        public ClientSettings Settings;

        /// <summary>Free layer the diegetic HUD renders on (matches the "VisorHud" entry in TagManager, but
        /// used by index so it works even when a batch build hasn't baked the layer name).</summary>
        private const int HudLayerIndex = 8;

        private Camera _hudCam;
        private RenderTexture _rt;
        private VisorComposite _composite;     // Built-in RP path (OnRenderImage)
        private VisorUrpCompositor _urp;       // URP path (render-graph blit after post)
        private float _urpTime;
        private int _w, _h;
        private Vector3 _lastEuler;
        private Vector2 _parallax;
        private bool _active;
        private float _intensity = 0.6f; // subtler visor effect by default (was 1.0 — user: "nicht ganz so stark")

        /// <summary>Always-on, just gentler under the reduced-effects / low-end preset.</summary>
        public void ApplyPreset(QualityPreset preset, bool reducedEffects)
        {
            // Much subtler by default: the visor composite only began rendering once Phase 0 forced an
            // intermediate colour target, and at the old 0.6 it washed the whole frame out. Keep it a faint
            // stylistic touch so the world stays crisp and readable.
            _intensity = reducedEffects ? 0.12f : 0.22f;
            if (_composite != null)
            {
                _composite.Intensity = _intensity;
            }
        }

        private void Start()
        {
            if (MainCamera == null)
            {
                enabled = false;
                return;
            }

            // Prefer the named layer (nice in the Editor) but fall back to a fixed free index: a batch build
            // doesn't always bake a freshly-added layer NAME, yet the index works the same for cull masks.
            int layer = LayerMask.NameToLayer("VisorHud");
            if (layer < 0)
            {
                layer = HudLayerIndex;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/Visor");
            if (shader == null)
            {
                Debug.LogWarning("[VisorHud] disabled — BlocksBeyondTheStars/Visor shader not found; flat HUD fallback.");
                enabled = false;
                return;
            }

            UiKit.HudLayer = layer;
            CreateRt();

            var camGo = new GameObject("HUD Camera");
            camGo.transform.SetParent(transform, false);
            _hudCam = camGo.AddComponent<Camera>();
            _hudCam.clearFlags = CameraClearFlags.SolidColor;
            _hudCam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent: only the HUD ends up in the RT
            _hudCam.cullingMask = 1 << layer;
            _hudCam.nearClipPlane = 0.1f;
            _hudCam.farClipPlane = 100f;
            _hudCam.depth = MainCamera.depth - 1; // render the HUD RT before the main camera composites it
            _hudCam.allowHDR = false;
            _hudCam.allowMSAA = false;
            _hudCam.useOcclusionCulling = false;
            _hudCam.targetTexture = _rt;

            // Keep the diegetic HUD out of the main camera's image so only the visor pass shows it.
            MainCamera.cullingMask &= ~(1 << layer);

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // URP: composite via a render-graph blit pass after post (OnRenderImage never runs under URP).
                _urp = new VisorUrpCompositor(MainCamera, shader);
            }
            else
            {
                _composite = MainCamera.gameObject.AddComponent<VisorComposite>();
                _composite.VisorShader = shader;
                _composite.Hud = _rt;
                _composite.Intensity = _intensity;
                _composite.Effects = Settings == null || Settings.VisorEffects;
            }

            UiKit.HudCamera = _hudCam; // diegetic canvases created from here on target this camera
            _lastEuler = MainCamera.transform.eulerAngles;
            _active = true;
            Debug.Log($"[VisorHud] engaged — holographic HUD on layer {layer}, RT {_w}x{_h}, pipeline: "
                      + (GraphicsSettings.currentRenderPipeline != null ? "URP (render graph)" : "Built-in (OnRenderImage)") + ".");
        }

        private void CreateRt()
        {
            _w = Mathf.Max(2, Screen.width);
            _h = Mathf.Max(2, Screen.height);
            // URP's render graph requires a camera output texture to carry a depth buffer; Built-in is happy
            // without one (and skipping it saves memory there).
            int depth = GraphicsSettings.currentRenderPipeline != null ? 24 : 0;
            _rt = new RenderTexture(_w, _h, depth, RenderTextureFormat.ARGB32)
            {
                name = "VisorHudRT",
                wrapMode = TextureWrapMode.Clamp,   // curvature samples just past the edge → transparent
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1,
            };
            _rt.Create();
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            // Live "visor effect" toggle: read each frame so flipping it in Settings applies at once.
            if (_composite != null && Settings != null)
            {
                _composite.Effects = Settings.VisorEffects;
            }

            // Resolution change → rebuild the RT and re-target.
            if (Screen.width != _w || Screen.height != _h)
            {
                if (_hudCam != null)
                {
                    _hudCam.targetTexture = null;
                }

                if (_rt != null)
                {
                    _rt.Release();
                    Destroy(_rt);
                }

                CreateRt();
                if (_hudCam != null)
                {
                    _hudCam.targetTexture = _rt;
                }

                if (_composite != null)
                {
                    _composite.Hud = _rt;
                }
            }

            // Parallax: the projection lags head turns slightly, then eases back to centre.
            var euler = MainCamera.transform.eulerAngles;
            float dyaw = Mathf.DeltaAngle(_lastEuler.y, euler.y);
            float dpitch = Mathf.DeltaAngle(_lastEuler.x, euler.x);
            _lastEuler = euler;
            var target = new Vector2(Mathf.Clamp(dyaw, -8f, 8f) * 0.0016f, Mathf.Clamp(-dpitch, -8f, 8f) * 0.0016f);
            _parallax = Vector2.Lerp(_parallax, target, 0.25f) * 0.85f;
            if (_composite != null)
            {
                _composite.Parallax = _parallax;
            }

            // URP path: drive the visor material here each frame (the Built-in path does this in
            // VisorComposite.OnRenderImage). Same subtle defaults + the same flat-HUD "effects off" branch.
            if (_urp?.Material is { } m)
            {
                _urpTime += Time.deltaTime;
                bool fx = Settings == null || Settings.VisorEffects;
                m.SetTexture("_HudTex", _rt);
                m.SetFloat("_VisorTime", _urpTime);
                m.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f);
                m.SetFloat("_HudOpacity", 0.97f);
                m.SetColor("_RimColor", ShaderColor.Srgb(new Color(0.4f, 0.85f, 1f, 1f)));
                m.SetFloat("_ScanCount", Mathf.Max(120f, _h * 0.5f));
                m.SetFloat("_Intensity", fx ? _intensity : 0f);
                m.SetFloat("_Curvature", fx ? 0.012f : 0f);   // gentle bow (was 0.045 — warped/softened the HUD)
                m.SetFloat("_Chroma", fx ? 0.0015f : 0f);     // whisper of fringe (was 0.005)
                m.SetVector("_Parallax", fx ? new Vector4(_parallax.x, _parallax.y, 0f, 0f) : Vector4.zero);
                m.SetFloat("_Glow", fx ? 0.35f : 0f);         // softer hologram bloom (was 0.6)
                m.SetFloat("_Reflect", fx ? 0.02f : 0f);      // barely-there world reflection (was 0.08 — ghosted the frame)
                m.SetFloat("_RimIntensity", fx ? 0.05f : 0f); // faint edge glow (was 0.10)
            }
        }

        private void OnDestroy()
        {
            if (UiKit.HudCamera == _hudCam)
            {
                UiKit.HudCamera = null;
            }

            if (_composite != null)
            {
                Destroy(_composite);
            }

            _urp?.Dispose();
            _urp = null;

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }

    /// <summary>
    /// Keeps a diegetic HUD canvas (and every child, including dynamically added ones) on the visor HUD
    /// layer, so the HUD camera renders them and the main camera doesn't. Re-applies only when the
    /// hierarchy changes (cheap), covering toasts/hotbar/etc. that appear after the canvas is built.
    /// </summary>
    public sealed class HudLayerEnforcer : MonoBehaviour
    {
        public int Layer;
        private int _count = -1;

        private void LateUpdate()
        {
            int c = transform.hierarchyCount;
            if (c == _count)
            {
                return;
            }

            _count = c;
            SetLayerRecursive(transform, Layer);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursive(t.GetChild(i), layer);
            }
        }
    }

    /// <summary>
    /// Fullscreen composite (after <see cref="PostFx"/> on the main camera): overlays the separately
    /// rendered HUD render texture onto the post-processed world through the <c>BlocksBeyondTheStars/Visor</c>
    /// shader. Falls back to a straight copy if the material/RT is missing.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VisorComposite : MonoBehaviour
    {
        public Shader VisorShader;
        public RenderTexture Hud;
        public float Intensity = 1f;
        public Vector2 Parallax;

        /// <summary>When false the HUD composites cleanly with NO stylisation (no curvature/chroma/scanlines/
        /// glow) — a flat, maximally-readable overlay (the player's "visor effect off" setting).</summary>
        public bool Effects = true;

        private Material _mat;
        private float _time;

        private void Update() => _time += Time.deltaTime;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            // Build the material lazily: OnEnable runs synchronously inside AddComponent (before VisorShader is
            // assigned), so create it here once the shader is available — otherwise the HUD would never composite.
            if (_mat == null && VisorShader != null)
            {
                _mat = new Material(VisorShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (_mat == null || Hud == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            _mat.SetTexture("_HudTex", Hud);
            _mat.SetFloat("_VisorTime", _time);
            _mat.SetFloat("_Aspect", src.height > 0 ? (float)src.width / src.height : 1.78f);
            _mat.SetFloat("_HudOpacity", 0.97f);
            _mat.SetColor("_RimColor", ShaderColor.Srgb(new Color(0.4f, 0.85f, 1f, 1f)));
            _mat.SetFloat("_ScanCount", Mathf.Max(120f, Hud.height * 0.5f));

            if (Effects)
            {
                // Stylised — but kept very subtle so the world stays crisp/readable (user: visor was far too strong).
                _mat.SetFloat("_Intensity", Intensity);
                _mat.SetFloat("_Curvature", 0.012f);  // gentle bow (was 0.045)
                _mat.SetFloat("_Chroma", 0.0015f);    // whisper of fringe (was 0.005)
                _mat.SetVector("_Parallax", new Vector4(Parallax.x, Parallax.y, 0f, 0f));
                _mat.SetFloat("_Glow", 0.35f);        // softer hologram glow (was 0.6)
                _mat.SetFloat("_Reflect", 0.02f);     // barely-there reflection (was 0.08)
                _mat.SetFloat("_RimIntensity", 0.05f); // faint edge glow (was 0.10)
            }
            else
            {
                // Off — a clean, flat HUD overlay: no warp, no fringe, no scanlines/glow/rim/reflection.
                _mat.SetFloat("_Intensity", 0f);
                _mat.SetFloat("_Curvature", 0f);
                _mat.SetFloat("_Chroma", 0f);
                _mat.SetVector("_Parallax", Vector4.zero);
                _mat.SetFloat("_Glow", 0f);
                _mat.SetFloat("_Reflect", 0f);
                _mat.SetFloat("_RimIntensity", 0f);
            }

            Graphics.Blit(src, dst, _mat, 0);
        }

        private void OnDisable()
        {
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
        }
    }
}

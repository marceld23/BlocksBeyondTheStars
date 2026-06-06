using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Holographic visor HUD (item 9). The diegetic HUD canvases are rendered through a dedicated UI
    /// camera into a render texture; a fullscreen <c>Spacecraft/Visor</c> pass then composites that over
    /// the post-processed world with barrel curvature, chromatic fringing, scanlines, a fresnel rim glow,
    /// glow and a faint reflection — so the HUD reads as projected onto the inside of the suit visor.
    /// Always on. Degrades safely: if the layer or shader is unavailable the HUD stays a normal
    /// screen-space overlay (<see cref="UiKit.HudCamera"/> stays null), so it is never lost.
    /// </summary>
    public sealed class VisorHud : MonoBehaviour
    {
        public Camera MainCamera;

        private Camera _hudCam;
        private RenderTexture _rt;
        private VisorComposite _composite;
        private int _w, _h;
        private Vector3 _lastEuler;
        private Vector2 _parallax;
        private bool _active;
        private float _intensity = 1f;

        /// <summary>Always-on, just gentler under the reduced-effects / low-end preset.</summary>
        public void ApplyPreset(QualityPreset preset, bool reducedEffects)
        {
            _intensity = reducedEffects ? 0.5f : 1f;
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

            int layer = LayerMask.NameToLayer("VisorHud");
            var shader = Shader.Find("Spacecraft/Visor");
            if (layer < 0 || shader == null)
            {
                enabled = false; // no visor pipeline — diegetic canvases fall back to plain overlay
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

            _composite = MainCamera.gameObject.AddComponent<VisorComposite>();
            _composite.VisorShader = shader;
            _composite.Hud = _rt;
            _composite.Intensity = _intensity;

            UiKit.HudCamera = _hudCam; // diegetic canvases created from here on target this camera
            _lastEuler = MainCamera.transform.eulerAngles;
            _active = true;
        }

        private void CreateRt()
        {
            _w = Mathf.Max(2, Screen.width);
            _h = Mathf.Max(2, Screen.height);
            _rt = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGB32)
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
    /// rendered HUD render texture onto the post-processed world through the <c>Spacecraft/Visor</c>
    /// shader. Falls back to a straight copy if the material/RT is missing.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VisorComposite : MonoBehaviour
    {
        public Shader VisorShader;
        public RenderTexture Hud;
        public float Intensity = 1f;
        public Vector2 Parallax;

        private Material _mat;
        private float _time;

        private void OnEnable()
        {
            if (VisorShader != null && _mat == null)
            {
                _mat = new Material(VisorShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private void Update() => _time += Time.deltaTime;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null || Hud == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            _mat.SetTexture("_HudTex", Hud);
            _mat.SetFloat("_Intensity", Intensity);
            _mat.SetFloat("_Curvature", 0.06f);
            _mat.SetFloat("_Chroma", 0.012f);
            _mat.SetFloat("_ScanCount", Mathf.Max(120f, Hud.height * 0.5f));
            _mat.SetFloat("_VisorTime", _time);
            _mat.SetVector("_Parallax", new Vector4(Parallax.x, Parallax.y, 0f, 0f));
            _mat.SetFloat("_Aspect", src.height > 0 ? (float)src.width / src.height : 1.78f);
            _mat.SetFloat("_HudOpacity", 0.96f);
            _mat.SetFloat("_Glow", 0.6f);
            _mat.SetFloat("_Reflect", 0.1f);
            _mat.SetColor("_RimColor", new Color(0.4f, 0.85f, 1f, 1f));
            _mat.SetFloat("_RimIntensity", 0.12f);
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

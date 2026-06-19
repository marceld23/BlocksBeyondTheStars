using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Heat-haze shimmer on hot worlds ("Welten reicher"): a camera-parented full-screen quad re-displays the
    /// scene with a faint, rising, depth-faded UV warp so the far field boils in the heat. Atmosphere-gated like
    /// the auroras — only on worlds with real air (skip airless space-sky bodies + the NoAirTemperature sentinel)
    /// and only with the visor open to the sky. The <see cref="HeatHaze"/> shader does the distortion; here we
    /// size the quad to the frustum, drive the global <c>_HeatAmp</c> from the air temperature, and switch the
    /// quad off entirely when there is no heat so normal worlds pay nothing. URP only (the shader needs the
    /// opaque + depth textures); under Built-in RP the shader is a no-op and this just idles.
    /// </summary>
    public sealed class HeatShimmer : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float HotWarmC = 38f;  // shimmer begins at/above this air temperature
        private const float HotMaxC = 75f;   // full strength at/above this
        private static readonly int HeatAmpId = Shader.PropertyToID("_HeatAmp");

        private Transform _quad;
        private float _amp; // smoothed 0..1 heat amount

        private void Start()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/HeatHaze");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "HeatHaze";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.GetComponent<Renderer>().sharedMaterial = new Material(shader);
            _quad = go.transform;
            _quad.SetParent((Camera != null ? Camera.transform : transform), false);
            go.SetActive(false);
        }

        private void Update()
        {
            if (_quad == null)
            {
                return;
            }

            var cam = Camera;
            var env = Game != null ? Game.Environment : null;
            // Needs real air (not airless/space-sky, guard the −999 sentinel), open sky, and genuinely hot.
            bool hotWorld = env != null && !env.SpaceSky && env.Temperature > HotWarmC;
            bool show = hotWorld && cam != null && Game.ExposedToSky && !Game.SpaceViewActive;

            float target = 0f;
            if (show)
            {
                target = Mathf.Clamp01(Mathf.InverseLerp(HotWarmC, HotMaxC, env.Temperature));
            }

            _amp = Mathf.MoveTowards(_amp, target, Time.deltaTime * 0.5f); // shimmer eases in / out
            Shader.SetGlobalFloat(HeatAmpId, _amp);

            bool active = _amp > 0.005f;
            if (_quad.gameObject.activeSelf != active)
            {
                _quad.gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            // Fit the quad to the camera frustum just past the near plane so it always covers the viewport.
            float z = Mathf.Max(cam.nearClipPlane + 0.05f, 0.2f);
            float h = 2f * z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * cam.aspect;
            _quad.localPosition = new Vector3(0f, 0f, z);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(w * 1.05f, h * 1.05f, 1f); // slight overscan to hide edges
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(HeatAmpId, 0f); // never leave the warp on when we stop driving it
        }
    }
}

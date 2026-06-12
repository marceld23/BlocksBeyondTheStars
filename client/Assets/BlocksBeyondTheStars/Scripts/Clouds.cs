using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The surface cloud layer (weather). A dome of soft, camera-facing cloud billboards drifting slowly
    /// with the wind around the player. Colour, count and opacity come from the authoritative
    /// <see cref="Networking.Messages.WorldEnvironment"/>: each planet has its own base cloud colour +
    /// density (frequency / how far they cut visibility), and the live weather state thickens and darkens
    /// them — storms turn the sky into low, dark cloud. Hidden in space, on airless bodies, and on planets
    /// with no cloud cover. Built-in RP, always-included <c>BlocksBeyondTheStars/Cloud</c> shader.
    /// </summary>
    public sealed class Clouds : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const int MaxClouds = 18;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Transform[] _clouds = new Transform[MaxClouds];
        private readonly Material[] _mats = new Material[MaxClouds];
        private readonly float[] _angle = new float[MaxClouds];   // azimuth around the camera (radians)
        private readonly float[] _elev = new float[MaxClouds];    // elevation above the horizon (radians)
        private readonly float[] _dist = new float[MaxClouds];
        private readonly float[] _size = new float[MaxClouds];
        private bool _built;

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            var textures = new Texture2D[4];
            for (int i = 0; i < textures.Length; i++)
            {
                textures[i] = GenerateCloudTexture(1234 + i * 97);
            }

            var rng = new System.Random(20260602);
            for (int i = 0; i < MaxClouds; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Cloud" + i;
                var col = quad.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                var mat = new Material(shader) { mainTexture = textures[i % textures.Length] };
                mat.renderQueue = 3000;
                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _mats[i] = mat;
                _clouds[i] = quad.transform;
                _clouds[i].SetParent(transform, false);
                _clouds[i].gameObject.SetActive(false);

                _angle[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                _elev[i] = 0.12f + (float)rng.NextDouble() * 0.45f;            // ~7°..33°
                _dist[i] = 170f + (float)rng.NextDouble() * 90f;
                _size[i] = 70f + (float)rng.NextDouble() * 80f;
            }

            _built = true;
        }

        private void Update()
        {
            if (!_built || Camera == null)
            {
                return;
            }

            var env = Game != null ? Game.Environment : null;
            bool show = env != null && !env.SpaceSky && (Game == null || !Game.SpaceViewActive) && env.CloudDensity > 0.001f;
            if (!show)
            {
                HideAll();
                return;
            }

            // Day brightness (mirrors Sky): clouds darken at night — uses the player's local time.
            float sunHeight = Mathf.Sin((Game.LocalTimeOfDay - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);

            // Cover: per-planet base raised by the live weather; storms darken the tint heavily.
            float weatherCover, darken, windScale;
            switch (env.Weather)
            {
                case "storm": weatherCover = 0.95f; darken = 0.35f; windScale = 3.0f; break;
                case "rain":  weatherCover = 0.80f; darken = 0.55f; windScale = 1.8f; break;
                case "clouds": weatherCover = 0.60f; darken = 0.80f; windScale = 1.2f; break;
                default:       weatherCover = env.CloudDensity * 0.5f; darken = 1.0f; windScale = 1.0f; break;
            }

            float cover = Mathf.Clamp01(Mathf.Max(env.CloudDensity, weatherCover));
            int count = Mathf.Clamp(Mathf.RoundToInt(MaxClouds * cover), 0, MaxClouds);
            float opacity = Mathf.Lerp(0.45f, 0.95f, cover);

            Color baseColor = Rgb(env.CloudColor);
            // Brightness from day + storm darkening; a touch of the sun colour at low sun (sunset glow).
            float bright = (0.35f + 0.65f * day) * darken;
            Color tint = baseColor * bright;
            tint = Color.Lerp(tint, tint * Rgb(env.SunColor), 0.25f * (1f - day));
            tint.a = opacity;

            Vector3 camPos = Camera.transform.position;
            float wind = Time.time * 0.012f * windScale;

            for (int i = 0; i < MaxClouds; i++)
            {
                if (i >= count)
                {
                    if (_clouds[i].gameObject.activeSelf)
                    {
                        _clouds[i].gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!_clouds[i].gameObject.activeSelf)
                {
                    _clouds[i].gameObject.SetActive(true);
                }

                float a = _angle[i] + wind;
                float ce = Mathf.Cos(_elev[i]);
                Vector3 dir = new Vector3(Mathf.Cos(a) * ce, Mathf.Sin(_elev[i]), Mathf.Sin(a) * ce);
                _clouds[i].position = camPos + dir * _dist[i];
                _clouds[i].rotation = Quaternion.LookRotation(camPos - _clouds[i].position);
                // Clouds are wider than tall.
                _clouds[i].localScale = new Vector3(_size[i] * 1.8f, _size[i], _size[i]);
                _mats[i].SetColor(ColorId, ShaderColor.Srgb(tint));
            }
        }

        private void HideAll()
        {
            if (!_built)
            {
                return;
            }

            for (int i = 0; i < MaxClouds; i++)
            {
                if (_clouds[i] != null && _clouds[i].gameObject.activeSelf)
                {
                    _clouds[i].gameObject.SetActive(false);
                }
            }
        }

        private void OnDisable() => HideAll();

        /// <summary>A soft, irregular cloud puff: white with a noise-broken alpha that fades to the edges.</summary>
        private static Texture2D GenerateCloudTexture(int seed)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var rng = new System.Random(seed);
            // A few overlapping soft lobes give a billowy silhouette; value noise breaks up the fill.
            int lobes = 4 + rng.Next(3);
            var lx = new float[lobes];
            var ly = new float[lobes];
            var lr = new float[lobes];
            for (int k = 0; k < lobes; k++)
            {
                lx[k] = 0.30f + (float)rng.NextDouble() * 0.40f;
                ly[k] = 0.38f + (float)rng.NextDouble() * 0.24f;
                lr[k] = 0.14f + (float)rng.NextDouble() * 0.16f;
            }

            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1), v = y / (float)(n - 1);
                    float field = 0f;
                    for (int k = 0; k < lobes; k++)
                    {
                        float dx = (u - lx[k]) / lr[k];
                        float dy = (v - ly[k]) / lr[k];
                        field += Mathf.Exp(-(dx * dx + dy * dy));
                    }

                    float a = Mathf.Clamp01(field - 0.55f);
                    a = Mathf.SmoothStep(0f, 1f, a);
                    // Slightly grey the undersides for depth.
                    float shade = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(v * 1.2f));
                    px[y * n + x] = new Color(shade, shade, shade, a * 0.95f);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}

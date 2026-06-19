using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The surface cloud layer (weather). A two-layer dome of soft, camera-facing cloud billboards drifting
    /// with the wind around the player. There are three cloud archetypes — puffy <b>cumulus</b> and broad flat
    /// <b>stratus</b> low down, thin wispy <b>cirrus</b> high up — each from its own pool of noise-broken
    /// textures (no two neighbours share a silhouette). Each puff drifts at its own speed (near clouds faster),
    /// slowly swells and fades on a lifecycle, and is lit from the real sun direction so it has a bright and a
    /// shadowed side (warm at sunset). Colour, count and opacity come from the authoritative
    /// <see cref="Networking.Messages.WorldEnvironment"/>: each planet has its own base cloud colour + density,
    /// and the live weather thickens/darkens them, raises low storm towers and hides the high cirrus in storms.
    /// Hidden in space, on airless bodies, and on planets with no cloud cover. Built-in/URP, always-included
    /// <c>BlocksBeyondTheStars/Cloud</c> shader.
    /// </summary>
    public sealed class Clouds : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const int MaxClouds = 20;
        private const int HighFrom = 14;       // clouds [HighFrom..MaxClouds) are the high cirrus layer
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int CloudSunDirId = Shader.PropertyToID("_CloudSunDir");
        private static readonly int SunShadeId = Shader.PropertyToID("_SunShade");
        private static readonly int BulgeId = Shader.PropertyToID("_Bulge");
        private static readonly int ScSunDirId = Shader.PropertyToID("_Sc_SunDir");

        private enum CloudKind { Cumulus, Stratus, Cirrus }

        private readonly Transform[] _clouds = new Transform[MaxClouds];
        private readonly Material[] _mats = new Material[MaxClouds];
        private readonly float[] _angle = new float[MaxClouds];   // azimuth around the camera (radians)
        private readonly float[] _elev = new float[MaxClouds];    // elevation above the horizon (radians)
        private readonly float[] _dist = new float[MaxClouds];
        private readonly float[] _size = new float[MaxClouds];
        private readonly float[] _aspect = new float[MaxClouds];  // width / height
        private readonly float[] _vscale = new float[MaxClouds];  // vertical scale (flat stratus < cumulus)
        private readonly float[] _drift = new float[MaxClouds];   // per-cloud wind multiplier (near = faster)
        private readonly float[] _phase = new float[MaxClouds];   // lifecycle phase offset
        private readonly float[] _pulse = new float[MaxClouds];   // lifecycle speed
        private readonly bool[] _high = new bool[MaxClouds];      // the high cirrus layer
        private bool _built;

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            // Twelve silhouettes: 6 cumulus, 3 stratus, 3 cirrus — far more variety than the old four.
            var textures = new Texture2D[12];
            for (int i = 0; i < 6; i++)
            {
                textures[i] = GenerateCloudTexture(1234 + i * 97, CloudKind.Cumulus);
            }

            for (int i = 0; i < 3; i++)
            {
                textures[6 + i] = GenerateCloudTexture(5500 + i * 71, CloudKind.Stratus);
            }

            for (int i = 0; i < 3; i++)
            {
                textures[9 + i] = GenerateCloudTexture(8800 + i * 53, CloudKind.Cirrus);
            }

            var rng = new System.Random(20260602);
            // Shuffled per-archetype pools so adjacent clouds rarely repeat a silhouette.
            int[] cumulus = Shuffle(new[] { 0, 1, 2, 3, 4, 5 }, rng);
            int[] stratus = Shuffle(new[] { 6, 7, 8 }, rng);
            int[] cirrus = Shuffle(new[] { 9, 10, 11 }, rng);
            int ci = 0, si = 0, ri = 0;

            for (int i = 0; i < MaxClouds; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Cloud" + i;
                var col = quad.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                bool high = i >= HighFrom;
                int texIndex;
                if (high)
                {
                    texIndex = cirrus[ri++ % cirrus.Length];
                    _elev[i] = 0.60f + (float)rng.NextDouble() * 0.45f;       // ~34°..60° (toward the zenith)
                    _dist[i] = 240f + (float)rng.NextDouble() * 90f;
                    _aspect[i] = 2.6f + (float)rng.NextDouble() * 1.9f;
                    _vscale[i] = 0.45f;
                    _size[i] = 90f + (float)rng.NextDouble() * 70f;
                }
                else if (rng.NextDouble() < 0.3)
                {
                    // Broad flat stratus.
                    texIndex = stratus[si++ % stratus.Length];
                    _elev[i] = 0.06f + (float)rng.NextDouble() * 0.26f;       // ~3°..18° (low)
                    _dist[i] = 150f + (float)rng.NextDouble() * 100f;
                    _aspect[i] = 3.0f + (float)rng.NextDouble() * 2.0f;
                    _vscale[i] = 0.5f;
                    _size[i] = 80f + (float)rng.NextDouble() * 90f;
                }
                else
                {
                    // Puffy cumulus.
                    texIndex = cumulus[ci++ % cumulus.Length];
                    _elev[i] = 0.12f + (float)rng.NextDouble() * 0.48f;       // ~7°..34°
                    _dist[i] = 150f + (float)rng.NextDouble() * 110f;
                    _aspect[i] = 1.3f + (float)rng.NextDouble() * 0.9f;
                    _vscale[i] = 1.0f;
                    _size[i] = 70f + (float)rng.NextDouble() * 90f;
                }

                var mat = new Material(shader) { mainTexture = textures[texIndex] };
                mat.renderQueue = 3000;
                mat.SetFloat(SunShadeId, 1f);
                mat.SetFloat(BulgeId, 1f);    // surface puffs fake a volume normal from the UV
                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _mats[i] = mat;
                _high[i] = high;
                _clouds[i] = quad.transform;
                _clouds[i].SetParent(transform, false);
                _clouds[i].gameObject.SetActive(false);

                _angle[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                _drift[i] = 200f / _dist[i];                                  // near clouds appear to drift faster
                _phase[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                _pulse[i] = 0.05f + (float)rng.NextDouble() * 0.08f;
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

            // Cover: per-planet base raised by the live weather. Storms also raise low cloud "towers"
            // (stormTall) and pull the thin high cirrus out of the sky (cirrusFade).
            float weatherCover, darken, windScale, stormTall, cirrusFade;
            switch (env.Weather)
            {
                case "storm": weatherCover = 0.95f; darken = 0.35f; windScale = 3.0f; stormTall = 0.9f; cirrusFade = 0.0f; break;
                case "rain":  weatherCover = 0.80f; darken = 0.55f; windScale = 1.8f; stormTall = 0.3f; cirrusFade = 0.2f; break;
                case "clouds": weatherCover = 0.60f; darken = 0.80f; windScale = 1.2f; stormTall = 0.0f; cirrusFade = 0.5f; break;
                default:       weatherCover = env.CloudDensity * 0.5f; darken = 1.0f; windScale = 1.0f; stormTall = 0.0f; cirrusFade = 1.0f; break;
            }

            float cover = Mathf.Clamp01(Mathf.Max(env.CloudDensity, weatherCover));
            int count = Mathf.Clamp(Mathf.RoundToInt(MaxClouds * cover), 0, MaxClouds);
            float opacity = Mathf.Lerp(0.45f, 0.95f, cover);

            Color baseColor = Rgb(env.CloudColor);
            // Brightness from day + storm darkening; a touch of the sun colour at low sun (sunset glow).
            float bright = (0.35f + 0.65f * day) * darken;
            Color tint = baseColor * bright;
            tint = Color.Lerp(tint, tint * Rgb(env.SunColor), 0.25f * (1f - day));

            // World-space direction toward the sun (shared with Sky/SkyBodies). Lets each puff have a lit
            // and a shadowed side; falls back to straight up before the first lighting update.
            Vector4 sunDir = Shader.GetGlobalVector(ScSunDirId);
            if (((Vector3)sunDir).sqrMagnitude < 1e-4f)
            {
                sunDir = new Vector4(0f, 1f, 0f, 0f);
            }

            Vector3 camPos = Camera.transform.position;
            float wind = Time.time * 0.012f * windScale;
            float t = Time.time;

            for (int i = 0; i < MaxClouds; i++)
            {
                // High cirrus is hidden in heavy weather; everything past the cover count is off.
                bool active = i < count && !(_high[i] && cirrusFade <= 0.01f);
                if (!active)
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

                // Lifecycle: each puff slowly swells and fades, and gently bobs — clouds form and dissipate
                // instead of riding a rigid ring.
                float life = 0.6f + 0.4f * Mathf.Sin(t * _pulse[i] + _phase[i]);
                float sizeMul = 0.82f + 0.32f * life;
                float alphaLife = 0.55f + 0.45f * life;
                float elev = _elev[i] + 0.015f * Mathf.Sin(t * 0.04f + _phase[i]);

                float a = _angle[i] + wind * _drift[i];
                float ce = Mathf.Cos(elev);
                Vector3 dir = new Vector3(Mathf.Cos(a) * ce, Mathf.Sin(elev), Mathf.Sin(a) * ce);
                _clouds[i].position = camPos + dir * _dist[i];
                _clouds[i].rotation = Quaternion.LookRotation(camPos - _clouds[i].position);

                float vs = _vscale[i] * (_high[i] ? 1f : 1f + stormTall);     // storm towers grow low clouds tall
                float s = _size[i] * sizeMul;
                _clouds[i].localScale = new Vector3(s * _aspect[i], s * vs, s);

                float alpha = opacity * alphaLife;
                if (_high[i])
                {
                    alpha *= cirrusFade * 0.7f;                               // cirrus is thinner / fades first
                }

                Color cloudTint = tint;
                cloudTint.a = Mathf.Clamp01(alpha);
                Color shade = cloudTint * 0.45f;
                shade.a = cloudTint.a;

                _mats[i].SetColor(ColorId, ShaderColor.Srgb(cloudTint));
                _mats[i].SetColor(ShadeColorId, ShaderColor.Srgb(shade));
                _mats[i].SetVector(CloudSunDirId, sunDir);
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

        private static int[] Shuffle(int[] a, System.Random rng)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }

            return a;
        }

        /// <summary>A soft cloud puff with noise-broken, fractal edges. The archetype sets the silhouette:
        /// cumulus = billowy lobes, stratus = a broad flat slab, cirrus = thin wind-stretched streaks.</summary>
        private static Texture2D GenerateCloudTexture(int seed, CloudKind kind)
        {
            const int n = 160;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var rng = new System.Random(seed);
            int lobes = kind == CloudKind.Cirrus ? 3 + rng.Next(2)
                      : kind == CloudKind.Stratus ? 5 + rng.Next(3)
                      : 4 + rng.Next(3);

            var lx = new float[lobes];
            var ly = new float[lobes];
            var lrx = new float[lobes];
            var lry = new float[lobes];
            for (int k = 0; k < lobes; k++)
            {
                switch (kind)
                {
                    case CloudKind.Cirrus: // long, paper-thin horizontal strokes
                        lx[k] = 0.20f + (float)rng.NextDouble() * 0.60f;
                        ly[k] = 0.30f + (float)rng.NextDouble() * 0.40f;
                        lrx[k] = 0.30f + (float)rng.NextDouble() * 0.20f;
                        lry[k] = 0.015f + (float)rng.NextDouble() * 0.030f;
                        break;
                    case CloudKind.Stratus: // wide, low slabs
                        lx[k] = 0.15f + (float)rng.NextDouble() * 0.70f;
                        ly[k] = 0.42f + (float)rng.NextDouble() * 0.16f;
                        lrx[k] = 0.18f + (float)rng.NextDouble() * 0.16f;
                        lry[k] = 0.06f + (float)rng.NextDouble() * 0.05f;
                        break;
                    default: // cumulus cauliflower
                        lx[k] = 0.30f + (float)rng.NextDouble() * 0.40f;
                        ly[k] = 0.36f + (float)rng.NextDouble() * 0.26f;
                        lrx[k] = 0.12f + (float)rng.NextDouble() * 0.10f;
                        lry[k] = lrx[k] * (0.8f + (float)rng.NextDouble() * 0.5f);
                        break;
                }
            }

            float cut = kind == CloudKind.Cirrus ? 0.35f : 0.5f;
            float edge = kind == CloudKind.Cirrus ? 1.2f : 1.6f;
            float cap = kind == CloudKind.Cirrus ? 0.55f : kind == CloudKind.Stratus ? 0.85f : 0.95f;

            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1), v = y / (float)(n - 1);
                    float field = 0f;
                    for (int k = 0; k < lobes; k++)
                    {
                        float dx = (u - lx[k]) / lrx[k];
                        float dy = (v - ly[k]) / lry[k];
                        field += Mathf.Exp(-(dx * dx + dy * dy));
                    }

                    // Fractal noise breaks the smooth blob into wisps. Cirrus stretches the noise along u.
                    float nf = kind == CloudKind.Cirrus ? Fbm(u * 9f, v * 26f, seed) : Fbm(u * 4.5f, v * 4.5f, seed);
                    field *= kind == CloudKind.Cirrus ? 0.2f + 1.4f * nf : 0.5f + 0.95f * nf;

                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(field - cut) * edge) * cap;

                    // Brighter tops, greyer undersides, plus internal density variation for depth.
                    float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(v * 1.2f));
                    shade *= 0.85f + 0.3f * Fbm(u * 6f + 11f, v * 6f + 7f, seed + 5);
                    shade = Mathf.Clamp01(shade);
                    px[y * n + x] = new Color(shade, shade, shade, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // --- Lightweight value-noise fBm (no Unity Perlin dependency, deterministic per seed) ---
        private static float Fbm(float x, float y, int seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, seed + o * 131);
                freq *= 2f;
                amp *= 0.5f;
            }

            return sum;
        }

        private static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float v00 = Hash01(xi, yi, seed), v10 = Hash01(xi + 1, yi, seed);
            float v01 = Hash01(xi, yi + 1, seed), v11 = Hash01(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, u), Mathf.Lerp(v01, v11, u), v);
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}

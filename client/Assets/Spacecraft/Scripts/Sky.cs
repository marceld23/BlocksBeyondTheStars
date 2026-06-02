using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Drives the visible day/night + weather + sun colour from the server's `WorldEnvironment`
    /// (World systems). Because the world uses unlit shaders, the main effect is a **global tint**
    /// (`_Sc_Light`) multiplied into the block shaders — sun colour × day brightness × weather dim.
    /// Also drives the sky (camera background) and a rotating directional sun light. Time is
    /// advanced locally between the server's periodic updates. Suspended while the space view owns
    /// the camera.
    /// </summary>
    public sealed class Sky : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int SkyId = Shader.PropertyToID("_Sc_Sky");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Light _sun;
        private Transform _sunDisc;     // visible glowing sun billboard in the sky
        private Material _sunDiscMat;
        private float _time;        // local 0..1 day fraction
        private float _dayLength = 600f;
        private bool _haveEnv;

        private void Awake()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.None;

            BuildSunDisc();
        }

        private void BuildSunDisc()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SunDisc";
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var shader = Shader.Find("Spacecraft/SunGlow");
            _sunDiscMat = new Material(shader != null ? shader : Shader.Find("Unlit/Color"))
            {
                mainTexture = GenerateGlowTexture(),
            };
            _sunDiscMat.SetColor(ColorId, Color.white);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _sunDiscMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _sunDisc = quad.transform;
            _sunDisc.SetParent(transform, false);
            _sunDisc.gameObject.SetActive(false);
        }

        private static Texture2D GenerateGlowTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float core = Mathf.Clamp01(1f - d * 4f);              // tight bright disc
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f); // soft surrounding glow
                    float a = Mathf.Clamp01(core * 0.8f + halo * 0.6f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var env = Game.Environment;
            if (env != null)
            {
                if (!_haveEnv)
                {
                    _time = env.TimeOfDay;
                    _haveEnv = true;
                }

                _dayLength = Mathf.Max(10f, env.DayLengthSeconds);

                // Re-sync toward the server time (it broadcasts periodically); advance locally too.
                _time = Mathf.LerpAngle(_time * 360f, env.TimeOfDay * 360f, 0.02f) / 360f;
            }

            _time = Mathf.Repeat(_time + Time.deltaTime / _dayLength, 1f);

            float intensity = env?.Intensity ?? 0f;
            Color sun = env != null ? Rgb(env.SunColor) : new Color(1f, 0.96f, 0.9f);
            bool spaceSky = env != null && env.SpaceSky;
            ApplyLighting(_time, intensity, sun, spaceSky);
        }

        private void ApplyLighting(float time, float weatherIntensity, Color sunColor, bool spaceSky)
        {
            // Sun height: peaks at noon (0.5), lowest at midnight (0/1).
            float sunHeight = Mathf.Sin((time - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            float brightness = Mathf.Lerp(0.20f, 1f, day);            // night floor → noon
            float weatherDim = Mathf.Lerp(1f, 0.5f, weatherIntensity); // storms darken

            Color tint = sunColor * (brightness * weatherDim);
            tint.a = 1f; // marks the global as "set" for the shaders
            Shader.SetGlobalColor(LightId, tint);

            // Sky colour: drives both the camera background and the global the lit shader samples
            // for grazing-angle environment reflections (glossy/metallic blocks).
            Color sky;
            if (spaceSky)
            {
                // Airless body (asteroid): the sky stays space-black even on the surface.
                sky = new Color(0.01f, 0.01f, 0.03f);
            }
            else
            {
                Color daySky = Color.Lerp(new Color(0.55f, 0.75f, 0.95f), new Color(0.6f, 0.62f, 0.68f), weatherIntensity);
                Color nightSky = new Color(0.03f, 0.04f, 0.09f);
                sky = Color.Lerp(nightSky, daySky, day);
            }
            sky.a = 1f;
            Shader.SetGlobalColor(SkyId, sky);

            // Apply to the camera background (skip while the space view controls the camera).
            if (Camera != null && (Game == null || !Game.SpaceViewActive))
            {
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = sky;
            }

            ApplyFog(sky, weatherIntensity, day, spaceSky);

            if (_sun != null)
            {
                _sun.color = sunColor;
                _sun.intensity = brightness;
                _sun.transform.rotation = Quaternion.Euler(time * 360f - 90f, 160f, 0f);
                // The lit block shader reads the sun direction from this global (direction TO the sun).
                Shader.SetGlobalVector(SunDirId, -_sun.transform.forward);
            }

            UpdateSunDisc(sunHeight, sunColor, spaceSky);
        }

        /// <summary>
        /// Distance fog in the sky colour, so the world fades into the horizon. Storms pull it in,
        /// night thickens it a little; airless bodies (space sky) and the space view stay clear. The
        /// fog-enabled block shader applies it; other scenes' shaders ignore RenderSettings.fog.
        /// </summary>
        private void ApplyFog(Color sky, float weatherIntensity, float day, bool spaceSky)
        {
            bool fog = !spaceSky && (Game == null || !Game.SpaceViewActive);
            RenderSettings.fog = fog;
            if (!fog)
            {
                return;
            }

            RenderSettings.fogColor = sky;
            RenderSettings.fogMode = FogMode.Linear;
            // Clear weather sees far; storms close in; night a touch hazier than day.
            float far = Mathf.Lerp(240f, 80f, weatherIntensity) * Mathf.Lerp(0.85f, 1f, day);
            RenderSettings.fogStartDistance = far * 0.35f;
            RenderSettings.fogEndDistance = far;
        }

        /// <summary>Places the glowing sun billboard in the sky in the sun direction, tinted by the
        /// system sun colour and faded near/below the horizon. Hidden during the space view.</summary>
        private void UpdateSunDisc(float sunHeight, Color sunColor, bool spaceSky)
        {
            if (_sunDisc == null || _sun == null)
            {
                return;
            }

            bool active = Camera != null && sunHeight > -0.12f && (Game == null || !Game.SpaceViewActive);
            _sunDisc.gameObject.SetActive(active);
            if (!active)
            {
                return;
            }

            Vector3 camPos = Camera.transform.position;
            Vector3 dir = (-_sun.transform.forward).normalized; // direction TO the sun
            // Kept comfortably inside the frustum (the disc isn't depth-tested, so distance only sets
            // its placement + angular size); avoids clipping against the camera's far plane.
            float dist = Mathf.Clamp(Camera.farClipPlane * 0.5f, 60f, 400f);
            _sunDisc.position = camPos + dir * dist;
            _sunDisc.rotation = Quaternion.LookRotation(camPos - _sunDisc.position); // billboard (Cull Off)
            float size = dist * (spaceSky ? 0.26f : 0.20f);
            _sunDisc.localScale = new Vector3(size, size, size);

            // Fade in as the sun rises; a touch brighter against an airless black sky.
            float a = Mathf.Clamp01(sunHeight * 1.6f + 0.3f) * (spaceSky ? 1.25f : 1f);
            Color c = sunColor;
            c.a = Mathf.Clamp01(a);
            _sunDiscMat.SetColor(ColorId, c);
        }

        private void OnDisable()
        {
            // Clear the tint so other scenes (menu) aren't affected.
            Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 0f));
            Shader.SetGlobalColor(Shader.PropertyToID("_Sc_LampColor"), new Color(0f, 0f, 0f, 0f)); // headlamp off
            RenderSettings.fog = false; // don't leak fog into the menu / space view
            if (_sunDisc != null)
            {
                _sunDisc.gameObject.SetActive(false);
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}

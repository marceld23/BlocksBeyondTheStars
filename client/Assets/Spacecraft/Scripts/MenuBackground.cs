using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Animated space-scene backdrop for the shell screens (M27 UI rework): a code-built starfield,
    /// a slowly rotating planet, a drifting blocky ship and tumbling asteroids, rendered by its own
    /// camera so the menu/loading IMGUI (and later uGUI) draws on top. The planet, moon, ship and
    /// asteroids are lit (Spacecraft/LitColor, a fixed key light) and carry block textures so they
    /// read as 3D; the starfield and engine/cockpit glow stay unlit. Gentle continuous motion.
    /// </summary>
    public sealed class MenuBackground : MonoBehaviour
    {
        private Camera _cam;
        private Transform _stars;
        private Transform _planet;
        private Transform _clouds;
        private Transform _moon;
        private Transform _ship;
        private readonly Transform[] _asteroids = new Transform[7];
        private Renderer _beacon;
        private Material _engineMat;
        private Transform _engineGlowL, _engineGlowR;
        private Light _engineLight;
        private float _t;

        private void Awake()
        {
            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.015f, 0.025f, 0.06f);
            _cam.farClipPlane = 600f;
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;

            BuildStars();

            var rock = LoadTex("stone");
            var hullTex = LoadTex("iron_wall");
            var iceTex = LoadTex("ice");

            _planet = MakeSphere("Planet", new Vector3(11f, -3f, 34f), 26f,
                Lit(new Color(0.30f, 0.50f, 0.70f), iceTex, new Vector2(4f, 2f))).transform;
            _clouds = BuildCloudShell(_planet);
            _moon = MakeSphere("Moon", new Vector3(-16f, 11f, 60f), 5f,
                Lit(new Color(0.60f, 0.60f, 0.64f), rock, new Vector2(2f, 1f))).transform;

            _ship = BuildShip(hullTex);
            _ship.localPosition = new Vector3(-2.5f, -1.5f, 9f);
            _ship.localRotation = Quaternion.Euler(6f, 28f, -3f);

            var rng = new System.Random(99);
            for (int i = 0; i < _asteroids.Length; i++)
            {
                var a = Cube("Asteroid", transform,
                    new Vector3((float)(rng.NextDouble() * 28 - 14), (float)(rng.NextDouble() * 16 - 8), 14f + (float)rng.NextDouble() * 24f),
                    Vector3.one * (0.6f + (float)rng.NextDouble() * 1.6f),
                    Lit(new Color(0.45f, 0.40f, 0.34f), rock));
                _asteroids[i] = a.transform;
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;

            if (_stars != null)
            {
                _stars.localRotation = Quaternion.Euler(0f, _t * 0.4f, 0f);
            }

            if (_planet != null)
            {
                _planet.localRotation = Quaternion.Euler(8f, _t * 1.6f, 0f);
            }

            if (_clouds != null)
            {
                _clouds.localRotation = Quaternion.Euler(0f, _t * 2.6f, 0f); // drift relative to the surface
            }

            if (_ship != null)
            {
                _ship.localPosition = new Vector3(-2.5f, -1.5f + Mathf.Sin(_t * 0.6f) * 0.35f, 9f);
                _ship.localRotation = Quaternion.Euler(6f + Mathf.Sin(_t * 0.5f) * 2f, 28f + Mathf.Sin(_t * 0.3f) * 3f, -3f);

                // Blinking beacon.
                if (_beacon != null)
                {
                    bool on = Mathf.Sin(_t * 4f) > 0.4f;
                    _beacon.sharedMaterial.color = on ? new Color(1f, 0.35f, 0.35f) : new Color(0.3f, 0.06f, 0.06f);
                }

                // Engine flicker: glow length + light intensity pulse.
                float pulse = 0.8f + Mathf.Sin(_t * 14f) * 0.12f + Mathf.Sin(_t * 5f) * 0.08f;
                if (_engineGlowL != null) _engineGlowL.localScale = new Vector3(0.5f, 0.5f, 1.4f * pulse);
                if (_engineGlowR != null) _engineGlowR.localScale = new Vector3(0.5f, 0.5f, 1.4f * pulse);
                if (_engineMat != null) _engineMat.color = new Color(0.5f, 0.85f, 1f) * (0.9f + pulse * 0.2f);
                if (_engineLight != null) _engineLight.intensity = 1.8f + pulse * 0.8f;
            }

            for (int i = 0; i < _asteroids.Length; i++)
            {
                var a = _asteroids[i];
                if (a != null)
                {
                    a.localRotation = Quaternion.Euler(_t * (12f + i * 3f), _t * (8f + i * 2f), 0f);
                    a.localPosition += new Vector3(0.06f + i * 0.004f, 0f, 0f) * Time.deltaTime;
                    if (a.localPosition.x > 18f)
                    {
                        a.localPosition = new Vector3(-18f, a.localPosition.y, a.localPosition.z);
                    }
                }
            }
        }

        private void BuildStars()
        {
            var root = new GameObject("Stars");
            root.transform.SetParent(transform, false);
            _stars = root.transform;
            var mat = Unlit(new Color(0.85f, 0.9f, 1f));
            var bright = Unlit(new Color(1f, 0.97f, 0.9f));
            var rng = new System.Random(4242);
            for (int i = 0; i < 420; i++)
            {
                var dir = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 1.4 + 0.2)).normalized;
                bool hero = rng.NextDouble() < 0.08;
                Cube("Star", _stars, dir * 320f, Vector3.one * (hero ? 3.2f + (float)rng.NextDouble() * 2.4f : 1.0f + (float)rng.NextDouble() * 2.2f), hero ? bright : mat);
            }
        }

        private Transform BuildShip(Texture2D hullTex)
        {
            var ship = new GameObject("Ship").transform;
            ship.SetParent(transform, false);
            var hull = Lit(new Color(0.62f, 0.64f, 0.70f), hullTex);
            var glass = Unlit(new Color(0.3f, 0.7f, 0.95f));
            var engine = Unlit(new Color(0.35f, 0.8f, 1f));

            Cube("Body", ship, new Vector3(0f, 0f, 0f), new Vector3(2.0f, 1.0f, 4.2f), hull);
            Cube("WingL", ship, new Vector3(-1.6f, 0f, -0.4f), new Vector3(1.4f, 0.25f, 1.7f), hull);
            Cube("WingR", ship, new Vector3(1.6f, 0f, -0.4f), new Vector3(1.4f, 0.25f, 1.7f), hull);
            Cube("Cockpit", ship, new Vector3(0f, 0.5f, 1.5f), new Vector3(1.1f, 0.6f, 1.3f), glass);
            Cube("EngL", ship, new Vector3(-0.7f, 0f, -2.4f), new Vector3(0.7f, 0.7f, 0.6f), engine);
            Cube("EngR", ship, new Vector3(0.7f, 0f, -2.4f), new Vector3(0.7f, 0.7f, 0.6f), engine);

            // Navigation lights: red to port (left), green to starboard (right), white headlights up front.
            Cube("NavRed", ship, new Vector3(-2.25f, 0.05f, -0.4f), Vector3.one * 0.28f, Unlit(new Color(1f, 0.25f, 0.25f)));
            Cube("NavGreen", ship, new Vector3(2.25f, 0.05f, -0.4f), Vector3.one * 0.28f, Unlit(new Color(0.3f, 1f, 0.4f)));
            Cube("HeadL", ship, new Vector3(-0.5f, -0.15f, 2.15f), Vector3.one * 0.22f, Unlit(new Color(1f, 1f, 0.92f)));
            Cube("HeadR", ship, new Vector3(0.5f, -0.15f, 2.15f), Vector3.one * 0.22f, Unlit(new Color(1f, 1f, 0.92f)));

            // Blinking top beacon.
            _beacon = Cube("Beacon", ship, new Vector3(0f, 0.7f, 0.2f), Vector3.one * 0.2f, Unlit(new Color(1f, 0.3f, 0.3f))).GetComponent<Renderer>();

            // Glowing engine exhaust trails + a pulsing point light.
            _engineMat = Unlit(new Color(0.5f, 0.85f, 1f));
            _engineGlowL = Cube("GlowL", ship, new Vector3(-0.7f, 0f, -3.1f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;
            _engineGlowR = Cube("GlowR", ship, new Vector3(0.7f, 0f, -3.1f), new Vector3(0.5f, 0.5f, 1.4f), _engineMat).transform;

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(ship, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, -3f);
            _engineLight = lightGo.AddComponent<Light>();
            _engineLight.type = LightType.Point;
            _engineLight.color = new Color(0.5f, 0.85f, 1f);
            _engineLight.range = 14f;
            _engineLight.intensity = 2.2f;
            _engineLight.shadows = LightShadows.None;
            return ship;
        }

        private Transform BuildCloudShell(Transform planet)
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "CloudShell";
            StripCollider(shell);
            shell.transform.SetParent(planet, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 1.04f;

            var shader = Shader.Find("Spacecraft/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader) { mainTexture = CloudTexture() };
            mat.renderQueue = 3000;
            mat.SetColor(Shader.PropertyToID("_Color"), new Color(0.95f, 0.97f, 1f, 0.7f));
            shell.GetComponent<Renderer>().sharedMaterial = mat;
            return shell.transform;
        }

        private static Texture2D CloudTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float f = 0.5f + 0.25f * Mathf.Sin(u * 3f + Mathf.Sin(v * 2f))
                                   + 0.15f * Mathf.Sin(v * 4f + Mathf.Cos(u * 3f))
                                   + 0.10f * Mathf.Sin((u + v) * 5f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - 0.55f) * 3f));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static GameObject MakeSphere(string name, Vector3 pos, float scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            StripCollider(go);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static Material Unlit(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = c };
        }

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        private static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = c };
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes raw 64x64 RGBA32,
        /// via LoadRawTextureData from the core module — no ImageConversion dependency).</summary>
        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(asset.bytes);
            tex.Apply();
            return tex;
        }
    }
}

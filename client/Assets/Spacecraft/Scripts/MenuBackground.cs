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
        private Transform _moon;
        private Transform _ship;
        private readonly Transform[] _asteroids = new Transform[7];
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

            if (_ship != null)
            {
                _ship.localPosition = new Vector3(-2.5f, -1.5f + Mathf.Sin(_t * 0.6f) * 0.35f, 9f);
                _ship.localRotation = Quaternion.Euler(6f + Mathf.Sin(_t * 0.5f) * 2f, 28f + Mathf.Sin(_t * 0.3f) * 3f, -3f);
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
            var rng = new System.Random(4242);
            for (int i = 0; i < 240; i++)
            {
                var dir = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 1.4 + 0.2)).normalized;
                Cube("Star", _stars, dir * 320f, Vector3.one * (1.2f + (float)rng.NextDouble() * 2.6f), mat);
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
            return ship;
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

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes PNG) at runtime.</summary>
        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null)
            {
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            return tex.LoadImage(asset.bytes) ? tex : null;
        }
    }
}

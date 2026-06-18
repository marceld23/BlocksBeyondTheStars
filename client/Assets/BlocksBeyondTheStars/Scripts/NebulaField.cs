using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A procedural nebula backdrop drawn behind the <see cref="Starfield"/> in deep space, so the void reads
    /// with colour and depth instead of flat black — the biggest single look gap for a space game. A dome of
    /// coloured gas clouds (generated in <c>BlocksBeyondTheStars/Nebula</c>) follows the camera. It is RE-SEEDED
    /// PER STAR SYSTEM — patch positions, hues and cloud density all vary by system id — so jumping between
    /// systems shows a visibly different nebula (the rebuild only happens when the system actually changes).
    /// It fades to full in true space / on airless bodies / inside an orbital station, and on a lived-in planet
    /// it lingers as a faint NIGHT glow, washed out by thick air and gone by day. Per-vertex colour places the
    /// patches around the sphere; the shader carves the wispy structure. Additive + ZWrite Off, so
    /// ships/asteroids/planets paint over it. Sits just inside the star dome so the stars read in front.
    /// </summary>
    public sealed class NebulaField : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>Menu attract-scene mode: when ≥ 0 the nebula runs WITHOUT a live <see cref="Game"/> at this fixed
        /// brightness (0..1) and a fixed <see cref="MenuSeed"/>, so the shell screens get the same real gas backdrop
        /// the world uses. Default −1 = driven by the game's space/night state + per-system re-seed.</summary>
        public float MenuBrightness = -1f;

        /// <summary>The fixed nebula seed used in menu mode (ignored unless <see cref="MenuBrightness"/> ≥ 0).</summary>
        public int MenuSeed = 1337;

        private const float MaxBrightness = 0.5f; // additive cap — keep the void mostly dark, clouds a soft glow

        private Transform _dome;
        private MeshFilter _filter;
        private Mesh _mesh;
        private Material _mat;
        private float _brightness;        // smoothed 0..1 fade
        private string _builtForSystem = "\0"; // system id the current dome mesh was seeded for

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Nebula");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader);
            _mat.SetFloat("_Brightness", 0f);

            var go = new GameObject("Nebula");
            go.transform.SetParent(transform, false);
            _filter = go.AddComponent<MeshFilter>();
            _mesh = BuildDomeMesh(1); // placeholder until the current system is known; replaced in LateUpdate
            _filter.sharedMesh = _mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _dome = go.transform;
        }

        private void LateUpdate()
        {
            if (_dome == null || Camera == null)
            {
                return;
            }

            // Ride the camera at "infinity", just inside the star dome (0.44 of the far plane) so stars layer
            // in front of the gas. Distance only sets placement — every vertex is a pure direction.
            _dome.SetPositionAndRotation(Camera.transform.position, Quaternion.identity);
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.44f;
            _dome.localScale = new Vector3(r, r, r);

            // Menu attract scene: no game — build the fixed-seed dome once and hold a fixed brightness.
            if (MenuBrightness >= 0f)
            {
                string menuKey = "menu:" + MenuSeed;
                if (menuKey != _builtForSystem)
                {
                    _builtForSystem = menuKey;
                    var prev = _mesh;
                    _mesh = BuildDomeMesh(MenuSeed);
                    _filter.sharedMesh = _mesh;
                    if (prev != null)
                    {
                        Destroy(prev);
                    }
                }

                _brightness = Mathf.Clamp01(MenuBrightness);
                _mat.SetFloat("_Brightness", _brightness * MaxBrightness);
                return;
            }

            if (Game == null)
            {
                return;
            }

            // Re-seed the nebula per star system so each system reads distinct (patch positions, hues AND cloud
            // density all vary). Rebuilds only when the system actually changes — a rare event, so the alloc is
            // fine; the old mesh is freed.
            string sysKey = CurrentSystemKey();
            if (sysKey != null && sysKey != _builtForSystem)
            {
                _builtForSystem = sysKey;
                var old = _mesh;
                _mesh = BuildDomeMesh(StableHash(sysKey));
                _filter.sharedMesh = _mesh;
                if (old != null)
                {
                    Destroy(old);
                }
            }

            // Hard space sky (space view / airless body / station): present instantly, full strength.
            bool spaceSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                            || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;

            float target;
            if (spaceSky)
            {
                target = 1f;
                _brightness = target; // no slow fade in true space
            }
            else
            {
                // On a lived-in planet the nebula is only a faint NIGHT glow — invisible by day, and washed out
                // by thick atmosphere (thin air → clearer, thick air → barely there). Mirrors the Starfield's
                // dusk/dawn fade so stars and nebula come and go together.
                float night = NightFactor();
                float air = Game.Environment != null ? Mathf.Clamp01((float)Game.Environment.AtmosphereDensity) : 0.4f;
                target = night * Mathf.Lerp(0.55f, 0.12f, air);
                _brightness = Mathf.MoveTowards(_brightness, target, Time.deltaTime * 0.7f);
            }

            _mat.SetFloat("_Brightness", _brightness * MaxBrightness);
        }

        /// <summary>Night strength 0..1 from the local time of day — mirrors the Starfield's dusk/dawn curve so
        /// the gas only glows once the stars do.</summary>
        private float NightFactor()
        {
            float sunHeight = Mathf.Sin((Game.LocalTimeOfDay - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            return Mathf.Clamp01(1f - day * 1.4f);
        }

        /// <summary>The id of the star system the player is currently in (the system whose bodies contain the
        /// active location), or null if the map hasn't arrived yet.</summary>
        private string CurrentSystemKey()
        {
            var map = Game?.StarMap;
            if (map?.Systems == null)
            {
                return null;
            }

            string active = map.ActiveLocationId;
            if (string.IsNullOrEmpty(active))
            {
                return null;
            }

            foreach (var s in map.Systems)
            {
                if (s?.Bodies == null)
                {
                    continue;
                }

                foreach (var b in s.Bodies)
                {
                    if (b.Id == active)
                    {
                        return s.Id;
                    }
                }
            }

            return null;
        }

        private static int StableHash(string s)
        {
            int h = 0;
            foreach (char c in s ?? string.Empty)
            {
                h = h * 31 + c;
            }

            return h & 0x7fffffff;
        }

        /// <summary>Builds a unit UV-sphere dome with a per-vertex nebula tint, SEEDED per star system: a handful
        /// of large coloured patches (count, direction, hue and tightness all from the seed) summed across the
        /// sphere, in cool sci-fi hues rotated by a per-system hue shift. The shader turns this smooth tint
        /// field into wispy clouds via fbm noise.</summary>
        private static Mesh BuildDomeMesh(int seed)
        {
            const int rings = 24;    // latitude bands
            const int sectors = 48;  // longitude segments
            int vCount = (rings + 1) * (sectors + 1);

            var verts = new Vector3[vCount];
            var cols = new Color[vCount];
            var tris = new int[rings * sectors * 6];

            // Per-system variation: patch count, a whole-system hue rotation, and a cloud "tightness" (higher
            // exponent → smaller, sparser blobs; lower → broad, dense gas). Each patch picks a base hue + soft
            // brightness, then the system hue-shift rotates the lot so no two systems share a palette.
            var rng = new System.Random(seed);
            int patches = 4 + rng.Next(4);                          // 4..7 patches
            float densityExp = 5f + (float)rng.NextDouble() * 5f;   // 5..10 — cloud tightness/density
            float hueShift = (float)rng.NextDouble();               // 0..1 system-wide hue rotation
            var pDir = new Vector3[patches];
            var pCol = new Color[patches];
            Color[] palette =
            {
                new Color(0.45f, 0.20f, 0.75f), // violet
                new Color(0.15f, 0.45f, 0.85f), // blue
                new Color(0.15f, 0.70f, 0.65f), // teal
                new Color(0.80f, 0.25f, 0.55f), // magenta
                new Color(0.85f, 0.45f, 0.20f), // faint ember
                new Color(0.30f, 0.35f, 0.80f), // indigo
            };
            for (int p = 0; p < patches; p++)
            {
                pDir[p] = RandomOnSphere(rng);
                pCol[p] = ShiftHue(palette[rng.Next(palette.Length)], hueShift) * (0.55f + 0.45f * (float)rng.NextDouble());
            }

            int vi = 0;
            for (int ring = 0; ring <= rings; ring++)
            {
                float v = (float)ring / rings;        // 0..1 pole-to-pole
                float theta = v * Mathf.PI;            // 0..π
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int sec = 0; sec <= sectors; sec++)
                {
                    float u = (float)sec / sectors;
                    float phi = u * Mathf.PI * 2f;
                    var dir = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi));
                    verts[vi] = dir;

                    Color c = Color.black;
                    for (int p = 0; p < patches; p++)
                    {
                        float align = Mathf.Clamp01(Vector3.Dot(dir, pDir[p])); // 1 at the patch centre
                        float fall = Mathf.Pow(align, densityExp);              // tightness varies per system
                        c += pCol[p] * fall;
                    }

                    c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = 1f;
                    cols[vi] = c;
                    vi++;
                }
            }

            int ti = 0;
            int stride = sectors + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int sec = 0; sec < sectors; sec++)
                {
                    int a = ring * stride + sec;
                    int b = a + stride;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f); // always in view (we follow the camera)
            return mesh;
        }

        /// <summary>Rotates a colour's hue by <paramref name="shift"/> (0..1) keeping saturation/value — the
        /// per-system recolour that makes each system's nebula its own.</summary>
        private static Color ShiftHue(Color c, float shift)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + shift, 1f), s, v);
        }

        private static Vector3 RandomOnSphere(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f;
            float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }
    }
}

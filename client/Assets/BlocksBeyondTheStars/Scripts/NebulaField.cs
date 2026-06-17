using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A procedural nebula backdrop drawn behind the <see cref="Starfield"/> in deep space, so the void reads
    /// with colour and depth instead of flat black — the biggest single look gap for a space game. A dome of
    /// coloured gas clouds (generated in <c>BlocksBeyondTheStars/Nebula</c>) follows the camera and fades in only
    /// where the sky is truly space (in the space view, on airless bodies, inside an orbital station's windows),
    /// staying invisible on lived-in planet skies. Per-vertex colour places a few large nebula patches around the
    /// sphere; the shader carves the wispy structure. Additive + ZWrite Off, so ships/asteroids/planets paint over
    /// it. Sits just inside the star dome so the stars read in front of the gas.
    /// </summary>
    public sealed class NebulaField : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float MaxBrightness = 0.5f; // additive cap — keep the void mostly dark, clouds a soft glow

        private Transform _dome;
        private Material _mat;
        private float _brightness; // smoothed 0..1 fade

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
            go.AddComponent<MeshFilter>().sharedMesh = BuildDomeMesh();
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
            if (_dome == null || Camera == null || Game == null)
            {
                return;
            }

            // Ride the camera at "infinity", just inside the star dome (0.45 of the far plane) so stars layer
            // in front of the gas. Distance only sets placement — every vertex is a pure direction.
            _dome.SetPositionAndRotation(Camera.transform.position, Quaternion.identity);
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.44f;
            _dome.localScale = new Vector3(r, r, r);

            // Same "hard space sky" test the Starfield uses: present instantly in space / airless / station,
            // and never on a normal planet sky (a soft fade-out if we transition out).
            bool spaceSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                            || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;
            float target = spaceSky ? 1f : 0f;
            _brightness = spaceSky ? target : Mathf.MoveTowards(_brightness, target, Time.deltaTime * 0.7f);
            _mat.SetFloat("_Brightness", _brightness * MaxBrightness);
        }

        /// <summary>Builds a unit UV-sphere dome with a per-vertex nebula tint: a handful of large coloured
        /// patches (seeded → a stable sky) summed across the sphere, in cool sci-fi hues. The shader turns this
        /// smooth tint field into wispy clouds via fbm noise.</summary>
        private static Mesh BuildDomeMesh()
        {
            const int rings = 24;    // latitude bands
            const int sectors = 48;  // longitude segments
            int vCount = (rings + 1) * (sectors + 1);

            var verts = new Vector3[vCount];
            var cols = new Color[vCount];
            var tris = new int[rings * sectors * 6];

            // A few nebula patches: a seeded direction + hue each; vertex colour = sum of their soft falloffs.
            var rng = new System.Random(20260617);
            const int patches = 6;
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
                pCol[p] = palette[p % palette.Length] * (0.6f + 0.4f * (float)rng.NextDouble());
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
                        float fall = Mathf.Pow(align, 6f);                       // tight-ish soft blob
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

        private static Vector3 RandomOnSphere(System.Random rng)
        {
            float z = 2f * (float)rng.NextDouble() - 1f;
            float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }
    }
}

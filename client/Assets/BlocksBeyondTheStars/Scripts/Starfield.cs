// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A twinkling starfield drawn behind the world. A dome of additive star sprites surrounds the camera
    /// (following its position, scaled to sit just inside the far plane) and fades in when the sky is dark:
    /// out in space, on airless bodies, inside an orbital station (seen through its windows), and at night
    /// on a planet — then fades back out toward noon. Each star pulses on its own phase so the field
    /// shimmers. Opaque geometry drawn afterwards paints over it, so stars only show in open sky.
    /// </summary>
    public sealed class Starfield : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>Menu attract-scene mode: when ≥ 0 the field runs WITHOUT a live <see cref="Game"/> at this fixed
        /// brightness (0..1), so the shell screens get the same real starfield the world uses. Default −1 = driven
        /// by the game's day/space state.</summary>
        public float MenuBrightness = -1f;

        private const int StarCount = 1500;
        private const float MaxBrightness = 1.3f; // additive cap (per-star dots, so the sky as a whole stays dark)

        private Transform _dome;
        private Material _mat;
        private float _brightness; // smoothed 0..1 fade

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Starfield");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = BuildDotTexture() };
            _mat.SetFloat("_Brightness", 0f);

            var go = new GameObject("Starfield");
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
            if (_dome == null || Camera == null)
            {
                return;
            }

            // Sit the dome on the camera so the stars stay at "infinity"; scale it to ride just inside the
            // far plane (which the space view enlarges), keeping the angular star size constant everywhere.
            _dome.SetPositionAndRotation(Camera.transform.position, Quaternion.identity);
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.45f;
            _dome.localScale = new Vector3(r, r, r);

            // Menu attract scene: no game, just a fixed full-strength field.
            if (MenuBrightness >= 0f)
            {
                _brightness = Mathf.Clamp01(MenuBrightness);
                _mat.SetFloat("_Brightness", _brightness * MaxBrightness);
                return;
            }

            if (Game == null)
            {
                return;
            }

            // Out in true space / on an airless body / inside a station the stars are there instantly (no slow
            // fade-in — that's why none seemed to show on entering space); a planet keeps the soft dusk/dawn fade.
            bool hardSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                           || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;
            float target = TargetBrightness();
            _brightness = hardSky ? target : Mathf.MoveTowards(_brightness, target, Time.deltaTime * 0.7f);
            _mat.SetFloat("_Brightness", _brightness * MaxBrightness);
        }

        /// <summary>How visible the stars should be: full in space / on airless worlds / inside a station,
        /// fading in through dusk to full at deep night on a planet, and out toward noon.</summary>
        private float TargetBrightness()
        {
            bool boarded = !string.IsNullOrEmpty(Game.StationName);
            var env = Game.Environment;
            bool spaceSky = Game.SpaceViewActive || boarded || (env != null && env.SpaceSky) || Game.OnFootInSpace;
            if (spaceSky)
            {
                return 1f;
            }

            // Match Sky's day curve so stars track the same dusk/dawn the lighting uses.
            float sunHeight = Mathf.Sin((Game.LocalTimeOfDay - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            return Mathf.Clamp01(1f - day * 1.4f); // gone by mid-morning, full after dusk
        }

        /// <summary>A soft round dot (bright core → transparent rim) used for every star sprite.</summary>
        private static Texture2D BuildDotTexture()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f); // tight glowing core
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Builds the unit-sphere dome of star quads (billboarded toward the centre/camera), each
        /// with a colour + per-star twinkle (phase, speed) packed into UV1.</summary>
        private static Mesh BuildDomeMesh()
        {
            var verts = new Vector3[StarCount * 4];
            var uvs = new Vector2[StarCount * 4];
            var uv2 = new Vector2[StarCount * 4]; // x = twinkle phase, y = twinkle speed
            var cols = new Color[StarCount * 4];
            var tris = new int[StarCount * 6];

            var rng = new System.Random(20260606); // fixed seed → a stable sky
            for (int s = 0; s < StarCount; s++)
            {
                Vector3 dir = RandomOnSphere(rng);
                Vector3 up = Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 right = Vector3.Normalize(Vector3.Cross(up, dir));
                Vector3 up2 = Vector3.Cross(dir, right);

                float h = 0.006f + 0.007f * (float)rng.NextDouble(); // angular half-size (a touch larger = readable)
                Color tint = StarColor(rng);
                float phase = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float speed = 0.6f + 1.8f * (float)rng.NextDouble();

                int v = s * 4;
                verts[v + 0] = dir - right * h - up2 * h;
                verts[v + 1] = dir - right * h + up2 * h;
                verts[v + 2] = dir + right * h + up2 * h;
                verts[v + 3] = dir + right * h - up2 * h;
                uvs[v + 0] = new Vector2(0, 0);
                uvs[v + 1] = new Vector2(0, 1);
                uvs[v + 2] = new Vector2(1, 1);
                uvs[v + 3] = new Vector2(1, 0);
                for (int k = 0; k < 4; k++)
                {
                    uv2[v + k] = new Vector2(phase, speed);
                    cols[v + k] = tint;
                }

                int t = s * 6;
                tris[t + 0] = v + 0; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 0; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv2);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f); // always in view (we follow the camera)
            return mesh;
        }

        private static Vector3 RandomOnSphere(System.Random rng)
        {
            // Uniform point on the unit sphere (z uniform, azimuth uniform).
            float z = 2f * (float)rng.NextDouble() - 1f;
            float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        /// <summary>Mostly white stars with a scattering of warm + cool tints, at varied brightness.</summary>
        private static Color StarColor(System.Random rng)
        {
            float b = 0.55f + 0.45f * (float)rng.NextDouble();
            double roll = rng.NextDouble();
            Color c = roll < 0.15 ? new Color(0.75f, 0.83f, 1f)   // blue-white
                    : roll < 0.30 ? new Color(1f, 0.86f, 0.7f)    // warm
                    : new Color(1f, 1f, 1f);                      // white
            return new Color(c.r * b, c.g * b, c.b * b, 1f);
        }
    }
}

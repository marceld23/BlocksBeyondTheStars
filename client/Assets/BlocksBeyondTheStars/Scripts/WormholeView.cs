// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A wormhole end in the flight view (#2242): a tear in space-time — a jagged vertical crack like breaking glass,
    /// not a round portal, its zigzag shaped by its own seed. It turns about its long axis to face the pilot, so it
    /// never thins to an invisible line from the side. A white-blue flickering rim with little electric arcs crawling
    /// along it; inside, the twin system's sky (its tint once the player knows where it leads, violet before); sparks
    /// drifting into it; a soft violet light on whatever is near; a deep humming crackle that grows as you approach.
    /// On Medium and above the glow around it bends the stars behind (the <c>Wormhole</c> shader); everywhere else the
    /// bright rim, the dark core and the halo carry it — readable without bloom, light enough for the browser.
    /// </summary>
    public sealed class WormholeView : MonoBehaviour
    {
        private const float Height = 44f;
        private const float Width = 10f;
        private const int Segments = 26;

        public static readonly Color RimColor = new Color(0.78f, 0.88f, 1f);
        public static readonly Color Violet = new Color(0.55f, 0.25f, 0.95f);

        private static Shader _shader;
        private static Material _tearMat;
        private static Material _haloMat;

        private Transform _turn;
        private Camera _cam;
        private ClientSettings _settings;
        private AudioSource _hum;
        private int _light;
        private bool _opaqueHeld;
        private float _arcTimer;
        private float _moteTimer;
        private Vector3[] _edge; // tear outline points (local to _turn) for the arcs
        private float _seed;

        public void Init(string id, string linkedSystemId, Camera cam, ClientSettings settings)
        {
            _cam = cam;
            _settings = settings;
            uint h = StableHash(id);
            _seed = (h & 0xFFFF) / 65535f * 50f;

            _turn = new GameObject("Turn").transform;
            _turn.SetParent(transform, false);

            EnsureMaterials();
            var block = new MaterialPropertyBlock();
            block.SetColor("_Inner", InsideTint(linkedSystemId));
            block.SetFloat("_Seed", _seed);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(_turn, false);
            halo.transform.localScale = new Vector3(Width * 4.2f, Height * 1.35f, 1f);
            halo.AddComponent<MeshFilter>().sharedMesh = FxKit.QuadMesh;
            var haloRenderer = halo.AddComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = _haloMat;
            haloRenderer.SetPropertyBlock(block);

            var tear = new GameObject("Tear");
            tear.transform.SetParent(_turn, false);
            tear.AddComponent<MeshFilter>().sharedMesh = BuildTearMesh(h);
            var tearRenderer = tear.AddComponent<MeshRenderer>();
            tearRenderer.sharedMaterial = _tearMat;
            tearRenderer.SetPropertyBlock(block);

            _light = FxLights.Attach(transform, Vector3.zero, Violet, 1.6f, 55f, flicker: true);
            ClientSettings.RequestOpaqueTexture(true, ref _opaqueHeld); // the lensing (honoured on Medium and above only)

            var clip = Resources.Load<AudioClip>("audio/wormhole_hum") ?? ProceduralAudio.Generate("wormhole_hum");
            if (clip != null)
            {
                _hum = gameObject.AddComponent<AudioSource>();
                _hum.clip = clip;
                _hum.loop = true;
                _hum.spatialBlend = 1f;
                _hum.rolloffMode = AudioRolloffMode.Linear;
                _hum.minDistance = 10f;
                _hum.maxDistance = 160f;
                _hum.dopplerLevel = 0f;
                _hum.volume = 0.45f * (_settings?.SfxVolume ?? 0.8f);
                _hum.Play();
            }
        }

        private void Update()
        {
            var cam = _cam != null ? _cam : Camera.main;
            if (cam != null && _turn != null)
            {
                Vector3 to = cam.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    _turn.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up); // face the pilot about the long axis
                }
            }

            float dt = Time.deltaTime;
            float scale = transform.lossyScale.x;
            _arcTimer -= dt;
            if (_arcTimer <= 0f && _edge != null && _edge.Length > 4)
            {
                _arcTimer = Random.Range(0.15f, 0.4f);
                int a = Random.Range(0, _edge.Length - 3);
                var mat = FxKit.BeamMaterial("wormholearc", Color.white, coreWidth: 0.3f, noiseScale: 3f, noiseSpeed: 30f, intensity: 2.4f);
                FxKit.Beam(_turn.TransformPoint(_edge[a]), _turn.TransformPoint(_edge[a + Random.Range(1, 3)]), RimColor, 0.18f * scale, 0.12f, mat);
            }

            _moteTimer -= dt;
            if (_moteTimer <= 0f)
            {
                _moteTimer = 0.05f;
                int n = FxKit.Scaled(2);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = transform.position + Random.insideUnitSphere * (Width * 3.2f * scale);
                    Vector3 target = transform.position + Vector3.up * Random.Range(-Height * 0.4f, Height * 0.4f) * scale;
                    float life = Random.Range(1.2f, 2.2f);
                    FxKit.Emit(FxKit.Kind.Motes, p, (target - p) / life, Random.Range(0.15f, 0.3f) * scale, life, Color.Lerp(RimColor, Violet, Random.value));
                }
            }

            if (_hum != null)
            {
                _hum.volume = 0.45f * (_settings?.SfxVolume ?? 0.8f);
            }
        }

        private void OnDestroy()
        {
            if (_light != 0)
            {
                FxLights.Remove(_light);
                _light = 0;
            }

            ClientSettings.RequestOpaqueTexture(false, ref _opaqueHeld);
        }

        /// <summary>The tear: a tall zigzag strip, wide in the middle, closing to a point at both ends — every vertex
        /// pair one segment, u = 0 on its left edge and 1 on its right (the shader's rim reads u).</summary>
        private Mesh BuildTearMesh(uint seed)
        {
            var verts = new Vector3[(Segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[Segments * 6];
            _edge = new Vector3[Segments + 1];
            float spine = 0f;
            for (int j = 0; j <= Segments; j++)
            {
                float v = j / (float)Segments;
                float y = (v - 0.5f) * Height;
                float profile = Mathf.Pow(Mathf.Sin(Mathf.PI * v), 0.8f);
                float w = Width * profile * (0.55f + 0.45f * Rand(seed, j, 1));
                spine = Mathf.Clamp(spine + (Rand(seed, j, 2) - 0.5f) * Width * 0.55f, -Width * 0.45f, Width * 0.45f) * profile;
                float left = spine - w * 0.5f * (0.7f + 0.6f * Rand(seed, j, 3));
                float right = spine + w * 0.5f * (0.7f + 0.6f * Rand(seed, j, 4));
                verts[j * 2] = new Vector3(left, y, 0f);
                verts[j * 2 + 1] = new Vector3(right, y, 0f);
                uvs[j * 2] = new Vector2(0f, v);
                uvs[j * 2 + 1] = new Vector2(1f, v);
                _edge[j] = j % 2 == 0 ? verts[j * 2] : verts[j * 2 + 1];
            }

            for (int j = 0; j < Segments; j++)
            {
                int o = j * 6;
                int a = j * 2;
                tris[o] = a;
                tris[o + 1] = a + 2;
                tris[o + 2] = a + 1;
                tris[o + 3] = a + 1;
                tris[o + 4] = a + 2;
                tris[o + 5] = a + 3;
            }

            var mesh = new Mesh { name = "WormholeTear", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureMaterials()
        {
            if (_tearMat != null)
            {
                return;
            }

            _shader ??= Shader.Find("BlocksBeyondTheStars/Wormhole");
            if (_shader == null)
            {
                _shader = Shader.Find("Unlit/Transparent");
            }

            _haloMat = new Material(_shader) { name = "WormholeHalo", renderQueue = 3000 };
            _haloMat.SetFloat("_Halo", 1f);
            _haloMat.SetColor("_Color", RimColor);
            _tearMat = new Material(_shader) { name = "WormholeTear", renderQueue = 3001 };
            _tearMat.SetFloat("_Halo", 0f);
            _tearMat.SetColor("_Color", RimColor);
        }

        /// <summary>Inside the tear: the twin system's sky — a hue from its id, the way each system's nebula differs —
        /// or violet while the player does not know where it leads.</summary>
        private static Color InsideTint(string linkedSystemId)
        {
            if (string.IsNullOrEmpty(linkedSystemId))
            {
                return Violet * 0.8f;
            }

            float hue = (StableHash(linkedSystemId) % 360u) / 360f;
            return Color.HSVToRGB(hue, 0.65f, 0.8f);
        }

        private static float Rand(uint seed, int j, int salt)
        {
            uint x = seed ^ (uint)(j * 374761393) ^ (uint)(salt * 668265263);
            x = (x ^ (x >> 13)) * 1274126177u;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215f;
        }

        /// <summary>FNV-1a — stable across runs and platforms (string.GetHashCode is not).</summary>
        private static uint StableHash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s ?? string.Empty)
            {
                h = (h ^ c) * 16777619u;
            }

            return h;
        }
    }

    /// <summary>The world-space flash other pilots see when a ship vanishes into a rift or shoots out of one (#2242).</summary>
    public static class WormholeVisuals
    {
        public static void FlashAt(Vector3 at, bool arriving)
        {
            var violet = WormholeView.Violet;
            FxKit.Flash(at, Color.white, 4f * FxKit.FlashScale, 0.16f);
            FxKit.Flash(at, violet, 8f, 0.4f);
            FxKit.Ring(at, Vector3.zero, violet, arriving ? 12f : 1f, arriving ? 1f : 14f, 0.6f, thickness: 0.1f, intensity: 2.2f);
            FxLights.Flash(at, violet, 2.2f, 30f, 0.5f);
        }
    }
}

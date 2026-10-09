// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Light-shaft cards (#2402): where the sun reaches into a dark place — a cave mouth, a pit, a gap in a canopy — a
    /// soft additive card hangs along the sun direction from the opening down to the floor, with a few dust motes
    /// drifting in it. No volumetric pass: the June full-screen attempt darkened the whole frame, the
    /// <c>SunRays</c> billboard was its replacement for the sky, this is the same idea indoors. Openings come from the
    /// streamed client chunks, scanned a few times a second around the player: a column with open sky whose floor sits
    /// near the camera's height and which has covered (roofed) neighbours — that is the edge of light. Only while the
    /// camera itself is in the shade (otherwise the whole world is lit and shafts would be noise), only with the sun
    /// up and real air, capped per preset. Cards face the camera around the sun axis so they never show edge-on.
    /// </summary>
    public sealed class LightShafts : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public AtmosphereProbe Probe;
        public int MaxCards = 12; // High 12, Medium 6, Low 0 (switched off by WorldRig)

        private const float ScanInterval = 0.75f;
        private const int ScanRadius = 22;
        private const int ScanStep = 2;

        private readonly List<Transform> _cards = new List<Transform>();
        private readonly List<Vector3> _tops = new List<Vector3>();   // opening (top) of each shaft
        private readonly List<float> _lengths = new List<float>();
        private readonly List<float> _alphas = new List<float>();    // eased per card
        private Material _mat;
        private Mesh _quad;
        private float _scanTimer;
        private float _moteTimer;
        private readonly System.Random _rng = new System.Random(2402);
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = BuildShaftTexture(), renderQueue = 3001 };
            // Additive over the alpha blend the Cloud shader declares would need a second shader; the Cloud shader's
            // alpha blend with a bright, low-alpha colour reads as a soft veil of light — good enough and depth-tested.
            _mat.SetColor(ColorId, new Color(1f, 0.95f, 0.8f, 0.0f));
            _quad = BuildQuad();
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
            }
        }

        private void LateUpdate()
        {
            if (Game == null || Camera == null || Game.World == null || MaxCards <= 0)
            {
                HideAll();
                return;
            }

            var env = Game.Environment;
            bool air = env != null && !env.SpaceSky && !Game.SpaceViewActive && string.IsNullOrEmpty(Game.StationName);
            float t = Game.LocalTimeOfDay;
            float sunHeight = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
            float shade = Probe != null ? 1f - Probe.CameraExposure : 0f;
            bool show = air && sunHeight > 0.12f && shade > 0.4f && !Game.MenuOpen;

            _scanTimer -= Time.deltaTime;
            if (show && _scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Scan();
            }

            Vector3 sunDir = -Shader.GetGlobalVector("_Sc_SunDir"); // direction the light travels (down from the sun)
            if (sunDir.sqrMagnitude < 1e-4f)
            {
                sunDir = Vector3.down;
            }

            sunDir.Normalize();
            float air01 = env != null ? Mathf.Clamp01(env.AtmosphereDensity) : 0.4f;
            float strength = show ? Mathf.Clamp01(sunHeight * 2f) * Mathf.Lerp(0.35f, 0.9f, air01) * shade : 0f;

            for (int i = 0; i < _cards.Count; i++)
            {
                bool active = show && i < _tops.Count;
                float target = active ? strength : 0f;
                _alphas[i] = Mathf.MoveTowards(_alphas[i], target, Time.deltaTime * 0.8f);
                var card = _cards[i];
                if (_alphas[i] <= 0.002f)
                {
                    if (card.gameObject.activeSelf)
                    {
                        card.gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!card.gameObject.activeSelf)
                {
                    card.gameObject.SetActive(true);
                }

                if (i < _tops.Count)
                {
                    // The card hangs from the opening along the sun direction; it turns about that axis to face the camera.
                    Vector3 top = _tops[i];
                    float len = _lengths[i];
                    Vector3 mid = top + sunDir * (len * 0.5f);
                    Vector3 toCam = Camera.transform.position - mid;
                    Vector3 side = Vector3.Cross(sunDir, toCam).normalized;
                    if (side.sqrMagnitude < 1e-4f)
                    {
                        side = Vector3.right;
                    }

                    Vector3 normal = Vector3.Cross(side, sunDir).normalized;
                    card.SetPositionAndRotation(mid, Quaternion.LookRotation(-normal, -sunDir));
                    card.localScale = new Vector3(2.2f, len, 1f);
                }

                var mr = card.GetComponent<MeshRenderer>();
                var block = new MaterialPropertyBlock();
                var sun = Shader.GetGlobalColor("_Sc_Light");
                var c = new Color(Mathf.Max(0.6f, sun.r), Mathf.Max(0.55f, sun.g), Mathf.Max(0.45f, sun.b), 0.22f * _alphas[i]);
                block.SetColor(ColorId, c);
                mr.SetPropertyBlock(block);
            }

            // Dust in the beams: a mote or two a second per visible shaft, drifting slowly along the light.
            if (show && _tops.Count > 0)
            {
                _moteTimer -= Time.deltaTime;
                if (_moteTimer <= 0f)
                {
                    _moteTimer = 0.35f;
                    int i = _rng.Next(_tops.Count);
                    if (i < _alphas.Count && _alphas[i] > 0.05f)
                    {
                        float along = (float)_rng.NextDouble() * _lengths[i];
                        Vector3 at = _tops[i] + sunDir * along + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 1.4f;
                        FxKit.Emit(FxKit.Kind.Motes, at, sunDir * 0.25f + Vector3.up * 0.05f, 0.05f, 2.5f, new Color(1f, 0.95f, 0.8f, 0.5f));
                    }
                }
            }
        }

        /// <summary>Finds the openings around the camera: open-sky columns at the camera's height whose neighbours are
        /// roofed. Keeps the nearest few, merging ones closer than 4 blocks.</summary>
        private void Scan()
        {
            var world = Game.World;
            Vector3 cam = Camera.transform.position;
            int cx = Mathf.FloorToInt(cam.x), cy = Mathf.FloorToInt(cam.y), cz = Mathf.FloorToInt(cam.z);
            var found = new List<(Vector3 top, float len, float dist)>();

            for (int dx = -ScanRadius; dx <= ScanRadius; dx += ScanStep)
            {
                for (int dz = -ScanRadius; dz <= ScanRadius; dz += ScanStep)
                {
                    int x = cx + dx, z = cz + dz;
                    // The floor near the camera's height in this column.
                    int floorY = int.MinValue;
                    for (int y = cy + 6; y >= cy - 10; y--)
                    {
                        if (world.TryGetBlock(x, y, z, out var id) && !id.IsAir)
                        {
                            floorY = y + 1;
                            break;
                        }
                    }

                    if (floorY == int.MinValue || !ColumnOpen(world, x, floorY, z))
                    {
                        continue;
                    }

                    // An opening is the EDGE of light: at least two of the four neighbours (3 blocks out) are roofed.
                    int roofed = 0;
                    if (!ColumnOpen(world, x + 3, floorY, z)) roofed++;
                    if (!ColumnOpen(world, x - 3, floorY, z)) roofed++;
                    if (!ColumnOpen(world, x, floorY, z + 3)) roofed++;
                    if (!ColumnOpen(world, x, floorY, z - 3)) roofed++;
                    if (roofed < 2)
                    {
                        continue;
                    }

                    // The shaft runs from a few blocks above the floor down to it.
                    float len = 6f;
                    var top = new Vector3(x + 0.5f, floorY + len, z + 0.5f);
                    float dist = Vector2.Distance(new Vector2(x, z), new Vector2(cam.x, cam.z));
                    if (dist < 2f)
                    {
                        continue; // not on top of the camera
                    }

                    bool merged = false;
                    for (int i = 0; i < found.Count; i++)
                    {
                        if (Vector3.Distance(found[i].top, top) < 4f)
                        {
                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                    {
                        found.Add((top, len, dist));
                    }
                }
            }

            found.Sort((a, b) => a.dist.CompareTo(b.dist));
            _tops.Clear();
            _lengths.Clear();
            for (int i = 0; i < found.Count && i < MaxCards; i++)
            {
                _tops.Add(found[i].top);
                _lengths.Add(found[i].len);
            }

            while (_cards.Count < Mathf.Min(MaxCards, _tops.Count))
            {
                _cards.Add(BuildCard());
                _alphas.Add(0f);
            }
        }

        private bool ColumnOpen(ClientWorld world, int x, int yFrom, int z)
        {
            for (int y = yFrom + 1; y <= yFrom + 40; y++)
            {
                if (world.TryGetBlock(x, y, z, out var id) && !id.IsAir)
                {
                    return false;
                }
            }

            return !Game.LandedShipCovers(x, yFrom + 1, z);
        }

        private Transform BuildCard()
        {
            var go = new GameObject("LightShaft");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.SetActive(false);
            return go.transform;
        }

        private void HideAll()
        {
            foreach (var c in _cards)
            {
                if (c != null && c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>A unit quad in the XY plane (height along +Y from −0.5 to 0.5), UV v = 0 at the bottom.</summary>
        private static Mesh BuildQuad()
        {
            var m = new Mesh { name = "LightShaftQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A soft beam: bright and narrow at the top (v = 1), widening and fading toward the floor, feathered sides.</summary>
        private static Texture2D BuildShaftTexture()
        {
            const int w = 64, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);          // 0 floor … 1 opening
                float fall = Mathf.Pow(v, 0.8f);         // fades toward the floor
                float width = Mathf.Lerp(0.95f, 0.45f, v); // wider at the bottom
                for (int x = 0; x < w; x++)
                {
                    float u = (x / (float)(w - 1)) * 2f - 1f;
                    float side = Mathf.Clamp01(1f - Mathf.Abs(u) / width);
                    side = side * side * (3f - 2f * side);
                    float a = fall * side * (1f - Mathf.Pow(1f - v, 6f) * 0.5f);
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, b);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}

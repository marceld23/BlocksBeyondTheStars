// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws the monorail's energy lines (#2113, #2129): for every rail line the server lists, the shared
    /// <see cref="RailSpline"/> through its pylon tops is sampled into a glowing blue <b>energy band</b> — two crossed
    /// additive ribbons (one flat, one upright, so it reads as a band from every angle) carrying a procedural glow
    /// gradient: a bright core fading into a soft halo. The colour is HDR, so the scene's bloom gives it a gentle glow,
    /// and a pulse pattern travels along the line by scrolling the material's texture offset (no per-frame mesh work).
    /// Every stop shows as a short, wider amber glow on the band. No collider — the line is light, the train hovers over
    /// it. The splines are cached per line (by the server's list reference) and handed to <see cref="TrainView"/>, the
    /// player controller and the remote avatars, so every consumer poses against the same curve.
    /// </summary>
    public sealed class RailView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float SampleStep = 1.0f;
        private const float BandHalfWidth = 0.6f;   // the halo's reach either side of the line (the core is ~a fifth of it)
        private const float StopHalfWidth = 0.95f;  // a stop's amber glow is wider than the band…
        private const float StopHalfLength = 1.2f;  // …and this long either side of the stop's arc
        private const float PulseTile = 6f;         // metres of line per texture tile = one travelling pulse
        private const float PulseSpeed = 5f;        // metres per second the pulses travel
        private const float RebuildSeconds = 3f;

        // HDR (components above 1): the additive ribbons push the core past the bloom threshold for a soft glow.
        private static readonly Color LineGlow = new Color(0.6f, 1.3f, 2.6f, 1f);
        private static readonly Color StopGlow = new Color(2.6f, 1.35f, 0.3f, 1f);

        private static Texture2D _glowTex;

        private readonly Dictionary<int, RailSpline> _splines = new Dictionary<int, RailSpline>();
        private NetRailLine[] _built;
        private float _builtAt;
        private GameObject _go;
        private Mesh _mesh;
        private Material _mat;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _tris = new List<int>();

        /// <summary>The spline of a line (null for an unknown line or one too short to run on).</summary>
        public RailSpline SplineOf(int lineId)
        {
            if (Game?.Rails == null)
            {
                return null;
            }

            if (!ReferenceEquals(_built, Game.Rails))
            {
                RefreshSplines();
            }

            return _splines.TryGetValue(lineId, out var s) && s.TotalArc > 0f ? s : null;
        }

        private void RefreshSplines()
        {
            _splines.Clear();
            int circ = Game != null ? Game.Circumference : Shared.World.WorldConstants.Circumference;
            foreach (var line in Game.Rails)
            {
                if (line == null || line.Points == null || line.Points.Length < 6)
                {
                    continue;
                }

                var pts = new List<Vector3f>(line.Points.Length / 3);
                for (int i = 0; i + 2 < line.Points.Length; i += 3)
                {
                    pts.Add(new Vector3f(line.Points[i], line.Points[i + 1], line.Points[i + 2]));
                }

                _splines[line.Id] = RailSpline.Build(pts, line.Closed, circ);
            }
        }

        private void Update()
        {
            if (Game == null || Game.Rails == null)
            {
                return;
            }

            // The pulses travel: scroll the glow pattern along the band (wrapped, so the offset never grows large).
            if (_mat != null && _go != null && _go.activeSelf)
            {
                _mat.SetTextureOffset("_MainTex", new Vector2(-Mathf.Repeat(Time.time * PulseSpeed / PulseTile, 1f), 0f));
            }

            bool changed = !ReferenceEquals(_built, Game.Rails);
            if (!changed && Time.time - _builtAt < RebuildSeconds)
            {
                return;
            }

            if (changed)
            {
                RefreshSplines();
            }

            _built = Game.Rails;
            _builtAt = Time.time;
            Rebuild();
        }

        private void Rebuild()
        {
            EnsureLayer();
            _verts.Clear();
            _colors.Clear();
            _uvs.Clear();
            _tris.Clear();
            if (Game.Rails.Length == 0 || Game.World == null)
            {
                _go.SetActive(false);
                return;
            }

            foreach (var line in Game.Rails)
            {
                if (line == null || !_splines.TryGetValue(line.Id, out var spline) || spline.TotalArc <= 0f)
                {
                    continue;
                }

                float total = spline.TotalArc;
                var p0 = spline.PointAt(0f);
                var prev = Game.ScenePos(p0.X, p0.Y, p0.Z);
                float prevArc = 0f;
                for (float s = SampleStep; s <= total + 0.001f; s += SampleStep)
                {
                    float arc = Mathf.Min(s, total);
                    var p = spline.PointAt(arc);
                    var cur = Game.ScenePos(p.X, p.Y, p.Z);
                    if ((cur - prev).sqrMagnitude > 400f)
                    {
                        prev = cur; // the lap seam: do not stretch a ribbon across the world
                        prevArc = arc;
                        continue;
                    }

                    AddBand(prev, cur, prevArc, arc, BandHalfWidth, LineGlow);
                    prev = cur;
                    prevArc = arc;
                }

                if (line.StopArcs != null)
                {
                    foreach (float stop in line.StopArcs)
                    {
                        AddStopGlow(spline, stop);
                    }
                }
            }

            _mesh.Clear();
            if (_verts.Count == 0)
            {
                _go.SetActive(false);
                return;
            }

            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
            _go.SetActive(true);
        }

        /// <summary>A stop: a short, wider amber section of the band around the stop's arc (a loop's seam wraps).</summary>
        private void AddStopGlow(RailSpline spline, float stopArc)
        {
            const int Steps = 6;
            float from = stopArc - StopHalfLength;
            var pa = spline.PointAt(spline.NormalizeArc(from));
            var prev = Game.ScenePos(pa.X, pa.Y, pa.Z);
            for (int i = 1; i <= Steps; i++)
            {
                float arc = from + (2f * StopHalfLength * i / Steps);
                var p = spline.PointAt(spline.NormalizeArc(arc));
                var cur = Game.ScenePos(p.X, p.Y, p.Z);
                if ((cur - prev).sqrMagnitude < 25f)
                {
                    AddBand(prev, cur, arc - (2f * StopHalfLength / Steps), arc, StopHalfWidth, StopGlow);
                }

                prev = cur;
            }
        }

        private void EnsureLayer()
        {
            if (_go != null)
            {
                return;
            }

            // Additive (SrcAlpha One): the band adds light and never darkens what is behind it.
            var shader = Shader.Find("BlocksBeyondTheStars/Particle") ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(shader) { renderQueue = 3050, mainTexture = GlowTexture() };
            _go = new GameObject("RailLines");
            _go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "RailLines", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>The band's glow pattern, made once: V runs across the ribbon (a bright core fading into a soft halo that
        /// reaches zero at the edge), U along the line (a steady glow with one narrow brighter pulse per tile). The colour
        /// comes from the vertices; the texture only shapes the light (its alpha is the brightness).</summary>
        private static Texture2D GlowTexture()
        {
            if (_glowTex != null)
            {
                return _glowTex;
            }

            const int W = 64, H = 32;
            var px = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float pulse = Mathf.Pow(0.5f + (0.5f * Mathf.Cos(2f * Mathf.PI * (u - 0.5f))), 12f);
                float along = 0.6f + (0.85f * pulse);
                for (int y = 0; y < H; y++)
                {
                    float d = Mathf.Abs((((y + 0.5f) / H) * 2f) - 1f); // 0 in the middle of the ribbon, 1 at its edge
                    float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 0.2f));
                    float halo = (1f - d) * (1f - d) * 0.5f;
                    float a = Mathf.Clamp01(((core * 0.85f) + halo) * along);
                    px[(y * W) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            _glowTex = new Texture2D(W, H, TextureFormat.RGBA32, true)
            {
                name = "RailGlow",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            _glowTex.SetPixels32(px);
            _glowTex.Apply(true, true);
            return _glowTex;
        }

        /// <summary>One segment of the band from <paramref name="a"/> to <paramref name="b"/>: a flat ribbon and an upright
        /// one crossing on the line, U = arc length in pulse tiles, V = across the ribbon.</summary>
        private void AddBand(Vector3 a, Vector3 b, float arcA, float arcB, float half, Color color)
        {
            var dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-4f)
            {
                return;
            }

            dir /= len;
            var side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }

            side = side.normalized * half;
            var up = Vector3.Cross(dir, side).normalized * half;
            float ua = arcA / PulseTile, ub = arcB / PulseTile;
            AddRibbon(a, b, side, ua, ub, color);
            AddRibbon(a, b, up, ua, ub, color);
        }

        private void AddRibbon(Vector3 a, Vector3 b, Vector3 across, float ua, float ub, Color color)
        {
            int i = _verts.Count;
            _verts.Add(a - across); _uvs.Add(new Vector2(ua, 0f));
            _verts.Add(a + across); _uvs.Add(new Vector2(ua, 1f));
            _verts.Add(b + across); _uvs.Add(new Vector2(ub, 1f));
            _verts.Add(b - across); _uvs.Add(new Vector2(ub, 0f));
            for (int k = 0; k < 4; k++)
            {
                _colors.Add(color);
            }

            // Cull Off in the shader: one quad shows from both sides.
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
        }
    }
}

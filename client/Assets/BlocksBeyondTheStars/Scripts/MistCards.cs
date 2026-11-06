// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Fog banks in the air (#3273). The haze is mixed into the surface a pixel hits, so it can never hang a bank of mist in
    /// front of the open sky; these cards do. A small pool of large, soft, camera-facing cards (6–20 m wide, low and wide)
    /// is spread in a ring 15–70 m around the player at the valley fog floor (<see cref="AtmosphereProbe.FogFloor"/>), only
    /// where the ground is low (never on a hill, never inside the ground or a parked hull), and re-seeded as the player
    /// moves. Each card drifts with the wind, lives 45–100 s, fades in and out, and its noise slowly evolves; it fades out
    /// toward the far edge of the ring and — per pixel in the shader — within ~8 m of the camera, so no card is ever "in
    /// your face". Count and opacity follow the Sky's mist strength (the world's mist × time of day × weather: dawn, dusk
    /// and fog weather strongest), the colour is the height fog's lit mist colour (the shader reads it from the shared
    /// atmosphere include). Only under the open sky: never in a cave, aboard, on a
    /// station, in the flight view or under water. Budget per preset in <see cref="MistCardRules"/> (Potato 0 · Low 4 ·
    /// Medium 10 · High 20, the browser none) plus a cap on the screen area all cards cover; the nearest card gives way.
    /// <para>All cards are ONE dynamic mesh (one draw call), written far to near every frame so they blend in order. The
    /// <c>MistCard</c> shader draws them inside the haze, with soft particles from Medium. No particle system.</para>
    /// </summary>
    public sealed class MistCards : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public AtmosphereProbe Probe;
        public Sky Sky;

        /// <summary>The preset's budget (<see cref="BudgetFor"/>; 0 = off) — WorldRig sets it, the settings live.</summary>
        public int MaxCards;

        /// <summary>How many screens all cards together may cover (<see cref="CoverageCapFor"/>).</summary>
        public float CoverageCap = 1f;

        private const int Pool = MistCardRules.MaxBudget;
        private const int SpawnsPerFrame = 2;
        private const float GateRate = 0.8f;   // per second, the open-sky gate
        private const float CardFadeRate = 0.5f; // per second, a single card
        private static readonly int ParamsId = Shader.PropertyToID("_Params");

        // Per card, in world space.
        private readonly Vector3[] _pos = new Vector3[Pool];
        private readonly float[] _width = new float[Pool];
        private readonly float[] _height = new float[Pool];
        private readonly float[] _age = new float[Pool];
        private readonly float[] _life = new float[Pool];
        private readonly float[] _alpha = new float[Pool];   // eased — what is drawn
        private readonly float[] _drift = new float[Pool];   // wind multiplier
        private readonly float[] _tiles = new float[Pool];   // noise tiles across the card
        private readonly Vector4[] _seed = new Vector4[Pool];   // noise offsets of the two octaves (uv)
        private readonly Vector4[] _evolve = new Vector4[Pool]; // their drift per world second (uv)
        private readonly bool[] _alive = new bool[Pool];

        // Per frame.
        private readonly float[] _area = new float[Pool];
        private readonly bool[] _keep = new bool[Pool];
        private readonly int[] _order = new int[Pool];
        private readonly float[] _depth = new float[Pool];

        private readonly List<Vector3> _verts = new List<Vector3>(Pool * 4);
        private readonly List<Vector4> _uv0 = new List<Vector4>(Pool * 4);
        private readonly List<Vector4> _uv1 = new List<Vector4>(Pool * 4);
        private readonly List<Color> _colors = new List<Color>(Pool * 4);

        private Mesh _mesh;
        private Material _mat;
        private MeshRenderer _renderer;
        private Transform _holder;
        private readonly System.Random _rng = new System.Random(3273);
        private float _gate;          // eased open-sky gate 0..1
        private float _evolveTime;    // world seconds, the noise evolution's clock
        private bool _snapPending;    // a capture asked every eased look to settle

        /// <summary>The cards a client gets from its settings: the "Fog banks" switch, then the preset's budget (Low 4,
        /// Medium 10, High 20; Potato, the browser and other light devices none).</summary>
        public static int BudgetFor(ClientSettings s)
            => s != null && s.FogBanks ? MistCardRules.Budget((int)s.Preset, ClientSettings.FarViewLightDevice) : 0;

        /// <summary>The screen-coverage cap for the client's preset (<see cref="MistCardRules.CoverageCap"/>).</summary>
        public static float CoverageCapFor(ClientSettings s) => MistCardRules.CoverageCap(s != null ? (int)s.Preset : 0);

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/MistCard");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = MistNoiseTexture.Get(), renderQueue = 3000 };
            // x/y = the per-pixel near fade (MistCardRules), z = the soft-particle depth (m), w = how hard the noise bites the rim.
            _mat.SetVector(ParamsId, new Vector4(MistCardRules.PixelNearStart, MistCardRules.PixelNearLength, 2f, 0.9f));
            _mesh = BuildMesh();

            var go = new GameObject("MistCards");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;
            _holder = go.transform;
        }

        private void OnEnable() => GameBootstrap.CaptureSnap += OnCaptureSnap;

        private void OnDisable()
        {
            GameBootstrap.CaptureSnap -= OnCaptureSnap;
            Hide();
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /// <summary>A capture asks every eased look to settle (#2405): the next frame spawns and fades without easing.</summary>
        private void OnCaptureSnap() => _snapPending = true;

        private void LateUpdate()
        {
            if (Game == null || Camera == null || _mesh == null)
            {
                return;
            }

            Vector3 cam = Camera.transform.position;
            float gateTarget = MaxCards > 0 ? OpenSkyGate(cam) : 0f;
            float mist = Sky != null ? Sky.MistStrength01 : 0f;
            int active = gateTarget > 0f ? MistCardRules.ActiveCount(Mathf.Min(MaxCards, Pool), mist) : 0;
            bool snap = _snapPending;
            _snapPending = false;
            _gate = snap ? gateTarget : Mathf.MoveTowards(_gate, gateTarget, Time.deltaTime * GateRate);

            float worldDt = Game.WorldDeltaTime;
            _evolveTime += worldDt;
            Vector3 drift = WindDrift();
            int spawns = snap ? Pool : SpawnsPerFrame;
            bool any = false;
            for (int i = 0; i < Pool; i++)
            {
                if (!_alive[i] && i < active && spawns > 0 && Probe != null && Game.World != null)
                {
                    spawns--;
                    TrySpawn(i, cam, snap); // a snapped spawn is stepped (settled) right below, in the same frame
                }

                if (_alive[i])
                {
                    any |= StepCard(i, cam, worldDt, drift, i < active, snap);
                }
            }

            float opacity = MistCardRules.Opacity(mist) * _gate;
            if (!any || opacity <= 0.002f)
            {
                Hide();
                return;
            }

            Draw(cam, opacity);
        }

        /// <summary>1 under the open sky (the probe's eased camera exposure, which is 0 under a roof or in a cave), 0 in
        /// space, on a station, aboard, on a spacewalk, above the atmosphere line or under water.</summary>
        private float OpenSkyGate(Vector3 cam)
        {
            var env = Game.Environment;
            if (env == null || env.SpaceSky || Game.SpaceViewActive || Game.Aboard || Game.InEva || Game.OnFootInSpace
                || !string.IsNullOrEmpty(Game.StationName) || Game.World == null || Probe == null || Game.IsWaterAt(cam))
            {
                return 0f;
            }

            return Probe.CameraExposure * (1f - Probe.UndergroundFactor);
        }

        /// <summary>The mist's drift (metres per second): along the smoothed wind, a slow creep even on a calm day.</summary>
        private Vector3 WindDrift()
        {
            float dir = Game.WindDirection;
            float speed = 0.25f + 2.2f * Game.WindSpeed;
            return new Vector3(Mathf.Cos(dir), 0f, Mathf.Sin(dir)) * speed;
        }

        /// <summary>One frame of a living card: drift, age, ease its alpha toward the life cycle × the far and near fades.
        /// A card that left its ring, outlived its life or is no longer wanted fades out and frees its slot. Returns
        /// whether it shows this frame.</summary>
        private bool StepCard(int i, Vector3 cam, float worldDt, Vector3 drift, bool wanted, bool snap)
        {
            _age[i] += worldDt;
            _pos[i] += drift * (_drift[i] * worldDt);
            float dx = _pos[i].x - cam.x, dz = _pos[i].z - cam.z;
            float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
            float halfWidth = _width[i] * 0.5f;
            bool leaving = !wanted || _age[i] >= _life[i] || MistCardRules.OutOfRing(horizontal, halfWidth);
            float target = leaving
                ? 0f
                : MistCardRules.Lifecycle(_age[i], _life[i]) * MistCardRules.FarFade(horizontal)
                  * MistCardRules.NearFade(Vector3.Distance(_pos[i], cam), halfWidth);
            _alpha[i] = snap ? target : Mathf.MoveTowards(_alpha[i], target, Time.deltaTime * CardFadeRate);
            if (leaving && _alpha[i] <= 0.002f)
            {
                _alive[i] = false;
                return false;
            }

            return _alpha[i] > 0.002f;
        }

        /// <summary>Looks for a spot in the ring that holds mist (low ground, room for the card, no parked hull) and starts
        /// card <paramref name="i"/> there. A few tries; a slot that finds none stays free until a later frame.</summary>
        private void TrySpawn(int i, Vector3 cam, bool settled)
        {
            float floor = Probe.FogFloor;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float angle = Rand() * Mathf.PI * 2f;
                float radius = Mathf.Lerp(MistCardRules.RingInner + 4f, MistCardRules.RingOuter - 8f, Mathf.Sqrt(Rand()));
                float x = cam.x + Mathf.Cos(angle) * radius, z = cam.z + Mathf.Sin(angle) * radius;
                float width = Mathf.Lerp(MistCardRules.MinWidth, MistCardRules.MaxWidth, Rand());
                float height = width * Mathf.Lerp(MistCardRules.MinAspect, MistCardRules.MaxAspect, Rand());
                if (!GroundUnder(x, z, floor, width * 0.35f, out float ground) || !MistCardRules.SpotHoldsMist(ground, floor))
                {
                    continue; // a hill, a ridge, a tree crown, or not streamed in yet
                }

                float y = MistCardRules.CentreY(floor, ground, Rand(), height);
                if (Game.LandedShipCovers(Mathf.FloorToInt(x), Mathf.FloorToInt(y), Mathf.FloorToInt(z)))
                {
                    continue;
                }

                Init(i, new Vector3(x, y, z), width, height, settled);
                return;
            }
        }

        private void Init(int i, Vector3 pos, float width, float height, bool settled)
        {
            _alive[i] = true;
            _pos[i] = pos;
            _width[i] = width;
            _height[i] = height;
            _life[i] = Mathf.Lerp(45f, 100f, Rand());
            _age[i] = settled ? _life[i] * Mathf.Lerp(0.2f, 0.6f, Rand()) : 0f; // a capture starts mid-life, settled
            _alpha[i] = 0f;
            _drift[i] = Mathf.Lerp(0.7f, 1.3f, Rand());
            _tiles[i] = width / 16f; // the noise keeps its size in the world: a wide bank shows more billows
            _seed[i] = new Vector4(Rand(), Rand(), Rand(), Rand());
            _evolve[i] = new Vector4(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * 0.024f;
        }

        /// <summary>The highest ground under a card's footprint (its centre and four points <paramref name="reach"/> out);
        /// false when the centre column is not streamed in.</summary>
        private bool GroundUnder(float x, float z, float floor, float reach, out float ground)
        {
            if (!ColumnTop(x, z, floor, out ground))
            {
                return false;
            }

            for (int k = 0; k < 4; k++)
            {
                float ox = k == 0 ? reach : k == 1 ? -reach : 0f;
                float oz = k == 2 ? reach : k == 3 ? -reach : 0f;
                if (ColumnTop(x + ox, z + oz, floor, out float side))
                {
                    ground = Mathf.Max(ground, side);
                }
            }

            return true;
        }

        /// <summary>The top of the first solid (non-air) block walking down a column from 24 above the fog floor to 40 below.
        /// Water counts as ground — mist lies on a lake too.</summary>
        private bool ColumnTop(float fx, float fz, float floor, out float top)
        {
            var world = Game.World;
            int x = Mathf.FloorToInt(fx), z = Mathf.FloorToInt(fz);
            int from = Mathf.FloorToInt(floor) + 24, to = Mathf.FloorToInt(floor) - 40;
            for (int y = from; y >= to; y--)
            {
                if (world.TryGetBlock(x, y, z, out var id) && !id.IsAir)
                {
                    top = y + 1;
                    return true;
                }
            }

            top = 0f;
            return false;
        }

        /// <summary>Writes the visible cards into the one mesh, far to near (alpha blending needs the order), after the
        /// coverage cap let the largest ones go — a card that takes too much screen dissolves and comes back elsewhere.</summary>
        private void Draw(Vector3 cam, float opacity)
        {
            int n = CollectVisible(cam);
            SortFarToNear(n);
            // The colour is the shader's: the height fog's own lit mist colour (_Sc_MistColor via the shared include), so a
            // card is exactly the mist the land behind it sinks into. The vertex colour only multiplies it.
            Color tint = Color.white;
            for (int k = 0; k < Pool; k++)
            {
                if (k < n)
                {
                    WriteCard(k * 4, _order[k], cam, tint, opacity);
                }
                else
                {
                    Collapse(k * 4, cam);
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _uv1);
            _mesh.SetColors(_colors);
            _mesh.bounds = new Bounds(cam, Vector3.one * (2f * (MistCardRules.RingOuter + MistCardRules.MaxWidth)));
            _holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // the vertices are world positions
            if (!_renderer.enabled)
            {
                _renderer.enabled = true;
            }
        }

        /// <summary>Fills <see cref="_order"/>/<see cref="_depth"/> with the cards that show this frame and applies the
        /// coverage cap (only cards in front of the camera cost fill). Returns how many there are.</summary>
        private int CollectVisible(Vector3 cam)
        {
            Vector3 fwd = Camera.transform.forward;
            float tanHalf = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            int n = 0;
            for (int i = 0; i < Pool; i++)
            {
                if (!_alive[i] || _alpha[i] <= 0.002f)
                {
                    continue;
                }

                float depth = Vector3.Dot(_pos[i] - cam, fwd);
                _order[n] = i;
                _depth[n] = depth;
                _keep[n] = depth > 0f; // a card behind the camera costs no fill
                _area[n] = depth > 0f
                    ? MistCardRules.ScreenArea(_width[i], _height[i], Mathf.Max(depth, 0.5f), tanHalf, Camera.aspect)
                    : 0f;
                n++;
            }

            MistCardRules.ApplyCoverageCap(_area, _keep, n, CoverageCap);
            for (int k = 0; k < n; k++)
            {
                int i = _order[k];
                if (_depth[k] > 0f && !_keep[k])
                {
                    _age[i] = Mathf.Max(_age[i], _life[i]); // fades out from the next frame on, no pop
                }
            }

            return n;
        }

        /// <summary>Insertion sort of the first <paramref name="n"/> entries of <see cref="_order"/> by view depth, the
        /// farthest first (at most <see cref="Pool"/> entries).</summary>
        private void SortFarToNear(int n)
        {
            for (int a = 1; a < n; a++)
            {
                int idx = _order[a];
                float d = _depth[a];
                int b = a - 1;
                while (b >= 0 && _depth[b] < d)
                {
                    _order[b + 1] = _order[b];
                    _depth[b + 1] = _depth[b];
                    b--;
                }

                _order[b + 1] = idx;
                _depth[b + 1] = d;
            }
        }

        /// <summary>One card as a camera-facing quad (upright: its up stays in the vertical plane through the camera), its
        /// width breathing a little over its life. uv0 = corner + noise tiles, uv1 = the two octaves' evolving offsets
        /// (wrapped at one tile — the noise repeats, so the wrap never shows), colour = tint + this frame's alpha.</summary>
        private void WriteCard(int v, int i, Vector3 cam, Color tint, float opacity)
        {
            Vector3 p = _pos[i];
            Vector3 toCam = cam - p;
            Vector3 right = Vector3.Cross(toCam, Vector3.up);
            if (right.sqrMagnitude < 1e-4f)
            {
                right = Camera.transform.right; // straight above or below the card
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(right, toCam).normalized;
            float breathe = 1f + 0.06f * Mathf.Sin(_age[i] * 0.11f + _seed[i].x * 6.283f);
            Vector3 hx = right * (_width[i] * 0.5f * breathe);
            Vector3 hy = up * (_height[i] * 0.5f);

            Vector4 s = _seed[i] + _evolve[i] * _evolveTime;
            var offsets = new Vector4(Mathf.Repeat(s.x, 1f), Mathf.Repeat(s.y, 1f), Mathf.Repeat(s.z, 1f), Mathf.Repeat(s.w, 1f));
            var col = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(_alpha[i] * opacity));
            float tiles = _tiles[i];

            _verts[v] = p - hx - hy;
            _verts[v + 1] = p + hx - hy;
            _verts[v + 2] = p - hx + hy;
            _verts[v + 3] = p + hx + hy;
            _uv0[v] = new Vector4(0f, 0f, tiles, 0f);
            _uv0[v + 1] = new Vector4(1f, 0f, tiles, 0f);
            _uv0[v + 2] = new Vector4(0f, 1f, tiles, 0f);
            _uv0[v + 3] = new Vector4(1f, 1f, tiles, 0f);
            for (int c = 0; c < 4; c++)
            {
                _uv1[v + c] = offsets;
                _colors[v + c] = col;
            }
        }

        /// <summary>An unused slot: a zero-area quad at the camera, fully transparent.</summary>
        private void Collapse(int v, Vector3 cam)
        {
            for (int c = 0; c < 4; c++)
            {
                _verts[v + c] = cam;
                _uv0[v + c] = Vector4.zero;
                _uv1[v + c] = Vector4.zero;
                _colors[v + c] = Color.clear;
            }
        }

        /// <summary>The pool's mesh: <see cref="Pool"/> quads (two triangles each), the buffers sized once and rewritten
        /// every frame.</summary>
        private Mesh BuildMesh()
        {
            var mesh = new Mesh { name = "MistCards" };
            mesh.MarkDynamic();
            for (int i = 0; i < Pool * 4; i++)
            {
                _verts.Add(Vector3.zero);
                _uv0.Add(Vector4.zero);
                _uv1.Add(Vector4.zero);
                _colors.Add(Color.clear);
            }

            mesh.SetVertices(_verts);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetColors(_colors);
            var tris = new int[Pool * 6];
            for (int q = 0; q < Pool; q++)
            {
                int v = q * 4, t = q * 6;
                tris[t] = v;
                tris[t + 1] = v + 2;
                tris[t + 2] = v + 1;
                tris[t + 3] = v + 1;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        private float Rand() => (float)_rng.NextDouble();

        private void Hide()
        {
            if (_renderer != null && _renderer.enabled)
            {
                _renderer.enabled = false;
            }
        }
    }
}

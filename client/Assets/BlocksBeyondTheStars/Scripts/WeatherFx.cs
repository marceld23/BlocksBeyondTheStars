// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Weather effects (M27 polish): an IMGUI rain overlay during rain/storm (density scaled by the
    /// authoritative <c>WorldEnvironment.Intensity</c>) and periodic lightning flashes in storms.
    /// Drawn behind the HUD (high GUI.depth), hidden in space. No shaders/particles — robust in builds.
    /// Also carries the cold-world <b>frost</b> overlay (subtle ice creeping in from the screen edges on
    /// genuinely cold AIR worlds) — another screen wash, atmosphere-gated like the auroras.
    /// </summary>
    public sealed class WeatherFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int Max = 140;
        private readonly float[] _x = new float[Max];
        private readonly float[] _phase = new float[Max];
        private readonly float[] _len = new float[Max];
        private readonly float[] _speed = new float[Max];
        private bool _init;
        private float _flash;
        private float _flashTimer;
        private float _underwater; // smoothed 0..1 blue submerged wash

        // Frost overlay (cold worlds): ice fogs the screen edges as the air drops below freezing. The texture
        // is a soft edge-vignette of crystalline detail; alpha is driven by how far below 0 °C the air is.
        private float _frost;        // smoothed 0..1 frost coverage
        private Texture2D _frostTex; // lazily built (AI PNG from Resources, else procedural fallback)
        private const float FrostWarmC = 0f;    // frost starts forming at/below this air temperature
        private const float FrostColdC = -25f;  // full coverage at/below this
        private const float FrostMaxAlpha = 0.5f;

        // Rain-on-the-visor droplets: beads cling to the display during actual rain (never ash/"lava rain",
        // snow, hail or sandstorm), shimmer, then run down leaving a faint streak. Pure IMGUI screen overlay.
        private const int Drops = 80;
        private readonly float[] _dx = new float[Drops];      // 0..1 screen position
        private readonly float[] _dy = new float[Drops];
        private readonly float[] _dsize = new float[Drops];   // px
        private readonly float[] _dsit = new float[Drops];    // seconds left clinging before it runs
        private readonly float[] _dvy = new float[Drops];     // downward run speed (screen frac / s; 0 = clinging)
        private readonly float[] _dtrail = new float[Drops];  // current streak length (screen frac)
        private bool _dropInit;
        private float _wet;            // smoothed 0..1 rain-on-glass presence
        private Texture2D _dropTex;    // lazily built droplet bead (Resources PNG, else procedural)
        private readonly System.Random _dropRng = new System.Random(317);

        // Lightning bolt (B8): a brief jagged streak drawn with the flash.
        private float _boltTimer;
        private float _boltBaseX;
        private readonly float[] _boltJitter = new float[12];
        private readonly System.Random _boltRng = new System.Random(91);

        private void Init()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < Max; i++)
            {
                _x[i] = (float)rng.NextDouble();
                _phase[i] = (float)rng.NextDouble();
                _len[i] = 10f + (float)rng.NextDouble() * 18f;
                _speed[i] = 0.8f + (float)rng.NextDouble() * 0.6f;
            }

            _init = true;
        }

        private void Update()
        {
            // Underwater blue wash — independent of weather (works in caves / at night too).
            _underwater = Mathf.MoveTowards(_underwater, EyeUnderwater() ? 1f : 0f, Time.deltaTime * 3.5f);

            if (Game?.Environment == null)
            {
                _frost = Mathf.MoveTowards(_frost, 0f, Time.deltaTime * 0.4f);
                _wet = Mathf.MoveTowards(_wet, 0f, Time.deltaTime * 0.7f);
                return;
            }

            UpdateFrost();
            UpdateDroplets();

            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);
            _boltTimer = Mathf.Max(0f, _boltTimer - Time.deltaTime);
            // Lightning only in a rain thunderstorm — not a blizzard, sandstorm or ashfall.
            if (Game.Environment.Weather == "storm" && Game.Environment.Precipitation == "rain")
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    _flashTimer = 4f + Random.value * 7f;
                    _flash = 1f;
                    _boltTimer = 0.18f; // a bolt accompanies most strikes
                    _boltBaseX = 0.15f + Random.value * 0.7f;
                    for (int i = 0; i < _boltJitter.Length; i++)
                    {
                        _boltJitter[i] = (float)(_boltRng.NextDouble() * 2 - 1); // stable zig-zag for this strike
                    }
                }
            }
        }

        /// <summary>
        /// Drives the cold-world frost coverage. Needs a real atmosphere (skip airless space-sky bodies, which
        /// also report the NoAirTemperature sentinel −999), open sky, and air below freezing; coverage scales
        /// from <see cref="FrostWarmC"/> down to <see cref="FrostColdC"/>. Smoothed so ice creeps in / thaws out.
        /// </summary>
        private void UpdateFrost()
        {
            var env = Game.Environment;
            bool coldAir = !env.SpaceSky && env.Temperature > -100f && env.Temperature < FrostWarmC;
            bool show = coldAir && Game.ExposedToSky && !Game.SpaceViewActive;

            float target = 0f;
            if (show)
            {
                float t = Mathf.InverseLerp(FrostWarmC, FrostColdC, env.Temperature); // 0 at 0 °C → 1 at −25 °C
                target = t * t * FrostMaxAlpha; // ease-in so the first chill is barely there
            }

            _frost = Mathf.MoveTowards(_frost, target, Time.deltaTime * 0.4f); // slow freeze / thaw
        }

        /// <summary>
        /// Lazily provides the frost-overlay texture: a soft edge-vignette of crystalline ice. Prefers an
        /// authored/AI PNG at <c>Resources/frost_overlay</c>, else builds a procedural one so the effect always
        /// works in builds even before any art lands.
        /// </summary>
        private Texture2D FrostTexture()
        {
            if (_frostTex != null)
            {
                return _frostTex;
            }

            _frostTex = Resources.Load<Texture2D>("frost_overlay");
            if (_frostTex != null)
            {
                return _frostTex;
            }

            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            var rng = new System.Random(1701);
            // A coarse value-noise field, smoothed, to break the ring into crystalline fingers.
            const int G = 24;
            var grid = new float[(G + 1) * (G + 1)];
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = (float)rng.NextDouble();
            }

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = x / (float)(N - 1) * 2f - 1f;
                    float ny = y / (float)(N - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny) / 1.41421f; // 0 centre → 1 corner

                    // Bilinear sample of the noise grid, plus a finer second octave.
                    float n = SampleNoise(grid, G, x / (float)N, y / (float)N)
                              + 0.5f * SampleNoise(grid, G, x / (float)N * 2.3f % 1f, y / (float)N * 2.3f % 1f);
                    n /= 1.5f;

                    // Frost lives near the edges; the noise pushes fingers inward and bites holes out.
                    float edge = Mathf.SmoothStep(0.48f, 1.02f, r);
                    float a = Mathf.Clamp01(edge * (0.35f + 0.85f * n));
                    a *= Mathf.SmoothStep(0f, 0.35f, n); // clear gaps where the noise is low → crystalline, not a fog
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            _frostTex = tex;
            return _frostTex;
        }

        /// <summary>Smoothed bilinear value-noise lookup into a (G+1)² grid, coords in 0..1 (wrapping).</summary>
        private static float SampleNoise(float[] grid, int g, float u, float v)
        {
            u = Mathf.Repeat(u, 1f) * g;
            v = Mathf.Repeat(v, 1f) * g;
            int x0 = (int)u, y0 = (int)v;
            int x1 = (x0 + 1) % (g + 1), y1 = (y0 + 1) % (g + 1);
            float fx = Mathf.SmoothStep(0f, 1f, u - x0), fy = Mathf.SmoothStep(0f, 1f, v - y0);
            float a = Mathf.Lerp(grid[y0 * (g + 1) + x0], grid[y0 * (g + 1) + x1], fx);
            float b = Mathf.Lerp(grid[y1 * (g + 1) + x0], grid[y1 * (g + 1) + x1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>
        /// Advances the rain-on-glass beads. Gated to genuine <c>rain</c> precipitation only — never the "lava
        /// rain" ashfall, nor snow/hail/sandstorm. Beads cling for a spell, then run down the screen and respawn.
        /// </summary>
        private void UpdateDroplets()
        {
            var env = Game.Environment;
            string precip = env.Precipitation ?? "none";
            float target = precip == "rain" ? Mathf.Clamp01(0.4f + env.Intensity * 0.6f) : 0f;
            _wet = Mathf.MoveTowards(_wet, target, Time.deltaTime * 0.7f);
            if (_wet <= 0.005f)
            {
                return; // dry → nothing to simulate
            }

            if (!_dropInit)
            {
                for (int i = 0; i < Drops; i++)
                {
                    RespawnDrop(i, (float)_dropRng.NextDouble()); // scatter the initial cling timers
                }

                _dropInit = true;
            }

            bool storm = env.Weather == "storm";
            float dt = Time.deltaTime;
            for (int i = 0; i < Drops; i++)
            {
                if (_dvy[i] > 0f) // running down the glass
                {
                    _dvy[i] = Mathf.Min(_dvy[i] + dt * 0.18f, storm ? 0.7f : 0.42f); // gravity accel, capped
                    _dy[i] += _dvy[i] * dt;
                    _dtrail[i] = Mathf.Min(_dtrail[i] + _dvy[i] * dt, 0.16f);
                    if (_dy[i] > 1.08f)
                    {
                        RespawnDrop(i, 0f);
                    }
                }
                else
                {
                    _dsit[i] -= dt;
                    if (_dsit[i] <= 0f)
                    {
                        // Bigger beads and stormier rain are likelier to break loose and roll.
                        float runChance = (storm ? 0.6f : 0.35f) + _dsize[i] / 80f;
                        if (_dropRng.NextDouble() < runChance)
                        {
                            _dvy[i] = (0.10f + _dsize[i] / 260f) * (storm ? 1.5f : 1f);
                            _dtrail[i] = 0f;
                        }
                        else
                        {
                            _dsit[i] = 0.6f + (float)_dropRng.NextDouble() * 3.5f; // keep clinging a while longer
                        }
                    }
                }
            }
        }

        /// <summary>Resets a droplet to a fresh clinging bead at a random spot in the upper screen.</summary>
        private void RespawnDrop(int i, float sitBias)
        {
            _dx[i] = (float)_dropRng.NextDouble();
            _dy[i] = (float)_dropRng.NextDouble() * 0.55f; // beads gather toward the top, then run down
            _dsize[i] = 7f + (float)_dropRng.NextDouble() * 17f;
            _dsit[i] = 0.4f + sitBias * 4f + (float)_dropRng.NextDouble() * 3f;
            _dvy[i] = 0f;
            _dtrail[i] = 0f;
        }

        /// <summary>
        /// Lazily provides the droplet bead sprite. Prefers <c>Resources/rain_droplet</c>, else builds a
        /// mostly-transparent glass bead procedurally: faint body, a soft dark rim and a bright top highlight —
        /// reads as a water drop on the display without needing a scene-refraction shader.
        /// </summary>
        private Texture2D DropletTexture()
        {
            if (_dropTex != null)
            {
                return _dropTex;
            }

            _dropTex = Resources.Load<Texture2D>("rain_droplet");
            if (_dropTex != null)
            {
                return _dropTex;
            }

            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            float c = (N - 1) / 2f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - c) / c, ny = (y - c) / c;
                    float d = Mathf.Sqrt(nx * nx + ny * ny); // 0 centre → 1 edge
                    if (d > 1f)
                    {
                        px[y * N + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float body = (1f - d) * 0.12f;                       // faint translucent lens body
                    float rim = Mathf.SmoothStep(0.72f, 0.98f, d) * 0.45f; // soft dark rim
                    // Highlight toward the upper-left (light from above), a small bright spot.
                    float hl = Mathf.Clamp01(1f - new Vector2(nx + 0.32f, ny - 0.36f).magnitude * 2.6f) * 0.7f;
                    float a = Mathf.Clamp01(body + rim + hl);
                    // Cool, slightly blue water with a white highlight; rim leans dark.
                    float white = hl;
                    byte r = (byte)(Mathf.Lerp(0.55f, 1f, white) * 255f);
                    byte g = (byte)(Mathf.Lerp(0.62f, 1f, white) * 255f);
                    byte b = (byte)(Mathf.Lerp(0.72f, 1f, white) * 255f);
                    px[y * N + x] = new Color32(r, g, b, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            _dropTex = tex;
            return _dropTex;
        }

        /// <summary>True when the first-person eye (≈ the head, ~1.5 above the player root) is inside water.</summary>
        private bool EyeUnderwater()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var p = Game.PlayerPosition;
            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y + 1.5f), Mathf.FloorToInt(p.z)));
            return def?.Key == "water";
        }

        private void OnGUI()
        {
            // Subtle blue submerged wash, drawn before (and independent of) the weather overlay so it shows
            // even underwater in a cave or at night. Hidden in space and while the menu is open.
            if (_underwater > 0.01f && Game != null && !Game.SpaceViewActive && !Game.MenuOpen)
            {
                GUI.depth = 10;
                var prevWater = GUI.color;
                GUI.color = new Color(0.15f, 0.40f, 0.62f, 0.34f * _underwater);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prevWater;
            }

            // Frost creeps in from the screen edges on cold worlds. Drawn after the water wash (so a frozen lake
            // still reads as water) but before weather; tinted a pale icy cyan-white. Hidden in space / in menus.
            if (_frost > 0.01f && Game != null && !Game.SpaceViewActive && !Game.MenuOpen)
            {
                GUI.depth = 10;
                var prevFrost = GUI.color;
                GUI.color = new Color(0.82f, 0.92f, 1f, _frost);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), FrostTexture());
                GUI.color = prevFrost;
            }

            // Rain beads on the display — only with the visor open to the sky during real rain (the _wet driver
            // never rises for ashfall/snow/hail/sand). Drawn before the airborne rain streaks below.
            if (_wet > 0.01f && _dropInit && Game != null && !Game.SpaceViewActive && !Game.MenuOpen && Game.ExposedToSky)
            {
                GUI.depth = 10;
                var prevDrop = GUI.color;
                var tex = DropletTexture();
                float w = Screen.width, h = Screen.height;
                for (int i = 0; i < Drops; i++)
                {
                    float a = _wet * (_dvy[i] > 0f ? 0.85f : 0.6f);
                    float x = _dx[i] * w, y = _dy[i] * h, s = _dsize[i];
                    if (_dtrail[i] > 0.002f)
                    {
                        float tp = _dtrail[i] * h;
                        GUI.color = new Color(0.6f, 0.7f, 0.85f, a * 0.22f);
                        GUI.DrawTexture(new Rect(x - s * 0.18f, y - tp, s * 0.36f, tp), Texture2D.whiteTexture);
                    }

                    GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.DrawTexture(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), tex);
                }

                GUI.color = prevDrop;
            }

            var env = Game?.Environment;
            if (env == null || Game.SpaceViewActive || !Game.ExposedToSky)
            {
                return; // no rain/lightning under a roof or in a cave
            }

            string precip = env.Precipitation ?? "none";
            if (precip == "none" && _flash <= 0.01f)
            {
                return;
            }

            if (!_init)
            {
                Init();
            }

            GUI.depth = 10; // behind the HUD
            var prevColor = GUI.color;

            if (precip != "none" && !Game.MenuOpen)
            {
                float h = Screen.height, w = Screen.width, t = Time.time;

                // A faint wash sells the heavy air, tinted to the precipitation (wet blue, smoky ash, tan dust…).
                Color wash = precip switch
                {
                    "ash" => new Color(0.30f, 0.22f, 0.18f),
                    "sandstorm" => new Color(0.74f, 0.62f, 0.40f),
                    "snow" or "hail" => new Color(0.72f, 0.78f, 0.84f),
                    _ => new Color(0.42f, 0.52f, 0.62f),
                };
                float washStrength = precip == "sandstorm" ? 0.22f : 0.05f;
                GUI.color = new Color(wash.r, wash.g, wash.b, washStrength + env.Intensity * 0.07f);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

                // 2D streaks only for actual rain — the 3D pool carries snow/hail/ash/sand, and blue
                // screen-streaks over those would read as rain.
                if (precip == "rain")
                {
                    int count = Mathf.RoundToInt(Max * Mathf.Clamp01(0.45f + env.Intensity * 0.55f));
                    float speedBoost = env.Weather == "storm" ? 1.5f : 1f;
                    float slant = env.Weather == "storm" ? 12f : 5f;
                    GUI.color = new Color(0.62f, 0.78f, 1f, 0.42f);
                    for (int i = 0; i < count; i++)
                    {
                        float y = ((_phase[i] + t * _speed[i] * speedBoost) % 1f) * (h + 40f) - 20f;
                        float x = _x[i] * w + Mathf.Sin(t + i) * slant;
                        GUI.DrawTexture(new Rect(x, y, 2f, _len[i] * speedBoost), Texture2D.whiteTexture);
                    }
                }
            }

            if (_flash > 0.01f)
            {
                GUI.color = new Color(0.85f, 0.92f, 1f, _flash * 0.6f); // brighter sky-lighting flash
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            if (_boltTimer > 0.01f && !Game.MenuOpen)
            {
                DrawBolt();
            }

            GUI.color = prevColor;
        }

        /// <summary>A brief jagged white bolt from the top of the screen down to mid-screen (B8).</summary>
        private void DrawBolt()
        {
            float w = Screen.width, h = Screen.height;
            float alpha = Mathf.Clamp01(_boltTimer / 0.18f);
            int segs = _boltJitter.Length;
            float segH = (h * 0.6f) / segs;
            float x = _boltBaseX * w;
            float prevX = x, prevY = 0f;
            for (int i = 0; i < segs; i++)
            {
                float nextY = prevY + segH;
                float nextX = x + _boltJitter[i] * (w * 0.045f);
                // Approximate the slanted segment with a stack of short horizontal-stepped slabs.
                int steps = 6;
                for (int k = 0; k < steps; k++)
                {
                    float f = k / (float)steps;
                    float sx = Mathf.Lerp(prevX, nextX, f);
                    float sy = Mathf.Lerp(prevY, nextY, f);
                    GUI.color = new Color(1f, 1f, 1f, alpha);                       // hot core
                    GUI.DrawTexture(new Rect(sx - 1.5f, sy, 3f, segH / steps + 1f), Texture2D.whiteTexture);
                    GUI.color = new Color(0.7f, 0.85f, 1f, alpha * 0.5f);            // soft glow
                    GUI.DrawTexture(new Rect(sx - 4f, sy, 8f, segH / steps + 1f), Texture2D.whiteTexture);
                }

                prevX = nextX;
                prevY = nextY;
            }
        }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Unity side of the planet weather (#2175, #2176, #2177, #2178): turns a body's projected weather
    /// (<see cref="WeatherMapProjector"/>) into textures — the cloud shell of a planet in the flight view and the
    /// weather layer of the landing-pad map and the M world map — and turns spheres to their local time of day.
    /// All textures are small (128×64 shell, the map's own size for overlays) and recomposed only when a weather
    /// snapshot arrives or a nearby front drifts — never per frame.
    /// </summary>
    public static class PlanetWeatherVisuals
    {
        /// <summary>Cloud shell texture size (equirect, wraps in longitude).</summary>
        public const int ShellW = 128;
        public const int ShellH = 64;

        private static readonly Dictionary<int, float[]> _noise = new Dictionary<int, float[]>();
        private static bool _mappingMeasured;
        private static float _seamDeg;
        private static float _uvDirection = 1f;

        /// <summary>A seamless fBm pattern (0..1) for a shell, cached per seed (a few dozen KB each).</summary>
        public static float[] Noise(int seed)
        {
            if (_noise.TryGetValue(seed, out var cached))
            {
                return cached;
            }

            if (_noise.Count > 48)
            {
                _noise.Clear(); // bounded: a system has ~15 bodies, a session visits a handful of systems
            }

            var n = new float[ShellW * ShellH];
            for (int y = 0; y < ShellH; y++)
            {
                for (int x = 0; x < ShellW; x++)
                {
                    n[y * ShellW + x] = TiledFbm(x / (float)ShellW, y / (float)ShellH, 3, seed);
                }
            }

            _noise[seed] = n;
            return n;
        }

        /// <summary>A fresh shell texture (RGBA32, mipmapped, longitude wraps).</summary>
        public static Texture2D NewShellTexture(string name)
            => new Texture2D(ShellW, ShellH, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = name,
            };

        /// <summary>Composes a cloud shell. Without weather (<paramref name="pixels"/> null) it is the planet's base
        /// cover; with weather every texel takes the cover and tint of the map pixel beneath it. <paramref name="phaseU"/>
        /// is how far the shell has turned relative to its planet (in map longitude), so weather stays anchored to the
        /// ground while the cloud pattern drifts with the wind.</summary>
        public static void ComposeShell(Texture2D tex, Color32[] buffer, float[] noise, PlanetMapData map, WeatherMapPixels pixels,
            float baseCover, int cloudRgb, int groundRgb, int floraRgb, float phaseU)
        {
            var tintCache = new Dictionary<int, Color32>();
            for (int y = 0; y < ShellH; y++)
            {
                int my = map != null ? Mathf.Clamp((int)((y + 0.5f) / ShellH * map.Height), 0, map.Height - 1) : 0;
                for (int x = 0; x < ShellW; x++)
                {
                    float cover = baseCover;
                    Color32 tint = Rgb32(cloudRgb);
                    if (pixels != null && map != null && pixels.Cover.Length == map.Width * map.Height)
                    {
                        float u = (x + 0.5f) / ShellW - phaseU * _uvDirection;
                        u -= Mathf.Floor(u);
                        int mx = Mathf.Clamp((int)(u * map.Width), 0, map.Width - 1);
                        int mi = my * map.Width + mx;
                        cover = pixels.Cover[mi];
                        int key = pixels.Visual[mi] << 8 | pixels.Precip[mi];
                        if (!tintCache.TryGetValue(key, out tint))
                        {
                            tint = Rgb32(WeatherLook.OrbitTint(
                                WeatherMapProjector.States[pixels.Visual[mi]],
                                WeatherMapProjector.Precipitations[pixels.Precip[mi]],
                                cloudRgb, groundRgb, floraRgb));
                            tintCache[key] = tint;
                        }
                    }

                    float threshold = Mathf.Lerp(0.72f, 0.28f, Mathf.Clamp01(cover));
                    float a = cover <= 0.001f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((noise[y * ShellW + x] - threshold) * 3.5f));
                    tint.a = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    buffer[y * ShellW + x] = tint;
                }
            }

            tex.SetPixels32(buffer);
            tex.Apply(true);
        }

        /// <summary>How strongly a state shows on a MAP layer (0 = clear sky, transparent).</summary>
        public static float MapSeverity(string state) => state switch
        {
            "clouds" => 0.30f,
            "drizzle" => 0.38f,
            "rain" => 0.58f,
            "storm" => 0.85f,
            "toxic_storm" => 0.85f,
            "blizzard" => 0.85f,
            "fog" => 0.55f,
            "ground_fog" => 0.45f,
            "gale" => 0.35f,
            "acid_rain" => 0.65f,
            "ion_storm" => 0.50f,
            "meteor_shower" => 0.30f,
            "ember_fall" => 0.70f,
            "spore_bloom" => 0.50f,
            "heatwave" => 0.15f,
            _ => 0f,
        };

        /// <summary>Composes a map weather layer (same size as the map): clear sky is transparent, every weather shows
        /// in its orbit tint with an alpha by severity, broken up by a cloud pattern so it reads as weather, not paint.
        /// Storm regions read darkest; the map below stays legible (alpha ≤ ~0.75).</summary>
        public static void ComposeMapLayer(Texture2D tex, Color32[] buffer, PlanetMapData map, WeatherMapPixels pixels,
            float[] noise, int cloudRgb)
        {
            var tintCache = new Dictionary<int, Color32>();
            for (int y = 0; y < map.Height; y++)
            {
                int ny = Mathf.Clamp((int)((y + 0.5f) / map.Height * ShellH), 0, ShellH - 1);
                for (int x = 0; x < map.Width; x++)
                {
                    int i = y * map.Width + x;
                    int nx = Mathf.Clamp((int)((x + 0.5f) / map.Width * ShellW), 0, ShellW - 1);
                    string drawn = WeatherMapProjector.States[pixels.Visual[i]];
                    float sev = MapSeverity(drawn);
                    int key = pixels.Visual[i] << 8 | pixels.Precip[i];
                    if (!tintCache.TryGetValue(key, out var tint))
                    {
                        tint = Rgb32(WeatherLook.OrbitTint(drawn, WeatherMapProjector.Precipitations[pixels.Precip[i]],
                            cloudRgb, map.GroundRgb, map.FloraRgb));
                        tintCache[key] = tint;
                    }

                    float n = noise[ny * ShellW + nx];
                    float a = sev * (0.55f + 0.45f * n) * 0.88f;
                    tint.a = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    buffer[i] = tint;
                }
            }

            tex.SetPixels32(buffer);
            tex.Apply(false);
        }

        /// <summary>Turns a body sphere so its noon meridian faces the star (#2177). <paramref name="sunLocal"/> is the
        /// direction from the body to the star in the sphere's PARENT space.</summary>
        public static void Spin(Transform sphere, double timeOfDay, Vector3 sunLocal)
        {
            if (sphere == null)
            {
                return;
            }

            MeasureMapping();
            float sunAz = Mathf.Atan2(sunLocal.z, sunLocal.x) * Mathf.Rad2Deg;
            float yaw = (float)PlanetSpin.YawDegrees(timeOfDay, sunAz, _seamDeg, _uvDirection);
            sphere.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>The point on a primitive sphere (radius 0.5, unrotated local space) at map coordinates (u, v),
        /// v = 0 the south pole — for lightning flashes and markers.</summary>
        public static Vector3 LocalPoint(float u, float v)
        {
            MeasureMapping();
            float az = (_seamDeg + _uvDirection * u * 360f) * Mathf.Deg2Rad;
            float el = (v - 0.5f) * Mathf.PI;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)) * 0.5f;
        }

        /// <summary>Measures once where Unity's sphere mesh puts longitude u = 0 and which way u runs, from the
        /// vertices themselves — so the noon meridian is right whatever the primitive's UV layout.</summary>
        private static void MeasureMapping()
        {
            if (_mappingMeasured)
            {
                return;
            }

            _mappingMeasured = true;
            var mesh = FxKit.SphereMesh;
            if (mesh == null)
            {
                return;
            }

            var verts = mesh.vertices;
            var uvs = mesh.uv;
            float bestSpread = float.MaxValue;
            for (int dir = -1; dir <= 1; dir += 2)
            {
                float sx = 0f, sz = 0f;
                int n = 0;
                for (int i = 0; i < verts.Length && i < uvs.Length; i++)
                {
                    var p = verts[i];
                    if (Mathf.Abs(p.y) > 0.4f || uvs[i].x <= 0.001f || uvs[i].x >= 0.999f)
                    {
                        continue; // poles and the seam column carry no clean longitude
                    }

                    float seam = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg - dir * uvs[i].x * 360f;
                    sx += Mathf.Cos(seam * Mathf.Deg2Rad);
                    sz += Mathf.Sin(seam * Mathf.Deg2Rad);
                    n++;
                }

                if (n == 0)
                {
                    continue;
                }

                float r = Mathf.Sqrt(sx * sx + sz * sz) / n; // 1 = every vertex agrees on the seam
                float spread = 1f - r;
                if (spread < bestSpread)
                {
                    bestSpread = spread;
                    _uvDirection = dir;
                    _seamDeg = Mathf.Atan2(sz, sx) * Mathf.Rad2Deg;
                }
            }
        }

        /// <summary>Map-pixel indices whose drawn weather throws lightning (storms).</summary>
        public static void CollectStormPixels(WeatherMapPixels pixels, List<int> into)
        {
            into.Clear();
            if (pixels == null)
            {
                return;
            }

            for (int i = 0; i < pixels.Visual.Length; i++)
            {
                string s = WeatherMapProjector.States[pixels.Visual[i]];
                if (s == "storm" || s == "toxic_storm")
                {
                    into.Add(i);
                }
            }
        }

        /// <summary>The weather of a body as the projector's episode + extrapolated fronts, or false without data.</summary>
        public static bool TryBodyWeather(GameBootstrap game, string bodyId, out NetBodyWeather body)
        {
            body = null;
            return game != null && game.SystemWeather.TryGet(bodyId ?? string.Empty, out body);
        }

        /// <summary>Projects a body's weather onto its map (the caller owns <paramref name="pixels"/>).</summary>
        public static void Project(GameBootstrap game, NetBodyWeather body, PlanetMapData map, PlanetType planet,
            float cloudDensity, WeatherMapPixels pixels)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            var state = game.SystemWeather;
            WeatherMapProjector.Project(map, SystemWeatherState.Episode(body), state.Fronts(body, now, map.Circumference),
                game.WorldSeed, state.SystemTimeDays(now), planet, cloudDensity, body.Precip, pixels);
        }

        /// <summary>The weather glyph of a body for a map marker (#2178), in the HUD's warning colour for violent and
        /// exotic weather — "" without data, and for an airless body under a plain clear sky (it has no sky).</summary>
        public static string GlyphMarkup(GameBootstrap game, string bodyId)
        {
            if (!TryBodyWeather(game, bodyId, out var body) || body == null)
            {
                return string.Empty;
            }

            string state = string.IsNullOrEmpty(body.State) ? "clear" : body.State;
            if (state == "clear" && body.LadderCeiling == 0)
            {
                return string.Empty;
            }

            string glyph = WeatherLook.Glyph(state);
            string hex = WeatherLook.FamilyColorHex(WeatherLook.Family(state));
            return hex is null ? glyph : $"<color={hex}>{glyph}</color>";
        }

        /// <summary>The body's day fraction now (extrapolated while its world is loaded).</summary>
        public static double TimeOfDay(GameBootstrap game, NetBodyWeather body, PlanetType planet)
            => game.SystemWeather.TimeOfDay(body, Time.realtimeSinceStartupAsDouble, planet?.DayLengthSeconds ?? 600.0);

        public static Color32 Rgb32(int rgb)
            => new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);

        public static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        // --- Seamless (wrapping) value-noise fBm for the cloud patterns ---
        private static float TiledFbm(float u, float v, int baseFreq, int seed)
        {
            float sum = 0f, amp = 0.5f;
            int freq = baseFreq;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * TiledNoise(u * freq, v * freq, freq, seed + o * 97);
                freq *= 2;
                amp *= 0.5f;
            }

            return sum;
        }

        private static float TiledNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            int x0 = ((xi % period) + period) % period, x1 = (x0 + 1) % period;
            int y0 = ((yi % period) + period) % period, y1 = (y0 + 1) % period;
            float v00 = Hash01(x0, y0, seed), v10 = Hash01(x1, y0, seed);
            float v01 = Hash01(x0, y1, seed), v11 = Hash01(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, u), Mathf.Lerp(v01, v11, u), v);
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }

    /// <summary>The remembered on/off switch of the map weather layers (#2176/#2178) — one setting for the landing-pad
    /// map and the M world map, stored locally (a viewer convenience, not game state).</summary>
    public static class MapWeatherLayer
    {
        private const string PrefKey = "map_weather_layer";
        private static int _cached = -1;

        public static bool On
        {
            get
            {
                if (_cached < 0)
                {
                    try
                    {
                        _cached = PlayerPrefs.GetInt(PrefKey, 1);
                    }
                    catch (System.Exception)
                    {
                        _cached = 1; // storage blocked (private window): default on
                    }
                }

                return _cached != 0;
            }
            set
            {
                _cached = value ? 1 : 0;
                try
                {
                    PlayerPrefs.SetInt(PrefKey, _cached);
                }
                catch (System.Exception)
                {
                    // not persisted this session — the toggle still works
                }
            }
        }
    }
}

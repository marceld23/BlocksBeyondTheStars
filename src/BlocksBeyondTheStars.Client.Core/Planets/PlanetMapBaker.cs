// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Everything a planet map bake needs (#2172) — captured on the main thread, so the bake itself touches
    /// no Unity API and can run on a worker thread (desktop) or in time slices (WebGL).</summary>
    public sealed class PlanetMapRequest
    {
        public GameContent Content = null!;
        public long WorldSeed;

        /// <summary>The body's location id (e.g. <c>sys0-p1</c>) — salts the terrain seed (#478).</summary>
        public string BodyId = string.Empty;

        /// <summary>The "System · Body" key the per-world flora colours are rolled with.</summary>
        public string FloraKey = string.Empty;

        public string PlanetTypeKey = string.Empty;
        public int Circumference;
        public int Width;
        public int Height;
        public bool Continents;
        public int Generation;

        /// <summary>The save's lava-core volcano option (#1631) — part of the server's terrain like the continents.</summary>
        public bool LavaCoreVolcanoes;

        /// <summary>Airless moons carry craters (the server's rule, mirrored by the caller).</summary>
        public bool Cratered;

        /// <summary>Average colour (0xRRGGBB) per block numeric id — at least every ground block the world can show
        /// (its biome surfaces, snow, ice). Read from the block atlas on the main thread.</summary>
        public IReadOnlyDictionary<ushort, int> GroundColors = new Dictionary<ushort, int>();

        /// <summary>The cache key of this bake (every input that changes a pixel).</summary>
        public string Key => $"{WorldSeed}|{FloraKey}|{BodyId}|{PlanetTypeKey}|{Circumference}|{Width}x{Height}|{Continents}|{Generation}|{Cratered}|{LavaCoreVolcanoes}";
    }

    /// <summary>A baked planet map: the colour texture plus the per-pixel terrain facts the weather layers project
    /// onto (#2172, #2175). Row 0 is the southern edge of the latitude band, column 0 longitude 0 — the equirect
    /// layout Unity's sphere UVs and the landing-pad map both use.</summary>
    public sealed class PlanetMapData
    {
        public int Width;
        public int Height;
        public int Circumference;
        public int LatitudePeriod;

        /// <summary>RGBA32, row-major.</summary>
        public byte[] Rgba = Array.Empty<byte>();

        /// <summary>Generated surface Y per pixel.</summary>
        public short[] Heights = Array.Empty<short>();

        /// <summary>Biome index per pixel (the per-biome weather offset keys on it).</summary>
        public byte[] Biomes = Array.Empty<byte>();

        /// <summary>Surface air temperature (°C, clamped to ±127) per pixel, before weather and day/night.</summary>
        public sbyte[] Temperatures = Array.Empty<sbyte>();

        /// <summary>Per-pixel flags, see <see cref="FlagSea"/> etc.</summary>
        public byte[] Flags = Array.Empty<byte>();

        /// <summary>The average ground colour (0xRRGGBB) — the flat look before the map is ready.</summary>
        public int GroundRgb;

        /// <summary>The world's own vegetation colour (0xRRGGBB).</summary>
        public int FloraRgb;

        public const byte FlagSea = 1;
        public const byte FlagFrozen = 2;
        public const byte FlagFluid = 4; // pond / lake above sea level

        /// <summary>The world X of a pixel column's centre.</summary>
        public int WorldX(int px) => (int)((px + 0.5) / Width * Circumference);

        /// <summary>The world Z of a pixel row's centre.</summary>
        public int WorldZ(int py) => (int)(((py + 0.5) / Height - 0.5) * LatitudePeriod);
    }

    /// <summary>
    /// Bakes an equirect map of a body's REAL generated world (#2172): the deterministic generator runs in the client
    /// from (seed, body, type, circumference, generation), so the orbit sphere, the bodies in the surface sky and the
    /// landing-pad map show the terrain you will land on — seas and lava with depth, ice on cold seas, the biome's own
    /// ground, snow and ice above the snow line, the world's water and vegetation colours, craters on airless moons.
    /// <para>Runs in steps of rows (<see cref="Step"/>) so the same code serves a worker thread on desktop (loop until
    /// done) and time slices on the main thread in the browser, where managed threads do not run. The first step pays
    /// the world calibration (~90 ms on .NET, once per body and session — the generator caches it statically).</para>
    /// </summary>
    public sealed class PlanetMapBakeJob
    {
        private readonly PlanetMapRequest _req;
        private readonly PlanetMapData _data;
        private WorldGenerator? _gen;
        private PlanetType? _planet;
        private int _row;
        private bool _prepared;

        // Per-world constants resolved in Prepare.
        private int _sea;
        private bool _lavaSea, _gasSea, _frozenSea, _rainbow;
        private float _baseY, _amplitude2;
        private (float R, float G, float B) _ground, _flora, _shallow, _deep, _ice;
        private int _waterTintMode;

        public PlanetMapBakeJob(PlanetMapRequest request)
        {
            _req = request ?? throw new ArgumentNullException(nameof(request));
            int n = Math.Max(1, request.Width) * Math.Max(1, request.Height);
            _data = new PlanetMapData
            {
                Width = Math.Max(1, request.Width),
                Height = Math.Max(1, request.Height),
                Circumference = Math.Max(1, request.Circumference),
                LatitudePeriod = WorldConstants.LatitudePeriodFor(Math.Max(1, request.Circumference)),
                Rgba = new byte[n * 4],
                Heights = new short[n],
                Biomes = new byte[n],
                Temperatures = new sbyte[n],
                Flags = new byte[n],
            };
        }

        /// <summary>The request this job bakes.</summary>
        public PlanetMapRequest Request => _req;

        /// <summary>The result (complete once <see cref="Done"/>).</summary>
        public PlanetMapData Data => _data;

        /// <summary>True once every row is baked.</summary>
        public bool Done => _row >= _data.Height;

        /// <summary>Bakes up to <paramref name="maxRows"/> rows (the first call also prepares the generator). Returns
        /// <see cref="Done"/>.</summary>
        public bool Step(int maxRows)
        {
            if (!_prepared)
            {
                Prepare();
                return Done;
            }

            int end = Math.Min(_data.Height, _row + Math.Max(1, maxRows));
            for (; _row < end; _row++)
            {
                BakeRow(_row);
            }

            return Done;
        }

        /// <summary>Bakes the whole map in one go (worker threads, tests).</summary>
        public PlanetMapData Run()
        {
            while (!Step(_data.Height))
            {
            }

            return _data;
        }

        private void Prepare()
        {
            _prepared = true;
            _planet = _req.Content?.GetPlanet(_req.PlanetTypeKey ?? string.Empty);
            _ground = Rgb(0x736F6B);
            _data.GroundRgb = 0x736F6B;
            if (_planet is null || _req.Content is null)
            {
                // Unknown type: a flat grey map (the caller usually draws the data-driven flat colour instead).
                for (int i = 0; i < _data.Flags.Length; i++)
                {
                    Put(i, _ground.R, _ground.G, _ground.B);
                }

                _row = _data.Height;
                return;
            }

            var planet = _planet;
            var gen = new WorldGenerator(_req.WorldSeed, _req.Content);
            gen.SetWorldMode(_data.Circumference, _req.Cratered, landingPads: null, _req.BodyId);
            gen.SetContinentsEnabled(_req.Continents); // #704: same creation-time gate as the server
            gen.SetLavaCoreVolcanoes(_req.LavaCoreVolcanoes); // #1631: same volcanoes as the server
            gen.SetTerrainGeneration(_req.Generation); // #1644: same landform generation as the server
            _gen = gen;

            _sea = gen.SeaLevel(planet); // the first terrain query: pays the world calibration
            bool hasAir = !planet.IsAirless;
            bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
            double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
            _lavaSea = waterAb <= 0.0 && (planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0)) > 0.0;
            _gasSea = planet.IsGasWorld; // #2112: the gas giant's sea of gas
            _frozenSea = !_lavaSea && !_gasSea && _sea != int.MinValue && gen.SeaSurfaceFrozen(planet);

            var surface = _req.Content.GetBlock(planet.SurfaceBlock);
            _data.GroundRgb = surface != null && _req.GroundColors.TryGetValue(surface.NumericId.Value, out int g) ? g : 0x736F6B;
            _ground = Rgb(_data.GroundRgb);

            var (fr, fg, fb) = FloraTints.For(_req.WorldSeed, _req.FloraKey ?? string.Empty,
                planet.SurfaceBlock == "mycelium" ? "mushroom_cap" : "tree_leaves");
            _flora = (fr, fg, fb);
            _data.FloraRgb = ToRgb(fr, fg, fb);

            // The world's own water (#1758/#2027): a tint shifts both sea shades; the rainbow mode paints hue bands.
            bool toxicWater = WorldTraits.For(planet, WorldGenerator.RosterSeedFor(_req.WorldSeed, _req.BodyId), _req.Generation).ToxicWater;
            var (waterRgb, mode) = FluidTints.ForWorld(_req.WorldSeed, _req.BodyId, planet, _req.Generation, toxicWater);
            _waterTintMode = (int)mode;
            if (_gasSea)
            {
                _shallow = (0.88f, 0.72f, 0.52f);
                _deep = (0.62f, 0.42f, 0.40f);
            }
            else if (_lavaSea)
            {
                _shallow = (0.95f, 0.45f, 0.15f);
                _deep = (0.55f, 0.15f, 0.05f);
            }
            else if (mode == FluidTints.Mode.Tint)
            {
                var w = Rgb(waterRgb);
                _shallow = (Math.Min(1f, w.R * 1.15f), Math.Min(1f, w.G * 1.15f), Math.Min(1f, w.B * 1.15f));
                _deep = (w.R * 0.45f, w.G * 0.45f, w.B * 0.45f);
            }
            else
            {
                _shallow = (0.30f, 0.55f, 0.78f);
                _deep = (0.10f, 0.22f, 0.45f);
            }

            _rainbow = mode == FluidTints.Mode.Rainbow && !_lavaSea && !_gasSea;
            _ice = (0.84f, 0.91f, 0.97f);
            _baseY = _sea != int.MinValue ? _sea : planet.BaseHeight - planet.Amplitude;
            _amplitude2 = Math.Max(1f, planet.Amplitude * 2f);
        }

        private void BakeRow(int y)
        {
            var gen = _gen!;
            var planet = _planet!;
            int wz = _data.WorldZ(y);
            for (int x = 0; x < _data.Width; x++)
            {
                int i = y * _data.Width + x;
                int wx = _data.WorldX(x);
                var col = gen.SummarizeSurface(planet, wx, wz);
                int h = col.Height;
                _data.Heights[i] = (short)Math.Clamp(h, short.MinValue, short.MaxValue);
                _data.Biomes[i] = (byte)Math.Clamp(col.Biome, 0, 255);
                _data.Temperatures[i] = (sbyte)Math.Clamp((int)Math.Round(gen.AirTemperatureAt(planet, h + 1)), -127, 127);

                float r, g, b;
                if (_sea != int.MinValue && h <= _sea)
                {
                    _data.Flags[i] |= PlanetMapData.FlagSea;
                    float depth = Math.Clamp((_sea - h) / 14f, 0f, 1f); // depth-shaded sea
                    (r, g, b) = Lerp(SeaShallow(x, y), _deep, depth);
                    if (_frozenSea)
                    {
                        _data.Flags[i] |= PlanetMapData.FlagFrozen;
                        (r, g, b) = Lerp(_ice, (r, g, b), 0.25f);
                    }
                }
                else if (!_lavaSea && gen.SurfacePondDepth(planet, wx, wz) > 0)
                {
                    _data.Flags[i] |= PlanetMapData.FlagFluid;
                    (r, g, b) = SeaShallow(x, y); // an upland pond/lake flush with the terrain
                }
                else
                {
                    float rel = Math.Clamp((h - _baseY) / _amplitude2, 0f, 1f);
                    float shade = 0.7f + 0.5f * rel; // height-shaded ground
                    var ground = _req.GroundColors.TryGetValue(col.Surface.Value, out int gc) ? Rgb(gc) : _ground;
                    (r, g, b) = (ground.R * shade, ground.G * shade, ground.B * shade);
                    float veg = (float)col.Vegetation;
                    if (veg > 0.005f)
                    {
                        float fs = 0.75f + 0.35f * rel;
                        (r, g, b) = Lerp((r, g, b), (_flora.R * fs, _flora.G * fs, _flora.B * fs), Math.Min(0.85f, veg * 1.4f));
                    }
                }

                Put(i, r, g, b);
            }
        }

        /// <summary>The shallow sea shade at a pixel — rainbow seas cycle their hue across the map.</summary>
        private (float R, float G, float B) SeaShallow(int x, int y)
        {
            if (!_rainbow)
            {
                return _shallow;
            }

            float hue = (x / (float)_data.Width * 3f + y / (float)_data.Height) % 1f;
            return Hsv(hue, 0.55f, 0.9f);
        }

        private void Put(int i, float r, float g, float b)
        {
            int o = i * 4;
            _data.Rgba[o] = ToByte(r);
            _data.Rgba[o + 1] = ToByte(g);
            _data.Rgba[o + 2] = ToByte(b);
            _data.Rgba[o + 3] = 255;
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);

        private static (float R, float G, float B) Rgb(int rgb)
            => (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static int ToRgb(float r, float g, float b)
            => (ToByte(r) << 16) | (ToByte(g) << 8) | ToByte(b);

        private static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t)
            => (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        private static (float R, float G, float B) Hsv(float h, float s, float v)
        {
            float c = v * s, hh = h * 6f, x = c * (1f - Math.Abs(hh % 2f - 1f)), m = v - c;
            (float r, float g, float b) = hh switch
            {
                < 1f => (c, x, 0f),
                < 2f => (x, c, 0f),
                < 3f => (0f, c, x),
                < 4f => (0f, x, c),
                < 5f => (x, 0f, c),
                _ => (c, 0f, x),
            };
            return (r + m, g + m, b + m);
        }

        /// <summary>The block ids whose colours a bake of this planet type can need — the main thread reads exactly
        /// these from the atlas before the job starts.</summary>
        public static IEnumerable<ushort> GroundBlockIds(GameContent content, string planetTypeKey)
        {
            var planet = content?.GetPlanet(planetTypeKey ?? string.Empty);
            if (planet is null || content is null)
            {
                yield break;
            }

            var keys = new List<string> { planet.SurfaceBlock, "snow", "ice" };
            foreach (var b in planet.Biomes)
            {
                keys.Add(b.SurfaceBlock);
            }

            var seen = new HashSet<ushort>();
            foreach (var key in keys)
            {
                var def = content.GetBlock(key ?? string.Empty);
                if (def != null && seen.Add(def.NumericId.Value))
                {
                    yield return def.NumericId.Value;
                }
            }
        }
    }
}

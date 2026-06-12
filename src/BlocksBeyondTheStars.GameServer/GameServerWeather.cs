using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Day/night + weather + sun colour (World systems), server-authoritative. A world clock
/// advances time of day (per-planet day length); a weather state machine cycles
/// clear→clouds→rain→storm biased by the planet's storm chance — unless the planet's weather is
/// fixed ("clear"/"overcast" planets have no changing weather). The sun's light colour comes
/// from the active star system. Broadcast on join, on weather change, and periodically so
/// clients can interpolate time locally.
/// </summary>
public sealed partial class GameServer
{
    // Stellar colour anchors from hot to cool (blue-white → white → yellow → orange → red), each with a weight
    // so most systems land near a natural sun-like white/yellow and blue/red stars are the rarer extremes.
    private static readonly (int Rgb, int Weight)[] StarRamp =
    {
        (0xC9D4FF, 7),   // blue-white (hot O/B/A)
        (0xFFF6F0, 15),  // white (F)
        (0xFFF1CE, 30),  // yellow-white, sun-like (G)
        (0xFFE6A0, 22),  // yellow (early K)
        (0xFFC97E, 14),  // orange (late K)
        (0xFFB074, 8),   // red-orange (M)
    };

    private static readonly string[] WeatherStates = { "clear", "clouds", "rain", "storm" };

    private const double WeatherChangeInterval = 25.0;
    private const double EnvBroadcastInterval = 5.0;

    private double _dayFraction { get => _worlds.Active.DayFraction; set => _worlds.Active.DayFraction = value; }
    private double _dayLength { get => _worlds.Active.DayLength; set => _worlds.Active.DayLength = value; }
    private double _stormChance { get => _worlds.Active.StormChance; set => _worlds.Active.StormChance = value; }
    private string _planetWeatherMode { get => _worlds.Active.PlanetWeatherMode; set => _worlds.Active.PlanetWeatherMode = value; }
    private string _weatherState { get => _worlds.Active.WeatherState; set => _worlds.Active.WeatherState = value; }
    private float _weatherIntensity { get => _worlds.Active.WeatherIntensity; set => _worlds.Active.WeatherIntensity = value; }
    private double _weatherTimer { get => _worlds.Active.WeatherTimer; set => _worlds.Active.WeatherTimer = value; }
    private double _sinceEnvBroadcast { get => _worlds.Active.SinceEnvBroadcast; set => _worlds.Active.SinceEnvBroadcast = value; }
    private int _sunColor { get => _worlds.Active.SunColor; set => _worlds.Active.SunColor = value; }
    private int _cloudColor { get => _worlds.Active.CloudColor; set => _worlds.Active.CloudColor = value; }
    private int _floraTint { get => _worlds.Active.FloraTint; set => _worlds.Active.FloraTint = value; }
    private float _cloudDensity { get => _worlds.Active.CloudDensity; set => _worlds.Active.CloudDensity = value; }
    private bool _breathable { get => _worlds.Active.Breathable; set => _worlds.Active.Breathable = value; }
    private bool _spaceSky { get => _worlds.Active.SpaceSky; set => _worlds.Active.SpaceSky = value; }
    private string _biome { get => _worlds.Active.Biome; set => _worlds.Active.Biome = value; }
    private double _oxygenExtractability { get => _worlds.Active.OxygenExtractability; set => _worlds.Active.OxygenExtractability = value; }
    private double _atmosphereHeight { get => _worlds.Active.AtmosphereHeight; set => _worlds.Active.AtmosphereHeight = value; }
    private System.Random _envRng { get => _worlds.Active.EnvRng; set => _worlds.Active.EnvRng = value; }

    // Public accessors (HUD / tests).
    public float TimeOfDay => (float)_dayFraction;
    public string Weather => _weatherState;
    public int SunColor => _sunColor;

    /// <summary>The planet's uniform flora base hue (0xRRGGBB) — all plant life is re-tinted to this.</summary>
    public int FloraTint => _floraTint;

    /// <summary>Whether the current planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool AtmosphereBreathable => _breathable;

    /// <summary>Whether this body shows a space sky (black + stars) on the surface (landable asteroids).</summary>
    public bool SpaceSky => _spaceSky;

    /// <summary>Absolute Y above which an on-foot player is in space (item 10); 0 = no atmosphere line here.</summary>
    public double AtmosphereHeight => _atmosphereHeight;

    private void InitWeather()
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        _dayLength = planet?.DayLengthSeconds ?? 600.0;
        _stormChance = planet?.StormChance ?? 0.35;
        _planetWeatherMode = string.IsNullOrEmpty(planet?.Weather) ? "dynamic" : planet!.Weather;
        _breathable = string.Equals(planet?.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase);
        _spaceSky = planet?.SpaceSky ?? false;
        _cloudColor = planet?.CloudColor ?? 0xEDEFF2;
        // Airless bodies (space sky) never have clouds.
        _cloudDensity = _spaceSky ? 0f : (float)System.Math.Clamp(planet?.CloudDensity ?? 0.45, 0.0, 1.0);
        _biome = string.IsNullOrEmpty(_worlds.Active.PlanetType) ? "rock" : _worlds.Active.PlanetType;
        _oxygenExtractability = System.Math.Clamp(planet?.OxygenExtractability ?? 0.0, 0.0, 1.0);
        _atmosphereHeight = planet?.AtmosphereHeight ?? 0.0;
        _envRng = new System.Random((int)_meta.Seed);
        _dayFraction = 0.35;
        _weatherTimer = 0;
        _sinceEnvBroadcast = 0;

        var (system, _) = ActiveLocationNames();
        _sunColor = StarColor(system);
        // One uniform flora base hue per planet (green / brown / pink / purple …), deterministic from the
        // world seed + planet, so every plant on the world shares a base colour (applied as a desaturate-tint
        // on the client). Airless/floraless worlds still carry a value; it just goes unused with no flora.
        _floraTint = FloraColor(unchecked((uint)(_meta.Seed ^ StableStringHash(_worlds.Active.PlanetType ?? string.Empty))));

        // Fixed-weather planets: lock the state; dynamic ones start clear.
        _weatherState = _planetWeatherMode switch
        {
            "clear" => "clear",
            "overcast" => "clouds",
            _ => "clear",
        };
        _weatherIntensity = IntensityOf(_weatherState);
    }

    private void TickWeather(double dt)
    {
        // Advance the day clock (wrap 0..1).
        if (_dayLength > 0)
        {
            _dayFraction = (_dayFraction + dt / _dayLength) % 1.0;
        }

        // Dynamic planets cycle weather; fixed ones never change.
        if (_planetWeatherMode == "dynamic")
        {
            _weatherTimer += dt;
            if (_weatherTimer >= WeatherChangeInterval)
            {
                _weatherTimer = 0;
                int idx = System.Array.IndexOf(WeatherStates, _weatherState);
                if (idx < 0)
                {
                    idx = 0;
                }

                idx = _envRng.NextDouble() < _stormChance
                    ? System.Math.Min(WeatherStates.Length - 1, idx + 1)
                    : System.Math.Max(0, idx - 1);

                string next = WeatherStates[idx];
                if (next != _weatherState)
                {
                    _weatherState = next;
                    _weatherIntensity = IntensityOf(next);
                    BroadcastEnvironment();
                }
            }
        }

        _sinceEnvBroadcast += dt;
        if (_sinceEnvBroadcast >= EnvBroadcastInterval)
        {
            _sinceEnvBroadcast = 0;
            BroadcastEnvironment();
        }
    }

    private static float IntensityOf(string weather) => weather switch
    {
        "storm" => 1.0f,
        "rain" => 0.6f,
        "clouds" => 0.3f,
        _ => 0f,
    };

    /// <summary>Builds the environment for a position — weather is per BIOME (a stormy biome can rain while
    /// a neighbouring clear biome stays sunny), shifted around the world's current weather; the rest
    /// (time, sun, clouds, atmosphere) is world-level. Empty position uses the world's base weather.</summary>
    private WorldEnvironment BuildEnvironment(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos = default)
    {
        var (state, intensity) = BiomeWeatherAt(pos);
        float temperature = CurrentTemperature(state, _dayFraction);
        return new WorldEnvironment
        {
            TimeOfDay = (float)_dayFraction,
            DayLengthSeconds = (float)_dayLength,
            Weather = state,
            Intensity = intensity,
            Temperature = temperature,
            Precipitation = PrecipitationFor(state, temperature),
            SunColor = _sunColor,
            CloudColor = _cloudColor,
            FloraTint = _floraTint,
            Circumference = _world.Circumference,
            LatitudeLimit = WorldConstants.LatitudeLimitFor(_world.Circumference),
            CloudDensity = _cloudDensity,
            Breathable = _breathable,
            SpaceSky = _spaceSky,
            Biome = _biome,
        };
    }

    /// <summary>Air temperature (°C): the planet-type base + a per-world seeded variation (so there are
    /// "especially hot/cold" worlds) + a weather cooling + a day↔night swing (bigger on airless worlds).</summary>
    /// <summary>Sentinel for "no meaningful air temperature" (vacuum / above the atmosphere) — the HUD shows "—".</summary>
    public const float NoAirTemperature = -999f;

    private float CurrentTemperature(string weather, double timeOfDay)
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet?.Void == true)
        {
            return 22f; // a ship / station cabin is climate-controlled
        }

        if (_spaceSky)
        {
            return NoAirTemperature; // airless vacuum world (asteroid/crystal) → no air temp, HUD shows "—"
        }

        double baseT = planet?.BaseTemperature ?? 15.0;
        double variation = ((((uint)StableStringHash(_world.LocationId) ^ (uint)_meta.Seed) & 0xFFFFu) / 65535.0) * 28.0 - 14.0;
        double weatherDelta = weather switch { "storm" => -8.0, "rain" => -5.0, "clouds" => -2.0, _ => 2.0 };
        double swing = _breathable ? 6.0 : 16.0; // airless worlds swing hard between day and night
        double dayNight = System.Math.Cos((timeOfDay - 0.5) * 2.0 * System.Math.PI) * swing;
        return (float)System.Math.Round(baseT + variation + weatherDelta + dayNight);
    }

    /// <summary>The precipitation form for the current weather + temperature: nothing unless it's actually
    /// raining/storming, then snow/hail when cold, ash (fire-rain) when very hot, else rain. (Sandstorm —
    /// stage 2 — keys off a dry/sand surface.)</summary>
    private string PrecipitationFor(string weather, float temp)
    {
        if (weather != "rain" && weather != "storm")
        {
            return "none";
        }

        if (_content.GetPlanet(_worlds.Active.PlanetType)?.SurfaceBlock == "sand") return "sandstorm"; // dry worlds blow sand
        if (temp >= 55f) return "ash";   // fire-rain / ash on very hot (lava) worlds
        if (temp <= -15f) return "hail"; // very cold → hail
        if (temp <= 2f) return "snow";   // cold → snow
        return "rain";
    }

    /// <summary>The weather in the biome at a position: the world's weather level shifted by a persistent
    /// per-biome offset (some biomes are always wetter/drier). Fixed-weather planets don't vary by biome.</summary>
    private (string State, float Intensity) BiomeWeatherAt(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        if (_planetWeatherMode != "dynamic")
        {
            return (_weatherState, _weatherIntensity);
        }

        int worldLevel = System.Array.IndexOf(WeatherStates, _weatherState);
        if (worldLevel < 0)
        {
            worldLevel = 0;
        }

        int biomeIdx = _generator.BiomeIndexAt(_world.Planet, (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z));
        int level = System.Math.Clamp(worldLevel + BiomeWeatherOffset(biomeIdx), 0, WeatherStates.Length - 1);
        string state = WeatherStates[level];
        return (state, IntensityOf(state));
    }

    /// <summary>Persistent per-biome weather offset (-1 drier .. +2 wetter), deterministic per world.</summary>
    private int BiomeWeatherOffset(int biomeIdx)
    {
        long h = _meta.Seed ^ (biomeIdx * 2654435761L) ^ 0xB10;
        return (int)((ulong)(h < 0 ? -h : h) % 4UL) - 1; // 0..3 → -1..+2
    }

    private void SendEnvironment(PlayerSession session) => Send(session, BuildEnvironment(session.State.Position));

    /// <summary>Each player in the world gets the weather of THEIR biome (per-player, not one broadcast).</summary>
    private void BroadcastEnvironment()
    {
        foreach (var s in JoinedInActiveWorld())
        {
            SendEnvironment(s);
        }
    }

    private static int StableStringHash(string s)
    {
        int h = 17;
        foreach (char c in s ?? string.Empty)
        {
            h = h * 31 + c;
        }

        return h;
    }

    /// <summary>A deterministic, continuously-varying star colour for a system: the system's hash picks a
    /// weighted anchor on the hot→cool stellar ramp and a second hash blends it toward a neighbour, so colours
    /// span the full ramp (not just a handful of fixed swatches) while clustering on natural sun-like hues.</summary>
    private static int StarColor(string system)
    {
        uint h = (uint)StableStringHash(system);
        int total = 0;
        foreach (var (_, w) in StarRamp)
        {
            total += w;
        }

        int roll = (int)(h % (uint)total);
        int i = 0;
        for (; i < StarRamp.Length; i++)
        {
            roll -= StarRamp[i].Weight;
            if (roll < 0)
            {
                break;
            }
        }

        if (i >= StarRamp.Length)
        {
            i = StarRamp.Length - 1;
        }

        int j = i + 1 < StarRamp.Length ? i + 1 : i - 1;
        if (j < 0)
        {
            j = 0;
        }

        float f = ((h >> 8) & 0xFF) / 255f * 0.5f; // up to halfway toward the neighbouring anchor
        return LerpRgb(StarRamp[i].Rgb, StarRamp[j].Rgb, f);
    }

    private static int LerpRgb(int a, int b, float t)
    {
        int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
        int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
        int r = (int)(ar + (br - ar) * t + 0.5f);
        int g = (int)(ag + (bg - ag) * t + 0.5f);
        int bl = (int)(ab + (bb - ab) * t + 0.5f);
        return (r << 16) | (g << 8) | bl;
    }

    // Per-planet flora base hue: green-dominant, with rarer brown / pink / purple / amber exotics.
    private static readonly (int Rgb, int Weight)[] FloraPalette =
    {
        (0x4FA63C, 30), // leaf green
        (0x6FBF4A, 20), // bright green
        (0x3E7D4F, 12), // deep teal-green
        (0x8A7B3A, 12), // olive
        (0x9C6B3A, 10), // brown
        (0xB85C9E, 7),  // pink / magenta (exotic)
        (0x7E4FB0, 6),  // violet / purple (exotic)
        (0xC9A23A, 3),  // amber / yellow (rare)
    };

    /// <summary>A deterministic per-planet flora base hue: a weighted pick from a green-dominant palette (with
    /// rarer brown / pink / purple / amber exotics) plus a small per-channel jitter, so most worlds are leafy
    /// green but some are strikingly alien. One hue for all of a planet's plant life.</summary>
    private static int FloraColor(uint h)
    {
        int total = 0;
        foreach (var (_, w) in FloraPalette)
        {
            total += w;
        }

        int roll = (int)(h % (uint)total);
        int i = 0;
        for (; i < FloraPalette.Length; i++)
        {
            roll -= FloraPalette[i].Weight;
            if (roll < 0)
            {
                break;
            }
        }

        if (i >= FloraPalette.Length)
        {
            i = FloraPalette.Length - 1;
        }

        int anchor = FloraPalette[i].Rgb;
        int r = (anchor >> 16) & 0xFF, g = (anchor >> 8) & 0xFF, b = anchor & 0xFF;
        int Jit(int shift) => (int)((h >> shift) & 0x1F) - 16; // -16..+15
        return (Clamp8b(r + Jit(3)) << 16) | (Clamp8b(g + Jit(8)) << 8) | Clamp8b(b + Jit(13));
    }

    private static int Clamp8b(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
}

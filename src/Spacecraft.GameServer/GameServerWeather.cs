using Spacecraft.Networking.Messages;

namespace Spacecraft.GameServer;

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
    private static readonly int[] SunPalette = { 0xFFF6E8, 0xFFE08A, 0x9FC0FF, 0xFF9E80, 0xFFC070 };
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
    private float _cloudDensity { get => _worlds.Active.CloudDensity; set => _worlds.Active.CloudDensity = value; }
    private bool _breathable { get => _worlds.Active.Breathable; set => _worlds.Active.Breathable = value; }
    private bool _spaceSky { get => _worlds.Active.SpaceSky; set => _worlds.Active.SpaceSky = value; }
    private string _biome { get => _worlds.Active.Biome; set => _worlds.Active.Biome = value; }
    private double _oxygenExtractability { get => _worlds.Active.OxygenExtractability; set => _worlds.Active.OxygenExtractability = value; }
    private System.Random _envRng { get => _worlds.Active.EnvRng; set => _worlds.Active.EnvRng = value; }

    // Public accessors (HUD / tests).
    public float TimeOfDay => (float)_dayFraction;
    public string Weather => _weatherState;
    public int SunColor => _sunColor;

    /// <summary>Whether the current planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool AtmosphereBreathable => _breathable;

    /// <summary>Whether this body shows a space sky (black + stars) on the surface (landable asteroids).</summary>
    public bool SpaceSky => _spaceSky;

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
        _envRng = new System.Random((int)_meta.Seed);
        _dayFraction = 0.35;
        _weatherTimer = 0;
        _sinceEnvBroadcast = 0;

        var (system, _) = ActiveLocationNames();
        _sunColor = SunPalette[(int)((uint)StableStringHash(system) % (uint)SunPalette.Length)];

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

    private WorldEnvironment BuildEnvironment() => new()
    {
        TimeOfDay = (float)_dayFraction,
        DayLengthSeconds = (float)_dayLength,
        Weather = _weatherState,
        Intensity = _weatherIntensity,
        SunColor = _sunColor,
        CloudColor = _cloudColor,
        CloudDensity = _cloudDensity,
        Breathable = _breathable,
        SpaceSky = _spaceSky,
        Biome = _biome,
    };

    private void SendEnvironment(PlayerSession session) => Send(session, BuildEnvironment());

    private void BroadcastEnvironment() => Broadcast(BuildEnvironment());

    private static int StableStringHash(string s)
    {
        int h = 17;
        foreach (char c in s ?? string.Empty)
        {
            h = h * 31 + c;
        }

        return h;
    }
}

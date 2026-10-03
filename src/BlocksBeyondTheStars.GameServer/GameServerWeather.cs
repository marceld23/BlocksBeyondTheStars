// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;
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

    private const double EnvBroadcastInterval = 5.0;

    /// <summary>Intensity move that earns an extra broadcast between heartbeats, so a swelling storm
    /// arrives as a ramp on the client instead of a 5 s staircase.</summary>
    private const float IntensityBroadcastStep = 0.05f;

    /// <summary>Day fraction every world starts at when it's activated (late-morning). Reset on each entry, so a
    /// body's arrival time-of-day is deterministic — day or night then depends purely on the landing longitude.</summary>
    private const double InitialDayFraction = 0.35;

    /// <summary>Reference seconds per "system day" for the orbital clock — fixed (not the local world's rotation
    /// period, which varies per body), so orbital periods mean the same wall-clock time everywhere and bodies
    /// stay in sync across all worlds in the system.</summary>
    private const double SystemDaySeconds = 600.0;

    /// <summary>Monotonic total elapsed system-days, advanced every tick. Server-wide (not per-world) so the
    /// orbital clock is shared by all players/worlds; runtime-only (a restart re-bases the phases, fine for
    /// pure ambience). Sent to clients in <see cref="WorldEnvironment.SystemTimeDays"/>.</summary>
    private double _systemTimeDays;

    private double _dayFraction { get => _worlds.Active.DayFraction; set => _worlds.Active.DayFraction = value; }
    private double _dayLength { get => _worlds.Active.DayLength; set => _worlds.Active.DayLength = value; }
    private double _stormChance { get => _worlds.Active.StormChance; set => _worlds.Active.StormChance = value; }
    private string _planetWeatherMode { get => _worlds.Active.PlanetWeatherMode; set => _worlds.Active.PlanetWeatherMode = value; }
    private WeatherSim _sim => _worlds.Active.Weather;
    private string _weatherState => _worlds.Active.Weather.State;
    private float _weatherIntensity => _worlds.Active.Weather.Intensity;
    private double _sinceEnvBroadcast { get => _worlds.Active.SinceEnvBroadcast; set => _worlds.Active.SinceEnvBroadcast = value; }
    private int _sunColor { get => _worlds.Active.SunColor; set => _worlds.Active.SunColor = value; }
    private int _cloudColor { get => _worlds.Active.CloudColor; set => _worlds.Active.CloudColor = value; }
    private int _skyColor { get => _worlds.Active.SkyColor; set => _worlds.Active.SkyColor = value; }
    private int _floraTint { get => _worlds.Active.FloraTint; set => _worlds.Active.FloraTint = value; }
    private int _waterTint { get => _worlds.Active.WaterTint; set => _worlds.Active.WaterTint = value; }         // #1758
    private int _waterTintMode { get => _worlds.Active.WaterTintMode; set => _worlds.Active.WaterTintMode = value; } // #1758
    private float _cloudDensity { get => _worlds.Active.CloudDensity; set => _worlds.Active.CloudDensity = value; }
    private bool _breathable { get => _worlds.Active.Breathable; set => _worlds.Active.Breathable = value; }
    private bool _spaceSky { get => _worlds.Active.SpaceSky; set => _worlds.Active.SpaceSky = value; }
    private string _biome { get => _worlds.Active.Biome; set => _worlds.Active.Biome = value; }
    private double _oxygenExtractability { get => _worlds.Active.OxygenExtractability; set => _worlds.Active.OxygenExtractability = value; }
    private double _atmosphereHeight { get => _worlds.Active.AtmosphereHeight; set => _worlds.Active.AtmosphereHeight = value; }
    private double _atmosphereDensity { get => _worlds.Active.AtmosphereDensity; set => _worlds.Active.AtmosphereDensity = value; }
    private float _gravityFactor { get => _worlds.Active.GravityFactor; set => _worlds.Active.GravityFactor = value; }

    // Public accessors (HUD / tests).
    public float TimeOfDay => (float)_dayFraction;

    /// <summary>The day fraction the sky shows at this position (#1865). The client draws the sun from
    /// <c>TimeOfDay + X / Circumference</c> (<c>GameBootstrap.LocalTimeOfDay</c>), so everything on the server that
    /// reacts to "night" — sleeping animals, VEGA's lamp tips, NPC routines — must ask the same question or it
    /// contradicts the sky above the player. A void world (a station deck, a ship cabin) has no longitude: its
    /// world clock is the local clock.</summary>
    private double LocalDayFraction(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        if (_world.Planet?.Void == true || _world.Circumference <= 0)
        {
            return _dayFraction;
        }

        double t = (_dayFraction + pos.X / (double)_world.Circumference) % 1.0;
        return t < 0 ? t + 1.0 : t;
    }

    /// <summary>Night at this position: the local sun is below the horizon (the client's sunrise/sunset are 0.25/0.75).</summary>
    private bool IsNightAt(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        double t = LocalDayFraction(pos);
        return t < 0.25 || t > 0.75;
    }

    /// <summary>Dawn or dusk at this position (crepuscular species are awake then).</summary>
    private bool IsDawnOrDuskAt(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        double t = LocalDayFraction(pos);
        return (t >= 0.20 && t <= 0.30) || (t >= 0.70 && t <= 0.80);
    }

    /// <summary>Test seam (#1865): the local day fraction at longitude <paramref name="x"/>.</summary>
    public double LocalDayFractionForTest(float x) => LocalDayFraction(new BlocksBeyondTheStars.Shared.Geometry.Vector3f(x, 0f, 0f));

    /// <summary>Test seam (#1865): pins the WORLD clock so the LOCAL clock at longitude <paramref name="x"/> reads
    /// <paramref name="fraction"/> — tests place things at arbitrary X and must not depend on the world clock.</summary>
    public void SetLocalDayFractionForTest(double fraction, float x) => SetLocalDayFraction(fraction, x);

    /// <summary>Sets the WORLD clock so that the LOCAL clock at longitude <paramref name="x"/> reads
    /// <paramref name="fraction"/> — the inverse of <see cref="LocalDayFraction"/>.</summary>
    private void SetLocalDayFraction(double fraction, float x)
    {
        double shift = _world.Planet?.Void == true || _world.Circumference <= 0 ? 0.0 : x / (double)_world.Circumference;
        _dayFraction = (((fraction - shift) % 1.0) + 1.0) % 1.0;
    }

    /// <summary>The words <c>/settime</c> understands and the local day fraction each one means (#2220). The sky draws
    /// sunrise at 0.25 and sunset at 0.75 (<see cref="IsNightAt"/>): "day" is the late morning every world starts at,
    /// "night" lies past the dusk band (<see cref="IsDawnOrDuskAt"/>) with most of the night still ahead.</summary>
    private static readonly (string Word, double Fraction)[] TimeWords =
    {
        ("midnight", 0.0),
        ("dawn", 0.25),
        ("day", InitialDayFraction),
        ("noon", 0.5),
        ("dusk", 0.75),
        ("night", 0.85),
    };

    /// <summary>Reads a <c>/settime</c> argument (#2220): one of the <see cref="TimeWords"/>, an hour of the day
    /// (1 to 24 — "6.5" is half past six, "24" midnight) or a day fraction below 1 ("0.5" is noon, "0" midnight).
    /// A comma counts as the decimal point, the way a German keyboard types it. Returns the local day fraction and
    /// the name the answer line shows (the word, or the clock time "18:30"); null for anything else.</summary>
    internal static (double Fraction, string Label)? ParseTimeOfDay(string? arg)
    {
        string text = (arg ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var (word, fraction) in TimeWords)
        {
            if (text == word)
            {
                return (fraction, word);
            }
        }

        // The range test is written so that "nan" (which the parser accepts) fails it too.
        if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out double value)
            || !(value >= 0.0 && value <= 24.0))
        {
            return null;
        }

        double local = value < 1.0 ? value : (value / 24.0) % 1.0;
        int minutes = (int)System.Math.Round(local * 24.0 * 60.0) % (24 * 60);
        return (local, $"{minutes / 60:00}:{minutes % 60:00}");
    }

    /// <summary>Admin <c>/settime</c> (#2220): really sets the active world's clock. The command used to write a field
    /// nothing read and still answered "time set". The value is the LOCAL time where the admin stands — the sky is
    /// drawn from the world clock plus the longitude (<see cref="LocalDayFraction"/>), so "night" has to mean night
    /// HERE, not on the far side of the world. Everyone in this world gets the new environment at once instead of
    /// with the next heartbeat. False (and nothing changed) when the argument is no time; <paramref name="label"/>
    /// is what the answer line names.</summary>
    private bool AdminSetTime(PlayerSession session, string? arg, out string label)
    {
        if (ParseTimeOfDay(arg) is not { } time)
        {
            label = string.Empty;
            return false;
        }

        label = time.Label;
        SetLocalDayFraction(time.Fraction, session.State.Position.X);
        _sinceEnvBroadcast = 0;
        BroadcastEnvironment();
        return true;
    }

    public string Weather => _weatherState;
    public int SunColor => _sunColor;

    /// <summary>This world's live wind strength 0..1 — test seam.</summary>
    public float WindSpeedForTest => _sim.WindSpeed;

    /// <summary>This world's current weather intensity 0..1 — test seam.</summary>
    public float WeatherIntensityForTest => _weatherIntensity;

    /// <summary>This world's weather simulation — test seam for the episode/front assertions.</summary>
    public WeatherSim WeatherSimForTest => _sim;

    /// <summary>This world's cloud cover 0..1 (0 = airless/clear) — test seam.</summary>
    public float CloudDensityForTest => _cloudDensity;

    /// <summary>This world's weather mode ("dynamic"/"clear"/"overcast") — test seam.</summary>
    public string WeatherModeForTest => _planetWeatherMode;

    /// <summary>This world's daytime sky/atmosphere base hue (0xRRGGBB) — seeded per world (atmosphere worlds).</summary>
    public int SkyColor => _skyColor;

    /// <summary>The planet's uniform flora base hue (0xRRGGBB) — all plant life is re-tinted to this.</summary>
    public int FloraTint => _floraTint;

    /// <summary>The water colour mode the environment message carries (#1758: 0 classic, 1 tint, 2 rainbow) — test seam.</summary>
    public int WaterTintMode => _waterTintMode;

    /// <summary>Whether the current planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool AtmosphereBreathable => _breathable;

    /// <summary>
    /// Whether this world has AIR AT ALL — breathable or toxic, as opposed to atmosphere "none" (asteroids and
    /// the airless bodies). Distinct from <see cref="AtmosphereBreathable"/>: you still need a suit in a toxic
    /// atmosphere, but a flame has something to burn in it. Gates the torch.
    /// </summary>
    public bool AtmospherePresent
    {
        get
        {
            var planet = _content.GetPlanet(_worlds.Active?.PlanetType ?? string.Empty);
            return !(planet?.IsAirless ?? true) && !_spaceSky;
        }
    }

    /// <summary>Whether this body shows a space sky (black + stars) on the surface (landable asteroids).</summary>
    public bool SpaceSky => _spaceSky;

    /// <summary>This world's seeded gravity multiplier (1.0 = baseline) — test seam.</summary>
    public float GravityFactor => _gravityFactor;

    /// <summary>Absolute Y above which an on-foot player is in space (item 10); 0 = no atmosphere line here.</summary>
    public double AtmosphereHeight => _atmosphereHeight;

    /// <summary>How thick/hazy this world's air is, 0..1 (0 = airless/clear). Drives fog density + fog weather.</summary>
    public double AtmosphereDensity => _atmosphereDensity;

    private void InitWeather()
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        _dayLength = planet?.DayLengthSeconds ?? 600.0;
        _stormChance = planet?.StormChance ?? 0.35;
        // #1473: a player-built station's deck is NOT free air — only its sealed pockets breathe (see
        // GameServerStationAir); the void world keeps "breathable" data for the authored NPC stations.
        _breathable = string.Equals(planet?.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase)
            && !IsPlayerStationWorld(_worlds.Active.LocationId);
        _spaceSky = planet?.SpaceSky ?? false;
        // Clouds + weather + fog require an actual atmosphere — gate on the atmosphere type, NOT just the space
        // sky. A body with atmosphere "none" (lava/crystal historically, asteroids) gets no clouds, no changing
        // weather (no rain/storm) and no fog haze; only worlds with air (breathable/toxic) have weather.
        bool airless = (planet?.IsAirless ?? true) || _spaceSky;
        // #900: two data modes stop meaning "frozen". An AIRLESS body keeps its empty ladder — no clouds,
        // no rain, no fog — but the model may schedule the VACUUM-SAFE events there (ion storms, meteor
        // showers), which is where a space game should feel most alien. An OVERCAST world now sits on a
        // ladder FLOOR of clouds instead of being pinned to them forever, so a swamp can finally break
        // into rain. Only void worlds (ship cabins, station decks) stay genuinely fixed.
        _planetWeatherMode = planet?.Void == true ? "clear" : "dynamic";
        // Base cloud tint comes from the planet type; the final per-world tint is derived below, once the
        // per-world sky hue is known (so clouds can be kept distinct from THIS world's sky).
        _cloudDensity = airless ? 0f : (float)System.Math.Clamp(planet?.CloudDensity ?? 0.45, 0.0, 1.0);
        _biome = string.IsNullOrEmpty(_worlds.Active.PlanetType) ? "rock" : _worlds.Active.PlanetType;
        _oxygenExtractability = System.Math.Clamp(planet?.OxygenExtractability ?? 0.0, 0.0, 1.0);
        _atmosphereHeight = planet?.AtmosphereHeight ?? 0.0;
        // Per-world air thickness (drives fog density + fog-weather chance): airless bodies are clear-vacuum (0);
        // otherwise the planet's explicit value, or a seeded 0.2..0.8 so worlds range from crisp to hazy.
        _atmosphereDensity = AtmosphereDensityFor(planet, airless, _world.LocationId);

        // #2173: a body of an occupied system already carries its AMBIENT weather — the world ADOPTS that sim
        // (the same object: this world ticks it while loaded, the ambient table while not), so the storm seen
        // from orbit is the storm you land in. Otherwise a fresh sim, seeded and configured exactly as before.
        bool adopted = false;
        if (_planetWeatherMode == "dynamic" && planet is not null
            && _bodyWeather.TryGetValue(_world.LocationId, out var ambient)
            && string.Equals(ambient.Planet.Key, planet.Key, System.StringComparison.Ordinal))
        {
            _worlds.Active.Weather = ambient.Sim;
            adopted = true;
        }
        else
        {
            _worlds.Active.Weather = NewWeatherSim(_world.LocationId, planet, airless);
        }

        _worlds.Active.WeatherEventWeights = planet?.WeatherEvents;

        // Mountain tops sit one ladder step wetter than the valley floor (#900). The line is the planet's
        // own relief — base height plus its amplitude — so a flat world effectively has none.
        _worlds.Active.CloudLineY = WeatherProjection.CloudLineY(planet);

        _dayFraction = InitialDayFraction;
        _sinceEnvBroadcast = 0;

        var (system, _) = ActiveLocationNames(); // #1856: a station world resolves to its system too — no more "" star
        _sunColor = StarColor(system);
        // One uniform flora base hue per WORLD (green / brown / pink / purple …). Seeded from
        // LocationId ^ Seed like sky/cloud/gravity (#478) — it was the last per-TYPE hue, contradicting
        // WORLD_GENERATION.md §3. Airless/floraless worlds still carry a value; it just goes unused. The
        // formula lives with the per-species colours (#1716) — one file for every flora colour.
        _floraTint = Shared.World.FloraTints.ForWorld(_meta.Seed, _world.LocationId);
        // #1758: the water colour, the same way — classic blue unless the type opts in (the rainbow planet, the
        // "auto" palette of the water types) AND the save is generation 5: older saves keep their blue.
        // #2027: a toxic world whose water rolled toxic shows it — its "auto" water picks from the poison palette.
        var (waterRgb, waterMode) = Shared.World.FluidTints.ForWorld(_meta.Seed, _world.LocationId, _world.Planet, _meta.Description.TerrainGeneration,
            toxicWater: ActiveTraits.ToxicWater);
        _waterTint = waterRgb;
        _waterTintMode = (int)waterMode;
        // One seeded daytime sky hue per WORLD (blue → green → yellow → red, blue-dominant), and one seeded cloud
        // tint kept distinct from it — moved to Shared (#2170) so the orbit view paints every body's rim and
        // cloud shell in exactly these colours. A type may name its sky (#2063, Toxica-Maxima's light green).
        _skyColor = AtmosphereTints.SkyRgb(_meta.Seed, _world.LocationId, planet);
        _cloudColor = AtmosphereTints.CloudRgb(_meta.Seed, _world.LocationId, planet, _skyColor);
        // One seeded gravity multiplier per WORLD: the body's size class sets the band (asteroids feather-light,
        // moons low, planets full + occasionally heavy) and the seed picks within it, so two same-size worlds
        // still differ. The client scales jump/walk/jetpack/fall from it (a ≥1-block jump is always preserved).
        _gravityFactor = GravityFor(_worlds.Active.SizeClass, unchecked((uint)(StableStringHash(_world.LocationId) ^ (int)_meta.Seed)));

        if (adopted)
        {
            return; // mid-episode already — no restart
        }

        if (_planetWeatherMode == "dynamic")
        {
            _sim.Start(WeatherCtx());
            RegisterLoadedBodyWeather(planet!, airless);
        }
        else
        {
            _sim.Force("clear"); // void worlds: a ship cabin has no sky at all
        }
    }

    /// <summary>A fresh weather sim for a world, seeded and configured exactly as a world load does it. The RNG is
    /// salted with the LOCATION (#900) — seeding it from the save seed alone ran every world in lockstep. The
    /// planet's authored mode becomes a BAND on the ladder rather than a freeze: "overcast" raises the floor to
    /// clouds, "stormy" pins it to the storm (#2063), airless bodies and "clear" types drop the ceiling to clear
    /// (events still run).</summary>
    private WeatherSim NewWeatherSim(string locationId, PlanetType? planet, bool airless)
    {
        var sim = new WeatherSim(unchecked((ulong)(uint)StableStringHash(locationId) << 32 | (uint)_meta.Seed) ^ 0x7EA7BEEFUL)
        {
            SeasonAmplitude = planet?.SeasonAmplitude ?? 0.35,
        };
        if (planet?.WeatherVolatility is { } vol && vol > 0)
        {
            sim.Volatility = System.Math.Clamp(vol, 0.3, 2.5);
        }

        sim.LadderFloor = airless ? 0
            : string.Equals(planet?.Weather, "stormy", System.StringComparison.OrdinalIgnoreCase) ? WeatherCatalog.MaxSeverity
            : string.Equals(planet?.Weather, "overcast", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        sim.LadderCeiling = airless || string.Equals(planet?.Weather, "clear", System.StringComparison.OrdinalIgnoreCase)
            ? 0
            : WeatherCatalog.MaxSeverity;
        return sim;
    }

    /// <summary>Per-world air thickness 0..1: 0 airless, else the type's value or a seeded 0.2..0.8.</summary>
    private double AtmosphereDensityFor(PlanetType? planet, bool airless, string locationId)
        => airless ? 0.0
            : planet?.AtmosphereDensity is { } ad ? System.Math.Clamp(ad, 0.0, 1.0)
            : 0.2 + ((((uint)StableStringHash(locationId) ^ (uint)_meta.Seed) & 0xFFFFu) / 65535.0) * 0.6;

    /// <summary>#2173: a freshly loaded galaxy body enters the ambient table with its world's sim, so its weather
    /// keeps running (and stays visible from orbit) after the last player leaves.</summary>
    private void RegisterLoadedBodyWeather(PlanetType planet, bool airless)
    {
        var body = _galaxy?.FindBody(_world.LocationId);
        if (body is null)
        {
            return;
        }

        _bodyWeather[body.Id] = new BodyWeather
        {
            BodyId = body.Id,
            SystemId = body.SystemId,
            Planet = planet,
            Circumference = _world.Circumference,
            Sim = _sim,
            AtmosphereDensity = _atmosphereDensity,
            Airless = airless,
            Toxic = !airless && !string.Equals(planet.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>The per-tick inputs the weather model needs from this world.</summary>
    private WeatherContext WeatherCtx()
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        return new WeatherContext
        {
            StormChance = _stormChance,
            AtmosphereDensity = _atmosphereDensity,
            DayFraction = _dayFraction,
            SystemTimeDays = _systemTimeDays,
            // "Airless" for the model means no air weather: vacuum bodies AND space-sky surfaces.
            Airless = (planet?.IsAirless ?? true) || _spaceSky,
            Toxic = !_breathable && !((planet?.IsAirless ?? true) || _spaceSky),
            BaseTemperature = planet?.BaseTemperature ?? 15,
            PlanetKey = _worlds.Active.PlanetType ?? string.Empty,
            EventWeights = _worlds.Active.WeatherEventWeights,
            Circumference = _world.Circumference,
            Dynamic = _planetWeatherMode == "dynamic",
        };
    }

    private void TickWeather(double dt)
    {
        // Advance the day clock (wrap 0..1).
        if (_dayLength > 0)
        {
            _dayFraction = (_dayFraction + dt / _dayLength) % 1.0;
        }

        // The model owns the episode schedule, the intensity envelope, the wind and the fronts (#900).
        float before = _weatherIntensity;
        bool changed = _sim.Advance(dt, WeatherCtx());

        _sinceEnvBroadcast += dt;
        // Broadcast on a state change, on a meaningful intensity move (so ramps read as ramps) or on
        // the plain heartbeat — whichever comes first.
        if (changed
            || System.Math.Abs(_weatherIntensity - before) >= IntensityBroadcastStep
            || _sinceEnvBroadcast >= EnvBroadcastInterval)
        {
            _sinceEnvBroadcast = 0;
            BroadcastEnvironment();
        }

        TickWeatherEffects(dt);
    }

    /// <summary>Fallback strength for a state whose episode peak isn't this world's own (a wetter biome
    /// inside a lighter world episode). Ladder states keep their historic ordering.</summary>
    private static float IntensityOf(string weather)
    {
        var def = WeatherCatalog.Find(weather);
        return def is null ? 0f : WeatherCatalog.MidPeak(def);
    }

    /// <summary>Builds the environment for a position — weather is per BIOME (a stormy biome can rain while
    /// a neighbouring clear biome stays sunny), shifted around the world's current weather; the rest
    /// (time, sun, clouds, atmosphere) is world-level. Empty position uses the world's base weather.</summary>
    private WorldEnvironment BuildEnvironment(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos = default)
    {
        var (state, intensity) = BiomeWeatherAt(pos);
        float temperature = CurrentTemperature(state, _dayFraction, pos);
        var def = WeatherCatalog.Find(state);
        return new WorldEnvironment
        {
            TimeOfDay = (float)_dayFraction,
            DayLengthSeconds = (float)_dayLength,
            SystemTimeDays = _systemTimeDays,
            Weather = state,
            Intensity = intensity,
            IntensityRate = _sim.IntensityRate,
            WeatherFamily = (def?.Family ?? WeatherFamily.Calm).ToString().ToLowerInvariant(),
            WindSpeed = _sim.WindSpeed,
            WindDirection = _sim.WindDirection,
            SeasonWetness = (float)_sim.Wetness(_systemTimeDays),
            Temperature = temperature,
            Precipitation = PrecipitationFor(state, temperature),
            SunColor = _sunColor,
            CloudColor = _cloudColor,
            SkyColor = _skyColor,
            FloraTint = _floraTint,
            WaterTint = _waterTint,         // #1758
            WaterTintMode = _waterTintMode, // #1758
            Circumference = _world.Circumference,
            LatitudeLimit = WorldConstants.LatitudeLimitFor(_world.Circumference),
            CloudDensity = _cloudDensity,
            AtmosphereDensity = (float)_atmosphereDensity,
            Breathable = _breathable,
            SpaceSky = _spaceSky,
            Biome = _biome,
            GravityFactor = _gravityFactor,
        };
    }

    /// <summary>One seeded per-world gravity multiplier (1.0 = baseline). A body's size class sets the band —
    /// asteroids feather-light, moons low, planets full and sometimes heavy — and the seed picks within it, so
    /// two same-size worlds still differ. Range spans ~0.35..1.6 across all classes.</summary>
    private static float GravityFor(WorldConstants.WorldSizeClass cls, uint h)
    {
        (float lo, float hi) = cls switch
        {
            WorldConstants.WorldSizeClass.Asteroid => (0.35f, 0.55f),
            WorldConstants.WorldSizeClass.Moon => (0.55f, 0.85f),
            _ => (0.80f, 1.60f),
        };
        // Salt the hash (so gravity isn't correlated with the sky/flora hues seeded from the same id ^ seed).
        float t = ((h ^ 0x9E3779B9u) & 0xFFFFu) / 65535f;
        return lo + t * (hi - lo);
    }

    /// <summary>Sentinel for "no meaningful air temperature" — kept for client compatibility (older FX guard
    /// against it), but since #668 the server no longer sends it: airless surfaces report their physical
    /// temperature and EVA/space reports the sun-dependent vacuum reading.</summary>
    public const float NoAirTemperature = -999f;

    /// <summary>Air temperature (°C): the worldgen-static part (planet base + per-world variation + the
    /// ALTITUDE lapse above sea level, #476 — one shared formula with chunk generation, so the HUD's cold
    /// agrees with where the snow line actually sits) + a weather cooling + a day↔night swing, blended
    /// toward the constant ground temperature with depth underground (#667). Since #666 this value is
    /// survival-relevant: outside the suit's comfort band it drains suit energy, then health
    /// (decision #7 — "temperature stays cosmetic" — was revised by the user on 2026-08-02).</summary>
    private float CurrentTemperature(string weather, double timeOfDay,
        BlocksBeyondTheStars.Shared.Geometry.Vector3f pos = default)
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet?.Void == true)
        {
            return 22f; // a ship / station cabin is climate-controlled
        }

        return TemperatureAt(planet, _sim, weather, timeOfDay, _breathable, pos);
    }

    /// <summary>The temperature formula of <see cref="CurrentTemperature"/> with explicit inputs, so the landing-pad
    /// weather (#2173) can read it for a body that is not the active world (the shared generator must be set to that
    /// body). <paramref name="pos"/> = default reads at the reference altitude.</summary>
    private float TemperatureAt(PlanetType? planet, WeatherSim sim, string weather, double timeOfDay, bool breathable,
        BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        // The static part comes from the SAME per-world calibration worldgen uses (base + variation −
        // lapse·altitude). Empty position (world-level broadcasts) reads at the reference altitude.
        // Airless space-sky bodies (asteroids) report their physical base temperature too (#668) — the
        // old "—" sentinel hid, e.g., an icy asteroid's −95 °C from the suit systems.
        bool hasPos = pos.X != 0f || pos.Y != 0f || pos.Z != 0f;
        double baseT = planet is null
            ? 15.0
            : _generator.AirTemperatureAt(planet, hasPos ? (int)System.Math.Round(pos.Y) : int.MinValue);
        // The state's own offset, scaled by how far the episode has actually swelled — a storm that is
        // still building doesn't yet bite like one at full strength (#900).
        var wdef = WeatherCatalog.Find(weather);
        double envelope = sim.Peak > 0.0001f ? System.Math.Clamp(sim.Intensity / sim.Peak, 0f, 1f) : 1f;
        double weatherDelta = wdef is null ? 2.0 : wdef.TempDelta * (wdef.Key == "clear" ? 1.0 : envelope);
        double swing = breathable ? 6.0 : 16.0; // airless worlds swing hard between day and night
        double dayNight = System.Math.Cos((timeOfDay - 0.5) * 2.0 * System.Math.PI) * swing;
        double t = baseT + weatherDelta + dayNight;

        // Generation 8 (Titas): a hot zone reads its biome's own temperature (+100 °C), with a little of the day swing.
        if (hasPos && planet is { HotZoneShare: > 0.0 }
            && _generator.IsHotZoneAt(planet, (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z)))
        {
            double hot = 100.0;
            foreach (var biome in planet.Biomes)
            {
                if (biome.HotZone && biome.Temperature is { } biomeT)
                {
                    hot = biomeT;
                    break;
                }
            }

            t = hot + dayNight * 0.25;
        }

        // Underground the day/night swing and the weather stop reaching you: blend toward the constant
        // ground temperature over the first blocks of depth (#667). Local heat/cold sources (lava, fire,
        // ice) then override this via the hazard probe, not here.
        if (hasPos && planet is not null)
        {
            double f = _generator.UndergroundFactor(planet,
                (int)System.Math.Floor(pos.X), (int)System.Math.Round(pos.Y), (int)System.Math.Floor(pos.Z));
            if (f > 0.0)
            {
                t += (WorldGeneration.WorldGenerator.GroundComfortC - t) * f;
            }
        }

        return (float)System.Math.Round(t);
    }

    /// <summary>The precipitation form for the current weather + temperature: nothing unless it's actually
    /// raining/storming, then snow/hail when cold, ash (fire-rain) when very hot, else rain. (Sandstorm —
    /// stage 2 — keys off a dry/sand surface.)</summary>
    private string PrecipitationFor(string weather, float temp)
        => WeatherProjection.Precipitation(weather, _content.GetPlanet(_worlds.Active.PlanetType), temp, _sim.Precip);

    /// <summary>The weather in the biome at a position: the world's weather level shifted by a persistent
    /// per-biome offset (some biomes are always wetter/drier). Fixed-weather planets don't vary by biome.</summary>
    private (string State, float Intensity) BiomeWeatherAt(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        // #2174: ONE formula with the orbit view and the maps (Shared.Weather.WeatherProjection). Only the inputs are
        // gathered here — and only when they can matter: events and fixed worlds ignore every positional shift.
        bool dynamic = _planetWeatherMode == "dynamic";
        bool hasPos = pos.X != 0f || pos.Y != 0f || pos.Z != 0f;
        int offset = 0, boost = 0;
        bool summit = false;
        if (dynamic && hasPos && (WeatherCatalog.Find(_weatherState)?.IsLadder ?? true))
        {
            int biomeIdx = _generator.BiomeIndexAt(_world.Planet, (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Z));
            offset = WeatherProjection.BiomeOffset(_meta.Seed, biomeIdx, _systemTimeDays);
            boost = _sim.FrontBoostAt(pos.X, _world.Circumference);
            summit = pos.Y > _worlds.Active.CloudLineY; // summits sit in the cloud/snow while the valley stays clear
        }

        return WeatherProjection.At(EpisodeOf(_sim, dynamic), offset, boost, summit, hasPos);
    }

    /// <summary>Test hook: forces this world's weather (and unlocks dynamic mode so the per-biome shift
    /// applies), for asserting weather-driven behaviour like rain dousing fire (#789).</summary>
    public void SetWeatherForTest(string state)
    {
        _planetWeatherMode = "dynamic";
        _sim.Force(state);
    }

    /// <summary>Test hook: drops a front centred on this longitude so the spatial shift can be asserted.</summary>
    public void AddWeatherFrontForTest(double centerX, double halfWidth, int boost)
        => _sim.Fronts.Add(new WeatherFront
        {
            CenterX = centerX,
            HalfWidth = halfWidth,
            Boost = boost,
            Drift = 0,
            Life = 1e6,
        });

    private void SendEnvironment(PlayerSession session)
    {
        var env = BuildEnvironment(session.State.Position);
        // In vacuum (EVA spacewalk / on foot above the atmosphere) the air reading is meaningless — show
        // the sun-dependent hull temperature instead (#668): scorching on the day side, brutal in shadow.
        if (session.State.InEva || session.State.AboveAtmosphere)
        {
            env.Temperature = VacuumTemperature(_dayFraction);
            env.Precipitation = "none";
        }
        else if (session.MoodUneasy && session.MoodLocationId == _world.LocationId)
        {
            // 2026-09 (Valuma): after a long stay the fog closes in around this player, whatever the sky does.
            env.Weather = "fog";
            env.WeatherFamily = "obscuring";
            env.Intensity = System.Math.Max(env.Intensity, 0.85f);
            env.IntensityRate = 0f;
        }

        Send(session, env);
    }

    /// <summary>Each player in the world gets the weather of THEIR biome (per-player, not one broadcast).</summary>
    private void BroadcastEnvironment()
    {
        foreach (var s in JoinedInActiveWorld())
        {
            SendEnvironment(s);
        }
    }

    private static int StableStringHash(string s) => AtmosphereTints.StableHash(s);

    /// <summary>A deterministic, continuously-varying star colour for a system: the system's hash picks a
    /// weighted anchor on the hot→cool stellar ramp and a second hash blends it toward a neighbour, so colours
    /// span the full ramp (not just a handful of fixed swatches) while clustering on natural sun-like hues.</summary>
    internal static int StarColor(string system)
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

    // The world's flora base hue moved to Shared.World.FloraTints.ForWorld (#1716); the sky hue and the cloud
    // tint moved to Shared.World.AtmosphereTints (#2170).
}

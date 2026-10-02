// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A small, clonable xorshift PRNG. Deliberately NOT <see cref="System.Random"/>: the forecast gadget
/// forks the stream to peek at coming episodes, which needs a value-copyable generator, and a fixed
/// integer algorithm keeps every platform on the same sequence.
/// </summary>
public struct WeatherRng
{
    private ulong _s;

    /// <summary>Seeds the generator (0 is remapped — xorshift cannot leave that state).</summary>
    public WeatherRng(ulong seed) => _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>Next raw 64-bit value (xorshift64*).</summary>
    public ulong NextUlong()
    {
        _s ^= _s >> 12;
        _s ^= _s << 25;
        _s ^= _s >> 27;
        return unchecked(_s * 0x2545F4914F6CDD1DUL);
    }

    /// <summary>Uniform double in [0,1).</summary>
    public double NextDouble() => (NextUlong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform value in [lo,hi].</summary>
    public double Range(double lo, double hi) => lo + (hi - lo) * NextDouble();

    /// <summary>Uniform float in [lo,hi].</summary>
    public float RangeF(float lo, float hi) => (float)(lo + (hi - lo) * NextDouble());
}

/// <summary>Per-tick inputs the scheduler needs from the world it belongs to.</summary>
public sealed class WeatherContext
{
    /// <summary>The planet type's storm bias (0..1) — how eagerly the ladder climbs.</summary>
    public double StormChance { get; set; } = 0.35;

    /// <summary>Air thickness 0..1; 0 = airless. Gates fog and scales its likelihood.</summary>
    public double AtmosphereDensity { get; set; }

    /// <summary>Current day fraction 0..1 — drives the afternoon-convection and dawn-fog biases.</summary>
    public double DayFraction { get; set; }

    /// <summary>Monotonic shared clock, in system-days — drives the slow seasonal swing.</summary>
    public double SystemTimeDays { get; set; }

    /// <summary>No atmosphere at all: the ladder is pinned to clear and only vacuum-safe events run.</summary>
    public bool Airless { get; set; }

    /// <summary>Atmosphere present but not breathable — the precondition for acid rain.</summary>
    public bool Toxic { get; set; }

    /// <summary>The world's calibrated surface temperature in °C, before weather — gates blizzards,
    /// heatwaves and ember fall.</summary>
    public double BaseTemperature { get; set; } = 15;

    /// <summary>Planet type key ("jungle", "lava", …) for the type-flavoured events.</summary>
    public string PlanetKey { get; set; } = string.Empty;

    /// <summary>Optional per-planet event-weight overrides from <c>planets.json</c>.</summary>
    public IReadOnlyDictionary<string, double>? EventWeights { get; set; }

    /// <summary>East–west wrap of this world, in blocks — the axis the fronts travel along.</summary>
    public int Circumference { get; set; } = 6000;

    /// <summary>Whether this world's weather changes at all ("dynamic"); fixed worlds never re-roll.</summary>
    public bool Dynamic { get; set; } = true;
}

/// <summary>A weather cell drifting along the world's east–west wrap, boosting the ladder where it passes.</summary>
public sealed class WeatherFront
{
    /// <summary>Longitude (world X) of the front's centre.</summary>
    public double CenterX { get; set; }

    /// <summary>Half-width in blocks; the boost applies inside this radius, feathered at the rim.</summary>
    public double HalfWidth { get; set; }

    /// <summary>Blocks per second along X (signed).</summary>
    public double Drift { get; set; }

    /// <summary>How many ladder steps this front adds where it covers (1 or 2).</summary>
    public int Boost { get; set; } = 1;

    /// <summary>Remaining lifetime in seconds.</summary>
    public double Life { get; set; }
}

/// <summary>
/// One world's live weather: the current episode (state, rolled peak + precipitation form, duration),
/// the smoothed intensity envelope, wind, the seasonal phase and the moving fronts.
/// </summary>
public sealed class WeatherSim
{
    /// <summary>How long the attack/decay ramps last, as a fraction of the episode.</summary>
    private const double AttackFraction = 0.18;
    private const double DecayFraction = 0.25;
    private const double AttackMaxSeconds = 9.0;
    private const double DecayMaxSeconds = 14.0;

    /// <summary>Chance an episode boundary picks an event instead of walking the ladder.</summary>
    private const double BaseEventChance = 0.24;

    /// <summary>Chance the ladder stays where it is (a rain that just keeps going).</summary>
    private const double PersistChance = 0.22;

    /// <summary>Chance the ladder jumps two steps at once — a squall out of a near-clear sky.</summary>
    private const double SquallChance = 0.06;

    /// <summary>Fronts a world may carry at once.</summary>
    private const int MaxFronts = 2;

    private WeatherRng _rng;

    /// <summary>Current state key (ladder or event).</summary>
    public string State { get; set; } = "clear";

    /// <summary>Precipitation form rolled for this episode ("none" when nothing falls).</summary>
    public string Precip { get; set; } = "none";

    /// <summary>Where the ladder stands, remembered across events so an event doesn't reset the sky.</summary>
    public int LadderSeverity { get; set; }

    /// <summary>This episode's rolled peak intensity.</summary>
    public float Peak { get; set; }

    /// <summary>Current envelope-shaped intensity (0..1).</summary>
    public float Intensity { get; set; }

    /// <summary>Current rate of change of <see cref="Intensity"/> per second — sent to the client so it
    /// can extrapolate between the 5 s environment broadcasts instead of stepping.</summary>
    public float IntensityRate { get; set; }

    /// <summary>Episode length in seconds.</summary>
    public double Duration { get; set; } = 60;

    /// <summary>Seconds elapsed in this episode.</summary>
    public double Elapsed { get; set; }

    /// <summary>Smoothed wind strength 0..1.</summary>
    public float WindSpeed { get; set; }

    /// <summary>Wind direction in radians, drifting slowly.</summary>
    public float WindDirection { get; set; }

    /// <summary>Seasonal phase offset 0..1, seeded per world.</summary>
    public double SeasonPhase { get; set; }

    /// <summary>Length of this world's wet/dry cycle in system-days.</summary>
    public double SeasonPeriodDays { get; set; } = 20;

    /// <summary>How strongly the season swings the wetness (0 = no seasons).</summary>
    public double SeasonAmplitude { get; set; } = 0.35;

    /// <summary>Per-world pacing: &gt;1 = shorter, more frequent episodes.</summary>
    public double Volatility { get; set; } = 1.0;

    /// <summary>Lowest ladder severity this world's sky ever reaches. An "overcast" planet sits at 1, so it
    /// is never truly clear but can still build into rain, storm and events — before #900 it was frozen on
    /// "clouds" for good.</summary>
    public int LadderFloor { get; set; }

    /// <summary>Highest ladder severity this world reaches. Airless bodies are pinned at 0: no clouds, no
    /// rain, no fog — only the vacuum-safe events run there.</summary>
    public int LadderCeiling { get; set; } = WeatherCatalog.MaxSeverity;

    /// <summary>Live fronts drifting across this world.</summary>
    public List<WeatherFront> Fronts { get; } = new();

    /// <summary>Builds a sim for a world. The seed MUST already be salted per world — sharing one
    /// stream across worlds was the original "every planet has the same weather" bug.</summary>
    public WeatherSim(ulong seed)
    {
        _rng = new WeatherRng(seed);
        SeasonPhase = _rng.NextDouble();
        SeasonPeriodDays = _rng.Range(12.0, 40.0);
        Volatility = _rng.Range(0.55, 1.7);
        WindDirection = _rng.RangeF(0f, 6.2831855f);
    }

    /// <summary>Copy constructor for the forecast peek — clones the RNG stream by value so running the
    /// copy forward cannot disturb the live world.</summary>
    private WeatherSim(WeatherSim other)
    {
        _rng = other._rng;
        State = other.State;
        Precip = other.Precip;
        LadderSeverity = other.LadderSeverity;
        Peak = other.Peak;
        Intensity = other.Intensity;
        IntensityRate = other.IntensityRate;
        Duration = other.Duration;
        Elapsed = other.Elapsed;
        WindSpeed = other.WindSpeed;
        WindDirection = other.WindDirection;
        SeasonPhase = other.SeasonPhase;
        SeasonPeriodDays = other.SeasonPeriodDays;
        SeasonAmplitude = other.SeasonAmplitude;
        Volatility = other.Volatility;
        _windTarget = other._windTarget;
        foreach (var f in other.Fronts)
        {
            Fronts.Add(new WeatherFront
            {
                CenterX = f.CenterX,
                HalfWidth = f.HalfWidth,
                Drift = f.Drift,
                Boost = f.Boost,
                Life = f.Life,
            });
        }
    }

    /// <summary>The state definition currently in force.</summary>
    public WeatherDef Def => WeatherCatalog.Find(State) ?? WeatherCatalog.Ladder[0];

    /// <summary>How far this world's season currently leans wet (0 = dry season, 1 = wet season).</summary>
    public double Wetness(double systemDays)
    {
        if (SeasonAmplitude <= 0.0001)
        {
            return 0.5;
        }

        double phase = (systemDays / SeasonPeriodDays) + SeasonPhase;
        double s = Math.Sin(phase * 2.0 * Math.PI);
        return Math.Clamp(0.5 + s * SeasonAmplitude, 0.0, 1.0);
    }

    /// <summary>Starts the world on a plausible first episode (called once after construction).</summary>
    public void Start(WeatherContext ctx)
    {
        LadderSeverity = ClampSeverity(0);
        State = WeatherCatalog.Ladder[LadderSeverity].Key;
        BeginEpisode(ctx, State);
    }

    /// <summary>Advances the episode envelope, the wind and the fronts; rolls the next episode at the
    /// boundary. Returns true when the reported state changed (the caller broadcasts then).</summary>
    public bool Advance(double dt, WeatherContext ctx)
    {
        if (dt <= 0)
        {
            return false;
        }

        string before = State;
        AdvanceFronts(dt, ctx);

        if (ctx.Dynamic)
        {
            Elapsed += dt;
            if (Elapsed >= Duration)
            {
                BeginEpisode(ctx, PickNext(ctx));
            }
        }

        float prev = Intensity;
        Intensity = ctx.Dynamic ? Peak * Envelope() : Peak;
        IntensityRate = (float)((Intensity - prev) / dt);
        AdvanceWind(dt);
        return !string.Equals(before, State, StringComparison.Ordinal);
    }

    /// <summary>Attack → plateau → decay, so an episode swells and fades instead of snapping on.</summary>
    private float Envelope()
    {
        double attack = Math.Min(Duration * AttackFraction, AttackMaxSeconds);
        double decay = Math.Min(Duration * DecayFraction, DecayMaxSeconds);
        double t = Math.Clamp(Elapsed, 0, Duration);
        double rise = attack <= 0 ? 1 : SmoothStep(t / attack);
        double fall = decay <= 0 ? 1 : SmoothStep((Duration - t) / decay);
        return (float)Math.Clamp(rise * fall, 0, 1);
    }

    private static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private void BeginEpisode(WeatherContext ctx, string key)
    {
        var def = WeatherCatalog.Find(key) ?? WeatherCatalog.Ladder[0];
        State = def.Key;
        if (def.IsLadder)
        {
            LadderSeverity = def.Severity;
        }

        Elapsed = 0;
        Duration = Math.Clamp(_rng.Range(def.DurLo, def.DurHi) / Math.Max(0.35, Volatility), 12, 260);

        // A wet season pushes peaks up, a dry one damps them — on top of the state's own band.
        double wet = Wetness(ctx.SystemTimeDays);
        float peak = _rng.RangeF(def.PeakLo, def.PeakHi);
        if (def.Family is WeatherFamily.Wet or WeatherFamily.Violent)
        {
            peak *= (float)(0.82 + 0.36 * wet);
        }

        Peak = Math.Clamp(peak, 0f, 1f);
        Precip = RollPrecip(def, ctx);
        _windTarget = _rng.RangeF(def.WindLo, def.WindHi);

        // Fronts are born at episode boundaries on worlds that can actually have moving weather.
        if (ctx.Dynamic && !ctx.Airless && Fronts.Count < MaxFronts && _rng.NextDouble() < 0.35)
        {
            SpawnFront(ctx);
        }
    }

    /// <summary>Picks this episode's precipitation form from the state's candidates. The temperature
    /// refinement (snow line, hail, sandstorm) stays with the caller, which knows the position.</summary>
    private string RollPrecip(WeatherDef def, WeatherContext ctx)
    {
        if (def.Precip.Length == 0)
        {
            return "none";
        }

        // Rain-family states can also come down as a lighter form, so not every rain is a downpour.
        if (def.Key is "rain" or "storm" && ctx.BaseTemperature > 4 && _rng.NextDouble() < 0.28)
        {
            return "drizzle";
        }

        return def.Precip[(int)(_rng.NextDouble() * def.Precip.Length) % def.Precip.Length];
    }

    private string PickNext(WeatherContext ctx)
    {
        // 1) Events first — they suspend the ladder rather than replacing it.
        if (_rng.NextDouble() < BaseEventChance * Math.Clamp(Volatility, 0.6, 1.6))
        {
            string? ev = PickEvent(ctx);
            if (ev is not null)
            {
                return ev;
            }
        }

        // 2) Airless bodies have no rain ramp at all (ceiling 0) — between events their sky is simply clear.
        // 3) Walk the ladder. Climbing is biased by the planet's storm chance, the season and the
        //    time of day (convection peaks in the afternoon).
        double wet = Wetness(ctx.SystemTimeDays);
        double up = Math.Clamp(ctx.StormChance * (0.7 + 0.6 * wet) * Convection(ctx.DayFraction), 0.02, 0.95);
        double roll = _rng.NextDouble();
        if (roll < PersistChance)
        {
            return WeatherCatalog.Ladder[ClampSeverity(LadderSeverity)].Key;
        }

        int sev = LadderSeverity;
        if (_rng.NextDouble() < up)
        {
            sev += _rng.NextDouble() < SquallChance ? 2 : 1;
        }
        else
        {
            sev -= 1;
        }

        return WeatherCatalog.Ladder[ClampSeverity(sev)].Key;
    }

    /// <summary>Clamps a ladder severity into this world's own band — an overcast world never reaches
    /// clear, an airless one never leaves it.</summary>
    public int ClampSeverity(int severity)
        => Math.Clamp(severity, Math.Clamp(LadderFloor, 0, WeatherCatalog.MaxSeverity),
            Math.Clamp(LadderCeiling, 0, WeatherCatalog.MaxSeverity));

    /// <summary>Afternoon convection: storms build through the day and settle at night.</summary>
    private static double Convection(double dayFraction)
        => 0.75 + 0.55 * Math.Max(0.0, Math.Cos((dayFraction - 0.62) * 2.0 * Math.PI));

    /// <summary>Weighted pick among the events this world currently allows; null when none qualify.</summary>
    private string? PickEvent(WeatherContext ctx)
    {
        Span<double> weights = stackalloc double[WeatherCatalog.Events.Length];
        double total = 0;
        for (int i = 0; i < WeatherCatalog.Events.Length; i++)
        {
            double w = EventWeight(WeatherCatalog.Events[i], ctx);
            weights[i] = w;
            total += w;
        }

        if (total <= 0)
        {
            return null;
        }

        double roll = _rng.NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return WeatherCatalog.Events[i].Key;
            }
        }

        return null;
    }

    /// <summary>How likely each event is on this world right now: hard gates first (atmosphere,
    /// temperature, planet type), then the time-of-day and per-planet weighting.</summary>
    private static double EventWeight(WeatherDef def, WeatherContext ctx)
    {
        if (def.NeedsAir && ctx.Airless)
        {
            return 0;
        }

        double tod = ctx.DayFraction;
        bool night = tod < 0.22 || tod > 0.80;
        double w = def.Key switch
        {
            // Fog needs thick air and loves the hours around dawn.
            "fog" => ctx.AtmosphereDensity < 0.15 ? 0 : 1.0 * ctx.AtmosphereDensity * (IsDawn(tod) ? 3.0 : 1.0),
            "ground_fog" => ctx.AtmosphereDensity < 0.10 ? 0 : 1.2 * ctx.AtmosphereDensity * (IsDawn(tod) ? 3.5 : 0.8),
            "drizzle" => 1.1,
            "gale" => 1.0,
            "blizzard" => ctx.BaseTemperature <= 1 ? 1.6 : 0,
            "heatwave" => ctx.BaseTemperature >= 28 ? 1.3 : 0,
            "acid_rain" => ctx.Toxic ? 1.5 : 0,
            "toxic_storm" => ctx.Toxic ? 0.6 : 0, // #2064: rarer than acid rain, on the same worlds; a type raises it in data
            "ion_storm" => (ctx.Airless ? 2.2 : 0.7) * (night ? 1.5 : 1.0),
            // Thin or absent air doesn't burn the debris up, so airless bodies see far more of it.
            "meteor_shower" => (ctx.Airless ? 2.6 : 0.5) * (night ? 1.6 : 0.7),
            "ember_fall" => ctx.BaseTemperature >= 45 || ctx.PlanetKey is "lava" or "ashen" ? 1.7 : 0,
            "spore_bloom" => ctx.PlanetKey is "jungle" or "swamp" or "fungal" ? 1.4 * (night ? 1.5 : 1.0) : 0,
            _ => 0.5,
        };

        if (w > 0 && ctx.EventWeights is not null && ctx.EventWeights.TryGetValue(def.Key, out double over))
        {
            w *= Math.Max(0, over);
        }

        return w;
    }

    private static bool IsDawn(double dayFraction) => dayFraction is >= 0.17 and <= 0.32;

    private float _windTarget;

    private void AdvanceWind(double dt)
    {
        float k = (float)Math.Clamp(dt * 0.35, 0, 1);
        WindSpeed += (_windTarget - WindSpeed) * k;
        // The direction wanders, faster when the air is already moving.
        WindDirection += (float)(dt * (0.02 + 0.10 * WindSpeed) * (_rng.NextDouble() * 2 - 1));
        if (WindDirection > 6.2831855f)
        {
            WindDirection -= 6.2831855f;
        }
        else if (WindDirection < 0f)
        {
            WindDirection += 6.2831855f;
        }
    }

    private void SpawnFront(WeatherContext ctx)
    {
        Fronts.Add(new WeatherFront
        {
            CenterX = _rng.Range(0, Math.Max(1, ctx.Circumference)),
            HalfWidth = _rng.Range(70, 260),
            Drift = _rng.Range(4, 15) * (_rng.NextDouble() < 0.5 ? -1 : 1),
            Boost = _rng.NextDouble() < 0.25 ? 2 : 1,
            Life = _rng.Range(150, 420),
        });
    }

    private void AdvanceFronts(double dt, WeatherContext ctx)
    {
        if (Fronts.Count == 0)
        {
            return;
        }

        double circ = Math.Max(1, ctx.Circumference);
        for (int i = Fronts.Count - 1; i >= 0; i--)
        {
            var f = Fronts[i];
            f.Life -= dt;
            if (f.Life <= 0)
            {
                Fronts.RemoveAt(i);
                continue;
            }

            f.CenterX += f.Drift * dt;
            // Worlds wrap east–west, so a front leaving one edge comes back round the other side.
            f.CenterX -= Math.Floor(f.CenterX / circ) * circ;
        }
    }

    /// <summary>Extra ladder steps from any front covering this longitude (0 when none does).</summary>
    public int FrontBoostAt(double x, int circumference)
    {
        int best = 0;
        double circ = Math.Max(1, circumference);
        foreach (var f in Fronts)
        {
            double d = Math.Abs(x - f.CenterX);
            d = Math.Min(d, circ - d); // shortest way round the wrap
            if (d <= f.HalfWidth)
            {
                best = Math.Max(best, f.Boost);
            }
        }

        return best;
    }

    /// <summary>Distance in blocks to the nearest front edge, and its boost — feeds the forecast gadget.
    /// Returns −1 when the world carries no fronts.</summary>
    public (double Distance, int Boost) NearestFront(double x, int circumference)
    {
        double circ = Math.Max(1, circumference);
        double best = -1;
        int boost = 0;
        foreach (var f in Fronts)
        {
            double d = Math.Abs(x - f.CenterX);
            d = Math.Min(d, circ - d);
            d = Math.Max(0, d - f.HalfWidth);
            if (best < 0 || d < best)
            {
                best = d;
                boost = f.Boost;
            }
        }

        return (best, boost);
    }

    /// <summary>Peeks at the coming episodes by forking the RNG stream onto a copy and running it
    /// forward — the live world is untouched. Used by the forecast gadget (#900).</summary>
    public List<(string State, double StartsInSeconds, double Duration)> Forecast(int count, WeatherContext ctx)
    {
        var result = new List<(string, double, double)>(count);
        var copy = new WeatherSim(this);
        double clock = 0;
        var local = new WeatherContext
        {
            StormChance = ctx.StormChance,
            AtmosphereDensity = ctx.AtmosphereDensity,
            DayFraction = ctx.DayFraction,
            SystemTimeDays = ctx.SystemTimeDays,
            Airless = ctx.Airless,
            Toxic = ctx.Toxic,
            BaseTemperature = ctx.BaseTemperature,
            PlanetKey = ctx.PlanetKey,
            EventWeights = ctx.EventWeights,
            Circumference = ctx.Circumference,
            Dynamic = ctx.Dynamic,
        };

        for (int i = 0; i < count && ctx.Dynamic; i++)
        {
            double remaining = Math.Max(0.5, copy.Duration - copy.Elapsed);
            clock += remaining;
            // Step the day clock along with the peek so the diurnal bias applies to the prediction too.
            local.DayFraction = (ctx.DayFraction + clock / 600.0) % 1.0;
            local.SystemTimeDays = ctx.SystemTimeDays + clock / 600.0;
            copy.Elapsed = copy.Duration;
            copy.Advance(0.016, local);
            result.Add((copy.State, clock, copy.Duration));
        }

        return result;
    }

    /// <summary>Test/admin seam: forces a state, keeping the ladder position consistent.</summary>
    public void Force(string key)
    {
        var def = WeatherCatalog.Find(key) ?? WeatherCatalog.Ladder[0];
        State = def.Key;
        if (def.IsLadder)
        {
            LadderSeverity = def.Severity;
        }

        // A forced state (admin /setweather, tests) arrives at FULL strength — "give me a storm" should
        // mean the real thing, not an average one — and it starts mid-episode, on the plateau. Starting at
        // elapsed 0 would drop it straight into the attack ramp, i.e. to intensity ~0 on the very next tick.
        Peak = Math.Max(def.PeakHi, 0.01f);
        Intensity = Peak;
        Duration = Math.Max(90, def.DurHi);
        Elapsed = Duration * 0.5;
        Precip = def.Precip.Length > 0 ? def.Precip[0] : "none";
        _windTarget = (def.WindLo + def.WindHi) * 0.5f;
        WindSpeed = _windTarget;
    }
}

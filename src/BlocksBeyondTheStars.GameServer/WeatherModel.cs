// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The pure weather model (#900): the state table, the episode scheduler and the moving fronts,
/// with no <see cref="GameServer"/> dependency so it can be unit-tested at full speed.
/// <para>
/// Two layers, deliberately separate. The <b>ladder</b> — clear → clouds → rain → storm — carries an
/// explicit <see cref="WeatherDef.Severity"/> and is the ONLY thing the per-biome offset, the fronts
/// and altitude shift. <b>Events</b> (fog, gale, blizzard, ion storm, …) carry severity −1, override
/// the reported state for their episode, and never take part in that arithmetic. The old code walked
/// an index into a <c>string[]</c>, which meant adding a state silently rebalanced every biome.
/// </para>
/// </summary>
public enum WeatherFamily
{
    /// <summary>Clear skies — nothing falling, nothing obscuring.</summary>
    Calm,

    /// <summary>Cloud cover without precipitation.</summary>
    Cloudy,

    /// <summary>Something wet is falling (drizzle, rain).</summary>
    Wet,

    /// <summary>Dangerous, loud, high-intensity weather (storm, blizzard).</summary>
    Violent,

    /// <summary>Visibility killers (fog, ground fog).</summary>
    Obscuring,

    /// <summary>Wind-dominated (gale) — no precipitation, but everything is moving.</summary>
    Windy,

    /// <summary>Not-of-this-Earth weather (acid rain, ion storm, meteor shower, spores, embers).</summary>
    Exotic,
}

/// <summary>One weather state: how strong it gets, how long it lasts, what falls out of it.</summary>
public sealed class WeatherDef
{
    /// <summary>Wire value sent to clients in <c>WorldEnvironment.Weather</c>.</summary>
    public string Key { get; init; } = "clear";

    /// <summary>Position on the ladder (0..3), or −1 for an event that sits outside the ladder.</summary>
    public int Severity { get; init; } = -1;

    /// <summary>Coarse grouping the client switches on for washes, audio and the HUD icon.</summary>
    public WeatherFamily Family { get; init; } = WeatherFamily.Calm;

    /// <summary>Peak-intensity band. Each episode rolls its own peak, so no two storms are alike.</summary>
    public float PeakLo { get; init; }

    /// <summary>Upper bound of the peak-intensity band.</summary>
    public float PeakHi { get; init; }

    /// <summary>Episode-duration band in seconds (before the world's volatility scales it).</summary>
    public double DurLo { get; init; } = 40;

    /// <summary>Upper bound of the episode-duration band, in seconds.</summary>
    public double DurHi { get; init; } = 110;

    /// <summary>Air-temperature offset in °C while this state is at full strength.</summary>
    public float TempDelta { get; init; }

    /// <summary>Wind band this state drives (0..1).</summary>
    public float WindLo { get; init; }

    /// <summary>Upper bound of the wind band.</summary>
    public float WindHi { get; init; }

    /// <summary>Needs a real atmosphere — never scheduled on airless bodies.</summary>
    public bool NeedsAir { get; init; } = true;

    /// <summary>Precipitation forms this state can produce; empty = nothing falls.</summary>
    public string[] Precip { get; init; } = Array.Empty<string>();

    /// <summary>True for the four ladder states (severity ≥ 0).</summary>
    public bool IsLadder => Severity >= 0;
}

/// <summary>The weather state table. Ladder states are indexed by severity; events are picked by weight.</summary>
public static class WeatherCatalog
{
    /// <summary>Wettest ladder severity (storm).</summary>
    public const int MaxSeverity = 3;

    /// <summary>The rain ramp, indexed by severity — the only states the biome/front/altitude shifts touch.</summary>
    public static readonly WeatherDef[] Ladder =
    {
        new()
        {
            Key = "clear", Severity = 0, Family = WeatherFamily.Calm,
            PeakLo = 0f, PeakHi = 0f, DurLo = 45, DurHi = 150, TempDelta = 2f,
            WindLo = 0.05f, WindHi = 0.25f,
        },
        new()
        {
            Key = "clouds", Severity = 1, Family = WeatherFamily.Cloudy,
            PeakLo = 0.20f, PeakHi = 0.45f, DurLo = 40, DurHi = 140, TempDelta = -2f,
            WindLo = 0.15f, WindHi = 0.40f,
        },
        new()
        {
            Key = "rain", Severity = 2, Family = WeatherFamily.Wet,
            PeakLo = 0.40f, PeakHi = 0.80f, DurLo = 35, DurHi = 110, TempDelta = -5f,
            WindLo = 0.25f, WindHi = 0.55f, Precip = new[] { "rain" },
        },
        new()
        {
            Key = "storm", Severity = 3, Family = WeatherFamily.Violent,
            PeakLo = 0.75f, PeakHi = 1.00f, DurLo = 25, DurHi = 75, TempDelta = -8f,
            WindLo = 0.55f, WindHi = 0.95f, Precip = new[] { "rain" },
        },
    };

    /// <summary>Off-ladder episodes. Availability is decided by <see cref="WeatherSim"/> from the world's
    /// atmosphere, temperature and planet type; the weights come from <c>planets.json</c> or the defaults.</summary>
    public static readonly WeatherDef[] Events =
    {
        new()
        {
            Key = "drizzle", Family = WeatherFamily.Wet,
            PeakLo = 0.15f, PeakHi = 0.35f, DurLo = 30, DurHi = 95, TempDelta = -3f,
            WindLo = 0.10f, WindHi = 0.30f, Precip = new[] { "drizzle" },
        },
        new()
        {
            Key = "fog", Family = WeatherFamily.Obscuring,
            PeakLo = 0.35f, PeakHi = 0.75f, DurLo = 40, DurHi = 130, TempDelta = -3f,
            WindLo = 0.02f, WindHi = 0.12f,
        },
        new()
        {
            Key = "ground_fog", Family = WeatherFamily.Obscuring,
            PeakLo = 0.25f, PeakHi = 0.55f, DurLo = 45, DurHi = 120, TempDelta = -2f,
            WindLo = 0.02f, WindHi = 0.10f,
        },
        new()
        {
            Key = "gale", Family = WeatherFamily.Windy,
            PeakLo = 0.45f, PeakHi = 0.90f, DurLo = 30, DurHi = 95, TempDelta = -4f,
            WindLo = 0.75f, WindHi = 1.00f, Precip = new[] { "dust" },
        },
        new()
        {
            Key = "blizzard", Family = WeatherFamily.Violent,
            PeakLo = 0.70f, PeakHi = 1.00f, DurLo = 30, DurHi = 85, TempDelta = -12f,
            WindLo = 0.70f, WindHi = 1.00f, Precip = new[] { "snow" },
        },
        new()
        {
            Key = "heatwave", Family = WeatherFamily.Exotic,
            PeakLo = 0.40f, PeakHi = 0.85f, DurLo = 70, DurHi = 190, TempDelta = 12f,
            WindLo = 0.02f, WindHi = 0.15f,
        },
        new()
        {
            Key = "acid_rain", Family = WeatherFamily.Exotic,
            PeakLo = 0.50f, PeakHi = 0.95f, DurLo = 30, DurHi = 95, TempDelta = -4f,
            WindLo = 0.25f, WindHi = 0.60f, Precip = new[] { "acid" },
        },
        new()
        {
            Key = "ion_storm", Family = WeatherFamily.Exotic,
            PeakLo = 0.60f, PeakHi = 1.00f, DurLo = 25, DurHi = 80, TempDelta = 0f,
            WindLo = 0.20f, WindHi = 0.70f, NeedsAir = false,
        },
        new()
        {
            Key = "meteor_shower", Family = WeatherFamily.Exotic,
            PeakLo = 0.40f, PeakHi = 0.90f, DurLo = 30, DurHi = 95, TempDelta = 0f,
            WindLo = 0.05f, WindHi = 0.30f, NeedsAir = false, Precip = new[] { "meteor" },
        },
        new()
        {
            Key = "ember_fall", Family = WeatherFamily.Exotic,
            PeakLo = 0.50f, PeakHi = 0.95f, DurLo = 35, DurHi = 110, TempDelta = 8f,
            WindLo = 0.15f, WindHi = 0.45f, Precip = new[] { "ash" },
        },
        new()
        {
            Key = "spore_bloom", Family = WeatherFamily.Exotic,
            PeakLo = 0.30f, PeakHi = 0.65f, DurLo = 50, DurHi = 150, TempDelta = 1f,
            WindLo = 0.05f, WindHi = 0.25f, Precip = new[] { "spores" },
        },
    };

    private static readonly Dictionary<string, WeatherDef> ByKey = BuildIndex();

    private static Dictionary<string, WeatherDef> BuildIndex()
    {
        var map = new Dictionary<string, WeatherDef>(StringComparer.Ordinal);
        foreach (var d in Ladder)
        {
            map[d.Key] = d;
        }

        foreach (var d in Events)
        {
            map[d.Key] = d;
        }

        return map;
    }

    /// <summary>Looks a state up by its wire key; null for an unknown key.</summary>
    public static WeatherDef? Find(string? key)
        => key is not null && ByKey.TryGetValue(key, out var d) ? d : null;

    /// <summary>Every valid state key (ladder + events) — used by the protocol tests.</summary>
    public static IEnumerable<string> AllKeys => ByKey.Keys;

    /// <summary>The mid-band peak intensity for a state — the fallback strength when a position's
    /// severity differs from the world episode's own (a wetter biome inside a rain episode).</summary>
    public static float MidPeak(WeatherDef def) => (def.PeakLo + def.PeakHi) * 0.5f;
}

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

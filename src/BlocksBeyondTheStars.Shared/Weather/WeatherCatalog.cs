// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Weather;

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
        // #2064: the toxic storm — acid rain's violent sibling on worlds whose air is not breathable: a hard wind, lightning
        // (the client draws it for acid precipitation in a violent state) and rain that burns. Appended last.
        new()
        {
            Key = "toxic_storm", Family = WeatherFamily.Violent,
            PeakLo = 0.70f, PeakHi = 1.00f, DurLo = 25, DurHi = 80, TempDelta = -6f,
            WindLo = 0.75f, WindHi = 1.00f, Precip = new[] { "acid" },
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

    /// <summary>Words a player types for a state that are not its wire key (#2220): the admin command advertised
    /// "cloudy" while the key is "clouds". Only <see cref="FindByName"/> reads them — the wire, the planet data and
    /// <see cref="AllKeys"/> keep the exact keys.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["cloudy"] = "clouds",
    };

    /// <summary>Looks a state up by its wire key; null for an unknown key.</summary>
    public static WeatherDef? Find(string? key)
        => key is not null && ByKey.TryGetValue(key, out var d) ? d : null;

    /// <summary>Looks a state up the way a player types it (<c>/setweather</c>, #2220): the exact key, the key in
    /// another letter case or with blanks around it, or an alias ("cloudy"). Null for an unknown name.</summary>
    public static WeatherDef? FindByName(string? name)
    {
        string key = (name ?? string.Empty).Trim().ToLowerInvariant();
        return Find(Aliases.TryGetValue(key, out var aliased) ? aliased : key);
    }

    /// <summary>Every valid state key (ladder + events) — used by the protocol tests.</summary>
    public static IEnumerable<string> AllKeys => ByKey.Keys;

    /// <summary>The mid-band peak intensity for a state — the fallback strength when a position's
    /// severity differs from the world episode's own (a wetter biome inside a rain episode).</summary>
    public static float MidPeak(WeatherDef def) => (def.PeakLo + def.PeakHi) * 0.5f;
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The four seasons of a planet (#3041, Justus 2026-10-30: "every planet should have autumn, summer, spring and winter, which
/// differ by cold or warmth; in winter plants grow slower"). Pure functions of the persisted system clock
/// (<c>WorldMetadata.SystemTimeDays</c>), the body's day length and a phase seeded per world and body — so neighbouring
/// planets are in different seasons, nothing is stored per body, and server and tests read the same year.
/// <list type="bullet">
/// <item><b>Length:</b> a season lasts <see cref="DaysPerSeason"/> of the planet's own days (≈ 40–75 minutes of play), so a
/// child sees one change within an evening.</item>
/// <item><b>Warmth:</b> a smooth swing of ± <see cref="PlanetType.SeasonTemperature"/> °C (data-driven, default 8 — kind to
/// the suit's comfort band), warmest in mid-summer, coldest in mid-winter, zero in mid-spring and mid-autumn.</item>
/// <item><b>Growth:</b> plants regrow faster in spring and slower in autumn and winter (<see cref="GrowthFactor"/>).</item>
/// <item><b>Who has them:</b> every world with air that is not a void (a cabin, a station) and whose type does not switch
/// them off (<c>seasonTemperature: 0</c>).</item>
/// <item><b>Which year (#3993/#3994):</b> the body's <see cref="SeasonProfile"/> (<see cref="SeasonProfiles"/>) picks the cycle
/// — four seasons, summer and winter, wet and dry, one season or none — and whether north and south are opposite;
/// <see cref="Read"/> gives the season at a latitude.</item>
/// </list>
/// The client draws the season (autumn leaves, winter frost, spring blossoms, summer warmth) from
/// <c>WorldEnvironment.Season</c>.
/// </summary>
public static class PlanetSeasons
{
    public const string Spring = "spring";
    public const string Summer = "summer";
    public const string Autumn = "autumn";
    public const string Winter = "winter";

    /// <summary>#3995: the wet and the dry season — the two seasons of a wet-and-dry planet, and what the tropics of a
    /// planet with hemispheres show.</summary>
    public const string Wet = "wet";
    public const string Dry = "dry";

    /// <summary>The seasons in the order a year runs through them.</summary>
    public static readonly string[] Order = { Spring, Summer, Autumn, Winter };

    /// <summary>#3993: the year of a summer-and-winter planet.</summary>
    public static readonly string[] SummerWinter = { Summer, Winter };

    /// <summary>#3993: the year of a wet-and-dry planet (the wet season first, where the wet/dry curve rises).</summary>
    public static readonly string[] WetDry = { Wet, Dry };

    /// <summary>Planet days per season.</summary>
    public const double DaysPerSeason = 5.0;

    /// <summary>Seconds of one system day — the unit of the server's system clock.</summary>
    public const double SystemDaySeconds = 600.0;

    /// <summary>Whether a world of this type has seasons: air, no void, and a non-zero season warmth.</summary>
    public static bool HasSeasons(PlanetType? planet)
        => planet is { Void: false, SeasonTemperature: > 0.0 }
           && !string.Equals(planet.Atmosphere, "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>The body's own phase of the year, 0..1, seeded per world and body.</summary>
    public static double Phase(long worldSeed, string bodyId)
        => (StableHashing.Mixed(worldSeed.ToString(CultureInfo.InvariantCulture) + ":season:" + bodyId) & 0xFFFF) / 65536.0;

    /// <summary>Where the year stands, 0..4: 0 = the first day of spring, 1 summer, 2 autumn, 3 winter.</summary>
    public static double YearPosition(double systemTimeDays, double dayLengthSeconds, double phase)
    {
        double planetDays = systemTimeDays * SystemDaySeconds / Math.Max(60.0, dayLengthSeconds);
        double pos = ((planetDays / DaysPerSeason) + (phase * 4.0)) % 4.0;
        return pos < 0.0 ? pos + 4.0 : pos;
    }

    /// <summary>The season at a year position.</summary>
    public static string SeasonAt(double yearPosition) => Order[Math.Min(3, Math.Max(0, (int)Math.Floor(yearPosition)))];

    /// <summary>How far into its season the year is, 0..1.</summary>
    public static double Progress(double yearPosition) => yearPosition - Math.Floor(yearPosition);

    /// <summary>The season's warmth at a year position: + <paramref name="amplitude"/> in mid-summer, − in mid-winter, 0 in
    /// mid-spring and mid-autumn.</summary>
    public static double TemperatureOffset(double yearPosition, double amplitude)
        => amplitude * Math.Cos((yearPosition - 1.5) * Math.PI / 2.0);

    // ---------------- #3993/#3994/#3995: profiles, hemispheres, the wet/dry curve on the year ----------------

    /// <summary>Planet days in the year of a profile: one <see cref="DaysPerSeason"/> per season of its cycle, four seasons'
    /// worth for a body with one season or none (the length its wet/dry curve runs on).</summary>
    public static double YearPlanetDays(SeasonProfile profile)
        => DaysPerSeason * (profile.Cycle is SeasonProfiles.TwoCycle or SeasonProfiles.RainCycle ? 2 : 4);

    /// <summary>The year in system days (the unit of the wet/dry curve, <c>WeatherSim.SeasonPeriodDays</c>).</summary>
    public static double YearSystemDays(SeasonProfile profile, double dayLengthSeconds)
        => YearPlanetDays(profile) * Math.Max(60.0, dayLengthSeconds) / SystemDaySeconds;

    /// <summary>Where the year stands, 0..1, for a body's profile (the north's year on a planet with hemispheres).</summary>
    public static double YearFraction(SeasonProfile profile, double systemTimeDays, double dayLengthSeconds, double phase)
    {
        double f = (systemTimeDays / YearSystemDays(profile, dayLengthSeconds)) + phase;
        f -= Math.Floor(f);
        return f;
    }

    /// <summary>Latitude in degrees (−90 south … +90 north, the torus seam at ±90) of a world Z.</summary>
    public static double LatitudeDeg(double z, int circumference)
    {
        if (circumference <= 0)
        {
            return 0.0;
        }

        double period = WorldConstants.LatitudePeriodFor(circumference);
        return period <= 0 ? 0.0 : WorldConstants.WrapZ(z, circumference) / period * 180.0;
    }

    /// <summary>Latitude (degrees, either hemisphere) up to which the tropics have no season at all.</summary>
    public const double TropicsDeg = 12.0;

    /// <summary>Latitude from which the season is at full strength.</summary>
    public const double FullSeasonDeg = 30.0;

    /// <summary>Latitude from which the season fades again toward the torus seam, where north meets south.</summary>
    public const double SeamFadeDeg = 75.0;

    /// <summary>How strongly a planet with hemispheres feels its season at a latitude, 0..1: nothing in the tropics up to
    /// <see cref="TropicsDeg"/>, full from <see cref="FullSeasonDeg"/>, fading back to nothing from <see cref="SeamFadeDeg"/>
    /// to the seam at 90° — continuous everywhere, so neither the equator nor the seam jumps from summer to winter.</summary>
    public static double HemisphereStrength(double latitudeDeg)
    {
        double a = Math.Min(90.0, Math.Abs(latitudeDeg));
        return SmoothStep(TropicsDeg, FullSeasonDeg, a) * (1.0 - SmoothStep(SeamFadeDeg, 90.0, a));
    }

    /// <summary>The season at a place on a body (#3993/#3994): its profile's season, how far into it, its warmth (°C, already
    /// scaled by the strength), the strength (1 off a planet with hemispheres), the hemisphere (<c>north</c>/<c>south</c>,
    /// empty without hemispheres) and the plants' growth factor. The south runs half a year behind the north.</summary>
    public static SeasonReading Read(SeasonProfile profile, double systemTimeDays, double dayLengthSeconds, double phase,
        double latitudeDeg, double amplitude)
    {
        if (!profile.HasSeasons)
        {
            return SeasonReading.None;
        }

        if (profile.Cycle == SeasonProfiles.OneCycle)
        {
            return new SeasonReading(profile.Fixed, 0.5, 0.0, 1.0, string.Empty, 1.0, profile.Cycle);
        }

        double f = YearFraction(profile, systemTimeDays, dayLengthSeconds, phase);
        string hemisphere = string.Empty;
        double strength = 1.0;
        if (profile.Hemispheres)
        {
            bool south = latitudeDeg < 0.0;
            hemisphere = south ? "south" : "north";
            strength = HemisphereStrength(latitudeDeg);
            if (south)
            {
                f = (f + 0.5) % 1.0;
            }
        }

        var order = profile.Order;
        double pos = f * order.Count;
        int index = Math.Min(order.Count - 1, (int)Math.Floor(pos));
        string season = order[index];
        double progress = pos - Math.Floor(pos);
        double warmth = profile.Cycle switch
        {
            SeasonProfiles.FourCycle => TemperatureOffset(pos, amplitude),
            SeasonProfiles.TwoCycle => amplitude * Math.Cos((pos - 0.5) * Math.PI),
            _ => 0.0, // a wet-and-dry year swings the rain, not the warmth
        };
        double growth = 1.0 + ((GrowthFactor(season) - 1.0) * strength);
        return new SeasonReading(season, progress, warmth * strength, strength, hemisphere, growth, profile.Cycle);
    }

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Max(0.0, Math.Min(1.0, (x - a) / (b - a)));
        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>How fast plants regrow in a season, on top of the weather (spring 1.2, summer 1, autumn 0.9, winter 0.6; no
    /// season 1).</summary>
    public static double GrowthFactor(string? season) => season switch
    {
        Spring => 1.2,
        Autumn => 0.9,
        Winter => 0.6,
        _ => 1.0,
    };
}

/// <summary>The season at a place (<see cref="PlanetSeasons.Read"/>). <see cref="Season"/> is empty on a body without
/// seasons; <see cref="Cycle"/> is the profile's cycle key.</summary>
public readonly record struct SeasonReading(string Season, double Progress, double Warmth, double Strength, string Hemisphere,
    double Growth, string Cycle)
{
    /// <summary>No season: nothing to show, no warmth, the plain growth rate.</summary>
    public static readonly SeasonReading None = new(string.Empty, 0.0, 0.0, 0.0, string.Empty, 1.0, string.Empty);
}

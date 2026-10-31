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

    /// <summary>The seasons in the order a year runs through them.</summary>
    public static readonly string[] Order = { Spring, Summer, Autumn, Winter };

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

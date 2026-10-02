// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Weather;

/// <summary>
/// One world's weather episode reduced to what a POSITION needs (#2174): the state in force, where the ladder
/// stands, the world's ladder band and the episode envelope. The server builds it from its live
/// <c>WeatherSim</c>; the client builds it from the <c>SystemWeather</c> snapshot — so both feed the very same
/// numbers into <see cref="WeatherProjection.At"/>.
/// </summary>
public readonly struct WeatherEpisode
{
    public WeatherEpisode(string state, int ladderSeverity, int ladderFloor, int ladderCeiling, float peak, float intensity, bool dynamic)
    {
        State = state ?? "clear";
        LadderSeverity = ladderSeverity;
        LadderFloor = ladderFloor;
        LadderCeiling = ladderCeiling;
        Peak = peak;
        Intensity = intensity;
        Dynamic = dynamic;
    }

    /// <summary>The world-level state key (a ladder state or an event).</summary>
    public string State { get; }

    /// <summary>Where the ladder stands (0 clear .. 3 storm), kept across events.</summary>
    public int LadderSeverity { get; }

    /// <summary>Lowest ladder severity this world reaches (an overcast world sits at 1).</summary>
    public int LadderFloor { get; }

    /// <summary>Highest ladder severity this world reaches (airless bodies are pinned at 0).</summary>
    public int LadderCeiling { get; }

    /// <summary>This episode's rolled peak intensity.</summary>
    public float Peak { get; }

    /// <summary>The envelope-shaped intensity right now.</summary>
    public float Intensity { get; }

    /// <summary>False on fixed-weather worlds (ship cabins, station decks): no position shifts apply.</summary>
    public bool Dynamic { get; }
}

/// <summary>A drifting weather front reduced to its geometry (#2174) — the snapshot form of the server's
/// <c>WeatherFront</c>.</summary>
public readonly struct WeatherFrontState
{
    public WeatherFrontState(double centerX, double halfWidth, double drift, int boost)
    {
        CenterX = centerX;
        HalfWidth = halfWidth;
        Drift = drift;
        Boost = boost;
    }

    /// <summary>Longitude (world X) of the front's centre.</summary>
    public double CenterX { get; }

    /// <summary>True half-width in blocks.</summary>
    public double HalfWidth { get; }

    /// <summary>Blocks per second along X (signed).</summary>
    public double Drift { get; }

    /// <summary>Ladder steps the front adds where it covers (1 or 2).</summary>
    public int Boost { get; }
}

/// <summary>
/// The weather at a position, as ONE formula shared by the server (gameplay: what you feel, what falls, the HUD)
/// and the client (the orbit cloud shells and the map layers) — #2174. The server's world episode is shifted by a
/// persistent per-biome offset, by any front covering the longitude and by one step above the cloud line; events
/// blanket the whole world. Before this lived in <c>GameServerWeather</c> only, so nothing off the surface could
/// show it truthfully.
/// </summary>
public static class WeatherProjection
{
    /// <summary>System-days per rotation step of the biome offsets (the wet biome is not the wet one forever).</summary>
    public const double BiomeOffsetEraDays = 6.0;

    /// <summary>Persistent per-biome weather offset (−1 drier .. +2 wetter), deterministic per save, slowly rotating
    /// over the shared system clock (#900).</summary>
    public static int BiomeOffset(long worldSeed, int biomeIndex, double systemTimeDays)
    {
        long era = (long)(systemTimeDays / BiomeOffsetEraDays);
        long h = unchecked(worldSeed ^ ((biomeIndex + era) * 2654435761L) ^ 0xB10);
        return (int)((ulong)(h < 0 ? -h : h) % 4UL) - 1; // 0..3 → -1..+2
    }

    /// <summary>Clamps a ladder severity into a world's own band.</summary>
    public static int ClampSeverity(int severity, int floor, int ceiling)
        => Math.Clamp(severity, Math.Clamp(floor, 0, WeatherCatalog.MaxSeverity), Math.Clamp(ceiling, 0, WeatherCatalog.MaxSeverity));

    /// <summary>Extra ladder steps from any front covering longitude <paramref name="x"/> (0 when none does) — the
    /// true-width rule the server applies.</summary>
    public static int FrontBoost(IReadOnlyList<WeatherFrontState>? fronts, double x, int circumference)
    {
        if (fronts is null)
        {
            return 0;
        }

        int best = 0;
        double circ = Math.Max(1, circumference);
        for (int i = 0; i < fronts.Count; i++)
        {
            var f = fronts[i];
            double d = Math.Abs(x - f.CenterX);
            d = Math.Min(d, circ - d); // shortest way round the wrap
            if (d <= f.HalfWidth)
            {
                best = Math.Max(best, f.Boost);
            }
        }

        return best;
    }

    /// <summary>The absolute Y above which a position reads one ladder step wetter (summits in the cloud).</summary>
    public static double CloudLineY(PlanetType? planet)
        => planet is null ? double.MaxValue : planet.BaseHeight + planet.Amplitude * 1.15 + 10;

    /// <summary>The state and intensity at a position. <paramref name="hasPosition"/> = false gives the
    /// world-level weather (no shifts). Events and fixed-weather worlds ignore every shift.</summary>
    public static (string State, float Intensity) At(in WeatherEpisode episode, int biomeOffset, int frontBoost, bool aboveCloudLine,
        bool hasPosition = true)
    {
        if (!episode.Dynamic)
        {
            return (episode.State, episode.Intensity);
        }

        // An EVENT (fog, gale, blizzard, ion storm, …) blankets the whole world: it is not on the ladder, so no
        // biome/front/altitude arithmetic touches it.
        var current = WeatherCatalog.Find(episode.State);
        if (current is not null && !current.IsLadder)
        {
            return (episode.State, episode.Intensity);
        }

        int level = episode.LadderSeverity;
        if (hasPosition)
        {
            level += biomeOffset;
            level += frontBoost;
            if (aboveCloudLine)
            {
                level += 1; // summits sit in the cloud/snow while the valley below stays clear
            }
        }

        // Clamp into THIS world's band: an overcast world never reads clear, an airless one never reads rain.
        level = ClampSeverity(level, episode.LadderFloor, episode.LadderCeiling);
        var def = WeatherCatalog.Ladder[level];
        // Keep the world episode's envelope shape, scaled to this position's severity, so a wetter biome inside a
        // rain episode still swells and fades with the same rhythm.
        float envelope = episode.Peak > 0.0001f ? episode.Intensity / episode.Peak : 0f;
        float peak = level == episode.LadderSeverity ? episode.Peak : WeatherCatalog.MidPeak(def);
        return (def.Key, Math.Clamp(envelope * peak, 0f, 1f));
    }

    /// <summary>The precipitation form for a state at a temperature: nothing unless something falls; events carry
    /// their own form (the episode's roll); a type may name what its ladder rain falls as (#2063); otherwise the
    /// climate decides — sand worlds blow sand, very hot worlds rain ash, cold ones hail/snow/sleet.</summary>
    public static string Precipitation(string? state, PlanetType? planet, float temperature, string? episodePrecip)
    {
        var def = WeatherCatalog.Find(state);
        if (def is null || def.Precip.Length == 0)
        {
            return "none";
        }

        string rolled = string.IsNullOrEmpty(episodePrecip) ? "none" : episodePrecip!;
        // Events carry their own form (acid, embers, meteors, spores, blown dust) — temperature has no say over
        // what an ion-charged sky throws at you.
        if (!def.IsLadder && def.Key != "blizzard" && def.Key != "drizzle")
        {
            return rolled == "none" ? def.Precip[0] : rolled;
        }

        if (planet is { Precipitation.Length: > 0 })
        {
            return planet.Precipitation;
        }

        // Ladder rain (and the two wet events) resolves by climate, position-dependent: the snow line has to agree
        // with where worldgen actually freezes water.
        if (planet?.SurfaceBlock == "sand")
        {
            return "sandstorm"; // dry worlds blow sand
        }

        if (temperature >= 55f)
        {
            return "ash";   // fire-rain / ash on very hot (lava) worlds
        }

        if (temperature <= -15f)
        {
            return "hail";  // very cold → hail
        }

        if (temperature <= 2f)
        {
            return "snow";  // cold → snow
        }

        if (temperature <= 5f)
        {
            return "sleet"; // the wet-snow band between snow and rain
        }

        // Within the rain band the episode's own roll decides between a downpour and a drizzle.
        return rolled is "drizzle" or "rain" ? rolled : "rain";
    }
}

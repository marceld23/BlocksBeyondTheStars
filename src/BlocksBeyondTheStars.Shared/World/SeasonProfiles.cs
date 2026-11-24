// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// A body's season profile (#3993): which cycle its year runs through and whether its north and south have opposite seasons
/// (#3994). <see cref="Cycle"/> is one of the <c>SeasonProfiles.*Cycle</c> keys; <see cref="Fixed"/> names the season of a
/// <see cref="SeasonProfiles.OneCycle"/> body.
/// </summary>
public sealed record SeasonProfile(string Cycle, string Fixed, bool Hemispheres)
{
    /// <summary>A body without seasons.</summary>
    public static readonly SeasonProfile NoSeasons = new(SeasonProfiles.NoneCycle, string.Empty, false);

    /// <summary>The seasons the year runs through, in order (empty for none, one entry for a one-season body).</summary>
    public IReadOnlyList<string> Order => Cycle switch
    {
        SeasonProfiles.FourCycle => PlanetSeasons.Order,
        SeasonProfiles.TwoCycle => PlanetSeasons.SummerWinter,
        SeasonProfiles.RainCycle => PlanetSeasons.WetDry,
        SeasonProfiles.OneCycle => new[] { Fixed },
        _ => Array.Empty<string>(),
    };

    /// <summary>Whether the body has any season at all.</summary>
    public bool HasSeasons => Cycle != SeasonProfiles.NoneCycle;
}

/// <summary>
/// Picks a body's season profile (#3993, lead decisions 2026-11-24): a planet type may fix it in <c>data/planets.json</c>
/// (<c>seasons</c>, <c>hemispheres</c>), else it is rolled per world seed and body from the type's weights
/// (<c>seasonRoll</c>, else <see cref="DefaultRoll"/>). The start planet always has four seasons and keeps one season across
/// the whole planet unless its type pins hemispheres (its landing pad sits at the equator). Hemispheres are rolled only for
/// planet-class bodies with four or two seasons. Pure functions of their inputs — nothing is stored, so server, tests and an
/// older save all read the same profile.
/// </summary>
public static class SeasonProfiles
{
    public const string RollKey = "roll";
    public const string FourCycle = "four";
    public const string TwoCycle = "two";
    public const string RainCycle = "rain";
    public const string OneCycle = "one";
    public const string NoneCycle = "none";

    /// <summary>The share of four- and two-season planets that roll hemispheres.</summary>
    public const double HemisphereChance = 0.4;

    /// <summary>The roll's weights when a type names none.</summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultRoll = new Dictionary<string, double>
    {
        [FourCycle] = 45,
        [TwoCycle] = 20,
        [RainCycle] = 15,
        [OneCycle] = 10,
        [NoneCycle] = 10,
    };

    /// <summary>The keys a type may name in <c>seasons</c> / <c>seasonRoll</c>.</summary>
    public static readonly IReadOnlyCollection<string> Keys = new[]
    {
        FourCycle, TwoCycle, RainCycle, OneCycle, NoneCycle,
        PlanetSeasons.Spring, PlanetSeasons.Summer, PlanetSeasons.Autumn, PlanetSeasons.Winter,
    };

    /// <summary>The profile of a body.</summary>
    /// <param name="planet">Its planet type (null or without seasons → none).</param>
    /// <param name="worldSeed">The world seed.</param>
    /// <param name="bodyId">The body's id.</param>
    /// <param name="isStart">Whether it is the world's start planet.</param>
    /// <param name="planetClass">Whether it is a planet-sized body (moons and asteroids never roll hemispheres).</param>
    public static SeasonProfile For(PlanetType? planet, long worldSeed, string bodyId, bool isStart, bool planetClass)
    {
        if (!PlanetSeasons.HasSeasons(planet) || string.IsNullOrEmpty(bodyId))
        {
            return SeasonProfile.NoSeasons;
        }

        string key = isStart ? FourCycle : ChooseKey(planet!, worldSeed, bodyId);
        var (cycle, fixedSeason) = Resolve(key, planet!, worldSeed, bodyId);
        if (cycle == NoneCycle)
        {
            return SeasonProfile.NoSeasons;
        }

        bool canSplit = cycle is FourCycle or TwoCycle;
        bool hemispheres = planet!.Hemispheres switch
        {
            { } pinned => pinned && canSplit,
            null => canSplit && planetClass && !isStart && Unit(worldSeed, bodyId, "hemispheres") < HemisphereChance,
        };
        return new SeasonProfile(cycle, fixedSeason, hemispheres);
    }

    /// <summary>The profile key of a non-start body: the type's fixed key, else the weighted roll.</summary>
    private static string ChooseKey(PlanetType planet, long worldSeed, string bodyId)
    {
        string fixedKey = (planet.Seasons ?? RollKey).Trim().ToLowerInvariant();
        if (fixedKey != RollKey && IsKnown(fixedKey))
        {
            return fixedKey;
        }

        var weights = planet.SeasonRoll ?? (IReadOnlyDictionary<string, double>)DefaultRoll;
        double total = 0;
        foreach (var kv in weights)
        {
            if (IsKnown(kv.Key) && kv.Value > 0)
            {
                total += kv.Value;
            }
        }

        if (total <= 0)
        {
            return FourCycle;
        }

        double pick = Unit(worldSeed, bodyId, "profile") * total;
        string last = FourCycle;
        foreach (var kv in weights)
        {
            if (!IsKnown(kv.Key) || kv.Value <= 0)
            {
                continue;
            }

            last = kv.Key;
            pick -= kv.Value;
            if (pick < 0)
            {
                return kv.Key;
            }
        }

        return last;
    }

    /// <summary>A profile key as (cycle, fixed season): a season name is a one-season body of that season; <c>one</c> picks
    /// the season from the type's warmth (hot → summer, cold → winter, else a seeded spring, summer or autumn).</summary>
    private static (string Cycle, string Fixed) Resolve(string key, PlanetType planet, long worldSeed, string bodyId)
    {
        switch (key)
        {
            case PlanetSeasons.Spring or PlanetSeasons.Summer or PlanetSeasons.Autumn or PlanetSeasons.Winter:
                return (OneCycle, key);
            case OneCycle:
                string season = planet.BaseTemperature >= 30 ? PlanetSeasons.Summer
                    : planet.BaseTemperature <= -10 ? PlanetSeasons.Winter
                    : Mild[StableHashing.Index(Hash(worldSeed, bodyId, "one"), Mild.Length)];
                return (OneCycle, season);
            default:
                return (key, string.Empty);
        }
    }

    private static readonly string[] Mild = { PlanetSeasons.Spring, PlanetSeasons.Summer, PlanetSeasons.Autumn };

    /// <summary>Whether a key is a profile key (<see cref="Keys"/>).</summary>
    public static bool IsKnown(string key) => ((ICollection<string>)Keys).Contains(key);

    /// <summary>The phase (0..1) of the body's wet/dry curve within its year: for a wet-and-dry body the year's own phase
    /// (the wet season is its first season), else seeded per body.</summary>
    public static double WetPhase(SeasonProfile profile, long worldSeed, string bodyId)
        => profile.Cycle == RainCycle ? PlanetSeasons.Phase(worldSeed, bodyId) : Unit(worldSeed, bodyId, "wet");

    private static ulong Hash(long worldSeed, string bodyId, string salt)
        => StableHashing.Mixed(worldSeed.ToString(CultureInfo.InvariantCulture) + ":seasonprofile:" + salt + ":" + bodyId);

    private static double Unit(long worldSeed, string bodyId, string salt) => StableHashing.Unit(Hash(worldSeed, bodyId, salt));
}

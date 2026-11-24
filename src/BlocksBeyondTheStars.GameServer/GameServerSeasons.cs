// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The seasons on the server (#3041, see <see cref="PlanetSeasons"/>): a body's season comes from the saved system clock, its
/// day length, its seeded phase and — since #3993/#3994 — its season profile (<see cref="SeasonProfiles"/>: four seasons,
/// summer and winter, wet and dry, one season or none, with opposite hemispheres on some planets) and the place's latitude.
/// The season's warmth joins the air temperature (<c>TemperatureAt</c>, so the suit, the precipitation form — winter snow —
/// and the HUD all feel it), plant regrowth runs × the season's growth factor (<c>WeatherRegrowFactor</c>), the wet/dry curve
/// of the weather runs on the same year (<see cref="TieWetnessToYear"/>, #3995), and the environment broadcast carries the
/// season for the client's look.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The start planet's id (set when the galaxy is built): it always has four seasons (#3993).</summary>
    private string _startBodyId = string.Empty;

    /// <summary>Each body's profile, with the planet type it was derived for (a retyped body derives it anew).</summary>
    private readonly Dictionary<string, (string Type, SeasonProfile Profile)> _seasonProfiles = new();

    /// <summary>A body's season profile (#3993).</summary>
    private SeasonProfile SeasonProfileOf(string bodyId, PlanetType? planet)
    {
        if (planet is null || string.IsNullOrEmpty(bodyId))
        {
            return SeasonProfile.NoSeasons;
        }

        if (_seasonProfiles.TryGetValue(bodyId, out var cached) && cached.Type == planet.Key)
        {
            return cached.Profile;
        }

        var body = _galaxy?.FindBody(bodyId);
        bool planetClass = body is not null && body.Kind == CelestialKind.Planet && !WorldConstants.IsAsteroidType(planet.Key);
        bool isStart = bodyId == _startBodyId || (body is null && _startBodyId.Length == 0 && bodyId == _meta.ActiveLocationId);
        var profile = SeasonProfiles.For(planet, _meta.Seed, bodyId, isStart, planetClass);
        _seasonProfiles[bodyId] = (planet.Key, profile);
        return profile;
    }

    /// <summary>A body's season at a world Z (its latitude matters only on a planet with hemispheres); a world without seasons
    /// reads <see cref="SeasonReading.None"/>.</summary>
    private SeasonReading SeasonOf(string bodyId, PlanetType? planet, double dayLengthSeconds, double z, int circumference)
    {
        if (SeasonsOffForTest || !PlanetSeasons.HasSeasons(planet) || string.IsNullOrEmpty(bodyId))
        {
            return SeasonReading.None;
        }

        var profile = SeasonProfileOf(bodyId, planet);
        double latitude = profile.Hemispheres ? PlanetSeasons.LatitudeDeg(z, circumference) : 0.0;
        return PlanetSeasons.Read(profile, _systemTimeDays, dayLengthSeconds, PlanetSeasons.Phase(_meta.Seed, bodyId), latitude,
            planet!.SeasonTemperature);
    }

    /// <summary>The active world's season at a position (default = the equator, the world-level reading).</summary>
    private SeasonReading ActiveSeasonAt(Vector3f pos)
        => SeasonOf(_world.LocationId, _content.GetPlanet(_worlds.Active.PlanetType), _dayLength, pos.Z, _world.Circumference);

    /// <summary>The active world's season at its equator (the world-level reading).</summary>
    private SeasonReading ActiveSeason() => ActiveSeasonAt(default);

    /// <summary>Whether the active world has opposite hemispheres (#3994) — then snow settles and melts by latitude.</summary>
    private bool ActiveWorldHasHemispheres
        => !SeasonsOffForTest && SeasonProfileOf(_world.LocationId, _content.GetPlanet(_worlds.Active.PlanetType)).Hemispheres;

    /// <summary>#3995: points a weather sim's wet/dry curve at the body's year — one curve for the whole planet, its timing
    /// within the year seeded per body (on a wet-and-dry planet the wet season is the year's first season). A body with one
    /// season keeps a mild, steady swing; a wet-and-dry planet a clear one. The sim's RNG draws stay untouched, so its
    /// episodes keep their sequence.</summary>
    private void TieWetnessToYear(WeatherSim sim, string bodyId, PlanetType? planet)
    {
        if (SeasonsOffForTest || planet is null || !PlanetSeasons.HasSeasons(planet))
        {
            return;
        }

        var profile = SeasonProfileOf(bodyId, planet);
        sim.SeasonPeriodDays = PlanetSeasons.YearSystemDays(profile, planet.DayLengthSeconds);
        sim.SeasonPhase = SeasonProfiles.WetPhase(profile, _meta.Seed, bodyId);
        sim.SeasonAmplitude = profile.Cycle switch
        {
            SeasonProfiles.RainCycle => System.Math.Max(0.35, planet.SeasonAmplitude),
            SeasonProfiles.OneCycle => planet.SeasonAmplitude * 0.3,
            _ => planet.SeasonAmplitude,
        };
    }

    /// <summary>Test seam (#3041): the active world's season at its equator.</summary>
    public SeasonReading ActiveSeasonForTest => ActiveSeason();

    /// <summary>Test seam (#3994): the active world's season at a world Z.</summary>
    public SeasonReading ActiveSeasonAtForTest(double z) => ActiveSeasonAt(new Vector3f(1f, 70f, (float)z));

    /// <summary>Test seam (#3993): a body's season profile.</summary>
    public SeasonProfile SeasonProfileForTest(string bodyId)
        => SeasonProfileOf(bodyId, _content.GetPlanet(_galaxy?.FindBody(bodyId)?.PlanetType ?? _worlds.Active.PlanetType));

    /// <summary>Test seam (#3993): the start planet's id.</summary>
    public string StartBodyIdForTest => _startBodyId;

    /// <summary>Test seam (#3041): no seasons on any world — for tests that time plant growth or temperatures to the second
    /// and are about something else (the season's growth factor would otherwise depend on the world seed's year). Set before
    /// <c>Start</c> it also keeps the weather's own wet/dry roll (#3995: no tie to the year).</summary>
    public bool SeasonsOffForTest { get; set; }

    /// <summary>Test seam (#3041): moves the system clock (the seasons run on it).</summary>
    public void SetSystemTimeDaysForTest(double days) => _systemTimeDays = days;
}

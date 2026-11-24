// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The season profiles (#3993), the hemispheres with their tropics (#3994) and the wet/dry season on the planet's year
/// (#3995), lead decisions of 2026-11-24: rolled per world and body, the start planet always has four seasons, one season
/// grows plants at the plain rate, Earth has opposite hemispheres.
/// </summary>
public sealed class SeasonProfilesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SeasonProfilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_seasonprof_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var r in _repos)
        {
            r.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private static PlanetType Temperate(string seasons = "roll", bool? hemispheres = null)
        => new() { Key = "test_temperate", Atmosphere = "breathable", BaseTemperature = 15, Seasons = seasons, Hemispheres = hemispheres };

    private static SeasonProfile Profile(string cycle, bool hemispheres = false, string fixedSeason = "")
        => new(cycle, fixedSeason, hemispheres);

    // ---------------- #3993 the profile ----------------

    [Fact]
    public void TheStartPlanet_AlwaysHasFourSeasons_AcrossTheWholePlanet()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var p = SeasonProfiles.For(Temperate(), seed, "sys0-p" + (seed % 7), isStart: true, planetClass: true);
            Assert.Equal(SeasonProfiles.FourCycle, p.Cycle);
            Assert.False(p.Hemispheres); // its landing pad sits at the equator
        }
    }

    [Fact]
    public void TheRoll_IsStableForASeed_AndVariesAcrossWorlds()
    {
        var planet = Temperate();
        Assert.Equal(SeasonProfiles.For(planet, 42, "sys3-p1", false, true), SeasonProfiles.For(planet, 42, "sys3-p1", false, true));

        var cycles = new HashSet<string>();
        int hemispheres = 0, splittable = 0;
        for (long seed = 1; seed <= 400; seed++)
        {
            var p = SeasonProfiles.For(planet, seed, "sys1-p2", false, true);
            cycles.Add(p.Cycle);
            if (p.Cycle is SeasonProfiles.FourCycle or SeasonProfiles.TwoCycle)
            {
                splittable++;
                hemispheres += p.Hemispheres ? 1 : 0;
            }
            else
            {
                Assert.False(p.Hemispheres); // only four or two seasons split into hemispheres
            }
        }

        Assert.Equal(new[] { "four", "none", "one", "rain", "two" }, cycles.OrderBy(c => c, StringComparer.Ordinal));
        Assert.InRange(hemispheres / (double)splittable, 0.25, 0.55);
    }

    [Fact]
    public void AFixedProfile_AndPinnedHemispheres_AreKept_AndAMoonNeverRollsHemispheres()
    {
        Assert.Equal(SeasonProfiles.NoneCycle, SeasonProfiles.For(Temperate("none"), 1, "b", false, true).Cycle);
        var winter = SeasonProfiles.For(Temperate("winter"), 1, "b", false, true);
        Assert.Equal((SeasonProfiles.OneCycle, PlanetSeasons.Winter), (winter.Cycle, winter.Fixed));
        Assert.True(SeasonProfiles.For(Temperate("four", hemispheres: true), 1, "b", false, true).Hemispheres);
        Assert.True(SeasonProfiles.For(Temperate("four", hemispheres: true), 1, "b", isStart: true, planetClass: true).Hemispheres);
        Assert.False(SeasonProfiles.For(Temperate("rain", hemispheres: true), 1, "b", false, true).Hemispheres);
        for (long seed = 1; seed <= 100; seed++)
        {
            Assert.False(SeasonProfiles.For(Temperate("four"), seed, "moon", false, planetClass: false).Hemispheres);
        }

        var airless = new PlanetType { Key = "rock", Atmosphere = "none" };
        Assert.Same(SeasonProfile.NoSeasons, SeasonProfiles.For(airless, 1, "b", true, true));
    }

    [Fact]
    public void OneSeason_IsChosenFromTheTypesWarmth()
    {
        var hot = new PlanetType { Key = "hot", Atmosphere = "toxic", BaseTemperature = 80, Seasons = "one" };
        var cold = new PlanetType { Key = "cold", Atmosphere = "toxic", BaseTemperature = -40, Seasons = "one" };
        Assert.Equal(PlanetSeasons.Summer, SeasonProfiles.For(hot, 5, "b", false, true).Fixed);
        Assert.Equal(PlanetSeasons.Winter, SeasonProfiles.For(cold, 5, "b", false, true).Fixed);
    }

    [Fact]
    public void EveryProfileKeyInThePlanetData_IsKnown_AndEarthHasHemispheres()
    {
        foreach (var planet in _content.Planets.Values)
        {
            Assert.True(planet.Seasons == SeasonProfiles.RollKey || SeasonProfiles.IsKnown(planet.Seasons),
                $"{planet.Key}: unknown seasons '{planet.Seasons}'");
            foreach (string key in planet.SeasonRoll?.Keys ?? Enumerable.Empty<string>())
            {
                Assert.True(SeasonProfiles.IsKnown(key), $"{planet.Key}: unknown seasonRoll key '{key}'");
            }
        }

        var earth = _content.GetPlanet("sol_earth")!;
        Assert.Equal((SeasonProfiles.FourCycle, true), (earth.Seasons, earth.Hemispheres == true));
        Assert.Equal(SeasonProfiles.NoneCycle, _content.GetPlanet("gas_giant")!.Seasons);
    }

    // ---------------- the year of each cycle ----------------

    [Fact]
    public void FourSeasons_ReadExactlyAsBefore()
    {
        var four = Profile(SeasonProfiles.FourCycle);
        for (double day = 0; day < 40; day += 0.37)
        {
            double pos = PlanetSeasons.YearPosition(day, 600, 0.3);
            var r = PlanetSeasons.Read(four, day, 600, 0.3, 0.0, 8.0);
            Assert.Equal(PlanetSeasons.SeasonAt(pos), r.Season);
            Assert.Equal(PlanetSeasons.TemperatureOffset(pos, 8.0), r.Warmth, 6);
            Assert.Equal(PlanetSeasons.GrowthFactor(r.Season), r.Growth, 6);
        }
    }

    [Fact]
    public void SummerAndWinter_AndWetAndDry_RunTheirOwnYears()
    {
        var two = Profile(SeasonProfiles.TwoCycle);
        var rain = Profile(SeasonProfiles.RainCycle);
        var seenTwo = new List<string>();
        var seenRain = new List<string>();
        for (int day = 0; day < 10; day++)
        {
            seenTwo.Add(PlanetSeasons.Read(two, day + 0.5, 600, 0.0, 0.0, 8.0).Season);
            seenRain.Add(PlanetSeasons.Read(rain, day + 0.5, 600, 0.0, 0.0, 8.0).Season);
        }

        Assert.Equal(new[] { "summer", "winter" }, seenTwo.Distinct());
        Assert.Equal(new[] { "wet", "dry" }, seenRain.Distinct());
        Assert.All(seenTwo.Take(5), s => Assert.Equal("summer", s)); // five planet days a season
        Assert.True(PlanetSeasons.Read(two, 2.5, 600, 0.0, 0.0, 8.0).Warmth > 7.9);  // mid-summer
        Assert.True(PlanetSeasons.Read(two, 7.5, 600, 0.0, 0.0, 8.0).Warmth < -7.9); // mid-winter
        Assert.Equal(0.0, PlanetSeasons.Read(rain, 2.5, 600, 0.0, 0.0, 8.0).Warmth);  // the rain swings, not the warmth
        Assert.Equal(0.6, PlanetSeasons.Read(two, 7.5, 600, 0.0, 0.0, 8.0).Growth, 6);
    }

    [Fact]
    public void OneSeason_StaysAllYear_WithNoWarmthSwing_AndThePlainGrowth()
    {
        var spring = Profile(SeasonProfiles.OneCycle, fixedSeason: PlanetSeasons.Spring);
        for (double day = 0; day < 30; day += 1.3)
        {
            var r = PlanetSeasons.Read(spring, day, 600, 0.4, 0.0, 8.0);
            Assert.Equal(("spring", 0.0, 1.0), (r.Season, r.Warmth, r.Growth));
        }

        Assert.Equal(SeasonReading.None, PlanetSeasons.Read(SeasonProfile.NoSeasons, 3, 600, 0.0, 0.0, 8.0));
    }

    // ---------------- #3994 hemispheres and tropics ----------------

    [Fact]
    public void NorthAndSouth_AreOpposite_AndTheTropicsAndTheSeamHaveNoSeason()
    {
        var earth = Profile(SeasonProfiles.FourCycle, hemispheres: true);
        for (double day = 0; day < 20; day += 0.5)
        {
            var north = PlanetSeasons.Read(earth, day, 600, 0.0, 45.0, 8.0);
            var south = PlanetSeasons.Read(earth, day, 600, 0.0, -45.0, 8.0);
            Assert.Equal(("north", "south"), (north.Hemisphere, south.Hemisphere));
            Assert.Equal(-north.Warmth, south.Warmth, 6); // half a year apart
            int ni = Array.IndexOf(PlanetSeasons.Order, north.Season), si = Array.IndexOf(PlanetSeasons.Order, south.Season);
            Assert.Equal(2, ((si - ni) + 4) % 4);
        }

        Assert.Equal(0.0, PlanetSeasons.HemisphereStrength(0.0));
        Assert.Equal(0.0, PlanetSeasons.HemisphereStrength(PlanetSeasons.TropicsDeg));
        Assert.Equal(1.0, PlanetSeasons.HemisphereStrength(50.0));
        Assert.Equal(0.0, PlanetSeasons.HemisphereStrength(90.0), 9);
        Assert.Equal(0.0, PlanetSeasons.Read(earth, 7.5, 600, 0.0, 5.0, 8.0).Warmth);
        Assert.Equal(1.0, PlanetSeasons.Read(earth, 7.5, 600, 0.0, 5.0, 8.0).Growth);
    }

    [Fact]
    public void TheSeason_ChangesSmoothly_FromPoleToPole()
    {
        var earth = Profile(SeasonProfiles.FourCycle, hemispheres: true);
        double previous = PlanetSeasons.Read(earth, 7.5, 600, 0.0, -90.0, 8.0).Warmth;
        for (double lat = -89.5; lat <= 90.0; lat += 0.5)
        {
            double warmth = PlanetSeasons.Read(earth, 7.5, 600, 0.0, lat, 8.0).Warmth;
            Assert.True(Math.Abs(warmth - previous) < 0.6, $"warmth jumps at {lat}°: {previous} → {warmth}");
            previous = warmth;
        }
    }

    [Fact]
    public void Latitude_FollowsTheWorldZ_NorthPositive_TheSeamAtNinety()
    {
        int circ = 16000;
        int period = WorldConstants.LatitudePeriodFor(circ);
        Assert.Equal(0.0, PlanetSeasons.LatitudeDeg(0, circ));
        Assert.Equal(45.0, PlanetSeasons.LatitudeDeg(period / 4.0, circ), 6);
        Assert.Equal(-45.0, PlanetSeasons.LatitudeDeg(-period / 4.0, circ), 6);
        Assert.Equal(-45.0, PlanetSeasons.LatitudeDeg(period * 0.75, circ), 6); // wrapped round the seam
    }

    // ---------------- #3995 the wet/dry season on the year ----------------

    [Fact]
    public void TheWetSeason_OfAWetAndDryPlanet_IsWhereTheWetnessCurveIsHigh()
    {
        var rain = Profile(SeasonProfiles.RainCycle);
        const long seed = 9;
        const string body = "sys2-p0";
        var sim = new WeatherSim(77)
        {
            SeasonPeriodDays = PlanetSeasons.YearSystemDays(rain, 600),
            SeasonPhase = SeasonProfiles.WetPhase(rain, seed, body),
            SeasonAmplitude = 0.35,
        };
        double phase = PlanetSeasons.Phase(seed, body);
        for (double day = 0.05; day < 20; day += 0.1)
        {
            var r = PlanetSeasons.Read(rain, day, 600, phase, 0.0, 8.0);
            double wet = sim.Wetness(day);
            if (Math.Abs(wet - 0.5) > 0.02)
            {
                Assert.Equal(wet > 0.5 ? "wet" : "dry", r.Season);
            }
        }
    }

    // ---------------- on the server ----------------

    private SvGameServer NewServer(string name, string startPlanet = "varied")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = startPlanet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        }, _content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TheServer_GivesTheStartPlanetFourSeasons_AndTheWetnessRunsOnItsYear()
    {
        var server = NewServer("prof_start");
        Assert.Equal(server.ActiveLocationId, server.StartBodyIdForTest);
        var profile = server.SeasonProfileForTest(server.ActiveLocationId);
        Assert.Equal(SeasonProfiles.FourCycle, profile.Cycle);
        Assert.False(profile.Hemispheres);
    }

    [Fact]
    public void OnAPlanetWithHemispheres_NorthAndSouthFeelOppositeSeasons()
    {
        _content.GetPlanet("varied")!.Hemispheres = true; // this test's own content copy: the start planet splits
        var server = NewServer("prof_hemi");
        server.AddLocalPlayer("Walker");
        int period = WorldConstants.LatitudePeriodFor(server.World.Circumference);
        double northZ = period / 4.0, southZ = -period / 4.0;

        double best = 0;
        for (double t = 0; t < 40; t += 0.25)
        {
            server.SetSystemTimeDaysForTest(t);
            var north = server.ActiveSeasonAtForTest(northZ);
            var south = server.ActiveSeasonAtForTest(southZ);
            Assert.Equal(-north.Warmth, south.Warmth, 4);
            Assert.Equal(0.0, server.ActiveSeasonAtForTest(0).Warmth); // the tropics
            if (Math.Abs(north.Warmth) > Math.Abs(best))
            {
                best = north.Warmth;
            }

            float tn = server.ReferenceTemperatureAtLatitudeForTest(northZ), ts = server.ReferenceTemperatureAtLatitudeForTest(southZ);
            Assert.True(Math.Abs((tn - ts) - (north.Warmth - south.Warmth)) <= 1.01, "the air follows each hemisphere's season");
        }

        Assert.True(Math.Abs(best) > 7.0, "a full summer or winter away from the equator");
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The client half of the planet weather package (#2170–#2178): the orbit look from data, the planet map bake, the
/// weather projection onto the map, the shared weather look, the sphere spin and the snapshot extrapolation — and,
/// end to end, that the client's projection of the server's snapshot is the weather the server applies on the ground.
/// </summary>
public sealed class PlanetWeatherClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void OrbitLook_EveryTypeWithAirAndClouds_GetsAShell_AirlessNone()
    {
        int shells = 0;
        foreach (var planet in Content.Planets.Values)
        {
            var atm = OrbitLook.For(planet, 7, "sys0-p1");
            bool air = !planet.IsAirless && !planet.SpaceSky && !planet.Void;
            Assert.Equal(air && planet.CloudDensity > 0.001, atm.HasClouds);
            Assert.Equal(air, atm.HasAir);
            if (atm.HasClouds)
            {
                shells++;
                Assert.Equal((float)Math.Clamp(planet.CloudDensity, 0.0, 1.0), atm.CloudDensity);
                Assert.Equal(AtmosphereTints.CloudRgb(7, "sys0-p1", planet, AtmosphereTints.SkyRgb(7, "sys0-p1", planet)), atm.CloudRgb);
            }
        }

        Assert.True(shells > 30, $"the data gives most types clouds (#2170 — the old table knew 15 keys), got {shells}");
        Assert.False(OrbitLook.For(Content.GetPlanet("crystal"), 7, "x").HasClouds); // airless — the old table gave it clouds
        Assert.True(OrbitLook.For(Content.GetPlanet("ocean"), 7, "x").HasClouds);    // the old table gave it none
    }

    [Fact]
    public void WeatherLook_KeepsTheSurfaceSkyTable_AndHasAGlyphForEveryState()
    {
        // The values the surface cloud layer used before the table moved (#2174).
        Assert.Equal(0.95f, WeatherLook.Sky("storm", 0.4f).Cover);
        Assert.Equal(0.35f, WeatherLook.Sky("storm", 0.4f).Darken);
        Assert.Equal(0.60f, WeatherLook.Sky("clouds", 0.4f).Cover);
        Assert.Equal(0.2f, WeatherLook.Sky("clear", 0.4f).Cover, 4);
        Assert.Equal(0.4f, WeatherLook.Cover("clear", 0.4f), 4); // the planet's base cover never thins
        Assert.Equal(0.95f, WeatherLook.Cover("storm", 0.4f), 4);
        foreach (var key in WeatherCatalog.AllKeys)
        {
            Assert.NotEqual("•", WeatherLook.Glyph(key));
        }

        Assert.Equal("#ff7439", WeatherLook.FamilyColorHex(WeatherLook.Family("storm")));
        Assert.Null(WeatherLook.FamilyColorHex(WeatherLook.Family("clouds")));
    }

    [Theory]
    [InlineData(0.0, 30.0, 0.0, 1.0)]
    [InlineData(0.35, -120.0, 90.0, 1.0)]
    [InlineData(0.8, 200.0, -45.0, -1.0)]
    [InlineData(0.5, 0.0, 180.0, -1.0)]
    public void PlanetSpin_TurnsTheNoonMeridianToTheSun(double tod, double sunAz, double seam, double dir)
    {
        double yaw = PlanetSpin.YawDegrees(tod, sunAz, seam, dir);
        double noonAz = PlanetSpin.AzimuthOf(PlanetSpin.NoonLongitude(tod), yaw, seam, dir);
        double diff = Math.Abs(((noonAz - sunAz) % 360.0 + 540.0) % 360.0 - 180.0);
        Assert.True(diff < 1e-6, $"noon meridian at {noonAz}°, sun at {sunAz}°");
        // Local time rule (the map's day/night band): noon at u = 0.5 − timeOfDay.
        Assert.Equal(((0.5 - tod) % 1.0 + 1.0) % 1.0, PlanetSpin.NoonLongitude(tod), 9);
    }

    [Fact]
    public void SystemWeatherState_ExtrapolatesFrontsAndARunningClock_NotTheStates()
    {
        var state = new SystemWeatherState();
        state.Apply(new SystemWeather
        {
            SystemId = "sys0",
            SystemTimeDays = 3.0,
            Bodies = new[]
            {
                new NetBodyWeather
                {
                    BodyId = "a", State = "rain", TimeOfDay = 0.9f, ClockRunning = true,
                    Fronts = new[] { new NetWeatherFront { CenterX = 990, HalfWidth = 50, Drift = 10, Boost = 1 } },
                },
                new NetBodyWeather { BodyId = "b", State = "clear", TimeOfDay = 0.35f },
            },
        }, now: 100.0);

        Assert.True(state.TryGet("a", out var a));
        var fronts = state.Fronts(a, 102.0, 1000);
        Assert.Equal(10.0, fronts[0].CenterX, 6);                    // 990 + 2 s × 10, wrapped round 1000
        Assert.Equal(0.9 + 0.2, state.TimeOfDay(a, 160.0, 300.0) + 1.0, 6); // 60 s of a 300 s day, wrapped
        Assert.True(state.TryGet("b", out var b));
        Assert.Equal(0.35, state.TimeOfDay(b, 1000.0, 300.0), 6);       // an unloaded body waits at its arrival hour
        Assert.Equal(3.0 + 60.0 / 600.0, state.SystemTimeDays(160.0), 9);
        Assert.Equal("rain", a.State);
        Assert.False(state.TryGet("nope", out _));
    }

    private static PlanetMapRequest Request(string type, string body = "sys0-p1", int w = 48, int h = 24, bool cratered = false, int circ = 6000)
    {
        var colors = new Dictionary<ushort, int>();
        int n = 1;
        foreach (ushort id in PlanetMapBakeJob.GroundBlockIds(Content, type))
        {
            colors[id] = (n * 0x3A5F17) & 0xFFFFFF; // distinct fake atlas colours per block
            n++;
        }

        return new PlanetMapRequest
        {
            Content = Content,
            WorldSeed = 4242,
            BodyId = body,
            FloraKey = "Sol · " + body,
            PlanetTypeKey = type,
            Circumference = circ,
            Width = w,
            Height = h,
            Continents = true,
            Generation = 19,
            Cratered = cratered,
            GroundColors = colors,
        };
    }

    [Fact]
    public void PlanetMapBake_InSteps_EqualsOneRun()
    {
        var whole = new PlanetMapBakeJob(Request("varied")).Run();
        var job = new PlanetMapBakeJob(Request("varied"));
        int steps = 0;
        while (!job.Step(3))
        {
            steps++;
        }

        Assert.True(steps > 3, "the browser path must really bake in slices");
        Assert.Equal(whole.Rgba, job.Data.Rgba);
        Assert.Equal(whole.Heights, job.Data.Heights);
        Assert.Equal(whole.Biomes, job.Data.Biomes);
    }

    [Fact]
    public void PlanetMapBake_ShowsTheBiomesOwnGround_AndCratersOnAirlessMoons()
    {
        var planet = Content.Planets.Values.First(p => p.Biomes.Count > 2 && !p.IsAirless && p.MinTerrainGeneration <= 19
            && p.Biomes.Select(b => b.SurfaceBlock).Distinct().Count() > 2);
        var map = new PlanetMapBakeJob(Request(planet.Key, w: 96, h: 48)).Run();
        Assert.True(map.Biomes.Distinct().Count() > 1, $"{planet.Key}: a multi-biome world must show more than one biome");
        var land = Enumerable.Range(0, map.Flags.Length).Where(i => map.Flags[i] == 0).ToList();
        var colours = land.Select(i => (map.Rgba[i * 4], map.Rgba[i * 4 + 1], map.Rgba[i * 4 + 2])).Distinct().Count();
        Assert.True(colours > 8, "the ground is not one flat colour");

        var airless = Content.Planets.Values.First(p => p.IsAirless && !p.SpaceSky && !p.Void && !p.Cratered);
        var smooth = new PlanetMapBakeJob(Request(airless.Key, "sys0-m1", cratered: false)).Run();
        var cratered = new PlanetMapBakeJob(Request(airless.Key, "sys0-m1", cratered: true)).Run();
        Assert.NotEqual(smooth.Heights, cratered.Heights); // #2170: airless moons carry their craters on the map
    }

    [Fact]
    public void PlanetMapBake_ColdWorlds_ShowSnow()
    {
        var planet = Content.GetPlanet("glacier") ?? Content.GetPlanet("ice")!;
        var map = new PlanetMapBakeJob(Request(planet.Key, w: 96, h: 48)).Run();
        Assert.Contains(map.Temperatures, t => t < 0);
        var snow = Content.GetBlock("snow")!.NumericId.Value;
        var ice = Content.GetBlock("ice")!.NumericId.Value;
        var req = Request(planet.Key, w: 96, h: 48);
        int snowRgb = req.GroundColors.TryGetValue(snow, out var s) ? s : -1;
        int iceRgb = req.GroundColors.TryGetValue(ice, out var c) ? c : -1;
        bool anyWhite = false;
        for (int i = 0; i < map.Flags.Length && !anyWhite; i++)
        {
            if (map.Flags[i] != 0 || map.Temperatures[i] >= -2)
            {
                continue;
            }

            int r = map.Rgba[i * 4], g = map.Rgba[i * 4 + 1], b = map.Rgba[i * 4 + 2];
            anyWhite = Near(r, g, b, snowRgb) || Near(r, g, b, iceRgb);
        }

        Assert.True(anyWhite, "cold land must show snow or ice ground");
    }

    private static bool Near(int r, int g, int b, int rgb)
    {
        if (rgb < 0)
        {
            return false;
        }

        // Height shading scales the colour by 0.7..1.2 and vegetation can wash it; compare the hue ratio.
        int er = (rgb >> 16) & 0xFF, eg = (rgb >> 8) & 0xFF, eb = rgb & 0xFF;
        double k = (r + g + b) / Math.Max(1.0, er + eg + eb);
        return Math.Abs(r - er * k) < 14 && Math.Abs(g - eg * k) < 14 && Math.Abs(b - eb * k) < 14;
    }

    [Fact]
    public void Projector_UsesTheSharedFormula_AndWideFrontsNeverChangeTheTrueWeather()
    {
        var map = new PlanetMapData
        {
            Width = 40,
            Height = 4,
            Circumference = 4000,
            LatitudePeriod = 2000,
            Heights = new short[160],
            Biomes = new byte[160],
            Temperatures = new sbyte[160],
            Flags = new byte[160],
        };
        for (int i = 0; i < 160; i++)
        {
            map.Biomes[i] = (byte)(i % 3);
            map.Heights[i] = (short)(i % 7 == 0 ? 400 : 60);
            map.Temperatures[i] = 15;
        }

        var planet = Content.GetPlanet("varied")!;
        var episode = new WeatherEpisode("clouds", 1, 0, 3, 0.4f, 0.4f, true);
        var fronts = new[] { new WeatherFrontState(1000, 20, 3, 1) }; // 40 blocks wide — under one 100-block pixel
        var pixels = new WeatherMapPixels();
        WeatherMapProjector.Project(map, episode, fronts, 99, 1.0, planet, 0.5f, "none", pixels);

        double cloudLine = WeatherProjection.CloudLineY(planet);
        bool widened = false;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                int wx = map.WorldX(x);
                var expected = WeatherProjection.At(episode, WeatherProjection.BiomeOffset(99, map.Biomes[i], 1.0),
                    WeatherProjection.FrontBoost(fronts, wx, map.Circumference), map.Heights[i] + 1 > cloudLine).State;
                Assert.Equal(expected, WeatherMapProjector.States[pixels.State[i]]);
                widened |= pixels.Visual[i] != pixels.State[i];
            }
        }

        Assert.True(widened, "the front must be drawn wider than its true band (#2175)");

        var airless = Content.Planets.Values.First(p => p.IsAirless);
        WeatherMapProjector.Project(map, episode, fronts, 99, 1.0, airless, 0f, "none", pixels);
        Assert.All(pixels.Cover, c => Assert.Equal(0f, c)); // no clouds without air
    }

    [Fact]
    public void ClientProjection_OfTheServersSnapshot_IsTheWeatherTheServerApplies()
    {
        using var h = new ClientServerHarness(Content);
        SystemWeather? snapshot = null;
        h.Client.SystemWeatherReceived += m => snapshot = m;
        h.Join();
        Assert.NotNull(h.JoinAccepted);

        var server = h.Server;
        server.SetWeatherForTest("clouds"); // a ladder state: biome offsets, the front and summits all shift it
        server.WeatherSimForTest.Fronts.Clear();
        var body = server.Galaxy.FindBody(server.ActiveLocationId)!;
        var planet = Content.GetPlanet(body.PlanetType!)!;
        int circ = WorldConstants.CircumferenceFor(body.Id, WorldConstants.SizeClassFor(body.Kind, planet.Key), body.SizeBias);
        server.AddWeatherFrontForTest(circ / 3, 400, 1);

        snapshot = null;
        h.Client.SendRequestLandingPads(body.Id); // the server answers with the system's weather too
        Assert.True(h.PumpUntil(() => snapshot != null, 30), "the system weather snapshot must arrive");

        var state = new SystemWeatherState();
        state.Apply(snapshot!, 0.0);
        Assert.True(state.TryGet(body.Id, out var bw));
        var join = h.JoinAccepted!;
        var map = new PlanetMapBakeJob(new PlanetMapRequest
        {
            Content = Content,
            WorldSeed = join.WorldSeed,
            BodyId = body.Id,
            FloraKey = body.Name,
            PlanetTypeKey = planet.Key,
            Circumference = circ,
            Width = 32,
            Height = 16,
            Continents = join.TerrainContinents,
            Generation = join.TerrainGeneration,
            LavaCoreVolcanoes = join.TerrainLavaCoreVolcanoes,
            Cratered = body.Kind == CelestialKind.Moon && planet.IsAirless,
        }).Run();

        var pixels = new WeatherMapPixels();
        WeatherMapProjector.Project(map, SystemWeatherState.Episode(bw), state.Fronts(bw, 0.0, circ), join.WorldSeed,
            state.SystemTimeDays(0.0), planet, (float)planet.CloudDensity, bw.Precip, pixels);

        int compared = 0, differing = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                var pos = new Vector3f(map.WorldX(x), map.Heights[i] + 1, map.WorldZ(y));
                if (server.WeatherAtForTest(pos).State != WeatherMapProjector.States[pixels.State[i]])
                {
                    differing++;
                }

                compared++;
            }
        }

        Assert.True(pixels.State.Distinct().Count() > 1, "the biomes, the front and the summits must vary the weather across the map");
        // A pixel centre can sit on a landing pad the loaded world levelled (the map knows no pads): allow a stray one.
        Assert.True(differing <= 1, $"{differing} of {compared} pixels disagree with the server's weather");
    }
}

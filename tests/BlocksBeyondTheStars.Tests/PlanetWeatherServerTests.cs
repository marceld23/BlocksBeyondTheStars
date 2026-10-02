// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The server half of the planet weather package (#2170–#2179): ambient weather for every body of an occupied system,
/// landing into the weather seen from orbit, the system weather snapshot, the weather per landing pad, the landing
/// map's clock for bodies other players hold, the once-per-tick orbital clock, and the per-world sky/cloud tints
/// moved to Shared.
/// </summary>
public sealed class PlanetWeatherServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public PlanetWeatherServerTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_pw_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Transport recording every server send, so a test can read what a player received.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }

        public void Stop() { }

        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        if (!server.Ship.HasModule("jump_generator"))
        {
            server.Ship.Modules.Add("jump_generator");
        }

        return server;
    }

    /// <summary>A body of the home system with air and changing weather (not the home body).</summary>
    private CelestialBody NeighbourWithWeather(SvGameServer server)
    {
        var home = server.Galaxy.FindBody(server.ActiveLocationId)!;
        return server.Galaxy.AllBodies().First(b =>
            b.SystemId == home.SystemId && b.Id != home.Id
            && b.Kind is CelestialKind.Planet or CelestialKind.Moon
            && _content.GetPlanet(b.PlanetType ?? string.Empty) is { IsAirless: false, SpaceSky: false, Void: false } p
            && !string.Equals(p.Weather, "clear", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OtherBodiesOfAnOccupiedSystem_CarryAmbientWeather_ThatAdvances()
    {
        var server = NewServer("ambient", new RecordingTransport());
        server.AddLocalPlayer("Pilot");
        var other = NeighbourWithWeather(server);

        server.Tick(1.1);
        var sim = server.BodyWeatherSimForTest(other.Id);
        Assert.NotNull(sim); // the body nobody stands on has weather of its own now
        double before = sim!.Elapsed;
        for (int i = 0; i < 5; i++)
        {
            server.Tick(1.1);
        }

        Assert.True(sim.Elapsed != before || sim.State != "clear", "the ambient sim must advance while nobody is on the body");
    }

    [Fact]
    public void Landing_AdoptsTheBodysAmbientWeather_InsteadOfRestarting()
    {
        var server = NewServer("adopt", new RecordingTransport());
        server.AddLocalPlayer("Pilot");
        var other = NeighbourWithWeather(server);
        server.Tick(1.1);
        var ambient = server.BodyWeatherSimForTest(other.Id)!;
        ambient.Force("storm"); // what the orbit view would show

        server.Travel("Pilot", other.Id);

        Assert.Equal(other.Id, server.ActiveLocationId);
        Assert.Same(ambient, server.WeatherSimForTest); // the very same simulation, not a fresh one
        Assert.Equal("storm", server.Weather);
    }

    [Fact]
    public void SystemWeatherSnapshot_ListsEveryWeatherBodyOfTheSystem()
    {
        var transport = new RecordingTransport();
        var server = NewServer("snapshot", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        transport.Sent.Clear();
        server.EnterSpace("Pilot");

        var msg = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).OfType<SystemWeather>().LastOrDefault();
        Assert.NotNull(msg);
        var home = server.Galaxy.FindBody(server.ActiveLocationId)!;
        Assert.Equal(home.SystemId, msg!.SystemId);
        var expected = server.Galaxy.AllBodies()
            .Where(b => b.SystemId == home.SystemId
                && b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
                && _content.GetPlanet(b.PlanetType ?? string.Empty) is { Void: false })
            .Select(b => b.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, msg.Bodies.Select(b => b.BodyId).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var self = msg.Bodies.Single(b => b.BodyId == home.Id);
        Assert.True(self.ClockRunning, "the loaded body's clock runs");
        Assert.Equal(server.Weather, self.State);
    }

    [Fact]
    public void PadWeather_IsTheWeatherTheSurfaceReportsAfterLanding()
    {
        var transport = new RecordingTransport();
        var server = NewServer("padweather", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        var other = NeighbourWithWeather(server);
        server.Tick(1.1);
        server.BodyWeatherSimForTest(other.Id)!.Force("rain"); // a ladder state: biomes/fronts/summits shift it per pad

        transport.Sent.Clear();
        server.RequestLandingPadsForTest(pilot, other.Id);
        var list = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).OfType<LandingPadList>().Last();
        Assert.NotEmpty(list.Pads);
        Assert.All(list.Pads, p => Assert.False(string.IsNullOrEmpty(p.Weather)));
        Assert.Contains(transport.Sent, x => x.Conn == pilot.ConnectionId && x.Msg is SystemWeather);

        server.Travel("Pilot", other.Id);
        var pos = pilot.State.Position;
        var pad = list.Pads.OrderBy(p => (p.X - pos.X) * (p.X - pos.X) + (p.Z - pos.Z) * (p.Z - pos.Z)).First();
        var (state, _) = server.WeatherAtForTest(new Vector3f(pad.X, pos.Y, pad.Z));
        Assert.Equal(pad.Weather, state);
    }

    [Fact]
    public void LandingMapClock_OfABodyAnotherPlayerHolds_IsThatWorldsOwnClock()
    {
        var transport = new RecordingTransport();
        var server = NewServer("clock", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        server.AddLocalPlayer("Holder");
        var other = NeighbourWithWeather(server);
        server.Travel("Holder", other.Id);
        Assert.True(server.HasWorldLoadedForTest(other.Id));
        for (int i = 0; i < 6; i++)
        {
            server.Tick(10.0); // 60 s on the held world's clock
        }

        transport.Sent.Clear();
        server.RequestLandingPadsForTest(pilot, other.Id);
        var list = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).OfType<LandingPadList>().Last();
        Assert.NotEqual(0.35f, list.TimeOfDay, 3); // #2171: no longer the activation default
    }

    [Fact]
    public void OrbitalClock_AdvancesOncePerTick_EvenWithTwoWorldsLoaded()
    {
        var server = NewServer("sysclock", new RecordingTransport());
        server.AddLocalPlayer("Pilot");
        server.AddLocalPlayer("Holder");
        server.Travel("Holder", NeighbourWithWeather(server).Id);
        double before = server.SystemTimeDaysForTest;
        server.Tick(60.0);
        Assert.Equal(0.1, server.SystemTimeDaysForTest - before, 6); // 60 s of a 600 s system-day — once, not per world
    }

    [Fact]
    public void SkyAndCloudTints_AreTheSharedFunctions()
    {
        var server = NewServer("tints", new RecordingTransport());
        server.AddLocalPlayer("Pilot");
        var bodies = new List<CelestialBody> { server.Galaxy.FindBody(server.ActiveLocationId)!, NeighbourWithWeather(server) };
        foreach (var body in bodies)
        {
            if (body.Id != server.ActiveLocationId)
            {
                server.Travel("Pilot", body.Id);
            }

            var planet = _content.GetPlanet(body.PlanetType!);
            int sky = AtmosphereTints.SkyRgb(1, body.Id, planet);
            Assert.Equal(sky, server.SkyColor);
            Assert.Equal(AtmosphereTints.CloudRgb(1, body.Id, planet, sky), server.CloudColorForTest);
        }
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — a straggling handle on Windows must not fail the suite
        }
    }
}

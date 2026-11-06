// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The server wiring of the mist profile (#3269): the environment message carries the active world's profile — the shared
/// function of (save seed, location, type, air density) — and the weather's summit line is the base of that world's cloud
/// band, so the summit you see standing in the clouds is the one that reads wetter.
/// </summary>
public sealed class MistProfileServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_mist_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    private SvGameServer NewServer(string name, string planet, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>The newest environment message after a heartbeat.</summary>
    private static WorldEnvironment LastEnvironment(SvGameServer server, RecordingTransport transport)
    {
        server.AddLocalPlayer("Pilot");
        transport.Sent.Clear();
        server.Tick(6.0); // past the 5 s environment heartbeat
        return transport.Messages().OfType<WorldEnvironment>().Last();
    }

    [Fact]
    public void Environment_CarriesTheActiveWorldsMistProfile()
    {
        var transport = new RecordingTransport();
        var server = NewServer("mist_env", "jungle", transport);
        var env = LastEnvironment(server, transport);

        var planet = _content.GetPlanet(server.Galaxy.FindBody(server.ActiveLocationId)!.PlanetType!)!;
        // #3272: the visible band is placed against the world's measured relief (the generator's calibration).
        server.World.ApplyGeneratorMode();
        var terrain = server.World.Generator.SurfaceAltitudes(server.World.Planet);
        var expected = MistProfiles.For(1, server.ActiveLocationId, planet, server.AtmosphereDensity, airless: false, terrain);
        Assert.True(expected.Amount > 0f, "a jungle world breeds mist");
        Assert.Equal(expected.Amount, env.MistAmount);
        Assert.Equal(expected.ColorRgb, env.MistColor);
        Assert.Equal(expected.Wispiness, env.MistWispiness);
        Assert.Equal(expected.CloudBaseY, env.CloudBaseY);
        Assert.Equal(expected.CloudTopY, env.CloudTopY);
        Assert.True(env.CloudTopY > env.CloudBaseY + 4f, "a cloudy world has a band");
        Assert.True(env.CloudBaseY >= terrain.MedianY + MistProfiles.MinBandClearance, "the band floats well above the median ground");
        Assert.True(env.CloudBaseY >= terrain.UpperY + 4, "only the land above the upper tenth reaches into the band");

        // The summits-are-wetter line is the shared type-relief line the pad weather and the orbit map use too.
        Assert.Equal(MistProfiles.SummitLineY(1, server.ActiveLocationId, planet), server.CloudLineYForTest);
    }

    [Fact]
    public void AnAirlessWorld_SendsNoMist_AndKeepsTheClassicCloudLine()
    {
        var transport = new RecordingTransport();
        var server = NewServer("mist_airless", "crystal", transport);
        var env = LastEnvironment(server, transport);

        var planet = _content.GetPlanet(server.Galaxy.FindBody(server.ActiveLocationId)!.PlanetType!)!;
        Assert.True(planet.IsAirless);
        Assert.Equal(0f, env.MistAmount);
        Assert.Equal(-1, env.MistColor);
        Assert.True(env.CloudTopY <= env.CloudBaseY, "no cloud band without air");
        Assert.Equal(WeatherProjection.CloudLineY(planet), server.CloudLineYForTest);
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort — a straggling handle on Windows must not fail the suite
        }
    }
}

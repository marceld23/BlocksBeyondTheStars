// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// What the server contributes to the hyperspace chart (#1603): the star map carries every system's real
/// star colour (#1604), and the finale system is placed out past every other star instead of at a nominal
/// corner of the procedural box (#1605).
/// </summary>
public sealed class HyperspaceChartServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HyperspaceChartServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hyperchart_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void StarMap_CarriesEverySystemsStarColour_MatchingTheSunOfTheCurrentWorld()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "col"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        StarMapData? map = null;
        WorldEnvironment? env = null;
        client.PayloadReceived += p =>
        {
            switch (NetCodec.Decode(p))
            {
                case StarMapData m: map = m; break;
                case WorldEnvironment e: env = e; break;
            }
        };

        var config = new ServerConfig { WorldName = "col", Seed = 5, AutoSaveIntervalMinutes = 9999 };
        config.World.StarSystemCount = 6;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Send(NetCodec.Encode(new RequestStarMap()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(map);
        Assert.NotNull(env);
        Assert.All(map!.Systems, s => Assert.NotEqual(0, s.StarColor));
        Assert.True(map.Systems.Select(s => s.StarColor).Distinct().Count() > 1, "six systems should not all share one star colour");

        // The colour of the system the player is in equals the sun colour the planet sky received on join —
        // one source of truth, so the chart's star matches the sun seen after landing.
        var here = map.Systems.Single(s => s.Bodies.Any(b => b.Id == map.ActiveLocationId));
        Assert.Equal(env!.SunColor, here.StarColor);
        Assert.Equal(SvGameServer.StarColor(here.Name), here.StarColor); // keyed by NAME, as the weather does
    }

    [Fact]
    public void StarColour_RoundTripsThroughTheCodec()
    {
        var msg = new StarMapData
        {
            Systems = new[] { new NetStarSystem { Id = "sys3", Name = "Kel", MapX = 1f, MapY = 2f, Tier = 2, StarColor = 0xFFC97E } },
        };
        var back = Assert.IsType<StarMapData>(NetCodec.Decode(NetCodec.Encode(msg)));
        Assert.Equal(0xFFC97E, back.Systems[0].StarColor);
        Assert.Equal(2, back.Systems[0].Tier);
    }

    [Theory]
    [InlineData(42, 8, 0)]
    [InlineData(42, 8, 6)]
    [InlineData(7, 12, 3)]
    [InlineData(2026, 1, 0)]
    public void FinalePosition_LiesBeyondEveryOtherStar_AndIsSeedStable(long seed, int systems, int grown)
    {
        var desc = new WorldDescription { StarSystemCount = systems, SystemVariance = true };
        var galaxy = new UniverseGenerator(seed, desc, _content).Generate(systems + grown);
        var home = galaxy.Systems[0];

        var (x, y) = SvGameServer.GuardianFinaleMapPosition(galaxy);
        float finaleReach = MathF.Sqrt((x - home.MapX) * (x - home.MapX) + (y - home.MapY) * (y - home.MapY));
        Assert.True(finaleReach >= SvGameServer.GuardianFinaleMapDistance - 0.5f, $"finale sits only {finaleReach:F0} units out");
        foreach (var s in galaxy.Systems)
        {
            float dx = s.MapX - home.MapX, dy = s.MapY - home.MapY;
            Assert.True(MathF.Sqrt(dx * dx + dy * dy) < finaleReach, $"{s.Id} lies farther out than the finale");
        }

        var again = SvGameServer.GuardianFinaleMapPosition(new UniverseGenerator(seed, desc, _content).Generate(systems + grown));
        Assert.Equal((x, y), again);
    }

    [Fact]
    public void RevealedFinale_IsFrontierTier_AndOutsideEveryOtherSystem()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fin"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fin", Seed = 4242, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Pilot");
        for (int i = 0; i < 300 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        Assert.True(server.GalaxyHasGuardianSystemForTest);
        var finale = server.Galaxy.Systems.Single(s => s.Id == SvGameServer.GuardianFinaleSystemId);
        var home = server.Galaxy.Systems[0];
        float finaleReach = MathF.Sqrt((finale.MapX - home.MapX) * (finale.MapX - home.MapX) + (finale.MapY - home.MapY) * (finale.MapY - home.MapY));
        foreach (var s in server.Galaxy.Systems.Where(s => s.Id != finale.Id))
        {
            float dx = s.MapX - home.MapX, dy = s.MapY - home.MapY;
            Assert.True(MathF.Sqrt(dx * dx + dy * dy) < finaleReach);
        }

        Assert.Equal(2, server.FrontierTierForTest(finale.Id));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}

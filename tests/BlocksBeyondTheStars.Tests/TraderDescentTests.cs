// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3824 (a young player's report: "I cannot see where traders land"): a trader that lands flies to its destination
/// body in the flight scene, stops where a player ship may land and dives into the body — every pilot there gets the
/// dive to draw and a line that says who lands where. It used to head for a random point around the scene centre and
/// leave with the warp flash of a ship that jumps away.
/// </summary>
public sealed class TraderDescentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_traderdescent_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { /* best effort */ }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off; // traders are independent of combat
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static float Distance(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>A pilot in flight over the body they stood on, and that body's id.</summary>
    private static (PlayerSession Pilot, string Home) Flying(SvGameServer server)
    {
        var pilot = server.AddLocalPlayer("Pilot");
        string home = pilot.CurrentLocationId;
        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        return (pilot, home);
    }

    [Fact]
    public void TheLaunchBody_HangsWhereTheFlightSceneDrawsIt()
    {
        var server = NewServer("scene", new RecordingTransport());
        var (_, home) = Flying(server);

        var spot = server.TraderBodyInSceneForTest("Pilot", home);
        Assert.NotNull(spot);
        Assert.Equal(new Vector3f(SystemBodyLayout.HomeX, SystemBodyLayout.HomeY, SystemBodyLayout.HomeZ), spot!.Value.Centre);
        Assert.True(spot.Value.Radius > 20f, "the launch body is drawn big");

        // A body of another system is not in this scene: a trader bound for it keeps its inner waypoint.
        var homeBody = server.Galaxy.FindBody(home)!;
        var elsewhere = server.Galaxy.Systems.Where(s => s.Id != homeBody.SystemId).SelectMany(s => s.Bodies).First();
        Assert.Null(server.TraderBodyInSceneForTest("Pilot", elsewhere.Id));
    }

    [Fact]
    public void EveryOtherBodyOfTheSystem_HasASpotClearOfTheLaunchBody()
    {
        var server = NewServer("others", new RecordingTransport());
        var (_, home) = Flying(server);
        var homeBody = server.Galaxy.FindBody(home)!;
        var homeSpot = server.TraderBodyInSceneForTest("Pilot", home)!.Value;

        var others = server.Galaxy.Systems.First(s => s.Id == homeBody.SystemId).Bodies
            .Where(b => b.Id != home && b.Kind is CelestialKind.Planet or CelestialKind.Moon)
            .ToList();
        foreach (var b in others)
        {
            var spot = server.TraderBodyInSceneForTest("Pilot", b.Id);
            Assert.NotNull(spot);
            Assert.True(spot!.Value.Radius > 4f);
            if (b.ParentId == home)
            {
                // A moon of the launch body is drawn outside its surface, in its plane.
                Assert.Equal(homeSpot.Centre.Y, spot.Value.Centre.Y);
                Assert.True(Distance(spot.Value.Centre, homeSpot.Centre) >= SystemBodyLayout.MinOrbitFor(homeSpot.Radius, spot.Value.Radius) - 0.01f);
            }
        }
    }

    [Fact]
    public void LaunchedFromAMoon_ItsPlanetHangsClearOfTheClimb_AsTheClientDrawsIt()
    {
        // #3919: the flight scene of a first moon drew its planet at the star map's moon orbit, right over the launch
        // column — the climb ended inside the planet's keep-out shell. The server sends its landing traders to the same
        // spot the client draws, so it must apply the client's rule (SystemBodyLayout.HomeParentPosition).
        var server = NewServer("moon", new RecordingTransport());
        var pilot = server.AddLocalPlayer("Pilot");
        var moon = server.Galaxy.Systems.SelectMany(s => s.Bodies).First(b => b.Kind == CelestialKind.Moon
            && b.Id.EndsWith("-m0", StringComparison.Ordinal)
            && server.Galaxy.FindBody(b.ParentId)!.SystemId == b.SystemId
            && server.Galaxy.Systems.First(s => s.Id == b.SystemId).Bodies.Count(o => o.ParentId == b.ParentId) >= 2);
        pilot.CurrentLocationId = moon.Id;
        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));

        var homeSpot = server.TraderBodyInSceneForTest("Pilot", moon.Id)!.Value;
        float moonRadius = homeSpot.Radius / SystemBodyLayout.HomeDiameterFactor;
        var planet = server.Galaxy.FindBody(moon.ParentId)!;
        var spot = server.TraderBodyInSceneForTest("Pilot", planet.Id)!.Value;
        float fromColumn = (float)System.Math.Sqrt((spot.Centre.X * spot.Centre.X) + (spot.Centre.Z * spot.Centre.Z));

        Assert.Equal(0f, spot.Centre.Y); // in the flight plane, like every other planet
        Assert.True(
            fromColumn >= spot.Radius + SystemBodyLayout.KeepOutMargin + SystemBodyLayout.LaunchColumnClearance,
            $"the planet sits {fromColumn:0.0} from the launch column, its shell reaches {spot.Radius + SystemBodyLayout.KeepOutMargin:0.0}");
        const float scale = SystemBodyLayout.FlightViewScale;
        var (x, z) = SystemBodyLayout.HomeParentPosition(
            (planet.SystemX - moon.SystemX) * scale, (planet.SystemZ - moon.SystemZ) * scale, spot.Radius, moonRadius);
        Assert.Equal(x, spot.Centre.X, 3); // the client's rule: its true direction, at the clear distance
        Assert.Equal(z, spot.Centre.Z, 3);

        // The launch moon holds the planet's first orbit slot: its siblings ring above it, clear of the climb too.
        foreach (var sibling in server.Galaxy.Systems.First(s => s.Id == moon.SystemId).Bodies
                     .Where(b => b.ParentId == planet.Id && b.Id != moon.Id))
        {
            var s = server.TraderBodyInSceneForTest("Pilot", sibling.Id)!.Value;
            Assert.True(Distance(s.Centre, spot.Centre) >= SystemBodyLayout.NextMoonOrbit(spot.Radius, s.Radius, fromColumn, moonRadius) - 0.01f);
            float sibFromColumn = (float)System.Math.Sqrt((s.Centre.X * s.Centre.X) + (s.Centre.Z * s.Centre.Z));
            Assert.True(sibFromColumn >= s.Radius + SystemBodyLayout.KeepOutMargin, $"{sibling.Id}'s shell holds the launch column");
        }

        // A trader bound for the planet still stops where a ship may land on it.
        Assert.True(server.SpawnLandingTraderForTest("Pilot", planet.Id));
        var route = Assert.Single(server.TraderRoutesForTest("Pilot"), r => r.DestBodyId == planet.Id);
        Assert.Equal(SystemBodyLayout.LandApproachDistance(spot.Radius), Distance(route.Target, spot.Centre), 2);
    }

    [Fact]
    public void ALandingTrader_HeadsForItsBody_AndStopsWhereAShipMayLand()
    {
        var server = NewServer("aim", new RecordingTransport());
        var (_, home) = Flying(server);

        Assert.True(server.SpawnLandingTraderForTest("Pilot", home));
        var route = Assert.Single(server.TraderRoutesForTest("Pilot"), r => r.DestBodyId == home);
        var spot = server.TraderBodyInSceneForTest("Pilot", home)!.Value;

        float reach = SystemBodyLayout.LandApproachDistance(spot.Radius);
        Assert.Equal(reach, Distance(route.Target, spot.Centre), 2);
        // On the side it comes from: the approach point lies on the line from the body to the trader.
        Assert.True(Distance(route.Position, route.Target) < Distance(route.Position, spot.Centre));
        Assert.False(route.Descending);
    }

    [Fact]
    public void AtItsBody_TheTraderDives_AndLeavesTheFlightWithoutAWarpFlash()
    {
        var transport = new RecordingTransport();
        var server = NewServer("dive", transport);
        var (pilot, home) = Flying(server);
        Assert.True(server.SpawnLandingTraderForTest("Pilot", home));
        var approach = server.TraderRoutesForTest("Pilot").Single(r => r.DestBodyId == home).Target;

        // It cruises to the approach point (a few hundred units at 22 units a second).
        transport.Sent.Clear();
        bool diving = false;
        for (int i = 0; i < 600 && !diving; i++)
        {
            server.Tick(0.1);
            diving = server.TraderRoutesForTest("Pilot").Any(r => r.Descending);
        }

        Assert.True(diving, "the trader never began its dive");
        Assert.Equal(1, server.LandedTraderCountForTest()); // the pad is reserved when the dive begins

        var mine = transport.Sent.Where(x => x.Conn == pilot.ConnectionId).Select(x => x.Msg).ToList();
        var fx = Assert.Single(mine.OfType<SpaceWarpFx>(), f => f.Style == SvGameServer.TraderDescentStyle);
        Assert.StartsWith("npc:", fx.Subject, StringComparison.Ordinal);
        Assert.Equal(home, fx.Body);
        Assert.Equal((float)SvGameServer.TraderDescentSeconds, fx.Seconds);
        Assert.True(Distance(new Vector3f(fx.X, fx.Y, fx.Z), approach) < 7f, "the dive starts at the approach point");
        Assert.Contains(mine.OfType<ServerMessage>(), m => m.Text.Contains("landing on", StringComparison.Ordinal));

        // The pose stays for the dive, then the trader is gone — and no departure flash was shown at its spot.
        var diver = server.TraderRoutesForTest("Pilot").Single(r => r.Descending);
        for (int i = 0; i < 60; i++)
        {
            server.Tick(0.1);
        }

        Assert.DoesNotContain(server.TraderRoutesForTest("Pilot"), r => r.Descending);
        Assert.DoesNotContain(
            transport.Sent.Select(x => x.Msg).OfType<SpaceWarpFx>(),
            f => f.Style.Length == 0 && !f.Arriving && Distance(new Vector3f(f.X, f.Y, f.Z), diver.Position) < 1f);
    }
}

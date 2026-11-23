// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The intercity line split into planner and builder (#3984): a route handed in as three stops by position — the middle
/// one a through station, a waypoint on the second leg — planned by the given planner and built by the same builder as the
/// rolled line, on a flat world: three stations, one rail line through every pylon with all three stops on it, its records
/// under its own line index, and a public train on it.
/// </summary>
public sealed class IntercityGivenRouteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_givenroute_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    [Fact]
    public void AThreeStopGivenRoute_IsPlannedAndBuilt_OneLineThroughEveryStop()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "given_route"));
        var config = new ServerConfig
        {
            WorldName = "given_route",
            Seed = 1,
            StartPlanet = "salt_flats", // flat, no water
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceIntercityRail = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();

        var (padX, _, padZ) = server.LandingPadForTest(0);
        var route = new SvGameServer.IntercityGivenRoute(lineIndex: 1);
        route.Stops.Add(new SvGameServer.IntercityGivenStop("West", padX + 60, padZ + 40));
        route.Stops.Add(new SvGameServer.IntercityGivenStop("Middle", padX + 200, padZ + 40));
        var east = new SvGameServer.IntercityGivenStop("East", padX + 360, padZ + 40);
        east.Via.Add((padX + 280, padZ + 90));
        route.Stops.Add(east);

        var plan = server.PlaceGivenIntercityRouteForTest(route, SvGameServer.IntercityLimits.Rolled(WorldDescription.CurrentTerrainGeneration), out string why);
        Assert.True(plan is not null, why);
        Assert.Equal(new[] { 0, 0, 1 }, plan!.Stops.Select(s => s.Heading)); // toward the next stop; the last one back toward the waypoint

        // The builder made it the world's line: three stations, their stops on the line, every free pylon standing.
        var built = Assert.Single(server.IntercityLinesForTest);
        Assert.Equal(1, built.Index);
        Assert.Equal(new[] { "West", "Middle", "East" }, built.Stations.Select(s => s.SettlementName));
        var stopId = _content.GetBlock(RailRules.StopBlockKey)!.NumericId;
        var pylonId = _content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
        foreach (var st in built.Stations)
        {
            Assert.Equal(stopId, server.World.GetBlock(st.Stop));
            Assert.Contains(st.EndPylon, built.Pylons);
            Assert.Contains(st.ExitPylon, built.Pylons);
        }

        Assert.All(built.Pylons, p => Assert.Equal(pylonId, server.World.GetBlock(p)));
        for (int j = 1; j < built.Pylons.Count; j++)
        {
            Assert.True(server.RailLinkClearForTest(built.Pylons[j - 1], built.Pylons[j]), $"the corridor between pylons {j - 1} and {j} is clear");
        }

        var line = Assert.Single(server.RailLinesForTest());
        Assert.False(line.Closed);
        Assert.Equal(3, line.Stops);
        Assert.Equal(built.Pylons.ToHashSet(), line.Pylons.ToHashSet());

        // Pinned under its own line index, its stations under that line's record band; a public train runs on it.
        var recs = server.PlacementRecordsForTest;
        Assert.Contains(recs, r => r.Kind == "rail_line" && r.Index == 1 && r.Placed && r.Composition!.Count == built.Pylons.Count);
        Assert.Equal(new[] { 64, 65, 66 }, recs.Where(r => r.Kind == "rail_station" && r.Placed).Select(r => r.Index).OrderBy(i => i));
        var train = Assert.Single(server.TrainsForTest());
        Assert.True(RailRules.IsPublic(train.OwnerId));
        Assert.Equal(line.Id, train.LineId);
        server.Stop();
    }
}

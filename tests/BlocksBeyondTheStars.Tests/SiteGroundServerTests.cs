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
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 40 on a real server (#3564, #3569, #3571, #3572): a body stamped for the first time reserves the ground
/// of its structures, pins the list in the save and reads it back before any chunk on the next load; the city world and the
/// landmarks take part; the placement search tries the candidates a kind likes first; an older world reserves nothing.
/// </summary>
public sealed class SiteGroundServerTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sites_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;

    public SiteGroundServerTests(ITestOutputHelper output) => _out = output;

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

    private SvGameServer Start(string name, string planet, long seed, int generation, out SqliteWorldRepository repo, double landmarkChance = 0.0)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            LandmarkChance = landmarkChance,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AFreshWorld_ReservesItsStructuresGround_PinsIt_AndReadsItBackOnTheNextLoad()
    {
        var server = Start("fresh", "meadowlands", 4711, WorldDescription.GeographyGeneration, out var repo);
        var sites = server.SiteFlatsForTest.ToList();
        string body = server.PlacementRecordsForTest.First().LocationId;
        string? pinned = server.PinnedSitesForTest(body);
        var heights = sites.Select(s => server.SurfaceHeightForTest(s.MinX + (s.Width / 2), s.MinZ + (s.Length / 2))).ToList();
        server.Stop();
        repo.Dispose();
        _out.WriteLine($"{sites.Count} sites: {pinned}");

        Assert.NotEmpty(sites);
        Assert.Equal(SiteFlat.Format(sites), pinned);
        for (int i = 0; i < sites.Count; i++)
        {
            Assert.InRange(heights[i], sites[i].FloorY - sites[i].MaxFill, sites[i].FloorY + sites[i].MaxCut);
        }

        // Every reserved site is a stamped structure's box: a settlement's record stands at a site's corner.
        var settlements = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed && r.Seat is "flat" or "slope" or "shelf").ToList();
        Assert.All(settlements, r => Assert.Contains(sites, s => s.MinX == r.X && s.MinZ == r.Z && s.FloorY == r.GroundY));

        var again = Start("fresh", "meadowlands", 4711, WorldDescription.GeographyGeneration, out var repo2);
        try
        {
            Assert.True(SiteFlat.SameList(sites, again.SiteFlatsForTest.ToList()));
            Assert.Equal(heights, sites.Select(s => again.SurfaceHeightForTest(s.MinX + (s.Width / 2), s.MinZ + (s.Length / 2))).ToList());
        }
        finally
        {
            again.Stop();
            repo2.Dispose();
        }
    }

    [Fact]
    public void AnOlderWorld_ReservesNothing()
    {
        var server = Start("older", "meadowlands", 4711, WorldDescription.SchoolClubLandmarksGeneration, out var repo);
        try
        {
            Assert.Empty(server.SiteFlatsForTest);
            Assert.Null(server.PinnedSitesForTest(server.PlacementRecordsForTest.First().LocationId));
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    [Fact]
    public void TheCityWorld_StandsOnReservedGround_AndItsPadsStayLevel()
    {
        var server = Start("city", "gds_desert", 20261113, WorldDescription.GeographyGeneration, out var repo);
        try
        {
            var city = server.PlacementRecordsForTest.Single(r => r.Kind == "settlement" && r.Index == 0);
            var site = Assert.Single(server.SiteFlatsForTest, s => s.MinX == city.X && s.MinZ == city.Z);
            Assert.Equal(city.GroundY, site.FloorY);
            Assert.True(site.Radius > Content.StructureTypes.Get(StructureTypeRules.Settlement).Grade.Radius, "the city grades a wider ring");
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    [Fact]
    public void ALandmark_SeatedOnTheGround_ReservesIt()
    {
        int checkedLandmarks = 0;
        foreach (long seed in new[] { 90210L, 4711L, 31337L, 777L, 2026L, 99L })
        {
            var server = Start("landmark" + seed, "meadowlands", seed, WorldDescription.GeographyGeneration, out var repo, landmarkChance: 1.0);
            try
            {
                var landmark = server.PlacementRecordsForTest.FirstOrDefault(r => r.Kind == LandmarkRules.RecordKind && r.Placed);
                _out.WriteLine($"seed {seed}: landmark seat {landmark?.Seat ?? "none"}");
                if (landmark is null || landmark.Seat is not ("flat" or "slope" or "shelf"))
                {
                    continue; // a span across water, or none: no ground to reserve
                }

                Assert.Contains(server.SiteFlatsForTest, s => s.MinX == landmark.X && s.MinZ == landmark.Z
                    && s.Radius == Content.StructureTypes.Get(StructureTypeRules.Landmark).Grade.Radius);
                checkedLandmarks++;
            }
            finally
            {
                server.Stop();
                repo.Dispose();
            }

            if (checkedLandmarks >= 2)
            {
                break;
            }
        }

        Assert.True(checkedLandmarks > 0, "no seed carried a landmark seated on the ground");
    }

    [Fact]
    public void TheSearch_PlacesSettlementsWhereTheyLikeToStand()
    {
        var server = Start("search", "meadowlands", 4711, WorldDescription.GeographyGeneration, out var repo);
        try
        {
            var settlements = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
            Assert.NotEmpty(settlements);
            double chosen = settlements.Average(r => server.SiteScoreForTest(StructureTypeRules.Settlement, r.X + 8, r.Z + 8));

            // The field the search draws from: random spots in the same ring around pad 0.
            var rng = new Random(7);
            double field = Enumerable.Range(0, 200).Select(_ =>
            {
                double a = rng.NextDouble() * Math.PI * 2;
                int d = 40 + rng.Next(0, 2000);
                return server.SiteScoreForTest(StructureTypeRules.Settlement, (int)(Math.Cos(a) * d), (int)(Math.Sin(a) * d) / 3);
            }).Average();
            _out.WriteLine($"{settlements.Count} settlements: mean score {chosen:F3} against {field:F3} for the field");
            Assert.True(chosen > field, $"settlements scored {chosen:F3}, the field {field:F3}");
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    [Fact]
    public void TheSiteBand_IsFullInside_AndFadesOutside()
    {
        Assert.Equal(1.0, SvGameServer.Band(50, 20, 120));
        Assert.Equal(1.0, SvGameServer.Band(20, 20, 120));
        Assert.True(SvGameServer.Band(10, 20, 120) is > 0.0 and < 1.0);
        Assert.Equal(0.0, SvGameServer.Band(400, 20, 120));
    }
}

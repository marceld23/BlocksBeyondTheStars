// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3639–#3641: the civilization stage on a real server with test-made civilizations (independent of the shipped ones): a
/// home body stamps its sites once into structure overlays and replays them from their records; a sunken site writes water,
/// never air, below the sea — except its air pocket; a prop stands where its marker was; a home body gives up its landmark;
/// a world older than terrain generation 43 has no homes and no sites.
/// </summary>
public sealed class CivilizationStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_civ_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    /// <summary>Every body of the start type in the start system is a home (so the start body is one).</summary>
    private static CivilizationDefinition HomeCiv(string key, string type, bool underwater, params (string Pool, int Count)[] sites)
    {
        var civ = CivilizationFrameworkTests.Civ(key, types: new[] { type }, min: 8, max: 8, perSystem: 0, start: CivilizationRules.StartOnly,
            underwater: underwater);
        civ.Sites = sites.Select(s => new CivilizationSitePool { Pool = s.Pool, Count = new List<int> { s.Count, s.Count } }).ToList();
        civ.HomeBody.Suppress.Add(StructureTypeRules.Landmark);
        return civ;
    }

    private static StructureTemplate Box(string key, string civ, string pool, int w, int h, int l, string seat = "")
        => new() { Key = key, Name = key, Civilization = civ, CivPool = pool, Width = w, Height = h, Length = l, Seat = seat, Ruin = LandmarkRules.RuinNever };

    private static void Fill(StructureTemplate t, int x0, int y0, int z0, int x1, int y1, int z1, string kind, string id = "")
    {
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = kind, Id = id });
                }
            }
        }
    }

    /// <summary>A sunken hall: a stone floor, a low wall ring with a gap, a glass diving bell (open below) holding a 3 × 3 × 3
    /// air pocket, and a prop marker.</summary>
    private static List<StructureTemplate> SeaTemplates(string civ)
    {
        var hall = Box("sea_hall", civ, CivilizationRules.PoolGiant, 11, 10, 11, LandmarkRules.SeatSeabed);
        Fill(hall, 0, 0, 0, 10, 0, 10, "block", "stone");
        Fill(hall, 0, 1, 0, 10, 2, 0, "block", "stone");
        Fill(hall, 0, 1, 10, 10, 2, 10, "block", "stone");
        Fill(hall, 3, 4, 3, 7, 8, 3, "block", "glass");
        Fill(hall, 3, 4, 7, 7, 8, 7, "block", "glass");
        Fill(hall, 3, 4, 4, 3, 8, 6, "block", "glass");
        Fill(hall, 7, 4, 4, 7, 8, 6, "block", "glass");
        Fill(hall, 4, 8, 4, 6, 8, 6, "block", "glass");
        Fill(hall, 4, 5, 4, 6, 7, 6, CivilizationRules.AirKind);
        hall.Cells.Add(new TemplateCell { X = 9, Y = 1, Z = 5, Kind = "marker", Id = CivilizationRules.PropMarker, Config = "pool=bits" });
        var bit = Box("sea_bit", civ, CivilizationRules.PoolProp, 1, 2, 1);
        bit.PropPool = "bits";
        Fill(bit, 0, 0, 0, 0, 1, 0, "block", "stone");
        return new List<StructureTemplate> { hall, bit };
    }

    private SvGameServer Start(GameContent content, string name, out SqliteWorldRepository repo, string planet, int generation,
        Action<ServerConfig>? tweak = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = GiantWreckTestWorlds.Config(name, planet, generation, chance: 0);
        tweak?.Invoke(config);
        var server = new SvGameServer(config, content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    private static GameContent Content(CivilizationDefinition civ, List<StructureTemplate> templates)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var warnings = new List<string>();
        // #3643: the shipped legacy civilizations (the G.D.S., the Sounders, the SPS — tags only) stay loaded beside the test's,
        // so the content's tags still name a civilization and the loader has nothing to warn about.
        var civs = content.Civilizations.Where(c => !c.IsFramework).Append(civ).ToArray();
        content.SetCivilizations(civs, templates, warnings.Add);
        Assert.True(warnings.Count == 0, string.Join(" | ", warnings));
        return content;
    }

    [Fact]
    public void ASunkenSite_IsWaterNeverAir_ButItsAirPocket_AndIsReplayedFromItsRecord()
    {
        var content = Content(HomeCiv("test_sea", "ocean", underwater: true, ("giant", 1)), SeaTemplates("test_sea"));
        ushort water = content.GetBlock("water")!.NumericId.Value;
        const string Name = "civ_sea";
        Vector3i origin;
        byte[] blob;
        var server = Start(content, Name, out var repo, "ocean", WorldDescription.CivilizationsGeneration);
        using (repo)
        {
            string location = server.World.LocationId;
            Assert.Contains(location, server.CivilizationHomesForTest["test_sea"]);
            var site = Assert.Single(server.CivilizationSitesForTest);
            Assert.Equal("test_sea", site.Civilization);
            Assert.NotNull(site.WaterTop);
            Assert.False(site.Staffed);
            origin = site.Origin;
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == CivilizationRules.RecordKind("test_sea"));
            Assert.True(rec.Placed);
            Assert.Equal(LandmarkRules.SeatSeabed, rec.Seat);
            Assert.Equal(site.WaterTop, MarkerSettings.Parse(rec.KitLayout).GetInt(SvGameServer.CivilizationWaterPin, -1));
            blob = Assert.Single(repo.ListStructureOverlays(location), r => r.Key == CivilizationRules.FeatureKey("test_sea", 0)).Data;

            AssertSunken(server, site, water);
            var prop = Assert.Single(site.Structure.Markers, m => m.Type == CivilizationRules.PropMarker); // VEGA finds it there
            Assert.NotEqual(0, site.Structure.Get(prop.LocalPos.X, prop.LocalPos.Y, prop.LocalPos.Z)); // the prop stands on its cell
            Assert.NotEqual(0, site.Structure.Get(prop.LocalPos.X, prop.LocalPos.Y + 1, prop.LocalPos.Z));
            server.Stop();
        }

        var again = Start(content, Name, out var repo2, "ocean", WorldDescription.CivilizationsGeneration);
        using (repo2)
        {
            var site = Assert.Single(again.CivilizationSitesForTest);
            Assert.Equal(origin, site.Origin);
            Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == CivilizationRules.RecordKind("test_sea"));
            Assert.Equal(blob, Assert.Single(repo2.ListStructureOverlays(again.World.LocationId),
                r => r.Key == CivilizationRules.FeatureKey("test_sea", 0)).Data);
            AssertSunken(again, site, water);
        }
    }

    /// <summary>Every cell of the site at or below the water top holds a block, or water — never air — except the air pocket,
    /// which is air.</summary>
    private static void AssertSunken(SvGameServer server, SvGameServer.LandmarkInstance site, ushort water)
    {
        int top = site.WaterTop!.Value, pockets = 0;
        foreach (var c in GiantWreckTestWorlds.AllCells(site.Structure).Where(c => c.Y >= site.Layout.GroundRow))
        {
            var cell = GiantWreckTestWorlds.At(server, site, c);
            if (cell.Y > top)
            {
                continue;
            }

            ushort got = server.World.GetBlock(cell).Value;
            if (site.Layout.IsAirPocket(c.X, c.Y, c.Z))
            {
                Assert.Equal(0, got);
                pockets++;
            }
            else if (site.Structure.Get(c.X, c.Y, c.Z) == 0)
            {
                Assert.True(got == water, $"cell {c} of the sunken site holds {got}, not water");
            }
        }

        Assert.Equal(27, pockets);
    }

    [Fact]
    public void AHomeBodyOnLand_StampsItsSites_ApartAndUnstaffed_AndGivesUpItsLandmark()
    {
        var hall = Box("land_hall", "test_land", CivilizationRules.PoolGiant, 9, 5, 9);
        Fill(hall, 0, 0, 0, 8, 0, 8, "block", "stone");
        Fill(hall, 0, 1, 0, 0, 4, 8, "block", "stone");
        var hut = Box("land_hut", "test_land", CivilizationRules.PoolSmall, 5, 4, 5);
        Fill(hut, 0, 0, 0, 4, 3, 0, "block", "stone");
        var content = Content(HomeCiv("test_land", "meadowlands", underwater: false, ("giant", 1), ("small", 2)),
            new List<StructureTemplate> { hall, hut });
        var server = Start(content, "civ_land", out var repo, "meadowlands", WorldDescription.CivilizationsGeneration, c =>
        {
            c.PlaceLandmarks = true;
            c.LandmarkChance = 1.0;
        });
        using (repo)
        {
            Assert.Equal(3, server.CivilizationSitesForTest.Count);
            Assert.All(server.CivilizationSitesForTest, s => Assert.Null(s.WaterTop));
            Assert.All(server.CivilizationSitesForTest, s => Assert.False(s.Staffed));
            var rects = server.CivilizationSitesForTest.Select(s => s.Rect).ToList();
            for (int i = 0; i < rects.Count; i++)
            {
                for (int j = i + 1; j < rects.Count; j++)
                {
                    bool apart = Math.Abs(rects[i].Cx - rects[j].Cx) >= rects[i].Hw + rects[j].Hw || Math.Abs(rects[i].Cz - rects[j].Cz) >= rects[i].Hl + rects[j].Hl;
                    Assert.True(apart, $"sites {i} and {j} overlap");
                }
            }

            Assert.Empty(server.LandmarksForTest); // the home body gives its landmark up
            Assert.Contains(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind && !r.Placed);
        }
    }

    [Fact]
    public void AMazeEntry_IsBuiltByItsSource_AsARuinOnAToxicWorld_WithItsStatueMarkers()
    {
        var maze = Box("maze_s", "test_maze", CivilizationRules.PoolGiant, 0, 0, 0);
        (maze.Width, maze.Height, maze.Length) = (0, 0, 0);
        maze.Source = "maze_ruin";
        maze.SourceParams = "size=S";
        maze.Ruin = LandmarkRules.RuinAlways;
        maze.RuinStrength = 0.45;
        var content = Content(HomeCiv("test_maze", "rocky", underwater: false, ("giant", 1)), new List<StructureTemplate> { maze });
        var server = Start(content, "civ_maze", out var repo, "rocky", WorldDescription.CivilizationsGeneration);
        using (repo)
        {
            var site = Assert.Single(server.CivilizationSitesForTest);
            Assert.True(site.Layout.Ruined);
            Assert.Null(site.WaterTop);
            Assert.InRange(site.Structure.Width, 60, 100);
            Assert.Contains(site.Structure.Markers, m => m.Type == CivilizationRules.PropMarker && m.Data.Contains("statue_large", StringComparison.Ordinal));
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == CivilizationRules.RecordKind("test_maze"));
            Assert.Equal("maze_ruin", MarkerSettings.Parse(rec.KitLayout).Get(BlocksBeyondTheStars.WorldGeneration.Sources.SourceTemplates.PinKey));
            GiantWreckTestWorlds.AssertStandsAsBuilt(server, site);
        }
    }

    [Fact]
    public void AWorldBeforeGeneration43_HasNoHomes_AndNoSites()
    {
        var content = Content(HomeCiv("test_sea", "ocean", underwater: true, ("giant", 1)), SeaTemplates("test_sea"));
        var server = Start(content, "civ_old", out var repo, "ocean", WorldDescription.CivilizationsGeneration - 1);
        using (repo)
        {
            Assert.Empty(server.CivilizationHomesForTest);
            Assert.Empty(server.CivilizationSitesForTest);
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind.StartsWith(CivilizationRules.RecordKindPrefix, StringComparison.Ordinal));
        }
    }
}

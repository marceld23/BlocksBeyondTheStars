// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The school club's landmark rules (terrain generation 39): a bridge's span seat across water or a valley — in one of two
/// orientations, or not at all, and then the body takes another candidate — and the fixed landmarks of a planet type, stamped
/// on every body of it instead of the roll, each recorded under its own index and replayed. A generation-38 world takes neither.
/// </summary>
public sealed class LandmarkSchoolClubTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landmark_club_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        _repos.ForEach(r => r.Dispose());
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

    private static GameContent ContentWith(params StructureTemplate[] landmarks)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(landmarks);
        Assert.Equal(landmarks.Length, content.Landmarks.Count);
        return content;
    }

    private SvGameServer Start(GameContent content, string name, string planet = "meadowlands", long seed = 20261111,
        int generation = WorldDescription.SchoolClubLandmarksGeneration, double chance = 1.0)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            LandmarkChance = chance,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private void Stop(SvGameServer server)
    {
        server.Stop();
        _repos[^1].Dispose();
        _repos.RemoveAt(_repos.Count - 1);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static Vector3i At(SvGameServer server, SvGameServer.LandmarkInstance lm, Vector3i local)
        => WorldConstants.CanonicalBlock(lm.Cell(local), server.World.Circumference);

    /// <summary>A small intact bridge, 40 long (X) × 10 high × 5 wide: the deck on the ground row 4 (the deck row — its ends
    /// rest on the banks), a railing on it, two pairs of piers under it down to row 0 and their pylons above.</summary>
    private static StructureTemplate Bridge(string key = "test_bridge", int weight = 1)
    {
        const int W = 40, H = 10, L = 5, Deck = 4;
        var t = new StructureTemplate
        {
            Key = key,
            Name = "Test Bridge",
            Landmark = true,
            Ruin = LandmarkRules.RuinNever,
            Requires = new List<string> { LandmarkRules.RequiresAtmosphere },
            Width = W,
            Height = H,
            Length = L,
            GroundRow = Deck,
            Seat = LandmarkRules.SeatSpan,
            Weight = weight,
        };
        for (int x = 0; x < W; x++)
        {
            for (int z = 0; z < L; z++)
            {
                t.Cells.Add(LandmarkShapes.Block(x, Deck, z, "stone"));
            }

            t.Cells.Add(LandmarkShapes.Block(x, Deck + 1, 0, "stone"));
            t.Cells.Add(LandmarkShapes.Block(x, Deck + 1, L - 1, "stone"));
        }

        foreach (int px in new[] { 13, 14, 25, 26 })
        {
            for (int y = 0; y < Deck; y++)
            {
                for (int z = 1; z < L - 1; z++)
                {
                    t.Cells.Add(LandmarkShapes.Block(px, y, z, "stone"));
                }
            }

            for (int y = Deck + 2; y < H; y++)
            {
                t.Cells.Add(LandmarkShapes.Block(px, y, 0, "stone"));
                t.Cells.Add(LandmarkShapes.Block(px, y, L - 1, "stone"));
            }
        }

        Assert.Empty(LandmarkRules.Problems(t));
        return t;
    }

    // ---------------------------------------------------------------- the span seat

    [Fact]
    public void ABridge_SpansWaterOrAValley_InOneOfTwoOrientations_PinnedAndReplayed()
    {
        var content = ContentWith(Bridge());
        var server = Start(content, "club_bridge", planet: "river_lowlands");
        var lm = Assert.Single(server.LandmarksForTest);
        var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
        Assert.Equal(LandmarkRules.SeatSpan, rec.Seat);
        Assert.Equal(40, Math.Max(lm.Structure.Width, lm.Structure.Length)); // turned along X or along Z
        Assert.True(server.SpanSeatHoldsForTest(lm), "the bridge does not span a gap from bank to bank");

        // Every block of the bridge stands; under the middle of the deck no plinth was carried down into the gap.
        var s = lm.Structure;
        int g = lm.Layout.GroundRow;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != 0)
                    {
                        Assert.Equal(s.Get(x, y, z), server.World.GetBlock(At(server, lm, new Vector3i(x, y, z))).Value);
                    }
                }
            }
        }

        var middle = new Vector3i(s.Width / 2, g - 1, s.Length / 2); // the middle of the long axis, whichever way it runs
        Assert.Equal(0, s.Get(middle.X, middle.Y, middle.Z));
        Assert.NotEqual(content.GetBlock("stone")!.NumericId.Value, server.World.GetBlock(At(server, lm, middle)).Value);

        var origin = lm.Origin;
        var size = (s.Width, s.Length);
        Stop(server);
        var again = Start(content, "club_bridge", planet: "river_lowlands");
        var replayed = Assert.Single(again.LandmarksForTest);
        Assert.Equal((origin, size), (replayed.Origin, (replayed.Structure.Width, replayed.Structure.Length)));
        Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
    }

    [Fact]
    public void ABridge_OnAPlanetWithoutAGap_GivesWayToAnotherCandidate()
    {
        var content = ContentWith(Bridge(weight: 1000), LandmarkShapes.Tower(ruin: false));
        var server = Start(content, "club_no_gap", planet: "salt_flats");
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.Equal("test_tower", lm.Key);
        Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Template == "test_bridge");
    }

    [Fact]
    public void ATemplateOfGeneration39_NeverRollsOnAnOlderWorld()
    {
        var club = LandmarkShapes.Tower("club_tower", ruin: false);
        club.MinTerrainGeneration = WorldDescription.SchoolClubLandmarksGeneration;
        var old = Start(ContentWith(club), "club_min_38", generation: WorldDescription.StructurePipelineGeneration);
        Assert.Empty(old.LandmarksForTest);
        Assert.Equal("club_tower", Assert.Single(Start(ContentWith(club), "club_min_39").LandmarksForTest).Key);
    }

    // ---------------------------------------------------------------- fixed landmarks

    private static GameContent FixedContent(params string[] keys)
    {
        var content = ContentWith(LandmarkShapes.Tower("fixed_a", ruin: false), LandmarkShapes.Tower("fixed_b", size: 12, height: 24),
            LandmarkShapes.Tower("rolled", ruin: false));
        content.GetPlanet("meadowlands")!.FixedLandmarks = keys.ToList();
        return content;
    }

    [Fact]
    public void AFixedLandmarkType_GetsEveryOneOfThem_RecordedAndReplayed_InsteadOfTheRoll()
    {
        var content = FixedContent("fixed_a", "fixed_b");
        var server = Start(content, "club_fixed", chance: 0.0); // the roll says no — the fixed ones stand all the same
        Assert.Equal(new[] { "fixed_a", "fixed_b" }, server.LandmarksForTest.Select(l => l.Key));
        var records = server.PlacementRecordsForTest.Where(r => r.Kind == LandmarkRules.RecordKind).OrderBy(r => r.Index).ToList();
        Assert.Equal(new[] { (0, "fixed_a", true), (1, "fixed_b", true) }, records.Select(r => (r.Index, r.Template, r.Placed)));
        Assert.Empty(server.FootprintCollisionsForTest()); // the second keeps clear of the first
        var origins = server.LandmarksForTest.Select(l => l.Origin).ToList();

        Stop(server);
        var again = Start(content, "club_fixed", chance: 0.0);
        Assert.Equal(origins, again.LandmarksForTest.Select(l => l.Origin));
        Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
    }

    [Fact]
    public void AFixedLandmarkType_OnAGeneration38World_RollsAsBefore()
    {
        var server = Start(FixedContent("fixed_a", "fixed_b"), "club_fixed_38", generation: WorldDescription.StructurePipelineGeneration);
        var lm = Assert.Single(server.LandmarksForTest); // the one rolled landmark of the pool, the fixed list unread
        Assert.Equal(0, Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind).Index);
        Assert.Contains(lm.Key, new[] { "fixed_a", "fixed_b", "rolled" });

        var none = Start(FixedContent("fixed_a", "fixed_b"), "club_fixed_38_none", generation: WorldDescription.StructurePipelineGeneration,
            chance: 0.0);
        Assert.Empty(none.LandmarksForTest);
    }

    [Fact]
    public void AnUnknownFixedKey_IsReportedAtLoad_AndSkipped()
    {
        var content = FixedContent();
        content.GetPlanet("meadowlands")!.FixedLandmarks = new List<string> { "no_such_landmark", "fixed_b" };
        var warnings = new List<string>();
        content.SetLandmarks(content.Landmarks.ToList(), warnings.Add);
        Assert.Contains(warnings, w => w.Contains("no_such_landmark", StringComparison.Ordinal));
        Assert.Equal(new[] { (1, "fixed_b") }, content.FixedLandmarksFor(content.GetPlanet("meadowlands")).Select(f => (f.Index, f.Template.Key)));

        var server = Start(content, "club_fixed_unknown", chance: 0.0);
        var lm = Assert.Single(server.LandmarksForTest);
        Assert.Equal("fixed_b", lm.Key);
        Assert.Equal(1, Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind).Index);
    }
}

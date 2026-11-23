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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The given placement seat (#3980): a landmark template handed in at a fixed centre and quarter turn stands exactly there
/// (no search), its ground graded to the site height, pinned under a given place's record index, replayed after a restart
/// even when the place list no longer names it — and the rolled structures stamped after it keep clear of it.
/// </summary>
public sealed class GivenSeatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_given_" + Guid.NewGuid().ToString("N"));

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

    private static GameContent ContentWithTower()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(new[] { LandmarkShapes.Compacted(LandmarkShapes.Tower()) });
        return content;
    }

    /// <summary>A meadow world with the rolled landmark off; <paramref name="place"/> (asked during the boot, when the pads
    /// stand) names the given landmark's seat, null = no given place.</summary>
    private SvGameServer Start(GameContent content, string name, out SqliteWorldRepository repo, bool settlements,
        Func<SvGameServer, GivenSeat>? place)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 20261123,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceLandmarks = false,
        };
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        if (place is not null)
        {
            server.GivenLandmarkSource = _ => new List<SvGameServer.GivenLandmarkPlace> { new("test_tower", place(server)) };
        }

        server.Start();
        return server;
    }

    private static bool Overlaps(SvGameServer server, (int Cx, int Cz, int Hw, int Hl) a, int minX, int minZ, int maxX, int maxZ)
    {
        int dx = Math.Abs(WorldConstants.WrapDeltaX(a.Cx - ((minX + maxX) / 2), server.World.Circumference));
        return dx <= a.Hw + ((maxX - minX) / 2) && Math.Abs(a.Cz - ((minZ + maxZ) / 2)) <= a.Hl + ((maxZ - minZ) / 2);
    }

    [Fact]
    public void AGivenLandmark_StandsExactlyAtItsSeat_GradedAndPinned_AndReplaysAfterARestart()
    {
        var content = ContentWithTower();
        GivenSeat seat = default;
        var server = Start(content, "given_once", out var repo, settlements: false, s =>
        {
            var (padX, _, padZ) = s.LandingPadForTest(0);
            return seat = new GivenSeat(padX + 140, padZ + 30, turns: 1);
        });
        Vector3iSnapshot origin;
        using (repo)
        {
            var lm = Assert.Single(server.LandmarksForTest);
            Assert.Equal("test_tower", lm.Key);
            var (ox, oz) = seat.OriginFor(lm.Structure.Width, lm.Structure.Length);
            Assert.Equal((ox, oz), (lm.Origin.X, lm.Origin.Z)); // no search: the footprint is centred on the seat

            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Equal(SvGameServer.GivenLandmarkIndexBase, rec.Index);
            Assert.Equal(GivenSeat.SeatName, rec.Seat);
            Assert.Equal((ox, lm.GroundY, oz), (rec.X, rec.GroundY, rec.Z));
            Assert.Contains("turns=1", rec.KitLayout);

            // The ground under the footprint is graded to the site height by the generator's site flats …
            Assert.Contains(server.SiteFlatsForTest, f => f.MinX == ox && f.MinZ == oz && f.FloorY == lm.GroundY
                && f.Width == lm.Structure.Width && f.Length == lm.Structure.Length);

            // … and the box is in the footprint registry every later stamper asks.
            Assert.Contains(server.FootprintsForTest, f => f.Kind == StructureTypeRules.Landmark && f.Cx == lm.Rect.Cx && f.Cz == lm.Rect.Cz);
            origin = new Vector3iSnapshot(lm.Origin.X, lm.Origin.Y, lm.Origin.Z, lm.GroundY);
            server.Stop();
        }

        // A restart replays the record — also when the place list no longer names the place.
        var again = Start(content, "given_once", out var repo2, settlements: false, place: null);
        using (repo2)
        {
            var lm = Assert.Single(again.LandmarksForTest);
            Assert.Equal(origin, new Vector3iSnapshot(lm.Origin.X, lm.Origin.Y, lm.Origin.Z, lm.GroundY));
            Assert.Single(again.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Contains(again.FootprintsForTest, f => f.Kind == StructureTypeRules.Landmark && f.Cx == lm.Rect.Cx && f.Cz == lm.Rect.Cz);
            again.Stop();
        }
    }

    [Fact]
    public void RolledSettlements_KeepClearOfAGivenLandmark_StandingWhereOneWouldHaveStood()
    {
        var content = ContentWithTower();

        // Without the given place: where this seed's first settlement stands.
        var plain = Start(content, "given_plain", out var repo, settlements: true, place: null);
        int townX, townZ;
        using (repo)
        {
            var town = plain.SettlementBoxesForTest.First(s => !s.Ruined);
            (townX, townZ) = ((town.Min.X + town.Max.X) / 2, (town.Min.Z + town.Max.Z) / 2);
            plain.Stop();
        }

        // The same world with a given landmark centred there: it stands there, and no rolled structure overlaps it.
        var given = Start(content, "given_town", out var repo2, settlements: true, _ => new GivenSeat(townX, townZ));
        using (repo2)
        {
            var lm = Assert.Single(given.LandmarksForTest);
            Assert.Equal((townX, townZ), (lm.Origin.X + (lm.Structure.Width / 2), lm.Origin.Z + (lm.Structure.Length / 2)));
            Assert.NotEmpty(given.SettlementBoxesForTest);
            foreach (var s in given.SettlementBoxesForTest)
            {
                Assert.False(Overlaps(given, lm.Rect, s.Min.X, s.Min.Z, s.Max.X, s.Max.Z), $"a settlement at {s.Min} overlaps the given landmark");
            }

            Assert.DoesNotContain(given.FootprintCollisionsForTest(),
                c => c.A.Kind == StructureTypeRules.Landmark || c.B.Kind == StructureTypeRules.Landmark);
            given.Stop();
        }
    }

    private readonly record struct Vector3iSnapshot(int X, int Y, int Z, int GroundY);
}

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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3986: the SPS headquarters in the Amazon — its five pin-only templates, and the <c>guardian_giant</c> template marker:
/// the lying body turns with its landmark exactly, and the great hall stamps one fallen giant record that never wakes and
/// replays after a restart (its body written into the hall's overlay, protected with the hall).
/// </summary>
public sealed class SpsHeadquartersTests : IDisposable
{
    private static readonly string[] Keys =
    {
        "earth_sps_hq_main", "earth_sps_hq_hangar", "earth_sps_hq_servers", "earth_sps_hq_core", "earth_sps_hq_hall",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_spshq_" + Guid.NewGuid().ToString("N"));

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
    public void TheFiveTemplates_LoadPinOnly_TheHallAndTheCoreProtectedAndIntact_TheRestOpenRuins()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (string key in Keys)
        {
            var t = content.LandmarkByKey(key);
            Assert.NotNull(t);
            Assert.True(t!.PinOnly, key);
            Assert.Empty(LandmarkRules.Problems(t));
            bool sealedPart = key is "earth_sps_hq_hall" or "earth_sps_hq_core";
            Assert.Equal(!sealedPart, LandmarkRules.IsRuin(t));
            Assert.Equal(sealedPart, LandmarkRules.IsProtected(t, byKind: !LandmarkRules.IsRuin(t)));
        }

        Assert.DoesNotContain(content.LandmarksFor(null, "meadowlands", atmosphere: true, inhabited: true),
            t => Keys.Contains(t.Key)); // pin-only: no world rolls them
        var hall = content.LandmarkByKey("earth_sps_hq_hall")!;
        var giant = Assert.Single(hall.Cells, c => c.Kind == "marker" && c.Id == LandmarkRules.GuardianGiantMarker);
        Assert.False(MarkerSettings.Parse(giant.Config).GetBool(LandmarkRules.GuardianGiantWakeSetting)); // it never wakes
        Assert.True(RuinMarkerRules.IsKept(LandmarkRules.GuardianGiantMarker));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AGiantMarker_TurnsWithItsLandmark_CellForCell(int turns)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var t = FloorWithGiant(width: 30, length: 24, facing: 3);
        var unturned = GiantCells(LandmarkGenerator.Build(t, 7, 0, content, "grass"));
        var turned = GiantCells(LandmarkGenerator.Build(t, 7, turns, content, "grass"));

        var expected = unturned.Select(c =>
        {
            var (x, z) = LandmarkGenerator.Turn(c.X, c.Z, t.Width, t.Length, turns);
            return (x, c.Y, z, c.Block);
        }).ToHashSet();
        Assert.Equal(expected.Count, turned.Count);
        Assert.True(expected.SetEquals(turned), $"turn {turns}: the lying body is not the unturned one turned");
    }

    [Fact]
    public void TheGreatHall_StampsOneFallenGiantThatNeverWakes_AndReplaysItAfterARestart()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        GuardianGiantRecord giant;
        using (var repo = NewRepo("hall"))
        {
            var server = Start(content, repo, givenHall: true);
            var lm = Assert.Single(server.LandmarksForTest);
            Assert.Equal("earth_sps_hq_hall", lm.Key);
            giant = Assert.Single(server.StampedGuardianGiantsForTest);
            Assert.False(giant.CanWake);
            Assert.Equal(GuardianGiantRecord.Dormant, giant.State);
            Assert.Equal(SvGameServer.LandmarkGiantIndex(SvGameServer.GivenLandmarkIndexBase, 0), giant.Index);
            Assert.Equal(3, giant.Facing); // the template's facing (no turn): its head toward the pedestal, its feet to the gate

            // The body lies in the hall, written with it (lights out), and the hall's protection covers it.
            foreach (var (x, y, z, block) in GuardianGiantBuilder.Cells(giant.X, giant.Y, giant.Z, giant.Seed, giant.Facing, lit: false))
            {
                var cell = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), server.World.Circumference);
                Assert.Equal(content.GetBlock(block)!.NumericId, server.World.GetBlock(cell));
                Assert.True(server.IsProtectedStructureCellForTest(cell), $"giant cell {cell} is not protected");
            }

            server.Stop();
        }

        using (var repo = NewRepo("hall"))
        {
            var again = Start(content, repo, givenHall: false); // the record replays even when no list names the hall
            var replayed = Assert.Single(again.StampedGuardianGiantsForTest);
            Assert.Equal((giant.SiteId, giant.X, giant.Y, giant.Z, giant.Seed, giant.CanWake),
                (replayed.SiteId, replayed.X, replayed.Y, replayed.Z, replayed.Seed, replayed.CanWake));
            again.Stop();
        }
    }

    // ------------------------------------------------------------------------------------------------------------ helpers

    private SqliteWorldRepository NewRepo(string name) => new(new SaveGamePaths(_root, name));

    /// <summary>A meadow world with the rolled landmark and the giants off; with <paramref name="givenHall"/> the great hall
    /// stands at a given seat well away from the first pad.</summary>
    private static SvGameServer Start(GameContent content, SqliteWorldRepository repo, bool givenHall)
    {
        var config = new ServerConfig
        {
            WorldName = "spshq",
            Seed = 20261124,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceLandmarks = false,
            PlaceGuardianGiants = false,
        };
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        if (givenHall)
        {
            server.GivenLandmarkSource = _ =>
            {
                var (padX, _, padZ) = server.LandingPadForTest(0);
                return new List<SvGameServer.GivenLandmarkPlace> { new("earth_sps_hq_hall", new GivenSeat(padX + 160, padZ + 40)) };
            };
        }

        server.Start();
        return server;
    }

    /// <summary>A flat stone floor with one fallen giant standing on it (template facing <paramref name="facing"/>).</summary>
    private static StructureTemplate FloorWithGiant(int width, int length, int facing)
    {
        var t = new StructureTemplate
        {
            Key = "giant_floor",
            Name = "Giant Floor",
            Landmark = true,
            Ruin = LandmarkRules.RuinNever,
            Width = width,
            Height = 6,
            Length = length,
        };
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < length; z++)
            {
                t.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = "stone" });
            }
        }

        t.Cells.Add(new TemplateCell
        {
            X = 8, Y = 1, Z = 12, Kind = "marker", Id = LandmarkRules.GuardianGiantMarker,
            Config = $"{LandmarkRules.GuardianGiantFacingSetting}={facing}",
        });
        return t;
    }

    private static HashSet<(int X, int Y, int Z, string Block)> GiantCells(LandmarkLayout layout)
    {
        var g = Assert.Single(LandmarkGiants.Of(layout));
        return GuardianGiantBuilder.Cells(g.Anchor.X, g.Anchor.Y, g.Anchor.Z, seed: 5, g.Facing, lit: false).ToHashSet();
    }
}

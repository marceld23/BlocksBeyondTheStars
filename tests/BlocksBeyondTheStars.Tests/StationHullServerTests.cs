// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1917 / #1918 on the server: a generated station's layout is decided and pinned the moment it shows up in flight, its
/// real hull flies 1:1 with a dock point at the hangar mouth, boarding is measured to the hull, and a kit station pinned
/// before exterior detail existed gets its kit's detail once — without a single module moving.
/// </summary>
public sealed class StationHullServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_stationhulls_" + Guid.NewGuid().ToString("N"));

    /// <summary>Content whose only station structures are a small kit: a hub, a hangar with a force-field mouth, two
    /// cabins and a canteen — with the given exterior detail counts.</summary>
    private static GameContent KitContent(int solarWings, int antennas, int domes)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var hangar = StationKitComposerTests.Room("kit_hangar", "hangar", faces: new[] { "z+" }, markers: new[] { ("hangar", 3, 1, 3) });
        foreach (var c in hangar.Cells.Where(c => c.Kind == "block" && c.Z == 0 && c.X >= 1 && c.X <= 5 && c.Y >= 1 && c.Y <= 3))
        {
            c.Id = "force_field"; // the mouth
        }

        var modules = new List<StructureTemplate>
        {
            StationKitComposerTests.Room("kit_hub", "hub", markers: new[] { ("spawn", 3, 1, 3), ("vendor", 2, 1, 4), ("mission_board", 4, 1, 4) }),
            hangar,
            StationKitComposerTests.Room("kit_cabin", "cabins", faces: new[] { "x-" }),
            StationKitComposerTests.Room("kit_canteen", "canteen", faces: new[] { "x+" }),
        };
        content.SetStructureTemplates(modules, content.SettlementTemplates);
        var kits = new List<StructureKit>();
        foreach (string tier in new[] { "small", "medium", "large", "huge", "colossal" })
        {
            kits.Add(new StructureKit
            {
                Key = tier == "small" ? "k" : "k_" + tier,
                Name = "Test Kit",
                Kind = "station",
                Tier = tier,
                Start = "kit_hub",
                SolarWings = solarWings,
                Antennas = antennas,
                Domes = domes,
                Entries =
                {
                    new KitEntry { Module = "kit_hub", Required = true, Max = 1 },
                    new KitEntry { Module = "kit_hangar", Required = true, Max = 1, Rotate = false },
                    new KitEntry { Module = "kit_cabin", Required = true, Max = 1 },
                    new KitEntry { Module = "kit_canteen", Required = true, Max = 1 },
                },
            });
        }

        var warnings = new List<string>();
        content.SetStructureKits(kits, warnings.Add);
        Assert.Empty(warnings);
        return content;
    }

    private SvGameServer Started(GameContent content, out SqliteWorldRepository repo, string world)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent, StationTemplateUse = Frequency.Rare },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        return server;
    }

    private static string MarkersOf(SvGameServer server)
        => string.Join(";", server.SpaceStationMarkers.Select(m => $"{m.Type}@{m.Pos.X:F1},{m.Pos.Y:F1},{m.Pos.Z:F1}").OrderBy(s => s, StringComparer.Ordinal));

    private static Dictionary<Vector3i, ushort> BlocksOf(SvGameServer server)
    {
        var cells = new Dictionary<Vector3i, ushort>();
        for (int x = -4; x < 80; x++)
            for (int y = 56; y < 90; y++)
                for (int z = -4; z < 80; z++)
                {
                    var p = new Vector3i(x, y, z);
                    var b = server.World.GetBlock(p);
                    if (!b.IsAir)
                    {
                        cells[p] = b.Value;
                    }
                }

        return cells;
    }

    [Fact]
    public void AStationSeenInFlight_IsPinnedAtOnce_FliesItsHull_AndBoardingStampsThatLayout()
    {
        var server = Started(KitContent(solarWings: 2, antennas: 2, domes: 1), out var repo, "firstsight");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);

            // Pinned before anybody boards, exterior detail included.
            var rec = Assert.Contains(station.Id, repo.LoadMetadata()!.StationKits);
            Assert.StartsWith("k", rec.Kit, StringComparison.Ordinal);
            Assert.Equal((2, 2, 1), (rec.Exterior!.SolarWings, rec.Exterior.Antennas, rec.Exterior.Domes));

            // The hull flies in the instance (so it is sent with the other voxel bodies), centred on the contact, above
            // every planet sphere, its dock point on the hangar's -Z mouth.
            var hull = server.StationHullForTest(station.Id)!.Value;
            Assert.True(hull.Cells > 0);
            Assert.Equal(hull.Cells, server.StructureBlockCountForTest(station.Id));
            Assert.Equal((station.Position.X, station.Position.Y, station.Position.Z), (hull.Centre.X, hull.Centre.Y, hull.Centre.Z));
            Assert.True(hull.Centre.Y - hull.Half.Y >= 44f, $"the hull's lowest block sits at {hull.Centre.Y - hull.Half.Y}");
            Assert.Equal((0, -1), (hull.OutX, hull.OutZ));
            float face = hull.Centre.Z - hull.Half.Z;
            Assert.InRange(hull.Dock.Z, face - 0.01f, face + StationKitExterior.MarginXZ + 0.01f);

            // Boarding from in front of the mouth stamps exactly the pinned composition.
            server.ShipMove("Pilot", hull.Dock.X, hull.Dock.Y, hull.Dock.Z - 12f);
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"));
            var after = repo.LoadMetadata()!.StationKits[station.Id];
            Assert.Equal(rec.Modules.Select(m => (m.Key, m.X, m.Y, m.Z, m.Turns)), after.Modules.Select(m => (m.Key, m.X, m.Y, m.Z, m.Turns)));
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "hangar");
            server.Stop();
        }
    }

    [Fact]
    public void BoardingRange_IsMeasuredToTheHull_NotToItsCentre()
    {
        var server = Started(KitContent(solarWings: 2, antennas: 2, domes: 1), out var repo, "boardrange");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
            var hull = server.StationHullForTest(station.Id)!.Value;

            // Diagonally off the hull's +X / -Z corner: about 80 from the hull is too far …
            server.ShipMove("Pilot", hull.Centre.X + hull.Half.X + 55f, hull.Centre.Y, hull.Centre.Z - hull.Half.Z - 58f);
            server.BoardStation("Pilot", station.Id);
            Assert.False(server.InStation("Pilot"), "80 off the hull is too far");

            // … about 60 from the hull is close enough, although the centre is more than 70 away.
            float centreDistance = MathF.Sqrt((hull.Half.X + 40f) * (hull.Half.X + 40f) + (hull.Half.Z + 45f) * (hull.Half.Z + 45f));
            Assert.True(centreDistance > 70f, $"the test needs the centre out of range ({centreDistance})");
            server.ShipMove("Pilot", hull.Centre.X + hull.Half.X + 40f, hull.Centre.Y, hull.Centre.Z - hull.Half.Z - 45f);
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"), "60 off the hull is close enough, however far its centre is");
            server.Stop();
        }
    }

    [Fact]
    public void EvaEdits_OnAGeneratedStationHull_AreRefused()
    {
        var server = Started(KitContent(solarWings: 0, antennas: 0, domes: 0), out var repo, "evahull");
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
            var hull = server.StationHullForTest(station.Id)!.Value;
            server.ShipMove("Pilot", hull.Centre.X, hull.Centre.Y, hull.Centre.Z - hull.Half.Z - 2f);
            pilot.State.InEva = true;

            var cell = Enumerable.Range(0, 30).SelectMany(x => Enumerable.Range(0, 12).SelectMany(y => Enumerable.Range(0, 30).Select(z => new Vector3i(x, y, z))))
                .First(c => server.StructureCellForTest(station.Id, c.X, c.Y, c.Z) != 0);
            ushort before = server.StructureCellForTest(station.Id, cell.X, cell.Y, cell.Z);
            server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = station.Id, X = cell.X, Y = cell.Y, Z = cell.Z, Mine = true });
            Assert.Equal(before, server.StructureCellForTest(station.Id, cell.X, cell.Y, cell.Z));
            server.Stop();
        }
    }

    [Fact]
    public void AKitStationPinnedBeforeExteriorDetail_GetsItsKitsDetailOnce_AndNoModuleMoves()
    {
        string stationId;
        string markers;
        Dictionary<Vector3i, ushort> blocks;
        Vector3i origin;
        var server = Started(KitContent(solarWings: 0, antennas: 0, domes: 0), out var repo, "exteriorupgrade");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
            stationId = station.Id;
            var hull = server.StationHullForTest(stationId)!.Value;
            server.ShipMove("Pilot", hull.Dock.X, hull.Dock.Y, hull.Dock.Z - 12f);
            server.BoardStation("Pilot", stationId);
            Assert.True(server.InStation("Pilot"));
            markers = MarkersOf(server);
            blocks = BlocksOf(server);
            origin = server.StationStampOriginForTest(stationId);
            server.LeaveStation("Pilot");
            server.Stop();

            var meta = repo.LoadMetadata()!;
            meta.StationKits[stationId].Exterior = null; // as saved before #1918
            repo.SaveMetadata(meta);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = Started(KitContent(solarWings: 2, antennas: 2, domes: 1), out var repo2, "exteriorupgrade");
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            server2.EnterSpace("Pilot");
            var hull = server2.StationHullForTest(stationId)!.Value;
            server2.ShipMove("Pilot", hull.Dock.X, hull.Dock.Y, hull.Dock.Z - 12f);
            server2.BoardStation("Pilot", stationId);
            Assert.True(server2.InStation("Pilot"));

            var rec = repo2.LoadMetadata()!.StationKits[stationId];
            Assert.Equal((2, 2, 1), (rec.Exterior!.SolarWings, rec.Exterior.Antennas, rec.Exterior.Domes));
            Assert.Equal(markers, MarkersOf(server2));
            Assert.Equal(new Vector3i(origin.X - StationKitExterior.MarginXZ, origin.Y, origin.Z - StationKitExterior.MarginXZ), server2.StationStampOriginForTest(stationId));

            var now = BlocksOf(server2);
            foreach (var (cell, block) in blocks)
            {
                Assert.True(now.TryGetValue(cell, out var b) && b == block, $"the station's block at {cell} changed");
            }

            Assert.True(now.Count > blocks.Count, "the kit's exterior detail was added outside");
            server2.Stop();
        }
    }

    [Fact]
    public void HullCentres_KeepTheStationsOfAnOrbitApart_AndAboveEveryBody()
    {
        var halves = new List<Vector3f> { new(64f, 10f, 55f), new(12f, 5f, 12f), new(64f, 16f, 60f), new(8f, 4f, 9f) };
        var centres = SvGameServer.LayoutHullCentres(halves);
        Assert.Equal(halves.Count, centres.Length);
        for (int i = 0; i < centres.Length; i++)
        {
            Assert.True(centres[i].Y - halves[i].Y >= 44f, $"station {i} dips to {centres[i].Y - halves[i].Y}");
            Assert.True(centres[i].Z - halves[i].Z >= 50f - 0.01f, $"station {i} reaches back to z {centres[i].Z - halves[i].Z}");
            if (i > 0)
            {
                float gap = (centres[i].Z - halves[i].Z) - (centres[i - 1].Z + halves[i - 1].Z);
                Assert.True(gap >= 30f - 0.01f, $"stations {i - 1} and {i} are only {gap} apart");
            }
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Earth's fixed places on the server (#3981): Sol's Earth, visited for the first time, stamps the three pyramids of Giza
/// exactly at their places (given seats, records 1000–1002) and the five parts of the SPS headquarters side by side on one
/// ground row (a complex, records 1003–1007, #3986), takes its ten landing pads from its place list (London first),
/// replays all of it after a restart, carries only the rolled kinds its whitelist names — and does all of that on an old
/// save as well (a map world's pipeline runs at the geography generation at least).
/// </summary>
public sealed class EarthPlacesTests : IDisposable
{
    private const string EarthBody = "sol-earth";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The record kinds a rolled structure on Earth may have (its whitelist), besides the given landmarks.</summary>
    private static readonly HashSet<string> AllowedRolledKinds = new(StringComparer.Ordinal)
    {
        StructureTypeRules.Settlement, StructureTypeRules.Lodge, StructureTypeRules.SpsLab, StructureTypeRules.SpsTower,
        StructureTypeRules.GuardianGiant, StructureTypeRules.GiantWreck, StructureTypeRules.NetFragment,
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_earth_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public EarthPlacesTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>Boots the world (Sol revealed on its first boot) and visits Earth as a first landing would; the visit's wall
    /// time is written to the test output.</summary>
    private SvGameServer BootOnEarth(string world, int? generation = null)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        if (generation is { } g)
        {
            config.World.TerrainGeneration = g;
        }

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
        server.Start();
        if (!server.Galaxy.Systems.Any(s => s.Id == "sol"))
        {
            var admin = server.AddLocalPlayer("Ada");
            admin.State.Role = PlayerRole.WorldAdmin;
            server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" });
        }

        var clock = Stopwatch.StartNew();
        Assert.True(server.VisitBodyForTest(EarthBody));
        _output.WriteLine($"{world}: first boot of {EarthBody} (generation {server.Metadata.Description.TerrainGeneration}) took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(16000, server.World.Circumference);
        return server;
    }

    private static PlaceListDefinition EarthPlaces => Content.PlacesFor("sol_earth")!;

    /// <summary>The given landmarks as (template, centre X, centre Z, ground Y).</summary>
    private static List<(string Key, int X, int Z, int GroundY)> Pyramids(SvGameServer server)
        => server.LandmarksForTest.OrderBy(l => l.Key, StringComparer.Ordinal)
            .Select(l => (l.Key, l.Origin.X + (l.Structure.Width / 2), l.Origin.Z + (l.Structure.Length / 2), l.GroundY)).ToList();

    private static void AssertPlacesAndPads(SvGameServer server)
    {
        // The pyramids and the parts of the SPS headquarters stand exactly on their places (no search), pinned as given
        // landmarks in list order …
        var places = PlaceRules.ListedLandmarks(EarthPlaces).ToList();
        Assert.Equal(places.Count, server.LandmarksForTest.Count);
        for (int i = 0; i < places.Count; i++)
        {
            var lm = Assert.Single(server.LandmarksForTest, l => l.Key == places[i].Template);
            var (x, z) = places[i].CentreOn(16000, lm.Structure.Width, lm.Structure.Length);
            Assert.Equal((x, z), (lm.Origin.X + (lm.Structure.Width / 2), lm.Origin.Z + (lm.Structure.Length / 2)));
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind && r.Template == places[i].Template);
            Assert.Equal((SvGameServer.GivenLandmarkIndexBase + i, GivenSeat.SeatName), (rec.Index, rec.Seat));
            Assert.Contains(server.SiteFlatsForTest, f => f.MinX == lm.Origin.X && f.MinZ == lm.Origin.Z && f.Width == lm.Structure.Width);
            Assert.Contains(server.FootprintsForTest, f => f.Kind == StructureTypeRules.Landmark && f.Cx == lm.Rect.Cx && f.Cz == lm.Rect.Cz);
        }

        // … and the pads are the list's, in its order: London is the home touchdown.
        var pads = EarthPlaces.OfKind(PlaceListKinds.Pad).ToList();
        Assert.Equal(pads.Count, server.LandingPadCount);
        for (int i = 0; i < pads.Count; i++)
        {
            var (x, z) = pads[i].BlockOn(16000);
            var pad = server.LandingPadForTest(i);
            Assert.Equal((x, z), (pad.X, pad.Z));
        }
    }

    /// <summary>The corridor joints of the SPS headquarters (tools/landmarks/_sps_hq.py): complex-frame x and z ranges
    /// (inclusive), the walk row Y relative to the shared ground row, the clear height.</summary>
    private static readonly (int X0, int X1, int Z0, int Z1, int Y, int H)[] HqJoints =
    {
        (159, 160, 56, 63, 1, 5),     // main ↔ hangar, ground floor
        (66, 70, 119, 120, -20, 4),   // main's basement tunnel ↔ server level 1
        (149, 150, 193, 197, -58, 5), // server level 3 ↔ the core chamber's tunnel
        (258, 261, 119, 120, 1, 5),   // hangar ↔ the great hall
    };

    /// <summary>#3986: the five parts of the SPS headquarters stand at the anchor plus their offsets on ONE ground row, their
    /// corridors open through every joint, and the great hall carries its fallen giant's record (it never wakes).</summary>
    private static void AssertHeadquarters(SvGameServer server)
    {
        var hq = EarthPlaces.Places.Single(p => p.Key == "sps_hq");
        var (ax, az) = hq.BlockOn(16000);
        var parts = hq.Members.Select(m => Assert.Single(server.LandmarksForTest, l => l.Key == m.Template)).ToList();
        int groundY = parts[0].GroundY;
        for (int k = 0; k < parts.Count; k++)
        {
            Assert.Equal((ax + hq.Members[k].East, az + hq.Members[k].North, groundY), (parts[k].Origin.X, parts[k].Origin.Z, parts[k].GroundY));
        }

        AssertJointsOpen(server, parts, groundY);

        int hallIndex = PlaceRules.ListedLandmarks(EarthPlaces).ToList().FindIndex(l => l.Template == "earth_sps_hq_hall");
        int giantIndex = SvGameServer.LandmarkGiantIndex(SvGameServer.GivenLandmarkIndexBase + hallIndex, 0);
        var giant = Assert.Single(server.StampedGuardianGiantsForTest, g => g.Index == giantIndex);
        Assert.False(giant.CanWake);
    }

    /// <summary>Every joint holds only what its own part built there (the part's layout after the ruin pass — rubble, a cable
    /// tray; never a seal stone or the planet's rock from the stamp of the part beside it), and keeps at least one walkable
    /// lane across the seam: two rows above the walk floor free (air or a block without collision, a plant) on both sides.</summary>
    private static void AssertJointsOpen(SvGameServer server, List<SvGameServer.LandmarkInstance> parts, int groundY)
    {
        // The frame's low corner is the main building's: complex frame (x, Y, z) is world (fx + x, groundY + Y, fz + z).
        var (fx, fz) = (parts[0].Origin.X, parts[0].Origin.Z);
        var foreign = new List<string>();
        bool Free(int x, int y, int z)
        {
            var cell = new Vector3i(fx + x, groundY + y, fz + z);
            var block = server.World.GetBlock(cell);
            var owner = parts.Single(p => cell.X >= p.Origin.X && cell.X < p.Max.X && cell.Z >= p.Origin.Z && cell.Z < p.Max.Z);
            var local = new Vector3i(cell.X - owner.Origin.X, cell.Y - owner.Origin.Y, cell.Z - owner.Origin.Z);
            ushort built = owner.Structure.InBounds(local.X, local.Y, local.Z) ? owner.Structure.Get(local.X, local.Y, local.Z) : (ushort)0;
            bool free = block.IsAir || Content.BlockById(block) is { Solid: false };
            if (block.Value != built && !free)
            {
                foreign.Add($"({x},{y},{z}) {Content.BlockById(block)?.Key} in {owner.Key}");
            }

            return free;
        }

        foreach (var (x0, x1, z0, z1, y, h) in HqJoints)
        {
            bool acrossX = x1 == x0 + 1; // the seam runs between x0 and x1; else between z0 and z1
            int lanes = 0;
            for (int lane = acrossX ? z0 : x0; lane <= (acrossX ? z1 : x1); lane++)
            {
                bool open = true;
                for (int dy = 0; dy < h; dy++)
                {
                    bool a = acrossX ? Free(x0, y + dy, lane) : Free(lane, y + dy, z0);
                    bool b = acrossX ? Free(x1, y + dy, lane) : Free(lane, y + dy, z1);
                    open &= dy >= 2 || (a && b);
                }

                lanes += open ? 1 : 0;
            }

            Assert.True(lanes > 0, $"the joint at frame ({x0},{y},{z0}) has no walkable lane");
        }

        Assert.True(foreign.Count == 0, "foreign blocks in the joints (frame x,Y,z): " + string.Join(" ", foreign.Take(16)));
    }

    [Fact]
    public void Earth_StampsThePyramidsAtTheirPlaces_TakesItsPadsFromTheList_AndReplaysAfterARestart()
    {
        List<(string, int, int, int)> before;
        {
            var server = BootOnEarth("earth");
            AssertPlacesAndPads(server);
            AssertHeadquarters(server);
            before = Pyramids(server);

            // Only the whitelisted rolled kinds stand on Earth: no G.D.S. ruin, monument, camp, factory, rail ruin, civilization.
            var kinds = server.PlacementRecordsForTest.Where(r => r.Placed && r.Kind != LandmarkRules.RecordKind).Select(r => r.Kind).Distinct().ToList();
            _output.WriteLine("rolled kinds on Earth: " + string.Join(", ", kinds));
            _output.WriteLine("stamp report: " + string.Join(", ", server.StampReportForTest.Select(r => $"{r.Kind} {r.Placed}/{r.Requested}")));
            Assert.All(kinds, k => Assert.True(AllowedRolledKinds.Contains(k), $"'{k}' stands on Earth"));
            Assert.All(server.PlacementRecordsForTest.Where(r => r.Placed && r.Kind == LandmarkRules.RecordKind),
                r => Assert.True(r.Index >= SvGameServer.GivenLandmarkIndexBase, "a rolled landmark stands on Earth"));
            server.Stop();
            _repos[^1].Dispose();
            _repos.RemoveAt(_repos.Count - 1);
        }

        var again = BootOnEarth("earth");
        AssertPlacesAndPads(again);
        AssertHeadquarters(again);
        Assert.Equal(before, Pyramids(again));
        Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
    }

    [Fact]
    public void OnAnOldSave_EarthsPlacesAndPadsStandAllTheSame()
    {
        // A save of generation 30 — before the footprint registry (38), the overlays (39) and the reserved ground (40).
        var server = BootOnEarth("earth_old", generation: 30);
        Assert.Equal(30, server.Metadata.Description.TerrainGeneration);
        AssertPlacesAndPads(server);
    }
}

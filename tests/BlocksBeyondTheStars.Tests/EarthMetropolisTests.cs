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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Earth's nine metropolises on a real Earth (#3983): each stands at its place with its pad in the middle of its plaza, on the
/// land only (London on its island), stored as one structure overlay, with its landmarks on squares of their own inside it
/// and the G.D.S. tower's guardians at their posts; the residents stay in the range of the G.D.S. city; all of it comes back
/// after a restart. The first boot's time, the overlay sizes and the block and resident counts are written to the output.
/// </summary>
public sealed class EarthMetropolisTests : IDisposable
{
    private const string EarthBody = "sol-earth";
    private const int Circumference = 16000;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_metro_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public EarthMetropolisTests(ITestOutputHelper output) => _output = output;

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

    private SvGameServer BootOnEarth(string world)
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
        _output.WriteLine($"{world}: the boot of {EarthBody} took {clock.ElapsedMilliseconds} ms (metropolis stamp: plan, commit, landmarks {server.MetroStampMillisForTest} ms)");
        return server;
    }

    private static List<PlaceDefinition> Cities => Content.PlacesFor("sol_earth")!.OfKind(PlaceListKinds.City).ToList();

    /// <summary>Degrees east (west negative) of a world column.</summary>
    private static double Longitude(int x)
    {
        double l = (((x % Circumference) + Circumference) % Circumference) * 360.0 / Circumference;
        return l >= 180 ? l - 360 : l;
    }

    private static double Latitude(int z) => z * 180.0 / (Circumference / 2);

    [Fact]
    public void Earth_StandsItsNineMetropolises_OnTheLand_WithTheirLandmarks_AndKeepsThemAfterARestart()
    {
        var server = BootOnEarth("earth_metro");
        var metros = server.MetropolisesForTest;
        Assert.Equal(9, metros.Count);
        var before = new List<(int, int, int, string)>();
        for (int i = 0; i < Cities.Count; i++)
        {
            var place = Cities[i];
            var m = Assert.Single(metros, c => c.Index == SvGameServer.CityPlaceIndexBase + i);
            AssertCityStandsAtItsPlace(server, place, m);
            AssertOnTheLand(m);
            AssertLandmarksOnTheirSquares(server, place, m);
            ReportCity(server, place, m);
            before.Add((m.Min.X, m.Min.Z, m.Index, m.Spec.Serialize()));
        }

        AssertLondonStaysOnItsIsland(metros.Single(c => c.Name == "London"));
        AssertGdsGuardians(server);
        Assert.Contains("Kairo", server.SettlementDisplayNamesForTest("de")); // the map and VEGA name a city in the player's language
        Assert.Contains("Cairo", server.SettlementDisplayNamesForTest("en"));
        _output.WriteLine("stamp report: " + string.Join(", ", server.StampReportForTest.Select(r => $"{r.Kind} {r.Placed}/{r.Requested}")));
        server.Stop();
        _repos[^1].Dispose();
        _repos.RemoveAt(_repos.Count - 1);

        var again = BootOnEarth("earth_metro");
        var after = again.MetropolisesForTest.OrderBy(m => m.Index).Select(m => (m.Min.X, m.Min.Z, m.Index, m.Spec.Serialize())).ToList();
        Assert.Equal(before.OrderBy(b => b.Item3).ToList(), after);
        Assert.Equal(Cities.Sum(c => c.Landmarks.Count) + PlaceRules.ListedLandmarks(Content.PlacesFor("sol_earth")!).Count(),
            again.LandmarksForTest.Count); // + the listed landmarks: the three pyramids and the five parts of the SPS headquarters
        AssertGdsGuardians(again);
        Assert.DoesNotContain(again.StampReportForTest, r => r.Kind == LandmarkRules.RecordKind);
    }

    private static void AssertCityStandsAtItsPlace(SvGameServer server, PlaceDefinition place,
        (int Index, string Name, string NameKey, Shared.Geometry.Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) m)
    {
        var (x, z) = place.BlockOn(Circumference);
        var (px, pz) = m.Spec.PlazaCentre;
        Assert.Equal((x, z), (m.Min.X + px, m.Min.Z + pz)); // the place's centre — its pad — in the middle of the plaza
        Assert.Equal((place.Plaza[0], place.Plaza[1]), m.Spec.PlazaCell);
        Assert.Equal(place.Grid, m.Spec.Grid);
        Assert.Equal(place.NameKey, m.NameKey);
        Assert.NotNull(server.StructureOverlaySizeForTest(SvGameServer.CityOverlayKey(m.Index))); // one overlay, no block edits
        var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == "settlement" && r.Index == m.Index);
        Assert.Equal("city:" + place.ThemeOrKey, rec.Template);
        Assert.Equal(SvGameServer.CityOverlayPin, rec.Plan);
    }

    /// <summary>No district of the city stands on the sea: each one it builds on is at least half land on Earth's map (the
    /// sea is the map's below 0 m — the graded world's rivers and lakes are no sea).</summary>
    private static void AssertOnTheLand(
        (int Index, string Name, string NameKey, Shared.Geometry.Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) m)
    {
        var terrain = MapTerrain.For(Content.GetPlanetMap("earth")!);
        bool Sea(int x, int z) => terrain.ElevationFixed(x, z, Circumference, Circumference / 2) < 0;
        for (int gx = 0; gx < m.Spec.Grid; gx++)
        {
            for (int gz = 0; gz < m.Spec.Grid; gz++)
            {
                char role = m.Spec.RoleMap[gz][gx];
                if (role is '.' or 'L')
                {
                    continue;
                }

                var (ox, oz) = m.Spec.DistrictOrigin(gx, gz);
                int wet = 0, all = 0;
                for (int dx = 0; dx < 32; dx += 4)
                {
                    for (int dz = 0; dz < 32; dz += 4)
                    {
                        all++;
                        wet += Sea(m.Min.X + ox + dx, m.Min.Z + oz + dz) ? 1 : 0;
                    }
                }

                Assert.True(role == 'P' || wet * 2 <= all, $"{m.Name}: district {gx},{gz} ({role}) stands on the sea ({wet}/{all})");
            }
        }
    }

    private static void AssertLandmarksOnTheirSquares(SvGameServer server, PlaceDefinition place,
        (int Index, string Name, string NameKey, Shared.Geometry.Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) m)
    {
        foreach (string key in place.Landmarks)
        {
            var lm = Assert.Single(server.LandmarksForTest, l => l.Key == key);
            int x0 = lm.Origin.X - m.Min.X, z0 = lm.Origin.Z - m.Min.Z;
            int x1 = x0 + lm.Structure.Width - 1, z1 = z0 + lm.Structure.Length - 1;
            Assert.True(x0 >= 0 && z0 >= 0 && x1 < m.Spec.Footprint && z1 < m.Spec.Footprint, $"{key} stands inside {m.Name}");
            foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1), ((x0 + x1) / 2, (z0 + z1) / 2) })
            {
                int gx = Math.Min(m.Spec.Grid - 1, Math.Max(0, (cx - 4) / 36)), gz = Math.Min(m.Spec.Grid - 1, Math.Max(0, (cz - 4) / 36));
                Assert.Equal('L', m.Spec.RoleMap[gz][gx]);
            }

            Assert.Equal(m.Min.Y, lm.GroundY); // at the city's floor
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind && r.Template == key);
            Assert.InRange(rec.Index, SvGameServer.CityLandmarkIndexBase, SvGameServer.CityLandmarkIndexBase + 999);
        }
    }

    private static void AssertLondonStaysOnItsIsland(
        (int Index, string Name, string NameKey, Shared.Geometry.Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) london)
    {
        int built = 0;
        double north = -90;
        for (int gx = 0; gx < london.Spec.Grid; gx++)
        {
            for (int gz = 0; gz < london.Spec.Grid; gz++)
            {
                if (london.Spec.RoleMap[gz][gx] == '.')
                {
                    continue;
                }

                var (ox, oz) = london.Spec.DistrictOrigin(gx, gz);
                double lon = Longitude(london.Min.X + ox + 16), lat = Latitude(london.Min.Z + oz + 16);
                Assert.False(lon > 1.3 && lat < 51.1, $"London's district {gx},{gz} ({lat:F2} N, {lon:F2} E) stands on the continent");
                north = Math.Max(north, lat);
                built++;
            }
        }

        Assert.True(built >= 25, $"London covers its island, {built} districts");
        Assert.True(north > 55.0, $"London reaches into Scotland ({north:F2} N)");
    }

    private static void AssertGdsGuardians(SvGameServer server)
    {
        var guards = server.NpcPostsForTest.Where(n => n.LandmarkKey == "earth_gds_tower" && n.Role == "guardian").ToList();
        Assert.True(guards.Count >= 10, $"the G.D.S. tower's guard posts are staffed ({guards.Count})");
        Assert.All(guards, g => Assert.Equal("gds_guard", g.Look));
        Assert.Contains(server.NpcPostsForTest, n => n.LandmarkKey == "earth_gds_tower" && n.Role == "vendor");
        Assert.DoesNotContain(server.NpcPostsForTest, n => n.Role == "guardian" && n.LandmarkKey.Length == 0 && n.Settlement.Length > 0);
    }

    private void ReportCity(SvGameServer server, PlaceDefinition place,
        (int Index, string Name, string NameKey, Shared.Geometry.Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) m)
    {
        int blocks = 0;
        var s = m.Layout;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                    blocks += s.Get(x, y, z) != 0 && s.FloorAt(x, z) >= 0 ? 1 : 0;
        int npcs = server.NpcPostsForTest.Count(n => n.Settlement == m.Name);
        var overlay = server.StructureOverlaySizeForTest(SvGameServer.CityOverlayKey(m.Index))!.Value;
        int districts = string.Concat(m.Spec.RoleMap).Count(c => c != '.');
        _output.WriteLine($"{place.Key}: {s.Width}×{s.Length}, {districts} districts [{string.Join("|", m.Spec.RoleMap)}], {blocks} blocks, " +
                          $"{npcs} residents, overlay {overlay.Cells} cells / {overlay.Bytes} bytes");
        Assert.InRange(npcs, 10, 140); // in the range of the G.D.S. city (80 by bed, the professions, children, the night watch)
    }
}

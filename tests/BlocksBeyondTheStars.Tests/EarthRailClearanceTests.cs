// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
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
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Earth's rail lines keep clear of what stands in their way (#4014–#4016). The metropolis district maps — and so the
/// rail-hall squares — differ from seed to seed, and a line once cut through buildings in New York, Toronto and Paris, sank
/// into a street and scraped the Alps. On several seeds every line plans, and the curve its trains ride (the spline through
/// its pylon tops, sampled every half block, the wagon's middle and both sides) stays off every city's districts, every
/// landmark and every village, and over the town floor; after every section is stamped, the wagon's box finds not one solid
/// cell outside the line's own halls. A line pinned by the planner before is re-planned on the next load, what it stamped
/// is taken back — but a cell a player built since — and its graph and train follow.
/// </summary>
public sealed class EarthRailClearanceTests : IDisposable
{
    private const string EarthBody = "sol-earth";
    private const int Circumference = 16000;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_railclear_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public EarthRailClearanceTests(ITestOutputHelper output) => _output = output;

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

    private SvGameServer BootOnEarth(string world, int seed)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var log = new CapturingLogger();
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo, log);
        server.Start();
        if (!server.Galaxy.Systems.Any(s => s.Id == "sol"))
        {
            var admin = server.AddLocalPlayer("Ada");
            admin.State.Role = PlayerRole.WorldAdmin;
            server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" });
        }

        Assert.True(server.VisitBodyForTest(EarthBody));
        foreach (string line in log.Lines.Where(l => l.Contains("ail line", StringComparison.Ordinal)))
        {
            _output.WriteLine(line);
        }

        return server;
    }

    private void Restart(SvGameServer server)
    {
        server.Stop();
        _repos[^1].Dispose();
        _repos.RemoveAt(_repos.Count - 1);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Theory]
    [Trait("Category", "Slow")] // an Earth boot per seed (~20–40 s)
    [InlineData(4242)]
    [InlineData(20261124)]
    [InlineData(1)]
    [InlineData(77)]
    [InlineData(31337)]
    [InlineData(555)]
    public void EveryLine_Plans_AndItsCurve_KeepsClearOfTheCities_TheLandmarks_AndTheVillages(int seed)
    {
        var server = BootOnEarth("clear" + seed, seed);
        var lines = server.IntercityLinesForTest;
        Assert.Equal(3, lines.Count);
        var newYork = server.MetropolisesForTest.Single(m => m.Name == "New York");
        Assert.Equal(2, newYork.Spec.RoleMap.Sum(row => row.Count(c => c == MetroRailHalls.StationRole))); // #4015

        var problems = new List<string>();
        foreach (var line in lines)
        {
            problems.AddRange(CurveProblems(server, line));
        }

        _output.WriteLine($"seed {seed}: {string.Join("; ", lines.Select(l => $"{string.Join(" – ", l.Stations.Select(s => s.SettlementName))} {l.Pylons.Count} pylons"))}");
        Assert.True(problems.Count == 0, $"seed {seed}: {problems.Count} spots off the way: {string.Join("; ", problems.Take(12))}");
    }

    [Theory]
    [Trait("Category", "Slow")] // an Earth boot and every section stamped (~1 min per seed)
    [InlineData(20261124)]
    [InlineData(77)]
    public void RidingEveryLine_AfterEverySectionIsStamped_TheWagonsBoxFindsNoSolidCell(int seed)
    {
        var server = BootOnEarth("ride" + seed, seed);
        foreach (var p in server.IntercityLinesForTest.SelectMany(l => l.Pylons))
        {
            server.StampRailSectionsNearForTest(new Vector3f(p.X, p.Y, p.Z), 1);
        }

        Assert.All(server.RailSectionsForTest, s => Assert.True(s.Built));
        var hits = new List<string>();
        foreach (var line in server.IntercityLinesForTest)
        {
            hits.AddRange(SolidCellsInTheWagon(server, line));
        }

        Assert.True(hits.Count == 0, $"seed {seed}: {hits.Count} solid cells in the wagon's box: {string.Join("; ", hits.Take(12))}");
    }

    [Fact]
    [Trait("Category", "Slow")] // three Earth boots
    public void ALinePinnedByTheOldPlanner_IsReplanned_ItsStampTakenBack_ButAPlayersCell()
    {
        var server = BootOnEarth("migrate", 4242);
        var atlantic = server.IntercityLinesForTest[0];
        var newYork = atlantic.Stations.Single(s => s.SettlementName == "New York");
        server.StampRailSectionsNearForTest(new Vector3f(newYork.Stop.X, newYork.Stop.Y, newYork.Stop.Z), 64);
        var pylonId = Content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
        var built = server.RailSectionsForTest.Where(s => s.Line == atlantic.Index && s.Built).ToList();
        Assert.NotEmpty(built);
        var free = built.SelectMany(s => Enumerable.Range(s.First, s.Last - s.First + 1))
            .Select(j => atlantic.Pylons[j]).Where(p => !atlantic.Stations.Any(st => st.OwnsPylon(p))).Distinct().ToList();
        Assert.True(free.Count >= 2);
        var (gone, kept) = (free[0], free[^1]);
        Assert.Equal(pylonId, server.World.GetBlock(gone));
        var stone = Content.GetBlock("stone")!.NumericId;
        server.World.SetBlock(kept, stone, owner: "player-ada"); // a player built on the old line since

        // Make the line look like the old planner's: no planner pin, one stop more than its place has now.
        var rec = server.Metadata.Placements.Single(r => r.LocationId == EarthBody && r.Kind == "rail_line" && r.Index == atlantic.Index);
        rec.Template = string.Empty;
        server.SaveNow();
        Restart(server);

        var again = BootOnEarth("migrate", 4242);
        var replanned = again.Metadata.Placements.Single(r => r.LocationId == EarthBody && r.Kind == "rail_line" && r.Index == atlantic.Index);
        Assert.Equal(SvGameServer.ListedRailPlanPin, replanned.Template);
        Assert.DoesNotContain(again.RailSectionsForTest, s => s.Line == atlantic.Index && s.Built); // its sections stamp again as players come near
        Assert.NotEqual(pylonId, again.World.GetBlock(gone)); // the old pylon is gone, the ground as the generator made it
        Assert.Equal(stone, again.World.GetBlock(kept)); // what the player built stays
        Assert.Equal(3, again.TrainsForTest().Count(t => RailRules.IsPublic(t.OwnerId)));
        var pylons = again.IntercityLinesForTest.SelectMany(l => l.Pylons).ToHashSet();
        Assert.All(again.RailLinesForTest(), l => Assert.All(l.Pylons, p => Assert.Contains(p, pylons))); // no old node is left
        Assert.Equal(again.IntercityLinesForTest.Count, again.RailLinesForTest().Count);
        Restart(again);

        var third = BootOnEarth("migrate", 4242); // a line of the routed planner replays as it is
        Assert.Equal(pylons, third.IntercityLinesForTest.SelectMany(l => l.Pylons).ToHashSet());
        Assert.Equal(3, third.TrainsForTest().Count(t => RailRules.IsPublic(t.OwnerId)));
    }

    // ---------------------------------------------------------------- the curve the train rides

    /// <summary>The spots where a line's curve runs over a district, a landmark or a village, or under a town's floor — outside
    /// its own halls.</summary>
    private static IEnumerable<string> CurveProblems(SvGameServer server, SvGameServer.IntercityLineState line)
    {
        var metros = server.MetropolisesForTest;
        var boxes = new List<(double X0, double Z0, double X1, double Z1, string What)>();
        foreach (var m in metros)
        {
            boxes.AddRange(MetroRailHalls.DistrictRects(m.Spec).Select(r => ((double)m.Min.X + r.X0, (double)m.Min.Z + r.Z0, (double)m.Min.X + r.X1 + 1, (double)m.Min.Z + r.Z1 + 1, m.Name)));
        }

        boxes.AddRange(server.LandmarksForTest.Select(lm => ((double)lm.Origin.X, (double)lm.Origin.Z, (double)lm.Origin.X + lm.Structure.Width, (double)lm.Origin.Z + lm.Structure.Length, "landmark " + lm.Key)));
        foreach (var s in server.SettlementsForTest)
        {
            if (!metros.Any(m => m.Min.X == s.MinX && m.Min.Z == s.MinZ))
            {
                boxes.Add((s.MinX, s.MinZ, s.MaxX + 1, s.MaxZ + 1, "a village"));
            }
        }

        var spline = SplineOf(line.Pylons);
        for (float arc = 0; arc <= spline.TotalArc; arc += 0.5f)
        {
            var (p, rx, rz) = Pose(spline, arc);
            if (line.Stations.Any(st => InStation(st, p.X, p.Z)))
            {
                continue;
            }

            foreach (float side in new[] { -1.45f, 0f, 1.45f })
            {
                float x = p.X + rx * side, z = p.Z + rz * side;
                foreach (var b in boxes)
                {
                    double lx = WorldConstants.WrapDeltaX(x - b.X0, Circumference);
                    if (lx >= 0 && lx < b.X1 - b.X0 && z >= b.Z0 && z < b.Z1)
                    {
                        yield return $"line {line.Index} arc {arc:F0} over {b.What} at ({x:F0}, {z:F0})";
                    }
                }

                foreach (var m in metros)
                {
                    int lx = WorldConstants.WrapDeltaX((int)Math.Floor(x) - m.Min.X, Circumference), lz = (int)Math.Floor(z) - m.Min.Z;
                    int floor = m.Layout.FloorAt(lx, lz);
                    if (floor >= 0 && p.Y + RailRules.HoverHeight < m.Min.Y + floor + 1)
                    {
                        yield return $"line {line.Index} arc {arc:F0} under {m.Name}'s floor at ({x:F0}, {z:F0})";
                    }
                }
            }
        }
    }

    /// <summary>Every solid cell (not air, not a fluid, not the line's pylons or stops) in the wagon's box along a line's curve,
    /// outside its own halls.</summary>
    private static IEnumerable<string> SolidCellsInTheWagon(SvGameServer server, SvGameServer.IntercityLineState line)
    {
        var pylonId = Content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
        var stopId = Content.GetBlock(RailRules.StopBlockKey)!.NumericId;
        var spline = SplineOf(line.Pylons);
        var seen = new HashSet<Vector3i>();
        for (float arc = 0; arc <= spline.TotalArc; arc += 0.5f)
        {
            var (p, rx, rz) = Pose(spline, arc);
            float floor = p.Y + RailRules.HoverHeight;
            foreach (float side in new[] { -1.45f, -0.75f, 0f, 0.75f, 1.45f })
            {
                foreach (float up in new[] { 0.05f, 1f, 2f, 3.1f })
                {
                    var cell = WorldConstants.CanonicalBlock(new Vector3i((int)MathF.Floor(p.X + rx * side), (int)MathF.Floor(floor + up), (int)MathF.Floor(p.Z + rz * side)), Circumference);
                    if (!seen.Add(cell) || line.Stations.Any(st => InStation(st, cell.X + 0.5f, cell.Z + 0.5f) && cell.Y >= st.Origin.Y && cell.Y <= st.Origin.Y + st.Structure.Height))
                    {
                        continue;
                    }

                    var id = server.World.GetBlock(cell);
                    var def = Content.BlockById(id);
                    if (!id.IsAir && id != pylonId && id != stopId && def is { Liquid: false } && !IsFluid(def))
                    {
                        yield return $"line {line.Index} arc {arc:F0}: {def.Key} at {cell}";
                    }
                }
            }
        }
    }

    private static bool IsFluid(BlockDefinition def) => def.Key is "water" or "lava" or "oil" || def.Key.EndsWith("_water", StringComparison.Ordinal);

    private static RailSpline SplineOf(IReadOnlyList<Vector3i> pylons)
        => RailSpline.Build(pylons.Select(p => new Vector3f(p.X + 0.5f, p.Y + RailRules.LineRise, p.Z + 0.5f)).ToList(), false, Circumference);

    private static (Vector3f Point, float Rx, float Rz) Pose(RailSpline spline, float arc)
    {
        var a = spline.PointAtUnwrapped(Math.Max(0, arc - 0.25f));
        var b = spline.PointAtUnwrapped(Math.Min(spline.TotalArc, arc + 0.25f));
        float tx = b.X - a.X, tz = b.Z - a.Z, tl = MathF.Max(1e-4f, MathF.Sqrt(tx * tx + tz * tz));
        return (spline.PointAtUnwrapped(arc), tz / tl, -tx / tl);
    }

    /// <summary>Whether a point lies in a station's footprint or the block round it (X the short way round the seam).</summary>
    private static bool InStation(SvGameServer.IntercityStation st, float x, float z)
    {
        float lx = (float)WorldConstants.WrapDeltaX((double)x - st.Origin.X, Circumference), lz = z - st.Origin.Z;
        return lx >= -1 && lz >= -1 && lx < st.W + 1 && lz < st.L + 1;
    }
}

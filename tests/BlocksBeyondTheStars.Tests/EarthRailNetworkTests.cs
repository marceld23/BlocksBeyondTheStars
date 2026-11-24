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
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Earth's hover-rail network on a real Earth (#3985): the Atlantic and the Orient line plan on the first boot from the
/// metropolises' records — every hall on a rail-hall square of its city with its track in the street beside it, Paris's two
/// halls on two squares, a through station's halted train inside its hall, the Atlantic line over the X seam — and their
/// public trains run at the express step on lines whose voxels are not stamped yet. Sections stamp by the tick round Paris
/// and over the open Atlantic (a pylon on the sea bed) without any tick spending long on them, the rest in one go, and
/// everything comes back after a restart without a new stamp — but the section the restart cut short, which the tick then
/// stamps again whole. The boot's planning time, the tick times and the one-go stamp times are written to the output.
/// </summary>
public sealed class EarthRailNetworkTests : IDisposable
{
    private const string EarthBody = "sol-earth";
    private const int Circumference = 16000;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_railnet_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public EarthRailNetworkTests(ITestOutputHelper output) => _output = output;

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
        _output.WriteLine($"{world}: the boot of {EarthBody} took {clock.ElapsedMilliseconds} ms, of it the rail lines {server.ListedRailMillisForTest} ms");
        return server;
    }

    [Fact]
    public void EarthsTwoLines_StandOnTheirCities_RunEndToEnd_AndStampSectionBySection()
    {
        var server = BootOnEarth("earth_rail");
        var lines = server.IntercityLinesForTest;
        Assert.Equal(new[] { SvGameServer.ListedRailLineIndexForTest(0), SvGameServer.ListedRailLineIndexForTest(1) }, lines.Select(l => l.Index));
        var metros = server.MetropolisesForTest;
        var list = Content.PlacesFor("sol_earth")!;
        var cities = list.OfKind(PlaceListKinds.City).ToList();
        var places = list.OfKind(PlaceListKinds.Rail).ToList();
        for (int k = 0; k < lines.Count; k++)
        {
            var line = lines[k];
            var expected = places[k].Stops.Select(s => metros.Single(m => m.Index == SvGameServer.CityPlaceIndexBase + cities.FindIndex(c => c.Key == s.City)).Name);
            Assert.Equal(expected, line.Stations.Select(s => s.SettlementName));
            for (int i = 0; i < line.Stations.Count; i++)
            {
                var st = line.Stations[i];
                Assert.Equal(i > 0 && i < line.Stations.Count - 1, st.Through);
                AssertOnARailHallSquare(st, metros.Single(m => m.Name == st.SettlementName));
            }

            Report(line);
        }

        // Paris: one hall per line, on two squares.
        var paris = lines.SelectMany(l => l.Stations).Where(s => s.SettlementName == "Paris").ToList();
        Assert.Equal(2, paris.Count);
        Assert.NotEqual(paris[0].Origin, paris[1].Origin);

        // The Atlantic line crosses the X seam (London's box spans it).
        var atlantic = lines[0].Pylons;
        Assert.Contains(Enumerable.Range(1, atlantic.Count - 1), j => Math.Abs(atlantic[j].X - atlantic[j - 1].X) > Circumference / 2);

        AssertClearOfTheLandmarks(server);
        AssertThroughTrainsStandInTheirHalls(server);
        AssertPublicTrainsAtTheExpressStep(server);
        Assert.All(server.RailSectionsForTest, s => Assert.False(s.Built)); // nothing stamped at the boot

        RideTheWholeLine(server); // before a single section is stamped
        var ticks = StampByTheTick(server, paris[0].Stop, "round Paris");
        ticks.AddRange(StampByTheTick(server, DeepestPylon(lines[0]), "over the open Atlantic"));
        StampNearStations(server);
        AssertSeaPylonOnTheSeaBed(server, lines[0]);
        var berlin = lines[0].Stations[0];
        var cut = BeginStampByTheTick(server, berlin.Stop); // stopped by the restart below, half written
        var built = StampEverySection(server, cut);
        var before = lines.Select(l => l.Pylons.ToList()).ToList();
        server.Stop();
        _repos[^1].Dispose();
        _repos.RemoveAt(_repos.Count - 1);

        var again = BootOnEarth("earth_rail");
        Assert.Equal(before, again.IntercityLinesForTest.Select(l => l.Pylons.ToList()).ToList());
        Assert.Equal(built.OrderBy(b => b), again.RailSectionsForTest.Where(s => s.Built).Select(s => (s.Line, s.Section)).OrderBy(b => b));
        Assert.Empty(again.RailSectionMillisForTest); // replayed, nothing stamped again
        Assert.Equal(2, again.TrainsForTest().Count(t => RailRules.IsPublic(t.OwnerId)));

        // The section the restart cut short is stamped again from its start, and stands whole.
        ticks.AddRange(StampByTheTick(again, berlin.Stop, "Berlin's section again after the restart"));
        Assert.Contains(again.RailSectionsForTest, s => (s.Line, s.Section) == cut && s.Built);
        Assert.Equal(Content.GetBlock(RailRules.StopBlockKey)!.NumericId, again.World.GetBlock(berlin.Stop));
        Assert.Equal(Content.GetBlock(RailRules.PylonBlockKey)!.NumericId, again.World.GetBlock(berlin.ExitPylon));

        // The tick never freezes for a section: its chunks come from the background pool and its cells a few ms per tick.
        // One tick over the bound is a pause of the shared test machine (a GC or a descheduled runner, seen at 50.9 ms
        // with a 99th percentile of 0.9 ms); a section stamped in one go would put each of the eight sections over it.
        int slow = ticks.Count(t => t >= LongestSectionTickMs);
        _output.WriteLine($"all ticks with a section: the longest {ticks.Max():F1} ms, 99th percentile {Percentile(ticks, 0.99):F1} ms, {slow} over {LongestSectionTickMs} ms");
        Assert.True(slow <= AllowedPauseTicks, $"at most {AllowedPauseTicks} tick spends {LongestSectionTickMs} ms or more on the rail sections ({slow} did, the longest {ticks.Max():F1} ms)");
    }

    /// <summary>The bound the test holds a tick to — well above the ~4 ms budget, as a shared test machine has its pauses;
    /// the stamp in one go took a median 161–320 ms and up to 1.2 s.</summary>
    private const double LongestSectionTickMs = 50;

    /// <summary>How many ticks may pass <see cref="LongestSectionTickMs"/> — one pause of the test machine, not a section.</summary>
    private const int AllowedPauseTicks = 1;

    /// <summary>Drives the section ticks as a player standing at <paramref name="at"/> (the default view's reach) until every
    /// section in reach stands, and returns each tick's time (ms). A tick here comes right after the last (a short pause for
    /// the workers), so the ride takes fewer ticks than the 15 Hz tick would.</summary>
    private List<double> StampByTheTick(SvGameServer server, Vector3i at, string where)
    {
        var point = new Vector3f(at.X, at.Y, at.Z);
        int before = server.RailSectionsForTest.Count(s => s.Built);
        var ticks = new List<double>();
        var wall = Stopwatch.StartNew();
        for (int i = 0; i < MaxSectionTicks; i++)
        {
            var clock = Stopwatch.StartNew();
            var (busy, unbuilt) = server.TickRailSectionsForTest(point, SectionReachBlocks, 1.0 / 15);
            ticks.Add(clock.Elapsed.TotalMilliseconds);
            if (!busy && unbuilt == 0)
            {
                break;
            }

            System.Threading.Thread.Sleep(1);
        }

        int stamped = server.RailSectionsForTest.Count(s => s.Built) - before;
        Assert.True(stamped > 0, $"{where}: a section is stamped by the tick");
        _output.WriteLine($"{where}: {stamped} sections stamped by the tick over {ticks.Count} ticks in {wall.ElapsedMilliseconds} ms: " +
                          $"the longest tick {ticks.Max():F1} ms, 99th percentile {Percentile(ticks, 0.99):F1} ms, {ticks.Sum():F0} ms on the tick in all");
        return ticks;
    }

    /// <summary>Starts the tick's stamp of the section round <paramref name="at"/> and drives it until its station stands and
    /// its pylons are being written; returns which section it is (not built yet).</summary>
    private static (int Line, int Section) BeginStampByTheTick(SvGameServer server, Vector3i at)
    {
        var point = new Vector3f(at.X, at.Y, at.Z);
        for (int i = 0; i < MaxSectionTicks; i++)
        {
            server.TickRailSectionsForTest(point, SectionReachBlocks, 1.0 / 15);
            if (server.RailSectionStampForTest is { Step: not SvGameServer.IntercityStampStep.Stations } s)
            {
                Assert.NotEqual(SvGameServer.IntercityStampStep.Done, s.Step);
                Assert.DoesNotContain(server.RailSectionsForTest, b => (b.Line, b.Section) == (s.Line, s.Section) && b.Built);
                return (s.Line, s.Section);
            }

            System.Threading.Thread.Sleep(1);
        }

        throw new Xunit.Sdk.XunitException("the tick never got to the section's pylons");
    }

    /// <summary>A player's reach at the default view: (4 chunks + the load-ahead ring + 3 chunks) × 16 blocks.</summary>
    private const double SectionReachBlocks = (4 + 1 + 3) * 16;

    private const int MaxSectionTicks = 20_000;

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    }

    /// <summary>The line's pylon over the deepest water.</summary>
    private static Vector3i DeepestPylon(SvGameServer.IntercityLineState line)
    {
        var terrain = MapTerrain.For(Content.GetPlanetMap("earth")!);
        return line.Pylons.OrderBy(p => terrain.ElevationFixed(p.X, p.Z, Circumference, Circumference / 2)).First();
    }

    /// <summary>Every cell of a hall's footprint lies on a rail-hall square of its city or on a street beside it, and some
    /// on the square: the platform on the square, the track in the street.</summary>
    private static void AssertOnARailHallSquare(SvGameServer.IntercityStation st,
        (int Index, string Name, string NameKey, Vector3i Min, SettlementStructure Layout, CityLayoutSpec Spec) m)
    {
        int pitch = m.Spec.DistrictSize + m.Spec.Street, onSquare = 0;
        Assert.Equal(m.Min.Y, st.Origin.Y); // the hall's floor is the city's
        Assert.NotEqual(RailHallPlatforms.Both, st.Platforms);
        for (int x = 0; x < st.W; x++)
        {
            for (int z = 0; z < st.L; z++)
            {
                int lx = WorldConstants.WrapDeltaX(st.Origin.X + x - m.Min.X, Circumference), lz = st.Origin.Z + z - m.Min.Z;
                Assert.True(lx >= 0 && lz >= 0 && lx < m.Spec.Footprint && lz < m.Spec.Footprint, $"{m.Name}: the hall stands inside the city");
                bool inDistrict = lx % pitch >= m.Spec.Street && lz % pitch >= m.Spec.Street;
                if (inDistrict)
                {
                    Assert.Equal('S', m.Spec.RoleMap[lz / pitch][lx / pitch]);
                    onSquare++;
                }
            }
        }

        Assert.True(onSquare >= RailStationGenerator.Length * 4, $"{m.Name}: the hall's platform stands on its square ({onSquare} cells)");
    }

    /// <summary>A halted public train, centred on a stop as the shared rules centre it, stands with both ends inside the
    /// hall of that stop — at a through station too (its stop in the hall's middle).</summary>
    private static void AssertThroughTrainsStandInTheirHalls(SvGameServer server)
    {
        float span = RailRules.TrainSpan(RailRules.PublicTrainWagons.Length);
        var stations = server.IntercityLinesForTest.SelectMany(l => l.Stations).ToList();
        foreach (var line in server.RailListForTest.Lines)
        {
            var spline = SplineOf(line);
            foreach (float stop in line.StopArcs)
            {
                float middle = RailRules.HaltMiddleArc(stop, span, spline.TotalArc, spline.Closed);
                float reach = (span * 0.5f) + (RailRules.WagonLength * 0.5f);
                var ends = new[] { spline.PointAt(middle - reach), spline.PointAt(middle + reach) };
                var hall = stations.OrderBy(s => Gap(s, spline.PointAt(stop))).First();
                Assert.All(ends, p => Assert.True(Gap(hall, p) < 0.01f, $"the train halted at {hall.SettlementName} stands in its hall ({Gap(hall, p):0.00} out)"));
            }
        }
    }

    /// <summary>No span's corridor (the wagon's width either side of the line) runs over a landmark's footprint — the
    /// pyramids, the SPS headquarters, the cities' landmarks beside the streets the lines leave by.</summary>
    private static void AssertClearOfTheLandmarks(SvGameServer server)
    {
        foreach (var line in server.IntercityLinesForTest)
        {
            for (int j = 1; j < line.Pylons.Count; j++)
            {
                var (a, b) = (line.Pylons[j - 1], line.Pylons[j]);
                float dx = WorldConstants.WrapDeltaX(b.X - a.X, Circumference), dz = b.Z - a.Z;
                int steps = (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz));
                foreach (var lm in server.LandmarksForTest)
                {
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = i / (float)Math.Max(1, steps);
                        float lx = (float)WorldConstants.WrapDeltaX((double)(a.X + 0.5f + dx * t - lm.Origin.X), Circumference), lz = a.Z + 0.5f + dz * t - lm.Origin.Z;
                        Assert.False(lx > -1.5f && lz > -1.5f && lx < lm.Structure.Width + 1.5f && lz < lm.Structure.Length + 1.5f,
                            $"line {line.Index}: the span {j} runs over the landmark {lm.Key}");
                    }
                }
            }
        }
    }

    private static void AssertPublicTrainsAtTheExpressStep(SvGameServer server)
    {
        var trains = server.TrainsForTest().Where(t => RailRules.IsPublic(t.OwnerId)).ToList();
        Assert.Equal(2, trains.Count);
        Assert.All(trains, t => Assert.Equal(RailRules.PublicTrainExpressSpeed, t.Speed));
        Assert.All(trains, t => Assert.True(t.Halted, "each starts halted at its first station"));
    }

    /// <summary>Stamps every section that is left in one go (what the first boot would have cost stamped whole) — all but the
    /// one the tick is stamping (<paramref name="cut"/>) — and returns the built ones.</summary>
    private List<(int, int)> StampEverySection(SvGameServer server, (int Line, int Section) cut)
    {
        int before = server.RailSectionMillisForTest.Count;
        var clock = Stopwatch.StartNew();
        foreach (var st in server.IntercityLinesForTest.SelectMany(l => l.Pylons))
        {
            server.StampRailSectionsNearForTest(new Vector3f(st.X, st.Y, st.Z), 1);
        }

        var rest = server.RailSectionMillisForTest.Skip(before).ToList();
        Assert.All(server.RailSectionsForTest, s => Assert.Equal((s.Line, s.Section) != cut, s.Built));
        _output.WriteLine($"every other section in one go: {rest.Count} in {rest.Sum()} ms of stamping ({clock.ElapsedMilliseconds} ms with this test's walk); " +
                          $"the dearest {rest.Max()} ms, the median {rest.OrderBy(m => m).ElementAt(rest.Count / 2)} ms");
        return server.RailSectionsForTest.Where(s => s.Built).Select(s => (s.Line, s.Section)).ToList();
    }

    /// <summary>Stamps the sections round Paris's and London's halls (as a player standing there would) and checks each
    /// stop plate and the halls' pylons stand.</summary>
    private void StampNearStations(SvGameServer server)
    {
        var stopId = Content.GetBlock(RailRules.StopBlockKey)!.NumericId;
        var pylonId = Content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
        int before = server.RailSectionMillisForTest.Count;
        foreach (var st in server.IntercityLinesForTest.SelectMany(l => l.Stations).Where(s => s.SettlementName is "Paris" or "London"))
        {
            server.StampRailSectionsNearForTest(new Vector3f(st.Stop.X, st.Stop.Y, st.Stop.Z), 16);
            Assert.Equal(stopId, server.World.GetBlock(st.Stop));
            Assert.Equal(pylonId, server.World.GetBlock(st.EndPylon));
            Assert.Equal(pylonId, server.World.GetBlock(st.ExitPylon));
        }

        var millis = server.RailSectionMillisForTest.Skip(before).ToList();
        _output.WriteLine($"sections stamped in one go near Paris (what the tick left) and London: {millis.Count}, {string.Join(", ", millis)} ms");
    }

    /// <summary>The deepest-water pylon of the line's open-sea stretch, once its section is stamped, is one column from the
    /// sea bed up: pylon blocks all the way, solid ground under its foot.</summary>
    private void AssertSeaPylonOnTheSeaBed(SvGameServer server, SvGameServer.IntercityLineState line)
    {
        var deepest = DeepestPylon(line);
        int before = server.RailSectionMillisForTest.Count;
        server.StampRailSectionsNearForTest(new Vector3f(deepest.X, deepest.Y, deepest.Z), 1);
        _output.WriteLine($"the open-sea section: {string.Join(", ", server.RailSectionMillisForTest.Skip(before))} ms");
        var pylonId = Content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
        int y = deepest.Y, cells = 0;
        while (server.World.GetBlock(new Vector3i(deepest.X, y, deepest.Z)) == pylonId)
        {
            cells++;
            y--;
        }

        var under = Content.BlockById(server.World.GetBlock(new Vector3i(deepest.X, y, deepest.Z)));
        Assert.True(under is { Solid: true, Liquid: false }, $"the sea pylon at {deepest} stands on the sea bed (under its foot: {under?.Key})");
        Assert.True(cells > 30, $"the sea pylon reaches down to the bed ({cells} cells)");
        _output.WriteLine($"sea pylon at {deepest}: {cells} cells from the bed at Y {y}");
    }

    /// <summary>The Atlantic train runs its whole line on spline alone (most of its voxels are not stamped): from its first
    /// station it halts at every further one in line order, each time for the public halt, with both wagons in the hall.</summary>
    private void RideTheWholeLine(SvGameServer server)
    {
        var line = server.IntercityLinesForTest[0];
        var train = server.TrainsForTest().First(t => RailRules.IsPublic(t.OwnerId) && server.RailLinesForTest().Single(l => l.Id == t.LineId).Pylons.Contains(line.Pylons[0]));
        // It starts halted at one end of the line (the rail line's arc may run either way along the plan).
        var start = server.TrainHaltForTest(train.Id).Stop;
        var order = start == line.Stations[0].Stop ? line.Stations.ToList() : Enumerable.Reverse(line.Stations).ToList();
        Assert.Equal(order[0].Stop, start);
        var calls = new List<string>();
        var times = new List<double>();
        bool halted = true;
        for (double now = 0; now < 1200 && calls.Count < line.Stations.Count - 1; now += 0.25)
        {
            server.RunTrainsForTest(0.25);
            var h = server.TrainHaltForTest(train.Id);
            if (h.Halted && !halted)
            {
                var st = line.Stations.Single(s => s.Stop == h.Stop);
                calls.Add(st.SettlementName);
                times.Add(now);
                for (int w = 0; w < RailRules.PublicTrainWagons.Length; w++)
                {
                    Assert.True(Gap(st, server.WagonPoseForTest(train.Id, w)!.Value.Pos) < 0.01f, $"wagon {w} stands in the hall at {st.SettlementName}");
                }
            }

            halted = h.Halted;
        }

        Assert.Equal(order.Skip(1).Select(s => s.SettlementName), calls);
        _output.WriteLine($"the Atlantic train called at {string.Join(", ", calls.Zip(times, (c, t) => $"{c} ({t:F0} s)"))}");
    }

    private void Report(SvGameServer.IntercityLineState line)
    {
        var p = line.Pylons;
        double route = 0;
        for (int j = 1; j < p.Count; j++)
        {
            double dx = WorldConstants.WrapDeltaX((double)(p[j].X - p[j - 1].X), Circumference), dz = p[j].Z - p[j - 1].Z;
            route += Math.Sqrt(dx * dx + dz * dz);
        }

        var top = p.OrderByDescending(q => q.Y).First();
        _output.WriteLine($"line {line.Index}: {string.Join(" – ", line.Stations.Select(s => s.SettlementName))}, {p.Count} pylons, " +
                          $"{route:F0} blocks, {line.Sections.Count} sections, highest pylon top Y {top.Y} at {Longitude(top.X):F1}°E {Latitude(top.Z):F1}°N");
    }

    private static RailSpline SplineOf(NetRailLine line)
    {
        var points = new List<Vector3f>();
        for (int i = 0; i + 2 < line.Points.Length; i += 3)
        {
            points.Add(new Vector3f(line.Points[i], line.Points[i + 1], line.Points[i + 2]));
        }

        return RailSpline.Build(points, line.Closed, Circumference);
    }

    /// <summary>How far a point lies outside a station's footprint (0 inside; X the short way round the seam).</summary>
    private static float Gap(SvGameServer.IntercityStation st, Vector3f p)
    {
        float lx = (float)WorldConstants.WrapDeltaX((double)p.X - st.Origin.X, Circumference), lz = p.Z - st.Origin.Z;
        float dx = Math.Max(0f, Math.Max(-lx, lx - st.W)), dz = Math.Max(0f, Math.Max(-lz, lz - st.L));
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static float Gap(SvGameServer.IntercityStation st, Vector3i p) => Gap(st, new Vector3f(p.X + 0.5f, p.Y, p.Z + 0.5f));

    private static double Longitude(int x)
    {
        double l = (((x % Circumference) + Circumference) % Circumference) * 360.0 / Circumference;
        return l >= 180 ? l - 360 : l;
    }

    private static double Latitude(int z) => z * 180.0 / (Circumference / 2);
}

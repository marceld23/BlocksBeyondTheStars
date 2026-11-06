// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The monorail's auto-link after #3256 (a player's straight line refused to link with "too sharp" — it crossed the
/// world's X seam): a straight row of pylons links across BOTH seams (X at the circumference, Z at the latitude period);
/// when the last placed pylon does not take a new one, the best free line end does (filling a gap, extending a line's
/// other end, re-placing a mined pylon) — the straighter continuation before the nearer end of a parallel line; a full
/// pylon says "no forks" before "too sharp"; and when nothing links, the builder is pointed at the rail linker — except
/// for the world's very first pylon, which keeps VEGA's welcome.
/// </summary>
public sealed class RailAutoLinkTests : IDisposable
{
    private const int Y = 200; // high in the air: nothing solid anywhere near the lines

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_railauto_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        string world = "railauto_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        // No generated intercity line (#2125): these tests count the player's own lines.
        var config = new ServerConfig { WorldName = world, Seed = 1, StartPlanet = "salt_flats", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceIntercityRail = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Fly = true;
        p.State.Position = new Vector3f(0.5f, Y + 2, 0.5f);
        p.State.Inventory.SetSlot(5, new ItemStack("rail_pylon", 64));
        p.State.Inventory.SetSlot(6, new ItemStack("rail_linker", 1));
        return p;
    }

    /// <summary>Places a pylon, the builder standing over the spot (placing has a reach).</summary>
    private static void Pylon(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession b, int x, int z)
    {
        b.State.Position = new Vector3f(x + 0.5f, Y + 2, z + 0.5f);
        server.PlaceBlock(b.State.PlayerId, x, Y, z, "rail_pylon");
        Assert.False(server.World.GetBlock(new Vector3i(x, Y, z)).IsAir, $"the pylon at {x},{z} was not placed");
    }

    private static Vector3i At(int x, int z) => new(x, Y, z);

    /// <summary>The line holding <paramref name="pylon"/>, or null.</summary>
    private static (int Id, System.Collections.Generic.IReadOnlyList<Vector3i> Pylons, bool Closed, float TotalArc, int Stops)? LineOf(SvGameServer server, Vector3i pylon)
    {
        foreach (var line in server.RailLinesForTest())
        {
            if (line.Pylons.Contains(pylon))
            {
                return line;
            }
        }

        return null;
    }

    [Fact]
    public void AStraightRow_AutoLinksAcrossTheXSeam()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            int c = server.World.Circumference;
            // Westward of the seam, over it, and on: one straight line along X, 20 blocks a step. The third link is the
            // one the old raw "previous direction" read as a U-turn (the step before it had crossed x = 0 ≡ C).
            Pylon(server, b, c - 30, 0);
            Pylon(server, b, c - 10, 0);
            Pylon(server, b, 10, 0);
            Pylon(server, b, 30, 0);

            var line = Assert.Single(server.RailLinesForTest());
            Assert.Equal(4, line.Pylons.Count);
            Assert.InRange(line.TotalArc, 58f, 62f); // three straight spans — not the long way round the world
            Assert.DoesNotContain("@srv.rail.too_sharp", t.TextsTo(b));
        }
    }

    [Fact]
    public void AStraightRow_AutoLinksAcrossTheZSeam()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            int half = WorldConstants.LatitudePeriodFor(server.World.Circumference) / 2;
            // North over the latitude seam (z = +half ≡ −half): canonical cells on both sides of it.
            Pylon(server, b, 0, half - 30);
            Pylon(server, b, 0, half - 10);
            Pylon(server, b, 0, -half + 10);
            Pylon(server, b, 0, -half + 30);

            var line = Assert.Single(server.RailLinesForTest());
            Assert.Equal(4, line.Pylons.Count);
            Assert.InRange(line.TotalArc, 58f, 62f); // the spline is unwrapped along Z too
            Assert.DoesNotContain("@srv.rail.too_sharp", t.TextsTo(b));
        }
    }

    [Fact]
    public void TheSpline_RunsAcrossTheZSeam_TheShortWay()
    {
        int circ = WorldConstants.Circumference;
        int half = WorldConstants.LatitudePeriodFor(circ) / 2;
        var pts = new[] { new Vector3f(0.5f, 60f, half - 19.5f), new Vector3f(0.5f, 60f, -half + 0.5f), new Vector3f(0.5f, 60f, -half + 20.5f) };
        var spline = RailSpline.Build(pts, closed: false, circ);
        Assert.InRange(spline.TotalArc, 39f, 41f);
        var mid = spline.PointAt(spline.TotalArc * 0.25f); // halfway along the first span: just short of the seam
        Assert.InRange(mid.Z, half - 12f, half);
        var (_, d) = spline.Nearest(new Vector3f(0.5f, 60f, -half + 4.5f));
        Assert.True(d < 1f, $"the nearest point across the seam is {d:0.0} away");
    }

    [Fact]
    public void TheBendAndTheCost_RankAStraightContinuation_First()
    {
        var from = new Vector3f(0f, 0f, 0f);
        Assert.Equal(0f, RailRules.AutoLinkBendDegrees(from, new Vector3f(20f, 0f, 0f), new Vector3f(1f, 0f, 0f)), 3);
        Assert.Equal(90f, RailRules.AutoLinkBendDegrees(from, new Vector3f(0f, 0f, 20f), new Vector3f(1f, 0f, 0f)), 2);
        Assert.Equal(0f, RailRules.AutoLinkBendDegrees(from, new Vector3f(0f, 0f, 20f), default)); // a lone pylon: any way
        Assert.True(RailRules.AutoLinkCost(20f, 0f) < RailRules.AutoLinkCost(19f, 18f), "a straight end 20 away beats a near one bending 18°");
    }

    [Fact]
    public void AFreeLineEnd_TakesThePylon_ExtendingTheOtherEnd_AndFillingAGap()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 100, 0);
            Pylon(server, b, 120, 0);
            Pylon(server, b, 140, 0);
            Assert.Equal(3, Assert.Single(server.RailLinesForTest()).Pylons.Count);

            // The other end: the last placed pylon (x = 140) is 60 away, the line's free end at x = 100 continues straight.
            Pylon(server, b, 80, 0);
            Assert.Equal(new[] { At(100, 0) }, server.RailLinksForTest(At(80, 0)));
            Assert.Equal(4, Assert.Single(server.RailLinesForTest()).Pylons.Count);

            // Somewhere else first (a lone pylon: nothing in range), then back to fill the gap on from x = 140.
            Pylon(server, b, 400, 300);
            Assert.Empty(server.RailLinksForTest(At(400, 300)));
            Pylon(server, b, 160, 0);
            Assert.Equal(new[] { At(140, 0) }, server.RailLinksForTest(At(160, 0)));
            Assert.Equal(5, LineOf(server, At(160, 0))!.Value.Pylons.Count);
        }
    }

    [Fact]
    public void APylonRePlacedAfterMining_LinksBackIntoItsLine()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Pylon(server, b, 20, 0);
            Pylon(server, b, 40, 0);

            // Mining the end forgets it as the builder's last pylon — the re-placed one used to start a line of its own.
            b.State.Position = new Vector3f(40.5f, Y + 2, 0.5f);
            server.MineBlock(b.State.PlayerId, 40, Y, 0);
            Assert.True(server.World.GetBlock(At(40, 0)).IsAir, "the pylon was not mined");
            Assert.Equal(2, Assert.Single(server.RailLinesForTest()).Pylons.Count);

            Pylon(server, b, 40, 0);
            Assert.Equal(new[] { At(20, 0) }, server.RailLinksForTest(At(40, 0)));
            Assert.Equal(3, Assert.Single(server.RailLinesForTest()).Pylons.Count);
        }
    }

    [Fact]
    public void AParallelLinesNearerEnd_IsNotStolen_WhileAStraighterContinuationIsInRange()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0); // line 1 along X at z = 0
            Pylon(server, b, 20, 0);
            Pylon(server, b, 2, 6); // line 2, parallel at z = 6 (too sharp from line 1: a line of its own)
            Pylon(server, b, 22, 6);
            Assert.Equal(2, server.RailLinesForTest().Count);
            Pylon(server, b, 300, 300); // the last placed pylon is far off: the fallback decides

            // From (40, 0) line 2's end (22, 6) is nearer (≈19) and within the bend (≈18°), line 1's end (20, 0) runs on
            // straight (20): the straight continuation wins.
            Pylon(server, b, 40, 0);
            Assert.Equal(new[] { At(20, 0) }, server.RailLinksForTest(At(40, 0)));
            Assert.Single(server.RailLinksForTest(At(22, 6))); // line 2 keeps its free end
        }
    }

    [Fact]
    public void AFullPylon_SaysNoForks_BeforeTooSharp()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Pylon(server, b, 40, 0); // 40 apart: no link
            Pylon(server, b, 20, 0); // links to the last one (x = 40) — and is the last placed pylon now
            Assert.Single(server.RailLinksForTest(At(20, 0)));

            // The linker couples x = 0 on: the last placed pylon carries two links.
            b.State.Position = new Vector3f(0.5f, Y + 1.5f, 2f);
            server.UseGadgetForTest(b.State.PlayerId, "rail_linker", new Vector3f(0.5f, Y + 0.5f, 0.5f));
            server.TickForTest(0.5);
            b.State.Position = new Vector3f(20.5f, Y + 1.5f, 2f);
            server.UseGadgetForTest(b.State.PlayerId, "rail_linker", new Vector3f(20.5f, Y + 0.5f, 0.5f));
            server.TickForTest(0.5);
            Assert.Equal(2, server.RailLinksForTest(At(20, 0)).Count);

            // Square off it: both the bend and the two-link cap refuse — the cap is the one the builder hears.
            int before = t.TextsTo(b).Count;
            Pylon(server, b, 20, 20);
            var said = t.TextsTo(b).Skip(before).ToList();
            Assert.Contains("@srv.rail.branch", said);
            Assert.DoesNotContain("@srv.rail.too_sharp", said);
            Assert.Empty(server.RailLinksForTest(At(20, 20)));
        }
    }

    [Fact]
    public void NothingInRange_PointsAtTheLinker_ButTheVeryFirstPylonKeepsVegasWelcome()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Assert.Contains("vega.sys.rail_first", t.LineKeys(b));
            Assert.DoesNotContain("@srv.rail.no_link_hint", t.TextsTo(b));

            Pylon(server, b, 100, 0); // out of range of every pylon
            Assert.Empty(server.RailLinksForTest(At(100, 0)));
            Assert.Contains("@srv.rail.no_link_hint", t.TextsTo(b));

            string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
            string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
            Assert.Contains("\"srv.rail.no_link_hint\"", en);
            Assert.Contains("\"srv.rail.no_link_hint\"", de);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort — a locked SQLite file must not fail the test run
        }
    }
}

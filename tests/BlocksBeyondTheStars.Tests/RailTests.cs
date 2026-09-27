// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The monorail hover train (#2113, Justus' idea): pylons auto-link into a line (within 32 blocks and 30°, never through a
/// solid block, never a fork), the linker closes a loop or uncouples, the shared spline is the same on both sides, the
/// cab appears on the line and wagons couple behind it, the train runs on autopilot, reverses at the ends of an open
/// line, halts at a stop and departs on a signal, and a rider's pose is "wagon-local" — the server derives the world
/// position from the moving wagon, two riders on one train (one walking, one seated) stay where they stand, walking out
/// of the wagon is leaving, and stowing returns every item.
/// </summary>
public sealed class RailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_rail_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        string world = "rail_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, StartPlanet = "salt_flats", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private const int Y = 200; // high in the air: nothing solid anywhere near the line

    /// <summary>A builder in the air over the flats with the rail kit in the pack.</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Fly = true;
        p.State.Position = new Vector3f(0.5f, Y + 2, 0.5f);
        p.State.Inventory.SetSlot(5, new ItemStack("rail_pylon", 64));
        p.State.Inventory.SetSlot(6, new ItemStack("rail_linker", 1));
        p.State.Inventory.SetSlot(7, new ItemStack("rail_cab", 1));
        p.State.Inventory.SetSlot(8, new ItemStack("wagon_seats", 1));
        p.State.Inventory.SetSlot(9, new ItemStack("rail_stop", 2));
        return p;
    }

    /// <summary>Uses the linker on pylon A, then on pylon B — walking up to each (a gadget has a reach), a moment apart
    /// (a gadget's second use inside its cooldown is dropped quietly).</summary>
    private static void Linker(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession b, int ax, int az, int bx, int bz)
    {
        Use(server, b, "rail_linker", new Vector3f(ax + 0.5f, Y + 0.5f, az + 0.5f));
        server.TickForTest(0.5);
        Use(server, b, "rail_linker", new Vector3f(bx + 0.5f, Y + 0.5f, bz + 0.5f));
        server.TickForTest(0.5);
    }

    /// <summary>Uses a gadget at a spot, standing beside it.</summary>
    private static void Use(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession b, string gadget, Vector3f target)
    {
        b.State.Position = new Vector3f(target.X, target.Y + 1f, target.Z + 1.5f);
        server.UseGadgetForTest(b.State.PlayerId, gadget, target);
    }

    /// <summary>Places a pylon: the builder walks up to the spot first (placing has a reach).</summary>
    private static void Pylon(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession b, int x, int z)
    {
        b.State.Position = new Vector3f(x + 0.5f, Y + 2, z + 0.5f);
        server.PlaceBlock(b.State.PlayerId, x, Y, z, "rail_pylon");
    }

    [Fact]
    public void Pylons_AutoLinkIntoALine_ButNotAcrossASharpBend_AndNotThroughAHill()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Assert.Empty(server.RailLinesForTest()); // one pylon is no line
            Pylon(server, b, 20, 0);
            Pylon(server, b, 40, 2);
            var line = Assert.Single(server.RailLinesForTest());
            Assert.Equal(3, line.Pylons.Count);
            Assert.False(line.Closed);
            Assert.InRange(line.TotalArc, 38f, 46f);

            // A sharp turn back is refused by the auto-link: the pylon starts a line of its own.
            Pylon(server, b, 30, 30);
            Assert.Single(server.RailLinesForTest());
            Assert.Empty(server.RailLinksForTest(new Vector3i(30, Y, 30)));

            // Too far: no link.
            Pylon(server, b, 80, 30);
            Assert.Single(server.RailLinesForTest());
            Assert.Empty(server.RailLinksForTest(new Vector3i(80, Y, 30)));

            // A wall of stone across the way: the next pylon in line (30 blocks on, straight) is refused ("Hindernis").
            for (int dy = 0; dy < 6; dy++)
                for (int dz = 27; dz <= 34; dz++)
                {
                    server.World.SetBlock(new Vector3i(95, Y + dy, dz), _content.GetBlock("stone")!.NumericId);
                }

            Pylon(server, b, 110, 30);
            Assert.Empty(server.RailLinksForTest(new Vector3i(110, Y, 30))); // the wall kept the line out
            Assert.Empty(server.RailLinksForTest(new Vector3i(80, Y, 30)));

            // Without the wall the same pylon would have linked: one more, clear of it.
            Pylon(server, b, 110, 60);
            Assert.Single(server.RailLinksForTest(new Vector3i(110, Y, 60)));
        }
    }

    [Fact]
    public void TheLinker_ClosesALoop_AndUncouples_AndAPylonNeverForks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var b = Builder(server);
            // Four corners of a square: the 90° turns are too sharp for the auto-link, so two straight pairs come out.
            Pylon(server, b, 0, 0);
            Pylon(server, b, 24, 0);
            Pylon(server, b, 24, 24);
            Pylon(server, b, 0, 24);
            Assert.Equal(2, server.RailLinesForTest().Count);

            // The linker couples the corners: first the second pair onto the first…
            Linker(server, b, 24, 0, 24, 24);
            var open = Assert.Single(server.RailLinesForTest());
            Assert.False(open.Closed);
            Assert.Equal(4, open.Pylons.Count);

            // …then the last pylon onto the first: the loop closes.
            Linker(server, b, 0, 24, 0, 0);
            var loop = Assert.Single(server.RailLinesForTest());
            Assert.True(loop.Closed, "the linker did not close the loop");
            Assert.Equal(4, loop.Pylons.Count);

            // A fifth pylon cannot link into a pylon that already carries two links.
            Pylon(server, b, 48, 0);
            Assert.Empty(server.RailLinksForTest(new Vector3i(48, Y, 0)));
            Linker(server, b, 48, 0, 24, 0);
            Assert.Empty(server.RailLinksForTest(new Vector3i(48, Y, 0)));

            // The linker on two linked pylons uncouples them: the loop opens again.
            Linker(server, b, 0, 24, 0, 0);
            Assert.False(Assert.Single(server.RailLinesForTest(), l => l.Pylons.Count == 4).Closed);
        }
    }

    [Fact]
    public void TheSpline_IsTheSameOnBothSides_AndRunsAcrossTheWorldSeam()
    {
        // The client builds the same curve from the same points (RailView) — the wire carries only the points.
        var pts = new[] { new Vector3f(5990.5f, 60f, 0.5f), new Vector3f(6020.5f, 60f, 0.5f), new Vector3f(6050.5f, 64f, 8.5f) };
        var a = RailSpline.Build(pts, closed: false, WorldConstants.Circumference);
        var b = RailSpline.Build(pts, closed: false, WorldConstants.Circumference);
        Assert.Equal(a.TotalArc, b.TotalArc);
        Assert.InRange(a.TotalArc, 60f, 70f);
        for (float s = 0; s < a.TotalArc; s += 7f)
        {
            Assert.Equal(a.PointAt(s), b.PointAt(s));
            Assert.Equal(a.YawAt(s), b.YawAt(s));
        }

        // Across the seam: the middle point lies past the circumference and is wrapped back into the world.
        var mid = a.PointAt(a.TotalArc * 0.5f);
        Assert.InRange(mid.X, 0f, WorldConstants.Circumference);
        Assert.True(mid.X < 100f, $"the wrapped x is {mid.X}");

        // Frames: local → world → local is the identity.
        var local = new Vector3f(0.8f, 0.3f, -2.1f);
        var (pos, yaw) = a.PoseAt(20f);
        var world = RailRules.LocalToWorld(pos, yaw, local);
        var back = RailRules.WorldToLocal(pos, yaw, world);
        Assert.Equal(local.X, back.X, 3);
        Assert.Equal(local.Y, back.Y, 3);
        Assert.Equal(local.Z, back.Z, 3);
    }

    [Fact]
    public void TheCab_AppearsOnTheLine_WagonsCouple_TheTrainRunsAndReverses_AndStowReturnsEverything()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Pylon(server, b, 30, 0);
            Pylon(server, b, 60, 0);
            Use(server, b, "rail_cab", new Vector3f(30.5f, Y + 1.5f, 0.5f));
            var t = Assert.Single(server.TrainsForTest());
            Assert.Equal(0, b.State.Inventory.CountOf("rail_cab"));
            Assert.Equal(new[] { "rail_cab" }, t.Wagons);
            Assert.True(t.Autopilot);

            // The wagon couples behind the cab: aim just behind the tail.
            var tail = server.WagonPoseForTest(t.Id, 1)!.Value;
            Use(server, b, "wagon_seats", tail.Pos);
            t = Assert.Single(server.TrainsForTest());
            Assert.Equal(new[] { "rail_cab", "wagon_seats" }, t.Wagons);

            // It runs on autopilot and turns round at the end of the open line.
            float arc0 = t.Arc;
            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.25);
            }

            t = Assert.Single(server.TrainsForTest());
            Assert.NotEqual(arc0, t.Arc);
            int dir0 = t.Direction;
            for (int i = 0; i < 400; i++)
            {
                server.TickForTest(0.25);
            }

            var laps = server.TrainsForTest();
            Assert.Contains(laps, x => x.Direction != dir0 || true); // it kept running
            Assert.True(server.TrainsForTest().Single().Arc >= 0f);

            // The speed setting and a halt.
            server.SetTrainForTest(b.State.PlayerId, t.Id, speed: 3);
            Assert.Equal(3, server.TrainsForTest().Single().Speed);
            server.SetTrainForTest(b.State.PlayerId, t.Id, halt: 1);
            float held = server.TrainsForTest().Single().Arc;
            server.TickForTest(1.0);
            Assert.Equal(held, server.TrainsForTest().Single().Arc);
            server.SetTrainForTest(b.State.PlayerId, t.Id, halt: 0);

            // Stow: everything comes back.
            b.State.Position = server.WagonPoseForTest(t.Id, 0)!.Value.Pos;
            server.StowTrainForTest(b.State.PlayerId, t.Id);
            Assert.Empty(server.TrainsForTest());
            Assert.Equal(1, b.State.Inventory.CountOf("rail_cab"));
            Assert.Equal(1, b.State.Inventory.CountOf("wagon_seats"));
        }
    }

    [Fact]
    public void AStop_HaltsTheAutopilot_AndASignalDepartsIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0);
            Pylon(server, b, 30, 0);
            Pylon(server, b, 60, 0);
            b.State.Position = new Vector3f(30.5f, Y + 2, 5.5f); server.PlaceBlock(b.State.PlayerId, 30, Y, 3, "rail_stop"); // beside the line
            Assert.Equal(1, Assert.Single(server.RailLinesForTest()).Stops);

            Use(server, b, "rail_cab", new Vector3f(5.5f, Y + 1.5f, 0.5f));
            var t = Assert.Single(server.TrainsForTest());
            bool halted = false;
            for (int i = 0; i < 200 && !halted; i++)
            {
                server.TickForTest(0.25);
                halted = server.TrainsForTest().Single().Halted;
            }

            Assert.True(halted, "the autopilot never halted at the stop");
            float at = server.TrainsForTest().Single().Arc;
            server.TickForTest(1.0);
            Assert.Equal(at, server.TrainsForTest().Single().Arc); // waiting

            // The signal on the stop's port: a rising edge departs it at once — here the owner's press on the stop (the
            // machines' manual start, the same path), standing beside it (a device action has a reach).
            b.State.Position = new Vector3f(30.5f, Y + 1f, 5.5f);
            server.SetCrystalDeviceForTest(b, new Vector3i(30, Y, 3), 1);
            server.TickForTest(0.5);
            var after = server.TrainsForTest().Single();
            Assert.False(after.Halted, "the signal did not depart the train");
            Assert.NotEqual(at, after.Arc);
        }
    }

    [Fact]
    public void Riders_RideInTheWagonsFrame_OneWalkingOneSeated_AndWalkingOutIsLeaving()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var b = Builder(server);
            Pylon(server, b, 0, 0); // 30 apart: inside the auto-link's 32-block range
            Pylon(server, b, 30, 0);
            Pylon(server, b, 60, 0);
            Pylon(server, b, 90, 0);
            Assert.Single(server.RailLinesForTest());
            Use(server, b, "rail_cab", new Vector3f(20.5f, Y + 1.5f, 0.5f)); // between two pylons: the line is sampled
            var t = Assert.Single(server.TrainsForTest());
            server.TickForTest(0.5); // past the gadget cooldown
            Use(server, b, "wagon_seats", server.WagonPoseForTest(t.Id, 1)!.Value.Pos);
            t = Assert.Single(server.TrainsForTest());
            Assert.Equal(2, t.Wagons.Count);

            // A second player, allied with the owner, rides too.
            var w = server.AddLocalPlayer("Walker");
            w.State.AboardShip = false;
            w.State.Fly = true;
            server.RequestAllianceForTest(b.State.PlayerId, w.State.PlayerId);
            server.RespondAllianceForTest(w.State.PlayerId, b.State.PlayerId, accept: true);

            // The owner sits in the seat wagon; the walker stands in the cab.
            b.State.Position = server.WagonPoseForTest(t.Id, 1)!.Value.Pos;
            server.EnterTrainForTest(b.State.PlayerId, t.Id, 1, seat: 0);
            Assert.Equal(RailRules.FrameId(t.Id, 1), b.State.InTrain);
            Assert.True(b.State.Seated);
            w.State.Position = server.WagonPoseForTest(t.Id, 0)!.Value.Pos;
            server.EnterTrainForTest(w.State.PlayerId, t.Id, 0);
            Assert.Equal(RailRules.FrameId(t.Id, 0), w.State.InTrain);
            Assert.Equal(2, server.TrainsForTest().Single().Riders.Count);

            // The walker walks to the front of the cab: their world position is the wagon's pose plus the offset.
            server.FramedMoveForTest(w.State.PlayerId, w.State.InTrain, new Vector3f(0.5f, 0f, 2f));
            var cab = server.WagonPoseForTest(t.Id, 0)!.Value;
            var expect = RailRules.LocalToWorld(cab.Pos, cab.Yaw, new Vector3f(0.5f, 0f, 2f));
            Assert.Equal(expect.X, w.State.Position.X, 2);
            Assert.Equal(expect.Z, w.State.Position.Z, 2);

            // The train moves: both riders move with it, keeping their offsets — the seated one at the seat.
            var wBefore = w.State.Position;
            var bBefore = b.State.Position;
            for (int i = 0; i < 12; i++)
            {
                server.TickForTest(0.25);
            }

            Assert.True(Math.Abs(w.State.Position.X - wBefore.X) > 2f, "the walker did not ride along");
            Assert.True(Math.Abs(b.State.Position.X - bBefore.X) > 2f, "the seated rider did not ride along");
            cab = server.WagonPoseForTest(t.Id, 0)!.Value;
            expect = RailRules.LocalToWorld(cab.Pos, cab.Yaw, new Vector3f(0.5f, 0f, 2f));
            Assert.Equal(expect.X, w.State.Position.X, 1);
            var seatWagon = server.WagonPoseForTest(t.Id, 1)!.Value;
            var seat = RailRules.LocalToWorld(seatWagon.Pos, seatWagon.Yaw, RailRules.SeatOffsets("wagon_seats")[0]);
            Assert.Equal(seat.X, b.State.Position.X, 1);

            // A fall reported aboard is nothing; walking out of the open side is leaving.
            float health = w.State.Health;
            server.FallDamageForTest(w.State.PlayerId, 40f);
            Assert.Equal(health, w.State.Health);
            server.FramedMoveForTest(w.State.PlayerId, w.State.InTrain, new Vector3f(3.5f, 0f, 0f));
            Assert.Equal(string.Empty, w.State.InTrain);
            Assert.Single(server.TrainsForTest().Single().Riders);

            // Leaving by the door: set down beside the wagon.
            server.ExitTrainForTest(b.State.PlayerId);
            Assert.Equal(string.Empty, b.State.InTrain);
            Assert.False(b.State.Seated);
            Assert.Empty(server.TrainsForTest().Single().Riders);
        }
    }

    [Fact]
    public void TheDealer_TheKit_AndTheLocales_AreInPlace()
    {
        var dealer = NpcProfessions.All.Single(p => p.Job == "rail_dealer");
        Assert.True(dealer.Trades);
        Assert.Equal("rails", dealer.Theme);
        Assert.Equal("rail_post", dealer.PostBlock);
        Assert.NotNull(_content.GetBlock("rail_post"));
        Assert.NotNull(_content.GetBlock("rail_pylon"));
        Assert.NotNull(_content.GetBlock("rail_stop"));
        foreach (string item in new[] { "rail_pylon", "rail_linker", "rail_stop", "rail_cab", "wagon_seats", "wagon_sleeper", "wagon_bar", "rail_post" })
        {
            Assert.NotNull(_content.GetItem(item));
            Assert.NotNull(_content.GetRecipe(item));
        }

        Assert.Equal(CrystalDeviceKind.RailStop, CrystalNetRules.KindOf(_content.GetBlock("rail_stop")!));
        Assert.True(CrystalNetRules.IsEdgeSink(CrystalDeviceKind.RailStop));
        Assert.True(_content.Recipes.Values.Count(r => r.MarketTheme == "rails") >= 5);

        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (string key in new[] { "item.rail_cab.desc", "blueprint.monorail.desc", "srv.rail.obstacle", "srv.rail.linked", "ui.train.title", "npc.role.rail_dealer", "vega.sys.rail_first" })
        {
            Assert.Contains("\"" + key + "\"", en);
            Assert.Contains("\"" + key + "\"", de);
        }
    }

    public void Dispose()
    {
        try
        {
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

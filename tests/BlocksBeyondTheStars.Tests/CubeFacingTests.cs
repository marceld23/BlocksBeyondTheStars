// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2124: a cube block with a FRONT — the vending machine's screen, the forge's fire hole, the watcher's eye. The front
/// is stored as a face index in the otherwise unused up-face field of a cube's descriptor (<see cref="CubeFacing"/>),
/// so a cell stays a plain cube for everything but its tiles. These tests pin the mapping (all four turns, the ±X
/// mirror between the look heading and the geometry yaw), the placement (stored for flagged blocks, not for others),
/// the drop (never carries the front) and the Crystal Net's signal direction staying in step with the drawn front.
/// </summary>
public sealed class CubeFacingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CubeFacingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_facing_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the server by a moment on Windows — the temp dir is not worth failing over.
        }
    }

    [Fact]
    public void SideOf_GivesExactlyOneFront_UnderAllFourTurns()
    {
        for (int heading = 0; heading < 4; heading++)
        {
            int front = CubeFacing.FaceAlongHeading(heading);
            var (fx, _, fz) = ShapeCode.FaceDirection(front);
            Assert.Equal(CubeFacing.HeadingOfFace(front), heading);
            Assert.Equal(1, System.Math.Abs(fx) + System.Math.Abs(fz)); // one of the four horizontal faces

            Assert.Equal(FaceSide.Top, CubeFacing.SideOf(0, front));
            Assert.Equal(FaceSide.Bottom, CubeFacing.SideOf(1, front));
            int fronts = 0;
            for (int face = 2; face <= 5; face++)
            {
                var side = CubeFacing.SideOf(face, front);
                Assert.True(side is FaceSide.Front or FaceSide.Side);
                fronts += side == FaceSide.Front ? 1 : 0;
            }

            Assert.Equal(1, fronts);
            Assert.Equal(CubeFacing.Opposite(front), CubeFacing.FaceAlongHeading(heading + 2));
        }

        // No front (a block without facing): four plain sides.
        for (int face = 2; face <= 5; face++)
        {
            Assert.Equal(FaceSide.Side, CubeFacing.SideOf(face, -1));
        }
    }

    [Fact]
    public void Toward_LooksBackAtThePlayer_Away_LooksAlong_OnEveryHeading()
    {
        // Heading index: 0 = +Z, 1 = +X, 2 = −Z, 3 = −X (the Unity euler convention placement uses).
        (int X, int Z)[] look = { (0, 1), (1, 0), (0, -1), (-1, 0) };
        for (int heading = 0; heading < 4; heading++)
        {
            var toward = ShapeCode.FaceDirection(CubeFacing.FrontForHeading(CubeFacing.Toward, heading));
            var away = ShapeCode.FaceDirection(CubeFacing.FrontForHeading(CubeFacing.Away, heading));
            Assert.Equal((-look[heading].X, 0, -look[heading].Z), toward);
            Assert.Equal((look[heading].X, 0, look[heading].Z), away);

            // The Crystal Net keeps its signal direction as the heading the player looked along; it comes back from the
            // stored front for both modes.
            Assert.Equal(heading, CubeFacing.LookHeadingOf(CubeFacing.Toward, CubeFacing.FrontForHeading(CubeFacing.Toward, heading)));
            Assert.Equal(heading, CubeFacing.LookHeadingOf(CubeFacing.Away, CubeFacing.FrontForHeading(CubeFacing.Away, heading)));

            // A player's yaw in degrees rounds to the same heading placement always used.
            Assert.Equal(heading, CubeFacing.HeadingOfYaw((heading * 90f) + 30f));
        }

        // An explicit turn stands in for the look heading (the Crystal Net's convention); -1 uses the player's yaw.
        Assert.Equal(CubeFacing.FrontForHeading(CubeFacing.Toward, 3), CubeFacing.FrontForPlacement(CubeFacing.Toward, 3, 0f));
        Assert.Equal(CubeFacing.FrontForHeading(CubeFacing.Toward, 2), CubeFacing.FrontForPlacement(CubeFacing.Toward, -1, 180f));
        Assert.Equal(CubeFacing.FrontForHeading(CubeFacing.Away, 1), CubeFacing.FrontForPlacement(CubeFacing.Away, -1, 90f));
    }

    [Fact]
    public void Descriptor_IsAPlainCube_WithTheFrontInTheUpFaceField()
    {
        for (int face = 2; face <= 5; face++)
        {
            int d = CubeFacing.Pack(face);
            Assert.NotEqual(0, d); // "faces +Z" must stay distinguishable from "nothing stored"
            Assert.True(ShapeCode.IsCube(d));
            Assert.Equal(0, ShapeCode.ShapeOf(d));
            Assert.Equal(face, CubeFacing.FrontOf(d));
            Assert.Equal(face, CubeFacing.FrontOf(ShapeCode.WithDesign(d, 7))); // paint rides along
        }

        Assert.Equal(-1, CubeFacing.FrontOf(0));
        Assert.Equal(0, CubeFacing.Pack(0));
        Assert.Equal(0, CubeFacing.Pack(1));
        Assert.Equal(-1, CubeFacing.FrontOf(ShapeCode.Pack(BlockShape.Ramp, 1, 4))); // a form's up-face is no front
    }

    [Fact]
    public void TemplateTurn_TurnsTheStoredFront()
    {
        // A station template turned a quarter keeps the front on the same side of the machine.
        for (int face = 2; face <= 5; face++)
        {
            int turned = TemplateTransform.TurnShape(CubeFacing.Pack(face));
            Assert.True(ShapeCode.IsCube(turned));
            Assert.Equal(TemplateTransform.TurnUpFace(face), CubeFacing.FrontOf(turned));
        }
    }

    [Fact]
    public void DeriveFront_LooksIntoTheOpen_AwayFromTheWall()
    {
        // Wall behind at −Z, open everywhere else: face the room (+Z).
        Assert.Equal(4, CubeFacing.DeriveFront(openPlusX: true, openMinusX: true, openPlusZ: true, openMinusZ: false));
        // Against a +X wall: face −X.
        Assert.Equal(3, CubeFacing.DeriveFront(openPlusX: false, openMinusX: true, openPlusZ: true, openMinusZ: true));
        // In a corridor open only toward −Z: face it.
        Assert.Equal(5, CubeFacing.DeriveFront(openPlusX: false, openMinusX: false, openPlusZ: false, openMinusZ: true));
        // Free-standing or buried: a fixed answer (+Z), the same on every client.
        Assert.Equal(4, CubeFacing.DeriveFront(true, true, true, true));
        Assert.Equal(4, CubeFacing.DeriveFront(false, false, false, false));
    }

    [Fact]
    public void TopPicture_TurnsWithTheFront()
    {
        // The top face's UVs are mapped with the picture's upper edge toward +Z; a front at −Z needs no turn and every
        // other front a different one, so the picture reads upright from in front of the block.
        Assert.Equal(0, CubeFacing.TopUvTurns(5));
        var turns = new HashSet<int> { CubeFacing.TopUvTurns(2), CubeFacing.TopUvTurns(3), CubeFacing.TopUvTurns(4), CubeFacing.TopUvTurns(5) };
        Assert.Equal(4, turns.Count);
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "facing"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "facing", Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static void ClearAround(SvGameServer server, Vector3i cell)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz), BlockId.Air);
                }
            }
        }
    }

    [Fact]
    public void Placement_StoresTheFront_ForFlaggedBlocks_AndNothingForOthers()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Yaw = 90f; // looking along +X
            p.State.Inventory.Add("gaming_monitor", 4, 64);
            p.State.Inventory.Add("crate", 4, 64);

            // Toward: the screen looks back at the player, i.e. toward −X.
            var screen = new Vector3i(3, 64, 0);
            ClearAround(server, screen);
            server.PlaceBlock(p.State.PlayerId, screen.X, screen.Y, screen.Z, "gaming_monitor");
            Assert.Equal(_content.GetBlock("gaming_monitor")!.NumericId.Value, server.World.GetBlock(screen).Value);
            int stored = server.World.GetShape(screen);
            Assert.True(ShapeCode.IsCube(stored));
            Assert.Equal(3, CubeFacing.FrontOf(stored)); // −X

            // The rotate key's quarter turn wins.
            var turned = new Vector3i(3, 64, 3);
            ClearAround(server, turned);
            server.PlaceBlock(p.State.PlayerId, turned.X, turned.Y, turned.Z, "gaming_monitor", yaw: 2);
            Assert.Equal(4, CubeFacing.FrontOf(server.World.GetShape(turned))); // as if looking −Z: the screen looks +Z

            // A block without a front stays descriptor 0 — nothing about ordinary blocks changes.
            var box = new Vector3i(3, 64, -3);
            ClearAround(server, box);
            server.PlaceBlock(p.State.PlayerId, box.X, box.Y, box.Z, "crate");
            Assert.Equal(_content.GetBlock("crate")!.NumericId.Value, server.World.GetBlock(box).Value);
            Assert.Equal(0, server.World.GetShape(box));
        }
    }

    [Fact]
    public void MinedDrop_NeverCarriesTheFront()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Yaw = 0f;
            p.State.Inventory.Add("gaming_monitor", 2, 64);

            var a = new Vector3i(0, 64, 3);
            ClearAround(server, a);
            server.PlaceBlock(p.State.PlayerId, a.X, a.Y, a.Z, "gaming_monitor");
            var b = new Vector3i(3, 64, 3);
            ClearAround(server, b);
            server.PlaceBlock(p.State.PlayerId, b.X, b.Y, b.Z, "gaming_monitor", yaw: 1);
            Assert.NotEqual(CubeFacing.FrontOf(server.World.GetShape(a)), CubeFacing.FrontOf(server.World.GetShape(b)));
            Assert.Equal(0, p.State.Inventory.CountOf("gaming_monitor"));

            // Both come back as the same plain item — a front is a placement, not a property of the item.
            server.MineBlock(p.State.PlayerId, a.X, a.Y, a.Z);
            server.MineBlock(p.State.PlayerId, b.X, b.Y, b.Z);
            Assert.Equal(2, p.State.Inventory.CountOf("gaming_monitor"));
            Assert.DoesNotContain(p.State.Inventory.Slots, s => s?.Item?.StartsWith("gaming_monitor#", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void Watcher_LooksWhereItsEyeIsDrawn_EvenWhenTurnedByTheRotateKey()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Tinkerer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Yaw = 90f; // looking along +X
            p.State.Inventory.Add("watcher", 8, 64);

            // Auto: the eye (the front) looks the way the player looked, and so does the watched face of the net.
            var auto = new Vector3i(3, 64, 0);
            ClearAround(server, auto);
            server.PlaceBlock(p.State.PlayerId, auto.X, auto.Y, auto.Z, "watcher");
            int front = CubeFacing.FrontOf(server.World.GetShape(auto));
            Assert.Equal(2, front); // +X
            Assert.Equal(CubeFacing.HeadingOfFace(front), server.CrystalDeviceYaw(auto));

            // Turned with the rotate key: the drawn eye and the watched face still agree on every turn (the ±X mirror
            // between a form's geometry yaw and a look heading would be the trap here).
            Vector3i[] cells = { new(0, 64, 3), new(3, 64, 3), new(6, 64, 0), new(6, 64, 3) }; // positive: the net keys canonical cells
            for (int yaw = 0; yaw < 4; yaw++)
            {
                var cell = cells[yaw];
                ClearAround(server, cell);
                server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "watcher", yaw: yaw);
                int f = CubeFacing.FrontOf(server.World.GetShape(cell));
                Assert.Equal(CubeFacing.FaceAlongHeading(yaw), f);
                Assert.True(server.CrystalDeviceYaw(cell).HasValue, $"no Crystal Net device was registered at {cell}");
                var eye = CrystalNetRules.OutputFace(server.CrystalDeviceYaw(cell)!.Value);
                Assert.Equal(ShapeCode.FaceDirection(f), (eye.X, eye.Y, eye.Z));
            }
        }
    }
}

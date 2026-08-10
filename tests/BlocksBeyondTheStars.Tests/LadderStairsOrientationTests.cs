// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Orientable ladders and the crafted staircase (#909). Both used to place with NO stored form: the ladder's
/// look was re-derived by the mesher on every rebuild (so it flipped walls whenever a neighbour was mined),
/// and the crafted <c>stairs</c> block was a plain cube despite <see cref="BlockShape.Stairs"/> existing.
/// They now go through the prop-stamp path, which pins what the player chose — while a ladder with no
/// descriptor (older saves, settlements) keeps falling back to the original heuristic.
/// </summary>
public sealed class LadderStairsOrientationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LadderStairsOrientationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ladder_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "ladder"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "ladder", Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Empties the cell and everything touching it, so a placement test sees the neighbourhood it
    /// asked for rather than whatever terrain the seed grew there.</summary>
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
    public void Ladder_StampsTheWallItWasGiven()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Climber");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("ladder", 8, 64);

            // Auto with exactly one wall around: the plate leans on it. The up-face points AWAY from the
            // support, so a wall at -X stores up-face +X (2).
            var cell = new Vector3i(2, 64, 0);
            ClearAround(server, cell);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("stone")!.NumericId);
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "ladder");

            int stamped = server.World.GetShape(cell);
            Assert.Equal((int)BlockShape.Panel, ShapeCode.ShapeOf(stamped));
            Assert.Equal(2, ShapeCode.UpFaceOf(stamped));

            // An explicit mount face (the client's rotate cycle) wins over the neighbour scan.
            var chosen = new Vector3i(2, 64, 3);
            ClearAround(server, chosen);
            server.World.SetBlock(new Vector3i(1, 64, 3), _content.GetBlock("stone")!.NumericId);
            server.PlaceBlock(p.State.PlayerId, chosen.X, chosen.Y, chosen.Z, "ladder", upFace: 5);

            int forced = server.World.GetShape(chosen);
            Assert.Equal((int)BlockShape.Panel, ShapeCode.ShapeOf(forced));
            Assert.Equal(5, ShapeCode.UpFaceOf(forced));
        }
    }

    [Fact]
    public void Ladder_WithNoWall_OrAskedTo_StandsFree()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Poler");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("ladder", 8, 64);

            // Nothing to hug → the pole form, and it is STORED (a bare 0 descriptor would let the mesher
            // start re-deriving the look again).
            var free = new Vector3i(2, 64, 0);
            ClearAround(server, free);
            server.PlaceBlock(p.State.PlayerId, free.X, free.Y, free.Z, "ladder");

            int pole = server.World.GetShape(free);
            Assert.Equal(PropShapes.LadderFreeStanding, ShapeCode.ShapeOf(pole));
            Assert.NotEqual(0, pole);

            // Free-standing is also the fifth state of the rotate cycle: it sends +Y, which means "no wall"
            // even where there is one to hug.
            var beside = new Vector3i(2, 64, 3);
            ClearAround(server, beside);
            server.World.SetBlock(new Vector3i(1, 64, 3), _content.GetBlock("stone")!.NumericId);
            server.PlaceBlock(p.State.PlayerId, beside.X, beside.Y, beside.Z, "ladder", upFace: ShapeCode.UpPlusY);

            Assert.Equal(PropShapes.LadderFreeStanding, ShapeCode.ShapeOf(server.World.GetShape(beside)));
        }
    }

    [Fact]
    public void Ladder_DropsPlain_SoBothFormsStackAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("ladder", 2, 64);

            var onWall = new Vector3i(2, 64, 0);
            ClearAround(server, onWall);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("stone")!.NumericId);
            server.PlaceBlock(p.State.PlayerId, onWall.X, onWall.Y, onWall.Z, "ladder");

            var free = new Vector3i(2, 64, 3);
            ClearAround(server, free);
            server.PlaceBlock(p.State.PlayerId, free.X, free.Y, free.Z, "ladder");
            Assert.Equal(0, p.State.Inventory.CountOf("ladder"));

            // Both come back as the SAME plain item: a "ladder#s10" pole would split the stack and then
            // place as a plate anyway (the ladder item carries no shape).
            server.MineBlock(p.State.PlayerId, onWall.X, onWall.Y, onWall.Z);
            server.MineBlock(p.State.PlayerId, free.X, free.Y, free.Z);
            Assert.Equal(2, p.State.Inventory.CountOf("ladder"));
            Assert.DoesNotContain(p.State.Inventory.Slots, s => s?.Item?.StartsWith("ladder#", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void CraftedStairs_PlaceAsARealStaircase_AndDropPlain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Yaw = 180f;
            p.State.Inventory.Add("stairs", 4, 64);

            // Auto: step geometry (it used to place as a plain cube), turned the way the player faces.
            var cell = new Vector3i(2, 64, 0);
            ClearAround(server, cell);
            server.World.SetBlock(new Vector3i(2, 63, 0), _content.GetBlock("stone")!.NumericId);
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "stairs");

            int stamped = server.World.GetShape(cell);
            Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(stamped));
            Assert.Equal(2, ShapeCode.OrientationOf(stamped));
            Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(stamped));

            // Unlike furniture, the staircase may tip: upside-down steps are a normal building move.
            var tipped = new Vector3i(2, 64, 3);
            ClearAround(server, tipped);
            server.PlaceBlock(p.State.PlayerId, tipped.X, tipped.Y, tipped.Z, "stairs", upFace: 1, yaw: 3);

            int inverted = server.World.GetShape(tipped);
            Assert.Equal((int)BlockShape.Stairs, ShapeCode.ShapeOf(inverted));
            Assert.Equal(3, ShapeCode.OrientationOf(inverted));
            Assert.Equal(1, ShapeCode.UpFaceOf(inverted));

            server.MineBlock(p.State.PlayerId, tipped.X, tipped.Y, tipped.Z);
            Assert.Equal(3, p.State.Inventory.CountOf("stairs"));
        }
    }

    [Fact]
    public void PropShapes_DescribeEachPropsReach()
    {
        // What the server honours per prop — the client's rotate cycle mirrors exactly this table.
        Assert.Equal(PropOrientation.LadderMount, PropShapes.OrientationOf("ladder"));
        Assert.Equal(PropOrientation.Full, PropShapes.OrientationOf("stairs"));
        Assert.Equal(PropOrientation.YawOnly, PropShapes.OrientationOf("bed"));
        Assert.Equal(PropOrientation.YawOnly, PropShapes.OrientationOf("flower_pot"));
        Assert.Equal(PropOrientation.None, PropShapes.OrientationOf("stone"));
        Assert.Equal(0, PropShapes.DefaultPlaceShape("stone"));

        // The mount face decides the form: the four walls keep the plate, everything else is the pole.
        foreach (int wall in ShapeCode.WallFaces)
        {
            Assert.Equal(((int)BlockShape.Panel, wall), PropShapes.LadderForm(wall));
        }

        Assert.Equal((PropShapes.LadderFreeStanding, ShapeCode.UpPlusY), PropShapes.LadderForm(ShapeCode.UpPlusY));
        Assert.Equal((PropShapes.LadderFreeStanding, ShapeCode.UpPlusY), PropShapes.LadderForm(1));
        Assert.Equal((PropShapes.LadderFreeStanding, ShapeCode.UpPlusY), PropShapes.LadderForm(-1));

        // Both ladder forms are server-stamped, so both are stripped from the drop; a player's own form on a
        // shapeable material is not.
        Assert.True(PropShapes.IsStampedForm("ladder", (int)BlockShape.Panel));
        Assert.True(PropShapes.IsStampedForm("ladder", PropShapes.LadderFreeStanding));
        Assert.True(PropShapes.IsStampedForm("stairs", (int)BlockShape.Stairs));
        Assert.True(PropShapes.IsStampedForm("bed", (int)BlockShape.Slab));
        Assert.False(PropShapes.IsStampedForm("ladder", (int)BlockShape.Sphere));
        Assert.False(PropShapes.IsStampedForm("stone", (int)BlockShape.Post));
        Assert.False(PropShapes.IsStampedForm("ladder", 0));
    }

    [Fact]
    public void LadderMount_PrefersTheWallThePlayerClicked()
    {
        // Two walls around the cell: the scan order would take +X (2), but the aim says otherwise.
        bool HasWall(int face) => face == 2 || face == 5;

        Assert.Equal(5, PropShapes.DeriveLadderMount(HasWall, clickedFace: 5));
        Assert.Equal(2, PropShapes.DeriveLadderMount(HasWall, clickedFace: -1));

        // A clicked face with no wall behind it (aiming at the floor, or at a glass pane) falls back to the
        // scan order rather than hanging the plate on nothing.
        Assert.Equal(2, PropShapes.DeriveLadderMount(HasWall, clickedFace: 4));
        Assert.Equal(2, PropShapes.DeriveLadderMount(HasWall, clickedFace: 0));

        // No wall at all → free-standing, whatever was clicked.
        Assert.Equal(ShapeCode.UpPlusY, PropShapes.DeriveLadderMount(_ => false, clickedFace: 3));
        Assert.Equal(ShapeCode.UpPlusY, PropShapes.DeriveLadderMount(null!, clickedFace: 3));
    }

    [Fact]
    public void ShapeCodeFaces_RoundTripBetweenIndexAndDirection()
    {
        for (int face = 0; face <= 5; face++)
        {
            var dir = ShapeCode.FaceDirection(face);
            Assert.Equal(face, ShapeCode.FaceFromDirection(dir.X, dir.Y, dir.Z));
            Assert.Equal(1, System.Math.Abs(dir.X) + System.Math.Abs(dir.Y) + System.Math.Abs(dir.Z));
        }

        Assert.Equal(-1, ShapeCode.FaceFromDirection(0, 0, 0));
        Assert.Equal(-1, ShapeCode.FaceFromDirection(1, 1, 0)); // a diagonal is no block face
        Assert.Equal(new[] { 2, 3, 4, 5 }, ShapeCode.WallFaces);
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
        catch (IOException)
        {
            // A leftover temp world is no reason to fail a green test run.
        }
    }
}

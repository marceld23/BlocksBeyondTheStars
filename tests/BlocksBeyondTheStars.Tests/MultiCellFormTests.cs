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
/// Player-designed forms over several blocks (#1961): the payload format, the cell field of the descriptor, and
/// the server rules — every cell is checked before anything is consumed, the form turns with its footprint,
/// it falls as one piece whoever removes a cell, and it is one item made of as many blocks as it has cells.
/// </summary>
public sealed class MultiCellFormTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MultiCellFormTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_multiform_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>One 8³ cell with its bottom layer filled — a slab. Distinct per <paramref name="extra"/> so two
    /// forms are not deduplicated.</summary>
    private static string SlabCell(int extra = 0)
    {
        var chars = new string('0', CustomShape.LargeChars).ToCharArray();
        for (int i = 0; i < (CustomShape.GridLarge * CustomShape.GridLarge) + extra; i++)
        {
            chars[i] = '1';
        }

        return new string(chars);
    }

    /// <summary>A form W×H×L of slab cells.</summary>
    private static string Form(int w, int h, int l, int extra = 0)
        => CustomShape.ComposeMulti(w, h, l, Enumerable.Range(0, w * h * l).Select(_ => SlabCell(extra)).ToList());

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "multiform")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at the origin with the shaping tool and mud (shapeable, mineable by hand); the
    /// cells around y=64 are air.</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, int mud = 8)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 64, 0.5f);
        p.State.Yaw = 0f;
        p.State.Inventory.Add("shape_tool", 1, 1);
        p.State.Inventory.Add("mud", mud, 999);
        return p;
    }

    private static string FormItem(BlocksBeyondTheStars.GameServer.PlayerSession p)
        => p.State.Inventory.Slots.First(s => s is { IsEmpty: false } && ItemKey.Base(s.Item) == "mud" && ItemKey.Shape(s.Item) != 0).Item;

    private static int CellAt(SvGameServer server, int x, int y, int z) => ShapeCode.CellOf(server.World.GetShape(new Vector3i(x, y, z)));

    private static bool IsAir(SvGameServer server, int x, int y, int z) => server.World.GetBlock(new Vector3i(x, y, z)).IsAir;

    // ---------------------------------------------------------------- format

    [Fact]
    public void TheMultiCellPayload_SaysItsFootprint_AndHandsOutItsCells()
    {
        string form = Form(2, 1, 1);

        Assert.StartsWith("m1:211:", form);
        Assert.True(CustomShape.IsValidVoxels(form));
        Assert.True(CustomShape.FitsBudget(form));
        Assert.True(CustomShape.TryFootprint(form, out int w, out int h, out int l));
        Assert.Equal((2, 1, 1), (w, h, l));
        Assert.Equal(2, CustomShape.CellCount(form));
        Assert.Equal(SlabCell(), CustomShape.CellVoxels(form, 1));
        Assert.Equal(string.Empty, CustomShape.CellVoxels(form, 2));
        Assert.Equal(0, CustomShape.GridOf(form)); // an older peer finds no legacy length → unknown form → plain cube

        // a one-block form is 1×1×1 and its own only cell
        string single = SlabCell();
        Assert.Equal(1, CustomShape.CellCount(single));
        Assert.Equal(single, CustomShape.CellVoxels(single, 0));
    }

    [Fact]
    public void AMultiCellPayload_ThatIsNotAForm_IsRefused()
    {
        string empty = new string('0', CustomShape.LargeChars);
        string full = new string('1', CustomShape.LargeChars);

        Assert.Equal(string.Empty, CustomShape.ComposeMulti(2, 1, 1, new[] { SlabCell(), empty }));   // an invisible cell that still claims its place
        Assert.Equal(string.Empty, CustomShape.ComposeMulti(2, 1, 1, new[] { full, full }));          // just two cubes
        Assert.Equal(string.Empty, CustomShape.ComposeMulti(3, 3, 1, Enumerable.Repeat(SlabCell(), 9).ToList())); // more than eight cells
        Assert.Equal(string.Empty, CustomShape.ComposeMulti(1, 1, 1, new[] { SlabCell() }));          // one cell is the legacy format's job
        Assert.False(CustomShape.IsValidVoxels("m1:211:" + SlabCell()));                               // header promises more than it carries
        Assert.False(CustomShape.IsValidVoxels("m1:411:" + string.Concat(Enumerable.Repeat(SlabCell(), 4))));
        Assert.False(CustomShape.IsValidVoxels("m1:211:" + SlabCell() + SlabCell().ToUpperInvariant().Replace('1', 'A')));
        Assert.NotEqual(string.Empty, CustomShape.ComposeMulti(2, 1, 1, new[] { SlabCell(), full })); // a solid cell inside a form is fine
    }

    [Fact]
    public void ACheckerboardCell_BreaksTheBudget_OfTheWholeForm()
    {
        var chars = new char[CustomShape.LargeChars];
        for (int i = 0; i < chars.Length; i++)
        {
            int x = i % 8, z = (i / 8) % 8, y = i / 64;
            chars[i] = ((x + y + z) & 1) == 0 ? '1' : '0';
        }

        string form = CustomShape.ComposeMulti(2, 1, 1, new[] { SlabCell(), new string(chars) });

        Assert.NotEqual(string.Empty, form);
        Assert.False(CustomShape.FitsBudget(form));
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]   // unturned: cell 1 of a 2×1×1 form lies at +X
    [InlineData(1, 0, 0, 1)]   // one quarter turn: (x, z) → (−z, x)
    [InlineData(2, -1, 0, 0)]
    [InlineData(3, 0, 0, -1)]
    public void CellsTurnWithTheForm_LikeTheGeometryInsideACell(int yaw, int dx, int dy, int dz)
        => Assert.Equal((dx, dy, dz), CustomShape.CellOffset(Form(2, 1, 1), 1, yaw));

    [Fact]
    public void CellOrder_IsXThenZThenY_AndTheHeightNeverTurns()
    {
        string form = Form(2, 2, 2);

        Assert.Equal((1, 0, 0), CustomShape.CellOffset(form, 1, 0));
        Assert.Equal((0, 0, 1), CustomShape.CellOffset(form, 2, 0));
        Assert.Equal((0, 1, 0), CustomShape.CellOffset(form, 4, 0));
        Assert.Equal((-1, 1, 1), CustomShape.CellOffset(form, 7, 1));
        Assert.Equal(7, CustomShape.CellIndex(1, 1, 1, width: 2, length: 2));
    }

    [Fact]
    public void TheCellField_SitsAboveThePaint_AndLeavesEveryOtherFieldAlone()
    {
        int descriptor = ShapeCode.WithDesign(ShapeCode.Pack(ShapeCode.FirstCustom + 3, 2, ShapeCode.UpPlusY), 0xFFFF);

        int withCell = ShapeCode.WithCell(descriptor, 7);

        Assert.Equal(7, ShapeCode.CellOf(withCell));
        Assert.Equal(0, ShapeCode.CellOf(descriptor)); // everything placed before reads "cell 0"
        Assert.Equal(ShapeCode.FirstCustom + 3, ShapeCode.ShapeOf(withCell));
        Assert.Equal(2, ShapeCode.OrientationOf(withCell));
        Assert.Equal(0xFFFF, ShapeCode.DesignOf(withCell));
        Assert.Equal(7, ShapeCode.CellOf(ShapeCode.WithoutDesign(withCell)));
        Assert.Equal(descriptor, ShapeCode.WithoutCell(withCell));
        Assert.True(ShapeCode.WithCell(descriptor, ShapeCode.MaxCellIndex) > 0); // the sign bit stays clear
    }

    [Fact]
    public void TheSilhouette_ShowsTheWholeForm()
    {
        var mask = CustomShape.SilhouetteOfForm(Form(2, 2, 1), out int columns, out int rows);

        Assert.Equal((16, 16), (columns, rows));
        Assert.True(mask[0]);                 // the slab of the bottom-left cell
        Assert.True(mask[(8 * 16) + 15]);     // the slab of the top-right cell, one block up
        Assert.False(mask[(4 * 16) + 4]);     // air above a slab
    }

    // ---------------------------------------------------------------- server

    [Fact]
    public void AForm_IsMadeFromAsManyBlocksAsItHasCells_AndGivesThemBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server, mud: 5);

            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 5);

            string item = FormItem(p);
            Assert.Equal(2, p.State.Inventory.CountOf(item));  // five blocks make two two-cell forms …
            Assert.Equal(1, p.State.Inventory.CountOf("mud")); // … and the fifth stays a block

            server.ShapeCraft(p.State.PlayerId, item, 0, count: 1); // back to plain blocks
            Assert.Equal(1, p.State.Inventory.CountOf(item));
            Assert.Equal(3, p.State.Inventory.CountOf("mud"));
        }
    }

    [Fact]
    public void OneBlock_IsNotEnoughForATwoCellForm()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server, mud: 1);

            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 1);

            Assert.Equal(1, p.State.Inventory.CountOf("mud"));
            Assert.DoesNotContain(p.State.Inventory.Slots, s => s is { IsEmpty: false } && ItemKey.Shape(s.Item) != 0);
        }
    }

    [Fact]
    public void Placing_StampsEveryCell_TurnedWithTheYaw_ForOneItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 2, 1), "Schrank", count: 8);
            string item = FormItem(p);
            int before = p.State.Inventory.CountOf(item);

            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, item, yaw: 1); // one quarter turn: +X of the form → +Z of the world

            Assert.Equal(before - 1, p.State.Inventory.CountOf(item));
            Assert.Equal(0, CellAt(server, 2, 64, 0));
            Assert.Equal(1, CellAt(server, 2, 64, 1));
            Assert.Equal(2, CellAt(server, 2, 65, 0));
            Assert.Equal(3, CellAt(server, 2, 65, 1));
            foreach (var (x, y, z) in new[] { (2, 64, 0), (2, 64, 1), (2, 65, 0), (2, 65, 1) })
            {
                int d = server.World.GetShape(new Vector3i(x, y, z));
                Assert.Equal(ItemKey.Shape(item), ShapeCode.ShapeOf(d));
                Assert.Equal(1, ShapeCode.OrientationOf(d));
                Assert.Equal(ShapeCode.UpPlusY, ShapeCode.UpFaceOf(d)); // turns, never tips
            }

            Assert.True(IsAir(server, 3, 64, 0)); // not where the unturned form would have reached
        }
    }

    [Fact]
    public void AFormThatDoesNotFit_IsRefusedWhole_AndCostsNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 2);
            string item = FormItem(p);
            server.World.SetBlock(new Vector3i(3, 64, 0), _content.GetBlock("stone")!.NumericId); // where cell 1 wants to be

            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, item, yaw: 0);

            Assert.True(IsAir(server, 2, 64, 0));
            Assert.Equal(1, p.State.Inventory.CountOf(item));
        }
    }

    [Fact]
    public void MiningAnyCell_TakesTheWholeForm_AndYieldsOneItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 2);
            string item = FormItem(p);
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, item, yaw: 0);
            Assert.Equal(0, p.State.Inventory.CountOf(item));

            server.MineBlock(p.State.PlayerId, 3, 64, 0); // NOT the anchor

            Assert.True(IsAir(server, 2, 64, 0));
            Assert.True(IsAir(server, 3, 64, 0));
            Assert.Equal(1, p.State.Inventory.CountOf(item));
            Assert.Equal(6, p.State.Inventory.CountOf("mud")); // the six blocks that were never part of it — no loose material on top
        }
    }

    [Fact]
    public void WhateverRemovesACell_TheFormFallsAsOnePiece_AndNeighboursStay()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 4);
            string item = FormItem(p);
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, item, yaw: 0); // cells (2,64,0) + (3,64,0)
            server.PlaceBlock(p.State.PlayerId, 2, 64, 1, item, yaw: 0); // a second bench right beside it

            // fire, a fluid, falling sand, a stamp — none of them knows what a form is; they all end up here
            server.World.SetBlock(new Vector3i(2, 64, 0), BlockId.Air);

            Assert.True(IsAir(server, 3, 64, 0));   // its other cell went with it
            Assert.False(IsAir(server, 2, 64, 1));  // the neighbour's cells did not
            Assert.False(IsAir(server, 3, 64, 1));
        }
    }

    [Fact]
    public void RepaintingOrDyeingACell_DoesNotKnockTheFormOver()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.CustomShapeCraft(p.State.PlayerId, "mud", Form(2, 1, 1), "Bank", count: 2);
            server.PlaceBlock(p.State.PlayerId, 2, 64, 0, FormItem(p), yaw: 0);
            var anchor = new Vector3i(2, 64, 0);
            var mud = server.World.GetBlock(anchor);
            int descriptor = server.World.GetShape(anchor);

            server.World.SetBlock(anchor, mud, tint: 0xFF0000, glow: 0, shape: ShapeCode.WithDesign(descriptor, 5));

            Assert.False(IsAir(server, 3, 64, 0));
        }
    }

    [Fact]
    public void TheForm_SurvivesARestart_InOneRegistrySlot()
    {
        string form = Form(2, 1, 2);
        var server = Started(out var repo, "multiform_restart");
        var p = Builder(server);
        server.CustomShapeCraft(p.State.PlayerId, "mud", form, "Tisch", count: 4);
        int shape = ItemKey.Shape(FormItem(p));
        server.Stop();
        repo.Dispose();

        var server2 = Started(out var repo2, "multiform_restart");
        using (repo2)
        {
            var stored = Assert.Single(repo2.ListCustomShapes());
            Assert.Equal(shape, stored.Id);
            Assert.Equal(form, stored.Voxels);
            Assert.True(server2.HasCustomShape(shape));
            Assert.Equal(4, server2.MaterialUnitsOfShape(shape));
            server2.Stop();
        }
    }
}

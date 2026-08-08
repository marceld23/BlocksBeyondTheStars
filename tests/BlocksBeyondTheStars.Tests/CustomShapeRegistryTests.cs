// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// The per-save registry of player-designed forms (#843): registration + content dedup, the shaping-tool
/// gate, the collider budget, the free 1:1 craft, persistence across a restart, the wipe (which frees the
/// id again), and the rule that an item carrying an unregistered form places as a plain cube.
/// </summary>
public sealed class CustomShapeRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CustomShapeRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_form_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A legal 4³ form: <paramref name="filled"/> micro cells in the bottom row, rest empty.</summary>
    private static string Voxels(int filled = 1)
    {
        var chars = new string('0', CustomShape.SmallChars).ToCharArray();
        for (int i = 0; i < filled; i++)
        {
            chars[i] = '1';
        }

        return new string(chars);
    }

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) Start(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules() };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client, repo);
    }

    /// <summary>Stocks the player with the shaping tool and some stone — everything a form craft needs.</summary>
    private static void Equip(SvGameServer server, int stone = 8, bool withTool = true)
    {
        var inv = server.Sessions[1].State.Inventory;
        if (withTool)
        {
            inv.Add("shape_tool", 1, 1);
        }

        inv.Add("stone", stone, 999);
    }

    private static void Craft(LoopbackClientTransport client, SvGameServer server, string voxels, string name = "Bogen", int count = 1)
    {
        client.Send(NetCodec.Encode(new CustomShapeCraftIntent
        {
            SourceItemKey = "stone",
            Voxels = voxels,
            Name = name,
            Count = count,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
    }

    private static int CraftedShapeOf(SvGameServer server)
    {
        foreach (var slot in server.Sessions[1].State.Inventory.Slots)
        {
            if (slot is { IsEmpty: false } stack && ItemKey.Base(stack.Item) == "stone" && ItemKey.Shape(stack.Item) != 0)
            {
                return ItemKey.Shape(stack.Item);
            }
        }

        return 0;
    }

    [Fact]
    public void Craft_RegistersTheForm_AndHandsBackTheMaterialCarryingIt()
    {
        var (server, client, repo) = Start("form1");
        Equip(server);

        Craft(client, server, Voxels(2), "Bogen");

        int shape = CraftedShapeOf(server);
        Assert.True(ShapeCode.IsCustomShape(shape));
        var stored = Assert.Single(repo.ListCustomShapes());
        Assert.Equal(shape, stored.Id);
        Assert.Equal(Voxels(2), stored.Voxels);
        Assert.Equal("Bogen", stored.Name);
        Assert.Equal("Justus", stored.OwnerName);
        Assert.Equal(ShapeCode.FirstCustom, stored.Id); // ids start right above the built-in forms
    }

    [Fact]
    public void Craft_IsFreeOneForOne_AndKeepsTheColour()
    {
        var (server, client, _) = Start("form2");
        var inv = server.Sessions[1].State.Inventory;
        inv.Add("shape_tool", 1, 1);
        inv.Add("stone#t3f6fb0", 4, 999);

        client.Send(NetCodec.Encode(new CustomShapeCraftIntent
        {
            SourceItemKey = "stone#t3f6fb0",
            Voxels = Voxels(3),
            Name = "Zacke",
            Count = 2,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        int shaped = 0, plain = 0;
        foreach (var slot in inv.Slots)
        {
            if (slot is { IsEmpty: false } stack && ItemKey.Base(stack.Item) == "stone")
            {
                if (ItemKey.Shape(stack.Item) != 0)
                {
                    shaped += stack.Count;
                    Assert.Equal(0x3f6fb0, ItemKey.Tint(stack.Item)); // the dye survives the re-forming
                }
                else
                {
                    plain += stack.Count;
                }
            }
        }

        Assert.Equal(2, shaped);
        Assert.Equal(2, plain); // 4 in, 2 consumed — nothing created, nothing destroyed
    }

    [Fact]
    public void SameGeometry_ReusesTheSameFormId()
    {
        var (server, client, repo) = Start("form3");
        Equip(server);

        Craft(client, server, Voxels(2), "Bogen");
        int first = CraftedShapeOf(server);
        server.Tick(3.0); // registrations are throttled to one per 2 s
        Craft(client, server, Voxels(2), "Anderer Name");

        Assert.Single(repo.ListCustomShapes());
        Assert.Equal("Bogen", repo.ListCustomShapes()[0].Name); // whoever designed it first names it
        Assert.Equal(first, repo.ListCustomShapes()[0].Id);
    }

    [Fact]
    public void MalformedVoxels_AreDroppedSilently()
    {
        var (server, client, repo) = Start("form4");
        Equip(server);

        Craft(client, server, "not-a-form");
        Craft(client, server, new string('1', CustomShape.SmallChars)); // a full grid IS a cube
        Craft(client, server, new string('0', CustomShape.SmallChars)); // …and an empty one is nothing

        Assert.Empty(repo.ListCustomShapes());
        Assert.Equal(0, CraftedShapeOf(server));
    }

    [Fact]
    public void OverBudgetForm_IsRefused()
    {
        var (server, client, repo) = Start("form5");
        Equip(server);

        // A checkerboard needs one box per filled cell — far past what the collider budget allows.
        var chars = new string('0', CustomShape.LargeChars).ToCharArray();
        for (int y = 0; y < CustomShape.GridLarge; y++)
        {
            for (int z = 0; z < CustomShape.GridLarge; z++)
            {
                for (int x = 0; x < CustomShape.GridLarge; x++)
                {
                    if ((x + y + z) % 2 == 0)
                    {
                        chars[CustomShape.IndexOf(x, y, z, CustomShape.GridLarge)] = '1';
                    }
                }
            }
        }

        Craft(client, server, new string(chars));

        Assert.Empty(repo.ListCustomShapes());
        Assert.Equal(0, CraftedShapeOf(server));
    }

    [Fact]
    public void WithoutTheShapingTool_TheCraftIsRefused()
    {
        var (server, client, repo) = Start("form6");
        Equip(server, withTool: false);

        Craft(client, server, Voxels(2));

        Assert.Empty(repo.ListCustomShapes()); // the gate is server-side, not a greyed-out button
        Assert.Equal(0, CraftedShapeOf(server));
    }

    [Fact]
    public void Registry_SurvivesARestart()
    {
        var (server, client, repo) = Start("form7");
        Equip(server);
        Craft(client, server, Voxels(2), "Bogen");
        int shape = CraftedShapeOf(server);
        server.Stop();

        var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "form7"));
        var link = new LoopbackLink();
        var server2 = new SvGameServer(
            new ServerConfig { WorldName = "form7", Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules() },
            _content,
            new LoopbackServerTransport(link),
            repo2);
        server2.Start();

        var reloaded = Assert.Single(repo2.ListCustomShapes());
        Assert.Equal(shape, reloaded.Id);
        Assert.Equal("Bogen", reloaded.Name);
        server2.Stop();
    }

    [Fact]
    public void PlacingAnUnregisteredForm_FallsBackToAPlainCube()
    {
        var (server, client, _) = Start("form8");
        var player = server.Sessions[1].State;
        // An item carrying a form index this save never registered (a wiped id, or a save-hopping stack).
        string key = ItemKey.Compose("stone", 0, 0, ShapeCode.FirstCustom);
        player.Inventory.Add(key, 4, 999);
        player.Position = new Vector3f(5.5f, 301f, 0.5f);
        var pos = new Vector3i(5, 300, 1);

        server.PlaceBlock(player.PlayerId, pos.X, pos.Y, pos.Z, key);

        Assert.False(server.World.GetBlock(pos).IsAir);
        Assert.True(ShapeCode.IsCube(server.World.GetShape(pos))); // geometry nobody can mesh is never stamped
    }

    [Fact]
    public void RegisteredForm_IsPlacedWithItsFormAndOrientation()
    {
        var (server, client, _) = Start("form9");
        Equip(server);
        Craft(client, server, Voxels(2), "Bogen");
        int shape = CraftedShapeOf(server);

        var player = server.Sessions[1].State;
        player.Position = new Vector3f(5.5f, 301f, 0.5f);
        var pos = new Vector3i(5, 300, 1);
        server.PlaceBlock(player.PlayerId, pos.X, pos.Y, pos.Z, ItemKey.Compose("stone", 0, 0, shape), upFace: 2, yaw: 3);

        int desc = server.World.GetShape(pos);
        Assert.Equal(shape, ShapeCode.ShapeOf(desc));
        Assert.Equal(3, ShapeCode.OrientationOf(desc));
        Assert.Equal(2, ShapeCode.UpFaceOf(desc));
    }

    [Fact]
    public void Wipe_RemovesTheForm_AndFreesItsIdForTheNextDesigner()
    {
        var (server, client, repo) = Start("form10");
        Equip(server, stone: 16);
        server.Sessions[1].State.Role = PlayerRole.Admin;
        Craft(client, server, Voxels(2), "Bogen");
        int first = CraftedShapeOf(server);

        server.HandleForTest(server.Sessions[1], new AdminCommandIntent { Command = "shapewipe", StringArg = "#" + first });
        Assert.Empty(repo.ListCustomShapes());

        // The freed slot is handed to the next distinct form — the documented trade at 45 ids.
        server.Tick(3.0);
        Craft(client, server, Voxels(3), "Neu");
        var reused = Assert.Single(repo.ListCustomShapes());
        Assert.Equal(first, reused.Id);
        Assert.Equal("Neu", reused.Name);
    }

    [Fact]
    public void Stencil_TakesAFormStamp_SoItCanBeGivenAway()
    {
        // A blank stencil is not a building material, but stamping a form onto it is the SAME 1:1 exchange —
        // the item key carries the form index for a stencil exactly as it does for a block (#846).
        var (server, client, repo) = Start("form12");
        var inv = server.Sessions[1].State.Inventory;
        inv.Add("shape_tool", 1, 1);
        inv.Add("shape_stencil", 2, 16);

        client.Send(NetCodec.Encode(new CustomShapeCraftIntent
        {
            SourceItemKey = "shape_stencil",
            Voxels = Voxels(2),
            Name = "Geschenk",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var stored = Assert.Single(repo.ListCustomShapes());
        bool stamped = false;
        foreach (var slot in inv.Slots)
        {
            if (slot is { IsEmpty: false } stack && ItemKey.Base(stack.Item) == "shape_stencil"
                && ItemKey.Shape(stack.Item) == stored.Id)
            {
                stamped = true;
            }
        }

        Assert.True(stamped, "the stencil should carry the registered form index");
    }

    [Fact]
    public void Networking_RoundTripsTheFormMessages()
    {
        var intent = new CustomShapeCraftIntent { SourceItemKey = "stone", Voxels = Voxels(2), Name = "Bogen", Count = 3 };
        var di = Assert.IsType<CustomShapeCraftIntent>(NetCodec.Decode(NetCodec.Encode(intent)));
        Assert.Equal(Voxels(2), di.Voxels);
        Assert.Equal("Bogen", di.Name);
        Assert.Equal(3, di.Count);

        var data = new CustomShapeData { Id = 21, Voxels = Voxels(1), Name = "Zacke", Owner = "Justus" };
        var dd = Assert.IsType<CustomShapeData>(NetCodec.Decode(NetCodec.Encode(data)));
        Assert.Equal(21, dd.Id);
        Assert.Equal("Justus", dd.Owner);

        var list = new CustomShapeList
        {
            Ids = new[] { 19, 20 },
            Voxels = new[] { Voxels(1), Voxels(2) },
            Names = new[] { "A", "B" },
            Owners = new[] { "Justus", "Marcel" },
        };
        var dl = Assert.IsType<CustomShapeList>(NetCodec.Decode(NetCodec.Encode(list)));
        Assert.Equal(new[] { 19, 20 }, dl.Ids);
        Assert.Equal(new[] { "A", "B" }, dl.Names);
    }

    [Fact]
    public void PaintDesigns_CarryTheDesignerName()
    {
        // Attribution for copied designs (#846) rides on the existing paint registry.
        var (server, client, repo) = Start("form11");
        var stone = _content.GetBlock("stone")!.NumericId;
        var pos = new Vector3i(5, 300, 0);
        server.World.SetBlock(pos, stone);
        server.Sessions[1].State.Position = new Vector3f(5.5f, 301f, 0.5f);

        client.Send(NetCodec.Encode(new PaintBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z, Pixels = new string('3', 1024) }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var design = Assert.Single(repo.ListPaintDesigns());
        Assert.Equal("Justus", design.OwnerName);
    }
}

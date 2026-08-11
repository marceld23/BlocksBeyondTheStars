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
/// The hotbar slot actions: the item-key paint-design field (<c>p&lt;xxxx&gt;</c>), the slot-pinned craft
/// output (dye/shape/paint land back in the hotbar slot they were invoked on), the whole-stack 1:1 exchange
/// in a FULL inventory (room freed by the consumed source counts), and the painted-item round trip
/// (paint craft → place stamps the design → mine recovers it; unknown ids place plain).
/// </summary>
public sealed class HotbarSlotActionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HotbarSlotActionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hotact_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A legal 32×32 design payload (1024 palette symbols).</summary>
    private static string Pixels(char symbol = '1') => new string(symbol, 1024);

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) Start(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = new GameRules(), PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.AboardShip = false; // craft from the personal inventory only, not a ship hold
        return (server, client, repo);
    }

    // --- the item-key design field ---

    [Fact]
    public void ItemKey_DesignField_ComposesAndReadsBack_WithAllModifiers()
    {
        string painted = ItemKey.Compose("stone", 0, 0, 0, 0x012a);
        Assert.Equal("stone#p012a", painted);
        Assert.Equal(0x012a, ItemKey.Design(painted));
        Assert.Equal("stone", ItemKey.Base(painted));
        Assert.Equal(0, ItemKey.Shape(painted));

        // All four modifiers compose in the fixed order t, g, s, p and read back independently.
        string combo = ItemKey.Compose("mud", 0xFF0000, 0x00FF00, (int)BlockShape.Ramp, 7);
        Assert.Equal("mud#tff0000g00ff00s05p0007", combo);
        Assert.Equal(0xFF0000, ItemKey.Tint(combo));
        Assert.Equal(0x00FF00, ItemKey.Glow(combo));
        Assert.Equal((int)BlockShape.Ramp, ItemKey.Shape(combo));
        Assert.Equal(7, ItemKey.Design(combo));

        // Design 0 adds no suffix; re-composing drops the previous design like every other modifier.
        Assert.Equal("stone", ItemKey.Compose("stone#p012a", 0, 0, 0, 0));
    }

    [Fact]
    public void ItemKey_DesignTag_NeverFalseMatchesInsideColourHex()
    {
        // 'd' is a hex digit, which is why the tag is 'p' — a colour full of d's must read as design 0.
        string dyed = ItemKey.Compose("stone", 0xDD00DD, 0, 0, 0);
        Assert.Equal(0, ItemKey.Design(dyed));
        Assert.Equal(0xDD00DD, ItemKey.Tint(dyed));

        // And the design digits never bleed into the colour fields either.
        string both = ItemKey.Compose("stone", 0xDD00DD, 0, 0, 0xBEEF);
        Assert.Equal(0xDD00DD, ItemKey.Tint(both));
        Assert.Equal(0xBEEF, ItemKey.Design(both));
    }

    [Fact]
    public void Codec_RoundTrips_PaintCraftIntent_AndTheNewSlotFields()
    {
        var paint = new PaintCraftIntent { SourceItemKey = "mud", Pixels = Pixels(), Count = 5, Slot = 3 };
        var dp = Assert.IsType<PaintCraftIntent>(NetCodec.Decode(NetCodec.Encode(paint)));
        Assert.Equal("mud", dp.SourceItemKey);
        Assert.Equal(Pixels(), dp.Pixels);
        Assert.Equal(5, dp.Count);
        Assert.Equal(3, dp.Slot);

        var shape = new ShapeCraftIntent { SourceItemKey = "stone", Shape = 4, Count = 2, Slot = 7 };
        Assert.Equal(7, Assert.IsType<ShapeCraftIntent>(NetCodec.Decode(NetCodec.Encode(shape))).Slot);

        var tint = new TintCraftIntent { SourceItemKey = "stone", Tint = 0x123456, Count = 2 };
        Assert.Equal(-1, Assert.IsType<TintCraftIntent>(NetCodec.Decode(NetCodec.Encode(tint))).Slot); // default = legacy
    }

    // --- slot-pinned craft output ---

    [Fact]
    public void ShapeCraft_WithSlot_LandsTheOutputInThatSlot()
    {
        var (server, client, _) = Start("slotpin");
        var inv = server.Sessions[1].State.Inventory;
        inv.SetSlot(3, new ItemStack("stone", 4));

        client.Send(NetCodec.Encode(new ShapeCraftIntent
        {
            SourceItemKey = "stone",
            Shape = (int)BlockShape.Sphere,
            Count = 4,
            Slot = 3,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal("stone#s04", inv.Slots[3]!.Item); // the whole stack transformed IN PLACE
        Assert.Equal(4, inv.Slots[3]!.Count);
        Assert.Equal(0, inv.CountOf("stone"));
    }

    [Fact]
    public void ShapeCraft_WithOccupiedSlot_FallsBackWithoutLosingAnything()
    {
        var (server, client, _) = Start("slotbusy");
        var inv = server.Sessions[1].State.Inventory;
        inv.SetSlot(2, new ItemStack("stone", 3));
        inv.SetSlot(5, new ItemStack("wood_log", 9)); // the "pinned" slot holds something else

        client.Send(NetCodec.Encode(new ShapeCraftIntent
        {
            SourceItemKey = "stone",
            Shape = (int)BlockShape.Dome,
            Count = 3,
            Slot = 5,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal("wood_log", inv.Slots[5]!.Item);      // untouched
        Assert.Equal(3, inv.CountOf("stone#s03"));         // output stored elsewhere, nothing destroyed
        Assert.Equal(0, inv.CountOf("stone"));
    }

    [Fact]
    public void TintCraft_WholeStack_SucceedsInAFullInventory()
    {
        var (server, client, _) = Start("fullinv");
        var inv = server.Sessions[1].State.Inventory;
        int mudSlot = -1;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is null || inv.Slots[i]!.IsEmpty)
            {
                if (mudSlot < 0)
                {
                    mudSlot = i;
                    inv.SetSlot(i, new ItemStack("mud", 12));
                }
                else
                {
                    inv.SetSlot(i, new ItemStack("wood_log", 1)); // fill every remaining slot
                }
            }
        }

        Assert.Equal(-1, inv.FirstEmptySlot()); // genuinely full — the old CanFit would refuse this craft

        client.Send(NetCodec.Encode(new TintCraftIntent
        {
            SourceItemKey = "mud",
            Tint = 0x3F6FB0,
            Count = 12,
            Slot = mudSlot,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal("mud#t3f6fb0", inv.Slots[mudSlot]!.Item); // 1:1 exchange reused the source's own slot
        Assert.Equal(12, inv.Slots[mudSlot]!.Count);
        Assert.Equal(0, inv.CountOf("mud"));
    }

    // --- the painted-item round trip ---

    [Fact]
    public void PaintCraft_MintsAPaintedItem_AndPlaceMineRoundTripsTheDesign()
    {
        var (server, client, repo) = Start("paintitem");
        var session = server.Sessions[1];
        var inv = session.State.Inventory;
        inv.SetSlot(2, new ItemStack("mud", 2));

        client.Send(NetCodec.Encode(new PaintCraftIntent
        {
            SourceItemKey = "mud",
            Pixels = Pixels(),
            Count = 2,
            Slot = 2,
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // The design registered once (id 1) and the whole stack carries it, pinned to the slot.
        var stored = Assert.Single(repo.ListPaintDesigns());
        Assert.Equal(Pixels(), stored.Pixels);
        string painted = ItemKey.Compose("mud", 0, 0, 0, stored.Id);
        Assert.Equal(painted, inv.Slots[2]!.Item);
        Assert.Equal(2, inv.Slots[2]!.Count);

        // Placing stamps the design into the cell's descriptor… (an empty, supported cell within reach —
        // whatever the seed-1 terrain generated there must not decide this test)
        session.State.Position = new Vector3f(0.5f, 66f, 0.5f);
        var pos = new Vector3i(0, 65, 0);
        server.World.SetBlock(new Vector3i(0, 64, 0), _content.GetBlock("stone")!.NumericId);
        server.World.SetBlock(pos, BlockId.Air);
        server.PlaceBlock(session.State.PlayerId, pos.X, pos.Y, pos.Z, painted);
        Assert.Equal(stored.Id, ShapeCode.DesignOf(server.World.GetShape(pos)));

        // …and mining recovers it into the drop, so the stack merges with the unplaced remainder.
        server.MineBlockOnce(session.State.PlayerId, pos.X, pos.Y, pos.Z);
        Assert.Equal(2, inv.CountOf(painted));
    }

    [Fact]
    public void PaintCraft_EmptyPixels_StripsTheDesignAgain()
    {
        var (server, client, _) = Start("paintstrip");
        var inv = server.Sessions[1].State.Inventory;
        inv.SetSlot(0, new ItemStack("mud", 3));

        client.Send(NetCodec.Encode(new PaintCraftIntent { SourceItemKey = "mud", Pixels = Pixels(), Count = 3, Slot = 0 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        string painted = inv.Slots[0]!.Item;
        Assert.NotEqual(0, ItemKey.Design(painted));

        client.Send(NetCodec.Encode(new PaintCraftIntent { SourceItemKey = painted, Pixels = string.Empty, Count = 3, Slot = 0 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal("mud", inv.Slots[0]!.Item); // back to the plain material, same slot
        Assert.Equal(3, inv.Slots[0]!.Count);
    }

    [Fact]
    public void PaintCraft_RefusesANonTintableSource()
    {
        var (server, client, _) = Start("paintgate");
        var inv = server.Sessions[1].State.Inventory;
        inv.SetSlot(0, new ItemStack("iron_ingot", 2));

        client.Send(NetCodec.Encode(new PaintCraftIntent { SourceItemKey = "iron_ingot", Pixels = Pixels(), Count = 2, Slot = 0 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(2, inv.CountOf("iron_ingot")); // nothing consumed, nothing produced
        Assert.Equal("iron_ingot", inv.Slots[0]!.Item);
    }

    [Fact]
    public void Place_WithAnUnknownDesignId_PlacesThePlainMaterial()
    {
        var (server, _, _) = Start("paintghost");
        var session = server.Sessions[1];
        session.State.Position = new Vector3f(0.5f, 66f, 0.5f);
        // An item carrying a design this save never registered (imported save, wiped id, hand edit).
        session.State.Inventory.SetSlot(0, new ItemStack("mud#p00ff", 1));

        var pos = new Vector3i(0, 65, 0);
        server.World.SetBlock(new Vector3i(0, 64, 0), _content.GetBlock("stone")!.NumericId);
        server.World.SetBlock(pos, BlockId.Air);
        server.PlaceBlock(session.State.PlayerId, pos.X, pos.Y, pos.Z, "mud#p00ff");

        Assert.Equal(_content.GetBlock("mud")!.NumericId.Value, server.World.GetBlock(pos).Value); // it DID place
        Assert.Equal(0, ShapeCode.DesignOf(server.World.GetShape(pos))); // …but with no geometry nobody can resolve
    }
}

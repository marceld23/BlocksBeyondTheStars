// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A full inventory must never destroy items. Reported by a player as "Items futsch": he crafted glass with
/// all 24 slots occupied and no ship cargo in reach — the glass never appeared, the silicate that went into it
/// was gone, and the client still showed a success toast. Every path that consumes something before producing
/// something has to check for room FIRST and refuse cleanly instead.
/// </summary>
public sealed class InventoryFullSafetyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public InventoryFullSafetyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_invfull_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "invfull"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "invfull",
            Seed = 11,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Occupies every slot of an inventory with a full stack of <paramref name="filler"/>, so nothing
    /// new can be stored and no existing stack has room to be topped up.</summary>
    private void FillEverySlot(Inventory inv, string filler = "stone")
    {
        int max = _content.MaxStackOf(filler);
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, new ItemStack(filler, max));
        }
    }

    /// <summary>Leaves the player with a completely full inventory that still holds the crafting inputs: one
    /// slot carries a big silicate stack (so consuming 3 does NOT free the slot), the rest is packed with
    /// stone. This is the situation in which the result has nowhere to go.</summary>
    private static void PutInputsInAFullInventory(Inventory inv, string inputItem, int inputStack)
    {
        inv.SetSlot(0, new ItemStack(inputItem, inputStack));
    }

    [Fact]
    public void Craft_WithNoRoomForTheResult_ChangesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus"); // aboard → the ship's workshop module is available
            FillEverySlot(p.State.Inventory);
            FillEverySlot(server.Ship.Cargo); // and no cargo space to spill into either
            PutInputsInAFullInventory(p.State.Inventory, "silicate", 300);

            server.Craft("Justus", "glass", 1); // 3 silicate → 1 glass

            Assert.Equal(0, p.State.Inventory.CountOf("glass"));    // no phantom output …
            Assert.Equal(300, p.State.Inventory.CountOf("silicate")); // … and the inputs are UNTOUCHED
        }
    }

    [Fact]
    public void Craft_WithRoomForTheResult_StillWorks()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Inventory.Add("silicate", 3, _content.MaxStackOf("silicate"));

            server.Craft("Justus", "glass", 1);

            Assert.Equal(1, p.State.Inventory.CountOf("glass"));
            Assert.Equal(0, p.State.Inventory.CountOf("silicate"));
        }
    }

    [Fact]
    public void Craft_SpillsIntoShipCargo_WhenPersonalInventoryIsFull()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus"); // aboard → cargo counts
            FillEverySlot(p.State.Inventory);
            PutInputsInAFullInventory(p.State.Inventory, "silicate", 300);

            server.Craft("Justus", "glass", 1);

            // Personal inventory has no free slot, but the cargo hold does — the craft must succeed there.
            Assert.Equal(1, server.Ship.Cargo.CountOf("glass"));
            Assert.Equal(297, p.State.Inventory.CountOf("silicate"));
        }
    }

    [Fact]
    public void BatchCraft_ThatOnlyPartlyFits_IsRefusedAsAWhole()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var inv = p.State.Inventory;
            FillEverySlot(inv);
            FillEverySlot(server.Ship.Cargo);
            PutInputsInAFullInventory(inv, "silicate", 300);

            // One free-ish target: a partly filled glass stack with room for 2 more, but the batch makes 5.
            int max = _content.MaxStackOf("glass");
            inv.SetSlot(1, new ItemStack("glass", max - 2));

            server.Craft("Justus", "glass", 5);

            // All-or-nothing: 2 of the 5 would fit, so the whole batch is refused rather than 3 destroyed.
            Assert.Equal(max - 2, inv.CountOf("glass"));
            Assert.Equal(300, inv.CountOf("silicate"));
        }
    }

    [Fact]
    public void Disassemble_WithNoRoomForTheSalvage_KeepsTheItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var inv = p.State.Inventory;
            FillEverySlot(inv);
            FillEverySlot(server.Ship.Cargo);
            inv.SetSlot(0, new ItemStack("iron_plate", 4)); // the item to take apart, in an otherwise full inv

            server.Disassemble("Justus", "iron_plate");

            // Salvage (iron_ingot) needs a slot of its own; refusing keeps the plate instead of eating it.
            Assert.Equal(4, inv.CountOf("iron_plate"));
            Assert.Equal(0, inv.CountOf("iron_ingot"));
        }
    }

    /// <summary>Since #853 a full inventory no longer stops the drill: the block breaks and its drop lands on
    /// the ground as a packet. What must never happen — and what this test still guards — is the drop being
    /// destroyed. (It used to be guarded by refusing the break; the block stayed standing.)</summary>
    [Fact]
    public void Mining_WithNoRoomForTheDrop_BreaksTheBlockAndDropsItOnTheGround()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false; // no cargo to spill into
            FillEverySlot(p.State.Inventory);

            // A stone block right next to the player, well within reach, plus a drill to swing at it.
            var pos = new Vector3i(12, 60, 12);
            p.State.Position = new Vector3f(pos.X + 1.5f, pos.Y + 0.5f, pos.Z + 0.5f);
            server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);
            p.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

            server.MineBlock("Justus", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir); // digging on is the whole point of #853 …
            var packet = Assert.Single(server.DropPackets);
            Assert.Equal(1, packet.Items.Count(s => s.Item == "stone" && s.Count > 0)); // … and the stone is not lost
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
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}

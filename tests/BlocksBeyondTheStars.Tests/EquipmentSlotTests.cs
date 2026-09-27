// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The equipment slots (#2110, Justus' "Inventar wie in Minecraft", Marcel's decisions): gear works only while WORN in a
/// suit slot; wearing is a swap between a backpack slot and the slot the piece belongs to; a save from before the slots
/// migrates its best pieces into them once; the boots soften a fall; the 36-slot backpack; the crafting pool sees the
/// worn piece for an upgrade.
/// </summary>
public sealed class EquipmentSlotTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EquipmentSlotTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_equip_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "equip"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "equip", Seed = 3, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int SlotOf(Inventory inv, string item)
    {
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void TheData_NamesASlotForEveryPieceOfSuitGear_AndTheBackpackHasThirtySixSlots()
    {
        Assert.Equal(36, PlayerState.PersonalSlots);
        Assert.Equal(36, new PlayerState().Inventory.SlotCount);
        Assert.Equal(EquipSlots.Count, new PlayerState().Equipment.SlotCount);

        foreach (var key in new[] { "helmet", "armor_chest", "stealth_suit", "armor_legs", "boots", "jetpack", "oxygen_tank_1", "oxygen_tank_2",
                     "oxygen_tank_3", "suit_liner_1", "suit_liner_2", "suit_liner_3", "suit_lamp", "oxygen_extractor", "comm_radio", "system_radio",
                     "galaxy_radio", "radar_scanner" })
        {
            var def = _content.GetItem(key)!;
            Assert.NotNull(EquipSlots.Parse(def.EquipSlot));
            Assert.True(SuitEquipment.IsSuitGear(def));
            Assert.Equal(1, def.MaxStack);
        }

        Assert.Equal(EquipSlot.Head, EquipSlots.Parse(_content.GetItem("helmet")!.EquipSlot));
        Assert.Equal(EquipSlot.Feet, EquipSlots.Parse(_content.GetItem("boots")!.EquipSlot));
        Assert.Equal(EquipSlot.Tank, EquipSlots.Parse(_content.GetItem("oxygen_tank_3")!.EquipSlot));
        Assert.True(EquipSlots.Accepts(EquipSlot.Module2, "module"));
        Assert.False(EquipSlots.Accepts(EquipSlot.Head, "chest"));
        Assert.Null(_content.GetItem("stone")!.EquipSlot);
        Assert.Null(_content.GetItem("suit_teleporter")!.EquipSlot); // a held gadget, not worn
        Assert.True(_content.GetItem("boots")!.FallProtection > 0f);
    }

    [Fact]
    public void Wearing_IsASwapWithTheSlot_AndOnlyWornGearCounts()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Dresser");
            var inv = p.State.Inventory;
            var eq = p.State.Equipment;
            inv.Add("helmet", 1, 1);
            inv.Add("stone", 5, 64);
            int helmetSlot = SlotOf(inv, "helmet");
            int stoneSlot = SlotOf(inv, "stone");

            // Carried in the backpack: no effect. The client's own formula agrees (the same predicate).
            Assert.Equal(SuitEquipment.BaseOxygen, SuitEquipment.MaxOxygen(_content.Items.Values, key => eq.Has(key, 1)));

            server.EquipItemForTest("Dresser", stoneSlot);
            Assert.Equal(5, inv.CountOf("stone"));                 // not wearable — refused
            Assert.Equal(0, eq.CountOf("stone"));

            server.EquipItemForTest("Dresser", helmetSlot);
            Assert.Equal(1, eq.CountOf("helmet"));
            Assert.Equal("helmet", eq.Slots[(int)EquipSlot.Head]!.Item);
            Assert.Null(inv.Slots[helmetSlot]);                    // the backpack slot is free now
            Assert.Equal(0.15f, SuitEquipment.ArmorResistance(_content.Items.Values, key => eq.Has(key, 1)), 3);

            // A second helmet swaps: the worn one comes back into the backpack slot the new one left.
            inv.Add("helmet", 1, 1);
            int second = SlotOf(inv, "helmet");
            server.EquipItemForTest("Dresser", second, (int)EquipSlot.Head);
            Assert.Equal("helmet", inv.Slots[second]!.Item);
            Assert.Equal("helmet", eq.Slots[(int)EquipSlot.Head]!.Item);

            // The wrong slot is refused.
            inv.Add("armor_legs", 1, 1);
            int legs = SlotOf(inv, "armor_legs");
            server.EquipItemForTest("Dresser", legs, (int)EquipSlot.Head);
            Assert.Equal(1, inv.CountOf("armor_legs"));
            Assert.Equal(0, eq.CountOf("armor_legs"));

            // The starter kit's lamp was put on at the join (the one-time migration) — take it off first.
            Assert.Equal("suit_lamp", eq.Slots[(int)EquipSlot.Module1]!.Item);
            server.UnequipItemForTest("Dresser", (int)EquipSlot.Module1);
            Assert.Null(eq.Slots[(int)EquipSlot.Module1]);

            // Two modules: the second one takes the second module slot.
            inv.Add("comm_radio", 1, 1);
            server.EquipItemForTest("Dresser", SlotOf(inv, "suit_lamp"));
            server.EquipItemForTest("Dresser", SlotOf(inv, "comm_radio"));
            Assert.Equal("suit_lamp", eq.Slots[(int)EquipSlot.Module1]!.Item);
            Assert.Equal("comm_radio", eq.Slots[(int)EquipSlot.Module2]!.Item);

            // Taking off goes past the quick-bar into the first free backpack slot.
            server.UnequipItemForTest("Dresser", (int)EquipSlot.Module2);
            Assert.Null(eq.Slots[(int)EquipSlot.Module2]);
            int back = SlotOf(inv, "comm_radio");
            Assert.True(back >= 9, $"taken off into the backpack, not the quick-bar (slot {back})");
        }
    }

    [Fact]
    public void TakingOff_IntoAFullBackpack_IsRefused()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Packed");
            var inv = p.State.Inventory;
            inv.Add("helmet", 1, 1);
            server.EquipItemForTest("Packed", SlotOf(inv, "helmet"));
            for (int i = 0; i < inv.SlotCount; i++)
            {
                if (inv.Slots[i] is null)
                {
                    inv.SetSlot(i, new ItemStack("stone", 1));
                }
            }

            server.UnequipItemForTest("Packed", (int)EquipSlot.Head);
            Assert.Equal("helmet", p.State.Equipment.Slots[(int)EquipSlot.Head]!.Item); // still worn
        }
    }

    [Fact]
    public void AnOldSave_MovesItsBestGearIntoTheSlots_OnceOnJoin()
    {
        var state = new PlayerState { EquipmentInitialised = false };
        state.Inventory.Add("helmet", 1, 1);
        state.Inventory.Add("oxygen_tank_1", 1, 1);
        state.Inventory.Add("oxygen_tank_3", 1, 1);
        state.Inventory.Add("suit_liner_2", 1, 1);
        state.Inventory.Add("suit_lamp", 1, 1);
        state.Inventory.Add("comm_radio", 1, 1);
        state.Inventory.Add("oxygen_extractor", 1, 1);
        state.Inventory.Add("stone", 12, 64);

        int moved = SuitEquipment.MigrateIntoSlots(state.Inventory, state.Equipment, key => _content.GetItem(key));

        Assert.Equal(5, moved); // helmet, the best tank, the liner, two of the three modules
        Assert.Equal("helmet", state.Equipment.Slots[(int)EquipSlot.Head]!.Item);
        Assert.Equal("oxygen_tank_3", state.Equipment.Slots[(int)EquipSlot.Tank]!.Item);
        Assert.Equal("suit_liner_2", state.Equipment.Slots[(int)EquipSlot.Liner]!.Item);
        Assert.NotNull(state.Equipment.Slots[(int)EquipSlot.Module1]);
        Assert.NotNull(state.Equipment.Slots[(int)EquipSlot.Module2]);
        Assert.Null(state.Equipment.Slots[(int)EquipSlot.Feet]);
        Assert.Equal(1, state.Inventory.CountOf("oxygen_tank_1")); // the lesser tank stays in the pack
        Assert.Equal(12, state.Inventory.CountOf("stone"));
        Assert.Equal(1, state.Inventory.CountOf("suit_lamp") + state.Inventory.CountOf("comm_radio") + state.Inventory.CountOf("oxygen_extractor"));

        // Running it again moves nothing more (the tank slot is taken, the lesser tank stays).
        Assert.Equal(0, SuitEquipment.MigrateIntoSlots(state.Inventory, state.Equipment, key => _content.GetItem(key)));
    }

    [Fact]
    public void TheSnapshot_RoundTripsTheWornGear_AndWidensAnOldBackpack()
    {
        var state = new PlayerState { PlayerId = "p1", Name = "Saver", EquipmentInitialised = true };
        state.Equipment.SetSlot((int)EquipSlot.Head, new ItemStack("helmet", 1));
        state.Inventory.SetSlot(30, new ItemStack("stone", 3)); // a slot only the 36-wide pack has

        var snap = StateMapper.ToSnapshot(state);
        var back = StateMapper.FromSnapshot(snap);
        Assert.Equal(36, back.Inventory.SlotCount);
        Assert.Equal("helmet", back.Equipment.Slots[(int)EquipSlot.Head]!.Item);
        Assert.Equal(3, back.Inventory.Slots[30]!.Count);
        Assert.True(back.EquipmentInitialised);

        // A pre-#2110 snapshot: 24 slots, no equipment, not initialised.
        var old = new PlayerSnapshot { Id = "p2", Name = "Old", InventorySlotCount = 24, Equipment = null! };
        old.Inventory.Add(new InventorySlotDto { Index = 3, Item = "helmet", Count = 1 });
        var restored = StateMapper.FromSnapshot(old);
        Assert.Equal(36, restored.Inventory.SlotCount);
        Assert.Equal("helmet", restored.Inventory.Slots[3]!.Item);
        Assert.False(restored.EquipmentInitialised);
        Assert.Equal(0, restored.Equipment.CountOf("helmet"));
    }

    [Fact]
    public void WornBoots_SoftenAHardLanding()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var bare = server.AddLocalPlayer("Bare");
            var shod = server.AddLocalPlayer("Shod");
            bare.State.AboardShip = false;
            shod.State.AboardShip = false;
            shod.State.Inventory.Add("boots", 1, 1);
            server.EquipItemForTest("Shod", SlotOf(shod.State.Inventory, "boots"));
            Assert.Equal("boots", shod.State.Equipment.Slots[(int)EquipSlot.Feet]!.Item);

            server.FallDamageForTest("Bare", 24f);
            server.FallDamageForTest("Shod", 24f);
            Assert.True(bare.State.Health < 100f, "a hard landing hurts");
            Assert.True(shod.State.Health > bare.State.Health, $"boots soften it ({shod.State.Health} vs {bare.State.Health})");
        }
    }

    [Fact]
    public void TheCraftingPool_SeesTheWornPiece_ForAnUpgrade()
    {
        var state = new PlayerState { AboardShip = false };
        state.Equipment.SetSlot((int)EquipSlot.Tank, new ItemStack("oxygen_tank_1", 1));
        var pool = new MaterialPool(_content, state, new ShipState());
        Assert.Equal(1, pool.Count("oxygen_tank_1"));
        pool.Remove(new[] { new ItemAmount("oxygen_tank_1", 1) });
        Assert.Equal(0, state.Equipment.CountOf("oxygen_tank_1"));
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
            // a locked temp dir is no test failure
        }
    }
}

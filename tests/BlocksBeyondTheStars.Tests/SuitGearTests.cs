// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The paper-doll suit, server/shared half: four module slots (#2293) and old saves that widen into them; wearing a
/// piece straight from the cargo hold (#2289); the second gear tier (#2294–#2297) — titanium plates, spring boots with
/// their jump boost, the glider and its glide flag on the wire, the suit battery that lifts the suit energy to 150; and the
/// one gear-look mask (<see cref="GearLook"/>) server and client both draw the body from.
/// </summary>
public sealed class SuitGearTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SuitGearTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_suitgear_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string name, IServerTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
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

    private static void FillBackpack(Inventory inv)
    {
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is null)
            {
                inv.SetSlot(i, new ItemStack("stone", 1));
            }
        }
    }

    private static System.Func<string, bool> Wearing(params string[] keys) => key => keys.Contains(key);

    // ---------------- Four module slots (#2293) ----------------

    [Fact]
    public void TheSuit_HasElevenSlots_FourOfThemModules()
    {
        Assert.Equal(11, EquipSlots.Count);
        Assert.Equal(11, new PlayerState().Equipment.SlotCount);
        Assert.Equal(new[] { EquipSlot.Module1, EquipSlot.Module2, EquipSlot.Module3, EquipSlot.Module4 }, EquipSlots.ModuleSlots);
        Assert.All(EquipSlots.ModuleSlots, s => Assert.True(EquipSlots.IsModule(s)));
        Assert.False(EquipSlots.IsModule(EquipSlot.Liner));
        Assert.Equal("ui.equip.slot.module", EquipSlots.LabelKey(EquipSlot.Module4));
        Assert.Equal("ui.equip.slot.back", EquipSlots.LabelKey(EquipSlot.Back));
    }

    [Fact]
    public void AModule_TakesTheFirstFreeModuleSlot_AndAFifthSwapsTheFirst()
    {
        var server = Started("modules");
        var p = server.AddLocalPlayer("Tinker");
        var inv = p.State.Inventory;
        var eq = p.State.Equipment;
        Assert.Equal("suit_lamp", eq.Slots[(int)EquipSlot.Module1]!.Item); // the starter lamp is worn from the start (#2288)

        foreach (var module in new[] { "comm_radio", "radar_scanner", "climbing_gloves" })
        {
            inv.Add(module, 1, 1);
            server.EquipItemForTest("Tinker", SlotOf(inv, module));
        }

        Assert.Equal("comm_radio", eq.Slots[(int)EquipSlot.Module2]!.Item);
        Assert.Equal("radar_scanner", eq.Slots[(int)EquipSlot.Module3]!.Item);
        Assert.Equal("climbing_gloves", eq.Slots[(int)EquipSlot.Module4]!.Item);

        // All four taken: the fifth swaps with the first module slot, the lamp comes back into the slot it left.
        inv.Add("suit_battery", 1, 1);
        int from = SlotOf(inv, "suit_battery");
        server.EquipItemForTest("Tinker", from);
        Assert.Equal("suit_battery", eq.Slots[(int)EquipSlot.Module1]!.Item);
        Assert.Equal("suit_lamp", inv.Slots[from]!.Item);

        // A named module slot works too, and a module never fits a body slot.
        server.EquipItemForTest("Tinker", from, (int)EquipSlot.Module3);
        Assert.Equal("suit_lamp", eq.Slots[(int)EquipSlot.Module3]!.Item);
        Assert.Equal("radar_scanner", inv.Slots[from]!.Item);
        server.EquipItemForTest("Tinker", from, (int)EquipSlot.Back);
        Assert.Equal("radar_scanner", inv.Slots[from]!.Item);
    }

    [Fact]
    public void ASaveWithNineSlots_LoadsWithEleven_AndKeepsEveryPiece()
    {
        // A v10 save: the worn row had nine slots (0..8), sparse by index like every inventory in the snapshot.
        var old = new PlayerSnapshot { Id = "p9", Name = "Nine", InventorySlotCount = 36, EquipmentInitialised = true };
        string[] worn = { "helmet", "armor_chest", "armor_legs", "boots", "jetpack", "oxygen_tank_2", "suit_liner_1", "suit_lamp", "comm_radio" };
        for (int i = 0; i < worn.Length; i++)
        {
            old.Equipment.Add(new InventorySlotDto { Index = i, Item = worn[i], Count = 1 });
        }

        var restored = StateMapper.FromSnapshot(old);
        Assert.Equal(EquipSlots.Count, restored.Equipment.SlotCount);
        for (int i = 0; i < worn.Length; i++)
        {
            Assert.Equal(worn[i], restored.Equipment.Slots[i]!.Item);
        }

        Assert.Null(restored.Equipment.Slots[(int)EquipSlot.Module3]);
        Assert.Null(restored.Equipment.Slots[(int)EquipSlot.Module4]);
        Assert.True(restored.EquipmentInitialised);

        // And the widened row round-trips with the new slots filled.
        restored.Equipment.SetSlot((int)EquipSlot.Module4, new ItemStack("radar_scanner", 1));
        var again = StateMapper.FromSnapshot(StateMapper.ToSnapshot(restored));
        Assert.Equal("radar_scanner", again.Equipment.Slots[(int)EquipSlot.Module4]!.Item);
    }

    // ---------------- Wear straight from the cargo hold (#2289) ----------------

    [Fact]
    public void WearingFromTheHold_PutsItOn_AndTheOldPieceGoesIntoTheBackpack()
    {
        var server = Started("hold_wear");
        var p = server.AddLocalPlayer("Pilot");
        p.State.AboardShip = true;
        var cargo = server.Ship.Cargo;
        var eq = p.State.Equipment;
        var inv = p.State.Inventory;

        cargo.SetSlot(0, new ItemStack("helmet", 1));
        server.EquipItemForTest("Pilot", 0, fromCargo: true);
        Assert.Equal("helmet", eq.Slots[(int)EquipSlot.Head]!.Item);
        Assert.Null(cargo.Slots[0]);

        // A better helmet from the hold: the old one goes to the player — past the quick-bar — not back into the hold.
        cargo.SetSlot(3, new ItemStack("titan_helmet", 1));
        server.EquipItemForTest("Pilot", 3, fromCargo: true);
        Assert.Equal("titan_helmet", eq.Slots[(int)EquipSlot.Head]!.Item);
        Assert.Null(cargo.Slots[3]);
        int back = SlotOf(inv, "helmet");
        Assert.True(back >= 9, $"the old helmet went into the backpack past the quick-bar (slot {back})");
    }

    [Fact]
    public void WearingFromTheHold_WithAFullBackpack_SwapsIntoTheHoldSlot()
    {
        var server = Started("hold_full");
        var p = server.AddLocalPlayer("Pilot");
        p.State.AboardShip = true;
        var cargo = server.Ship.Cargo;
        p.State.Equipment.SetSlot((int)EquipSlot.Head, new ItemStack("titan_helmet", 1));
        FillBackpack(p.State.Inventory);

        cargo.SetSlot(4, new ItemStack("helmet", 1));
        server.EquipItemForTest("Pilot", 4, fromCargo: true);
        Assert.Equal("helmet", p.State.Equipment.Slots[(int)EquipSlot.Head]!.Item);
        Assert.Equal("titan_helmet", cargo.Slots[4]!.Item);
        Assert.Equal(0, p.State.Inventory.CountOf("titan_helmet"));
    }

    [Fact]
    public void WearingFromTheHold_IsRefused_OffShip_InTheWrongSlot_AndForNonGear()
    {
        var transport = new RecordingTransport();
        var server = Started("hold_refuse", transport);
        var p = server.AddLocalPlayer("Pilot");
        var cargo = server.Ship.Cargo;
        var eq = p.State.Equipment;
        cargo.SetSlot(0, new ItemStack("helmet", 1));
        cargo.SetSlot(1, new ItemStack("armor_legs", 1));
        cargo.SetSlot(2, new ItemStack("stone", 20));

        // Not aboard: the hold is out of reach.
        p.State.AboardShip = false;
        server.EquipItemForTest("Pilot", 0, fromCargo: true);
        Assert.Equal("helmet", cargo.Slots[0]!.Item);
        Assert.Null(eq.Slots[(int)EquipSlot.Head]);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Reason == "@srv.misc.aboard_for_cargo");

        p.State.AboardShip = true;

        // The wrong slot: leg armour does not go on the head.
        server.EquipItemForTest("Pilot", 1, (int)EquipSlot.Head, fromCargo: true);
        Assert.Equal("armor_legs", cargo.Slots[1]!.Item);
        Assert.Null(eq.Slots[(int)EquipSlot.Head]);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Reason == "@srv.equip.wrong_slot");

        // Not gear at all.
        server.EquipItemForTest("Pilot", 2, fromCargo: true);
        Assert.Equal(20, cargo.Slots[2]!.Count);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Reason == "@srv.equip.not_wearable");
        Assert.Equal(0, eq.CountOf("stone"));
    }

    [Fact]
    public void TheIntents_CarryTheHoldFlag_AndTheGlide()
    {
        var equip = Assert.IsType<EquipItemIntent>(NetCodec.Decode(NetCodec.Encode(new EquipItemIntent { FromSlot = 3, FromCargo = true })));
        Assert.True(equip.FromCargo);
        Assert.Equal(3, equip.FromSlot);
        Assert.Equal(-1, equip.Slot);
        Assert.False(new EquipItemIntent().FromCargo); // an older client's intent means the backpack

        Assert.True(Assert.IsType<SetGlidingIntent>(NetCodec.Decode(NetCodec.Encode(new SetGlidingIntent { Active = true }))).Active);
        var presence = Assert.IsType<PlayerPresence>(NetCodec.Decode(NetCodec.Encode(new PlayerPresence { PlayerId = "a", Gliding = true, Gear = GearLook.Glider })));
        Assert.True(presence.Gliding);
        Assert.Equal(GearLook.Glider, presence.Gear);
    }

    // ---------------- The second tier: data and formulas (#2294–#2297) ----------------

    [Fact]
    public void TheSecondTier_IsResearchableCraftableGear_ThatOutclassesTheFirst()
    {
        var expected = new (string Key, EquipSlot Slot, string Prerequisite)[]
        {
            ("titan_helmet", EquipSlot.Head, "helmet"), ("titan_chest", EquipSlot.Chest, "armor_chest"),
            ("titan_legs", EquipSlot.Legs, "armor_legs"), ("spring_boots", EquipSlot.Feet, "boots"),
            ("glider", EquipSlot.Back, "jetpack"), ("suit_battery", EquipSlot.Module1, "jetpack"),
        };
        foreach (var (key, slot, prerequisite) in expected)
        {
            var def = _content.GetItem(key)!;
            Assert.Equal(slot, EquipSlots.Parse(def.EquipSlot));
            Assert.Equal(1, def.MaxStack);
            Assert.True(SuitEquipment.IsSuitGear(def), key);

            var bp = _content.GetBlueprint(key)!;
            Assert.Equal("Suit", bp.Category.ToString());
            Assert.Contains(prerequisite, bp.Prerequisites);

            var recipe = _content.GetRecipe(key)!;
            Assert.Equal(CraftingStation.Workshop, recipe.Station);
            Assert.Equal(key, recipe.RequiredBlueprint);
            Assert.Equal(key, recipe.Outputs.Single().Item);
        }

        foreach (var (better, plain) in new[] { ("titan_helmet", "helmet"), ("titan_chest", "armor_chest"), ("titan_legs", "armor_legs"), ("spring_boots", "boots") })
        {
            Assert.True(_content.GetItem(better)!.ArmorResistance > _content.GetItem(plain)!.ArmorResistance, better);
            Assert.True(SuitEquipment.Rank(_content.GetItem(better)!) > SuitEquipment.Rank(_content.GetItem(plain)!), better);
        }

        Assert.Contains(_content.GetRecipe("spring_boots")!.Inputs, i => i.Item == "boots" && i.Count == 1); // an upgrade
    }

    [Fact]
    public void SpringBoots_BoostTheJump_AndOnlyTheBestPieceCounts()
    {
        var items = _content.Items.Values;
        Assert.Equal(0f, SuitEquipment.JumpBoost(items, Wearing()));
        Assert.Equal(0f, SuitEquipment.JumpBoost(items, Wearing("boots")));
        Assert.Equal(0.6f, SuitEquipment.JumpBoost(items, Wearing("spring_boots")), 3);
        Assert.Equal(0.5f, SuitEquipment.FallProtection(items, Wearing("spring_boots")), 3); // and a softer landing

        var springy = new[] { new ItemDefinition { Key = "a", JumpBoost = 0.4f }, new ItemDefinition { Key = "b", JumpBoost = 5f } };
        Assert.Equal(0.4f, SuitEquipment.JumpBoost(springy, Wearing("a")), 3);
        Assert.Equal(SuitEquipment.MaxJumpBoost, SuitEquipment.JumpBoost(springy, Wearing("a", "b"))); // capped, not summed
    }

    [Fact]
    public void TheSuitBattery_LiftsTheMaximum_AndTiersDoNotStack()
    {
        var items = _content.Items.Values;
        Assert.Equal(SuitEquipment.BaseSuitEnergy, SuitEquipment.MaxSuitEnergy(items, Wearing()));
        Assert.Equal(100f, SuitEquipment.BaseSuitEnergy);
        Assert.Equal(150f, SuitEquipment.MaxSuitEnergy(items, Wearing("suit_battery")));

        var cells = new[] { new ItemDefinition { Key = "a", SuitEnergyBonus = 50f }, new ItemDefinition { Key = "b", SuitEnergyBonus = 100f } };
        Assert.Equal(200f, SuitEquipment.MaxSuitEnergy(cells, Wearing("a", "b")));
    }

    // ---------------- The suit battery on the server (#2297) ----------------

    [Fact]
    public void AFreshPilot_StartsWithAFullHundred()
    {
        var server = Started("fresh");
        var p = server.AddLocalPlayer("Rookie");
        Assert.Equal(100f, p.State.SuitEnergy);
    }

    [Fact]
    public void TheSuitBattery_RechargesTo150_AndTakingItOff_ClampsTo100()
    {
        var server = Started("battery");
        var charged = server.AddLocalPlayer("Charged");
        var bare = server.AddLocalPlayer("Bare");
        foreach (var s in new[] { charged, bare })
        {
            s.State.AboardShip = false;
            s.State.Position = new Vector3f(0.5f, 64f, 0.5f);
            s.State.SuitEnergy = 90f;
        }

        charged.State.Inventory.Add("suit_battery", 1, 1);
        server.EquipItemForTest("Charged", SlotOf(charged.State.Inventory, "suit_battery"));
        Assert.Equal("suit_battery", charged.State.Equipment.Slots[(int)EquipSlot.Module2]!.Item);

        // The heal tank — the one off-ship recharge — fills each suit up to ITS maximum.
        server.World.SetBlock(new Vector3i(2, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
        for (int i = 0; i < 8; i++)
        {
            server.TickForTest(2.0);
        }

        Assert.True(charged.State.SuitEnergy > 140f && charged.State.SuitEnergy <= 150f, $"charged to the battery ({charged.State.SuitEnergy})");
        Assert.True(bare.State.SuitEnergy <= 100f, $"no battery, no more than 100 ({bare.State.SuitEnergy})");

        // Taking the battery off: the extra charge has nowhere to live.
        charged.State.SuitEnergy = 150f;
        server.UnequipItemForTest("Charged", (int)EquipSlot.Module2);
        Assert.Equal(100f, charged.State.SuitEnergy);
        Assert.Equal(1, charged.State.Inventory.CountOf("suit_battery"));
    }

    // ---------------- The glider (#2296) ----------------

    [Fact]
    public void Gliding_NeedsAWornGlider_AndTakingItOffEndsIt()
    {
        var transport = new RecordingTransport();
        var server = Started("glide", transport);
        var p = server.AddLocalPlayer("Flyer");

        server.SetGlidingForTest("Flyer", true);
        Assert.False(p.State.Gliding);
        Assert.Contains(transport.Sent, x => x.Msg is ActionRejected r && r.Action == "glider" && r.Reason == "@srv.equip.no_glider");

        p.State.Inventory.Add("glider", 1, 1);
        server.EquipItemForTest("Flyer", SlotOf(p.State.Inventory, "glider"));
        Assert.Equal("glider", p.State.Equipment.Slots[(int)EquipSlot.Back]!.Item);
        server.SetGlidingForTest("Flyer", true);
        Assert.True(p.State.Gliding);
        server.SetGlidingForTest("Flyer", false);
        Assert.False(p.State.Gliding);

        server.SetGlidingForTest("Flyer", true);
        server.UnequipItemForTest("Flyer", (int)EquipSlot.Back);
        Assert.False(p.State.Gliding);
    }

    [Fact]
    public void AnotherPlayer_SeesTheGlide_AndTheSecondTierLook()
    {
        var transport = new RecordingTransport();
        var server = Started("glide_presence", transport);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.AboardShip = false;
        bob.State.AboardShip = false;
        bob.State.Position = new Vector3f(6, 64, 4);
        bob.State.Equipment.SetSlot((int)EquipSlot.Head, new ItemStack("titan_helmet", 1));
        bob.State.Equipment.SetSlot((int)EquipSlot.Feet, new ItemStack("spring_boots", 1));
        bob.State.Equipment.SetSlot((int)EquipSlot.Back, new ItemStack("glider", 1));
        bob.State.Equipment.SetSlot((int)EquipSlot.Module2, new ItemStack("suit_battery", 1));
        server.SetGlidingForTest("Bob", true);

        transport.Sent.Clear();
        server.Tick(0.2);

        var seen = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence pr && pr.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .LastOrDefault();
        Assert.NotNull(seen);
        Assert.True(seen!.Gliding);
        int expected = GearLook.Helmet | GearLook.TitanHelmet | GearLook.Boots | GearLook.SpringBoots | GearLook.Glider
                       | GearLook.SuitBattery | GearLook.Lamp; // the starter lamp is worn too
        Assert.Equal(expected, seen.Gear);
    }

    // ---------------- The gear look (Shared) ----------------

    [Fact]
    public void TheGearLook_KeepsTheOldBits_AndAddsTheSecondTierOnTop()
    {
        // The old bits are on the wire for older clients — they must never move.
        Assert.Equal(1, GearLook.Helmet);
        Assert.Equal(2, GearLook.Chest);
        Assert.Equal(4, GearLook.Legs);
        Assert.Equal(8, GearLook.Jetpack);
        Assert.Equal(16, GearLook.Lamp);
        Assert.Equal(32, GearLook.Boots);
        Assert.Equal(64, GearLook.Tank);
        Assert.Equal(128, GearLook.ClimbingGloves);
        Assert.Equal(256, GearLook.ClimbingClaws);
        Assert.Equal(512, GearLook.TitanHelmet);
        Assert.Equal(1024, GearLook.TitanChest);
        Assert.Equal(2048, GearLook.TitanLegs);
        Assert.Equal(4096, GearLook.SpringBoots);
        Assert.Equal(8192, GearLook.Glider);
        Assert.Equal(16384, GearLook.SuitBattery);

        Assert.Equal(0, GearLook.Mask(Wearing()));
        Assert.Equal(GearLook.Helmet, GearLook.Mask(Wearing("helmet")));
        Assert.Equal(GearLook.Helmet | GearLook.TitanHelmet, GearLook.Mask(Wearing("titan_helmet")));
        Assert.Equal(GearLook.Chest, GearLook.Mask(Wearing("stealth_suit")));
        Assert.Equal(GearLook.Chest | GearLook.TitanChest, GearLook.Mask(Wearing("titan_chest")));
        Assert.Equal(GearLook.Legs | GearLook.TitanLegs, GearLook.Mask(Wearing("titan_legs")));
        Assert.Equal(GearLook.Boots | GearLook.SpringBoots, GearLook.Mask(Wearing("spring_boots")));
        Assert.Equal(GearLook.Glider, GearLook.Mask(Wearing("glider"))); // the glider is no jetpack
        Assert.Equal(GearLook.Jetpack, GearLook.Mask(Wearing("jetpack")));
        Assert.Equal(GearLook.Tank, GearLook.Mask(Wearing("oxygen_tank_2")));
        Assert.Equal(GearLook.SuitBattery, GearLook.Mask(Wearing("suit_battery")));
        Assert.Equal(GearLook.ClimbingGloves | GearLook.ClimbingClaws | GearLook.Lamp,
            GearLook.Mask(Wearing("climbing_gloves", "climbing_claws", "suit_lamp")));

        Assert.True(GearLook.Has(GearLook.Mask(Wearing("titan_helmet")), GearLook.Helmet));
        Assert.False(GearLook.Has(GearLook.Mask(Wearing("helmet")), GearLook.TitanHelmet));
    }

    /// <summary>A transport that records every server send so a test can assert who received what.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll()
        {
            _ = ClientConnected;
            _ = ClientDisconnected;
            _ = PayloadReceived;
        }

        public void Stop() { }

        public void Dispose() { }
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

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

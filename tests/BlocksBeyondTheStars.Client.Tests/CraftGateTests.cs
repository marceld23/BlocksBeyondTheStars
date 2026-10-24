// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2932: the crafting menu's rules live in <see cref="CraftGate"/> (Client.Core), so they are pinned here. The reason
/// order is player-visible text with a bug history (#1070 a station per recipe, #600 "inventory full" last, #1936
/// Creative crafts for free), and the fit dry-run must answer what the server's own <see cref="MaterialPool.CanFit"/>
/// answers — otherwise the button reads "craftable" and the server refuses with <c>@inventory_full</c>.
/// </summary>
public sealed class CraftGateTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private const string Plate = "iron_plate"; // stacks to 1024
    private const string Ore = "iron_ore"; // stacks to 1024
    private const string TankI = "oxygen_tank_1"; // stacks to 1

    private static NetItemStack[] Holding(params (string Item, int Count)[] stacks)
        => stacks.Select((s, i) => new NetItemStack { Slot = i, Item = s.Item, Count = s.Count }).ToArray();

    /// <summary>A backpack with <paramref name="used"/> slots taken by single tanks (stack size 1, so nothing tops them
    /// up) plus the given stacks.</summary>
    private static NetItemStack[] Backpack(int used, params (string Item, int Count)[] stacks)
        => Holding(Enumerable.Repeat((TankI, 1), used - stacks.Length).Concat(stacks).ToArray());

    private static CraftGateInput Input(NetItemStack[]? personal = null, bool creative = false, string[]? unlocked = null,
        bool marketAvailable = true, bool offeredHere = true, bool factoryAway = false, bool stationAvailable = true)
        => new(Content, creative, new HashSet<string>(unlocked ?? Array.Empty<string>()), personal ?? Array.Empty<NetItemStack>(),
            Array.Empty<NetItemStack>(), Array.Empty<NetItemStack>(), aboard: false, PlayerState.PersonalSlots, cargoSlots: 0,
            () => marketAvailable, _ => offeredHere, _ => factoryAway, _ => stationAvailable);

    private static RecipeDefinition Recipe(CraftingStation station = CraftingStation.Workshop, string? blueprint = "test_bp")
        => new()
        {
            Key = "test_recipe",
            Station = station,
            RequiredBlueprint = blueprint,
            Inputs = { new ItemAmount(Ore, 2) },
            Outputs = { new ItemAmount(Plate, 1) },
        };

    private static readonly string[] Unlocked = { "test_bp" };

    // --- the reason order ---

    [Fact]
    public void Creative_ChecksOnlyTheRoomForTheResult()
    {
        // No blueprint, no materials, no station, no market — the server crafts for free in Creative (#1936).
        foreach (var station in new[] { CraftingStation.Workshop, CraftingStation.Market, CraftingStation.Factory })
        {
            var r = Recipe(station);
            Assert.Equal(CraftBlock.None, CraftGate.Check(r, Input(creative: true, marketAvailable: false, offeredHere: false,
                factoryAway: true, stationAvailable: false)));
            Assert.Equal(CraftBlock.InventoryFull, CraftGate.Check(r, Input(Backpack(36), creative: true)));
        }
    }

    [Fact]
    public void Blueprint_IsNamedBeforeMaterialsStationAndRoom()
    {
        Assert.Equal(CraftBlock.NeedBlueprint, CraftGate.Check(Recipe(), Input(Backpack(36), stationAvailable: false)));
    }

    [Fact]
    public void Materials_AreNamedBeforeStationAndRoom()
    {
        Assert.Equal(CraftBlock.NeedMaterials,
            CraftGate.Check(Recipe(), Input(Backpack(36, (Ore, 1)), unlocked: Unlocked, stationAvailable: false)));
    }

    [Fact]
    public void Station_IsNamedBeforeTheRoom_AndInventoryFullComesLast()
    {
        var full = Backpack(36, (Ore, 2));

        Assert.Equal(CraftBlock.NeedStation, CraftGate.Check(Recipe(), Input(full, unlocked: Unlocked, stationAvailable: false)));
        Assert.Equal(CraftBlock.InventoryFull, CraftGate.Check(Recipe(), Input(full, unlocked: Unlocked)));
        Assert.Equal(CraftBlock.None, CraftGate.Check(Recipe(), Input(Holding((Ore, 2)), unlocked: Unlocked)));
    }

    [Fact]
    public void Station_IsAskedForTheRecipesOwnStation()
    {
        var asked = new List<CraftingStation>();
        var input = new CraftGateInput(Content, false, new HashSet<string>(Unlocked), Holding((Ore, 2)), Array.Empty<NetItemStack>(),
            Array.Empty<NetItemStack>(), false, PlayerState.PersonalSlots, 0, () => true, _ => true, _ => false,
            s =>
            {
                asked.Add(s);
                return s != CraftingStation.Refinery;
            });

        Assert.Equal(CraftBlock.NeedStation, CraftGate.Check(Recipe(CraftingStation.Refinery), input));
        Assert.Equal(CraftBlock.None, CraftGate.Check(Recipe(CraftingStation.Workshop), input));
        Assert.Equal(new[] { CraftingStation.Refinery, CraftingStation.Workshop }, asked);
    }

    [Fact]
    public void Market_NeedsAVendorBeforeTodaysOffer_AndNeverAsksTheStation()
    {
        var market = Recipe(CraftingStation.Market, blueprint: null);
        var owned = Holding((Ore, 2));

        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(market, Input(marketAvailable: false, offeredHere: false)));
        Assert.Equal(CraftBlock.NeedMarket, CraftGate.Check(market, Input(owned, marketAvailable: false, offeredHere: false)));
        Assert.Equal(CraftBlock.MarketElsewhere, CraftGate.Check(market, Input(owned, offeredHere: false)));
        Assert.Equal(CraftBlock.NeedMarket, CraftGate.Check(market, Input(Backpack(36, (Ore, 2)), marketAvailable: false)));
        Assert.Equal(CraftBlock.InventoryFull, CraftGate.Check(market, Input(Backpack(36, (Ore, 2)))));
        Assert.Equal(CraftBlock.None, CraftGate.Check(market, Input(owned, stationAvailable: false)));
    }

    [Fact]
    public void Factory_AsksTheTerminalInReach_NotTheStationSet()
    {
        var factory = Recipe(CraftingStation.Factory);
        var owned = Holding((Ore, 2));

        Assert.Equal(CraftBlock.GoToFactory, CraftGate.Check(factory, Input(owned, unlocked: Unlocked, factoryAway: true)));
        Assert.Equal(CraftBlock.GoToFactory, CraftGate.Check(factory, Input(Backpack(36, (Ore, 2)), unlocked: Unlocked, factoryAway: true)));
        Assert.Equal(CraftBlock.None, CraftGate.Check(factory, Input(owned, unlocked: Unlocked, stationAvailable: false)));
        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(factory, Input(unlocked: Unlocked, factoryAway: true)));
    }

    // --- the fit dry-run against the server's MaterialPool ---

    /// <summary>The inventory update the server sends (<c>GameServer.SendInventory</c>): the occupied slots of the
    /// backpack and of the worn gear, and the hold's occupied slots and size only while aboard. At the bench: every
    /// station in reach and the given blueprints researched.</summary>
    private static CraftGateInput FromServer(PlayerState player, ShipState ship, params string[] unlocked)
        => new(Content, false, new HashSet<string>(unlocked), Dump(player.Inventory),
            player.AboardShip ? Dump(ship.Cargo) : Array.Empty<NetItemStack>(), Dump(player.Equipment), player.AboardShip,
            player.Inventory.SlotCount, player.AboardShip ? ship.Cargo.SlotCount : 0,
            () => true, _ => true, _ => false, _ => true);

    private static NetItemStack[] Dump(Inventory inventory)
        => inventory.Slots
            .Select((s, i) => (Stack: s, Slot: i))
            .Where(x => x.Stack is { IsEmpty: false })
            .Select(x => new NetItemStack { Slot = x.Slot, Item = x.Stack!.Item, Count = x.Stack.Count })
            .ToArray();

    private static void Fill(Inventory inventory, int slots, string item = TankI, int count = 1)
    {
        for (int i = 0; i < slots; i++)
        {
            inventory.SetSlot(i, new ItemStack(item, count));
        }
    }

    private static (PlayerState Player, ShipState Ship, ItemAmount[] Outputs) Scenario(string name)
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        ItemAmount[] Out(params (string Item, int Count)[] outputs) => outputs.Select(o => new ItemAmount(o.Item, o.Count)).ToArray();
        switch (name)
        {
            case "empty_backpack":
                return (player, ship, Out((Plate, 1), (Ore, 2048)));
            case "full_backpack_off_ship_hold_has_room":
                Fill(player.Inventory, 36);
                return (player, ship, Out((Plate, 1)));
            case "full_backpack_aboard_spills_into_the_hold":
                Fill(player.Inventory, 36);
                player.AboardShip = true;
                return (player, ship, Out((Plate, 1)));
            case "full_backpack_aboard_full_hold":
                Fill(player.Inventory, 36);
                Fill(ship.Cargo, ship.Cargo.SlotCount);
                player.AboardShip = true;
                return (player, ship, Out((Plate, 1)));
            case "stack_top_up_fits":
                Fill(player.Inventory, 35);
                player.Inventory.SetSlot(35, new ItemStack(Plate, 1000));
                return (player, ship, Out((Plate, 24)));
            case "stack_top_up_overflows":
                Fill(player.Inventory, 35);
                player.Inventory.SetSlot(35, new ItemStack(Plate, 1000));
                return (player, ship, Out((Plate, 25)));
            case "top_up_then_spill_aboard":
                Fill(player.Inventory, 35);
                player.Inventory.SetSlot(35, new ItemStack(Plate, 1000));
                player.AboardShip = true;
                return (player, ship, Out((Plate, 30)));
            case "one_output_for_the_last_slot":
                Fill(player.Inventory, 35);
                return (player, ship, Out((Plate, 1)));
            case "two_outputs_for_the_last_slot":
                Fill(player.Inventory, 35);
                return (player, ship, Out((Plate, 1), (Ore, 1)));
            case "two_outputs_for_the_holds_last_slot":
                Fill(player.Inventory, 36);
                Fill(ship.Cargo, ship.Cargo.SlotCount - 1);
                player.AboardShip = true;
                return (player, ship, Out((Plate, 1), (Ore, 1)));
            case "max_stack_one_takes_a_slot_each_fits":
                Fill(player.Inventory, 34);
                return (player, ship, Out((TankI, 2)));
            case "max_stack_one_takes_a_slot_each_overflows":
                Fill(player.Inventory, 34);
                return (player, ship, Out((TankI, 3)));
            case "zero_count_outputs_fit_a_full_backpack":
                Fill(player.Inventory, 36);
                return (player, ship, Out((Plate, 0), (TankI, 0)));
            case "more_than_the_backpack_holds_off_ship":
                return (player, ship, Out((Plate, (36 * 1024) + 1)));
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "unknown scenario");
        }
    }

    [Theory]
    [InlineData("empty_backpack", true)]
    [InlineData("full_backpack_off_ship_hold_has_room", false)]
    [InlineData("full_backpack_aboard_spills_into_the_hold", true)]
    [InlineData("full_backpack_aboard_full_hold", false)]
    [InlineData("stack_top_up_fits", true)]
    [InlineData("stack_top_up_overflows", false)]
    [InlineData("top_up_then_spill_aboard", true)]
    [InlineData("one_output_for_the_last_slot", true)]
    [InlineData("two_outputs_for_the_last_slot", false)]
    [InlineData("two_outputs_for_the_holds_last_slot", false)]
    [InlineData("max_stack_one_takes_a_slot_each_fits", true)]
    [InlineData("max_stack_one_takes_a_slot_each_overflows", false)]
    [InlineData("zero_count_outputs_fit_a_full_backpack", true)]
    [InlineData("more_than_the_backpack_holds_off_ship", false)]
    public void ResultFits_AnswersWhatTheServersMaterialPoolAnswers(string scenario, bool fits)
    {
        // The cases lean on these stack sizes (data/items.json).
        Assert.Equal(1024, Content.MaxStackOf(Plate));
        Assert.Equal(1024, Content.MaxStackOf(Ore));
        Assert.Equal(1, Content.MaxStackOf(TankI));
        Assert.Equal(36, new PlayerState().Inventory.SlotCount);

        var (player, ship, outputs) = Scenario(scenario);
        var recipe = new RecipeDefinition { Key = "test_recipe", Outputs = outputs.ToList() };

        bool server = new MaterialPool(Content, player, ship).CanFit(outputs);
        Assert.Equal(fits, server);
        Assert.Equal(server, CraftGate.ResultFits(recipe, FromServer(player, ship)));
    }

    // --- what the player owns ---

    [Fact]
    public void Owned_CountsBackpackAndHoldAboard_LikeThePool()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = true };
        var ship = new ShipState();
        player.Inventory.SetSlot(0, new ItemStack(Plate, 5));
        player.Inventory.SetSlot(7, new ItemStack(Plate, 2));
        ship.Cargo.SetSlot(3, new ItemStack(Plate, 7));
        ship.Cargo.SetSlot(4, new ItemStack(Ore, 9));
        var pool = new MaterialPool(Content, player, ship);
        var input = FromServer(player, ship);

        Assert.Equal(14, CraftGate.Owned(Plate, input));
        Assert.Equal(pool.Count(Plate), CraftGate.Owned(Plate, input));
        Assert.Equal(pool.Count(Ore), CraftGate.Owned(Ore, input));
        Assert.True(CraftGate.HasAll(new[] { new ItemAmount(Plate, 14), new ItemAmount(Ore, 9) }, input));
        Assert.False(CraftGate.HasAll(new[] { new ItemAmount(Plate, 14), new ItemAmount(Ore, 10) }, input));

        // Off the ship the hold does not count — the server sends none, and its pool leaves it out.
        player.AboardShip = false;
        var ashore = FromServer(player, ship);
        Assert.Equal(7, CraftGate.Owned(Plate, ashore));
        Assert.Equal(new MaterialPool(Content, player, ship).Count(Plate), CraftGate.Owned(Plate, ashore));
    }

    [Fact]
    public void Owned_CountsTheWornPieceAndLabChangedOnes_LikeThePool()
    {
        // #2937 (the drift CG-1 pinned): the server's pool also counts the worn piece (#2110) and a piece the bio lab
        // changed (#2206), so the card must too — with tank I worn the oxygen_tank_2 card read "materials missing"
        // although the server crafts it.
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        player.Equipment.SetSlot((int)EquipSlot.Tank, new ItemStack(TankI, 1));
        string changedTank = ItemKey.SetTag(TankI, ItemMods.Tag, "120000");
        player.Inventory.SetSlot(0, new ItemStack(changedTank, 1));
        string changedGloves = new ItemMods(ModStat.Grip, 2, ModStat.None, 0, ModStat.None, 0).ApplyTo("climbing_gloves");
        player.Equipment.SetSlot((int)EquipSlot.Module1, new ItemStack(changedGloves, 1));
        var pool = new MaterialPool(Content, player, ship);
        var input = FromServer(player, ship);

        Assert.Equal(2, pool.Count(TankI));
        Assert.Equal(pool.Count(TankI), CraftGate.Owned(TankI, input));
        Assert.Equal(1, pool.Count("climbing_gloves"));
        Assert.Equal(pool.Count("climbing_gloves"), CraftGate.Owned("climbing_gloves", input));
        foreach (int need in new[] { 1, 2, 3 })
        {
            var cost = new[] { new ItemAmount(TankI, need), new ItemAmount("climbing_gloves", 1) };
            Assert.Equal(pool.Has(cost), CraftGate.HasAll(cost, input));
        }

        // Only the lab's change is soft: a dyed plate stays its own item on both sides, or a recipe would eat the
        // painted wall.
        player.Inventory.SetSlot(1, new ItemStack(ItemKey.Compose(Plate, 0x112233, 0), 4));
        Assert.Equal(0, new MaterialPool(Content, player, ship).Count(Plate));
        Assert.Equal(0, CraftGate.Owned(Plate, FromServer(player, ship)));
    }

    [Fact]
    public void AWornTankI_MakesTheTankIICardCraftable_LikeTheServer()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        player.Equipment.SetSlot((int)EquipSlot.Tank, new ItemStack(TankI, 1));
        var tank2 = Content.Recipes["oxygen_tank_2"];
        Assert.Contains(tank2.Inputs, i => i.Item == TankI); // the upgrade takes tank I (data/recipes.json)
        foreach (var inp in tank2.Inputs.Where(i => i.Item != TankI))
        {
            player.Inventory.Add(inp.Item, inp.Count, Content.MaxStackOf(inp.Item));
        }

        var pool = new MaterialPool(Content, player, ship);
        var atTheBench = FromServer(player, ship, "oxygen_tank_2");

        Assert.True(pool.Has(tank2.Inputs));
        Assert.Equal(pool.Has(tank2.Inputs), CraftGate.HasAll(tank2.Inputs, atTheBench));
        Assert.Equal(CraftBlock.None, CraftGate.Check(tank2, atTheBench));
        Assert.Equal(0, CraftGate.ReachTier(tank2.RequiredBlueprint, tank2.Inputs, stationInReach: true, atTheBench));

        // One tank to upgrade: an order of two is short of it, on both sides.
        Assert.Equal(1, CraftGate.MaxCraftable(tank2, atTheBench));
        Assert.False(pool.Has(Scaled(tank2.Inputs, 2)));
        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(tank2, atTheBench, count: 2));
    }

    [Fact]
    public void ALabChangedTool_CountsAsTheTool_LikeThePool()
    {
        // drill_laser takes a mining_beam (a tool); one the bio lab tuned is still a mining beam to the server's pool.
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        string tunedBeam = new ItemMods(ModStat.Power, 2, ModStat.None, 0, ModStat.None, 0).ApplyTo("mining_beam");
        Assert.NotEqual("mining_beam", tunedBeam);
        Assert.Equal(0, player.Inventory.Add(tunedBeam, 1, 1));
        var laser = Content.Recipes["drill_laser"];
        Assert.Contains(laser.Inputs, i => i.Item == "mining_beam");
        foreach (var inp in laser.Inputs.Where(i => i.Item != "mining_beam"))
        {
            player.Inventory.Add(inp.Item, inp.Count, Content.MaxStackOf(inp.Item));
        }

        var pool = new MaterialPool(Content, player, ship);
        var atTheBench = FromServer(player, ship, "drill_laser");

        Assert.Equal(1, pool.Count("mining_beam"));
        Assert.Equal(pool.Count("mining_beam"), CraftGate.Owned("mining_beam", atTheBench));
        Assert.True(pool.Has(laser.Inputs));
        Assert.Equal(pool.Has(laser.Inputs), CraftGate.HasAll(laser.Inputs, atTheBench));
        Assert.Equal(CraftBlock.None, CraftGate.Check(laser, atTheBench));
    }

    /// <summary>#2958: a recipe that takes any log is craftable from pale logs, alone or mixed with common ones — the gate
    /// counts the group exactly like the server's pool, and an order of two is short of it on both sides.</summary>
    [Fact]
    public void PaleLogs_MakeAWoodRecipeCraftable_LikeThePool()
    {
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        player.Inventory.Add("wood_log_b", 3, 1024);
        player.Inventory.Add("wood_log", 1, 1024);
        var door = Content.Recipes["door_wood"];
        var logs = door.Inputs.Single();
        Assert.Equal("logs", logs.Group);
        var pool = new MaterialPool(Content, player, ship);
        var here = FromServer(player, ship, "door_wood");

        Assert.Equal(4, pool.Count(logs));
        Assert.Equal(pool.Count(logs), CraftGate.Owned(logs, here));
        Assert.True(pool.Has(door.Inputs));
        Assert.Equal(pool.Has(door.Inputs), CraftGate.HasAll(door.Inputs, here));
        Assert.Equal(CraftBlock.None, CraftGate.Check(door, here));
        Assert.Equal(1, CraftGate.MaxCraftable(door, here));
        Assert.False(pool.Has(Scaled(door.Inputs, 2)));
        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(door, here, count: 2));
    }

    // --- an order of N, as the server scales it (HandleCraft) ---

    private static List<ItemAmount> Scaled(IEnumerable<ItemAmount> amounts, int count)
        => amounts.Select(a => new ItemAmount(a.Item, a.Count * count) { Group = a.Group }).ToList(); // #2958: as HandleCraft scales

    [Theory]
    [InlineData("empty_backpack", 17, true)]
    [InlineData("empty_backpack", 18, false)]
    [InlineData("stack_top_up_fits", 1, true)]
    [InlineData("stack_top_up_fits", 2, false)]
    [InlineData("top_up_then_spill_aboard", 3, true)]
    [InlineData("max_stack_one_takes_a_slot_each_fits", 2, false)]
    [InlineData("max_stack_one_takes_a_slot_each_fits", 0, true)]
    [InlineData("one_output_for_the_last_slot", 1024, true)]
    [InlineData("one_output_for_the_last_slot", 1025, true)]
    [InlineData("zero_count_outputs_fit_a_full_backpack", 5, true)]
    public void ResultFits_ForAnOrderOfN_AnswersWhatTheServersScaledCanFitAnswers(string scenario, int count, bool fits)
    {
        var (player, ship, outputs) = Scenario(scenario);
        var recipe = new RecipeDefinition { Key = "test_recipe", Outputs = outputs.ToList() };

        // HandleCraft bounds the order (1 … a full stack) and checks the scaled outputs as a whole.
        int batch = Math.Clamp(count, 1, ItemDefinition.DefaultMaxStack);
        bool server = new MaterialPool(Content, player, ship).CanFit(Scaled(outputs, batch));
        Assert.Equal(fits, server);
        Assert.Equal(server, CraftGate.ResultFits(recipe, FromServer(player, ship), count));
    }

    [Fact]
    public void Check_RefusesAnOrderOfN_ThatOnlyPartlyFitsOrIsShortOfInputs()
    {
        // 34 single tanks, 100 ore and a plate stack of 1000: one craft (2 ore → 24 plates) tops the stack up to 1024;
        // two need a fresh slot there is none of, and the server refuses that order with @inventory_full — the card used
        // to check a single craft and keep the ×2 button live.
        var player = new PlayerState { PlayerId = "p1", Name = "Crafter", AboardShip = false };
        var ship = new ShipState();
        Fill(player.Inventory, 34);
        player.Inventory.SetSlot(34, new ItemStack(Ore, 100));
        player.Inventory.SetSlot(35, new ItemStack(Plate, 1000));
        var r = new RecipeDefinition
        {
            Key = "test_recipe",
            RequiredBlueprint = "test_bp",
            Inputs = { new ItemAmount(Ore, 2) },
            Outputs = { new ItemAmount(Plate, 24) },
        };
        var pool = new MaterialPool(Content, player, ship);
        var input = FromServer(player, ship, "test_bp");

        Assert.True(pool.CanFit(Scaled(r.Outputs, 1)));
        Assert.Equal(CraftBlock.None, CraftGate.Check(r, input, 1));
        Assert.False(pool.CanFit(Scaled(r.Outputs, 2)));
        Assert.Equal(CraftBlock.InventoryFull, CraftGate.Check(r, input, 2));

        // The inputs are checked for the whole order too: 100 ore pay for 50 crafts, not 51.
        Assert.Equal(50, CraftGate.MaxCraftable(r, input));
        foreach (int n in new[] { 50, 51 })
        {
            Assert.Equal(pool.Has(Scaled(r.Inputs, n)), CraftGate.HasAll(r.Inputs, input, n));
        }

        Assert.False(pool.Has(Scaled(r.Inputs, 51)));
        Assert.Equal(CraftBlock.NeedMaterials, CraftGate.Check(r, input, 51));

        // Creative crafts for free but still needs the room for the whole order, like the server's free path (#1937).
        var tank = new RecipeDefinition { Key = "test_tank", Outputs = { new ItemAmount(TankI, 1) } };
        Assert.Equal(CraftBlock.None, CraftGate.Check(tank, Input(Backpack(35), creative: true), 1));
        Assert.Equal(CraftBlock.InventoryFull, CraftGate.Check(tank, Input(Backpack(35), creative: true), 2));
    }

    [Fact]
    public void MaxCraftable_IsTheScarcestInput_CappedAtAFullStack()
    {
        var r = new RecipeDefinition
        {
            Key = "test_recipe",
            Inputs = { new ItemAmount(Ore, 2), new ItemAmount(Plate, 3), new ItemAmount("ignored", 0) },
            Outputs = { new ItemAmount(Plate, 1) },
        };

        Assert.Equal(2, CraftGate.MaxCraftable(r, Input(Holding((Ore, 9), (Plate, 7)))));
        Assert.Equal(0, CraftGate.MaxCraftable(r, Input(Holding((Ore, 9)))));
        Assert.Equal(ItemDefinition.DefaultMaxStack, CraftGate.MaxCraftable(r, Input(creative: true)));

        var single = new RecipeDefinition { Key = "single", Inputs = { new ItemAmount(Ore, 1) }, Outputs = { new ItemAmount(Plate, 1) } };
        Assert.Equal(ItemDefinition.DefaultMaxStack, CraftGate.MaxCraftable(single, Input(Holding((Ore, 1024), (Ore, 1024), (Ore, 5)))));
        Assert.Equal(ItemDefinition.DefaultMaxStack, CraftGate.MaxCraftable(new RecipeDefinition { Key = "free" }, Input()));
    }

    [Fact]
    public void ReachTier_SortsCraftableFirstAndLockedLast()
    {
        var cost = new List<ItemAmount> { new(Ore, 2) };

        Assert.Equal(0, CraftGate.ReachTier("test_bp", cost, stationInReach: false, Input(creative: true)));
        Assert.Equal(3, CraftGate.ReachTier("test_bp", cost, stationInReach: false, Input()));
        Assert.Equal(2, CraftGate.ReachTier("test_bp", cost, stationInReach: false, Input(Holding((Ore, 2)), unlocked: Unlocked)));
        Assert.Equal(1, CraftGate.ReachTier("test_bp", cost, stationInReach: true, Input(unlocked: Unlocked)));
        Assert.Equal(0, CraftGate.ReachTier("test_bp", cost, stationInReach: true, Input(Holding((Ore, 2)), unlocked: Unlocked)));
        Assert.Equal(0, CraftGate.ReachTier(null, cost, stationInReach: true, Input(Holding((Ore, 2)))));
    }

    [Fact]
    public void DisassembleRecipe_SkipsMarketTradesAndFreeRecipes()
    {
        // A recipe list, not a GameContent: building one would replace the process-wide block fingerprint that the
        // integration tests of this suite join their servers with.
        var recipes = new[]
        {
            new RecipeDefinition { Key = "market_gem", Station = CraftingStation.Market, Inputs = { new ItemAmount("rock", 3) }, Outputs = { new ItemAmount("gem", 1) } },
            new RecipeDefinition { Key = "free_gem", Station = CraftingStation.Hand, Outputs = { new ItemAmount("gem", 1) } },
            new RecipeDefinition { Key = "widget", Station = CraftingStation.Workshop, Inputs = { new ItemAmount("rock", 2) }, Outputs = { new ItemAmount("bolt", 1), new ItemAmount("widget", 3) } },
            new RecipeDefinition { Key = "nut", Station = CraftingStation.Workshop, Inputs = { new ItemAmount("rock", 1) }, Outputs = { new ItemAmount("nut", 0) } },
        };

        var (gem, _) = CraftGate.DisassembleRecipe("gem", recipes);
        var (widget, perWidget) = CraftGate.DisassembleRecipe("widget", recipes);
        var (nut, perNut) = CraftGate.DisassembleRecipe("nut", recipes);
        var (none, perNone) = CraftGate.DisassembleRecipe("rock", recipes);

        Assert.Null(gem);
        Assert.Equal("widget", widget?.Key);
        Assert.Equal(3, perWidget);
        Assert.Equal("nut", nut?.Key);
        Assert.Equal(1, perNut);
        Assert.Null(none);
        Assert.Equal(1, perNone);
    }
}

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Craftable ship types + owning multiple ships + switching the active one (ships.json).</summary>
public sealed class ShipFleetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipFleetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fleet_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fleet"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fleet", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host"); // ships are per-player now — a player must exist to own one
        return server;
    }

    [Fact]
    public void ShipsContent_LoadsTypes()
    {
        Assert.True(_content.Ships.ContainsKey("starter"));
        Assert.True(_content.Ships.ContainsKey("hauler"));
        Assert.True(_content.Ships.ContainsKey("scout"));
    }

    [Fact]
    public void StartsWithOneActiveStarterShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Assert.Single(server.OwnedShips);
            Assert.Equal("default", server.ActiveShipId);
            Assert.Equal("starter", server.Ship.ShipType);
        }
    }

    [Fact]
    public void CraftShip_AddsToFleet_WhenUnlockedAndAffordable()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.UnlockedBlueprints.Add("ship_scout");
            pilot.State.Inventory.Add("titanium_plate", 25, 99);
            pilot.State.Inventory.Add("cable", 16, 99);
            pilot.State.Inventory.Add("energy_cell_1", 4, 99);
            pilot.State.Inventory.Add("glass", 6, 99);
            pilot.State.Inventory.Add("light_alloy", 6, 99);   // Task 5 Stage 4: scout now needs these
            pilot.State.Inventory.Add("circuit_board", 3, 99);

            var (ok, _) = server.CraftShip("Pilot", "scout");

            Assert.True(ok);
            Assert.Equal(2, server.OwnedShips.Count);
            Assert.Equal(0, pilot.State.Inventory.CountOf("titanium_plate")); // consumed
        }
    }

    [Fact]
    public void CraftShip_Rejected_WhenBlueprintLocked()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.Inventory.Add("titanium_plate", 99, 99);
            pilot.State.Inventory.Add("cable", 99, 99);
            pilot.State.Inventory.Add("energy_cell_1", 99, 99);
            pilot.State.Inventory.Add("glass", 99, 99);

            var (ok, _) = server.CraftShip("Pilot", "scout"); // ship_scout not unlocked

            Assert.False(ok);
            Assert.Single(server.OwnedShips);
        }
    }

    [Fact]
    public void SwitchShip_ChangesActiveAndStats()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true; // skip material cost
            pilot.State.UnlockedBlueprints.Add("ship_hauler");

            var (ok, id) = server.CraftShip("Pilot", "hauler");
            Assert.True(ok);

            Assert.True(server.SwitchShip(id));
            Assert.Equal(id, server.ActiveShipId);
            Assert.Equal("hauler", server.Ship.ShipType);
            Assert.Equal(170f, server.Ship.Hull); // hauler base hull
        }
    }

    [Fact]
    public void CargoCapacity_IsTheModuleSum_AndExpansionTiersStack()
    {
        // ships.json used to advertise a `cargoSlots` the hold never used (hauler 96 vs. a real 72 — it
        // ships with Expansion I already fitted, so building it again was refused: no upgrade path at
        // all, #1261). The design's number IS the module sum now, and Expansion II/III stack on top.
        var server = Started(out var repo);
        using (repo)
        {
            var hauler = _content.GetShip("hauler")!;
            Assert.Equal(72, _content.StartCargoSlots(hauler));
            Assert.Equal(48, _content.StartCargoSlots(_content.GetShip("starter")!));

            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true;
            pilot.State.AboardShip = true;
            pilot.State.UnlockedBlueprints.Add("ship_hauler");
            var (ok, id) = server.CraftShip("Pilot", "hauler");
            Assert.True(ok);
            Assert.True(server.SwitchShip(id));
            Assert.Equal(72, server.Ship.Cargo.SlotCount);

            // Expansion I is pre-fitted → refused, capacity unchanged.
            pilot.State.UnlockedBlueprints.Add("cargo_expansion_1");
            Assert.True(server.BuildModuleForTest("Pilot", "cargo_hold_1")); // already fitted (start module)
            Assert.Equal(72, server.Ship.Cargo.SlotCount);
            Assert.Equal(1, server.Ship.Modules.Count(m => m == "cargo_hold_1"));

            // Expansion II needs its blueprint, then adds +32; III adds +48 on top.
            Assert.False(server.BuildModuleForTest("Pilot", "cargo_hold_2"));
            pilot.State.UnlockedBlueprints.Add("cargo_expansion_2");
            Assert.True(server.BuildModuleForTest("Pilot", "cargo_hold_2"));
            Assert.Equal(104, server.Ship.Cargo.SlotCount);
            pilot.State.UnlockedBlueprints.Add("cargo_expansion_3");
            Assert.True(server.BuildModuleForTest("Pilot", "cargo_hold_3"));
            Assert.Equal(152, server.Ship.Cargo.SlotCount);
        }
    }

    [Fact]
    public void AFleetShipThatFailsToLoad_StaysInTheIndex()
    {
        // A fleet row that cannot be read (corrupt, or written by a newer build) used to be pruned by the
        // next SaveFleet — the ship was gone for good and the player was silently back in ship one (#1275).
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Stop(); // persists the player; then plant an index entry with no ship row behind it
            pilot.State.FleetShipIds.Add("ghost");
            pilot.State.ActiveShipId = "ghost";
            repo.SavePlayer(pilot.State);
        }

        var again = Started(out var repo2);
        using (repo2)
        {
            var pilot = again.AddLocalPlayer("Pilot");
            Assert.NotEqual("ghost", pilot.ActiveShipId); // flies ship one for now …
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("ship_hauler");
            var (ok, id) = again.CraftShip("Pilot", "hauler"); // … which re-saves the fleet index
            Assert.True(ok);
            Assert.Contains("ghost", pilot.State.FleetShipIds); // … without losing the unreadable ship
            Assert.Contains(id, pilot.State.FleetShipIds);
        }
    }

    [Fact]
    public void UninstallModule_SalvagesHalfTheParts_AndKeepsHullStationsAndTheBasicHold()
    {
        // #1269 (Marcel's call: uninstall with salvage, no transfer). The only removal before this was the
        // hard-coded Mk2 → Mk3 core swap; everything else was welded to the ship that built it.
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            pilot.State.InstantBuild = true; // build for free …
            pilot.State.UnlockedBlueprints.Add("hull_plating");
            Assert.True(server.BuildModuleForTest("Pilot", "hull_plating"));
            float armoured = server.HullMaxForTest;

            pilot.State.InstantBuild = false; // … but salvage at the real rate
            int titaniumBefore = pilot.State.Inventory.CountOf("titanium_plate");
            int steelBefore = pilot.State.Inventory.CountOf("steel");
            Assert.True(server.UninstallModuleForTest("Pilot", "hull_plating"));
            Assert.False(server.Ship.HasModule("hull_plating"));
            Assert.Equal(titaniumBefore + 10, pilot.State.Inventory.CountOf("titanium_plate")); // 20 × 0.5
            Assert.Equal(steelBefore + 3, pilot.State.Inventory.CountOf("steel"));               // 6 × 0.5
            Assert.True(server.HullMaxForTest < armoured, "hull max must drop with the plating");

            // The hull essentials, the stations and the basic hold never come out.
            foreach (var welded in new[] { "cockpit", "reactor", "life_support", "workshop", "medbay", "cargo_hold_basic" })
            {
                Assert.False(server.UninstallModuleForTest("Pilot", welded), welded);
                Assert.True(server.Ship.HasModule(welded), welded);
            }

            // Not fitted → refused, nothing changes.
            Assert.False(server.UninstallModuleForTest("Pilot", "shield_generator"));
        }
    }

    [Fact]
    public void ACargoExpansion_OnlyComesOut_WhenTheHoldStillFitsEverything()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("cargo_expansion_1");
            Assert.True(server.BuildModuleForTest("Pilot", "cargo_hold_1"));
            Assert.Equal(72, server.Ship.Cargo.SlotCount);

            // 60 distinct stacks in a 72-slot hold: without the expansion (48) twelve would have no slot.
            for (int i = 0; i < 60; i++)
            {
                server.Ship.Cargo.Add("iron_ore#t" + i.ToString("x6"), 1, 1);
            }

            Assert.False(server.UninstallModuleForTest("Pilot", "cargo_hold_1"));
            Assert.Equal(72, server.Ship.Cargo.SlotCount);

            server.Ship.Cargo = new BlocksBeyondTheStars.Shared.State.Inventory(72); // emptied
            Assert.True(server.UninstallModuleForTest("Pilot", "cargo_hold_1"));
            Assert.Equal(48, server.Ship.Cargo.SlotCount);
        }
    }

    [Fact]
    public void BuildShieldGenerator_RestoresShieldToMax()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("shield_generator");

            var (_, shieldMaxBefore) = server.ShipShieldForTest("Pilot");
            server.SetShipShieldForTest("Pilot", shieldMaxBefore - 10f);

            Assert.True(server.BuildModuleForTest("Pilot", "shield_generator"));

            var (shield, shieldMax) = server.ShipShieldForTest("Pilot");
            Assert.Equal(shieldMax, shield);
            Assert.Equal(135f, shieldMax);
        }
    }

    [Fact]
    public void BuildNonShieldModule_DoesNotChangeShield()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.AboardShip = true;
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("cargo_expansion_1");

            var (_, shieldMax) = server.ShipShieldForTest("Pilot");
            server.SetShipShieldForTest("Pilot", shieldMax - 10f);

            var (shieldBefore, _) = server.ShipShieldForTest("Pilot");

            Assert.True(server.BuildModuleForTest("Pilot", "cargo_hold_1"));

            var (shieldAfter, actualShieldMax) = server.ShipShieldForTest("Pilot");
            Assert.Equal(shieldBefore, shieldAfter);
            Assert.Equal(shieldMax, actualShieldMax);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}

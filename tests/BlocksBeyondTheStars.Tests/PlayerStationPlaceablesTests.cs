// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Feature 2 — player-placeable vendors, mission boards and storage containers on the player's OWN
/// in-space-built station. The placeables are data-driven blueprint-gated blocks the owner crafts and
/// places into their station via the EVA structure-edit pipeline; once boarded they register as the same
/// interaction points (markers / containers) the procedural stations use, so any boarder can trade, take
/// missions and open the container. The placed cells persist with the station across a server restart.
/// </summary>
public sealed class PlayerStationPlaceablesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlayerStationPlaceablesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pstplace_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Deploys + builds out a player station, then places the three placeables onto it. Returns the
    /// station id. The player is left on an EVA in space next to the (commissioned) station.</summary>
    private static string BuildStationWithPlaceables(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true; // free build for the test

        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;

        // A small hull along +X so the station commissions (core + 11 walls = 12 blocks).
        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest(playerId,
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }

        // An airlock door → commissioned (boardable + persisted).
        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });

        // The three manual placeables, built into the station interior.
        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 1, Y = 1, Z = 0, Mine = false, ItemKey = "station_vendor" });
        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 2, Y = 1, Z = 0, Mine = false, ItemKey = "mission_board" });
        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 3, Y = 1, Z = 0, Mine = false, ItemKey = "station_container" });

        return id;
    }

    private static void BoardOwnStation(SvGameServer server, string playerId, string stationId)
    {
        if (!server.InSpace(playerId))
        {
            server.EnterSpace(playerId);
        }

        var contact = server.SpaceEntitiesFor(playerId).First(e => e.Id == stationId);
        server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(playerId, stationId);
    }

    // ---------------- Content consistency ----------------

    [Fact]
    public void Placeables_Blocks_Items_Recipes_Blueprints_AllLoadAndResolve()
    {
        string[] keys = { "station_vendor", "mission_board", "station_container" };
        foreach (var key in keys)
        {
            var block = _content.GetBlock(key);
            var item = _content.GetItem(key);
            var recipe = _content.GetRecipe(key);
            Assert.NotNull(block);
            Assert.NotNull(item);
            Assert.NotNull(recipe);
            Assert.Equal(key, item!.PlacesBlock); // the item places its block
            // The recipe inputs/outputs reference real items.
            foreach (var ia in recipe!.Inputs.Concat(recipe.Outputs))
            {
                Assert.NotNull(_content.GetItem(ia.Item));
            }
        }

        // Each recipe is gated by a blueprint that exists, and that blueprint's unlock cost is real.
        var gates = new[]
        {
            ("station_vendor", "station_vendor"),
            ("mission_board", "station_mission_board"),
            ("station_container", "station_container"),
        };
        foreach (var (recipeKey, blueprintKey) in gates)
        {
            var recipe = _content.GetRecipe(recipeKey);
            Assert.Equal(blueprintKey, recipe!.RequiredBlueprint);
            var bp = _content.GetBlueprint(blueprintKey);
            Assert.NotNull(bp);
            foreach (var ia in bp!.UnlockCost)
            {
                Assert.NotNull(_content.GetItem(ia.Item));
            }
        }
    }

    // ---------------- Blueprint gate ----------------

    [Fact]
    public void VendorRecipe_IsLocked_UntilItsBlueprintIsUnlocked()
    {
        var server = NewServer("gate", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("metal_panel", 4, 99);
            p.State.Inventory.Add("circuit_board", 2, 99);
            p.State.Inventory.Add("energy_cell_1", 1, 99);

            // A workbench beside the player so the workshop is available here.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("workbench")!.NumericId);

            // Locked: the blueprint isn't unlocked yet → nothing is produced.
            server.Craft("Builder", "station_vendor");
            Assert.Equal(0, p.State.Inventory.CountOf("station_vendor"));

            // Unlock the blueprint, then the same craft succeeds.
            p.State.UnlockedBlueprints.Add("station_vendor");
            server.Craft("Builder", "station_vendor");
            Assert.Equal(1, p.State.Inventory.CountOf("station_vendor"));
        }
    }

    // ---------------- Interaction for a boarder ----------------

    [Fact]
    public void PlacedVendor_MissionBoard_AndContainer_AreInteractable_WhenBoarded()
    {
        var server = NewServer("interact", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildStationWithPlaceables(server, pilot);
            BoardOwnStation(server, "Owner", id);
            Assert.True(server.InStation("Owner"));

            // The placed vendor + board registered as interaction markers (so the crew staffs them).
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "vendor");
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "mission_board");

            // Trade: at the vendor marker, a themeless market barter succeeds.
            var vendor = server.SpaceStationMarkers.First(m => m.Type == "vendor");
            pilot.State.Position = vendor.Pos;
            pilot.State.Inventory.Add("iron_ore", 5, 99);
            server.Craft("Owner", "market_iron_to_titanium");
            Assert.Equal(0, pilot.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(1, pilot.State.Inventory.CountOf("titanium_ore"));

            // Missions: the board offers jobs, and one can be accepted standing at the board.
            Assert.NotEmpty(server.StationMissionIds);
            string missionId = server.StationMissionIds.First();
            pilot.State.Position = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;
            server.AcceptMission("Owner", missionId);
            Assert.Contains(pilot.State.Missions, m => m.MissionId == missionId);

            // Container: the placed storage container is a lootable/stash-able crate; deposit loose materials.
            var crate = server.Containers.First(c => c.Id.StartsWith("scontainer_"));
            pilot.State.Position = new Vector3f(crate.Position.X + 0.5f, crate.Position.Y + 0.5f, crate.Position.Z + 0.5f);
            pilot.State.Inventory.Add("carbon", 7, 99);
            server.DepositToContainer("Owner", crate.Id);
            Assert.Equal(0, pilot.State.Inventory.CountOf("carbon"));
            Assert.Contains(server.Containers.First(c => c.Id == crate.Id).Items, s => s.Item == "carbon" && s.Count == 7);
        }
    }

    // ---------------- Persistence across restart ----------------

    [Fact]
    public void PlacedPlaceables_Persist_AcrossServerRestart()
    {
        string id;
        {
            var s1 = NewServer("pstplace_persist", out var repo1);
            using (repo1)
            {
                var owner = s1.AddLocalPlayer("Owner");
                id = BuildStationWithPlaceables(s1, owner);
                Assert.True(s1.StationIsBoardableForTest(id));
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Fresh server on the same save: board the restored station — the placeables come back as
        // interaction points and a container, proving the placed cells survived the restart.
        var s2 = NewServer("pstplace_persist", out var repo2);
        using (repo2)
        {
            Assert.True(s2.StationIsBoardableForTest(id));
            var pilot = s2.AddLocalPlayer("Owner");
            BoardOwnStation(s2, "Owner", id);
            Assert.True(s2.InStation("Owner"));

            Assert.Contains(s2.SpaceStationMarkers, m => m.Type == "vendor");
            Assert.Contains(s2.SpaceStationMarkers, m => m.Type == "mission_board");
            Assert.Contains(s2.Containers, c => c.Id.StartsWith("scontainer_"));

            // And an interaction still works after the restart: trade at the restored vendor.
            var vendor = s2.SpaceStationMarkers.First(m => m.Type == "vendor");
            pilot.State.Position = vendor.Pos;
            pilot.State.Inventory.Add("iron_ore", 5, 99);
            s2.Craft("Owner", "market_iron_to_titanium");
            Assert.Equal(1, pilot.State.Inventory.CountOf("titanium_ore"));
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

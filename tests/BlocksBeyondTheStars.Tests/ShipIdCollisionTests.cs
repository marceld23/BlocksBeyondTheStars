// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
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
/// #3192: a new fleet ship never takes the id of one the player still owns. Ids used to come from the fleet size, which
/// repeats once a keel site is dismantled to its last block — and the next crafted ship overwrote a ship of the same id,
/// cargo hold and save row included.
/// </summary>
public sealed class ShipIdCollisionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipIdCollisionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shipid_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shipid"));
        var config = new ServerConfig
        {
            WorldName = "shipid",
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // the keel goes down at a fixed column (see CustomShipTests)
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    [Fact]
    public void DismantlingAKeel_ThenCraftingTheSameShipAgain_KeepsTheFirstShipAndItsCargo()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("ship_hauler");

            // Lay a keel: the fleet grows by an un-commissioned construction entry.
            var core = new Vector3i(40, SurfaceY(server, 40, 40) + 1, 40);
            pilot.State.Position = new Vector3f(core.X + 2.5f, core.Y + 1.5f, core.Z + 2.5f);
            server.PlaceShipCoreForTest("Pilot", core.X, core.Y, core.Z);
            Assert.Single(pilot.Ships.Values.Where(s => s.IsCustom));

            var (ok, firstId) = server.CraftShip("Pilot", "hauler");
            Assert.True(ok);
            pilot.Ships[firstId].Cargo.Add("iron_ore", 50, 1024); // loaded hold

            // Dismantle the keel completely: the construction leaves the fleet, so the fleet is one smaller again.
            server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "shipyard:Pilot", X = 0, Y = 0, Z = 0, Mine = true });
            Assert.DoesNotContain(pilot.Ships.Values, s => s.IsCustom);

            var (again, secondId) = server.CraftShip("Pilot", "hauler");

            Assert.True(again);
            Assert.NotEqual(firstId, secondId);
            Assert.Equal(50, pilot.Ships[firstId].Cargo.CountOf("iron_ore")); // the first hauler survived with its cargo
            Assert.Equal(0, pilot.Ships[secondId].Cargo.CountOf("iron_ore"));
            Assert.Equal(2, pilot.Ships.Values.Count(s => s.ShipType == "hauler"));
        }
    }

    [Fact]
    public void UniqueShipId_CountsUpFromTheFleetSize_PastEveryTakenId()
    {
        var fleet = new Dictionary<string, ShipState>
        {
            ["default"] = new ShipState(),
            ["built_custom_2"] = new ShipState(),
            ["built_custom_3"] = new ShipState(),
        };

        Assert.Equal("built_custom_4", SvGameServer.UniqueShipId(fleet, "built_custom_"));
        Assert.Equal("hauler_hauler_3", SvGameServer.UniqueShipId(fleet, "hauler_hauler_"));
    }
}

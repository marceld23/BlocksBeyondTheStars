// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player-built ships (#948/#949/#950): laying a keel founds a construction site, block edits grow it
/// under the 15×15×15 cap, commissioning validates helm/engine/door/airtightness and turns it into the
/// active fleet ship with geometry-derived stats, and the launch gate re-checks the geometry.
/// </summary>
public sealed class CustomShipTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CustomShipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_customship_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "customship"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "customship", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host");
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

    /// <summary>Lays a keel at a fixed column and returns the keel's world cell. The pilot stands beside
    /// it with instant build, so construction edits need no materials.</summary>
    private static Vector3i LayKeel(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession pilot)
    {
        int sy = SurfaceY(server, 40, 40);
        var core = new Vector3i(40, sy + 1, 40);
        pilot.State.InstantBuild = true;
        pilot.State.Position = new Vector3f(core.X + 2.5f, core.Y + 1.5f, core.Z + 2.5f);
        server.PlaceShipCoreForTest("Pilot", core.X, core.Y, core.Z);
        return core;
    }

    private static void Edit(SvGameServer server, string structureId, int x, int y, int z, string item)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent
        {
            StructureId = structureId,
            X = x,
            Y = y,
            Z = z,
            Mine = false,
            ItemKey = item,
        });

    /// <summary>Builds a valid 5×4×5 hull around the keel at local (0,0,0): full floor + roof, wall ring
    /// with one slide door, a helm and an engine inside. Returns the helm's structure-local cell.</summary>
    private static Vector3i BuildValidHull(SvGameServer server)
    {
        const string yard = "shipyard:Pilot";

        // Floor y=0 (the keel is the corner cell), spreading out from the keel so every place attaches.
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                if (x == 0 && z == 0)
                {
                    continue; // the keel
                }

                Edit(server, yard, x, 0, z, "iron_wall");
            }

        // Wall ring y=1..2 with a doorway at (1,1,0).
        for (int y = 1; y <= 2; y++)
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                {
                    bool ring = x == 0 || x == 4 || z == 0 || z == 4;
                    if (!ring || (x == 1 && y == 1 && z == 0))
                    {
                        continue;
                    }

                    Edit(server, yard, x, y, z, "iron_wall");
                }

        Edit(server, yard, 1, 1, 0, "door_slide"); // the airtight-by-door opening

        // Roof y=3.
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                Edit(server, yard, x, 3, z, "iron_wall");
            }

        // Helm + engine inside the cabin.
        Edit(server, yard, 2, 1, 2, "ship_helm");
        Edit(server, yard, 1, 1, 1, "ship_engine");
        return new Vector3i(2, 1, 2);
    }

    private static ShipState CustomShipOf(BlocksBeyondTheStars.GameServer.PlayerSession pilot)
        => pilot.Ships.Values.Single(s => s.IsCustom);

    // ---------------- Content wiring ----------------

    [Fact]
    public void ShipBuilderContent_IsWired()
    {
        foreach (var key in new[] { "ship_core", "ship_helm", "ship_engine" })
        {
            var block = _content.GetBlock(key);
            Assert.NotNull(block);
            Assert.True(block!.Airtight, $"{key} must seal a hull (machine category)");
            Assert.Equal(key, _content.GetItem(key)!.PlacesBlock);
            Assert.True(_content.Recipes.ContainsKey(key), $"{key} needs a workshop recipe");
        }

        Assert.True(_content.Blueprints.ContainsKey("ship_builder"), "the shipwright blueprint gates the recipes");
        Assert.True(_content.Blueprints["ship_builder"].KnowledgeCost > 0, "new blueprints need a knowledge cost");
    }

    // ---------------- Founding a construction ----------------

    [Fact]
    public void PlacingKeel_FoundsConstruction_NotAWorldBlock()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var core = LayKeel(server, pilot);

            // A new un-commissioned custom fleet entry exists, anchored at the keel cell …
            var ship = CustomShipOf(pilot);
            Assert.False(ship.Commissioned);
            Assert.Equal((core.X, core.Y, core.Z), (ship.BuildX, ship.BuildY, ship.BuildZ));
            Assert.NotEqual(string.Empty, ship.BuiltCells);

            // … the site object is placed 1×1×1, and the world grid was never touched.
            var bounds = server.ConstructionBoundsForTest("Pilot");
            Assert.NotNull(bounds);
            Assert.Equal(new Vector3i(1, 1, 1), bounds!.Value.Size);
            Assert.True(server.World.GetBlock(core).IsAir, "the keel is a structure cell, not a world block");

            // The active ship is still the starter — the construction is not switchable yet.
            Assert.Equal("default", server.ActiveShipId);

            // A second keel is refused while one construction exists.
            server.PlaceShipCoreForTest("Pilot", core.X + 10, core.Y, core.Z);
            Assert.Single(pilot.Ships.Values.Where(s => s.IsCustom));
        }
    }

    // ---------------- Building rules ----------------

    [Fact]
    public void ConstructionEdits_EnforceAdjacencyAndSizeCap()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            LayKeel(server, pilot);

            // Detached cells are refused.
            Edit(server, "shipyard:Pilot", 3, 0, 3, "iron_wall");
            Assert.Equal(new Vector3i(1, 1, 1), server.ConstructionBoundsForTest("Pilot")!.Value.Size);

            // A chain along +X grows the hull to the 15-block cap …
            pilot.State.Position = new Vector3f(pilot.State.Position.X + 5f, pilot.State.Position.Y, pilot.State.Position.Z);
            for (int x = 1; x <= 14; x++)
            {
                if (x == 8)
                {
                    // stay in the 10 m edit reach while the row grows
                    pilot.State.Position = new Vector3f(pilot.State.Position.X + 6f, pilot.State.Position.Y, pilot.State.Position.Z);
                }

                Edit(server, "shipyard:Pilot", x, 0, 0, "iron_wall");
            }

            Assert.Equal(15, server.ConstructionBoundsForTest("Pilot")!.Value.Size.X);

            // … and the 16th block past the cap is refused.
            Edit(server, "shipyard:Pilot", 15, 0, 0, "iron_wall");
            Assert.Equal(15, server.ConstructionBoundsForTest("Pilot")!.Value.Size.X);
        }
    }

    [Fact]
    public void Construction_GrowingIntoNegativeX_ShiftsTheOriginNotTheHull()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var core = LayKeel(server, pilot);

            Edit(server, "shipyard:Pilot", -1, 0, 0, "iron_wall");

            var (origin, size) = server.ConstructionBoundsForTest("Pilot")!.Value;
            Assert.Equal(core.X - 1, origin.X); // the anchor moved one west …
            Assert.Equal(2, size.X);            // … and the hull is two wide
            var ship = CustomShipOf(pilot);
            Assert.Equal(core.X - 1, ship.BuildX); // the persisted anchor tracks the shift
        }
    }

    // ---------------- Commissioning ----------------

    [Fact]
    public void Commissioning_RejectsAnUnfinishedHull()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            LayKeel(server, pilot);
            Edit(server, "shipyard:Pilot", 1, 0, 0, "iron_wall");

            server.CommissionShipForTest("Pilot");

            Assert.False(CustomShipOf(pilot).Commissioned);
            Assert.Equal("default", server.ActiveShipId);
        }
    }

    [Fact]
    public void Commissioning_AValidHull_MakesItTheActiveShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var core = LayKeel(server, pilot);
            var helm = BuildValidHull(server);

            // Stand at the helm and commission.
            pilot.State.Position = new Vector3f(core.X + helm.X + 0.5f, core.Y + helm.Y, core.Z + helm.Z + 0.5f);
            server.CommissionShipForTest("Pilot");

            var ship = CustomShipOf(pilot);
            Assert.True(ship.Commissioned);
            string shipId = pilot.Ships.Single(kv => kv.Value.IsCustom).Key;
            Assert.Equal(shipId, server.ActiveShipId);
            Assert.True(ship.HasModule("life_support"), "a commissioned ship gets the baseline modules");

            // The construction object is gone; the parked ship stands at the build spot.
            Assert.Null(server.ConstructionBoundsForTest("Pilot"));
            var (origin, size) = server.LandedShipBoundsForTest("Pilot");
            Assert.Equal(core, origin);
            Assert.Equal(new Vector3i(5, 4, 5), size);

            // Geometry-derived stats are live and persisted with the fleet.
            var stats = server.CustomShipStatsForTest("Pilot", shipId);
            Assert.NotNull(stats);
            Assert.True(stats!.Value.HullMax > 60f);
            Assert.InRange(stats.Value.FlightSpeed, 0.4f, 1.8f);
            Assert.True(ship.Hull >= stats.Value.HullMax, "a fresh ship starts at full hull (geometry + modules)");

            var persisted = repo.LoadShip("ship_Pilot#" + shipId);
            Assert.NotNull(persisted);
            Assert.True(persisted!.Commissioned);
            Assert.Equal(ship.BuiltCells, persisted.BuiltCells);
        }
    }

    // ---------------- Geometry-derived stats ----------------

    [Fact]
    public void CustomShipStats_MoreEnginesFaster_HeavierSlower()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            ushort wall = _content.GetBlock("iron_wall")!.NumericId.Value;
            ushort engine = _content.GetBlock("ship_engine")!.NumericId.Value;

            string Blob(int walls, int engines)
            {
                var parts = new List<string>();
                for (int i = 0; i < walls; i++)
                {
                    parts.Add($"{i}:0:0:{wall}");
                }

                for (int i = 0; i < engines; i++)
                {
                    parts.Add($"{i}:1:0:{engine}");
                }

                return string.Join(";", parts);
            }

            pilot.Ships["custom_light"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = Blob(30, 2) };
            pilot.Ships["custom_thrusty"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = Blob(30, 6) };
            pilot.Ships["custom_heavy"] = new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = Blob(200, 2) };

            var light = server.CustomShipStatsForTest("Pilot", "custom_light")!.Value;
            var thrusty = server.CustomShipStatsForTest("Pilot", "custom_thrusty")!.Value;
            var heavy = server.CustomShipStatsForTest("Pilot", "custom_heavy")!.Value;

            Assert.True(thrusty.FlightSpeed > light.FlightSpeed, "more engines fly faster");
            Assert.True(heavy.FlightSpeed < light.FlightSpeed, "a heavier hull flies slower");
            Assert.True(heavy.HullMax > light.HullMax, "a bigger hull takes more damage");
            Assert.InRange(light.FlightSpeed, 0.4f, 1.8f);
            Assert.InRange(heavy.Handling, 0.4f, 1.7f);
        }
    }

    // ---------------- Launch gate ----------------

    [Fact]
    public void LaunchGate_RechecksTheGeometry_AfterTheEngineIsMinedOff()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var core = LayKeel(server, pilot);
            var helm = BuildValidHull(server);
            pilot.State.Position = new Vector3f(core.X + helm.X + 0.5f, core.Y + helm.Y, core.Z + helm.Z + 0.5f);
            server.CommissionShipForTest("Pilot");
            Assert.True(CustomShipOf(pilot).Commissioned);

            // Mining the only engine is a design change on the parked ship …
            server.HandleStructureEditForTest("Pilot", new StructureEditIntent
            {
                StructureId = "ship:Pilot",
                X = 1,
                Y = 1,
                Z = 1,
                Mine = true,
            });
            Assert.DoesNotContain($"1:1:1", CustomShipOf(pilot).BuiltCells.Split(';').Select(c => c.Substring(0, c.LastIndexOf(':'))));

            // … and the launch gate grounds the ship until an engine is back.
            server.EnterSpace("Pilot");
            Assert.False(server.InSpace("Pilot"));

            Edit(server, "ship:Pilot", 1, 1, 1, "ship_engine");
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));
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

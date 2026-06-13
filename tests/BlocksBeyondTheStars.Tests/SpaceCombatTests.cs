using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Free space flight, ship weapons, NPC combat and planet enemies (M19 / `anf_space_flight.md` §6-12).</summary>
public sealed class SpaceCombatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceCombatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_space_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, Action<GameRules> configure, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        configure(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- Free flight + instance population ----------------

    [Fact]
    public void EnterSpace_RejectedWhenFreeFlightDisabled()
    {
        var server = NewServer("noflight", r => r.FreeSpaceFlight = false, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.False(server.InSpace("Pilot"));
        }
    }

    [Fact]
    public void EnterSpace_SpawnsAsteroids_ButNoDronesWhenCombatOff()
    {
        var server = NewServer("asteroids", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.Off;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            Assert.True(server.InSpace("Pilot"));
            var entities = server.SpaceEntitiesFor("Pilot");
            Assert.Equal(3, entities.Count(e => e.Kind == CombatEntityKind.Asteroid));
            Assert.DoesNotContain(entities, e => e.Hostile);
        }
    }

    [Fact]
    public void EnterSpace_SpawnsDrones_WhenCombatAndNpcEnabled()
    {
        var server = NewServer("drones", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Normal; // -> 2 drones
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.Equal(2, server.SpaceEntitiesFor("Pilot").Count(e => e.Kind == CombatEntityKind.Drone));
        }
    }

    [Fact]
    public void EvaStructureEdit_MinesAndPlacesOnTheShipVoxelGrid()
    {
        // item 20 S2: on an EVA you can mine + place blocks on your own ship's voxel structure.
        var server = NewServer("structedit", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));

            pilot.State.InEva = true;        // editing is gated to a spacewalk
            pilot.State.InstantBuild = true; // free placement → no inventory needed for the test

            // Find a solid hull cell in the player's ship structure.
            int sx = -1, sy = -1, sz = -1;
            for (int x = 0; x < 24 && sx < 0; x++)
            for (int y = 0; y < 24 && sx < 0; y++)
            for (int z = 0; z < 24 && sx < 0; z++)
            {
                if (server.StructureBlockForTest("Pilot", x, y, z) != 0) { sx = x; sy = y; sz = z; }
            }

            Assert.True(sx >= 0, "the ship structure should have at least one solid cell");

            // Mine it → the cell becomes air.
            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = "ship:Pilot", X = sx, Y = sy, Z = sz, Mine = true });
            Assert.Equal(0, server.StructureBlockForTest("Pilot", sx, sy, sz));

            // Place iron_wall back into the now-empty cell → the cell is solid again.
            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = "ship:Pilot", X = sx, Y = sy, Z = sz, Mine = false, ItemKey = "iron_wall" });
            Assert.NotEqual(0, server.StructureBlockForTest("Pilot", sx, sy, sz));
        }
    }

    [Fact]
    public void StructureEdit_RejectedWhenNotOnEva()
    {
        var server = NewServer("structnoteva", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            pilot.State.InEva = false; // piloting, not floating → can't edit the hull

            int sx = -1, sy = -1, sz = -1;
            for (int x = 0; x < 24 && sx < 0; x++)
            for (int y = 0; y < 24 && sx < 0; y++)
            for (int z = 0; z < 24 && sx < 0; z++)
            {
                if (server.StructureBlockForTest("Pilot", x, y, z) != 0) { sx = x; sy = y; sz = z; }
            }

            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = "ship:Pilot", X = sx, Y = sy, Z = sz, Mine = true });
            Assert.NotEqual(0, server.StructureBlockForTest("Pilot", sx, sy, sz)); // unchanged
        }
    }

    [Fact]
    public void EvaMine_TakesOreFromAVoxelAsteroid()
    {
        // item 20 S3: on an EVA you can mine ore blocks off an asteroid (the other half of the hybrid).
        var server = NewServer("astmine", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            pilot.State.InEva = true;

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            int before = server.StructureBlockCountForTest(ast.Id);

            // Mine the ore core (0,0,0 is solid in the generated rock).
            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = ast.Id, X = 0, Y = 0, Z = 0, Mine = true });

            Assert.True(server.StructureBlockCountForTest(ast.Id) < before, "mining removes an ore block");
            Assert.True(pilot.State.Inventory.CountOf("titanium_ore") >= 1, "mining the core yields titanium ore");
        }
    }

    [Fact]
    public void PlayerStation_Deploys_BuildsOut_AndCommissions()
    {
        // item 20 S4: deploy a station core, build a hull + airlock around it → it commissions (boardable + map).
        var server = NewServer("pstation", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            pilot.State.InEva = true;
            pilot.State.InstantBuild = true; // free build for the test

            server.DeployStationCoreForTest("Pilot");
            var id = server.OwnedStationIdForTest("Pilot");
            Assert.NotNull(id);
            Assert.False(server.StationIsBoardableForTest(id!)); // just a core so far

            // Build a small hull (the core + 11 walls = 12 blocks).
            for (int i = 1; i <= 11; i++)
            {
                server.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = id!, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
            }

            Assert.False(server.StationIsBoardableForTest(id!)); // size met, but no airlock yet

            // Add an airlock door → commissioned.
            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = id!, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });
            Assert.True(server.StationIsBoardableForTest(id!));
        }
    }

    [Fact]
    public void PlayerStation_Persists_AcrossServerRestart()
    {
        // item 20 S4: a commissioned player station survives a server restart (same save).
        string id;
        {
            var s1 = NewServer("pstation_persist", r => r.FreeSpaceFlight = true, out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Pilot");
                s1.EnterSpace("Pilot");
                pilot.State.InEva = true;
                pilot.State.InstantBuild = true;

                s1.DeployStationCoreForTest("Pilot");
                id = s1.OwnedStationIdForTest("Pilot")!;
                for (int i = 1; i <= 11; i++)
                {
                    s1.HandleStructureEditForTest("Pilot",
                        new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
                }

                s1.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });
                Assert.True(s1.StationIsBoardableForTest(id));
                repo1.Flush();
            }
        }

        // Reopen the same world in a fresh server — the station is restored as a boardable body.
        var s2 = NewServer("pstation_persist", r => r.FreeSpaceFlight = true, out var repo2);
        using (repo2)
        {
            Assert.True(s2.StationIsBoardableForTest(id));
        }
    }

    [Fact]
    public void ShipHullEdits_Persist_AcrossServerRestart()
    {
        // item 20 S4 durable save: a player's own EVA hull edits (a mined-out cell + a built cell) survive a
        // server restart and re-entry into space (per-cell deltas re-applied on the rebuilt ship baseline).
        int sx = -1, sy = -1, sz = -1;     // a hull cell we mine out
        int ex = -1, ey = -1, ez = -1;     // an empty cell we build into
        {
            var s1 = NewServer("shipedit_persist", r => r.FreeSpaceFlight = true, out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Pilot");
                s1.EnterSpace("Pilot");
                pilot.State.InEva = true;
                pilot.State.InstantBuild = true;

                // Find a solid hull cell to mine and an adjacent air cell to build into.
                for (int x = 0; x < 24 && sx < 0; x++)
                for (int y = 0; y < 24 && sx < 0; y++)
                for (int z = 0; z < 24 && sx < 0; z++)
                {
                    if (s1.StructureBlockForTest("Pilot", x, y, z) != 0) { sx = x; sy = y; sz = z; }
                }

                Assert.True(sx >= 0, "the ship structure should have at least one solid cell");

                // Mine the hull cell → air.
                s1.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = "ship:Pilot", X = sx, Y = sy, Z = sz, Mine = true });
                Assert.Equal(0, s1.StructureBlockForTest("Pilot", sx, sy, sz));

                // Build iron_wall into the now-empty cell (the same cell, proving a place-delta persists too).
                ex = sx; ey = sy; ez = sz;
                s1.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = "ship:Pilot", X = ex, Y = ey, Z = ez, Mine = false, ItemKey = "iron_wall" });
                Assert.NotEqual(0, s1.StructureBlockForTest("Pilot", ex, ey, ez));

                // Now mine it out again so the persisted end-state is AIR (a removed hull cell), the harder case.
                s1.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = "ship:Pilot", X = sx, Y = sy, Z = sz, Mine = true });
                Assert.Equal(0, s1.StructureBlockForTest("Pilot", sx, sy, sz));
                repo1.Flush();
            }
        }

        // Reopen the same world in a fresh server, re-enter space → the rebuilt ship has the mined-out cell.
        var s2 = NewServer("shipedit_persist", r => r.FreeSpaceFlight = true, out var repo2);
        using (repo2)
        {
            s2.AddLocalPlayer("Pilot");
            s2.EnterSpace("Pilot");
            Assert.Equal(0, s2.StructureBlockForTest("Pilot", sx, sy, sz)); // mined-out hull cell stayed gone
        }
    }

    [Fact]
    public void ShipBuiltCell_Persists_AcrossServerRestart()
    {
        // Companion to the mine case: a cell the player BUILT onto the hull (where the baseline was air) is
        // still solid after a restart + re-entry.
        int bx = -1, by = -1, bz = -1;
        {
            var s1 = NewServer("shipbuilt_persist", r => r.FreeSpaceFlight = true, out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Pilot");
                s1.EnterSpace("Pilot");
                pilot.State.InEva = true;
                pilot.State.InstantBuild = true;

                // Find an air cell adjacent to a solid hull cell (a buildable spot just outside the hull).
                for (int x = 0; x < 24 && bx < 0; x++)
                for (int y = 0; y < 24 && bx < 0; y++)
                for (int z = 0; z < 24 && bx < 0; z++)
                {
                    if (s1.StructureBlockForTest("Pilot", x, y, z) != 0
                        && s1.StructureBlockForTest("Pilot", x, y + 1, z) == 0)
                    {
                        bx = x; by = y + 1; bz = z;
                    }
                }

                Assert.True(bx >= 0, "expected an empty cell above a hull cell");
                s1.HandleStructureEditForTest("Pilot",
                    new StructureEditIntent { StructureId = "ship:Pilot", X = bx, Y = by, Z = bz, Mine = false, ItemKey = "iron_wall" });
                Assert.NotEqual(0, s1.StructureBlockForTest("Pilot", bx, by, bz));
                repo1.Flush();
            }
        }

        var s2 = NewServer("shipbuilt_persist", r => r.FreeSpaceFlight = true, out var repo2);
        using (repo2)
        {
            s2.AddLocalPlayer("Pilot");
            s2.EnterSpace("Pilot");
            Assert.NotEqual(0, s2.StructureBlockForTest("Pilot", bx, by, bz)); // built cell survived the restart
        }
    }

    [Fact]
    public void StructureEdit_RejectedWhenTooFarFromAsteroid()
    {
        // item 20 S5: a static structure can only be edited from close range (anti-grief).
        var server = NewServer("reach", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            pilot.State.InEva = true;

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            int before = server.StructureBlockCountForTest(ast.Id);

            server.ShipMove("Pilot", 300f, 0f, 0f); // float far away from the field
            server.HandleStructureEditForTest("Pilot",
                new StructureEditIntent { StructureId = ast.Id, X = 0, Y = 0, Z = 0, Mine = true });

            Assert.Equal(before, server.StructureBlockCountForTest(ast.Id)); // out of range → unchanged
        }
    }

    [Fact]
    public void StructureEdit_CannotEditAnotherPlayersShip()
    {
        // item 20 S5: you can't mine/build on another player's ship.
        var server = NewServer("protect", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            server.EnterSpace("Alice");
            server.EnterSpace("Bob");
            bob.State.InEva = true;

            int sx = -1, sy = -1, sz = -1;
            for (int x = 0; x < 24 && sx < 0; x++)
            for (int y = 0; y < 24 && sx < 0; y++)
            for (int z = 0; z < 24 && sx < 0; z++)
            {
                if (server.StructureBlockForTest("Alice", x, y, z) != 0) { sx = x; sy = y; sz = z; }
            }

            Assert.True(sx >= 0);
            server.HandleStructureEditForTest("Bob",
                new StructureEditIntent { StructureId = "ship:Alice", X = sx, Y = sy, Z = sz, Mine = true });

            Assert.NotEqual(0, server.StructureBlockForTest("Alice", sx, sy, sz)); // Alice's hull is protected
        }
    }

    // ---------------- Weapons + rule gating ----------------

    [Fact]
    public void AsteroidBreaker_BreaksAsteroidsDown_AndEventuallyYieldsLoot()
    {
        var server = NewServer("breaker", r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Remove("tractor_beam"); // test the direct-to-inventory loot path (no tractor float)
            server.EnterSpace("Pilot");

            // Keep breaking the nearest asteroid: large -> medium -> small -> mineral drops.
            for (int i = 0; i < 16 && pilot.State.Inventory.CountOf("iron_ore") == 0; i++)
            {
                var a = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
                if (a is null) break;
                server.FireWeapon("Pilot", "asteroid_breaker", a.Id);
            }

            Assert.True(pilot.State.Inventory.CountOf("iron_ore") >= 5, "Breaking asteroids down should eventually drop ore.");
        }
    }

    [Fact]
    public void Shoot_CarvesVoxelAsteroid_ThenDestroysIt()
    {
        // item 20 S3: voxel ore asteroids carve down as you shoot them (no splitting), then yield loot + vanish.
        var server = NewServer("carve", r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Remove("tractor_beam"); // direct-to-inventory loot
            server.EnterSpace("Pilot");

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            int blocksBefore = server.StructureBlockCountForTest(ast.Id);
            Assert.True(blocksBefore > 0, "a voxel asteroid should start with ore blocks");

            // First hit carves the rock down (fewer blocks) but doesn't destroy it.
            server.FireWeapon("Pilot", "asteroid_breaker", ast.Id);
            int blocksAfter = server.StructureBlockCountForTest(ast.Id);
            Assert.True(blocksAfter < blocksBefore, "shooting should carve voxel blocks off the asteroid");

            // Keep firing until it's destroyed → its structure is gone and ore was banked.
            for (int i = 0; i < 16 && server.SpaceEntitiesFor("Pilot").Any(e => e.Id == ast.Id); i++)
            {
                server.FireWeapon("Pilot", "asteroid_breaker", ast.Id);
            }

            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Id == ast.Id); // entity gone
            Assert.Equal(0, server.StructureBlockCountForTest(ast.Id));                   // structure gone
            Assert.True(pilot.State.Inventory.CountOf("iron_ore") >= 5, "a destroyed asteroid yields ore");
        }
    }

    [Fact]
    public void CombatWeapon_RejectedWhenShipWeaponsOff()
    {
        var server = NewServer("weapons_off", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone
            r.ShipWeapons = ShipWeaponMode.Off;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("ship_cannon_1");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id);

            Assert.Contains(server.SpaceEntitiesFor("Pilot"), e => e.Id == drone.Id);
            Assert.Equal(drone.HullMax, drone.Hull); // unharmed
        }
    }

    [Fact]
    public void ShipCannon_DestroysDrone_WhenWeaponsAllowed()
    {
        var server = NewServer("cannon", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone, hull 40
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("ship_cannon_1"); // 20 dmg
            server.Ship.Modules.Remove("tractor_beam"); // loot drops straight to inventory (no tractor float)
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z); // close to fire (range from ship)
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // 40 -> 20
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // destroyed

            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Id == drone.Id);
            Assert.True(pilot.State.Inventory.CountOf("data_fragment") >= 1);
        }
    }

    [Fact]
    public void StarterShip_HasADualLaser_ThatMinesAsteroids()
    {
        var server = NewServer("laser_mine", r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly; // a pure cannon couldn't, but the dual laser can
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.True(server.Ship.HasModule("ship_laser_basic")); // fitted on every starter ship
            server.EnterSpace("Pilot");

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            server.ShipMove("Pilot", ast.Position.X, ast.Position.Y, ast.Position.Z); // close enough to fire
            float before = ast.Hull;
            server.FireWeapon("Pilot", "ship_laser_basic", ast.Id);

            Assert.True(ast.Hull < before, "The dual laser should mine asteroids even in MiningOnly mode.");
        }
    }

    [Fact]
    public void StarterShip_DualLaser_AlsoDamagesHostiles()
    {
        var server = NewServer("laser_fight", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare;
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z);
            float before = drone.Hull;
            server.FireWeapon("Pilot", "ship_laser_basic", drone.Id);

            Assert.True(drone.Hull < before, "The dual laser should also fight hostiles.");
        }
    }

    [Fact]
    public void StarterKit_IncludesASimpleMeleeWeapon()
    {
        var server = NewServer("kit", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            Assert.True(pilot.State.Inventory.CountOf("machete") >= 1, "A new player should start with a melee weapon.");
        }
    }

    // ---------------- Ship defeat = no permanent loss ----------------

    [Fact]
    public void NpcDrones_DisableShip_AndRecoverWithoutPermanentLoss()
    {
        var server = NewServer("defeat", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Normal; // 2 drones, 5 dps each = 10/s
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));

            // Drones now spawn far from the launch point (safe launch); fly into their midst to be engaged.
            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z);

            // The starter now launches with shields up: 100 hull + 25 design shield + 30 baseline shield = 155
            // effective HP. At 10 dps that's ~16s; tick well past it so the ship is reliably disabled.
            server.Tick(30.0);

            Assert.False(server.InSpace("Pilot"));        // forced out of space
            Assert.Equal(100f, server.Ship.Hull);          // hull restored (recovered to base)
            Assert.Equal(100f, pilot.State.Health);         // respawned at heal-tank
        }
    }

    // ---------------- Ship module build flow (over the wire) ----------------

    [Fact]
    public void BuildShipModule_ConsumesMaterials_AndRaisesHull()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "build"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "build", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Builder" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var state = server.Sessions[1].State;
        state.UnlockedBlueprints.Add("hull_plating");
        state.Inventory.Add("titanium_plate", 20, 99);
        state.Inventory.Add("iron_plate", 20, 99);
        state.Inventory.Add("steel", 6, 99); // Task 5 Stage 4: hull plating now also needs steel

        client.Send(NetCodec.Encode(new BuildShipModuleIntent { ModuleKey = "hull_plating" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.True(server.Ship.HasModule("hull_plating"));
        Assert.Equal(0, state.Inventory.CountOf("titanium_plate"));
        Assert.Equal(0, state.Inventory.CountOf("iron_plate"));
    }

    [Fact]
    public void BuildShipModule_RadarArray_WidensRadarRange()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "radar"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "radar", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Builder" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        float before = server.ShipRadarRange;

        var state = server.Sessions[1].State;
        state.UnlockedBlueprints.Add("radar_array");
        state.Inventory.Add("titanium_plate", 10, 99);
        state.Inventory.Add("cable", 10, 99);
        state.Inventory.Add("glass", 4, 99);
        state.Inventory.Add("energy_cell_1", 3, 99);
        state.Inventory.Add("magnet", 2, 99); // Task 5: radar_array now also needs magnets

        client.Send(NetCodec.Encode(new BuildShipModuleIntent { ModuleKey = "radar_array" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.True(server.Ship.HasModule("radar_array"));
        Assert.Equal(before + 170f, server.ShipRadarRange); // radar_bonus stat
    }

    // ---------------- Collision ----------------

    [Fact]
    public void FlyingIntoAsteroid_DamagesShip()
    {
        var server = NewServer("collide", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            float before = server.Ship.Hull + server.Ship.Shield; // a ram is absorbed by the shield first

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            server.ShipMove("Pilot", ast.Position.X, ast.Position.Y, ast.Position.Z); // fly straight into it
            server.Tick(0.1);

            Assert.True(server.Ship.Hull + server.Ship.Shield < before, "Colliding with an asteroid should damage the ship.");
        }
    }

    [Fact]
    public void DriftingInOpenSpace_DoesNotDamageShip()
    {
        var server = NewServer("nocollide", r => r.FreeSpaceFlight = true, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            float before = server.Ship.Hull;

            server.Tick(0.1); // ship sits at the origin; asteroids are far away
            Assert.Equal(before, server.Ship.Hull);
        }
    }

    [Fact]
    public void DistantHostile_DoesNotDamageShip_UntilWithinRange()
    {
        var server = NewServer("engage", r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);

            // Parked far away, the drone can't touch the ship (no relentless off-screen plinking). Incoming fire
            // is absorbed by the shield first, so compare total effective HP (shield + hull).
            drone.Position = new Vector3f(500f, 0f, 0f);
            float before = server.Ship.Hull + server.Ship.Shield;
            server.Tick(3.0);
            Assert.Equal(before, server.Ship.Hull + server.Ship.Shield);

            // Brought within engagement range, it deals damage as usual.
            drone.Position = new Vector3f(12f, 0f, 0f);
            server.Tick(2.0);
            Assert.True(server.Ship.Hull + server.Ship.Shield < before, "A hostile within engagement range should damage the ship.");
        }
    }

    // ---------------- Tractor beam (salvage) ----------------

    [Fact]
    public void TractorBeam_PullsSalvageDrops_IntoCargo()
    {
        var server = NewServer("tractor", r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        }, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Add("tractor_beam"); // with a tractor, the smallest chunks float as salvage
            server.EnterSpace("Pilot");

            // Break asteroids down until a floating salvage drop appears.
            CombatEntity? drop = null;
            for (int i = 0; i < 24 && drop == null; i++)
            {
                var a = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
                if (a != null)
                {
                    server.FireWeapon("Pilot", "asteroid_breaker", a.Id);
                }

                drop = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.ResourceDrop);
            }

            Assert.NotNull(drop); // breaking the smallest asteroid left a salvage drop (not instant loot)
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore")); // not collected yet

            // Fly onto the drop; the tractor pulls it into the cargo hold.
            server.ShipMove("Pilot", drop!.Position.X, drop.Position.Y, drop.Position.Z);
            server.Tick(0.1);

            Assert.True(server.Ship.Cargo.CountOf("iron_ore") >= 5, "Tractor should stow the salvage in cargo.");
            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Id == drop.Id); // drop consumed
        }
    }

    // ---------------- Planet enemies ----------------

    [Fact]
    public void PlanetEnemies_DoNotSpawnInCreative()
    {
        var server = NewServer("creative", r =>
        {
            r.GameMode = GameMode.Creative;
            r.PlanetEnemies = AlienActivity.Normal;
        }, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Walker");
            p.State.AboardShip = false;
            server.Tick(10.0);
            Assert.Empty(server.PlanetEnemies);
        }
    }

    [Fact]
    public void PlanetEnemies_SpawnAndDamageNearbyPlayer()
    {
        var server = NewServer("planet", r =>
        {
            r.GameMode = GameMode.Survival;
            r.PlanetEnemies = AlienActivity.Normal;
        }, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Walker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0); // spawn timer elapses -> 1 enemy near the player
            Assert.NotEmpty(server.PlanetEnemies);

            // Enemies now spawn a short distance away on the surface (not on top of the player); step up to
            // it so it is within proximity range and deals damage.
            var enemy = server.PlanetEnemies[0];
            p.State.Position = new Vector3f(enemy.Position.X, enemy.Position.Y, enemy.Position.Z);

            float before = p.State.Health;
            server.Tick(3.0); // now within proximity range -> damage
            Assert.True(p.State.Health < before);
        }
    }

    [Fact]
    public void AttackEntity_KillsPlanetEnemy_AndYieldsLoot()
    {
        var server = NewServer("attack", r =>
        {
            r.GameMode = GameMode.Survival;
            r.PlanetEnemies = AlienActivity.Normal;
        }, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Walker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var enemy = server.PlanetEnemies.First(); // Creature, hull 30
            var id = enemy.Id;

            // Enemies spawn a short distance away now; close in so it is within attack reach.
            p.State.Position = new Vector3f(enemy.Position.X, enemy.Position.Y, enemy.Position.Z);

            // Hand attack deals 15/hit -> a few hits to kill a 30-hull creature.
            for (int i = 0; i < 3 && server.PlanetEnemies.Any(e => e.Id == id); i++)
            {
                server.AttackEntity("Walker", id);
            }

            Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == id);
            Assert.True(p.State.Inventory.CountOf("carbon") >= 2);
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

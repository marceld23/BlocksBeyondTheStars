using Spacecraft.GameServer;
using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Free space flight, ship weapons, NPC combat and planet enemies (M19 / `anf_space_flight.md` §6-12).</summary>
public sealed class SpaceCombatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceCombatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_space_" + Guid.NewGuid().ToString("N"));
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
    public void LargeAsteroid_SplitsIntoSmallerChunks_WithoutLoot()
    {
        var server = NewServer("split", r =>
        {
            r.FreeSpaceFlight = true;
            r.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        }, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("asteroid_breaker");
            server.EnterSpace("Pilot");

            var large = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid && e.AsteroidTier == 2);
            int asteroidsBefore = server.SpaceEntitiesFor("Pilot").Count(e => e.Kind == CombatEntityKind.Asteroid);

            // asteroid_breaker = 25 dmg; a large asteroid has 40 hull → two hits.
            server.FireWeapon("Pilot", "asteroid_breaker", large.Id);
            server.FireWeapon("Pilot", "asteroid_breaker", large.Id);

            var asteroids = server.SpaceEntitiesFor("Pilot").Where(e => e.Kind == CombatEntityKind.Asteroid).ToList();
            Assert.DoesNotContain(asteroids, e => e.Id == large.Id);                 // the large one is gone
            Assert.True(asteroids.Count > asteroidsBefore - 1, "Destroying a large asteroid should spawn smaller chunks.");
            Assert.Contains(asteroids, e => e.AsteroidTier == 1);                    // medium chunks appeared
            Assert.Equal(0, pilot.State.Inventory.CountOf("iron_ore"));              // no loot from a large one
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
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Drone);
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // 40 -> 20
            server.FireWeapon("Pilot", "ship_cannon_1", drone.Id); // destroyed

            Assert.DoesNotContain(server.SpaceEntitiesFor("Pilot"), e => e.Id == drone.Id);
            Assert.True(pilot.State.Inventory.CountOf("data_fragment") >= 1);
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

            server.Tick(12.0); // 120 damage > 100 hull (no shield)

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
            float before = server.Ship.Hull;

            var ast = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.Asteroid);
            server.ShipMove("Pilot", ast.Position.X, ast.Position.Y, ast.Position.Z); // fly straight into it
            server.Tick(0.1);

            Assert.True(server.Ship.Hull < before, "Colliding with an asteroid should damage the ship.");
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

// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
/// Server-side aim validation (#693): the client now reports the shot's aim direction, and the
/// server checks the claimed target against it — a forward cone with the AutoAim world rule ON,
/// a genuine crosshair/boresight line with it OFF, plus line-of-sight for ranged shots. A zero
/// direction (older client) keeps the legacy range-only behaviour. Also covers the ship-weapon
/// cooldown that #694 made authoritative.
/// </summary>
public sealed class AimValidationTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AimValidationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_aim_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, Action<GameRules>? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "aim"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "aim",
            Seed = 777,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Player at y 300 (open sky — nothing ground-snaps while no ticks run) with a machine
    /// 10 blocks along +X. The laser pistol is ranged + energy-gated with no swing cooldown, so
    /// repeat shots need no time advancement.</summary>
    private static void Arrange(SvGameServer server, out string enemyId)
    {
        var p = server.AddLocalPlayer("Gunner");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0f, 300f, 0f);
        p.State.SuitEnergy = 100f;
        p.State.Inventory.SetSlot(0, new ItemStack("laser_pistol", 1));
        p.State.SelectedHotbarSlot = 0;
        server.SpawnPlanetEnemyAtForTest(new Vector3f(10f, 300f, 0f));
        enemyId = server.PlanetEnemies[^1].Id;
    }

    [Fact]
    public void LegacyShot_WithoutAimDirection_StillHits()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Arrange(server, out var enemyId);
            var enemy = server.PlanetEnemies.First(e => e.Id == enemyId);
            server.AttackEntity("Gunner", enemyId); // zero direction = pre-#693 client
            Assert.True(enemy.Hull < enemy.HullMax, "a legacy client's shot must keep working");
        }
    }

    [Fact]
    public void AutoAim_RejectsATargetBehindTheBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Arrange(server, out var enemyId);
            var enemy = server.PlanetEnemies.First(e => e.Id == enemyId);

            server.AttackEntity("Gunner", enemyId, dirX: -1f, dirY: 0f, dirZ: 0f); // looking AWAY
            Assert.Equal(enemy.HullMax, enemy.Hull);

            server.AttackEntity("Gunner", enemyId, dirX: 1f, dirY: 0f, dirZ: 0f); // looking at it
            Assert.True(enemy.Hull < enemy.HullMax);
        }
    }

    [Fact]
    public void ManualAim_OnlyTheCrosshairLineHits()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.SetAutoAimForTest(false);
            Arrange(server, out var enemyId);
            var enemy = server.PlanetEnemies.First(e => e.Id == enemyId);

            // 45° off the target: inside the old auto-aim cone, but not a crosshair hit — rejected.
            server.AttackEntity("Gunner", enemyId, dirX: 0.707f, dirY: 0f, dirZ: 0.707f);
            Assert.Equal(enemy.HullMax, enemy.Hull);

            // Dead on: the ray passes through the body — the shot lands.
            server.AttackEntity("Gunner", enemyId, dirX: 1f, dirY: 0f, dirZ: 0f);
            Assert.True(enemy.Hull < enemy.HullMax);
        }
    }

    [Fact]
    public void RangedShot_WithAimData_IsBlockedByAWall()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Arrange(server, out var enemyId);
            var enemy = server.PlanetEnemies.First(e => e.Id == enemyId);

            // A pillar squarely on the eye-line (sight runs at ~y 301.5 between the two).
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(5, 300, 0), stone);
            server.World.SetBlock(new Vector3i(5, 301, 0), stone);
            server.World.SetBlock(new Vector3i(5, 302, 0), stone);

            server.AttackEntity("Gunner", enemyId, dirX: 1f, dirY: 0f, dirZ: 0f);
            Assert.Equal(enemy.HullMax, enemy.Hull); // no shooting through walls

            // Clear the wall — the same shot lands.
            server.World.SetBlock(new Vector3i(5, 300, 0), BlockId.Air);
            server.World.SetBlock(new Vector3i(5, 301, 0), BlockId.Air);
            server.World.SetBlock(new Vector3i(5, 302, 0), BlockId.Air);
            server.AttackEntity("Gunner", enemyId, dirX: 1f, dirY: 0f, dirZ: 0f);
            Assert.True(enemy.Hull < enemy.HullMax);
        }
    }

    [Fact]
    public void ShipWeapon_HonoursFiringArc_WithAimData()
    {
        var server = Started(out var repo, r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        });
        using (repo)
        {
            server.AddLocalPlayer("Gunner");
            server.Ship.Modules.Add("ship_cannon_1");
            server.EnterSpace("Gunner");

            var drone = server.SpaceEntitiesFor("Gunner").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Gunner", drone.Position.X, drone.Position.Y, drone.Position.Z - 10f);

            // Nose pointing away from the drone: outside the arc — rejected, and the cooldown is NOT eaten.
            server.FireWeapon("Gunner", "ship_cannon_1", drone.Id, dirX: 0f, dirY: 0f, dirZ: -1f);
            Assert.Equal(drone.HullMax, drone.Hull);

            // Nose on the drone: the shot lands.
            server.FireWeapon("Gunner", "ship_cannon_1", drone.Id, dirX: 0f, dirY: 0f, dirZ: 1f);
            Assert.True(drone.Hull < drone.HullMax);
        }
    }

    [Fact]
    public void ShipWeapon_CooldownIsServerEnforced()
    {
        var server = Started(out var repo, r =>
        {
            r.FreeSpaceFlight = true;
            r.SpaceCombat = SpaceCombatMode.PvE;
            r.SpaceNpcEnemies = AlienActivity.Rare; // 1 drone, hull 40
            r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        });
        using (repo)
        {
            server.AddLocalPlayer("Gunner");
            server.Ship.Modules.Add("ship_cannon_1"); // 20 dmg, 1.0 s cooldown
            server.EnterSpace("Gunner");

            var drone = server.SpaceEntitiesFor("Gunner").First(e => e.Kind == CombatEntityKind.Drone);
            server.ShipMove("Gunner", drone.Position.X, drone.Position.Y, drone.Position.Z);

            server.FireWeapon("Gunner", "ship_cannon_1", drone.Id); // 40 -> 20
            server.FireWeapon("Gunner", "ship_cannon_1", drone.Id); // still cycling — swallowed (#694)
            Assert.Equal(drone.HullMax - 20f, drone.Hull);

            server.TickForTest(1.1); // cooldown cycles
            server.FireWeapon("Gunner", "ship_cannon_1", drone.Id); // 20 -> 0: destroyed
            Assert.DoesNotContain(server.SpaceEntitiesFor("Gunner"), e => e.Id == drone.Id);
        }
    }
}

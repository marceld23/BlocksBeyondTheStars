// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Enemies MOVE now (they used to stand rooted at their spawn points forever): planet fiends hunt a
/// player in detection range and wander otherwise; space hostiles (drones/UFOs) patrol around their
/// post and chase the ship when it comes close.
/// </summary>
public sealed class EnemyMovementTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public EnemyMovementTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_enemy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo, bool drones = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.PvE; // hostile NPCs only spawn with combat enabled
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        // Wildlife off: creatures share the entity-id counter with the machines, and a machine's id seeds its
        // locomotion (the scan-drone's strafe phase in particular) — so any change to the creature spawner
        // shifted which id the first fiend got and flipped the hunt assertion below (#1325 did exactly that).
        config.Rules.CreatureAbundance = AlienActivity.Off;
        config.Rules.PlanetDrones = drones; // the wall tests (#1482) want walking robots only
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void PlanetEnemies_HuntTheNearbyPlayer()
    {
        var server = Started("hunt", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false; // on foot on the surface — a valid enemy target

            // Tick until the first fiend spawns (the spawner is interval-driven).
            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];

            // Fiends spawn well OUTSIDE their 28-block detection range now (they roam until approached),
            // so walk the prey into range first — the hunt behaviour is what's under test here.
            pilot.State.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(
                enemy.Position.X + 10f, enemy.Position.Y, enemy.Position.Z);

            var start = enemy.Position;
            double d0 = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - start.X, 2) + Math.Pow(pilot.State.Position.Z - start.Z, 2));

            for (int i = 0; i < 10; i++)
            {
                server.Tick(0.2); // 2 s of hunting
            }

            var now = enemy.Position;
            Assert.False(now.Equals(start), "a fiend with a player in detection range must move");

            double d1 = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - now.X, 2) + Math.Pow(pilot.State.Position.Z - now.Z, 2));
            Assert.True(d1 < d0, $"the fiend should close in on the player (was {d0:F1}, now {d1:F1})");
        }
    }

    [Fact]
    public void PlanetEnemies_SpawnWellOutsideDetectionRange()
    {
        var server = Started("farspawn", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false;

            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];
            double d = Math.Sqrt(
                Math.Pow(pilot.State.Position.X - enemy.Position.X, 2)
                + Math.Pow(pilot.State.Position.Z - enemy.Position.Z, 2));
            Assert.True(d >= 30, $"fiends must spawn outside detection range (28), not ambush-close (was {d:F1})");
        }
    }

    [Fact]
    public void PlanetEnemies_IncludeWalkingRobots_NotOnlyDrones()
    {
        // Regression (#398): at the default Normal activity a single player's cap is 2, and the old drone-mix
        // keyed off the raw spawn count (`count % 5 < 2`) while the guard only spawns below the cap — so BOTH
        // slots filled as flying scan-drones and the walking three-eyed ground robots were never reached.
        // The live population must contain at least one walking robot (a non-drone kind).
        var server = Started("robotmix", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            pilot.State.AboardShip = false; // on foot on the surface — a valid enemy target

            // Fill to the cap (Normal + solo = 2). The spawner is interval-driven (5 s), so tick generously.
            for (int i = 0; i < 200 && server.PlanetEnemies.Count < 2; i++)
            {
                server.Tick(0.5);
            }

            Assert.Equal(2, server.PlanetEnemies.Count);
            Assert.Contains(server.PlanetEnemies, e => e.Kind != CombatEntityKind.ScanDrone);
        }
    }

    [Fact]
    public void PlanetEnemies_IgnoreAPlayerWhoFledAboardTheShip()
    {
        // Regression: fleeing into the ship must break off the hunt entirely — the machine neither chases
        // nor harms a boarded player (the client mirrors this by holding its laser/claw FX). Boarded players
        // are filtered out of the enemy target list, so even point-blank they take no damage.
        var server = Started("aboardsafe", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Refugee");
            pilot.State.AboardShip = false; // spawn on foot so an enemy actually spawns and turns hostile

            for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.PlanetEnemies);
            var enemy = server.PlanetEnemies[0];

            // Park the enemy on top of the player, then board: well inside both hunt and proximity range.
            pilot.State.Position = enemy.Position;
            pilot.State.AboardShip = true;
            pilot.State.Health = 100f;

            var start = enemy.Position;
            for (int i = 0; i < 15; i++)
            {
                server.Tick(0.2); // 3 s sitting on top of a boarded player
            }

            Assert.Equal(100f, pilot.State.Health); // no proximity damage while aboard
            Assert.True(enemy.Position.Equals(start), "a boarded player is no target — the machine must not chase them");
        }
    }

    [Fact]
    public void SpaceHostiles_PatrolInsteadOfHangingStill()
    {
        var server = Started("patrol", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            var drone = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.Drone);
            Assert.NotNull(drone); // SpaceNpcEnemies = Normal spawns drones
            var start = drone!.Position;

            for (int i = 0; i < 20; i++)
            {
                server.Tick(0.2); // 4 s — far from the ship, so this is the patrol orbit
            }

            Assert.False(drone.Position.Equals(start), "a space hostile must patrol around its post, not hang still");
        }
    }

    // ---------------- #1482: walls stop the walking robots ----------------

    /// <summary>The top Y of the first non-air block under <paramref name="from"/> in a column.</summary>
    private static int SurfaceYAt(SvGameServer server, int x, int z, int from)
    {
        for (int y = from; y > from - 96; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return from - 96;
    }

    /// <summary>Flattens a strip west of the player into an arena on real blocks (an iron floor at
    /// <c>ground</c>, nine cells of air above), builds a <paramref name="wallHeight"/>-block wall three cells
    /// west of the player across the strip, stands the player ON the wall and the first walking robot ten
    /// cells west of it, facing the wall with a clear line of sight to the player. Returns (wall X, ground Y).</summary>
    private (int WallX, int Ground) WallArena(SvGameServer server, PlayerSession pilot, CombatEntity robot, int wallHeight)
    {
        var iron = _content.GetBlock("iron_wall")!.NumericId;
        int px = (int)Math.Floor(pilot.State.Position.X);
        int pz = (int)Math.Floor(pilot.State.Position.Z);
        int ground = SurfaceYAt(server, px, pz, (int)Math.Floor(pilot.State.Position.Y) + 8);
        int wallX = px - 3;
        for (int x = px - 13; x <= px + 2; x++)
            for (int z = pz - 3; z <= pz + 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, ground, z), iron);
                for (int y = ground + 1; y <= ground + 9; y++)
                {
                    bool wall = x == wallX && y <= ground + wallHeight;
                    server.World.SetBlock(new Vector3i(x, y, z), wall ? iron : BlockId.Air);
                }
            }

        robot.Position = new Vector3f(px - 10 + 0.5f, ground + 1, pz + 0.5f);
        pilot.State.Position = new Vector3f(wallX + 0.5f, ground + wallHeight + 1, pz + 0.5f);
        return (wallX, ground);
    }

    private CombatEntity FirstWalkingRobot(SvGameServer server, PlayerSession pilot)
    {
        pilot.State.AboardShip = false; // on foot on the surface — a valid enemy target
        for (int i = 0; i < 40 && server.PlanetEnemies.Count == 0; i++)
        {
            server.Tick(0.5);
        }

        var robot = server.PlanetEnemies.First(e => e.Kind != CombatEntityKind.ScanDrone);
        return robot;
    }

    [Theory]
    [InlineData(3)] // used to be lifted onto the parapet in ONE tick (ground delta ≤ 3 passed as a step)
    [InlineData(7)] // used to walk straight THROUGH (nothing standable within ±6 → noise-surface fallback)
    public void WalkingRobot_IsStoppedByAWall(int wallHeight)
    {
        var server = Started("wall" + wallHeight, out var repo, drones: false);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            var robot = FirstWalkingRobot(server, pilot);
            var (wallX, ground) = WallArena(server, pilot, robot, wallHeight);

            float startX = robot.Position.X;
            for (int i = 0; i < 150; i++) // 15 s of hunting toward the player on the wall
            {
                server.Tick(0.1);
                Assert.True(robot.Position.X < wallX, $"the robot must never pass the wall plane (x {robot.Position.X:F1} ≥ wall {wallX}) after {i} ticks");
                Assert.True(robot.Position.Y < ground + 1 + 2, $"the robot must never climb the wall (y {robot.Position.Y:F1}, ground {ground}) after {i} ticks");
            }

            Assert.True(robot.Position.X > startX, "the robot should still have closed in on the wall (it hunts, it is just stopped)");
        }
    }

    [Fact]
    public void WalkingRobot_StillStepsUpASingleBlock()
    {
        var server = Started("step1", out var repo, drones: false);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Prey");
            var robot = FirstWalkingRobot(server, pilot);
            var (wallX, ground) = WallArena(server, pilot, robot, 1);
            // Prey on the floor five cells BEYOND the step (on the step itself the robot would hold at its
            // 1.6-block biting range before ever crossing the column).
            pilot.State.Position = new Vector3f(wallX + 5.5f, ground + 1, pilot.State.Position.Z);

            bool crossed = false;
            for (int i = 0; i < 150 && !crossed; i++)
            {
                server.Tick(0.1);
                crossed = robot.Position.X >= wallX;
            }

            Assert.True(crossed, "a one-block step is a step, not a wall");
        }
    }
}
